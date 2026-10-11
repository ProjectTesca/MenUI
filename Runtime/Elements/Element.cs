using System.Collections.Generic;

namespace MenUI
{
    /// Pseudo-class states that select style variants.
    public static class PseudoState
    {
        public const int Hover = 1;
        public const int Active = 2;
        public const int Focus = 4;
        public const int Disabled = 8;
        /// The element being dragged (`:dragging`).
        public const int Dragging = 16;
        /// A drop target with an acceptable payload over it (`:drag-over`).
        public const int DragOver = 32;
        /// Where a newly shown element starts from (`:enter-from`, like CSS
        /// `@starting-style`): its first style is computed with this state
        /// on, and its transitions carry it from there to its real style.
        public const int EnterFrom = 64;
        /// Where a leaving element goes (`:exit-to`): when the region that
        /// shows it removes it, it gets this state and stays until its
        /// transitions have played.
        public const int ExitTo = 128;
    }

    /// A node of the UI tree. Layout sees every element as a rectangle;
    /// painting lets each element emit several primitives in paint phases.
    public abstract class Element : IStyleTarget
    {
        internal readonly MenUIContext context;
        internal Element parent;
        internal readonly List<Element> children = new List<Element>();

        // Styles: inline, then the variants of the states that are active.
        internal readonly Style inlineStyle = new Style();
        internal Style hoverStyle;
        internal Style activeStyle;
        internal Style focusStyle;
        internal Style disabledStyle;
        internal Style draggingStyle;
        internal Style dragOverStyle;
        internal Style enterFromStyle;
        internal Style exitToStyle;
        internal DragSourceConfig dragSource;
        internal DropTargetConfig dropTarget;
        internal int pseudoStates;

        // Style sheet matching (see StyleSheet): tag, classes, and the rules
        // matched when the element was attached or its classes changed.
        internal string tag;
        internal List<string> classes;
        internal List<StyleRule> matchedRules;
        /// Pseudo-classes some matched rule depends on.
        internal int stateRuleMask;
        internal bool matchDirty = true;
        // How many pointers hover / press this element (see InputRouter).
        internal int hoverCount;
        internal int activeCount;
        internal PointerHandlers pointerHandlers;
        internal readonly ComputedStyle computed = new ComputedStyle();
        /// Its style has been resolved before (transitions start only from a
        /// style that was shown).
        internal bool styleResolvedOnce;
        /// Running style transitions (null: none so far).
        internal StyleAnimator animator;
        /// The reactive scope it was created in: removing that scope plays
        /// the element's exit (see MenUIContext.Leave).
        internal readonly Owner owner;
        internal bool registeredExit;

        // Dirty tracking.
        internal bool styleDirty = true;
        /// Attached (or re-attached) with descendants that may never have been
        /// resolved against this position: resolve the whole subtree.
        internal bool subtreeStyleDirty = true;
        internal bool queuedForStyle;
        internal int reconcileStamp;
        internal bool layoutDirty = true;

        // Layout results, in physical pixels relative to the panel.
        internal int x, y, width, height;
        internal int contentX, contentY;
        internal int clipLeft, clipTop, clipRight, clipBottom;

