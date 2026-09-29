// Routes device-independent pointer samples (see PointerSource.cs) to
// elements: hit testing, :hover / :active, pointer capture and events.
//
// What it depends on, and nothing else:
//   - the paint order the painter publishes (MenUIContext.paintOrder),
//   - each element's rectangle, clip, pointer-events/visibility, handlers,
//   - Element.SetPseudoState to switch style variants.
// Styling and drawing never reference input.
//
// Semantics (following the DOM):
//   - the target is the topmost hittable element under the pointer;
//     :hover applies to it and all its ancestors;
//   - pointerdown captures the pointer: move/up go to the captured element
//     until release; :active applies to the pressed chain meanwhile;
//   - click fires on release when the pointer is still over the pressed
//     element (or inside it);
//   - pointerenter/leave do not bubble; the other events bubble to the
//     ancestors until StopPropagation.
//
// Hittable: pointer-events auto, visible, and either listening (a handler or
// a :hover/:active style) or opaque (a background or border), so a card or
// a modal backdrop blocks what is behind it. Plain text does not.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    /// A pointer event as handlers see it. Coordinates are logical pixels.
    public sealed class PointerEvent
    {
        public int PointerId { get; internal set; }
        /// Position on the panel.
        public float X { get; internal set; }
        public float Y { get; internal set; }
        /// Position relative to the border box of `CurrentTarget`.
        public float LocalX { get; internal set; }
        public float LocalY { get; internal set; }
        /// The element the event was dispatched to.
        public Element Target { get; internal set; }
        /// The element whose handler is running (differs while bubbling).
        public Element CurrentTarget { get; internal set; }
        public float ScrollDelta { get; internal set; }

        internal bool propagationStopped;
        internal bool clickCancelled;
        internal MenUIContext context;

        /// Where a drop at this position would go among `container`'s
        /// children (looking through Show/For regions): the number of
        /// children whose middle is before the pointer, along the
        /// container's flex direction (vertical for block layout).
        public int InsertionIndexIn(Element container)
        {
            if (container == null || context == null)
            {
                return 0;
            }
            Direction direction = (Direction)container.computed.Enum(StyleProperty.Direction);
            bool horizontal = container.computed.Display == DisplayMode.Flex
                && (direction == Direction.Row || direction == Direction.RowReverse);
            float pointer = (horizontal ? X : Y) * context.scale;
            return CountBefore(container, pointer, horizontal);
        }

        private int CountBefore(Element container, float pointer, bool horizontal)
        {
            int count = 0;
            TransformGroups groups = context.groups;
            for (int i = 0; i < container.children.Count; i++)
            {
                Element child = container.children[i];
                if (child is Region)
                {
                    count += CountBefore(child, pointer, horizontal);
                    continue;
                }
                float middle = horizontal ? groups.ScreenX(child.group, child.x + child.width * 0.5f)
                    : groups.ScreenY(child.group, child.y + child.height * 0.5f);
                if (middle < pointer)
                {
                    count++;
                }
            }
            return count;
        }

        public void StopPropagation()
        {
            propagationStopped = true;
        }

        /// This press will not end in a click (and loses :active) — for
        /// example because it turned into a scroll.
        public void CancelClick()
        {
            clickCancelled = true;
        }
    }

    /// An element's pointer handlers (multicast: `+=` adds one).
    internal sealed class PointerHandlers
    {
        internal Action<PointerEvent> down;
        internal Action<PointerEvent> up;
        internal Action<PointerEvent> click;
        internal Action<PointerEvent> move;
        internal Action<PointerEvent> enter;
        internal Action<PointerEvent> leave;
        internal Action<PointerEvent> scroll;
    }

    internal sealed class InputRouter
    {
        internal const int MaxPointers = 4;
        private const int Down = 0, Up = 1, Click = 2, Move = 3, Enter = 4, Leave = 5, Scroll = 6;
        private const float MoveThreshold = 0.25f;   // physical px

        private readonly MenUIContext context;
        private readonly PointerEvent pointerEvent = new PointerEvent();

        // Hittable elements in paint order (topmost last), rebuilt when the
        // paint order or the set of listeners changes.
        private readonly List<Element> hitList = new List<Element>();
        private int hitListVersion = -1;

        // Per pointer.
        private readonly Element[] target = new Element[MaxPointers];
        private readonly Element[] captured = new Element[MaxPointers];
        private readonly bool[] wasPressed = new bool[MaxPointers];
        private readonly bool[] wasOver = new bool[MaxPointers];
        private readonly float[] lastX = new float[MaxPointers];
        private readonly float[] lastY = new float[MaxPointers];
        private readonly List<Element>[] hoverChain = new List<Element>[MaxPointers];
        private readonly List<Element>[] activeChain = new List<Element>[MaxPointers];
        private readonly int[] slotPointerId = new int[MaxPointers];
        private readonly bool[] slotUsed = new bool[MaxPointers];
        private readonly bool[] slotSeen = new bool[MaxPointers];
        private readonly int[] testedLayoutVersion = new int[MaxPointers];
        private readonly bool[] clickCancelled = new bool[MaxPointers];

        // Drag and drop, per pointer.
        private const float DragThreshold = 6f;   // logical px
        private readonly Element[] dragCandidate = new Element[MaxPointers];
        private readonly bool[] dragging = new bool[MaxPointers];
        private readonly object[] dragPayload = new object[MaxPointers];
        private readonly Element[] dragMoved = new Element[MaxPointers];
        private readonly Owner[] previewOwner = new Owner[MaxPointers];
        private readonly int[] dragGroup = new int[MaxPointers];
        private readonly float[] pressX = new float[MaxPointers];
        private readonly float[] pressY = new float[MaxPointers];
        private readonly Element[] dropElement = new Element[MaxPointers];
        private readonly DropTargetConfig[] dropConfig = new DropTargetConfig[MaxPointers];
        private bool groupsMoved;

        internal InputRouter(MenUIContext context)
        {
            this.context = context;
            for (int i = 0; i < MaxPointers; i++)
            {
                hoverChain[i] = new List<Element>();
                activeChain[i] = new List<Element>();
            }
        }

        internal void Process(PointerSample[] samples, int count)
        {
            if (hitListVersion != context.hitVersion)
            {
                RebuildHitList();
            }
            if (context.groups.dirty)
            {
                context.groups.Resolve();
                groupsMoved = true;
            }
            for (int i = 0; i < MaxPointers; i++)
            {
                slotSeen[i] = false;
            }
            for (int s = 0; s < count; s++)
            {
                int slot = SlotFor(samples[s].PointerId);
                if (slot < 0)
                {
                    continue;
                }
                slotSeen[slot] = true;
                ProcessPointer(slot, samples[s]);
            }
            groupsMoved = false;
            // A pointer that stopped reporting has left.
            for (int slot = 0; slot < MaxPointers; slot++)
            {
                if (slotUsed[slot] && !slotSeen[slot])
                {
                    Release(slot, false, lastX[slot], lastY[slot]);
                    SetHover(slot, null, 0f, 0f);
                    slotUsed[slot] = false;
                }
            }
        }

        /// What pointer `slot` is over (null: nothing, or no such pointer).
        internal Element HoverTarget(int slot)
        {
            return slotUsed[slot] ? target[slot] : null;
        }

        private int SlotFor(int pointerId)
        {
            int free = -1;
            for (int i = 0; i < MaxPointers; i++)
            {
                if (slotUsed[i] && slotPointerId[i] == pointerId) return i;
                if (!slotUsed[i] && free < 0) free = i;
            }
            if (free >= 0)
            {
                slotUsed[free] = true;
                slotPointerId[free] = pointerId;
                wasPressed[free] = false;
                wasOver[free] = false;
            }
            return free;
        }

        private void ProcessPointer(int slot, PointerSample sample)
        {
            bool over = sample.IsOverPanel;
            float x = sample.U * context.bufferWidth;
            float y = (1f - sample.V) * context.bufferHeight;
            bool moved = over && (!wasOver[slot] || Mathf.Abs(x - lastX[slot]) > MoveThreshold || Mathf.Abs(y - lastY[slot]) > MoveThreshold);
            bool pressed = sample.PrimaryPressed;
            bool changedPress = pressed != wasPressed[slot];

            // Test again when the pointer moved, or the layout moved things under it.
            if (moved || changedPress || !over || groupsMoved || testedLayoutVersion[slot] != context.layoutVersion)
            {
                testedLayoutVersion[slot] = context.layoutVersion;
                Element hit = over ? HitTest(x, y, dragging[slot] ? dragMoved[slot] : null) : null;
                SetHover(slot, hit, x, y);
            }
            Element current = target[slot];

            if (pressed && !wasPressed[slot])
            {
                clickCancelled[slot] = false;
                context.textInput.Pressed(current);
                captured[slot] = current;
                SetActive(slot, current);
                dragCandidate[slot] = NearestDraggable(current);
                pressX[slot] = x;
                pressY[slot] = y;
                Dispatch(Down, current, slot, x, y, 0f);
            }
            else if (!pressed && wasPressed[slot])
            {
                Release(slot, over, over ? x : lastX[slot], over ? y : lastY[slot]);
            }

            // A press on a draggable that travels far enough becomes a drag;
            // checked before move events, so nothing else (a scroll view)
            // starts its own gesture from this movement.
            if (moved && pressed && !dragging[slot] && dragCandidate[slot] != null)
            {
                float threshold = DragThreshold * context.scale;
                if (Mathf.Abs(x - pressX[slot]) >= threshold || Mathf.Abs(y - pressY[slot]) >= threshold)
                {
                    StartDrag(slot, x, y);
                }
            }
            if (dragging[slot])
            {
                if (moved)
                {
                    UpdateDrag(slot, x, y);
                }
            }
            else if (moved)
            {
                Element receiver = captured[slot] != null ? captured[slot] : current;
                Dispatch(Move, receiver, slot, x, y, 0f);
            }
            if (sample.ScrollDelta != 0f && context.diagnostics)
            {
                Debug.Log("[MenUI] wheel " + sample.ScrollDelta + " at frame " + context.frame + ": over panel " + over
                    + ", target " + Describe(current) + ", scroll view above it " + Describe(ScrollViewAbove(current)));
            }
            if (over && sample.ScrollDelta != 0f)
            {
                Dispatch(Scroll, current, slot, x, y, sample.ScrollDelta);
            }

            wasPressed[slot] = pressed;
            wasOver[slot] = over;
            if (over)
            {
                lastX[slot] = x;
                lastY[slot] = y;
            }
        }

        /// Ends a press: pointerup to the captured element, then click when
        /// the pointer is still over it (and `over` the panel at all).
        private void Release(int slot, bool over, float x, float y)
        {
            if (dragging[slot])
            {
                FinishDrag(slot, over, x, y);
            }
            dragCandidate[slot] = null;
            Element pressedElement = captured[slot];
            captured[slot] = null;
            SetActive(slot, null);
            wasPressed[slot] = false;
            if (pressedElement == null)
            {
                return;
            }
            Dispatch(Up, pressedElement, slot, x, y, 0f);
            Element current = target[slot];
            if (over && !clickCancelled[slot] && current != null && IsInclusiveAncestor(pressedElement, current))
            {
                Dispatch(Click, pressedElement, slot, x, y, 0f);
            }
        }

        // ---- hit testing -----------------------------------------------------------

        private void RebuildHitList()
        {
            hitListVersion = context.hitVersion;
            hitList.Clear();
            List<Element> order = context.paintOrder;
            for (int i = 0; i < order.Count; i++)
            {
                Element e = order[i];
                ComputedStyle s = e.computed;
                if (s.Enum(StyleProperty.PointerEvents) != (int)PointerEventsMode.Auto || !s.IsVisible)
                {
                    continue;
                }
                bool listening = e.pointerHandlers != null || e.hoverStyle != null || e.activeStyle != null
                    || e.dragSource != null || e.dropTarget != null || e is TextBox
                    || (e.stateRuleMask & (PseudoState.Hover | PseudoState.Active)) != 0;
                bool opaque = s.ColorOf(StyleProperty.Background).a > 0f
                    || s.numbers[StyleProperty.BorderTopWidth] + s.numbers[StyleProperty.BorderRightWidth]
                        + s.numbers[StyleProperty.BorderBottomWidth] + s.numbers[StyleProperty.BorderLeftWidth] > 0f;
                if (listening || opaque)
                {
                    hitList.Add(e);
                }
            }
        }

        /// Topmost hittable element at a screen position. Elements in a
        /// transform group are tested at the position moved back by the
        /// group's offset, and only inside the group's clip.
        private Element HitTest(float x, float y, Element excluded)
        {
            TransformGroups groups = context.groups;
            Vector4[] clips = groups.clips;
            for (int i = hitList.Count - 1; i >= 0; i--)
            {
                Element e = hitList[i];
                float localX = x, localY = y;
                int group = e.group;
                if (group != TransformGroups.Root)
                {
                    Vector4 clip = clips[group];
                    if (x < clip.x || y < clip.y || x >= clip.z || y >= clip.w)
                    {
                        continue;
                    }
                    localX = groups.LocalX(group, x);
                    localY = groups.LocalY(group, y);
                }
                if (localX >= e.x && localY >= e.y && localX < e.x + e.width && localY < e.y + e.height
                    && localX >= e.clipLeft && localY >= e.clipTop && localX < e.clipRight && localY < e.clipBottom
                    && (excluded == null || !IsInclusiveAncestor(excluded, e)))
                {
                    return e;
                }
            }
            return null;
        }

        // ---- drag and drop -------------------------------------------------------------

        private static Element NearestDraggable(Element e)
        {
            for (Element a = e; a != null; a = a.parent)
            {
                if (a.dragSource != null && (a.pseudoStates & PseudoState.Disabled) == 0)
                {
                    return a;
                }
            }
            return null;
        }

        private void StartDrag(int slot, float x, float y)
        {
            Element source = dragCandidate[slot];
            DragSourceConfig config = source.dragSource;
            dragging[slot] = true;
            dragPayload[slot] = config.payload != null ? config.payload() : null;
            // It is a drag now, not a press: no click, no :active.
            clickCancelled[slot] = true;
            SetActive(slot, null);
            source.SetPseudoState(PseudoState.Dragging, true);

            // What follows the pointer: a preview on the overlay layer, or the
            // element itself. Either way it moves as a transform group.
            Element moved = source;
            if (config.preview != null)
            {
                ReactiveRuntime runtime = context.runtime;
                Owner owner = new Owner(runtime, runtime.root);
                Element preview = null;
                runtime.RunWithOwner(owner, () => { preview = config.preview(); });
                if (preview != null)
                {
                    float scale = context.scale;
                    float zoom = context.groups.ScaleOf(source.group);
                    preview.Absolute()
                        .Left(ScreenX(source) / scale).Top(ScreenY(source) / scale)
                        .PointerEvents(PointerEventsMode.None);
                    if (!preview.inlineStyle.isSet[StyleProperty.Width]) preview.Width(source.width * zoom / scale);
                    if (!preview.inlineStyle.isSet[StyleProperty.Height]) preview.Height(source.height * zoom / scale);
                    context.overlayRoot.AppendChild(preview);
                    previewOwner[slot] = owner;
                    moved = preview;
                }
                else
                {
                    owner.Dispose();
                }
            }
            dragMoved[slot] = moved;
            int group = moved.translateGroup == TransformGroups.Root ? context.groups.Allocate() : TransformGroups.Root;
            dragGroup[slot] = group;
            if (group != TransformGroups.Root)
            {
                moved.translateGroup = group;
                moved.MarkLayoutDirty(false);
            }
            if (config.started != null)
            {
                FillEvent(slot, x, y, source, source);
                Action<PointerEvent> started = config.started;
                context.runtime.Batch(() => started(pointerEvent));
            }
            UpdateDrag(slot, x, y);
        }

        private void UpdateDrag(int slot, float x, float y)
        {
            if (dragGroup[slot] != TransformGroups.Root)
            {
                // Whole physical pixels, like every translate: crisp edges.
                context.groups.SetOffset(dragGroup[slot], Mathf.Round(x - pressX[slot]), Mathf.Round(y - pressY[slot]));
            }
            Element under = HitTest(x, y, dragMoved[slot]);
            object payload = dragPayload[slot];
            DropTargetConfig config = null;
            Element targetElement = FindDropTarget(under, payload, out config);
            if (targetElement != dropElement[slot])
            {
                LeaveDropTarget(slot, x, y, true);
                if (targetElement != null)
                {
                    dropElement[slot] = targetElement;
                    dropConfig[slot] = config;
                    targetElement.SetPseudoState(PseudoState.DragOver, true);
                    if (config.entered != null)
                    {
                        Action<object, PointerEvent> entered = config.entered;
                        FillEvent(slot, x, y, under, targetElement);
                        context.runtime.Batch(() => entered(payload, pointerEvent));
                    }
                }
            }
            if (dropElement[slot] != null && dropConfig[slot].over != null)
            {
                Action<object, PointerEvent> over = dropConfig[slot].over;
                FillEvent(slot, x, y, under, dropElement[slot]);
                context.runtime.Batch(() => over(payload, pointerEvent));
            }
        }

        private void LeaveDropTarget(int slot, float x, float y, bool notify)
        {
            Element previous = dropElement[slot];
            if (previous == null)
            {
                return;
            }
            DropTargetConfig config = dropConfig[slot];
            dropElement[slot] = null;
            dropConfig[slot] = null;
            previous.SetPseudoState(PseudoState.DragOver, false);
            if (notify && config.left != null)
            {
                object payload = dragPayload[slot];
                Action<object, PointerEvent> left = config.left;
                FillEvent(slot, x, y, previous, previous);
                context.runtime.Batch(() => left(payload, pointerEvent));
            }
        }

        /// The nearest element (from `e` up) with a drop target accepting `payload`.
        private static Element FindDropTarget(Element e, object payload, out DropTargetConfig found)
        {
            for (Element a = e; a != null; a = a.parent)
            {
                for (DropTargetConfig config = a.dropTarget; config != null; config = config.next)
                {
                    if (config.accepts(payload))
                    {
                        found = config;
                        return a;
                    }
                }
            }
            found = null;
            return null;
        }

        /// Ends a drag: drops on the current target if released over the
        /// panel, then restores the dragged element and removes the preview.
        private void FinishDrag(int slot, bool over, float x, float y)
        {
            Element source = dragCandidate[slot];
            Element target = dropElement[slot];
            DropTargetConfig config = dropConfig[slot];
            object payload = dragPayload[slot];
            bool dropped = over && target != null;
            LeaveDropTarget(slot, x, y, false);

            Element moved = dragMoved[slot];
            int group = dragGroup[slot];
            if (group != TransformGroups.Root)
            {
                context.groups.SetOffset(group, 0f, 0f);
                context.groups.Free(group);
                moved.translateGroup = TransformGroups.Root;
                moved.MarkLayoutDirty(false);
            }
            if (previewOwner[slot] != null)
            {
                previewOwner[slot].Dispose();
                previewOwner[slot] = null;
                context.overlayRoot.RemoveChild(moved);
            }
            source.SetPseudoState(PseudoState.Dragging, false);
            dragging[slot] = false;
            dragMoved[slot] = null;
            dragGroup[slot] = TransformGroups.Root;
            dragPayload[slot] = null;

            if (dropped)
            {
                Action<object, PointerEvent> drop = config.dropped;
                FillEvent(slot, x, y, target, target);
                context.runtime.Batch(() => drop(payload, pointerEvent));
            }
            if (source.dragSource.ended != null)
            {
                Action<bool> ended = source.dragSource.ended;
                context.runtime.Batch(() => ended(dropped));
            }
        }

        private void FillEvent(int slot, float x, float y, Element eventTarget, Element current)
        {
            float scale = context.scale;
            pointerEvent.context = context;
            pointerEvent.PointerId = slotPointerId[slot];
            pointerEvent.X = x / scale;
            pointerEvent.Y = y / scale;
            // In the element's own (unscaled) logical pixels.
            float zoom = current != null ? context.groups.ScaleOf(current.group) : 1f;
            pointerEvent.LocalX = current != null ? (x - ScreenX(current)) / (scale * zoom) : 0f;
            pointerEvent.LocalY = current != null ? (y - ScreenY(current)) / (scale * zoom) : 0f;
            pointerEvent.Target = eventTarget;
            pointerEvent.CurrentTarget = current;
            pointerEvent.ScrollDelta = 0f;
            pointerEvent.propagationStopped = false;
            pointerEvent.clickCancelled = false;
        }

        // Where an element is on the panel (physical px): its layout position
        // mapped by its transform group (scrolling, translation, scale).
        private float ScreenX(Element e)
        {
            return context.groups.ScreenX(e.group, e.x);
        }

        private float ScreenY(Element e)
        {
            return context.groups.ScreenY(e.group, e.y);
        }

        private static Element ScrollViewAbove(Element e)
        {
            for (Element a = e; a != null; a = a.parent)
            {
                if (a is ScrollView) return a;
            }
            return null;
        }

        private static string Describe(Element e)
        {
            if (e == null) return "none";
            string text = e.tag != null ? e.tag : "element";
            if (e.classes != null)
            {
                for (int i = 0; i < e.classes.Count; i++) text += "." + e.classes[i];
            }
            return text + (e.parent == null ? " (detached)" : "");
        }

        private static bool IsInclusiveAncestor(Element ancestor, Element e)
        {
            for (Element a = e; a != null; a = a.parent)
            {
                if (a == ancestor) return true;
            }
            return false;
        }

        // ---- pseudo states ----------------------------------------------------------

        private void SetHover(int slot, Element hit, float x, float y)
        {
            Element previous = target[slot];
            if (previous == hit)
            {
                return;
            }
            target[slot] = hit;
            List<Element> chain = hoverChain[slot];

            // Leave what is no longer hovered (deepest first), enter the rest.
            for (int i = 0; i < chain.Count; i++)
            {
                Element e = chain[i];
                if (hit == null || !IsInclusiveAncestor(e, hit))
                {
                    e.hoverCount--;
                    if (e.hoverCount == 0)
                    {
                        e.SetPseudoState(PseudoState.Hover, false);
                    }
                    DispatchOne(Leave, e, e, slot, x, y, 0f);
                }
            }
            List<Element> next = new List<Element>();
            for (Element e = hit; e != null; e = e.parent)
            {
                next.Add(e);
            }
            for (int i = next.Count - 1; i >= 0; i--)
            {
                Element e = next[i];
                if (!chain.Contains(e))
                {
                    e.hoverCount++;
                    if (e.hoverCount == 1)
                    {
                        e.SetPseudoState(PseudoState.Hover, true);
                    }
                    DispatchOne(Enter, e, e, slot, x, y, 0f);
                }
            }
            chain.Clear();
            chain.AddRange(next);
        }

        private void SetActive(int slot, Element pressed)
        {
            List<Element> chain = activeChain[slot];
            for (int i = 0; i < chain.Count; i++)
            {
                Element e = chain[i];
                e.activeCount--;
                if (e.activeCount == 0)
                {
                    e.SetPseudoState(PseudoState.Active, false);
                }
            }
            chain.Clear();
            for (Element e = pressed; e != null; e = e.parent)
            {
                chain.Add(e);
                e.activeCount++;
                if (e.activeCount == 1)
                {
                    e.SetPseudoState(PseudoState.Active, true);
                }
            }
        }

        // ---- dispatch -----------------------------------------------------------------

        private void Dispatch(int kind, Element start, int slot, float x, float y, float scroll)
        {
            if (start == null)
            {
                return;
            }
            pointerEvent.propagationStopped = false;
            for (Element e = start; e != null && !pointerEvent.propagationStopped; e = e.parent)
            {
                DispatchOne(kind, start, e, slot, x, y, scroll);
            }
        }

        private void DispatchOne(int kind, Element eventTarget, Element e, int slot, float x, float y, float scroll)
        {
            PointerHandlers handlers = e.pointerHandlers;
            if (handlers == null)
            {
                return;
            }
            // A disabled element's own handlers do not run (its ancestors' do).
            if ((e.pseudoStates & PseudoState.Disabled) != 0 && kind != Enter && kind != Leave)
            {
                return;
            }
            Action<PointerEvent> handler = kind == Down ? handlers.down
                : kind == Up ? handlers.up
                : kind == Click ? handlers.click
                : kind == Move ? handlers.move
                : kind == Enter ? handlers.enter
                : kind == Leave ? handlers.leave
                : handlers.scroll;
            if (handler == null)
            {
                return;
            }
            float scale = context.scale;
            pointerEvent.context = context;
            pointerEvent.PointerId = slotPointerId[slot];
            pointerEvent.X = x / scale;
            pointerEvent.Y = y / scale;
            pointerEvent.LocalX = (x - ScreenX(e)) / scale;
            pointerEvent.LocalY = (y - ScreenY(e)) / scale;
            pointerEvent.Target = eventTarget;
            pointerEvent.CurrentTarget = e;
            pointerEvent.ScrollDelta = scroll;
            // Handlers usually write signals; batch so effects run once.
            pointerEvent.clickCancelled = false;
            context.runtime.Batch(() => handler(pointerEvent));
            if (pointerEvent.clickCancelled && !clickCancelled[slot])
            {
                clickCancelled[slot] = true;
                SetActive(slot, null);
            }
        }
    }
}
