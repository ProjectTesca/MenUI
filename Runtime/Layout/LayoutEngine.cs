// Block and flexbox layout in whole physical pixels.
//
// Lengths arrive already snapped (see ComputedStyle.Apply), so every size
// here is an int. Where space is divided — flex-grow, flex-shrink, the gaps
// of justify-content — edges are rounded cumulatively, so the parts always
// add up to exactly the whole (three `flex-grow: 1` items in 100px: 33/34/33).
//
// Simplifications against CSS, for now:
//   - `min-width/height: auto` is 0 (flex items may shrink below their content).
//   - flexible lengths are resolved in one pass (a clamped item does not hand
//     its excess back to the others).
//   - absolute elements are placed against their parent's padding box.
//   - auto margins count as 0; no baseline alignment; no wrap-reverse.
//   - box-sizing is border-box everywhere.

using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    internal sealed class LayoutEngine
    {
        private const int Unbounded = int.MaxValue;
        private int pass;
        /// The pass number the next Run or measurement will use.
        internal int NextPass { get { return pass + 1; } }
        private int depth;
        private readonly List<LayoutScratch> scratchByDepth = new List<LayoutScratch>();
        private TransformGroups groups;
        /// Elements actually (re)arranged by the last Run — for diagnostics and tests.
        internal int arrangedCount;

        // Run's working lists: invalidated elements still to place (with their
        // depths), and the elements to lay out again in place.
        private readonly List<Element> pending = new List<Element>();
        private readonly List<int> pendingDepths = new List<int>();
        private readonly List<Element> relayoutRoots = new List<Element>();
        private readonly List<Element> postponed = new List<Element>();

        /// Lays out again what was invalidated (see Element.MarkLayoutDirty).
        ///
        /// First, how far each change reaches, deepest first: an invalidated
        /// element that is a relayout boundary, or that still gives its parent
        /// the same answers as before (early cutoff), is laid out again in the
        /// rectangle it already has; otherwise its parent is invalidated too
        /// and asked the same question. Deepest first matters: when an element
        /// is measured, every change below it has already climbed as far as it
        /// goes, so no clean ancestor on the way answers from a stale cache.
        ///
        /// Then those elements are arranged, shallowest first, in place.
        /// Unchanged subtrees below them are skipped.
        internal void Run(MenUIContext context)
        {
            groups = context.groups;
            pass++;
            arrangedCount = 0;
            List<Element> queue = context.layoutQueue;
            pending.Clear();
            pendingDepths.Clear();
            relayoutRoots.Clear();
            for (int i = 0; i < queue.Count; i++)
            {
                Element e = queue[i];
                int depthOf = 0;
                Element top = e;
                while (top.parent != null)
                {
                    top = top.parent;
                    depthOf++;
                }
                if (top != context.root && top != context.overlayRoot)
                {
                    e.queuedForLayout = false;   // detached; laid out when it is attached again
                    e.savedAnswerCount = -1;
                    continue;
                }
                if (e.InsidePausedLayout())
                {
                    postponed.Add(e);   // stays queued until its load lays it out
                    continue;
                }
                pending.Add(e);
                pendingDepths.Add(depthOf);
            }
            queue.Clear();
            queue.AddRange(postponed);
            postponed.Clear();

            while (pending.Count > 0)
            {
                int deepest = 0;
                for (int i = 1; i < pending.Count; i++)
                {
                    if (pendingDepths[i] > pendingDepths[deepest])
                    {
                        deepest = i;
                    }
                }
                Element e = pending[deepest];
                int depthOf = pendingDepths[deepest];
                int last = pending.Count - 1;
                pending[deepest] = pending[last];
                pendingDepths[deepest] = pendingDepths[last];
                pending.RemoveAt(last);
                pendingDepths.RemoveAt(last);
                e.queuedForLayout = false;

                Element parent = e.parent;
                if (parent == null || (e.hasArranged && e.IsLayoutBoundary()) || KeepsAnswers(e))
                {
                    relayoutRoots.Add(e);
                    continue;
                }
                // Its size may have changed: the parent must be laid out again.
                Invalidate(parent, pass);
                parent.TruncateStack(e);
                if (!parent.queuedForLayout)
                {
                    parent.queuedForLayout = true;
                    pending.Add(parent);
                    pendingDepths.Add(depthOf - 1);
                }
            }

            SortByDepth(relayoutRoots);
            for (int i = 0; i < relayoutRoots.Count; i++)
            {
                Element boundary = relayoutRoots[i];
                if (!boundary.layoutDirty)
                {
                    continue;   // already laid out as part of a shallower one
                }
                if (boundary.parent == null)
                {
                    if (boundary.computed.Display == DisplayMode.None)
                    {
                        continue;
                    }
                    int width = context.bufferWidth;
                    int height = context.bufferHeight;
                    Arrange(boundary, 0, 0, width, height, width, 0, 0, width, height);
                }
                else
                {
                    Arrange(boundary, boundary.x, boundary.y, boundary.width, boundary.height,
                        boundary.arrangedContainingWidth,
                        boundary.incomingClipLeft, boundary.incomingClipTop, boundary.incomingClipRight, boundary.incomingClipBottom);
                }
            }
            // Anything invalidated while arranging (a binding that follows a
            // scroll extent) stays queued for the next run.
        }

        /// Marks an element's layout invalid (its measurements are stale from
        /// `sincePass` on). The first time since it was last laid out, the
        /// answers its parent got are saved first, for KeepsAnswers.
        internal void Invalidate(Element e, int sincePass)
        {
            if (!e.layoutDirty)
            {
                SaveAnswers(e);
            }
            e.layoutDirty = true;
            e.validSincePass = sincePass;
            e.measureOverflowed = false;
        }

        private static void SaveAnswers(Element e)
        {
            e.savedAnswerCount = -1;
            e.savedMinContent = -1;
            e.savedContentHeightWidth = -1;
            if (!e.hasArranged || e.measureOverflowed || e.parent == null)
            {
                return;   // the cache may not hold every question the parent asked
            }
            if (e.savedAnswers == null)
            {
                e.savedAnswers = new int[Element.MeasureCacheSlots * 5];
            }
            int[] cache = e.measureCache;
            int[] saved = e.savedAnswers;
            int count = 0;
            for (int slot = 0; slot < Element.MeasureCacheSlots; slot++)
            {
                int o = slot * CacheEntry;
                if (cache[o + 5] == 0 || cache[o + 5] < e.validSincePass || cache[o + 6] == 0)
                {
                    continue;
                }
                int s = count * 5;
                saved[s] = cache[o];
                saved[s + 1] = cache[o + 1];
                saved[s + 2] = cache[o + 2];
                saved[s + 3] = cache[o + 3];
                saved[s + 4] = cache[o + 4];
                count++;
            }
            e.savedAnswerCount = count;
            if (e.minContentPass != 0 && e.minContentPass >= e.validSincePass)
            {
                e.savedMinContent = e.minContentWidth;
            }
            if (e.contentHeightPass != 0 && e.contentHeightPass >= e.validSincePass)
            {
                e.savedContentHeightWidth = e.contentHeightWidth;
                e.savedContentHeight = e.contentHeightAnswer;
            }
        }

        /// Early cutoff: whether an invalidated element still measures exactly
        /// as before for every question its parent asked (sizes, its
        /// min-content width, its content height). Then the parent's layout
        /// cannot change, and the element is laid out again in place.
        /// Measuring here fills the cache the arrange that follows reads.
        private bool KeepsAnswers(Element e)
        {
            if (KeepsEveryAnswer(e))
            {
                return true;
            }
            // The parent is laid out again and asks what it still needs: the
            // questions replayed here count only if it asks them (otherwise a
            // question it stopped asking would be replayed at every change).
            int[] cache = e.measureCache;
            for (int slot = 0; slot < Element.MeasureCacheSlots; slot++)
            {
                int o = slot * CacheEntry;
                if (cache[o + 5] == pass)
                {
                    cache[o + 6] = 0;
                }
            }
            return false;
        }

        private bool KeepsEveryAnswer(Element e)
        {
            int count = e.savedAnswerCount;
            e.savedAnswerCount = -1;   // used once
            if (count <= 0 || !e.hasArranged)
            {
                return false;
            }
            int[] saved = e.savedAnswers;
            for (int i = 0; i < count; i++)
            {
                int s = i * 5;
                int width, height;
                Measure(e, saved[s], saved[s + 1] != 0, saved[s + 2], out width, out height);
                if (width != saved[s + 3] || height != saved[s + 4])
                {
                    return false;
                }
            }
            if (e.savedMinContent >= 0 && MinContentWidth(e) != e.savedMinContent)
            {
                return false;
            }
            if (e.savedContentHeightWidth >= 0)
            {
                if (ContentHeightAt(e, e.savedContentHeightWidth) != e.savedContentHeight)
                {
                    return false;
                }
                // The parent keeps relying on it: current again (the measure
                // cache and min-content width were refreshed above).
                e.contentHeightWidth = e.savedContentHeightWidth;
                e.contentHeightAnswer = e.savedContentHeight;
                e.contentHeightPass = pass;
            }
            return true;
        }

        private static void SortByDepth(List<Element> list)
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }
            int[] depths = new int[count];
            for (int i = 0; i < count; i++)
            {
                int depth = 0;
                for (Element e = list[i].parent; e != null; e = e.parent)
                {
                    depth++;
                }
                depths[i] = depth;
            }
            for (int i = 1; i < count; i++)
            {
                Element item = list[i];
                int depth = depths[i];
                int j = i - 1;
                while (j >= 0 && depths[j] > depth)
                {
                    list[j + 1] = list[j];
                    depths[j + 1] = depths[j];
                    j--;
                }
                list[j + 1] = item;
                depths[j + 1] = depth;
            }
        }

        // ================================================================
        // Arrange: final rectangles
        // ================================================================

        private void Arrange(Element e, int x, int y, int width, int height, int containingWidth,
            int clipLeft, int clipTop, int clipRight, int clipBottom)
        {
            int incomingGroup = e.parent != null ? e.parent.childGroup : TransformGroups.Root;

            // Nothing inside changed and it lands exactly where it was: done.
            if (e.hasArranged && !e.layoutDirty && e.x == x && e.y == y && e.width == width && e.height == height
                && e.arrangedContainingWidth == containingWidth && e.incomingGroup == incomingGroup
                && e.incomingClipLeft == clipLeft && e.incomingClipTop == clipTop
                && e.incomingClipRight == clipRight && e.incomingClipBottom == clipBottom)
            {
                return;
            }
            arrangedCount++;
            e.hasArranged = true;
            e.arrangedContainingWidth = containingWidth;
            e.MarkPaintDirty();
            e.incomingGroup = incomingGroup;
            e.incomingClipLeft = clipLeft;
            e.incomingClipTop = clipTop;
            e.incomingClipRight = clipRight;
            e.incomingClipBottom = clipBottom;

            // A translated element moves as a group of its own: whatever
            // clipped it becomes the group's clip, not its records' clip.
            if (e.translateGroup > TransformGroups.Root)
            {
                groups.SetFrame(e.translateGroup, incomingGroup, clipLeft, clipTop, clipRight, clipBottom,
                    x + width * 0.5f, y + height * 0.5f);
                e.group = e.translateGroup;
                clipLeft = -TransformGroups.Unbounded;
                clipTop = -TransformGroups.Unbounded;
                clipRight = TransformGroups.Unbounded;
                clipBottom = TransformGroups.Unbounded;
            }
            else
            {
                e.group = incomingGroup;
            }
            e.childGroup = e.group;

            ComputedStyle s = e.computed;
            e.x = x;
            e.y = y;
            e.width = width;
            e.height = height;
            e.clipLeft = clipLeft;
            e.clipTop = clipTop;
            e.clipRight = clipRight;
            e.clipBottom = clipBottom;

            int borderLeft = (int)s.numbers[StyleProperty.BorderLeftWidth];
            int borderTop = (int)s.numbers[StyleProperty.BorderTopWidth];
            int borderRight = (int)s.numbers[StyleProperty.BorderRightWidth];
            int borderBottom = (int)s.numbers[StyleProperty.BorderBottomWidth];
            int paddingLeft = s.Resolve(StyleProperty.PaddingLeft, containingWidth, 0);
            int paddingTop = s.Resolve(StyleProperty.PaddingTop, containingWidth, 0);
            int paddingRight = s.Resolve(StyleProperty.PaddingRight, containingWidth, 0);
            int paddingBottom = s.Resolve(StyleProperty.PaddingBottom, containingWidth, 0);

            int contentX = x + borderLeft + paddingLeft;
            int contentY = y + borderTop + paddingTop;
            e.contentX = contentX;
            e.contentY = contentY;
            int contentWidth = Mathf.Max(0, width - borderLeft - borderRight - paddingLeft - paddingRight);
            int contentHeight = Mathf.Max(0, height - borderTop - borderBottom - paddingTop - paddingBottom);

            int childClipLeft = clipLeft, childClipTop = clipTop, childClipRight = clipRight, childClipBottom = clipBottom;
            if (s.ClipsContent)
            {
                childClipLeft = Mathf.Max(clipLeft, x + borderLeft);
                childClipTop = Mathf.Max(clipTop, y + borderTop);
                childClipRight = Mathf.Min(clipRight, x + width - borderRight);
                childClipBottom = Mathf.Min(clipBottom, y + height - borderBottom);
            }

            // A scroll view's content scrolls as a group; the padding box
            // (already in the child clip: scroll views clip) is its clip.
            ScrollView scrollView = e as ScrollView;
            // Its content is laid out from the padding box's corner (0, 0): the
            // group's origin places it, so when the view moves, the content's
            // layout (and its draw records) stays as it is.
            int localX = 0, localY = 0;
            if (scrollView != null)
            {
                groups.SetFrame(scrollView.scrollGroup, e.group, childClipLeft, childClipTop, childClipRight, childClipBottom);
                localX = x + borderLeft;
                localY = y + borderTop;
                groups.SetOrigin(scrollView.scrollGroup, localX, localY);
                e.childGroup = scrollView.scrollGroup;
                childClipLeft = -TransformGroups.Unbounded;
                childClipTop = -TransformGroups.Unbounded;
                childClipRight = TransformGroups.Unbounded;
                childClipBottom = TransformGroups.Unbounded;
            }

            if (e.IsLeaf)
            {
                e.ArrangeContent(contentWidth, contentHeight);
            }
            else
            {
                int usedWidth, usedHeight;
                LayoutChildren(e, contentWidth, true, contentHeight, true, true, contentX - localX, contentY - localY,
                    childClipLeft, childClipTop, childClipRight, childClipBottom, out usedWidth, out usedHeight);

                // Absolutely positioned children, against the padding box.
                int paddingBoxX = x + borderLeft - localX;
                int paddingBoxY = y + borderTop - localY;
                int paddingBoxWidth = Mathf.Max(0, width - borderLeft - borderRight);
                int paddingBoxHeight = Mathf.Max(0, height - borderTop - borderBottom);
                List<Element> absolutes = CollectAbsolutes(e, null, e.stackCount);
                for (int i = 0; absolutes != null && i < absolutes.Count; i++)
                {
                    ArrangeAbsolute(absolutes[i], paddingBoxX, paddingBoxY, paddingBoxWidth, paddingBoxHeight,
                        contentX - localX, contentY - localY, childClipLeft, childClipTop, childClipRight, childClipBottom);
                }
                if (scrollView != null)
                {
                    // Scrollable extent: the content plus the padding around it
                    // (and, for the scrollbar only, content being prepared).
                    int preparedWidth = 0, preparedHeight = 0;
                    PreparedExtent(e, paddingBoxX, paddingBoxY, ref preparedWidth, ref preparedHeight);
                    scrollView.ReportMetrics(
                        Mathf.Max(0, width - borderLeft - borderRight), Mathf.Max(0, height - borderTop - borderBottom),
                        usedWidth + paddingLeft + paddingRight, usedHeight + paddingTop + paddingBottom,
                        preparedWidth, preparedHeight);
                }
            }
            e.layoutDirty = false;
        }

        private void ArrangeAbsolute(Element e, int boxX, int boxY, int boxWidth, int boxHeight, int staticX, int staticY,
            int clipLeft, int clipTop, int clipRight, int clipBottom)
        {
            Element anchor = e.anchor;
            if (anchor != null && anchor.hasArranged && anchor.parent != null)
            {
                // A prepared box, on the element it will replace.
                int anchoredHeight;
                if (e.matchAnchorHeight)
                {
                    anchoredHeight = anchor.height;
                }
                else
                {
                    int unusedWidth;
                    Measure(e, anchor.width, true, -1, out unusedWidth, out anchoredHeight);
                }
                Arrange(e, anchor.x, anchor.y, anchor.width, anchoredHeight, boxWidth, clipLeft, clipTop, clipRight, clipBottom);
                return;
            }
            ComputedStyle s = e.computed;
            bool hasLeft = s.Unit(StyleProperty.Left) != (int)LengthUnit.Auto;
            bool hasRight = s.Unit(StyleProperty.Right) != (int)LengthUnit.Auto;
            bool hasTop = s.Unit(StyleProperty.Top) != (int)LengthUnit.Auto;
            bool hasBottom = s.Unit(StyleProperty.Bottom) != (int)LengthUnit.Auto;
            int left = s.Resolve(StyleProperty.Left, boxWidth, 0);
            int right = s.Resolve(StyleProperty.Right, boxWidth, 0);
            int top = s.Resolve(StyleProperty.Top, boxHeight, 0);
            int bottom = s.Resolve(StyleProperty.Bottom, boxHeight, 0);
            int marginLeft = s.Resolve(StyleProperty.MarginLeft, boxWidth, 0);
            int marginRight = s.Resolve(StyleProperty.MarginRight, boxWidth, 0);
            int marginTop = s.Resolve(StyleProperty.MarginTop, boxWidth, 0);
            int marginBottom = s.Resolve(StyleProperty.MarginBottom, boxWidth, 0);

            int width = s.Resolve(StyleProperty.Width, boxWidth, -1);
            if (width >= 0)
            {
                width = ClampWidth(s, width, boxWidth);
            }
            if (width < 0 && hasLeft && hasRight && s.Unit(StyleProperty.Width) != (int)LengthUnit.FitContent)
            {
                width = ClampWidth(s, Mathf.Max(0, boxWidth - left - right - marginLeft - marginRight), boxWidth);
            }
            int height = s.Resolve(StyleProperty.Height, boxHeight, -1);
            if (height >= 0)
            {
                height = ClampHeight(s, height, boxHeight);
            }
            if (height < 0 && hasTop && hasBottom && s.Unit(StyleProperty.Height) != (int)LengthUnit.FitContent)
            {
                height = ClampHeight(s, Mathf.Max(0, boxHeight - top - bottom - marginTop - marginBottom), boxHeight);
            }

            int measuredWidth, measuredHeight;
            if (width < 0)
            {
                int available = Mathf.Max(0, boxWidth - (hasLeft ? left : 0) - (hasRight ? right : 0) - marginLeft - marginRight);
                Measure(e, available, false, height >= 0 ? height : -1, out measuredWidth, out measuredHeight);
                width = measuredWidth;
            }
            if (height < 0)
            {
                Measure(e, width, true, -1, out measuredWidth, out measuredHeight);
                height = measuredHeight;
            }

            int x = hasLeft ? boxX + left + marginLeft
                : hasRight ? boxX + boxWidth - right - marginRight - width
                : staticX + marginLeft;
            int y = hasTop ? boxY + top + marginTop
                : hasBottom ? boxY + boxHeight - bottom - marginBottom - height
                : staticY + marginTop;
            Arrange(e, x, y, width, height, boxWidth, clipLeft, clipTop, clipRight, clipBottom);
        }

        // ================================================================
        // Measure: the border-box size an element wants
        // ================================================================

        /// `availableWidth` is -1 when unconstrained. With `exactWidth`, the
        /// width is imposed (stretch, a resolved flex size) and only the
        /// height is computed. `availableHeight` is the definite height of the
        /// containing box, or -1.
        internal void Measure(Element e, int availableWidth, bool exactWidth, int availableHeight,
            out int resultWidth, out int resultHeight)
        {
            if (TryCachedMeasure(e, availableWidth, exactWidth, availableHeight, out resultWidth, out resultHeight))
            {
                return;
            }

            ComputedStyle s = e.computed;
            int reference = availableWidth >= 0 ? availableWidth : 0;
            int horizontalEdges = (int)s.numbers[StyleProperty.BorderLeftWidth] + (int)s.numbers[StyleProperty.BorderRightWidth]
                + s.Resolve(StyleProperty.PaddingLeft, reference, 0) + s.Resolve(StyleProperty.PaddingRight, reference, 0);
            int verticalEdges = (int)s.numbers[StyleProperty.BorderTopWidth] + (int)s.numbers[StyleProperty.BorderBottomWidth]
                + s.Resolve(StyleProperty.PaddingTop, reference, 0) + s.Resolve(StyleProperty.PaddingBottom, reference, 0);

            int specifiedHeight = s.Resolve(StyleProperty.Height, availableHeight, -1);
            if (specifiedHeight >= 0)
            {
                specifiedHeight = Mathf.Max(ClampHeight(s, specifiedHeight, availableHeight), verticalEdges);
            }
            int innerHeight = specifiedHeight >= 0 ? specifiedHeight - verticalEdges : -1;

            int width = exactWidth ? availableWidth : s.Resolve(StyleProperty.Width, availableWidth, -1);
            int contentHeight = 0;

            if (width >= 0)
            {
                width = Mathf.Max(exactWidth ? width : ClampWidth(s, width, availableWidth), horizontalEdges);
                contentHeight = MeasureInner(e, width - horizontalEdges, true, innerHeight, out int unusedWidth);
            }
            else
            {
                // fit-content: min(max-content, available), then the height at that width.
                int innerAvailable = availableWidth >= 0 ? Mathf.Max(0, availableWidth - horizontalEdges) : -1;
                int maxContentHeight = MeasureInner(e, innerAvailable, false, innerHeight, out int contentWidth);
                width = contentWidth + horizontalEdges;
                if (availableWidth >= 0 && width > availableWidth)
                {
                    // fit-content never goes below min-content: past that it overflows.
                    width = Mathf.Max(availableWidth, MinContentWidth(e));
                }
                width = Mathf.Max(ClampWidth(s, width, availableWidth), horizontalEdges);
                if (width - horizontalEdges == contentWidth && (e.IsLeaf || (fitShortcut && IsPlainRow(s))))
                {
                    // At exactly its max-content width, a leaf or a single
                    // flex row lays out as it did for max-content: no second pass.
                    contentHeight = maxContentHeight;
                }
                else
                {
                    contentHeight = MeasureInner(e, width - horizontalEdges, true, innerHeight, out int unusedWidth);
                }
            }

            int height = specifiedHeight >= 0 ? specifiedHeight
                : Mathf.Max(ClampHeight(s, contentHeight + verticalEdges, availableHeight), verticalEdges);

            resultWidth = width;
            resultHeight = height;
            StoreMeasure(e, availableWidth, exactWidth, availableHeight, width, height);
        }

        internal bool fitShortcut = true;

        /// A flex row that does not wrap: at its max-content width every item
        /// gets its max-content size, as when measured unconstrained.
        private static bool IsPlainRow(ComputedStyle s)
        {
            if (s.Display != DisplayMode.Flex || s.Enum(StyleProperty.Wrap) == (int)WrapMode.Wrap)
            {
                return false;
            }
            Direction direction = (Direction)s.Enum(StyleProperty.Direction);
            return direction == Direction.Row || direction == Direction.RowReverse;
        }

        /// Content size inside the content box. Returns the content height and
        /// gives the content width through `contentWidth`.
        private int MeasureInner(Element e, int innerWidth, bool widthDefinite, int innerHeight, out int contentWidth)
        {
            if (e.IsLeaf)
            {
                int leafWidth, leafHeight;
                e.MeasureContent(innerWidth, out leafWidth, out leafHeight);
                contentWidth = leafWidth;
                return leafHeight;
            }
            int usedWidth, usedHeight;
            LayoutChildren(e, innerWidth, widthDefinite, innerHeight, innerHeight >= 0, false, 0, 0, 0, 0, 0, 0,
                out usedWidth, out usedHeight);
            contentWidth = widthDefinite ? innerWidth : usedWidth;
            return usedHeight;
        }

        private static int ClampWidth(ComputedStyle s, int value, int reference)
        {
            int minimum = s.Resolve(StyleProperty.MinWidth, reference, 0);
            int maximum = s.Resolve(StyleProperty.MaxWidth, reference, Unbounded);
            return Mathf.Max(minimum, Mathf.Min(value, maximum));
        }

        private static int ClampHeight(ComputedStyle s, int value, int reference)
        {
            int minimum = s.Resolve(StyleProperty.MinHeight, reference, 0);
            int maximum = s.Resolve(StyleProperty.MaxHeight, reference, Unbounded);
            return Mathf.Max(minimum, Mathf.Min(value, maximum));
        }

        // Cache entries per element: [availableWidth, exact, availableHeight,
        // width, height, pass, asked] — asked: 1 when its parent's layout
        // asked it (not only KeepsAnswers replaying it).
        internal const int CacheEntry = 7;

        private bool TryCachedMeasure(Element e, int availableWidth, bool exactWidth, int availableHeight,
            out int width, out int height)
        {
            int[] cache = e.measureCache;
            int exact = exactWidth ? 1 : 0;
            for (int slot = 0; slot < Element.MeasureCacheSlots; slot++)
            {
                int o = slot * CacheEntry;
                if (cache[o + 5] != 0 && cache[o + 5] >= e.validSincePass && (!e.layoutDirty || cache[o + 5] == pass)
                    && cache[o] == availableWidth && cache[o + 1] == exact && cache[o + 2] == availableHeight)
                {
                    cache[o + 6] = 1;
                    width = cache[o + 3];
                    height = cache[o + 4];
                    return true;
                }
            }
            width = 0;
            height = 0;
            return false;
        }

        private void StoreMeasure(Element e, int availableWidth, bool exactWidth, int availableHeight, int width, int height)
        {
            int[] cache = e.measureCache;
            int o = e.measureCacheNext * CacheEntry;
            e.measureCacheNext = (e.measureCacheNext + 1) % Element.MeasureCacheSlots;
            if (cache[o + 5] != 0 && cache[o + 5] >= e.validSincePass && cache[o + 6] != 0)
            {
                e.measureOverflowed = true;
            }
            cache[o] = availableWidth;
            cache[o + 1] = exactWidth ? 1 : 0;
            cache[o + 2] = availableHeight;
            cache[o + 3] = width;
            cache[o + 4] = height;
            cache[o + 5] = pass;
            cache[o + 6] = 1;
        }

        // ================================================================
        // Children: block and flex
        // ================================================================

        /// Lays the in-flow children out in the content box. With `arrange`,
        /// their final rectangles are set (content box at originX/originY);
        /// otherwise only the used content size is computed.
        private void LayoutChildren(Element e, int innerWidth, bool widthDefinite, int innerHeight, bool heightDefinite,
            bool arrange, int originX, int originY, int clipLeft, int clipTop, int clipRight, int clipBottom,
            out int usedWidth, out int usedHeight)
        {
            // Working arrays are reused per recursion depth: allocating them
            // on every call is a large share of the cost under Udon.
            if (depth == scratchByDepth.Count)
            {
                scratchByDepth.Add(new LayoutScratch());
            }
            LayoutScratch scratch = scratchByDepth[depth];
            depth++;
            LayoutChildrenWith(scratch, e, innerWidth, widthDefinite, innerHeight, heightDefinite, arrange, originX, originY,
                clipLeft, clipTop, clipRight, clipBottom, out usedWidth, out usedHeight);
            depth--;
        }

        private void LayoutChildrenWith(LayoutScratch scratch, Element e, int innerWidth, bool widthDefinite, int innerHeight,
            bool heightDefinite, bool arrange, int originX, int originY, int clipLeft, int clipTop, int clipRight, int clipBottom,
            out int usedWidth, out int usedHeight)
        {
            ComputedStyle s = e.computed;
            // Along a scroll axis the content is not limited by the box.
            ScrollView scrollView = e as ScrollView;
            if (scrollView != null)
            {
                if (scrollView.ScrollsVertically)
                {
                    heightDefinite = false;
                    innerHeight = -1;
                }
                if (scrollView.ScrollsHorizontally)
                {
                    widthDefinite = false;
                    innerWidth = -1;
                }
            }
            bool isFlex = s.Display == DisplayMode.Flex;
            Direction direction = isFlex ? (Direction)s.Enum(StyleProperty.Direction) : Direction.Column;
            bool isRow = direction == Direction.Row || direction == Direction.RowReverse;
            bool reverse = direction == Direction.RowReverse || direction == Direction.ColumnReverse;
            bool wrap = isFlex && s.Enum(StyleProperty.Wrap) == (int)WrapMode.Wrap;
            Justify justify = isFlex ? (Justify)s.Enum(StyleProperty.JustifyContent) : Justify.Start;
            Align alignItems = isFlex ? (Align)s.Enum(StyleProperty.AlignItems) : Align.Stretch;
            Align alignContent = (Align)s.Enum(StyleProperty.AlignContent);

            int mainAvailable = isRow ? innerWidth : innerHeight;
            bool mainDefinite = isRow ? widthDefinite : heightDefinite;
            int crossAvailable = isRow ? innerHeight : innerWidth;
            bool crossDefinite = isRow ? heightDefinite : widthDefinite;
            int rowGap = isFlex ? s.Resolve(StyleProperty.RowGap, heightDefinite ? innerHeight : -1, 0) : 0;
            int columnGap = isFlex ? s.Resolve(StyleProperty.ColumnGap, widthDefinite ? innerWidth : -1, 0) : 0;
            int mainGap = isRow ? columnGap : rowGap;
            int crossGap = isRow ? rowGap : columnGap;
            int percentReference = widthDefinite ? innerWidth : -1;
            int mainReference = mainDefinite ? mainAvailable : -1;
            int crossReference = crossDefinite ? crossAvailable : -1;

            // A column whose leading children are settled (see SettledChildren)
            // is laid out from its first new child: the settled ones count as
            // one block of known height.
            bool stacks = !isRow && !reverse && !wrap && justify == Justify.Start && scrollView == null;
            int skip = stacks ? SettledChildren(e, arrange, innerWidth, widthDefinite, mainGap, originX, originY,
                clipLeft, clipTop, clipRight, clipBottom) : 0;
            Element settledLast = skip > 0 ? e.children[skip - 1] : null;
            int stackMain = skip > 0 ? settledLast.stackEndMain : 0;
            int stackItems = skip > 0 ? settledLast.stackEndItems : 0;

            List<Element> items = scratch.items;
            items.Clear();
            CollectFlow(e, skip, items, arrange, originX, originY, innerWidth, innerHeight, clipLeft, clipTop, clipRight, clipBottom);
            int count = items.Count;
            scratch.Ensure(count);
            if (count == 0 && stackItems == 0)
            {
                usedWidth = 0;
                usedHeight = 0;
                if (arrange && stacks)
                {
                    SettleStack(e, skip, scratch, 0, innerWidth, widthDefinite, mainGap, originX, originY,
                        clipLeft, clipTop, clipRight, clipBottom, crossDefinite ? crossAvailable : 0);
                }
                return;
            }
            int stackLead = stackItems > 0 && count > 0 ? mainGap : 0;

            int[] marginMainStart = scratch.marginMainStart;
            int[] marginMainEnd = scratch.marginMainEnd;
            int[] marginCrossStart = scratch.marginCrossStart;
            int[] marginCrossEnd = scratch.marginCrossEnd;
            int[] hypothetical = scratch.hypothetical;
            int[] mainSize = scratch.mainSize;
            int[] crossSize = scratch.crossSize;
            float[] grow = scratch.grow;
            float[] shrink = scratch.shrink;
            int[] minimumMain = scratch.minimumMain;
            int[] maximumMain = scratch.maximumMain;
            bool[] stretch = scratch.stretch;
            int[] flexBase = scratch.flexBase;

            // ---- 1. margins and hypothetical main sizes ----------------------
            for (int i = 0; i < count; i++)
            {
                Element item = items[i];
                ComputedStyle c = item.computed;
                int marginTop = c.Resolve(StyleProperty.MarginTop, percentReference, 0);
                int marginRight = c.Resolve(StyleProperty.MarginRight, percentReference, 0);
                int marginBottom = c.Resolve(StyleProperty.MarginBottom, percentReference, 0);
                int marginLeft = c.Resolve(StyleProperty.MarginLeft, percentReference, 0);
                marginMainStart[i] = isRow ? marginLeft : marginTop;
                marginMainEnd[i] = isRow ? marginRight : marginBottom;
                marginCrossStart[i] = isRow ? marginTop : marginLeft;
                marginCrossEnd[i] = isRow ? marginBottom : marginRight;
                int marginsCross = marginCrossStart[i] + marginCrossEnd[i];

                grow[i] = isFlex ? c.Number(StyleProperty.FlexGrow) : 0f;
                shrink[i] = isFlex ? c.Number(StyleProperty.FlexShrink) : 0f;

                Align self = (Align)c.Enum(StyleProperty.AlignSelf);
                if (!isFlex || self == Align.Auto)
                {
                    self = alignItems;
                }
                int crossProperty = isRow ? StyleProperty.Height : StyleProperty.Width;
                stretch[i] = self == Align.Stretch && c.Resolve(crossProperty, crossReference, -1) < 0
                    && c.Unit(crossProperty) != (int)LengthUnit.FitContent;

                int mainProperty = isRow ? StyleProperty.Width : StyleProperty.Height;
                int basis = isFlex ? c.Resolve(StyleProperty.FlexBasis, mainReference, -1) : -1;
                if (basis < 0)
                {
                    basis = c.Resolve(mainProperty, mainReference, -1);
                }
                int measuredWidth, measuredHeight;
                if (basis < 0)
                {
                    if (isRow)
                    {
                        Measure(item, -1, false, crossReference >= 0 ? crossReference - marginsCross : -1,
                            out measuredWidth, out measuredHeight);
                        basis = measuredWidth;
                    }
                    else
                    {
                        int crossForItem = crossAvailable >= 0 ? Mathf.Max(0, crossAvailable - marginsCross) : -1;
                        bool exact = stretch[i] && crossDefinite;
                        Measure(item, crossForItem, exact, -1, out measuredWidth, out measuredHeight);
                        basis = measuredHeight;
                    }
                }

                int edges = isRow
                    ? (int)c.numbers[StyleProperty.BorderLeftWidth] + (int)c.numbers[StyleProperty.BorderRightWidth]
                        + c.Resolve(StyleProperty.PaddingLeft, percentReference, 0) + c.Resolve(StyleProperty.PaddingRight, percentReference, 0)
                    : (int)c.numbers[StyleProperty.BorderTopWidth] + (int)c.numbers[StyleProperty.BorderBottomWidth]
                        + c.Resolve(StyleProperty.PaddingTop, percentReference, 0) + c.Resolve(StyleProperty.PaddingBottom, percentReference, 0);
                maximumMain[i] = c.Resolve(isRow ? StyleProperty.MaxWidth : StyleProperty.MaxHeight, mainReference, Unbounded);
                int minimumProperty = isRow ? StyleProperty.MinWidth : StyleProperty.MinHeight;
                int minimum;
                if (c.Unit(minimumProperty) == (int)LengthUnit.Auto)
                {
                    minimum = isFlex ? AutomaticMinimum(item, isRow, mainProperty, mainReference, maximumMain[i],
                        crossAvailable, crossDefinite, marginsCross, stretch[i]) : 0;
                }
                else
                {
                    minimum = c.Resolve(minimumProperty, mainReference, 0);
                }
                minimumMain[i] = Mathf.Max(edges, minimum);
                flexBase[i] = basis;
                hypothetical[i] = Mathf.Max(minimumMain[i], Mathf.Min(basis, maximumMain[i]));
            }

            // ---- 2. lines --------------------------------------------------------
            int[] lineStarts = scratch.lineStarts;
            int lineCount = 1;
            lineStarts[0] = 0;
            if (wrap && mainDefinite)
            {
                int lineUsed = 0;
                int inLine = 0;
                for (int i = 0; i < count; i++)
                {
                    int outer = hypothetical[i] + marginMainStart[i] + marginMainEnd[i];
                    int next = lineUsed + (inLine > 0 ? mainGap : 0) + outer;
                    if (inLine > 0 && next > mainAvailable)
                    {
                        lineStarts[lineCount++] = i;
                        lineUsed = outer;
                        inLine = 1;
                    }
                    else
                    {
                        lineUsed = next;
                        inLine++;
                    }
                }
            }
            lineStarts[lineCount] = count;
            int[] lineCross = scratch.lineCross;
            int[] lineMainUsed = scratch.lineMainUsed;

            // ---- 3. flexible lengths, per line -----------------------------------
            for (int line = 0; line < lineCount; line++)
            {
                int start = lineStarts[line];
                int end = lineStarts[line + 1];
                int used = end > start ? mainGap * (end - start - 1) : 0;
                for (int i = start; i < end; i++)
                {
                    used += hypothetical[i] + marginMainStart[i] + marginMainEnd[i];
                    mainSize[i] = hypothetical[i];
                }
                used += stackMain + stackLead;
                if (mainDefinite && used != mainAvailable)
                {
                    if (stacks)
                    {
                        // Flexible lengths share space among every item: none
                        // is settled on its own.
                        e.stackCount = 0;
                        if (skip > 0)
                        {
                            LayoutChildrenWith(scratch, e, innerWidth, widthDefinite, innerHeight, heightDefinite, arrange,
                                originX, originY, clipLeft, clipTop, clipRight, clipBottom, out usedWidth, out usedHeight);
                            return;
                        }
                        stacks = false;
                    }
                    ResolveFlexibleLengths(scratch, start, end, used < mainAvailable, mainAvailable - mainGap * (end - start - 1));
                }
                int lineUsed = (end > start ? mainGap * (end - start - 1) : 0) + stackMain + stackLead;
                for (int i = start; i < end; i++)
                {
                    lineUsed += mainSize[i] + marginMainStart[i] + marginMainEnd[i];
                }
                lineMainUsed[line] = lineUsed;
            }

            // ---- 4. cross sizes ----------------------------------------------------
            bool singleLineFillsCross = !wrap && crossDefinite;
            for (int line = 0; line < lineCount; line++)
            {
                int start = lineStarts[line];
                int end = lineStarts[line + 1];
                int maximum = skip > 0 ? settledLast.stackEndCross : 0;
                for (int i = start; i < end; i++)
                {
                    Element item = items[i];
                    int marginsCross = marginCrossStart[i] + marginCrossEnd[i];
                    int measuredWidth, measuredHeight;
                    if (isRow)
                    {
                        Measure(item, mainSize[i], true, crossReference >= 0 ? Mathf.Max(0, crossReference - marginsCross) : -1,
                            out measuredWidth, out measuredHeight);
                        crossSize[i] = measuredHeight;
                    }
                    else if (stretch[i] && singleLineFillsCross)
                    {
                        crossSize[i] = ClampWidth(item.computed, Mathf.Max(0, crossAvailable - marginsCross), crossReference);
                    }
                    else
                    {
                        int crossForItem = crossAvailable >= 0 ? Mathf.Max(0, crossAvailable - marginsCross) : -1;
                        Measure(item, crossForItem, false, mainReference, out measuredWidth, out measuredHeight);
                        crossSize[i] = measuredWidth;
                    }
                    scratch.crossOuter[i] = crossSize[i] + marginsCross;
                    maximum = Mathf.Max(maximum, crossSize[i] + marginsCross);
                }
                lineCross[line] = singleLineFillsCross ? crossAvailable : maximum;
            }
            if (arrange && skip > 0 && lineCross[0] != e.stackLineCross)
            {
                // Wider now: the settled children are aligned in a new width.
                e.stackCount = 0;
                LayoutChildrenWith(scratch, e, innerWidth, widthDefinite, innerHeight, heightDefinite, arrange,
                    originX, originY, clipLeft, clipTop, clipRight, clipBottom, out usedWidth, out usedHeight);
                return;
            }

            // align-content: share the leftover cross space between lines.
            int[] lineOffset = scratch.lineOffset;
            int totalCross = crossGap * (lineCount - 1);
            for (int line = 0; line < lineCount; line++) totalCross += lineCross[line];
            int leftoverCross = crossDefinite && wrap ? crossAvailable - totalCross : 0;
            if (leftoverCross > 0 && alignContent == Align.Stretch)
            {
                int previousEdge = 0;
                for (int line = 0; line < lineCount; line++)
                {
                    int edge = Mathf.RoundToInt((float)leftoverCross * (line + 1) / lineCount);
                    lineCross[line] += edge - previousEdge;
                    previousEdge = edge;
                }
                leftoverCross = 0;
            }
            DistributeSpace(leftoverCross, lineCount, alignContent == Align.End ? Justify.End
                : alignContent == Align.Center ? Justify.Center
                : alignContent == Align.SpaceBetween ? Justify.SpaceBetween
                : alignContent == Align.SpaceAround ? Justify.SpaceAround
                : Justify.Start, crossGap, lineCross, lineOffset);

            // Stretched items take their line's cross size.
            for (int line = 0; line < lineCount; line++)
            {
                for (int i = lineStarts[line]; i < lineStarts[line + 1]; i++)
                {
                    if (stretch[i])
                    {
                        int stretched = Mathf.Max(0, lineCross[line] - marginCrossStart[i] - marginCrossEnd[i]);
                        crossSize[i] = isRow ? ClampHeight(items[i].computed, stretched, crossReference)
                            : ClampWidth(items[i].computed, stretched, crossReference);
                    }
                }
            }

            // ---- 5. used size --------------------------------------------------------
            int usedMain = 0;
            for (int line = 0; line < lineCount; line++) usedMain = Mathf.Max(usedMain, lineMainUsed[line]);
            int usedCross = crossGap * (lineCount - 1);
            for (int line = 0; line < lineCount; line++) usedCross += lineCross[line];
            usedWidth = isRow ? usedMain : usedCross;
            usedHeight = isRow ? usedCross : usedMain;
            if (!arrange)
            {
                return;
            }

            // ---- 6. positions --------------------------------------------------------
            int containerMain = mainDefinite ? mainAvailable : usedMain;
            for (int line = 0; line < lineCount; line++)
            {
                int start = lineStarts[line];
                int end = lineStarts[line + 1];
                int itemsInLine = end - start;
                int[] slotSizes = scratch.slotSizes;
                int[] slotOffsets = scratch.slotOffsets;
                for (int k = 0; k < itemsInLine; k++)
                {
                    int i = start + k;
                    slotSizes[k] = mainSize[i] + marginMainStart[i] + marginMainEnd[i];
                }
                DistributeSpace(Mathf.Max(0, containerMain - lineMainUsed[line]), itemsInLine, justify, mainGap, slotSizes, slotOffsets);

                for (int k = 0; k < itemsInLine; k++)
                {
                    int i = start + k;
                    Element item = items[i];
                    int mainPosition = slotOffsets[k] + marginMainStart[i] + stackMain + stackLead;
                    if (reverse)
                    {
                        mainPosition = containerMain - mainPosition - mainSize[i];
                    }

                    Align self = (Align)item.computed.Enum(StyleProperty.AlignSelf);
                    if (!isFlex || self == Align.Auto) self = alignItems;
                    int outerCross = crossSize[i] + marginCrossStart[i] + marginCrossEnd[i];
                    int crossPosition = lineOffset[line] + marginCrossStart[i];
                    if (self == Align.End)
                    {
                        crossPosition = lineOffset[line] + lineCross[line] - crossSize[i] - marginCrossEnd[i];
                    }
                    else if (self == Align.Center)
                    {
                        crossPosition = lineOffset[line] + marginCrossStart[i] + (lineCross[line] - outerCross) / 2;
                    }

                    int childX = originX + (isRow ? mainPosition : crossPosition);
                    int childY = originY + (isRow ? crossPosition : mainPosition);
                    int childWidth = isRow ? mainSize[i] : crossSize[i];
                    int childHeight = isRow ? crossSize[i] : mainSize[i];

                    // position: relative shifts the box without affecting others.
                    ComputedStyle c = item.computed;
                    if (c.Unit(StyleProperty.Left) != (int)LengthUnit.Auto) childX += c.Resolve(StyleProperty.Left, innerWidth, 0);
                    else if (c.Unit(StyleProperty.Right) != (int)LengthUnit.Auto) childX -= c.Resolve(StyleProperty.Right, innerWidth, 0);
                    if (c.Unit(StyleProperty.Top) != (int)LengthUnit.Auto) childY += c.Resolve(StyleProperty.Top, innerHeight, 0);
                    else if (c.Unit(StyleProperty.Bottom) != (int)LengthUnit.Auto) childY -= c.Resolve(StyleProperty.Bottom, innerHeight, 0);

                    Arrange(item, childX, childY, childWidth, childHeight, innerWidth, clipLeft, clipTop, clipRight, clipBottom);
                }
            }
            if (stacks)
            {
                SettleStack(e, skip, scratch, count, innerWidth, widthDefinite, mainGap, originX, originY,
                    clipLeft, clipTop, clipRight, clipBottom, lineCross[0]);
            }
        }

        /// How many leading children of a column are settled for a layout
        /// with these inputs: laid out, since, exactly as they would be now
        /// (every change to one, or before one, cuts the prefix back — see
        /// Element.TruncateStack). 0 when the inputs differ.
        private static int SettledChildren(Element e, bool arrange, int innerWidth, bool widthDefinite, int gap,
            int originX, int originY, int clipLeft, int clipTop, int clipRight, int clipBottom)
        {
            if (e.stackCount == 0 || e.stackWidth != innerWidth || e.stackWidthDefinite != widthDefinite || e.stackGap != gap)
            {
                return 0;
            }
            if (arrange && (e.stackOriginX != originX || e.stackOriginY != originY || e.stackGroup != e.childGroup
                || e.stackClipLeft != clipLeft || e.stackClipTop != clipTop || e.stackClipRight != clipRight
                || e.stackClipBottom != clipBottom))
            {
                return 0;
            }
            return e.stackCount;
        }

        /// After arranging a column: its children from `skip` on join the
        /// settled prefix, up to the first one whose layout may depend on
        /// more than what came before it (see Stacks).
        private static void SettleStack(Element e, int skip, LayoutScratch scratch, int count,
            int innerWidth, bool widthDefinite, int gap, int originX, int originY,
            int clipLeft, int clipTop, int clipRight, int clipBottom, int lineCross)
        {
            Element settledLast = skip > 0 ? e.children[skip - 1] : null;
            int main = skip > 0 ? settledLast.stackEndMain : 0;
            int cross = skip > 0 ? settledLast.stackEndCross : 0;
            int placed = skip > 0 ? settledLast.stackEndItems : 0;
            int settled = skip;
            int item = 0;
            List<Element> items = scratch.items;
            for (int c = skip; c < e.children.Count; c++)
            {
                Element child = e.children[c];
                ComputedStyle s = child.computed;
                if (child.layoutPaused || s.Display == DisplayMode.Contents || s.Position == PositionMode.Absolute)
                {
                    break;
                }
                if (s.Display != DisplayMode.None)
                {
                    if (item >= count || items[item] != child || !Stacks(s))
                    {
                        break;
                    }
                    main += (placed > 0 ? gap : 0) + scratch.mainSize[item] + scratch.marginMainStart[item] + scratch.marginMainEnd[item];
                    cross = Mathf.Max(cross, scratch.crossOuter[item]);
                    placed++;
                    item++;
                }
                child.stackIndex = c;
                child.stackEndMain = main;
                child.stackEndCross = cross;
                child.stackEndItems = placed;
                settled++;
            }
            e.stackCount = settled;
            e.stackWidth = innerWidth;
            e.stackWidthDefinite = widthDefinite;
            e.stackGap = gap;
            e.stackOriginX = originX;
            e.stackOriginY = originY;
            e.stackGroup = e.childGroup;
            e.stackClipLeft = clipLeft;
            e.stackClipTop = clipTop;
            e.stackClipRight = clipRight;
            e.stackClipBottom = clipBottom;
            e.stackLineCross = lineCross;
        }

        /// A column item laid out from its own content and the column's
        /// width only: no relative offset, nothing sized against the
        /// column's height.
        private static bool Stacks(ComputedStyle s)
        {
            int auto = (int)LengthUnit.Auto;
            int percent = (int)LengthUnit.Percent;
            return s.Unit(StyleProperty.Left) == auto && s.Unit(StyleProperty.Right) == auto
                && s.Unit(StyleProperty.Top) == auto && s.Unit(StyleProperty.Bottom) == auto
                && s.Unit(StyleProperty.Height) != percent && s.Unit(StyleProperty.FlexBasis) != percent
                && s.Unit(StyleProperty.MinHeight) != percent && s.Unit(StyleProperty.MaxHeight) != percent;
        }

        /// CSS "resolve the flexible lengths" (§9.7): distribute the free
        /// space by grow (or shrink × base) factors, clamp to min/max, freeze
        /// the items that violated in the direction of the total violation,
        /// and distribute again among the rest — so space an item cannot
        /// take is handed to the others. Shares are rounded cumulatively.
        private static void ResolveFlexibleLengths(LayoutScratch scratch, int start, int end, bool growing, int space)
        {
            int[] mainSize = scratch.mainSize;
            int[] flexBase = scratch.flexBase;
            int[] hypothetical = scratch.hypothetical;
            int[] minimumMain = scratch.minimumMain;
            int[] maximumMain = scratch.maximumMain;
            int[] marginMainStart = scratch.marginMainStart;
            int[] marginMainEnd = scratch.marginMainEnd;
            float[] grow = scratch.grow;
            float[] shrink = scratch.shrink;
            bool[] frozen = scratch.frozen;
            int[] violation = scratch.violation;

            for (int i = start; i < end; i++)
            {
                float factor = growing ? grow[i] : shrink[i];
                frozen[i] = factor <= 0f
                    || (growing && flexBase[i] > hypothetical[i])
                    || (!growing && flexBase[i] < hypothetical[i]);
                mainSize[i] = hypothetical[i];
            }

            for (int iteration = 0; iteration <= end - start; iteration++)
            {
                int free = space;
                float totalWeight = 0f;
                float totalGrow = 0f;
                bool anyOpen = false;
                for (int i = start; i < end; i++)
                {
                    free -= marginMainStart[i] + marginMainEnd[i];
                    if (frozen[i])
                    {
                        free -= mainSize[i];
                    }
                    else
                    {
                        free -= flexBase[i];
                        anyOpen = true;
                        totalGrow += grow[i];
                        totalWeight += growing ? grow[i] : shrink[i] * flexBase[i];
                    }
                }
                if (!anyOpen)
                {
                    return;
                }
                // A grow sum below 1 hands out only that fraction (CSS).
                float distributable = growing && totalGrow < 1f ? free * totalGrow : free;
                float accumulated = 0f;
                int previousEdge = 0;
                int totalViolation = 0;
                for (int i = start; i < end; i++)
                {
                    if (frozen[i])
                    {
                        continue;
                    }
                    int target = flexBase[i];
                    if (totalWeight > 0f)
                    {
                        accumulated += growing ? grow[i] : shrink[i] * flexBase[i];
                        int edge = Mathf.RoundToInt(distributable * accumulated / totalWeight);
                        target += edge - previousEdge;
                        previousEdge = edge;
                    }
                    int clamped = Mathf.Max(minimumMain[i], Mathf.Min(target, maximumMain[i]));
                    violation[i] = clamped - target;
                    totalViolation += violation[i];
                    mainSize[i] = clamped;
                }
                if (totalViolation == 0)
                {
                    return;
                }
                for (int i = start; i < end; i++)
                {
                    if (!frozen[i] && ((totalViolation > 0 && violation[i] > 0) || (totalViolation < 0 && violation[i] < 0)))
                    {
                        frozen[i] = true;
                    }
                }
            }
        }

        /// CSS automatic minimum size of a flex item on the main axis: its
        /// content (min-content width, or content height), no larger than a
        /// specified size; 0 for scroll containers (and overflow: hidden).
        private int AutomaticMinimum(Element item, bool isRow, int mainProperty, int mainReference, int maximum,
            int crossAvailable, bool crossDefinite, int marginsCross, bool stretched)
        {
            ComputedStyle c = item.computed;
            if (c.ClipsContent || item is ScrollView)
            {
                return 0;
            }
            int content;
            if (isRow)
            {
                content = MinContentWidth(item);
            }
            else
            {
                // Content height at the width the item will have.
                int crossForItem = crossAvailable >= 0 ? Mathf.Max(0, crossAvailable - marginsCross) : -1;
                int width, height;
                Measure(item, crossForItem, stretched && crossDefinite, -1, out width, out height);
                if (c.Resolve(StyleProperty.Height, -1, -1) >= 0)
                {
                    content = ContentHeightAt(item, width);
                    // Not in the measure cache: remembered for early cutoff.
                    item.contentHeightWidth = width;
                    item.contentHeightAnswer = content;
                    item.contentHeightPass = pass;
                }
                else
                {
                    content = height;
                }
            }
            content = Mathf.Min(content, maximum);
            int specified = c.Resolve(mainProperty, mainReference, -1);
            return specified >= 0 ? Mathf.Min(specified, content) : content;
        }

        /// Border-box height of an element's content at a border-box width,
        /// ignoring its specified height.
        private int ContentHeightAt(Element e, int width)
        {
            ComputedStyle s = e.computed;
            int horizontalEdges = (int)s.numbers[StyleProperty.BorderLeftWidth] + (int)s.numbers[StyleProperty.BorderRightWidth]
                + s.Resolve(StyleProperty.PaddingLeft, width, 0) + s.Resolve(StyleProperty.PaddingRight, width, 0);
            int verticalEdges = (int)s.numbers[StyleProperty.BorderTopWidth] + (int)s.numbers[StyleProperty.BorderBottomWidth]
                + s.Resolve(StyleProperty.PaddingTop, width, 0) + s.Resolve(StyleProperty.PaddingBottom, width, 0);
            int contentWidth;
            int contentHeight = MeasureInner(e, Mathf.Max(0, width - horizontalEdges), true, -1, out contentWidth);
            return contentHeight + verticalEdges;
        }

        /// Min-content width (border box): the narrowest the element can be
        /// without its content overflowing. Cached like measurements.
        internal int MinContentWidth(Element e)
        {
            if (e.minContentPass != 0 && e.minContentPass >= e.validSincePass && (!e.layoutDirty || e.minContentPass == pass))
            {
                return e.minContentWidth;
            }
            ComputedStyle s = e.computed;
            int edges = (int)s.numbers[StyleProperty.BorderLeftWidth] + (int)s.numbers[StyleProperty.BorderRightWidth]
                + s.Resolve(StyleProperty.PaddingLeft, 0, 0) + s.Resolve(StyleProperty.PaddingRight, 0, 0);
            int content = 0;
            if (e.IsLeaf)
            {
                content = e.MinContentWidthOfContent();
            }
            else
            {
                bool isFlex = s.Display == DisplayMode.Flex;
                Direction direction = (Direction)s.Enum(StyleProperty.Direction);
                bool sums = isFlex && (direction == Direction.Row || direction == Direction.RowReverse)
                    && s.Enum(StyleProperty.Wrap) != (int)WrapMode.Wrap;
                int gap = sums ? s.Resolve(StyleProperty.ColumnGap, -1, 0) : 0;
                content = ChildrenMinContent(e, sums, gap);
            }
            int result = content + edges;
            e.minContentWidth = result;
            e.minContentPass = pass;
            return result;
        }

        /// Sum (a single flex row) or maximum of the in-flow children's
        /// min-content contributions, through `display: contents`.
        private int ChildrenMinContent(Element e, bool sums, int gap)
        {
            int total = 0;
            int counted = 0;
            for (int i = 0; i < e.children.Count; i++)
            {
                Element child = e.children[i];
                ComputedStyle c = child.computed;
                if (c.Display == DisplayMode.None || c.Position == PositionMode.Absolute || child.layoutPaused)
                {
                    continue;
                }
                int contribution;
                if (c.Display == DisplayMode.Contents)
                {
                    contribution = ChildrenMinContent(child, sums, gap);
                }
                else
                {
                    int width = c.Resolve(StyleProperty.Width, -1, -1);
                    contribution = width >= 0 ? width : MinContentWidth(child);
                    contribution = Mathf.Max(c.Resolve(StyleProperty.MinWidth, -1, 0), Mathf.Min(contribution, c.Resolve(StyleProperty.MaxWidth, -1, Unbounded)));
                    contribution += c.Resolve(StyleProperty.MarginLeft, -1, 0) + c.Resolve(StyleProperty.MarginRight, -1, 0);
                }
                if (sums)
                {
                    total += contribution + (counted > 0 ? gap : 0);
                }
                else if (contribution > total)
                {
                    total = contribution;
                }
                counted++;
            }
            return total;
        }

        /// Offsets for `count` slots of the given sizes in `free` extra space,
        /// with `gap` between them, following justify-content. Uneven shares
        /// are rounded cumulatively so nothing is lost.
        private static void DistributeSpace(int free, int count, Justify justify, int gap, int[] sizes, int[] offsets)
        {
            int before = 0;
            int slots = 0;         // gaps that share `free`
            int leading = 0;       // of those, how many halves precede the first item (SpaceAround)
            if (free > 0)
            {
                if (justify == Justify.End) before = free;
                else if (justify == Justify.Center) before = free / 2;
                else if (justify == Justify.SpaceBetween) slots = count - 1;
                else if (justify == Justify.SpaceAround) { slots = count * 2; leading = 1; }
                else if (justify == Justify.SpaceEvenly) { slots = count + 1; leading = 1; }
            }
            if (slots <= 0 && justify == Justify.SpaceBetween && free > 0)
            {
                before = 0;   // a single item stays at the start
            }

            int position = before;
            for (int k = 0; k < count; k++)
            {
                if (slots > 0)
                {
                    int shareIndex;
                    if (justify == Justify.SpaceAround) shareIndex = 2 * k + leading;
                    else shareIndex = k + leading;
                    int shared = Mathf.RoundToInt((float)free * shareIndex / slots);
                    offsets[k] = position + shared;
                }
                else
                {
                    offsets[k] = position;
                }
                position += sizes[k] + gap;
            }
        }

        // ================================================================
        // Child collection (display: contents / none / absolute)
        // ================================================================

        /// In-flow children, with `display: contents` elements replaced by
        /// their own children (their rectangle is set to the parent's content
        /// box when arranging).
        private void CollectFlow(Element e, int firstChild, List<Element> into, bool arrange, int originX, int originY,
            int innerWidth, int innerHeight, int clipLeft, int clipTop, int clipRight, int clipBottom)
        {
            for (int i = firstChild; i < e.children.Count; i++)
            {
                Element child = e.children[i];
                ComputedStyle c = child.computed;
                DisplayMode display = c.Display;
                if (display == DisplayMode.None || child.layoutPaused)
                {
                    continue;
                }
                if (display == DisplayMode.Contents)
                {
                    if (arrange)
                    {
                        child.x = originX;
                        child.y = originY;
                        child.width = Mathf.Max(0, innerWidth);
                        child.height = Mathf.Max(0, innerHeight);
                        child.clipLeft = clipLeft;
                        child.clipTop = clipTop;
                        child.clipRight = clipRight;
                        child.clipBottom = clipBottom;
                        child.layoutDirty = false;
                        child.hasArranged = true;
                        child.incomingGroup = e.childGroup;
                        child.group = e.childGroup;
                        child.childGroup = e.childGroup;
                    }
                    CollectFlow(child, 0, into, arrange, originX, originY, innerWidth, innerHeight, clipLeft, clipTop, clipRight, clipBottom);
                    continue;
                }
                if (c.Position == PositionMode.Absolute)
                {
                    continue;
                }
                into.Add(child);
            }
        }

        /// How far content being prepared out of sight (inert, see
        /// BackgroundWork) reaches below / right of a padding box's origin.
        private static void PreparedExtent(Element e, int originX, int originY, ref int width, ref int height)
        {
            for (int i = 0; i < e.children.Count; i++)
            {
                Element child = e.children[i];
                if (child.inert && child.loading && child.hasArranged)
                {
                    width = Mathf.Max(width, child.x + child.width - originX);
                    height = Mathf.Max(height, child.y + child.height - originY);
                }
                else if (child.computed.Display == DisplayMode.Contents)
                {
                    PreparedExtent(child, originX, originY, ref width, ref height);
                }
            }
        }

        /// The absolutely positioned children (through `display: contents`);
        /// null when there are none, which is the common case.
        private static List<Element> CollectAbsolutes(Element e, List<Element> into, int firstChild)
        {
            for (int i = firstChild; i < e.children.Count; i++)
            {
                Element child = e.children[i];
                DisplayMode display = child.computed.Display;
                if (display == DisplayMode.None || child.layoutPaused)
                {
                    continue;
                }
                if (display == DisplayMode.Contents)
                {
                    into = CollectAbsolutes(child, into, 0);
                    continue;
                }
                if (child.computed.Position == PositionMode.Absolute)
                {
                    if (into == null)
                    {
                        into = new List<Element>();
                    }
                    into.Add(child);
                }
            }
            return into;
        }
    }

    /// Working arrays for one level of LayoutChildren, grown as needed.
    internal sealed class LayoutScratch
    {
        internal readonly List<Element> items = new List<Element>();
        internal int capacity = -1;
        internal int[] marginMainStart, marginMainEnd, marginCrossStart, marginCrossEnd;
        internal int[] hypothetical, mainSize, crossSize, minimumMain, maximumMain;
        internal float[] grow, shrink;
        internal bool[] stretch;
        internal int[] lineStarts, lineCross, lineMainUsed, lineOffset, slotSizes, slotOffsets;
        internal int[] flexBase, violation, crossOuter;
        internal bool[] frozen;

        internal void Ensure(int count)
        {
            if (count <= capacity)
            {
                return;
            }
            int size = Mathf.Max(8, Mathf.Max(count, capacity * 2));
            capacity = size;
            marginMainStart = new int[size];
            marginMainEnd = new int[size];
            marginCrossStart = new int[size];
            marginCrossEnd = new int[size];
            hypothetical = new int[size];
            mainSize = new int[size];
            crossSize = new int[size];
            minimumMain = new int[size];
            maximumMain = new int[size];
            grow = new float[size];
            shrink = new float[size];
            stretch = new bool[size];
            lineStarts = new int[size + 1];
            lineCross = new int[size];
            lineMainUsed = new int[size];
            lineOffset = new int[size];
            slotSizes = new int[size];
            slotOffsets = new int[size];
            flexBase = new int[size];
            violation = new int[size];
            crossOuter = new int[size];
            frozen = new bool[size];
        }
    }
}