        // The last measurements (see LayoutEngine.TryCachedMeasure), reused
        // while nothing below changed. `measureOverflowed`: an entry that may
        // still matter to the parent was evicted, so the cache no longer lists
        // every question the parent asked (see ContentChanged).
        internal const int MeasureCacheSlots = 4;
        internal readonly int[] measureCache = new int[MeasureCacheSlots * LayoutEngine.CacheEntry];
        internal int measureCacheNext;
        internal bool measureOverflowed;
        /// Entries measured in an earlier pass than this describe content
        /// that has since changed: they are ignored, and evicting them is fine.
        internal int validSincePass;
        // The answers its parent got before the element was invalidated
        // (LayoutEngine.Invalidate): [availableWidth, exact, availableHeight,
        // width, height] per entry, and the min-content width (-1: not asked).
        // If they all still hold, the parent's layout cannot change.
        // `savedAnswerCount` -1: unknown, the change must climb.
        internal int[] savedAnswers;
        // A column's settled leading children (LayoutEngine, "stack"): the
        // first `stackCount` children were laid out as they still are, so a
        // layout that only added children after them starts at the first new
        // one; with the inputs they were laid out with.
        internal int stackCount;
        internal int stackWidth, stackGap, stackOriginX, stackOriginY, stackLineCross, stackGroup;
        internal int stackClipLeft, stackClipTop, stackClipRight, stackClipBottom;
        internal bool stackWidthDefinite;
        /// Its index among its parent's children while it is in that prefix,
        /// and the prefix up to it: total height (gaps included), widest
        /// outer width, number of in-flow items.
        internal int stackIndex = int.MaxValue;
        internal int stackEndMain, stackEndCross, stackEndItems;
        // Painting's counterpart (Painter, record ranges): whether what this
        // subtree draws is unchanged since it was painted, and a prefix of
        // children, unchanged and in place, that a range repaint steps over.
        // Positions are relative to the range owner's start.
        internal bool paintClean;
        internal int paintPrefix;
        internal int paintPrefixGeneration;
        internal Element paintPrefixOwner;
        internal int childrenRecordStart, childrenOrderStart;
        internal float paintedOpacity;
        internal bool paintedKeep, paintedInert;
        internal int paintIndex = int.MaxValue;
        internal int paintEndRecord, paintEndOrder;
        /// The range repaint that last listed it (Painter.rangeStamp).
        internal int paintStamp;
        internal int savedAnswerCount = -1;
        internal int savedMinContent = -1;
        // The last content height its parent asked for (a flex column's
        // automatic minimum): at which width, the answer, and when.
        internal int contentHeightWidth, contentHeightAnswer, contentHeightPass;
        internal int savedContentHeightWidth = -1, savedContentHeight;

        // What the last arrange was given, so an unchanged subtree can be
        // skipped and a relayout boundary rearranged in place.
        internal bool hasArranged;
        internal int arrangedContainingWidth;
        internal int incomingClipLeft, incomingClipTop, incomingClipRight, incomingClipBottom;

        // Transform groups (see TransformGroups): the group this element's
        // records belong to, the one its children get, and the group it owns
        // when it is translated (0: none).
        internal int group;
        internal int childGroup;
        internal int incomingGroup;
        internal int translateGroup;
        /// `translateGroup` was allocated for the style's transform or a fade
        /// (see MenUIContext.UpdateLayer), and is freed when no longer needed.
        internal bool styleLayer;
        internal bool layerCleanupRegistered;
        /// Being prepared out of sight (background loading): styled and laid
        /// out like the rest, but neither drawn nor hit, and changes inside
        /// never repaint the panel.
        internal bool inert;
        /// Left out of layout for now (like `display: none`, but its layout
        /// is kept): a background load lays its content out now and then,
        /// not every frame (see BackgroundWork).
        internal bool layoutPaused;
        /// A prepared box sits exactly on this element (its left, top and
        /// width; its height too with `matchAnchorHeight`), where it will be
        /// shown: showing it then moves nothing.
        internal Element anchor;
        internal bool matchAnchorHeight;
        /// Its content is being loaded right now (see BackgroundWork).
        internal bool loading;
        // A record range of its own (see Painter.RepaintRange): its subtree's
        // records, with spare room after them, painted again on their own
        // when something is added or removed inside — not the whole panel.
        internal bool ownsRecordRange;
        internal int recordRangeStart, recordRangeLength, recordRangeWanted;
        internal int orderRangeStart, orderRangeCount;
        internal int rangeGeneration = -1;
        internal bool rangeRequested;

        /// The nearest element (itself included) with a record range.
        internal Element RangeOwner()
        {
            for (Element e = this; e != null; e = e.parent)
            {
                if (e.ownsRecordRange)
                {
                    return e;
                }
            }
            return null;
        }

