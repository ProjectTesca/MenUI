// A box whose content scrolls. It only moves the content — scrollbars are
// separate components bound to the same ScrollState (see Scrollbar in
// MenUIContext, and ScrollArea for the usual combination).
//
// Scrolling never lays out or repaints: the content is a transform group,
// and the offset goes straight to the GPU.
//
// Input: the wheel scrolls; pressing and dragging past a small threshold
// scrolls too (and cancels the click on whatever was pressed); letting go
// while moving keeps the content gliding.

using UnityEngine;

namespace MenUI
{
    public sealed class ScrollView : Element
    {
        private const float DragThreshold = 6f;        // logical px
        private const float VelocitySmoothing = 0.3f;  // weight of the newest sample

        internal readonly ScrollState state;
        internal readonly ScrollAxis axis;
        internal int scrollGroup;
        // Where the content starts when not scrolled (physical px), for ScrollIntoView.
        internal int contentOriginX, contentOriginY;

        private int dragPointer = -1;
        private bool dragging;
        private float dragStartX, dragStartY;
        private float dragStartOffsetX, dragStartOffsetY;
        private float lastX, lastY, lastTime;
        private float velocityX, velocityY;

        internal ScrollView(MenUIContext context, ScrollState state, ScrollAxis axis) : base(context)
        {
            this.state = state;
            this.axis = axis;
            tag = "scroll-view";
            state.view = this;
            inlineStyle.SetEnum(StyleProperty.Overflow, (int)OverflowMode.Hidden);

            scrollGroup = context.groups.Allocate();
            int group = scrollGroup;
            context.OnCleanup(() => context.groups.Free(group));
            // The offset goes to the GPU, snapped to whole physical pixels.
            context.Bind(() =>
            {
                float scale = context.scale;
                context.groups.SetOffset(group, -Mathf.Round(state.OffsetX * scale), -Mathf.Round(state.OffsetY * scale));
            });

            this.OnScroll(OnWheel);
            this.OnPointerDown(OnDown);
            this.OnPointerMove(OnMove);
            this.OnPointerUp(OnUp);
        }

        internal bool ScrollsVertically { get { return axis != ScrollAxis.Horizontal; } }
        internal bool ScrollsHorizontally { get { return axis != ScrollAxis.Vertical; } }

        public ScrollView Children(params Element[] items)
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

        public ScrollView Child(Element item)
        {
            if (item != null)
            {
                AppendChild(item);
            }
            return this;
        }

        internal void ReportMetrics(int viewportWidth, int viewportHeight, int contentWidth, int contentHeight)
        {
            if (context.diagnostics)
            {
                Debug.Log("[MenUI] scroll view metrics (physical px): viewport " + viewportWidth + "x" + viewportHeight
                    + ", content " + contentWidth + "x" + contentHeight + ", axis " + axis);
            }
            contentOriginX = x + (int)computed.numbers[StyleProperty.BorderLeftWidth];
            contentOriginY = y + (int)computed.numbers[StyleProperty.BorderTopWidth];
            state.SetMetrics(
                ScrollsHorizontally ? viewportWidth : 0, ScrollsVertically ? viewportHeight : 0,
                ScrollsHorizontally ? contentWidth : 0, ScrollsVertically ? contentHeight : 0);
        }

        private void OnWheel(PointerEvent e)
        {
            if (context.diagnostics)
            {
                Debug.Log("[MenUI] scroll view wheel " + e.ScrollDelta + " at offset " + state.OffsetY + " / max " + state.MaxOffsetY
                    + " (" + (ScrollsVertically ? "vertical" : "horizontal") + ")");
            }
            float beforeX = state.OffsetX;
            float beforeY = state.OffsetY;
            state.StopMomentum();
            if (ScrollsVertically) state.ScrollBy(0f, e.ScrollDelta);
            else state.ScrollBy(e.ScrollDelta, 0f);
            // An inner view that moved keeps the wheel; at its end, the outer one scrolls.
            if (state.OffsetX != beforeX || state.OffsetY != beforeY)
            {
                e.StopPropagation();
            }
        }

        private void OnDown(PointerEvent e)
        {
            state.StopMomentum();
            dragPointer = e.PointerId;
            dragging = false;
            dragStartX = e.X;
            dragStartY = e.Y;
            dragStartOffsetX = state.OffsetX;
            dragStartOffsetY = state.OffsetY;
            lastX = e.X;
            lastY = e.Y;
            lastTime = context.time;
            velocityX = 0f;
            velocityY = 0f;
        }

        private void OnMove(PointerEvent e)
        {
            if (e.PointerId != dragPointer)
            {
                return;
            }
            float dx = ScrollsHorizontally ? e.X - dragStartX : 0f;
            float dy = ScrollsVertically ? e.Y - dragStartY : 0f;
            if (!dragging)
            {
                if (Mathf.Abs(dx) < DragThreshold && Mathf.Abs(dy) < DragThreshold)
                {
                    return;
                }
                dragging = true;
                e.CancelClick();   // it is a scroll, not a press on what is underneath
                if (context.diagnostics)
                {
                    Debug.Log("[MenUI] scroll view drag started at offset " + state.OffsetY + " / max " + state.MaxOffsetY);
                }
            }
            state.ScrollTo(dragStartOffsetX - dx, dragStartOffsetY - dy);
            e.StopPropagation();

            float elapsed = context.time - lastTime;
            if (elapsed > 0.0001f)
            {
                float sampleX = -(e.X - lastX) / elapsed;
                float sampleY = -(e.Y - lastY) / elapsed;
                velocityX = Mathf.Lerp(velocityX, sampleX, VelocitySmoothing);
                velocityY = Mathf.Lerp(velocityY, sampleY, VelocitySmoothing);
            }
            lastX = e.X;
            lastY = e.Y;
            lastTime = context.time;
        }

        private void OnUp(PointerEvent e)
        {
            if (e.PointerId != dragPointer)
            {
                return;
            }
            dragPointer = -1;
            if (dragging)
            {
                dragging = false;
                // A pause before letting go means no glide.
                if (context.time - lastTime < 0.1f)
                {
                    state.Fling(ScrollsHorizontally ? velocityX : 0f, ScrollsVertically ? velocityY : 0f);
                }
            }
        }
    }
}
