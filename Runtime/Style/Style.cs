using UnityEngine;

namespace MenUI
{
    /// Property ids. Everything before `FirstPaintOnly` can change the layout;
    /// the rest only changes what is drawn.
    public static class StyleProperty
    {
        public const int Display = 0;
        public const int Direction = 1;
        public const int Wrap = 2;
        public const int JustifyContent = 3;
        public const int AlignItems = 4;
        public const int AlignSelf = 5;
        public const int AlignContent = 6;
        public const int Position = 7;
        public const int Overflow = 8;
        public const int FlexGrow = 9;
        public const int FlexShrink = 10;
        public const int FlexBasis = 11;
        public const int Width = 12;
        public const int Height = 13;
        public const int MinWidth = 14;
        public const int MinHeight = 15;
        public const int MaxWidth = 16;
        public const int MaxHeight = 17;
        public const int PaddingTop = 18;
        public const int PaddingRight = 19;
        public const int PaddingBottom = 20;
        public const int PaddingLeft = 21;
        public const int MarginTop = 22;
        public const int MarginRight = 23;
        public const int MarginBottom = 24;
        public const int MarginLeft = 25;
        public const int BorderTopWidth = 26;
        public const int BorderRightWidth = 27;
        public const int BorderBottomWidth = 28;
        public const int BorderLeftWidth = 29;
        public const int Top = 30;
        public const int Right = 31;
        public const int Bottom = 32;
        public const int Left = 33;
        public const int RowGap = 34;
        public const int ColumnGap = 35;
        public const int FontSize = 36;
        public const int TextAlign = 37;

        public const int FirstPaintOnly = 38;
        public const int ZIndex = 38;
        public const int CornerTopLeft = 39;
        public const int CornerTopRight = 40;
        public const int CornerBottomRight = 41;
        public const int CornerBottomLeft = 42;
        public const int Opacity = 43;
        public const int Background = 44;
        public const int BorderColor = 45;
        public const int TextColor = 46;
        public const int Shadows = 47;
        public const int Visibility = 48;
        public const int PointerEvents = 49;
        /// CSS `font-family`: resolved to an index into the panel's fonts.
        /// Numbered after the paint-only block but it does change layout.
        public const int FontFamily = 50;

        public const int Count = 51;

        public static bool AffectsLayout(int property)
        {
            return property < FirstPaintOnly || property == FontFamily;
        }

        /// Inherited from the parent unless the element sets it (as in CSS).
        public static bool IsInherited(int property)
        {
            return property == FontSize || property == TextAlign || property == TextColor
                || property == Visibility || property == PointerEvents || property == FontFamily;
        }
    }

    /// A set of specified style properties: an element's inline style, a
    /// state variant (`:hover`, ...) or a reusable style you build once and
    /// apply with `.Style(...)`. Values are logical pixels here; they become
    /// physical pixels when resolved.
    public sealed class Style : IStyleTarget
    {
        // Per property: a number (length value, enum value, factor) and, for
        // lengths, the unit. Colors and shadows have slots of their own.
        internal readonly float[] numbers = new float[StyleProperty.Count];
        internal readonly int[] units = new int[StyleProperty.Count];
        internal readonly Color[] colors = new Color[StyleProperty.Count];
        internal Shadow[] shadows;
        internal string fontFamily;
        /// Per property, how changes to it animate (null: they jump).
        internal AnimationSpec[] transitions;

        // The properties that were set, in the order they were first set, so
        // merging touches only those.
        internal readonly bool[] isSet = new bool[StyleProperty.Count];
        internal readonly int[] setOrder = new int[StyleProperty.Count];
        internal int setCount;
        internal bool affectsLayout;

        Style IStyleTarget.StyleForWriting { get { return this; } }

        void IStyleTarget.StyleWritten(int property)
        {
        }

        internal void MarkSet(int property)
        {
            if (!isSet[property])
            {
                isSet[property] = true;
                setOrder[setCount++] = property;
                if (StyleProperty.AffectsLayout(property))
                {
                    affectsLayout = true;
                }
            }
        }

        // The setters return whether anything changed: a binding that writes
        // the value a property already has must not restyle the element.

        internal bool SetNumber(int property, float value)
        {
            if (isSet[property] && numbers[property] == value && units[property] == (int)LengthUnit.Pixels)
            {
                return false;
            }
            numbers[property] = value;
            units[property] = (int)LengthUnit.Pixels;
            MarkSet(property);
            return true;
        }

        internal bool SetEnum(int property, int value)
        {
            if (isSet[property] && numbers[property] == value)
            {
                return false;
            }
            numbers[property] = value;
            MarkSet(property);
            return true;
        }

        internal bool SetLength(int property, Length length)
        {
            if (isSet[property] && numbers[property] == length.Value && units[property] == (int)length.Unit)
            {
                return false;
            }
            numbers[property] = length.Value;
            units[property] = (int)length.Unit;
            MarkSet(property);
            return true;
        }

        internal bool SetColor(int property, Color color)
        {
            if (isSet[property] && colors[property] == color)
            {
                return false;
            }
            colors[property] = color;
            MarkSet(property);
            return true;
        }

        internal bool SetFontFamily(string value)
        {
            if (isSet[StyleProperty.FontFamily] && fontFamily == value)
            {
                return false;
            }
            fontFamily = value;
            MarkSet(StyleProperty.FontFamily);
            return true;
        }

        /// Returns whether anything changed.
        internal bool SetTransition(int property, AnimationSpec spec)
        {
            if (transitions == null)
            {
                if (spec == null)
                {
                    return false;
                }
                transitions = new AnimationSpec[StyleProperty.Count];
            }
            if (transitions[property] == spec)
            {
                return false;
            }
            transitions[property] = spec;
            return true;
        }

        internal void SetShadows(Shadow[] value)
        {
            shadows = value;
            MarkSet(StyleProperty.Shadows);
        }

        /// Copies every property `other` sets into this style.
        internal void CopyFrom(Style other)
        {
            for (int i = 0; i < other.setCount; i++)
            {
                int property = other.setOrder[i];
                numbers[property] = other.numbers[property];
                units[property] = other.units[property];
                colors[property] = other.colors[property];
                MarkSet(property);
            }
            if (other.isSet[StyleProperty.Shadows])
            {
                shadows = other.shadows;
            }
            if (other.isSet[StyleProperty.FontFamily])
            {
                fontFamily = other.fontFamily;
            }
            if (other.transitions != null)
            {
                for (int i = 0; i < StyleProperty.Count; i++)
                {
                    if (other.transitions[i] != null)
                    {
                        SetTransition(i, other.transitions[i]);
                    }
                }
            }
        }

        public static Style New()
        {
            return new Style();
        }
    }

    /// What style chain methods write to: an element (its inline style) or a
    /// `Style` itself.
    public interface IStyleTarget
    {
        Style StyleForWriting { get; }
        void StyleWritten(int property);
    }
}