        internal bool InsidePausedLayout()
        {
            for (Element e = this; e != null; e = e.parent)
            {
                if (e.layoutPaused)
                {
                    return true;
                }
            }
            return false;
        }
        internal bool queuedForLayout;

        // Where this element's draw records went in the last full paint, so a
        // paint-only change rewrites just those (see Painter.Repaint).
        internal int paintGeneration = -1;
        internal int boxRecordStart, boxRecordCount;
        internal int contentRecordStart, contentRecordCount;
        /// During a full paint: its records may be copied from the last one.
        internal bool reusePaint;
        internal bool queuedForPaint;

        protected Element(MenUIContext context)
        {
            this.context = context;
            owner = context.runtime.currentOwner;
        }

        Style IStyleTarget.StyleForWriting { get { return inlineStyle; } }

        void IStyleTarget.StyleWritten(int property)
        {
            MarkStyleDirty();
        }

        public Element Parent { get { return parent; } }

        /// Border box in physical pixels (valid after layout).
        public int X { get { return x; } }
        public int Y { get { return y; } }
        public int Width { get { return width; } }
        public int Height { get { return height; } }

        // ---- tree ------------------------------------------------------------

        internal void AppendChild(Element child)
        {
            if (child.parent != null)
            {
                child.parent.RemoveChild(child);
            }
            child.parent = this;
            children.Add(child);
            MarkAttached(child);
            MarkLayoutDirty(false);
            MarkStructureChanged();
        }

        internal void InsertChild(int index, Element child)
        {
            if (child.parent != null)
            {
                child.parent.RemoveChild(child);
            }
            child.parent = this;
            if (index < stackCount) stackCount = index;
            if (index < paintPrefix) paintPrefix = index;
            children.Insert(index, child);
            MarkAttached(child);
            MarkLayoutDirty(false);
            MarkStructureChanged();
        }

        internal void RemoveChild(Element child)
        {
            if (children.Remove(child))
            {
                TruncateStack(child);
                child.stackIndex = int.MaxValue;
                if (child.paintIndex < paintPrefix) paintPrefix = child.paintIndex;
                child.paintIndex = int.MaxValue;
                child.parent = null;
                MarkLayoutDirty(false);
                MarkStructureChanged();
            }
        }

