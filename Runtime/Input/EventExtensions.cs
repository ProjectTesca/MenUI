using System;

namespace MenUI
{
    /// Pointer event handlers on elements. Handlers run batched: signals
    /// written in one handler update the UI once.
    public static class EventExtensions
    {
        public static T OnClick<T>(this T self, Action handler) where T : Element { Handlers(self).click += e => handler(); return self; }
        public static T OnClick<T>(this T self, Action<PointerEvent> handler) where T : Element { Handlers(self).click += handler; return self; }
        public static T OnPointerDown<T>(this T self, Action<PointerEvent> handler) where T : Element { Handlers(self).down += handler; return self; }
        public static T OnPointerUp<T>(this T self, Action<PointerEvent> handler) where T : Element { Handlers(self).up += handler; return self; }
        public static T OnPointerMove<T>(this T self, Action<PointerEvent> handler) where T : Element { Handlers(self).move += handler; return self; }
        /// Does not bubble (like `pointerenter`).
        public static T OnPointerEnter<T>(this T self, Action<PointerEvent> handler) where T : Element { Handlers(self).enter += handler; return self; }
        /// Does not bubble (like `pointerleave`).
        public static T OnPointerLeave<T>(this T self, Action<PointerEvent> handler) where T : Element { Handlers(self).leave += handler; return self; }
        public static T OnScroll<T>(this T self, Action<PointerEvent> handler) where T : Element { Handlers(self).scroll += handler; return self; }

        private static PointerHandlers Handlers(Element element)
        {
            if (element.pointerHandlers == null)
            {
                element.pointerHandlers = new PointerHandlers();
            }
            // The set of hittable elements may have changed.
            element.context.hitVersion++;
            return element.pointerHandlers;
        }
    }
}
