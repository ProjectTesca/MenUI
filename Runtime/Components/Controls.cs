// Built-in controls. Each is ordinary elements with tags (so style sheets
// can restyle them), bound to signals you pass in — the value lives in your
// signal, the control only shows and changes it.
//
//   Button("Save", ButtonVariant.Primary).OnClick(Save);
//   Checkbox(agreed, "I agree");
//   Switch(muted, "Mute");
//   Radio(size, Size.Large, "Large");
//   Slider(volume, 0f, 1f);
//   ProgressBar(() => loaded.Value / total);
//   Tabs(page, "General", "Audio", "About");
//
// Disable any of them with `.Disabled(() => ...)`.

using System;
using UnityEngine;

namespace MenUI
{
    public enum ButtonVariant { Secondary, Primary, Danger, Ghost }

    internal static class Controls
    {
        internal static Box Button(MenUIContext ui, Element content, ButtonVariant variant)
        {
            ui.EnsureStyles(ComponentStyles.Button);
            Box button = ui.Div().Tag("button");
            if (variant == ButtonVariant.Primary) button.Class("primary");
            else if (variant == ButtonVariant.Danger) button.Class("danger");
            else if (variant == ButtonVariant.Ghost) button.Class("ghost");
            return button.Child(content);
        }

        internal static Box Checkbox(MenUIContext ui, Signal<bool> isChecked, string label)
        {
            ui.EnsureStyles(ComponentStyles.Checkbox);
            Box mark = ui.Div().Tag("checkbox").Class("checked", () => isChecked.Value)
                .Child(Symbol(ui, "✓"));
            Box row = ui.Div().Tag("label").Class("checked", () => isChecked.Value).Child(mark)
                .OnClick(() => isChecked.Value = !isChecked.Peek());
            return WithLabel(ui, row, label);
        }

        internal static Box Switch(MenUIContext ui, Signal<bool> isOn, string label)
        {
            ui.EnsureStyles(ComponentStyles.Switch);
            // The thumb slides: 0 off, 1 on.
            AnimatedFloat position = ui.Animated(isOn.Peek() ? 1f : 0f);
            AnimationSpec slide = AnimationSpec.Spring(900f, 0.85f);
            ui.Bind(() =>
            {
                bool on = isOn.Value;
                position.AnimateTo(on ? 1f : 0f, slide);
            });
            Box thumb = ui.Div().Tag("switch-thumb").Bind(e => e.Left(2f + 16f * position.Value));
            Box track = ui.Div().Tag("switch").Class("checked", () => isOn.Value).Child(thumb);
            Box row = ui.Div().Tag("label").Class("checked", () => isOn.Value).Child(track)
                .OnClick(() => isOn.Value = !isOn.Peek());
            return WithLabel(ui, row, label);
        }

        internal static Box Radio<T>(MenUIContext ui, Signal<T> selected, T value, string label)
        {
            ui.EnsureStyles(ComponentStyles.Radio);
            Func<bool> isSelected = () => object.Equals(selected.Value, value);
            Box circle = ui.Div().Tag("radio").Class("checked", isSelected).Child(ui.Div().Tag("radio-dot"));
            Box row = ui.Div().Tag("label").Class("checked", isSelected).Child(circle)
                .OnClick(() => selected.Value = value);
            return WithLabel(ui, row, label);
        }

        internal static Box Slider(MenUIContext ui, Signal<float> value, float minimum, float maximum, float step)
        {
            ui.EnsureStyles(ComponentStyles.Slider);
            Func<float> percent = () => maximum > minimum
                ? Mathf.Clamp01((value.Value - minimum) / (maximum - minimum)) * 100f
                : 0f;
            Box fill = ui.Div().Tag("slider-fill").Width(() => Length.Percent(percent()));
            Box thumb = ui.Div().Tag("slider-thumb").Bind(e => e.Left(Length.Percent(percent())));
            Box slider = ui.Div().Tag("slider").Children(ui.Div().Tag("slider-rail"), fill, thumb);

            // Press anywhere on it and drag; the pointer is captured, so the
            // drag goes on outside it, and a scroll view around it stays put.
            int dragging = -1;
            Action<PointerEvent> follow = e =>
            {
                float width = slider.width / ui.scale;
                if (width <= 0f)
                {
                    return;
                }
                float next = minimum + Mathf.Clamp01(e.LocalX / width) * (maximum - minimum);
                if (step > 0f)
                {
                    next = Mathf.Clamp(minimum + Mathf.Round((next - minimum) / step) * step, minimum, maximum);
                }
                value.Value = next;
            };
            slider.OnPointerDown(e =>
            {
                dragging = e.PointerId;
                follow(e);
                e.StopPropagation();
            });
            slider.OnPointerMove(e =>
            {
                if (e.PointerId == dragging)
                {
                    follow(e);
                    e.StopPropagation();
                }
            });
            slider.OnPointerUp(e =>
            {
                if (e.PointerId == dragging)
                {
                    dragging = -1;
                }
            });
            return slider;
        }

        internal static Box ProgressBar(MenUIContext ui, Func<float> progress)
        {
            ui.EnsureStyles(ComponentStyles.Progress);
            Box fill = ui.Div().Tag("progress-fill").Width(() => Length.Percent(Mathf.Clamp01(progress()) * 100f));
            return ui.Div().Tag("progress").Child(fill);
        }

        internal static Box Spinner(MenUIContext ui)
        {
            ui.EnsureStyles(ComponentStyles.Spinner);
            // 0 → 1 once per 1.2 s, forever.
            AnimatedFloat phase = ui.Animated(0f);
            phase.Play(new Keyframes(0f).To(1f, 1.2f), true);
            Box spinner = ui.Div().Tag("spinner");
            for (int i = 0; i < 3; i++)
            {
                float offset = i / 3f;
                spinner.Child(ui.Div().Tag("spinner-dot").Opacity(() =>
                    0.25f + 0.75f * (0.5f + 0.5f * Mathf.Cos((phase.Value - offset) * Mathf.PI * 2f))));
            }
            return spinner;
        }

        internal static Box Tabs(MenUIContext ui, Signal<int> selected, string[] labels)
        {
            ui.EnsureStyles(ComponentStyles.Tabs);
            Box bar = ui.Div().Tag("tabs");
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                bar.Child(ui.Div().Tag("tab").Class("selected", () => selected.Value == index)
                    .OnClick(() => selected.Value = index)
                    .Child(ui.Text(labels[index])));
            }
            return bar;
        }

        internal static Box Divider(MenUIContext ui)
        {
            ui.EnsureStyles(ComponentStyles.Divider);
            return ui.Div().Tag("divider");
        }

        private static Box WithLabel(MenUIContext ui, Box row, string label)
        {
            if (!string.IsNullOrEmpty(label))
            {
                row.Child(ui.Text(label));
            }
            return row;
        }

        /// A symbol from the bundled font (✓ ▼), whatever font is around it.
        internal static TextElement Symbol(MenUIContext ui, string symbol)
        {
            TextElement text = ui.Text(symbol);
            if (ui.fontFamilies != null && ui.fontFamilies.Length > 0)
            {
                text.FontFamily(ui.fontFamilies[0]);
            }
            return text;
        }
    }
}
