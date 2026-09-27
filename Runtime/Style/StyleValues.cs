using UnityEngine;

namespace MenUI
{
    /// CSS `display`. `Contents` makes the element's children take its place
    /// in the parent's layout (the element itself draws nothing).
    public enum DisplayMode { Block, Flex, None, Contents }

    /// CSS `flex-direction`.
    public enum Direction { Row, Column, RowReverse, ColumnReverse }

    /// CSS `flex-wrap`.
    public enum WrapMode { NoWrap, Wrap }

    /// CSS `justify-content`.
    public enum Justify { Start, End, Center, SpaceBetween, SpaceAround, SpaceEvenly }

    /// CSS `align-items`, `align-self` and `align-content`. `Auto` is only
    /// meaningful for `align-self` (use the parent's `align-items`).
    public enum Align { Auto, Start, End, Center, Stretch, SpaceBetween, SpaceAround }

    /// CSS `position`. An absolute element is placed against its parent's
    /// padding box.
    public enum PositionMode { Relative, Absolute }

    /// CSS `overflow`. `Hidden` clips descendants to the padding box.
    public enum OverflowMode { Visible, Hidden }

    /// CSS `text-align`.
    public enum TextAlignMode { Start, Center, End }

    /// CSS `visibility`.
    public enum VisibilityMode { Visible, Hidden }

    /// CSS `pointer-events`.
    public enum PointerEventsMode { Auto, None }

    /// How a length is expressed.
    public enum LengthUnit { Auto = 0, Pixels = 1, Percent = 2, None = 3 }

    /// A CSS length: logical pixels (fractions allowed), a percentage of the
    /// containing box, or `auto`. A plain number converts to pixels:
    /// `.Width(240)`, `.Width(Length.Percent(50))`, `.Width(Length.Auto)`.
    public sealed class Length
    {
        public readonly LengthUnit Unit;
        public readonly float Value;

        public Length(LengthUnit unit, float value)
        {
            Unit = unit;
            Value = value;
        }

        public static Length Pixels(float value) { return new Length(LengthUnit.Pixels, value); }
        public static Length Percent(float value) { return new Length(LengthUnit.Percent, value); }
        public static Length Auto { get { return new Length(LengthUnit.Auto, 0f); } }
        /// `none`, for `max-width` / `max-height`.
        public static Length None { get { return new Length(LengthUnit.None, 0f); } }

        public static implicit operator Length(float pixels) { return new Length(LengthUnit.Pixels, pixels); }
        public static implicit operator Length(int pixels) { return new Length(LengthUnit.Pixels, pixels); }
    }

    /// One CSS `box-shadow` layer, in logical pixels.
    public sealed class Shadow
    {
        public readonly float OffsetX;
        public readonly float OffsetY;
        public readonly float Blur;
        public readonly float Spread;
        public readonly Color Color;
        public readonly bool Inset;

        public Shadow(float offsetX, float offsetY, float blur, float spread, Color color, bool inset = false)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
            Blur = blur;
            Spread = spread;
            Color = color;
            Inset = inset;
        }

        // Presets in the spirit of common design systems.
        public static Shadow Small { get { return new Shadow(0f, 1f, 3f, 0f, new Color(0f, 0f, 0f, 0.25f)); } }
        public static Shadow Medium { get { return new Shadow(0f, 4f, 12f, -2f, new Color(0f, 0f, 0f, 0.3f)); } }
        public static Shadow Large { get { return new Shadow(0f, 12f, 32f, -6f, new Color(0f, 0f, 0f, 0.35f)); } }
    }

    /// Color helpers. `Colors.Hex(0x3B82F6)` reads like CSS `#3B82F6`.
    public static class Colors
    {
        public static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }

        public static Color Hex(int rgb, float alpha)
        {
            Color color = Hex(rgb);
            color.a = alpha;
            return color;
        }

        public static Color Rgb(int red, int green, int blue)
        {
            return new Color(red / 255f, green / 255f, blue / 255f, 1f);
        }

        public static Color Rgba(int red, int green, int blue, float alpha)
        {
            return new Color(red / 255f, green / 255f, blue / 255f, alpha);
        }

        public static Color Transparent { get { return new Color(0f, 0f, 0f, 0f); } }
        public static Color White { get { return new Color(1f, 1f, 1f, 1f); } }
        public static Color Black { get { return new Color(0f, 0f, 0f, 1f); } }
    }
}
