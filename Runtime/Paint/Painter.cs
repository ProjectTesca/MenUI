// Turns the laid-out tree into draw records, in CSS painting order.
//
// Order key: (stacking context, z-index, tree order, paint phase).
//   An element with a z-index starts a stacking context: its subtree paints
//   as one unit, placed among the contexts of its nearest ancestor context
//   by z-index (stable, so equal values keep tree order). Inside a context:
//     its own phases 0-3 → child contexts with z < 0 → the in-flow
//     descendants (each: phases 0-3, content, children) → child contexts
//     with z >= 0.
//   Paint phases of one element:
//     0 outer shadows  1 background  2 inset shadows  3 border  4 content
//   The overlay root paints after everything, outside every clip.
//
// Incremental: a full paint remembers, per element, which records its box
// (phases 0-3) and its content went to. A later change that only alters
// how an element looks rewrites exactly those records (Repaint). If the
// element now needs a different number of records — or the tree, display
// or z-index changed — everything is painted again.
//
// Opacity is multiplied into each primitive (group opacity — rendering the
// subtree offscreen first — is not done yet), so overlapping children of a
// translucent element show through each other. A fully transparent subtree
// is skipped — unless its opacity transitions: then it keeps its records at
// zero alpha, so fading in and out rewrites records instead of adding and
// dropping them (a full paint).

