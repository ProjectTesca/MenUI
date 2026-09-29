// Material 3 selection controls: checkbox, radio button, switch, and the
// M3 Expressive slider (a thick track split by a bar handle).

using System;
using UnityEngine;

namespace MenUI.Material3
{
    internal static class MaterialSelection
    {
        /// The row a control sits in, with its label; clicking anywhere on it
        /// acts, and hovering or pressing it shows on the control (classes
        /// `hover` and `pressed`: selectors cannot follow an ancestor's state).
        private static Box Row(MaterialDesign m3, Box control, string label)
        {
            Box row = m3.ui.Div().Tag("m3-selection").Child(control);
            if (!string.IsNullOrEmpty(label))
            {
                row.Child(m3.ui.Text(label));
            }
            MirrorStates(row, control);
            return row;
        }

        internal static void MirrorStates(Element from, Element to)
        {
            from.OnPointerEnter(e => to.SetClass("hover", true));
            from.OnPointerLeave(e =>
            {
                to.SetClass("hover", false);
                to.SetClass("pressed", false);
            });
            from.OnPointerDown(e => to.SetClass("pressed", true));
            from.OnPointerUp(e => to.SetClass("pressed", false));
        }

        internal static Box Checkbox(MaterialDesign m3, Signal<bool> isChecked, string label)
        {
            m3.Ensure(MaterialStyles.Selection);
            MenUIContext ui = m3.ui;
            Func<bool> on = () => isChecked.Value;
            Box box = ui.Div().Tag("m3-checkbox").Class("checked", on)
                .Child(MaterialParts.Icon(m3, MaterialSymbol.Check, 0f, false).Tag("m3-check-mark"));
            Box target = ui.Div().Tag("m3-selection-target").Class("checked", on).Child(box);
            target.Child(MaterialParts.Ripple(m3, target));
            return Row(m3, target, label).OnClick(() => isChecked.Value = !isChecked.Peek());
        }

        internal static Box Radio<T>(MaterialDesign m3, Signal<T> selected, T value, string label)
        {
            m3.Ensure(MaterialStyles.Selection);
            MenUIContext ui = m3.ui;
            Func<bool> on = () => object.Equals(selected.Value, value);
            Box ring = ui.Div().Tag("m3-radio").Class("checked", on).Child(ui.Div().Tag("m3-radio-dot"));
            Box target = ui.Div().Tag("m3-selection-target").Class("checked", on).Child(ring);
            target.Child(MaterialParts.Ripple(m3, target));
            return Row(m3, target, label).OnClick(() => selected.Value = value);
        }

        internal static Box Switch(MaterialDesign m3, Signal<bool> isOn, string label, bool icons)
        {
            m3.Ensure(MaterialStyles.Selection);
            MenUIContext ui = m3.ui;
            Func<bool> on = () => isOn.Value;
            TextElement icon = ui.Text(() => isOn.Value ? MaterialSymbol.Check : MaterialSymbol.Close).Tag("m3-icon");
            Box thumb = ui.Div().Tag("m3-switch-thumb");
            if (icons)
            {
                thumb.Child(icon);
            }
            Box track = ui.Div().Tag("m3-switch").Class("checked", on).Child(thumb);
            if (icons)
            {
                track.Class("icons");
            }
            return Row(m3, track, label).OnClick(() => isOn.Value = !isOn.Peek());
        }

        internal static Box Slider(MaterialDesign m3, Signal<float> value, float minimum, float maximum, float step)
        {
            m3.Ensure(MaterialStyles.Selection);
            MenUIContext ui = m3.ui;
            Signal<bool> dragging = ui.Signal(false);
            AnimatedFloat handleWidth = ui.Animated(4f);
            const float TrackHeight = 16f;
            const float HandleGap = 6f;
            Func<float> fraction = () => maximum > minimum ? Mathf.Clamp01((value.Value - minimum) / (maximum - minimum)) : 0f;

            Graphic drawing = ui.Graphic(g =>
            {
                MaterialColorScheme c = m3.Scheme;
                float width = g.Width, height = g.Height;
                float handle = handleWidth.Value;
                float p = fraction();
                float center = p * width;
                float top = (height - TrackHeight) * 0.5f;
                float activeEnd = center - handle * 0.5f - HandleGap;
                float inactiveStart = center + handle * 0.5f + HandleGap;
                // Active track: round outside, nearly square next to the handle.
                g.Rectangle(0f, top, Mathf.Max(0f, activeEnd), TrackHeight, 8f, 2f, 2f, 8f,
                    activeEnd > 0f ? c.Primary : Colors.Transparent);
                g.Rectangle(inactiveStart, top, Mathf.Max(0f, width - inactiveStart), TrackHeight, 2f, 8f, 8f, 2f,
                    inactiveStart < width ? c.SecondaryContainer : Colors.Transparent);
                // The stop indicator at the end.
                g.Circle(width - 8f, height * 0.5f, 2f, p < 1f && inactiveStart < width - 12f ? c.Primary : Colors.Transparent);
                g.Rectangle(center - handle * 0.5f, 0f, handle, height, handle * 0.5f, c.Primary);
            }).FullSize();

            Box slider = ui.Div().Tag("m3-slider").Child(drawing);
            // The value while dragging, in a bubble above the handle.
            slider.Child(ui.Show(() => dragging.Value, () =>
                ui.Div().Tag("m3-slider-value").Bind(e => e.Left(Length.Percent(fraction() * 100f)))
                    .Child(ui.Text(() => (step >= 1f ? Mathf.Round(value.Value).ToString() : value.Value.ToString("0.##"))))));

            int pointer = -1;
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
            AnimationSpec press = m3.Motion.FastSpatial;
            slider.OnPointerDown(e =>
            {
                if ((slider.pseudoStates & PseudoState.Disabled) != 0)
                {
                    return;
                }
                pointer = e.PointerId;
                dragging.Value = true;
                handleWidth.AnimateTo(2f, press);
                follow(e);
                e.StopPropagation();
            });
            slider.OnPointerMove(e =>
            {
                if (e.PointerId == pointer)
                {
                    follow(e);
                    e.StopPropagation();
                }
            });
            slider.OnPointerUp(e =>
            {
                if (e.PointerId == pointer)
                {
                    pointer = -1;
                    dragging.Value = false;
                    handleWidth.AnimateTo(4f, press);
                }
            });
            return slider;
        }

