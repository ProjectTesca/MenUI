// Drag and drop, on top of the device-independent routing (any pointer
// source works: desktop, VR hands, ...).
//
//   card.Draggable(() => card.Id)                         // the payload
//       .DragPreview(() => CardView(card))                // optional: drawn on the overlay layer
//       .Dragging(s => s.Opacity(0.4f));                  // :dragging
//   column.DropTarget((int id, PointerEvent e) => Move(id, column, e.InsertionIndexIn(list)))
//       .DragOver(s => s.Border(2, accent));              // :drag-over
//
// A press on a draggable element that moves past a small threshold becomes a
// drag: the press no longer clicks, and inner draggables win over outer
// ones (a card in a scroll view drags; the view does not scroll). The preview
// — the element itself, or the one DragPreview builds on the overlay layer —
// follows the pointer on the GPU, so dragging never lays out. The drop
// target is the nearest ancestor (inclusive) of what is under the pointer
// that accepts the payload's type; the dragged element and the preview are
// not hit. Releasing over a target drops; anywhere else cancels.

using System;
using UnityEngine;

namespace MenUI
{
    internal sealed class DragSourceConfig
    {
        internal Func<object> payload;
        internal Func<Element> preview;
        internal Action<PointerEvent> started;
        internal Action<bool> ended;
    }

    internal sealed class DropTargetConfig
    {
        internal Func<object, bool> accepts;
        internal Action<object, PointerEvent> dropped;
        internal Action<object, PointerEvent> entered;
        internal Action<object, PointerEvent> left;
        internal Action<object, PointerEvent> over;
        internal DropTargetConfig next;   // several DropTarget<T> on one element
    }

    public static class DragAndDropExtensions
    {
        /// Makes the element draggable; `payload` is read when a drag starts.
        public static T Draggable<T, TPayload>(this T self, Func<TPayload> payload) where T : Element
        {
            DragSource(self).payload = () => payload();
            self.context.hitVersion++;
            return self;
        }

        /// What follows the pointer while dragging, built when a drag starts
        /// and placed on the overlay layer over the element (so it can leave
        /// clipping containers). Without it, the element itself moves.
        public static T DragPreview<T>(this T self, Func<Element> build) where T : Element
        {
            DragSource(self).preview = build;
            return self;
        }

        public static T OnDragStart<T>(this T self, Action<PointerEvent> handler) where T : Element
        {
            DragSourceConfig config = DragSource(self);
            config.started += handler;
            return self;
        }

        /// `dropped` is false when the drag was cancelled (released elsewhere).
        public static T OnDragEnd<T>(this T self, Action<bool> handler) where T : Element
        {
            DragSourceConfig config = DragSource(self);
            config.ended += handler;
            return self;
        }

        /// Accepts payloads of type `TPayload`, taken from the handler's
        /// parameter: `.DropTarget((int id, PointerEvent e) => ...)`.
        /// `onEnter`/`onLeave`/`onOver` follow a matching payload moving over
        /// this element or its descendants (unless a nearer target takes it).
        public static T DropTarget<T, TPayload>(this T self, Action<TPayload, PointerEvent> onDrop,
            Action<TPayload, PointerEvent> onEnter = null, Action<TPayload, PointerEvent> onLeave = null,
            Action<TPayload, PointerEvent> onOver = null) where T : Element
        {
            DropTargetConfig config = new DropTargetConfig();
            config.accepts = value => value is TPayload;
            config.dropped = (value, e) => onDrop((TPayload)value, e);
            if (onEnter != null) config.entered = (value, e) => onEnter((TPayload)value, e);
            if (onLeave != null) config.left = (value, e) => onLeave((TPayload)value, e);
            if (onOver != null) config.over = (value, e) => onOver((TPayload)value, e);
            config.next = self.dropTarget;
            self.dropTarget = config;
            self.context.hitVersion++;
            return self;
        }

        /// `:dragging` variant: the element being dragged.
        public static T Dragging<T>(this T self, Action<Style> configure) where T : Element
        {
            return ElementExtensions.Variant(self, PseudoState.Dragging, configure);
        }

        /// `:drag-over` variant: a drop target with an acceptable payload over it.
        public static T DragOver<T>(this T self, Action<Style> configure) where T : Element
        {
            return ElementExtensions.Variant(self, PseudoState.DragOver, configure);
        }

        private static DragSourceConfig DragSource(Element element)
        {
            if (element.dragSource == null)
            {
                element.dragSource = new DragSourceConfig();
            }
            return element.dragSource;
        }
    }
}
