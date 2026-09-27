// The state a scroll view and its scrollbars share — and anything else that
// wants to read or drive scrolling:
//
//   var scroll = ScrollState();
//   ScrollView(scroll).Children(...);           // moves the content
//   Scrollbar(scroll);                          // shows and drags it
//   Button("Top").OnClick(() => scroll.ScrollTo(0));
//
// Offsets and sizes are logical pixels and reactive (read them in a binding
// and it follows). Writing an offset moves the content on the GPU only.

using UnityEngine;

namespace MenUI
{
    public enum ScrollAxis { Vertical, Horizontal, Both }

    /// Work spread over frames (scroll momentum, filling list rows).
    internal interface IFrameTask
    {
        /// Returns false once there is nothing left to do.
        bool Tick(float seconds);
    }

    public sealed class ScrollState : IFrameTask
    {
        private const float StopSpeed = 8f;       // logical px per second
        private const float Friction = 4f;        // velocity decays by e^(-Friction * seconds)

        private readonly MenUIContext context;
        private readonly Signal<float> offsetX;
        private readonly Signal<float> offsetY;
        private readonly Signal<float> viewportWidth;
        private readonly Signal<float> viewportHeight;
        private readonly Signal<float> contentWidth;
        private readonly Signal<float> contentHeight;

        internal ScrollView view;
        private float velocityX;
        private float velocityY;
        internal bool animating;

        internal ScrollState(MenUIContext context)
        {
            this.context = context;
            offsetX = context.Signal(0f);
            offsetY = context.Signal(0f);
            viewportWidth = context.Signal(0f);
            viewportHeight = context.Signal(0f);
            contentWidth = context.Signal(0f);
            contentHeight = context.Signal(0f);
        }

        public float OffsetX { get { return offsetX.Value; } }
        public float OffsetY { get { return offsetY.Value; } }
        public float ViewportWidth { get { return viewportWidth.Value; } }
        public float ViewportHeight { get { return viewportHeight.Value; } }
        public float ContentWidth { get { return contentWidth.Value; } }
        public float ContentHeight { get { return contentHeight.Value; } }
        public float MaxOffsetX { get { return Mathf.Max(0f, contentWidth.Value - viewportWidth.Value); } }
        public float MaxOffsetY { get { return Mathf.Max(0f, contentHeight.Value - viewportHeight.Value); } }

        public void ScrollTo(float y)
        {
            ScrollTo(offsetX.Peek(), y);
        }

        public void ScrollTo(float x, float y)
        {
            float maxX = Mathf.Max(0f, contentWidth.Peek() - viewportWidth.Peek());
            float maxY = Mathf.Max(0f, contentHeight.Peek() - viewportHeight.Peek());
            context.Batch(() =>
            {
                offsetX.Value = Mathf.Clamp(x, 0f, maxX);
                offsetY.Value = Mathf.Clamp(y, 0f, maxY);
            });
        }

        public void ScrollBy(float dx, float dy)
        {
            ScrollTo(offsetX.Peek() + dx, offsetY.Peek() + dy);
        }

        /// Scrolls the least needed to show `element` (a descendant of the
        /// scroll view), after the current layout.
        public void ScrollIntoView(Element element)
        {
            if (view == null || element == null || !element.hasArranged)
            {
                return;
            }
            float scale = context.scale;
            float top = (element.y - view.contentOriginY) / scale;
            float bottom = top + element.height / scale;
            float left = (element.x - view.contentOriginX) / scale;
            float right = left + element.width / scale;
            float x = offsetX.Peek();
            float y = offsetY.Peek();
            if (top < y) y = top;
            else if (bottom > y + viewportHeight.Peek()) y = bottom - viewportHeight.Peek();
            if (left < x) x = left;
            else if (right > x + viewportWidth.Peek()) x = right - viewportWidth.Peek();
            ScrollTo(x, y);
        }

        /// Starts or replaces momentum (logical px per second).
        public void Fling(float speedX, float speedY)
        {
            velocityX = speedX;
            velocityY = speedY;
            if (!animating && (Mathf.Abs(speedX) > StopSpeed || Mathf.Abs(speedY) > StopSpeed))
            {
                animating = true;
                context.StartAnimating(this);
            }
        }

        public void StopMomentum()
        {
            velocityX = 0f;
            velocityY = 0f;
        }

        /// Advances momentum; returns false once it has stopped.
        public bool Tick(float seconds)
        {
            if (Mathf.Abs(velocityX) <= StopSpeed && Mathf.Abs(velocityY) <= StopSpeed)
            {
                animating = false;
                return false;
            }
            float beforeX = offsetX.Peek();
            float beforeY = offsetY.Peek();
            ScrollBy(velocityX * seconds, velocityY * seconds);
            float decay = Mathf.Exp(-Friction * seconds);
            // Hitting an end stops that axis.
            velocityX = offsetX.Peek() == beforeX ? 0f : velocityX * decay;
            velocityY = offsetY.Peek() == beforeY ? 0f : velocityY * decay;
            return true;
        }

        /// From layout, physical pixels.
        internal void SetMetrics(int viewportWidthPixels, int viewportHeightPixels, int contentWidthPixels, int contentHeightPixels)
        {
            float scale = context.scale;
            float newViewportWidth = viewportWidthPixels / scale;
            float newViewportHeight = viewportHeightPixels / scale;
            float newContentWidth = contentWidthPixels / scale;
            float newContentHeight = contentHeightPixels / scale;
            if (newViewportWidth == viewportWidth.Peek() && newViewportHeight == viewportHeight.Peek()
                && newContentWidth == contentWidth.Peek() && newContentHeight == contentHeight.Peek())
            {
                return;
            }
            context.Batch(() =>
            {
                viewportWidth.Value = newViewportWidth;
                viewportHeight.Value = newViewportHeight;
                contentWidth.Value = newContentWidth;
                contentHeight.Value = newContentHeight;
                // Keep the offset valid when the content shrank.
                offsetX.Value = Mathf.Clamp(offsetX.Peek(), 0f, Mathf.Max(0f, newContentWidth - newViewportWidth));
                offsetY.Value = Mathf.Clamp(offsetY.Peek(), 0f, Mathf.Max(0f, newContentHeight - newViewportHeight));
            });
        }
    }
}
