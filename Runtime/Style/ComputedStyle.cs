using UnityEngine;

namespace MenUI
{
    /// An element's resolved style: defaults, then inherited values, then the
    /// element's own style and active state variants — with every pixel
    /// length already snapped to whole physical pixels.
    public sealed class ComputedStyle
    {
        internal readonly float[] numbers = new float[StyleProperty.Count];
        internal readonly int[] units = new int[StyleProperty.Count];
        internal readonly Color[] colors = new Color[StyleProperty.Count];
        internal readonly bool[] isSet = new bool[StyleProperty.Count];
        internal Shadow[] shadows;   // physical pixels

        internal void Reset()
        {
            for (int i = 0; i < StyleProperty.Count; i++)
            {
                numbers[i] = 0f;
                units[i] = (int)LengthUnit.Pixels;
                isSet[i] = false;
            }
            shadows = null;

            numbers[StyleProperty.Display] = (int)DisplayMode.Block;
            numbers[StyleProperty.Direction] = (int)Direction.Row;
            numbers[StyleProperty.Wrap] = (int)WrapMode.NoWrap;
            numbers[StyleProperty.JustifyContent] = (int)Justify.Start;
            numbers[StyleProperty.AlignItems] = (int)Align.Stretch;
            numbers[StyleProperty.AlignSelf] = (int)Align.Auto;
            numbers[StyleProperty.AlignContent] = (int)Align.Stretch;
            numbers[StyleProperty.Position] = (int)PositionMode.Relative;
            numbers[StyleProperty.Overflow] = (int)OverflowMode.Visible;
            numbers[StyleProperty.FlexShrink] = 1f;
            units[StyleProperty.FlexBasis] = (int)LengthUnit.Auto;
            units[StyleProperty.Width] = (int)LengthUnit.Auto;
            units[StyleProperty.Height] = (int)LengthUnit.Auto;
            // min-width/height: auto — 0 in general, content-based for flex items.
            units[StyleProperty.MinWidth] = (int)LengthUnit.Auto;
            units[StyleProperty.MinHeight] = (int)LengthUnit.Auto;
            units[StyleProperty.MaxWidth] = (int)LengthUnit.None;
            units[StyleProperty.MaxHeight] = (int)LengthUnit.None;
            units[StyleProperty.Top] = (int)LengthUnit.Auto;
            units[StyleProperty.Right] = (int)LengthUnit.Auto;
            units[StyleProperty.Bottom] = (int)LengthUnit.Auto;
            units[StyleProperty.Left] = (int)LengthUnit.Auto;
            numbers[StyleProperty.FontSize] = 16f;
            numbers[StyleProperty.TextAlign] = (int)TextAlignMode.Start;
            units[StyleProperty.ZIndex] = (int)LengthUnit.Auto;
            numbers[StyleProperty.Opacity] = 1f;
            colors[StyleProperty.Background] = new Color(0f, 0f, 0f, 0f);
            colors[StyleProperty.BorderColor] = new Color(0f, 0f, 0f, 1f);
            colors[StyleProperty.TextColor] = new Color(0f, 0f, 0f, 1f);
            numbers[StyleProperty.Visibility] = (int)VisibilityMode.Visible;
            numbers[StyleProperty.PointerEvents] = (int)PointerEventsMode.Auto;
        }

        internal void InheritFrom(ComputedStyle parent)
        {
            CopyProperty(parent, StyleProperty.FontSize);
            CopyProperty(parent, StyleProperty.TextAlign);
            CopyProperty(parent, StyleProperty.TextColor);
            CopyProperty(parent, StyleProperty.Visibility);
            CopyProperty(parent, StyleProperty.PointerEvents);
            CopyProperty(parent, StyleProperty.FontFamily);
        }

        private void CopyProperty(ComputedStyle other, int property)
        {
            numbers[property] = other.numbers[property];
            units[property] = other.units[property];
            colors[property] = other.colors[property];
        }

