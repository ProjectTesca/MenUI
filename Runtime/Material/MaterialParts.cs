// Pieces the Material components share: icons, the press ripple, the rule
// groups of the Material style sheet, and small helpers.

using System;
using UnityEngine;

namespace MenUI.Material3
{
    /// The five sizes of Expressive buttons (XS 32, S 40, M 56, L 96, XL 136 px tall).
    public enum ComponentSize { ExtraSmall, Small, Medium, Large, ExtraLarge }

    public enum ButtonShape { Round, Square }

    /// Which rule groups the Material sheet holds (bits), and the rules.
    internal static class MaterialStyles
    {
        internal const int Base = 1;
        internal const int Button = 2;
        internal const int IconButton = 4;
        internal const int Fab = 8;
        internal const int ButtonGroup = 16;
        internal const int Toolbar = 32;
        internal const int Selection = 64;
        internal const int TextField = 128;
        internal const int Containers = 256;
        internal const int Navigation = 512;
        internal const int Overlays = 1024;
        internal const int Tooltip = 2048;
        internal const int Last = Tooltip;

        internal static void Add(StyleSheet sheet, MaterialColorScheme c, MotionScheme motion, int kind)
        {
            if (kind == Base) AddBase(sheet, c, motion);
            else if (kind == Button) MaterialButtons.ButtonRules(sheet, c, motion);
            else if (kind == IconButton) MaterialButtons.IconButtonRules(sheet, c, motion);
            else if (kind == Fab) MaterialButtons.FabRules(sheet, c, motion);
            else if (kind == ButtonGroup) MaterialButtons.GroupRules(sheet, c, motion);
            else if (kind == Toolbar) MaterialBars.Rules(sheet, c, motion);
            else if (kind == Selection) MaterialSelection.Rules(sheet, c, motion);
            else if (kind == TextField) MaterialFields.Rules(sheet, c, motion);
            else if (kind == Containers) MaterialContainers.Rules(sheet, c, motion);
            else if (kind == Navigation) MaterialNavigation.Rules(sheet, c, motion);
            else if (kind == Overlays) MaterialOverlays.Rules(sheet, c, motion);
            else if (kind == Tooltip) MaterialOverlays.TooltipRules(sheet, c);
        }

        private static void AddBase(StyleSheet sheet, MaterialColorScheme c, MotionScheme motion)
        {
            sheet
                .Rule("m3-surface", s => s.Background(c.Surface).TextColor(c.OnSurface).FontSize(TypeScale.Size(TypeRole.BodyLarge))
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects))
                .Rule("m3-icon", s => s.FontFamily(MaterialDesign.IconFont).TextAlign(TextAlignMode.Center).FlexShrink(0))
                .Rule("m3-icon.filled", s => s.FontFamily(MaterialDesign.FilledIconFont))
                .Rule("m3-ripple", s => s.Absolute().Inset(0).PointerEvents(PointerEventsMode.None));
        }
    }

    internal static class MaterialParts
    {
        /// A Material Symbol in the icon font; `size` 0 leaves the size to rules.
        internal static TextElement Icon(MaterialDesign m3, string symbol, float size, bool filled)
        {
            m3.Ensure(MaterialStyles.Base);
            TextElement icon = m3.ui.Text(symbol).Tag("m3-icon");
            if (size > 0f)
            {
                icon.FontSize(size);
            }
            if (filled)
            {
                icon.Class("filled");
            }
            return icon;
        }

        /// The ink of a press: a circle that grows from the pointer and fades
        /// when released, in the host's content color, clipped to the host's
        /// shape. One draw record, drawn only while visible.
        internal static Graphic Ripple(MaterialDesign m3, Element host)
        {
            m3.Ensure(MaterialStyles.Base);
            MenUIContext ui = m3.ui;
            Signal<float> centerX = ui.Signal(0f);
            Signal<float> centerY = ui.Signal(0f);
            Signal<Color> color = ui.Signal(Colors.Black);
            AnimatedFloat radius = ui.Animated(0f);
            AnimatedFloat alpha = ui.Animated(0f);
            radius.Precision = 0.1f;
            float reach = 0f;
            Graphic ripple = ui.Graphic(g =>
            {
                Color ink = color.Value;
                ink.a *= alpha.Value;
                g.Ripple(centerX.Value, centerY.Value, radius.Value, ink, 1f);
            }).Tag("m3-ripple").ClipToParent();

            AnimationSpec grow = AnimationSpec.Tween(0.45f, Easing.CubicBezier(0.2f, 0f, 0f, 1f));
            AnimationSpec fade = AnimationSpec.Tween(0.3f, Easing.Linear);
            host.OnPointerDown(e =>
            {
                if ((host.pseudoStates & PseudoState.Disabled) != 0)
                {
                    return;
                }
                float width = host.width / ui.scale, height = host.height / ui.scale;
                float dx = Mathf.Max(e.LocalX, width - e.LocalX);
                float dy = Mathf.Max(e.LocalY, height - e.LocalY);
                reach = Mathf.Sqrt(dx * dx + dy * dy);
                ui.Batch(() =>
                {
                    centerX.Value = e.LocalX;
                    centerY.Value = e.LocalY;
                    color.Value = host.computed.ColorOf(StyleProperty.TextColor);
                    radius.Set(reach * 0.2f);
                    radius.AnimateTo(reach, grow);
                    alpha.Set(StateLayer.Pressed * 1.2f);
                });
            });
            Action<PointerEvent> release = e => alpha.AnimateTo(0f, fade);
            host.OnPointerUp(release);
            host.OnPointerLeave(release);
            return ripple;
        }

        /// A value per component size.
        internal static float BySize(ComponentSize size, float extraSmall, float small, float medium, float large, float extraLarge)
        {
            if (size == ComponentSize.ExtraSmall) return extraSmall;
            if (size == ComponentSize.Small) return small;
            if (size == ComponentSize.Medium) return medium;
            if (size == ComponentSize.Large) return large;
            return extraLarge;
        }

        internal static string SizeClass(ComponentSize size)
        {
            if (size == ComponentSize.ExtraSmall) return "xs";
            if (size == ComponentSize.Small) return "s";
            if (size == ComponentSize.Medium) return "m";
            if (size == ComponentSize.Large) return "l";
            return "xl";
        }

        internal static Color Hover(Color container, Color content)
        {
            return StateLayer.Over(container, content, StateLayer.Hover);
        }

        internal static Color Pressed(Color container, Color content)
        {
            return StateLayer.Over(container, content, StateLayer.Pressed);
        }

        internal static Color Alpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }
    }
}