using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    public sealed class Painter
    {
        internal const float KindRoundedRectangle = 1f;
        internal const float KindGlyph = 2f;
        internal const float KindOuterShadow = 3f;
        internal const float KindInsetShadow = 4f;
        internal const float KindImage = 5f;   // reserved: images and custom-shader surfaces
        internal const float KindRipple = 6f;
        internal const float KindShape = 7f;
        internal const float KindArc = 8f;
        internal const float KindWave = 9f;
        /// The record's kind texel holds kind + GroupStride * transform group.
        internal const float GroupStride = 16f;

        private RecordBuffer renderer;   // swapped only by Verify (tests)
        private int generation;
        private int previousGeneration;
        /// Elements drawn by the last full paint carry this paintGeneration.
        internal int Generation { get { return generation; } }
        // Every element whose box was painted, in paint order (bottom first),
        // published for hit testing (MenUIContext.paintOrder).
        private List<Element> paintOrder;
        /// For diagnostics: whether the last Paint rebuilt every record, and
        /// how many elements it rewrote otherwise.
        internal bool lastPaintWasFull;
        internal int lastRepaintedCount;
        /// Painting inside a subtree whose opacity transitions: keep records
        /// even at zero alpha.
        private bool keepTransparent;
        /// Painting a prepared box (inert, with a GPU group at zero opacity):
        /// its records are made ahead, but nothing in it can be hit.
        private bool insideInert;

        internal Painter(RecordBuffer records)
        {
            renderer = records;
        }

        internal RecordBuffer records { get { return renderer; } }

        /// Updates the records for everything queued; returns after a full
        /// paint or after rewriting just the queued elements' records.
        internal void Paint(MenUIContext context)
        {
            List<Element> queue = context.paintQueue;
            bool repainted = !context.paintStructureDirty && generation > 0 && RepaintRanges(context) && Repaint(queue);
            lastPaintWasFull = !repainted;
            lastRepaintedCount = queue.Count;
            if (!repainted)
            {
                PaintAll(context);
            }
            for (int i = 0; i < queue.Count; i++)
            {
                queue[i].queuedForPaint = false;
            }
            queue.Clear();
            List<Element> requests = context.rangeRequests;
            for (int i = 0; i < requests.Count; i++)
            {
                requests[i].rangeRequested = false;
            }
            requests.Clear();
            context.paintStructureDirty = false;
        }

#if MENUI_HEADLESS
        /// Tests: whether the records are exactly what painting now would make
        /// — each drawn element's records recomputed and compared, in paint
        /// order, and every other record empty (nothing stale drawn). Null
        /// when all is well, otherwise what is wrong.
        internal string Verify(MenUIContext context)
        {
            RecordBuffer live = renderer;
            RecordBuffer scratch = new RecordBuffer(live.capacity);
            bool[] covered = new bool[live.capacity];
            int lastStart = -1;
            string problem = null;
            for (int i = 0; i < paintOrder.Count && problem == null; i++)
            {
                Element e = paintOrder[i];
                if (e.paintGeneration != generation)
                {
                    continue;   // past the capacity
                }
                if (e.boxRecordStart < lastStart)
                {
                    problem = "records out of paint order at " + e.tag;
                    break;
                }
                lastStart = e.boxRecordStart;
                float opacity = AncestorOpacity(e) * OwnOpacity(e);
                keepTransparent = FadesInTree(e);
                renderer = scratch;
                scratch.BeginRecords();
                if (opacity > 0f || keepTransparent) PaintBox(e, opacity);
                int boxCount = scratch.RecordCount;
                if (opacity > 0f || keepTransparent) e.PaintContent(this, opacity);
                int contentCount = scratch.RecordCount - boxCount;
                renderer = live;
                keepTransparent = false;
                problem = Same(live, e.boxRecordStart, e.boxRecordCount, scratch, 0, boxCount, e.tag + " box")
                    ?? Same(live, e.contentRecordStart, e.contentRecordCount, scratch, boxCount, contentCount, e.tag + " content");
                for (int r = 0; r < e.boxRecordCount; r++) covered[e.boxRecordStart + r] = true;
                for (int r = 0; r < e.contentRecordCount; r++) covered[e.contentRecordStart + r] = true;
            }
            renderer = live;
            for (int r = 0; r < live.RecordCount && problem == null; r++)
            {
                if (!covered[r] && live.pixels[r * RecordBuffer.TexelsPerRecord + 4].r > 0.5f)
                {
                    problem = "record " + r + " is drawn but belongs to nothing";
                }
            }
            return problem;
        }

        private static string Same(RecordBuffer live, int liveStart, int liveCount, RecordBuffer expected, int start, int count, string what)
        {
            if (liveCount != count)
            {
                return what + ": " + liveCount + " records, expected " + count;
            }
            for (int i = 0; i < count * RecordBuffer.TexelsPerRecord; i++)
            {
                if (live.pixels[liveStart * RecordBuffer.TexelsPerRecord + i] != expected.pixels[start * RecordBuffer.TexelsPerRecord + i])
                {
                    return what + ": texel " + i + " differs";
                }
            }
            return null;
        }
#endif

        // ---- record ranges ------------------------------------------------------------

        // A range repaint in progress: its owner, and the paint order being
        // rewritten in place (entries past the old end are inserted at the end).
        private Element rangeOwner;
        private bool orderInPlace;
        private int orderCursor, orderEnd;
        private readonly List<Element> orderInserts = new List<Element>();
        // Stacking contexts met (and left to their context) while painting flow.
        private int contextsPassed;
        /// Settled prefixes stepped over (diagnostics and tests).
        internal int prefixSkips;

        // Each range repaint's number, and the elements it took out of the
        // paint order: those it did not draw again are no longer drawn.
        private int rangeStamp;
        private readonly List<Element> displaced = new List<Element>();

        private void AddOrder(Element e)
        {
            if (!orderInPlace)
            {
                paintOrder.Add(e);
                return;
            }
            e.paintStamp = rangeStamp;
            if (orderCursor < orderEnd)
            {
                Element old = paintOrder[orderCursor];
                if (old != e)
                {
                    displaced.Add(old);
                }
                paintOrder[orderCursor] = e;
                orderCursor++;
            }
            else
            {
                orderInserts.Add(e);
            }
        }

        private bool RepaintRanges(MenUIContext context)
        {
            List<Element> requests = context.rangeRequests;
            for (int i = 0; i < requests.Count; i++)
            {
                if (!RepaintRange(requests[i], context))
                {
                    return false;
                }
            }
            return true;
        }

        /// Paints a record range again on its own: its elements in paint
        /// order (unchanged ones copied over), then empty records up to its
        /// end. False when it no longer fits (a full paint then gives it
        /// more room) or it was not painted as a range last time.
        private bool RepaintRange(Element owner, MenUIContext context)
        {
            if (owner.rangeGeneration != generation || owner.paintGeneration != generation || !context.IsAttached(owner)
                || owner.computed.Display == DisplayMode.None || !CreatesContext(owner))
            {
                return false;
            }
            int savedPrevious = previousGeneration;
            previousGeneration = generation;
            keepTransparent = FadesInTree(owner);
            insideInert = owner.InsideInert();
            // Its records and its part of the paint order (hit testing) are
            // rewritten in place: what is unchanged and where it was is
            // stepped over (see PaintFlowChildren).
            rangeOwner = owner;
            orderInPlace = true;
            rangeStamp++;
            displaced.Clear();
            orderCursor = owner.orderRangeStart;
            orderEnd = owner.orderRangeStart + owner.orderRangeCount;
            orderInserts.Clear();
            renderer.BeginRange(owner.recordRangeStart, owner.recordRangeLength);
            PaintContextContent(owner, AncestorOpacity(owner) * OwnOpacity(owner));
            bool fits = renderer.EndRange();
            bool hittable = !insideInert;
            keepTransparent = false;
            insideInert = false;
            rangeOwner = null;
            orderInPlace = false;
            previousGeneration = savedPrevious;
            int newCount;
            if (orderInserts.Count > 0)
            {
                paintOrder.InsertRange(orderEnd, orderInserts);
                newCount = orderEnd - owner.orderRangeStart + orderInserts.Count;
                orderInserts.Clear();
            }
            else
            {
                for (int i = orderCursor; i < orderEnd; i++)
                {
                    displaced.Add(paintOrder[i]);
                }
                if (orderCursor < orderEnd)
                {
                    paintOrder.RemoveRange(orderCursor, orderEnd - orderCursor);
                }
                newCount = orderCursor - owner.orderRangeStart;
            }
            // Hidden or removed since: their old record slots now belong to
            // others, so nothing may be rewritten there in their name.
            for (int i = 0; i < displaced.Count; i++)
            {
                if (displaced[i].paintStamp != rangeStamp)
                {
                    displaced[i].paintGeneration = -1;
                }
            }
            displaced.Clear();
            if (!fits)
            {
                owner.recordRangeWanted = owner.recordRangeLength * 2;
                return false;
            }
            int delta = newCount - owner.orderRangeCount;
            List<Element> owners = context.rangeOwners;
            for (int i = 0; i < owners.Count; i++)
            {
                Element other = owners[i];
                if (other != owner && other.rangeGeneration == generation && other.orderRangeStart > owner.orderRangeStart)
                {
                    other.orderRangeStart += delta;
                }
            }
            owner.orderRangeCount = newCount;
            if (hittable || delta != 0)
            {
                context.hitVersion++;   // out of sight, nothing in it can be hit yet (Reveal says when)
            }
            return true;
        }

        /// In a full paint: the owner's records, then room to grow.
        private void PaintOwnedRange(Element owner, float opacity)
        {
            int start = renderer.RecordCount;
            int orderStart = paintOrder.Count;
            PaintContextContent(owner, opacity);
            int used = renderer.RecordCount - start;
            int room = Mathf.Max(Mathf.Max(64, used / 2), owner.recordRangeWanted - used);
            renderer.AppendEmpty(room);
            owner.recordRangeStart = start;
            owner.recordRangeLength = renderer.RecordCount - start;
            owner.orderRangeStart = orderStart;
            owner.orderRangeCount = paintOrder.Count - orderStart;
            owner.rangeGeneration = renderer.Overflowed ? -1 : generation;
        }

        private bool Repaint(List<Element> queue)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                Element e = queue[i];
                if (e.paintGeneration != generation)
                {
                    // Not drawn last time (hidden, detached, in a transparent
                    // subtree). Whatever would make it drawn now changes an
                    // ancestor's records or the structure too.
                    continue;
                }
                float opacity = AncestorOpacity(e) * OwnOpacity(e);
                keepTransparent = FadesInTree(e);
                bool draw = opacity > 0f || keepTransparent;
                renderer.BeginOverwrite(e.boxRecordStart, e.boxRecordCount);
                if (draw)
                {
                    PaintBox(e, opacity);
                }
                if (!renderer.EndOverwrite())
                {
                    keepTransparent = false;
                    return false;
                }
                renderer.BeginOverwrite(e.contentRecordStart, e.contentRecordCount);
                if (draw)
                {
                    e.PaintContent(this, opacity);
                }
                keepTransparent = false;
                if (!renderer.EndOverwrite())
                {
                    return false;
                }
            }
            return true;
        }

        /// Paints every record again, in order. An element drawn by the last
        /// full paint and not changed since (not queued) has its records
        /// copied over in bulk instead of computed: a structural change (an
        /// element added or removed) costs little more than the change.
        private void PaintAll(MenUIContext context)
        {
            previousGeneration = generation;
            generation++;
            renderer.BeginRecords();
            paintOrder = context.paintOrder;
            paintOrder.Clear();
            context.hitVersion++;
            Element root = context.root;
            Element overlayRoot = context.overlayRoot;
            keepTransparent = false;
            insideInert = false;
            if (root.computed.Display != DisplayMode.None)
            {
                PaintContext(root, OwnOpacity(root));
            }
            if (overlayRoot.computed.Display != DisplayMode.None)
            {
                PaintContext(overlayRoot, OwnOpacity(overlayRoot));
            }
        }

        /// Its opacity transitions (so it may be fading).
        private static bool Fades(Element e)
        {
            return e.computed.TransitionOf(StyleProperty.Opacity) != null;
        }

        private static bool FadesInTree(Element e)
        {
            for (Element a = e; a != null; a = a.parent)
            {
                if (Fades(a))
                {
                    return true;
                }
            }
            return false;
        }

        private void PaintOwnBox(Element e, float opacity)
        {
            e.reusePaint = !e.queuedForPaint && e.paintGeneration == previousGeneration && previousGeneration > 0;
            int start = renderer.RecordCount;
            if (e.reusePaint)
            {
                renderer.CopyPrevious(e.boxRecordStart, e.boxRecordCount);
            }
            else
            {
                PaintBox(e, opacity);
            }
            e.boxRecordStart = start;
            e.boxRecordCount = renderer.RecordCount - start;
            if (renderer.Overflowed)
            {
                // Past the capacity: not drawn, not hit, never repainted in place.
                e.paintGeneration = -1;
                return;
            }
            e.paintGeneration = generation;
            // Prepared boxes are listed too (hit testing skips them while
            // they are out of sight), so showing one needs no new paint order.
            AddOrder(e);
        }

        /// A box out of sight is skipped — unless it is prepared with a GPU
        /// group of its own (drawn at zero opacity: its records exist before
        /// it is shown).
        private static bool SkipsInert(Element e)
        {
            return e.inert && e.translateGroup == TransformGroups.Root;
        }

        private void PaintOwnContent(Element e, float opacity)
        {
            int start = renderer.RecordCount;
            if (e.reusePaint)
            {
                renderer.CopyPrevious(e.contentRecordStart, e.contentRecordCount);
            }
            else
            {
                e.PaintContent(this, opacity);
            }
            e.contentRecordStart = start;
            e.contentRecordCount = renderer.RecordCount - start;
            if (renderer.Overflowed)
            {
                e.paintGeneration = -1;
            }
        }

        private static bool CreatesContext(Element e)
        {
            return e.computed.HasZIndex && e.computed.Display != DisplayMode.Contents;
        }

        private void PaintContext(Element context, float opacity)
        {
            bool saved = keepTransparent;
            bool savedInert = insideInert;
            keepTransparent = FadesInTree(context);
            insideInert = context.InsideInert();
            if (opacity > 0f || keepTransparent)
            {
                if (context.ownsRecordRange)
                {
                    PaintOwnedRange(context, opacity);
                }
                else
                {
                    PaintContextContent(context, opacity);
                }
            }
            keepTransparent = saved;
            insideInert = savedInert;
        }

        private void PaintContextContent(Element context, float opacity)
        {
            PaintOwnBox(context, opacity);

            List<Element> contexts = new List<Element>();
            CollectContexts(context, contexts, generation);
            SortByZIndex(contexts);

            int index = 0;
            for (; index < contexts.Count && contexts[index].computed.ZIndex < 0; index++)
            {
                Element child = contexts[index];
                PaintContext(child, AncestorOpacity(child) * OwnOpacity(child));
            }
            PaintOwnContent(context, opacity);
            PaintFlowChildren(context, opacity);
            for (; index < contexts.Count; index++)
            {
                Element child = contexts[index];
                PaintContext(child, AncestorOpacity(child) * OwnOpacity(child));
            }
            context.paintClean = true;
        }

        private void PaintFlow(Element e, float opacity)
        {
            bool saved = keepTransparent;
            if (Fades(e))
            {
                keepTransparent = true;
            }
            if (opacity > 0f || keepTransparent)
            {
                PaintOwnBox(e, opacity);
                PaintOwnContent(e, opacity);
                PaintFlowChildren(e, opacity);
            }
            keepTransparent = saved;
            e.paintClean = true;
        }

        private void PaintFlowChildren(Element e, float opacity)
        {
            int first = 0;
            bool extending = false;
            if (rangeOwner != null)
            {
                // Inside a range repaint: the leading children that are
                // unchanged, context-free and where they were last time are
                // stepped over — records and paint order entries stay put.
                int recordHere = renderer.RecordCount - rangeOwner.recordRangeStart;
                int orderHere = orderCursor - rangeOwner.orderRangeStart;
                if (e.paintPrefix > 0 && e.paintPrefixGeneration == generation && e.paintPrefixOwner == rangeOwner
                    && e.childrenRecordStart == recordHere && e.childrenOrderStart == orderHere && orderInserts.Count == 0
                    && e.paintedOpacity == opacity && e.paintedKeep == keepTransparent && e.paintedInert == insideInert)
                {
                    Element last = e.children[e.paintPrefix - 1];
                    int orderSteps = last.paintEndOrder - orderHere;
                    if (orderCursor + orderSteps <= orderEnd)
                    {
                        prefixSkips++;
                        renderer.SkipInRange(last.paintEndRecord - recordHere);
                        orderCursor += orderSteps;
                        first = e.paintPrefix;
                    }
                }
                if (first == 0)
                {
                    e.paintPrefix = 0;
                    e.paintPrefixGeneration = generation;
                    e.paintPrefixOwner = rangeOwner;
                    e.childrenRecordStart = recordHere;
                    e.childrenOrderStart = orderHere;
                    e.paintedOpacity = opacity;
                    e.paintedKeep = keepTransparent;
                    e.paintedInert = insideInert;
                }
                extending = e.paintPrefix == first;
            }
            for (int i = first; i < e.children.Count; i++)
            {
                Element child = e.children[i];
                if (child.computed.Display == DisplayMode.None || SkipsInert(child) || CreatesContext(child))
                {
                    if (CreatesContext(child)) contextsPassed++;
                    extending = false;
                    continue;
                }
                int contextsBefore = contextsPassed;
                bool saved = insideInert;
                insideInert = saved || child.inert;
                PaintFlow(child, opacity * OwnOpacity(child));
                insideInert = saved;
                if (extending)
                {
                    if (contextsPassed != contextsBefore || renderer.Overflowed || child.paintGeneration != generation)
                    {
                        extending = false;
                        continue;
                    }
                    child.paintIndex = i;
                    child.paintEndRecord = renderer.RecordCount - rangeOwner.recordRangeStart;
                    child.paintEndOrder = (orderInserts.Count > 0 ? orderEnd + orderInserts.Count : orderCursor) - rangeOwner.orderRangeStart;
                    e.paintPrefix = i + 1;
                }
            }
        }

        private static void CollectContexts(Element e, List<Element> into, int generation)
        {
            // A settled prefix holds no stacking context (see PaintFlowChildren).
            int first = e.paintPrefixGeneration == generation ? e.paintPrefix : 0;
            for (int i = first; i < e.children.Count; i++)
            {
                Element child = e.children[i];
                if (child.computed.Display == DisplayMode.None || SkipsInert(child))
                {
                    continue;
                }
                if (CreatesContext(child))
                {
                    into.Add(child);
                }
                else
                {
                    CollectContexts(child, into, generation);
                }
            }
        }

        /// Stable insertion sort; context lists are short.
        private static void SortByZIndex(List<Element> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                Element item = list[i];
                int z = item.computed.ZIndex;
                int j = i - 1;
                while (j >= 0 && list[j].computed.ZIndex > z)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = item;
            }
        }

        /// The opacity the records carry: none for an element with a
        /// transform group, whose opacity the GPU applies (see UpdateLayer).
        private static float OwnOpacity(Element e)
        {
            return e.translateGroup != TransformGroups.Root ? 1f : e.computed.Opacity;
        }

        private static float AncestorOpacity(Element e)
        {
            float opacity = 1f;
            for (Element a = e.parent; a != null; a = a.parent)
            {
                opacity *= OwnOpacity(a);
            }
            return opacity;
        }

        // ================================================================
        // Phases 0-3 of one element
        // ================================================================

        private void PaintBox(Element e, float opacity)
        {
            ComputedStyle s = e.computed;
            if (s.Display == DisplayMode.Contents || !s.IsVisible || e.width <= 0 || e.height <= 0)
            {
                return;
            }
            float x = e.x, y = e.y, width = e.width, height = e.height;
            Vector4 radii = CornerRadii(e);
            float radiusTopLeft = radii.x;
            float radiusTopRight = radii.y;
            float radiusBottomRight = radii.z;
            float radiusBottomLeft = radii.w;

            Shadow[] shadows = s.shadows;

            // Phase 0: outer shadows (the first listed is on top, so paint it last).
            if (shadows != null)
            {
                for (int i = shadows.Length - 1; i >= 0; i--)
                {
                    Shadow shadow = shadows[i];
                    if (shadow.Inset || shadow.Color.a <= 0f) continue;
                    float sigma = shadow.Blur * 0.5f;
                    float shapeX = x + shadow.OffsetX - shadow.Spread;
                    float shapeY = y + shadow.OffsetY - shadow.Spread;
                    float shapeWidth = width + shadow.Spread * 2f;
                    float shapeHeight = height + shadow.Spread * 2f;
                    if (shapeWidth <= 0f || shapeHeight <= 0f) continue;
                    float reach = Mathf.Ceil(sigma * 3f) + 1f;
                    int record = Begin(e, shapeX - reach, shapeY - reach, shapeWidth + reach * 2f, shapeHeight + reach * 2f);
                    if (record < 0) continue;
                    renderer.Texel(record, 2, Premultiplied(shadow.Color, opacity));
                    renderer.Texel(record, 3, radiusTopLeft, radiusTopRight, radiusBottomRight, radiusBottomLeft);
                    renderer.Texel(record, 4, KindOuterShadow + GroupStride * e.group, 0f, sigma, shadow.Spread);
                    renderer.Texel(record, 6, x, y, width, height);
                    renderer.Texel(record, 7, shapeX, shapeY, shapeWidth, shapeHeight);
                }
            }

            // Phases 1 and 3: background and border share one primitive.
            Color background = s.ColorOf(StyleProperty.Background);
            float borderTop = s.numbers[StyleProperty.BorderTopWidth];
            float borderRight = s.numbers[StyleProperty.BorderRightWidth];
            float borderBottom = s.numbers[StyleProperty.BorderBottomWidth];
            float borderLeft = s.numbers[StyleProperty.BorderLeftWidth];
            bool hasBorder = borderTop + borderRight + borderBottom + borderLeft > 0f
                && s.ColorOf(StyleProperty.BorderColor).a > 0f;
            bool hasInset = false;
            if (shadows != null)
            {
                for (int i = 0; i < shadows.Length; i++)
                {
                    if (shadows[i].Inset) hasInset = true;
                }
            }

            // An element whose look follows a state (a hover background) or
            // transitions keeps its record while transparent, so the change
            // rewrites that record instead of changing the record count (a
            // full paint).
            if (background.a > 0f || (hasBorder && !hasInset) || HasStateStyles(e))
            {
                int record = Begin(e, x, y, width, height);
                if (record >= 0)
                {
                    renderer.Texel(record, 2, Premultiplied(background, opacity));
                    renderer.Texel(record, 3, radiusTopLeft, radiusTopRight, radiusBottomRight, radiusBottomLeft);
                    renderer.Texel(record, 4, KindRoundedRectangle + GroupStride * e.group, 0f, 0f, 0f);
                    // With inset shadows the border is drawn after them (phase 3).
                    Color border = hasInset ? new Color(0f, 0f, 0f, 0f) : s.ColorOf(StyleProperty.BorderColor);
                    renderer.Texel(record, 5, Premultiplied(border, opacity));
                    if (hasInset) renderer.Texel(record, 6, 0f, 0f, 0f, 0f);
                    else renderer.Texel(record, 6, borderTop, borderRight, borderBottom, borderLeft);
                    renderer.Texel(record, 7, x, y, width, height);
                }
            }

            // Phase 2: inset shadows, then phase 3: the border on top of them.
            if (hasInset)
            {
                for (int i = shadows.Length - 1; i >= 0; i--)
                {
                    Shadow shadow = shadows[i];
                    if (!shadow.Inset || shadow.Color.a <= 0f) continue;
                    int record = Begin(e, x, y, width, height);
                    if (record < 0) continue;
                    renderer.Texel(record, 2, Premultiplied(shadow.Color, opacity));
                    renderer.Texel(record, 3, radiusTopLeft, radiusTopRight, radiusBottomRight, radiusBottomLeft);
                    renderer.Texel(record, 4, KindInsetShadow + GroupStride * e.group, 0f, shadow.Blur * 0.5f, shadow.Spread);
                    renderer.Texel(record, 6, x, y, width, height);
                    renderer.Texel(record, 7, x + shadow.OffsetX + shadow.Spread, y + shadow.OffsetY + shadow.Spread,
                        width - shadow.Spread * 2f, height - shadow.Spread * 2f);
                }
                if (hasBorder)
                {
                    int record = Begin(e, x, y, width, height);
                    if (record >= 0)
                    {
                        renderer.Texel(record, 2, 0f, 0f, 0f, 0f);
                        renderer.Texel(record, 3, radiusTopLeft, radiusTopRight, radiusBottomRight, radiusBottomLeft);
                        renderer.Texel(record, 4, KindRoundedRectangle + GroupStride * e.group, 0f, 0f, 0f);
                        renderer.Texel(record, 5, Premultiplied(s.ColorOf(StyleProperty.BorderColor), opacity));
                        renderer.Texel(record, 6, borderTop, borderRight, borderBottom, borderLeft);
                        renderer.Texel(record, 7, x, y, width, height);
                    }
                }
            }
        }

        /// The element's corner radii in physical px (tl, tr, br, bl), each at
        /// most half its shorter side. Animated radii may be fractional.
        internal static Vector4 CornerRadii(Element e)
        {
            ComputedStyle s = e.computed;
            float shorter = Mathf.Min(e.width, e.height);
            float half = shorter * 0.5f;
            return new Vector4(
                Mathf.Min(Corner(s, StyleProperty.CornerTopLeft, shorter), half),
                Mathf.Min(Corner(s, StyleProperty.CornerTopRight, shorter), half),
                Mathf.Min(Corner(s, StyleProperty.CornerBottomRight, shorter), half),
                Mathf.Min(Corner(s, StyleProperty.CornerBottomLeft, shorter), half));
        }

        private static float Corner(ComputedStyle s, int property, float reference)
        {
            int unit = s.units[property];
            if (unit == (int)LengthUnit.Pixels) return s.numbers[property];
            if (unit == (int)LengthUnit.Percent) return s.numbers[property] * 0.01f * reference;
            return 0f;
        }

        private static bool HasStateStyles(Element e)
        {
            return e.stateRuleMask != 0 || e.hoverStyle != null || e.activeStyle != null || e.focusStyle != null
                || e.disabledStyle != null || e.draggingStyle != null || e.dragOverStyle != null
                || e.enterFromStyle != null || e.exitToStyle != null
                || e.computed.TransitionOf(StyleProperty.Background) != null
                || e.computed.TransitionOf(StyleProperty.BorderColor) != null;
        }

        // ================================================================
        // Content primitives
        // ================================================================

        internal void Glyphs(Element e, ShapedText text, int originX, int originY, Color color, float opacity)
        {
            Glyphs(e, text, originX, originY, color, opacity, e.clipLeft, e.clipTop, e.clipRight, e.clipBottom);
        }

        /// Glyphs clipped to a rectangle of the element's own (already within
        /// its clip) — a text box clips its text to its content box.
        internal void Glyphs(Element e, ShapedText text, int originX, int originY, Color color, float opacity,
            int clipLeft, int clipTop, int clipRight, int clipBottom)
        {
            if (!e.computed.IsVisible)
            {
                return;
            }
            Color linear = Premultiplied(color, opacity);
            if (linear.a <= 0f && !keepTransparent)
            {
                return;
            }
            float[] glyphs = text.glyphs;
            for (int g = 0; g < text.glyphCount; g++)
            {
                int o = g * ShapedText.FloatsPerGlyph;
                float left = originX + glyphs[o];
                float top = originY + glyphs[o + 1];
                int record = Begin(left, top, glyphs[o + 2] - glyphs[o], glyphs[o + 3] - glyphs[o + 1],
                    clipLeft, clipTop, clipRight, clipBottom);
                if (record < 0) continue;
                renderer.Texel(record, 2, linear);
                renderer.Texel(record, 3, glyphs[o + 4], glyphs[o + 5], glyphs[o + 6], glyphs[o + 7]);
                renderer.Texel(record, 4, KindGlyph + GroupStride * e.group, glyphs[o + 8], 0f, 0f);
            }
        }

        // ================================================================
        // Graphics (see Graphic): ripples, shapes, arcs, lines. Each is one
        // record, drawn even when transparent (a steady record count).
        // Coordinates are physical px in layout space.
        // ================================================================

        /// A circle clipped to a rounded box (`box` x, y, width, height and
        /// its `radii`), with a soft edge of `soft` px.
        internal void Ripple(Element e, float centerX, float centerY, float radius, float soft,
            Vector4 box, Vector4 radii, Color color, float opacity)
        {
            int record = BeginSteady(e, box.x, box.y, box.z, box.w);
            if (record < 0) return;
            renderer.Texel(record, 2, Premultiplied(color, opacity));
            renderer.Texel(record, 3, radii.x, radii.y, radii.z, radii.w);
            renderer.Texel(record, 4, KindRipple + GroupStride * e.group, 0f, 0f, 0f);
            renderer.Texel(record, 6, centerX, centerY, Mathf.Max(0f, radius), soft);
            renderer.Texel(record, 7, box.x, box.y, box.z, box.w);
        }

        /// A shape of the morphing family filling the square centered in
        /// the rectangle: `from` morphed toward `to` by `morph` (0..1), the
        /// whole turned by `rotation` radians.
        internal void Shape(Element e, float x, float y, float width, float height, ShapeSpec from, ShapeSpec to,
            float morph, float rotation, Color color, float opacity)
        {
            int record = BeginSteady(e, x - 1f, y - 1f, width + 2f, height + 2f);
            if (record < 0) return;
            renderer.Texel(record, 2, Premultiplied(color, opacity));
            renderer.Texel(record, 3, from.kind, from.count, from.amount, from.rotation * Mathf.Deg2Rad);
            renderer.Texel(record, 4, KindShape + GroupStride * e.group, 0f, 0f, 0f);
            // A spring may overshoot a little past either shape.
            renderer.Texel(record, 5, Mathf.Clamp(morph, -0.3f, 1.3f), rotation, 0f, 0f);
            renderer.Texel(record, 6, to.kind, to.count, to.amount, to.rotation * Mathf.Deg2Rad);
            renderer.Texel(record, 7, x, y, width, height);
        }

        /// A stroked arc with round caps (angles in radians from the top,
        /// clockwise), optionally wavy.
        internal void Arc(Element e, float centerX, float centerY, float radius, float thickness, float start, float sweep,
            float amplitude, float waves, float phase, Color color, float opacity)
        {
            float reach = radius + Mathf.Abs(amplitude) + thickness * 0.5f + 1f;
            int record = BeginSteady(e, centerX - reach, centerY - reach, reach * 2f, reach * 2f);
            if (record < 0) return;
            renderer.Texel(record, 2, Premultiplied(color, opacity));
            renderer.Texel(record, 3, amplitude, waves, phase, 0f);
            renderer.Texel(record, 4, KindArc + GroupStride * e.group, 0f, 0f, 0f);
            renderer.Texel(record, 6, radius, thickness, start, sweep);
            renderer.Texel(record, 7, centerX - radius, centerY - radius, radius * 2f, radius * 2f);
        }

        /// A stroked horizontal line from x0 to x1 with round caps,
        /// optionally wavy (amplitude, wavelength, phase in radians).
        internal void Line(Element e, float x0, float x1, float y, float thickness,
            float amplitude, float wavelength, float phase, Color color, float opacity)
        {
            float reachX = thickness * 0.5f + 1f;
            float reachY = Mathf.Abs(amplitude) + thickness * 0.5f + 1f;
            int record = BeginSteady(e, x0 - reachX, y - reachY, (x1 - x0) + reachX * 2f, reachY * 2f);
            if (record < 0) return;
            renderer.Texel(record, 2, Premultiplied(color, opacity));
            renderer.Texel(record, 3, amplitude, wavelength, phase, 0f);
            renderer.Texel(record, 4, KindWave + GroupStride * e.group, 0f, 0f, 0f);
            renderer.Texel(record, 6, x0, x1, y, thickness);
        }

        /// Replays a Graphic's commands (logical px from its content box).
        internal void PaintGraphic(Graphic g, float opacity)
        {
            GraphicCanvas canvas = g.canvas;
            List<float> n = canvas.numbers;
            float scale = g.context.scale;
            float ox = g.contentX, oy = g.contentY;
            for (int i = 0; i < canvas.count; i++)
            {
                int o = i * GraphicCanvas.FloatsPerCommand;
                int kind = (int)n[o];
                Color color = canvas.colors[i];
                if (kind == GraphicCanvas.CommandRipple)
                {
                    Element clip = g.clipToParent && g.parent != null ? g.parent : g;
                    Vector4 box = new Vector4(clip.x, clip.y, clip.width, clip.height);
                    Ripple(g, ox + n[o + 1] * scale, oy + n[o + 2] * scale, n[o + 3] * scale, n[o + 4] * scale,
                        box, CornerRadii(clip), color, opacity);
                }
                else if (kind == GraphicCanvas.CommandShape)
                {
                    Shape(g, ox + n[o + 1] * scale, oy + n[o + 2] * scale, n[o + 3] * scale, n[o + 4] * scale,
                        canvas.shapes[i * 2], canvas.shapes[i * 2 + 1], n[o + 5], n[o + 6], color, opacity);
                }
                else if (kind == GraphicCanvas.CommandArc)
                {
                    Arc(g, ox + n[o + 1] * scale, oy + n[o + 2] * scale, n[o + 3] * scale, n[o + 4] * scale,
                        n[o + 5], n[o + 6], n[o + 7] * scale, n[o + 8], n[o + 9], color, opacity);
                }
                else if (kind == GraphicCanvas.CommandLine)
                {
                    Line(g, ox + n[o + 1] * scale, ox + n[o + 2] * scale, oy + n[o + 3] * scale, n[o + 4] * scale,
                        n[o + 5] * scale, n[o + 6] * scale, n[o + 7], color, opacity);
                }
                else
                {
                    float width = n[o + 3] * scale, height = n[o + 4] * scale;
                    float half = Mathf.Min(width, height) * 0.5f;
                    Box(g, new Vector4(ox + n[o + 1] * scale, oy + n[o + 2] * scale, width, height),
                        new Vector4(Mathf.Min(n[o + 5] * scale, half), Mathf.Min(n[o + 6] * scale, half),
                            Mathf.Min(n[o + 7] * scale, half), Mathf.Min(n[o + 8] * scale, half)), color, opacity);
                }
            }
        }

        /// A rounded rectangle (x, y, width, height, radii), no border.
        internal void Box(Element e, Vector4 box, Vector4 radii, Color color, float opacity)
        {
            int record = BeginSteady(e, box.x, box.y, box.z, box.w);
            if (record < 0) return;
            renderer.Texel(record, 2, Premultiplied(color, opacity));
            renderer.Texel(record, 3, radii.x, radii.y, radii.z, radii.w);
            renderer.Texel(record, 4, KindRoundedRectangle + GroupStride * e.group, 0f, 0f, 0f);
            renderer.Texel(record, 7, box.x, box.y, box.z, box.w);
        }

        /// Like Begin, but a record even when the quad is clipped away (an
        /// empty quad then): graphics keep a steady number of records while
        /// they move, so they repaint in place.
        private int BeginSteady(Element e, float x, float y, float width, float height)
        {
            if (x >= e.clipRight || y >= e.clipBottom || x + width <= e.clipLeft || y + height <= e.clipTop)
            {
                return Begin(e.clipLeft, e.clipTop, 0f, 0f, int.MinValue / 2, int.MinValue / 2, int.MaxValue / 2, int.MaxValue / 2);
            }
            return Begin(e, x, y, width, height);
        }

        /// Starts a record with its quad and the element's clip; -1 when the
        /// quad is clipped away entirely or the buffer is full.
        private int Begin(Element e, float x, float y, float width, float height)
        {
            return Begin(x, y, width, height, e.clipLeft, e.clipTop, e.clipRight, e.clipBottom);
        }

        private int Begin(float x, float y, float width, float height, int clipLeft, int clipTop, int clipRight, int clipBottom)
        {
            if (x >= clipRight || y >= clipBottom || x + width <= clipLeft || y + height <= clipTop)
            {
                return -1;
            }
            int record = renderer.AllocateRecord();
            if (record < 0)
            {
                return -1;
            }
            renderer.Texel(record, 0, x, y, width, height);
            renderer.Texel(record, 1, clipLeft, clipTop, clipRight, clipBottom);
            return record;
        }

#if MENUI_HEADLESS
        private static float ToLinear(float channel)
        {
            return channel <= 0.04045f ? channel / 12.92f : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }
#endif

        /// Linear color with opacity applied (the shader premultiplies).
        private static Color Premultiplied(Color color, float opacity)
        {
#if MENUI_HEADLESS
            // Tests run without Unity's native code: the same conversion in C#.
            Color linear = new Color(ToLinear(color.r), ToLinear(color.g), ToLinear(color.b), color.a);
#else
            Color linear = color.linear;
#endif
            linear.a = color.a * opacity;
            return linear;
        }
    }
}