        /// Applies a specified style; pixel lengths are converted to physical
        /// pixels with `scale` and snapped.
        internal void Apply(Style style, float scale, MenUIContext context)
        {
            for (int i = 0; i < style.setCount; i++)
            {
                int property = style.setOrder[i];
                isSet[property] = true;
                colors[property] = style.colors[property];
                int unit = style.units[property];
                units[property] = unit;
                float value = style.numbers[property];
                if (unit == (int)LengthUnit.Pixels && IsPixelLength(property))
                {
                    value = SnapLength(property, value, scale);
                }
                numbers[property] = value;
            }
            if (style.isSet[StyleProperty.Shadows])
            {
                shadows = SnapShadows(style.shadows, scale);
            }
            if (style.isSet[StyleProperty.FontFamily])
            {
                numbers[StyleProperty.FontFamily] = context != null ? context.ResolveFontFamily(style.fontFamily) : 0;
                units[StyleProperty.FontFamily] = (int)LengthUnit.Pixels;
            }
        }

        /// Properties whose number is a pixel length (the rest are enums or
        /// plain factors). Font sizes are scaled but not rounded.
        private static bool IsPixelLength(int property)
        {
            return (property >= StyleProperty.FlexBasis && property <= StyleProperty.FontSize)
                || (property >= StyleProperty.CornerTopLeft && property <= StyleProperty.CornerBottomLeft);
        }

        private static float SnapLength(int property, float logical, float scale)
        {
            float physical = logical * scale;
            if (property == StyleProperty.FontSize)
            {
                return physical;
            }
            float snapped = Mathf.Round(physical);
            // A hairline never disappears: a non-zero border is at least 1px.
            if (property >= StyleProperty.BorderTopWidth && property <= StyleProperty.BorderLeftWidth
                && logical > 0f && snapped < 1f)
            {
                snapped = 1f;
            }
            return snapped;
        }

        private static Shadow[] SnapShadows(Shadow[] logical, float scale)
        {
            if (logical == null)
            {
                return null;
            }
            Shadow[] physical = new Shadow[logical.Length];
            for (int i = 0; i < logical.Length; i++)
            {
                Shadow s = logical[i];
                physical[i] = new Shadow(
                    Mathf.Round(s.OffsetX * scale), Mathf.Round(s.OffsetY * scale),
                    Mathf.Round(s.Blur * scale), Mathf.Round(s.Spread * scale), s.Color, s.Inset);
            }
            return physical;
        }

        // ---- typed reads ---------------------------------------------------

        internal int Enum(int property) { return (int)numbers[property]; }
        internal float Number(int property) { return numbers[property]; }
        internal int Unit(int property) { return units[property]; }
        internal Color ColorOf(int property) { return colors[property]; }

        internal DisplayMode Display { get { return (DisplayMode)(int)numbers[StyleProperty.Display]; } }
        internal PositionMode Position { get { return (PositionMode)(int)numbers[StyleProperty.Position]; } }
        internal bool HasZIndex { get { return units[StyleProperty.ZIndex] != (int)LengthUnit.Auto; } }
        internal int ZIndex { get { return HasZIndex ? (int)numbers[StyleProperty.ZIndex] : 0; } }
        internal float Opacity { get { return numbers[StyleProperty.Opacity]; } }
        internal bool IsVisible { get { return (int)numbers[StyleProperty.Visibility] == (int)VisibilityMode.Visible; } }
        internal float FontSize { get { return numbers[StyleProperty.FontSize]; } }
        /// Index into the panel's fonts (0: the default, Noto Sans JP).
        internal int FontIndex { get { return (int)numbers[StyleProperty.FontFamily]; } }
        internal TextAlignMode TextAlign { get { return (TextAlignMode)(int)numbers[StyleProperty.TextAlign]; } }
        internal bool ClipsContent { get { return (int)numbers[StyleProperty.Overflow] == (int)OverflowMode.Hidden; } }

        /// A length against a reference size; `auto`/`none` (or a percentage
        /// of an unknown size) give `fallback`.
        internal int Resolve(int property, int reference, int fallback)
        {
            int unit = units[property];
            if (unit == (int)LengthUnit.Pixels)
            {
                return (int)numbers[property];
            }
            if (unit == (int)LengthUnit.Percent && reference >= 0)
            {
                return Mathf.RoundToInt(numbers[property] * 0.01f * reference);
            }
            return fallback;
        }
    }
}
