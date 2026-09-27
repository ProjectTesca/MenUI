// Chain methods for styling, named after the CSS properties they set.
// They work on elements and on `Style` objects alike:
//
//   Div().Flex().Column().Gap(8).Padding(16).Background(Colors.Hex(0x1E293B)).BorderRadius(12)
//   Style card = Style.New().Padding(16).BorderRadius(12);
//
// Lengths are logical pixels (fractions allowed) or `Length.Percent(...)`.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    public static class StyleExtensions
    {
        private static T Number<T>(T self, int property, float value) where T : IStyleTarget
        {
            if (self.StyleForWriting.SetNumber(property, value))
            {
                self.StyleWritten(property);
            }
            return self;
        }

        private static T EnumValue<T>(T self, int property, int value) where T : IStyleTarget
        {
            if (self.StyleForWriting.SetEnum(property, value))
            {
                self.StyleWritten(property);
            }
            return self;
        }

        private static T LengthValue<T>(T self, int property, Length value) where T : IStyleTarget
        {
            if (self.StyleForWriting.SetLength(property, value))
            {
                self.StyleWritten(property);
            }
            return self;
        }

        private static T ColorValue<T>(T self, int property, Color value) where T : IStyleTarget
        {
            if (self.StyleForWriting.SetColor(property, value))
            {
                self.StyleWritten(property);
            }
            return self;
        }

        // ---- display and flexbox -------------------------------------------

        public static T Display<T>(this T self, DisplayMode value) where T : IStyleTarget { return EnumValue(self, StyleProperty.Display, (int)value); }
        /// `display: flex`.
        public static T Flex<T>(this T self) where T : IStyleTarget { return EnumValue(self, StyleProperty.Display, (int)DisplayMode.Flex); }
        /// `display: flex; flex-direction: row`.
        public static T Row<T>(this T self) where T : IStyleTarget { return EnumValue(Flex(self), StyleProperty.Direction, (int)Direction.Row); }
        /// `display: flex; flex-direction: column`.
        public static T Column<T>(this T self) where T : IStyleTarget { return EnumValue(Flex(self), StyleProperty.Direction, (int)Direction.Column); }
        public static T FlexDirection<T>(this T self, Direction value) where T : IStyleTarget { return EnumValue(self, StyleProperty.Direction, (int)value); }
        public static T FlexWrap<T>(this T self, WrapMode value) where T : IStyleTarget { return EnumValue(self, StyleProperty.Wrap, (int)value); }
        public static T JustifyContent<T>(this T self, Justify value) where T : IStyleTarget { return EnumValue(self, StyleProperty.JustifyContent, (int)value); }
        public static T AlignItems<T>(this T self, Align value) where T : IStyleTarget { return EnumValue(self, StyleProperty.AlignItems, (int)value); }
        public static T AlignSelf<T>(this T self, Align value) where T : IStyleTarget { return EnumValue(self, StyleProperty.AlignSelf, (int)value); }
        public static T AlignContent<T>(this T self, Align value) where T : IStyleTarget { return EnumValue(self, StyleProperty.AlignContent, (int)value); }
        /// `display: flex` with the children centered on both axes
        /// (`justify-content` and `align-items: center`).
        public static T Center<T>(this T self) where T : IStyleTarget { return AlignItems(JustifyContent(Flex(self), Justify.Center), Align.Center); }
        public static T FlexGrow<T>(this T self, float value) where T : IStyleTarget { return Number(self, StyleProperty.FlexGrow, value); }
        public static T FlexShrink<T>(this T self, float value) where T : IStyleTarget { return Number(self, StyleProperty.FlexShrink, value); }
        public static T FlexBasis<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.FlexBasis, value); }
        public static T Gap<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(LengthValue(self, StyleProperty.RowGap, value), StyleProperty.ColumnGap, value); }
        public static T Gap<T>(this T self, Length row, Length column) where T : IStyleTarget { return LengthValue(LengthValue(self, StyleProperty.RowGap, row), StyleProperty.ColumnGap, column); }
        public static T RowGap<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.RowGap, value); }
        public static T ColumnGap<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.ColumnGap, value); }

        // ---- size ---------------------------------------------------------------

        public static T Width<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.Width, value); }
        public static T Height<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.Height, value); }
        public static T Size<T>(this T self, Length width, Length height) where T : IStyleTarget { return Height(Width(self, width), height); }
        public static T MinWidth<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.MinWidth, value); }
        public static T MinHeight<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.MinHeight, value); }
        public static T MaxWidth<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.MaxWidth, value); }
        public static T MaxHeight<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.MaxHeight, value); }
        /// `width: 100%`.
        public static T FullWidth<T>(this T self) where T : IStyleTarget { return Width(self, Length.Percent(100f)); }
        /// `height: 100%`.
        public static T FullHeight<T>(this T self) where T : IStyleTarget { return Height(self, Length.Percent(100f)); }
        /// `width: 100%; height: 100%`.
        public static T FullSize<T>(this T self) where T : IStyleTarget { return FullHeight(FullWidth(self)); }

        // ---- padding and margin -------------------------------------------------------

        public static T Padding<T>(this T self, Length all) where T : IStyleTarget { return Padding(self, all, all, all, all); }
        public static T Padding<T>(this T self, Length vertical, Length horizontal) where T : IStyleTarget { return Padding(self, vertical, horizontal, vertical, horizontal); }
        public static T Padding<T>(this T self, Length top, Length right, Length bottom, Length left) where T : IStyleTarget
        {
            LengthValue(self, StyleProperty.PaddingTop, top);
            LengthValue(self, StyleProperty.PaddingRight, right);
            LengthValue(self, StyleProperty.PaddingBottom, bottom);
            return LengthValue(self, StyleProperty.PaddingLeft, left);
        }
        public static T PaddingTop<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.PaddingTop, value); }
        public static T PaddingRight<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.PaddingRight, value); }
        public static T PaddingBottom<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.PaddingBottom, value); }
        public static T PaddingLeft<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.PaddingLeft, value); }
        public static T PaddingHorizontal<T>(this T self, Length value) where T : IStyleTarget { return PaddingRight(PaddingLeft(self, value), value); }
        public static T PaddingVertical<T>(this T self, Length value) where T : IStyleTarget { return PaddingBottom(PaddingTop(self, value), value); }

        public static T Margin<T>(this T self, Length all) where T : IStyleTarget { return Margin(self, all, all, all, all); }
        public static T Margin<T>(this T self, Length vertical, Length horizontal) where T : IStyleTarget { return Margin(self, vertical, horizontal, vertical, horizontal); }
        public static T Margin<T>(this T self, Length top, Length right, Length bottom, Length left) where T : IStyleTarget
        {
            LengthValue(self, StyleProperty.MarginTop, top);
            LengthValue(self, StyleProperty.MarginRight, right);
            LengthValue(self, StyleProperty.MarginBottom, bottom);
            return LengthValue(self, StyleProperty.MarginLeft, left);
        }
        public static T MarginTop<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.MarginTop, value); }
        public static T MarginRight<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.MarginRight, value); }
        public static T MarginBottom<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.MarginBottom, value); }
        public static T MarginLeft<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.MarginLeft, value); }
        public static T MarginHorizontal<T>(this T self, Length value) where T : IStyleTarget { return MarginRight(MarginLeft(self, value), value); }
        public static T MarginVertical<T>(this T self, Length value) where T : IStyleTarget { return MarginBottom(MarginTop(self, value), value); }

        // ---- positioning and stacking ----------------------------------------------------

        public static T Position<T>(this T self, PositionMode value) where T : IStyleTarget { return EnumValue(self, StyleProperty.Position, (int)value); }
        /// `position: absolute` (placed against the parent's padding box).
        public static T Absolute<T>(this T self) where T : IStyleTarget { return Position(self, PositionMode.Absolute); }
        public static T Top<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.Top, value); }
        public static T Right<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.Right, value); }
        public static T Bottom<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.Bottom, value); }
        public static T Left<T>(this T self, Length value) where T : IStyleTarget { return LengthValue(self, StyleProperty.Left, value); }
        public static T Inset<T>(this T self, Length all) where T : IStyleTarget { return Left(Bottom(Right(Top(self, all), all), all), all); }
        public static T Inset<T>(this T self, Length top, Length right, Length bottom, Length left) where T : IStyleTarget { return Left(Bottom(Right(Top(self, top), right), bottom), left); }
        /// Sets `z-index`, which also starts a stacking context.
        public static T ZIndex<T>(this T self, int value) where T : IStyleTarget { return Number(self, StyleProperty.ZIndex, value); }
        public static T Overflow<T>(this T self, OverflowMode value) where T : IStyleTarget { return EnumValue(self, StyleProperty.Overflow, (int)value); }
        /// `overflow: hidden`.
        public static T OverflowHidden<T>(this T self) where T : IStyleTarget { return Overflow(self, OverflowMode.Hidden); }

        // ---- background, border, shadow ------------------------------------------------------

        public static T Background<T>(this T self, Color value) where T : IStyleTarget { return ColorValue(self, StyleProperty.Background, value); }
        public static T Border<T>(this T self, Length width, Color color) where T : IStyleTarget { return BorderColor(BorderWidth(self, width), color); }
        public static T BorderWidth<T>(this T self, Length all) where T : IStyleTarget { return BorderWidth(self, all, all, all, all); }
        public static T BorderWidth<T>(this T self, Length top, Length right, Length bottom, Length left) where T : IStyleTarget
        {
            LengthValue(self, StyleProperty.BorderTopWidth, top);
            LengthValue(self, StyleProperty.BorderRightWidth, right);
            LengthValue(self, StyleProperty.BorderBottomWidth, bottom);
            return LengthValue(self, StyleProperty.BorderLeftWidth, left);
        }
        public static T BorderColor<T>(this T self, Color value) where T : IStyleTarget { return ColorValue(self, StyleProperty.BorderColor, value); }
        /// Corner radius; a percentage is of the shorter side (50% makes a circle or a pill).
        public static T BorderRadius<T>(this T self, Length all) where T : IStyleTarget { return BorderRadius(self, all, all, all, all); }
        public static T BorderRadius<T>(this T self, Length topLeft, Length topRight, Length bottomRight, Length bottomLeft) where T : IStyleTarget
        {
            LengthValue(self, StyleProperty.CornerTopLeft, topLeft);
            LengthValue(self, StyleProperty.CornerTopRight, topRight);
            LengthValue(self, StyleProperty.CornerBottomRight, bottomRight);
            return LengthValue(self, StyleProperty.CornerBottomLeft, bottomLeft);
        }
        /// `box-shadow`; several layers allowed, the first on top.
        public static T BoxShadow<T>(this T self, params Shadow[] layers) where T : IStyleTarget
        {
            self.StyleForWriting.SetShadows(layers);
            self.StyleWritten(StyleProperty.Shadows);
            return self;
        }
        public static T Opacity<T>(this T self, float value) where T : IStyleTarget { return Number(self, StyleProperty.Opacity, value); }
        public static T Visibility<T>(this T self, VisibilityMode value) where T : IStyleTarget { return EnumValue(self, StyleProperty.Visibility, (int)value); }
        public static T PointerEvents<T>(this T self, PointerEventsMode value) where T : IStyleTarget { return EnumValue(self, StyleProperty.PointerEvents, (int)value); }

        // ---- text ------------------------------------------------------------------------------

        public static T FontSize<T>(this T self, float value) where T : IStyleTarget { return Number(self, StyleProperty.FontSize, value); }
        /// CSS `color`.
        public static T TextColor<T>(this T self, Color value) where T : IStyleTarget { return ColorValue(self, StyleProperty.TextColor, value); }
        public static T TextAlign<T>(this T self, TextAlignMode value) where T : IStyleTarget { return EnumValue(self, StyleProperty.TextAlign, (int)value); }
        /// CSS `font-family`: a family registered in MenUI Settings, or a
        /// comma-separated list of candidates (the first registered one is
        /// used; none registered falls back to the default font). Inherited.
        public static T FontFamily<T>(this T self, string families) where T : IStyleTarget
        {
            if (self.StyleForWriting.SetFontFamily(families))
            {
                self.StyleWritten(StyleProperty.FontFamily);
            }
            return self;
        }

        // ---- composition ---------------------------------------------------------------------

        /// Copies every property `style` sets (later calls still override).
        public static T Apply<T>(this T self, Style style) where T : IStyleTarget
        {
            Style target = self.StyleForWriting;
            target.CopyFrom(style);
            for (int i = 0; i < style.setCount; i++)
            {
                self.StyleWritten(style.setOrder[i]);
            }
            return self;
        }
    }

    /// Element-only chain methods: state variants and reactive bindings.
    public static class ElementExtensions
    {
        /// Adds classes (space separated): `.Class("card selected")`.
        public static T Class<T>(this T self, string names) where T : Element
        {
            string[] parts = names.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    self.SetClass(parts[i], true);
                }
            }
            return self;
        }

        /// Has the class while `when` is true (like classList.toggle).
        public static T Class<T>(this T self, string name, Func<bool> when) where T : Element
        {
            self.context.Bind(() => self.SetClass(name, when()));
            return self;
        }

        /// Classes computed reactively; the ones it set before are replaced.
        public static T Class<T>(this T self, Func<string> names) where T : Element
        {
            List<string> previous = new List<string>();
            self.context.Bind(() =>
            {
                string[] parts = names().Split(' ');
                List<string> next = new List<string>();
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length > 0) next.Add(parts[i]);
                }
                for (int i = 0; i < previous.Count; i++)
                {
                    if (!next.Contains(previous[i])) self.SetClass(previous[i], false);
                }
                for (int i = 0; i < next.Count; i++)
                {
                    self.SetClass(next[i], true);
                }
                previous.Clear();
                previous.AddRange(next);
            });
            return self;
        }

        /// The tag selectors match (`div`, `text`, `scroll-view` by default).
        public static T Tag<T>(this T self, string tag) where T : Element
        {
            self.SetTag(tag);
            return self;
        }

        /// `:disabled` while `when` is true; a disabled element's own pointer
        /// handlers do not run.
        public static T Disabled<T>(this T self, Func<bool> when) where T : Element
        {
            self.context.Bind(() => self.SetPseudoState(PseudoState.Disabled, when()));
            return self;
        }

        /// `:hover` variant: `.Hover(s => s.Background(...))`.
        public static T Hover<T>(this T self, Action<Style> configure) where T : Element { return Variant(self, PseudoState.Hover, configure); }
        /// `:active` variant (while pressed).
        public static T Active<T>(this T self, Action<Style> configure) where T : Element { return Variant(self, PseudoState.Active, configure); }
        /// `:focus` variant.
        public static T Focus<T>(this T self, Action<Style> configure) where T : Element { return Variant(self, PseudoState.Focus, configure); }
        /// `:disabled` variant.
        public static T Disabled<T>(this T self, Action<Style> configure) where T : Element { return Variant(self, PseudoState.Disabled, configure); }

        internal static T Variant<T>(T self, int state, Action<Style> configure) where T : Element
        {
            configure(self.GetOrCreateVariant(state));
            self.context.hitVersion++;   // a :hover/:active style makes it hittable
            if ((self.pseudoStates & state) != 0)
            {
                self.MarkStyleDirty();
            }
            return self;
        }

        /// Reruns `apply` whenever a signal it reads changes:
        /// `.Bind(e => e.Background(selected.Value ? on : off).Width(size.Value))`.
        public static T Bind<T>(this T self, Action<T> apply) where T : Element
        {
            self.context.Bind(() => apply(self));
            return self;
        }

        /// CSS `transform: translate(x, y)` in logical pixels, applied on the
        /// GPU: changing it neither lays out nor repaints (good for animation).
        public static T Translate<T>(this T self, Func<float> x, Func<float> y) where T : Element
        {
            MenUIContext context = self.context;
            if (self.translateGroup == TransformGroups.Root)
            {
                int group = context.groups.Allocate();
                if (group == TransformGroups.Root)
                {
                    return self;
                }
                self.translateGroup = group;
                context.OnCleanup(() => context.groups.Free(group));
                self.MarkLayoutDirty(false);
            }
            int owned = self.translateGroup;
            context.Bind(() =>
            {
                float scale = context.scale;
                context.groups.SetOffset(owned, Mathf.Round(x() * scale), Mathf.Round(y() * scale));
            });
            return self;
        }

        public static T Translate<T>(this T self, float x, float y) where T : Element
        {
            return Translate(self, () => x, () => y);
        }

        public static T Background<T>(this T self, Func<Color> value) where T : Element { self.context.Bind(() => self.Background(value())); return self; }
        public static T TextColor<T>(this T self, Func<Color> value) where T : Element { self.context.Bind(() => self.TextColor(value())); return self; }
        public static T Opacity<T>(this T self, Func<float> value) where T : Element { self.context.Bind(() => self.Opacity(value())); return self; }
        public static T Width<T>(this T self, Func<Length> value) where T : Element { self.context.Bind(() => self.Width(value())); return self; }
        public static T Height<T>(this T self, Func<Length> value) where T : Element { self.context.Bind(() => self.Height(value())); return self; }
        public static T ZIndex<T>(this T self, Func<int> value) where T : Element { self.context.Bind(() => self.ZIndex(value())); return self; }
        public static T Display<T>(this T self, Func<DisplayMode> value) where T : Element { self.context.Bind(() => self.Display(value())); return self; }
    }
}