        internal static void Rules(StyleSheet sheet, MaterialColorScheme c, MotionScheme motion)
        {
            Color onSurfaceVariant = c.OnSurfaceVariant;
            sheet
                .Rule("m3-selection", s => s.Row().AlignItems(Align.Center).Gap(4).Width(Length.FitContent)
                    .TextColor(c.OnSurface).FontSize(TypeScale.Size(TypeRole.BodyLarge)))
                .Rule("m3-selection:disabled", s => s.Opacity(StateLayer.DisabledContent))
                // The 40 px target around a control carries its state layer and ripple.
                .Rule("m3-selection-target", s => s.Center().Size(40, 40).FlexShrink(0).BorderRadius(20)
                    .Position(PositionMode.Relative).TextColor(onSurfaceVariant)
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects))
                .Rule("m3-selection-target.checked", s => s.TextColor(c.Primary))
                .Rule("m3-selection-target.hover", s => s.Background(MaterialParts.Alpha(c.OnSurface, StateLayer.Hover)))
                .Rule("m3-selection-target.hover.checked", s => s.Background(MaterialParts.Alpha(c.Primary, StateLayer.Hover)))
                // Checkbox.
                .Rule("m3-checkbox", s => s.Center().Size(18, 18).BorderRadius(2).Border(2, onSurfaceVariant)
                    .Background(MaterialParts.Alpha(c.Primary, 0f))
                    .Transition(TransitionGroup.Colors, motion.FastEffects))
                .Rule("m3-checkbox.checked", s => s.Background(c.Primary).BorderColor(c.Primary))
                .Rule("m3-check-mark", s => s.FontSize(16).TextColor(c.OnPrimary).Opacity(0f).Scale(0.4f)
                    .Transition(StyleProperty.Opacity, motion.FastEffects).Transition(TransitionGroup.Transform, motion.FastSpatial))
                .Rule("m3-checkbox.checked > m3-check-mark", s => s.Opacity(1f).Scale(1f))
                // Radio button.
                .Rule("m3-radio", s => s.Center().Size(20, 20).BorderRadius(10).Border(2, onSurfaceVariant)
                    .Transition(TransitionGroup.Colors, motion.FastEffects))
                .Rule("m3-radio.checked", s => s.BorderColor(c.Primary))
                .Rule("m3-radio-dot", s => s.Size(10, 10).BorderRadius(5).Background(c.Primary).Scale(0f)
                    .Transition(TransitionGroup.Transform, motion.FastSpatial))
                .Rule("m3-radio.checked > m3-radio-dot", s => s.Scale(1f))
                // Switch: the thumb grows when on (and more while pressed).
                .Rule("m3-switch", s => s.Size(52, 32).FlexShrink(0).BorderRadius(16).Border(2, c.Outline)
                    .Background(c.SurfaceContainerHighest).Position(PositionMode.Relative).Margin(4)
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects))
                .Rule("m3-switch.checked", s => s.Background(c.Primary).BorderColor(c.Primary))
                .Rule("m3-switch-thumb", s => s.Absolute().Center().Left(6).Top(6).Size(16, 16).BorderRadius(14)
                    .Background(c.Outline).FontSize(16).TextColor(c.SurfaceContainerHighest)
                    .Transition(TransitionGroup.Position, motion.FastSpatial).Transition(TransitionGroup.Size, motion.FastSpatial)
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects))
                .Rule("m3-switch.icons > m3-switch-thumb", s => s.Left(2).Top(2).Size(24, 24))
                .Rule("m3-switch.checked > m3-switch-thumb", s => s.Left(22).Top(2).Size(24, 24)
                    .Background(c.OnPrimary).TextColor(c.OnPrimaryContainer))
                .Rule("m3-switch.pressed > m3-switch-thumb", s => s.Left(0).Top(0).Size(28, 28))
                .Rule("m3-switch.pressed.checked > m3-switch-thumb", s => s.Left(20).Top(0).Size(28, 28))
                .Rule("m3-switch-thumb > m3-icon", s => s.FontSize(16))
                // Slider.
                .Rule("m3-slider", s => s.Height(44).MinWidth(120).Position(PositionMode.Relative))
                .Rule("m3-slider:disabled", s => s.Opacity(StateLayer.DisabledContent))
                .Rule("m3-slider-value", s => s.Absolute().Bottom(52).MarginLeft(-24).Width(48).Height(44).Center()
                    .BorderRadius(22).Background(c.InverseSurface).TextColor(c.InverseOnSurface)
                    .FontSize(TypeScale.Size(TypeRole.LabelLarge)).PointerEvents(PointerEventsMode.None)
                    .Transition(StyleProperty.Opacity, motion.FastEffects).Transition(TransitionGroup.Transform, motion.FastSpatial))
                .Rule("m3-slider-value:enter-from", s => s.Opacity(0f).Scale(0.6f).TranslateY(10))
                .Rule("m3-slider-value:exit-to", s => s.Opacity(0f).Scale(0.6f).TranslateY(10));
        }
    }
}
