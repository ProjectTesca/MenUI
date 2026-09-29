// Material 3 text fields, filled and outlined: the label rests inside the
// field and floats up (smaller, in the primary color while focused) once the
// field is focused or has text. The text box fills the whole field, so a
// click anywhere on it opens VRChat's input.

using System;
using UnityEngine;

namespace MenUI.Material3
{
    internal static class MaterialFields
    {
        internal static Box TextField(MaterialDesign m3, Signal<string> value, string label, bool outlined,
            string supportingText, string leadingIcon)
        {
            m3.Ensure(MaterialStyles.TextField);
            MenUIContext ui = m3.ui;
            Box field = ui.Div().Tag("m3-text-field").Class(outlined ? "outlined" : "filled")
                .Class("populated", () => !string.IsNullOrEmpty(value.Value));
            if (!string.IsNullOrEmpty(leadingIcon))
            {
                field.Class("with-icon").Child(MaterialParts.Icon(m3, leadingIcon, 24f, false).Tag("m3-field-icon"));
            }
            TextBox input = ui.TextBox(value).Tag("m3-field-input");
            input.OnFocusChange(focused => field.SetClass("focused", focused));
            field.Child(input);
            if (!string.IsNullOrEmpty(label))
            {
                field.Child(ui.Text(label).Tag("m3-field-label"));
            }
            if (!outlined)
            {
                field.Child(ui.Div().Tag("m3-field-indicator"));
            }
            Box wrapper = ui.Div().Tag("m3-text-field-box").Child(field);
            if (!string.IsNullOrEmpty(supportingText))
            {
                wrapper.Child(ui.Text(supportingText).Tag("m3-field-support"));
            }
            return wrapper;
        }

        internal static void Rules(StyleSheet sheet, MaterialColorScheme c, MotionScheme motion)
        {
            float body = TypeScale.Size(TypeRole.BodyLarge);
            float small = TypeScale.Size(TypeRole.BodySmall);
            sheet
                .Rule("m3-text-field-box", s => s.Column().Gap(4).Width(280).MinWidth(120))
                .Rule("m3-text-field", s => s.Position(PositionMode.Relative).Height(56).FullWidth()
                    .TextColor(c.OnSurface).FontSize(body)
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects))
                .Rule("m3-text-field.filled", s => s.BorderRadius(4, 4, 0, 0).Background(c.SurfaceContainerHighest))
                .Rule("m3-text-field.filled:hover", s => s.Background(MaterialParts.Hover(c.SurfaceContainerHighest, c.OnSurface)))
                .Rule("m3-text-field.outlined", s => s.BorderRadius(4).Border(1, c.Outline))
                .Rule("m3-text-field.outlined:hover", s => s.BorderColor(c.OnSurface))
                .Rule("m3-text-field.outlined.focused", s => s.Border(2, c.Primary))
                // The text box covers the field (VRChat opens its input on a direct click).
                .Rule("m3-field-input", s => s.Absolute().Inset(0).Padding(24, 16, 8, 16).Background(Colors.Transparent)
                    .TextColor(c.OnSurface).FontSize(body))
                .Rule("m3-text-field.outlined > m3-field-input", s => s.Padding(16, 16, 16, 16))
                .Rule("m3-text-field.with-icon > m3-field-input", s => s.PaddingLeft(52))
                .Rule("m3-field-icon", s => s.Absolute().Left(12).Top(16).TextColor(c.OnSurfaceVariant).PointerEvents(PointerEventsMode.None))
                // The label: resting in the middle, floating when focused or filled in.
                .Rule("m3-field-label", s => s.Absolute().Left(16).Top(16).FontSize(body).TextColor(c.OnSurfaceVariant)
                    .PointerEvents(PointerEventsMode.None)
                    .Transition(TransitionGroup.Position, motion.FastSpatial)
                    .Transition(StyleProperty.FontSize, motion.FastSpatial)
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects))
                .Rule("m3-text-field.with-icon > m3-field-label", s => s.Left(52))
                .Rule("m3-text-field.filled.focused > m3-field-label, m3-text-field.filled.populated > m3-field-label",
                    s => s.Top(6).FontSize(small))
                // Outlined: the label sits on the border, over a patch of the surface.
                .Rule("m3-text-field.outlined > m3-field-label", s => s.Left(12).PaddingHorizontal(4)
                    .Background(MaterialParts.Alpha(c.Surface, 0f)))
                .Rule("m3-text-field.outlined.focused > m3-field-label, m3-text-field.outlined.populated > m3-field-label",
                    s => s.Top(-9).FontSize(small).Background(c.Surface))
                .Rule("m3-text-field.focused > m3-field-label", s => s.TextColor(c.Primary))
                // Filled: the line at the bottom, thicker and primary while focused.
                .Rule("m3-field-indicator", s => s.Absolute().Left(0).Right(0).Bottom(0).Height(1).Background(c.OnSurfaceVariant)
                    .PointerEvents(PointerEventsMode.None).Transition(StyleProperty.Height, motion.FastSpatial)
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects))
                .Rule("m3-text-field.focused > m3-field-indicator", s => s.Height(2).Background(c.Primary))
                .Rule("m3-field-support", s => s.PaddingHorizontal(16).FontSize(small).TextColor(c.OnSurfaceVariant));
        }
    }
}
