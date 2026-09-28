// Built-in components that open on the overlay layer (above everything,
// unclipped): drop-down selects, dialogs, tooltips and toasts.
//
//   Select(size, sizes, s => s.ToString());      // or Select(fruit, "Apple", "Banana")
//   Dialog(confirmOpen, () => Div().Children(Text("Delete?"), ...));
//   Button("?").Tooltip("Shows help");
//   Toast("Saved");
//
// The overlay layer is outside your element tree, so it does not inherit your
// fonts or colors: popups take the theme's colors (and a select's menu its
// font). Style them by tag: `select-menu`, `option`, `dialog`,
// `dialog-backdrop`, `tooltip`, `toast`.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    internal static class Popups
    {
        private const int VisibleOptions = 8;   // more than this: the menu scrolls

        internal static Box Select<T>(MenUIContext ui, Signal<T> selected, List<T> options, Func<T, string> label)
        {
            ui.EnsureStyles(ComponentStyles.Select);
            Signal<bool> open = ui.Signal(false);
            Box select = ui.Div().Tag("select").Class("open", () => open.Value);
            select.Children(
                ui.Text(() => label(selected.Value)),
                Controls.Symbol(ui, "▼").Tag("select-arrow"));
            select.OnClick(() => open.Value = !open.Peek());
            select.Child(ui.Show(() => open.Value, () => ui.Overlay(() => Menu(ui, select, selected, options, label, open))));
            return select;
        }

        private static Element Menu<T>(MenUIContext ui, Box select, Signal<T> selected, List<T> options,
            Func<T, string> label, Signal<bool> open)
        {
            float left, top, width, height;
            ui.LogicalRect(select, out left, out top, out width, out height);
            float fontSize = select.computed.FontSize / ui.scale;
            float rowHeight = fontSize * 1.45f + 12f;
            bool scrolls = options.Count > VisibleOptions;
            float menuHeight = (scrolls ? VisibleOptions : options.Count) * rowHeight + 10f;
            // Below the select; above it when there is no room below.
            float menuTop = top + height + 4f;
            if (menuTop + menuHeight > ui.LogicalHeight - 4f && top - menuHeight - 4f >= 4f)
            {
                menuTop = top - menuHeight - 4f;
            }

            List<Element> rows = new List<Element>();
            for (int i = 0; i < options.Count; i++)
            {
                T option = options[i];
                rows.Add(ui.Div().Tag("option").Class("selected", () => object.Equals(selected.Value, option))
                    .OnClick(() =>
                    {
                        selected.Value = option;
                        open.Value = false;
                    })
                    .Child(ui.Text(label(option))));
            }
            Box menu = ui.Div().Tag("select-menu").Left(left).Top(menuTop).Width(width).FontSize(fontSize);
            InheritFont(ui, select, menu);
            if (scrolls)
            {
                menu.Height(menuHeight).Child(ui.ScrollArea(ui.ScrollState(), rows.ToArray()).FullSize());
            }
            else
            {
                menu.Children(rows.ToArray());
            }

            // A press anywhere else closes it (and does nothing else).
            Box layer = ui.Div().Tag("select-layer").Child(menu);
            layer.OnPointerDown(e =>
            {
                if (e.Target == layer)
                {
                    open.Value = false;
                }
            });
            return layer;
        }

        internal static Region Dialog(MenUIContext ui, Signal<bool> open, Func<Element> content, bool closeOnBackdrop)
        {
            ui.EnsureStyles(ComponentStyles.Dialog);
            return ui.Show(() => open.Value, () => ui.Overlay(() =>
            {
                Box panel = ui.Div().Tag("dialog").Child(content());
                Box backdrop = ui.Div().Tag("dialog-backdrop").Child(panel);
                backdrop.OnClick(e =>
                {
                    if (closeOnBackdrop && e.Target == backdrop)
                    {
                        open.Value = false;
                    }
                });
                return backdrop;
            }));
        }

        internal static void Tooltip(Element element, string text)
        {
            MenUIContext ui = element.context;
            ui.EnsureStyles(ComponentStyles.Tooltip);
            Signal<bool> visible = ui.Signal(false);
            Delay delay = null;
            element.OnPointerEnter(e =>
            {
                if (delay != null)
                {
                    delay.Cancel();
                }
                delay = ui.After(0.5f, () => visible.Value = true);
            });
            Action<PointerEvent> hide = e =>
            {
                if (delay != null)
                {
                    delay.Cancel();
                    delay = null;
                }
                visible.Value = false;
            };
            element.OnPointerLeave(hide);
            element.OnPointerDown(hide);
            // Not placed in the tree: it lives as long as the scope that made it.
            ui.Show(() => visible.Value, () => ui.Overlay(() =>
            {
                float left, top, width, height;
                ui.LogicalRect(element, out left, out top, out width, out height);
                Box tip = ui.Div().Tag("tooltip").Child(ui.Text(text));
                float below = top + height + 6f;
                if (below + 32f > ui.LogicalHeight) tip.Bottom(ui.LogicalHeight - top + 6f);
                else tip.Top(below);
                if (left > ui.LogicalWidth * 0.6f) tip.Right(ui.LogicalWidth - left - width);
                else tip.Left(left);
                return tip;
            }));
        }

        private static void InheritFont(MenUIContext ui, Element from, Element to)
        {
            int index = from.computed.FontIndex;
            if (ui.fontFamilies != null && index > 0 && index < ui.fontFamilies.Length)
            {
                to.FontFamily(ui.fontFamilies[index]);
            }
        }
    }

    internal sealed class ToastMessage
    {
        internal readonly int id;
        internal readonly string text;

        internal ToastMessage(int id, string text)
        {
            this.id = id;
            this.text = text;
        }
    }

    /// Runs an action once after a time, unless cancelled.
    internal sealed class Delay : IFrameTask
    {
        private readonly MenUIContext ui;
        private readonly Action action;
        private float remaining;
        private bool cancelled;

        internal Delay(MenUIContext ui, float seconds, Action action)
        {
            this.ui = ui;
            this.action = action;
            remaining = seconds;
        }

        internal void Cancel()
        {
            cancelled = true;
        }

        public bool Tick(float seconds)
        {
            if (cancelled)
            {
                return false;
            }
            remaining -= seconds;
            if (remaining > 0f)
            {
                return true;
            }
            Action run = action;
            ui.runtime.Batch(run);
            return false;
        }
    }

    public static class TooltipExtensions
    {
        /// Shows `text` near the element after the pointer rests on it.
        public static T Tooltip<T>(this T self, string text) where T : Element
        {
            Popups.Tooltip(self, text);
            return self;
        }
    }
}