        /// Makes `ordered` the children, in that order: elements already here
        /// are moved (not re-attached, so their styles are kept), new ones
        /// attached, missing ones detached. Nothing happens when the order is
        /// unchanged.
        internal void ReplaceChildren(List<Element> ordered)
        {
            int kept = 0;   // leading children that stay where they are
            int shorter = ordered.Count < children.Count ? ordered.Count : children.Count;
            while (kept < shorter && ordered[kept] == children[kept])
            {
                kept++;
            }
            if (kept == ordered.Count && kept == children.Count)
            {
                return;
            }
            if (kept < stackCount) stackCount = kept;
            if (kept < paintPrefix) paintPrefix = kept;
            int stamp = ++context.reconcileStamp;
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].reconcileStamp = stamp;
            }
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i].reconcileStamp != stamp)
                {
                    children[i].parent = null;
                }
            }
            children.Clear();
            for (int i = 0; i < ordered.Count; i++)
            {
                Element child = ordered[i];
                if (child.parent != this)
                {
                    if (child.parent != null)
                    {
                        child.parent.RemoveChild(child);
                    }
                    child.parent = this;
                    MarkAttached(child);
                }
                children.Add(child);
            }
            MarkLayoutDirty(false);
            MarkStructureChanged();
        }

        internal void RemoveAllChildren()
        {
            stackCount = 0;
            paintPrefix = 0;
            for (int i = 0; i < children.Count; i++)
            {
                children[i].parent = null;
            }
            children.Clear();
            MarkLayoutDirty(false);
            MarkStructureChanged();
        }

        // ---- dirty flags ---------------------------------------------------

        /// The set or order of drawn things changed below this element: the
        /// whole panel is painted again — unless it is being prepared out of
        /// sight, where nothing is drawn yet.
        internal void MarkStructureChanged()
        {
            // One walk up: the nearest record range, whether it is out of
            // sight, and whether it is in the panel at all (a subtree being
            // built detached draws nothing; attaching it marks the parent).
            Element owner = null;
            bool inert = false;
            bool paused = false;
            Element top = this;
            for (Element e = this; e != null; e = e.parent)
            {
                if (owner == null && e.layoutPaused) paused = true;
                if (owner == null && e.ownsRecordRange) owner = e;
                if (e.inert) inert = true;
                top = e;
            }
            if (top != context.root && top != context.overlayRoot)
            {
                return;
            }
            MarkPaintChanged();
            if (owner != null)
            {
                // Content being loaded is not laid out yet: its range is
                // painted again when it is (BackgroundWork.ResumeLayout).
                if (!paused)
                {
                    context.RequestRangePaint(owner);   // only that range is painted again
                }
                return;
            }
            if (!inert)
            {
                context.paintStructureDirty = true;
            }
        }

        internal bool InsideInert()
        {
            for (Element e = this; e != null; e = e.parent)
            {
                if (e.inert)
                {
                    return true;
                }
            }
            return false;
        }

        internal void MarkStyleDirty()
        {
            styleDirty = true;
            if (!queuedForStyle)
            {
                queuedForStyle = true;
                context.styleQueue.Add(this);
            }
            context.needsStyle = true;
        }

        private void MarkAttached(Element child)
        {
            child.subtreeStyleDirty = true;
            child.MarkStyleDirty();
        }

        /// Invalidates this element's layout. The layout pass then works out
        /// how far up the change reaches (LayoutEngine.Run): it climbs the
        /// ancestors only while their size changes — an ancestor that still
        /// measures the same for everything its parent asked is laid out
        /// again in place, and nothing above it is touched — and stops at a
        /// relayout boundary (an element whose size cannot depend on what is
        /// inside it). `sizeMayChange` starts at the parent: a change to this
        /// element's own width, margin, flex factors or display affects the
        /// parent's layout even if it is a boundary.
        internal void MarkLayoutDirty(bool sizeMayChange)
        {
            LayoutEngine layout = context.layout;
            int nextPass = layout.NextPass;
            Element start = sizeMayChange && parent != null ? parent : this;
            if (sizeMayChange)
            {
                stackCount = 0;   // its own style may have changed how it stacks
            }
            if (start != this)
            {
                parent.TruncateStack(this);
                layoutDirty = true;
                validSincePass = nextPass;
                measureOverflowed = false;   // the parent measures afresh
            }
            layout.Invalidate(start, nextPass);
            if (!start.queuedForLayout)
            {
                start.queuedForLayout = true;
                context.layoutQueue.Add(start);
            }
            // No paint yet: a layout that moves something asks for one (content
            // being loaded, whose layout waits, draws nothing meanwhile).
            context.needsLayout = true;
        }

        /// Its size never depends on its content, so nothing outside it needs
        /// to be laid out again when its content changes:
        ///   - a pixel width and height (flex grow/shrink depend on the basis,
        ///     which is that size, and on siblings — not on this content),
        ///     except for a flex item that may shrink to its content-based
        ///     automatic minimum (set min-width/height, flex-shrink: 0 or
        ///     overflow: hidden to make it one), or
        ///   - absolutely positioned with each axis fixed by a pixel size or by
        ///     both insets (its box comes from the parent, and it never
        ///     affects the parent's size).
        internal bool IsLayoutBoundary()
        {
            ComputedStyle s = computed;
            if (s.Display == DisplayMode.Contents)
            {
                return false;
            }
            bool absolute = s.Position == PositionMode.Absolute;
            int auto = (int)LengthUnit.Auto;
            bool widthFixed = s.Unit(StyleProperty.Width) == (int)LengthUnit.Pixels
                || (absolute && s.Unit(StyleProperty.Width) == auto
                    && s.Unit(StyleProperty.Left) != auto && s.Unit(StyleProperty.Right) != auto);
            bool heightFixed = s.Unit(StyleProperty.Height) == (int)LengthUnit.Pixels
                || (absolute && s.Unit(StyleProperty.Height) == auto
                    && s.Unit(StyleProperty.Top) != auto && s.Unit(StyleProperty.Bottom) != auto);
            if (!widthFixed || !heightFixed)
            {
                return false;
            }
            // A flex item has a content-based minimum on the main axis
            // (`min-width/height: auto`) even with a pixel size; it matters
            // unless the item cannot shrink below its size.
            bool mayShrink = s.Number(StyleProperty.FlexShrink) > 0f || s.Unit(StyleProperty.FlexBasis) != auto;
            if (!absolute && mayShrink && !s.ClipsContent && !(this is ScrollView))
            {
                Element container = parent;
                while (container != null && container.computed.Display == DisplayMode.Contents)
                {
                    container = container.parent;
                }
                if (container != null && container.computed.Display == DisplayMode.Flex)
                {
                    Direction direction = (Direction)container.computed.Enum(StyleProperty.Direction);
                    bool row = direction == Direction.Row || direction == Direction.RowReverse;
                    if (s.Unit(row ? StyleProperty.MinWidth : StyleProperty.MinHeight) == auto)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// A leaf's content changed (new text). If it still measures the same
        /// for every size its parent asked about, only the leaf itself is laid
        /// out again (the parent's layout cannot change); otherwise the change
        /// climbs like any other.
        internal void ContentChanged()
        {
            MarkLayoutDirty(false);
        }

        /// What this element draws changes: it and its ancestors are no longer
        /// unchanged, and no ancestor's painted prefix reaches over it. Stops
        /// at an ancestor already marked (its own ancestors were then).
        internal void MarkPaintChanged()
        {
            paintClean = false;
            Element child = this;
            for (Element p = parent; p != null; p = p.parent)
            {
                if (child.paintIndex < p.paintPrefix)
                {
                    p.paintPrefix = child.paintIndex;
                }
                if (!p.paintClean)
                {
                    return;
                }
                p.paintClean = false;
                child = p;
            }
        }

        /// `child` may no longer be laid out as it was: the settled prefix
        /// of the children ends before it.
        internal void TruncateStack(Element child)
        {
            if (child.stackIndex < stackCount)
            {
                stackCount = child.stackIndex;
            }
        }

        internal void MarkPaintDirty()
        {
            if (paintGeneration != context.painter.Generation)
            {
                // Not drawn now (new, hidden, being loaded): nothing to paint
                // again. Whatever makes it drawn paints it (a structure change,
                // its ancestor, its record range).
                return;
            }
            MarkPaintChanged();
            if (!queuedForPaint)
            {
                queuedForPaint = true;
                context.paintQueue.Add(this);
            }
            context.needsPaint = true;
        }

        internal void MarkSubtreePaintDirty()
        {
            MarkPaintDirty();
            for (int i = 0; i < children.Count; i++)
            {
                children[i].MarkSubtreePaintDirty();
            }
        }

        // ---- states ------------------------------------------------------------

        internal void SetPseudoState(int state, bool on)
        {
            int next = on ? (pseudoStates | state) : (pseudoStates & ~state);
            if (next == pseudoStates)
            {
                return;
            }
            pseudoStates = next;
            if (VariantFor(state) != null || (stateRuleMask & state) != 0)
            {
                MarkStyleDirty();
            }
        }

        internal bool HasClass(string name)
        {
            return classes != null && classes.Contains(name);
        }

        internal void SetClass(string name, bool on)
        {
            if (on == HasClass(name))
            {
                return;
            }
            if (on)
            {
                if (classes == null)
                {
                    classes = new List<string>();
                }
                classes.Add(name);
            }
            else
            {
                classes.Remove(name);
            }
            ClassesChanged(name);
        }

        internal void SetTag(string value)
        {
            if (tag == value)
            {
                return;
            }
            tag = value;
            matchDirty = true;
            if (context.AnySheetUsesAncestorTags())
            {
                subtreeStyleDirty = true;
            }
            MarkStyleDirty();
        }

        /// Rules must be matched again: this element's, and its descendants'
        /// too when some rule names the class on an ancestor.
        private void ClassesChanged(string name)
        {
            matchDirty = true;
            if (context.IsAncestorClass(name))
            {
                subtreeStyleDirty = true;
            }
            MarkStyleDirty();
        }

        internal Style VariantFor(int state)
        {
            if (state == PseudoState.Hover) return hoverStyle;
            if (state == PseudoState.Active) return activeStyle;
            if (state == PseudoState.Focus) return focusStyle;
            if (state == PseudoState.Dragging) return draggingStyle;
            if (state == PseudoState.DragOver) return dragOverStyle;
            if (state == PseudoState.EnterFrom) return enterFromStyle;
            if (state == PseudoState.ExitTo) return exitToStyle;
            return disabledStyle;
        }

        internal Style GetOrCreateVariant(int state)
        {
            if (state == PseudoState.Hover) return hoverStyle ?? (hoverStyle = new Style());
            if (state == PseudoState.Active) return activeStyle ?? (activeStyle = new Style());
            if (state == PseudoState.Focus) return focusStyle ?? (focusStyle = new Style());
            if (state == PseudoState.Dragging) return draggingStyle ?? (draggingStyle = new Style());
            if (state == PseudoState.DragOver) return dragOverStyle ?? (dragOverStyle = new Style());
            if (state == PseudoState.EnterFrom) return enterFromStyle ?? (enterFromStyle = new Style());
            if (state == PseudoState.ExitTo) return exitToStyle ?? (exitToStyle = new Style());
            return disabledStyle ?? (disabledStyle = new Style());
        }

        // ---- hooks for leaf elements -------------------------------------------

        /// Size of the content box's content for leaf elements (text, images).
        /// `availableWidth` is -1 when unconstrained (max-content).
        internal virtual void MeasureContent(int availableWidth, out int contentWidth, out int contentHeight)
        {
            contentWidth = 0;
            contentHeight = 0;
        }

        /// The same, knowing whether the width is imposed and the content
        /// height set (-1: not): what an image needs to keep its aspect ratio.
        internal virtual void MeasureContent(int availableWidth, bool widthDefinite, int innerHeight,
            out int contentWidth, out int contentHeight)
        {
            MeasureContent(availableWidth, out contentWidth, out contentHeight);
        }

        /// Min-content width of a leaf's content (the narrowest it can be
        /// without overflowing: its longest unbreakable run).
        internal virtual int MinContentWidthOfContent()
        {
            return 0;
        }

        // Cached min-content width (border box), valid like the measure cache.
        internal int minContentWidth;
        internal int minContentPass;

        /// Called once the final content box is known.
        internal virtual void ArrangeContent(int contentWidth, int contentHeight)
        {
        }

        /// Paint phase 4 (content) for leaf elements.
        internal virtual void PaintContent(Painter painter, float opacity)
        {
        }

        internal virtual bool IsLeaf { get { return false; } }
    }

    /// A container: CSS `div`. Block layout by default, flexbox with `.Flex()`.
    public sealed class Box : Element
    {
        internal Box(MenUIContext context) : base(context)
        {
            tag = "div";
        }

        public Box Children(params Element[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != null)
                {
                    AppendChild(items[i]);
                }
            }
            return this;
        }

        public Box Child(Element item)
        {
            if (item != null)
            {
                AppendChild(item);
            }
            return this;
        }
    }

    /// A placeholder whose children are swapped by a reactive condition
    /// (`Show`, `Dynamic`). It lays out as `display: contents`, so it adds no
    /// box of its own.
    public sealed class Region : Element
    {
        internal Owner branchOwner;

        internal Region(MenUIContext context) : base(context)
        {
            inlineStyle.SetEnum(StyleProperty.Display, (int)DisplayMode.Contents);
        }
    }
}
