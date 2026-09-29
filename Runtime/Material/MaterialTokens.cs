// Material 3 design tokens: type scale, corner shapes, elevation, motion
// (Standard and Expressive), and the state layer colors.

using UnityEngine;

namespace MenUI.Material3
{
    /// The type scale's roles (font sizes in logical px; MenUI has one weight).
    public enum TypeRole
    {
        DisplayLarge, DisplayMedium, DisplaySmall,
        HeadlineLarge, HeadlineMedium, HeadlineSmall,
        TitleLarge, TitleMedium, TitleSmall,
        BodyLarge, BodyMedium, BodySmall,
        LabelLarge, LabelMedium, LabelSmall,
    }

    public static class TypeScale
    {
        public static float Size(TypeRole role)
        {
            switch (role)
            {
                case TypeRole.DisplayLarge: return 57f;
                case TypeRole.DisplayMedium: return 45f;
                case TypeRole.DisplaySmall: return 36f;
                case TypeRole.HeadlineLarge: return 32f;
                case TypeRole.HeadlineMedium: return 28f;
                case TypeRole.HeadlineSmall: return 24f;
                case TypeRole.TitleLarge: return 22f;
                case TypeRole.TitleMedium: return 16f;
                case TypeRole.TitleSmall: return 14f;
                case TypeRole.BodyLarge: return 16f;
                case TypeRole.BodyMedium: return 14f;
                case TypeRole.BodySmall: return 12f;
                case TypeRole.LabelLarge: return 14f;
                case TypeRole.LabelMedium: return 12f;
                default: return 11f;
            }
        }
    }

    /// Corner radii of the shape scale (logical px).
    public static class ShapeScale
    {
        public const float None = 0f;
        public const float ExtraSmall = 4f;
        public const float Small = 8f;
        public const float Medium = 12f;
        public const float Large = 16f;
        public const float LargeIncreased = 20f;
        public const float ExtraLarge = 28f;
        public const float ExtraLargeIncreased = 32f;
        public const float ExtraExtraLarge = 48f;
        /// Fully rounded (a pill or a circle).
        public const float Full = 9999f;
    }

    /// Elevation levels 0..5 as shadows (a key and an ambient shadow).
    public static class Elevation
    {
        public static Shadow[] Level(int level)
        {
            switch (level)
            {
                case 1: return Pair(1f, 2f, 0f, 1f, 3f, 1f);
                case 2: return Pair(1f, 2f, 0f, 2f, 6f, 2f);
                case 3: return Pair(1f, 3f, 0f, 4f, 8f, 3f);
                case 4: return Pair(2f, 3f, 0f, 6f, 10f, 4f);
                case 5: return Pair(4f, 4f, 0f, 8f, 12f, 6f);
                default: return new Shadow[0];
            }
        }

        private static Shadow[] Pair(float keyY, float keyBlur, float keySpread, float ambientY, float ambientBlur, float ambientSpread)
        {
            return new Shadow[]
            {
                new Shadow(0f, keyY, keyBlur, keySpread, new Color(0f, 0f, 0f, 0.3f)),
                new Shadow(0f, ambientY, ambientBlur, ambientSpread, new Color(0f, 0f, 0f, 0.15f)),
            };
        }
    }

    /// Material 3 motion: springs for movement ("spatial": position, size,
    /// shape) and for effects (color, opacity), each fast, default and slow.
    /// Expressive bounces a little; Standard does not.
    public sealed class MotionScheme
    {
        public AnimationSpec FastSpatial, DefaultSpatial, SlowSpatial;
        public AnimationSpec FastEffects, DefaultEffects, SlowEffects;

        public static MotionScheme Expressive
        {
            get
            {
                MotionScheme m = new MotionScheme();
                m.FastSpatial = AnimationSpec.Spring(800f, 0.6f);
                m.DefaultSpatial = AnimationSpec.Spring(380f, 0.8f);
                m.SlowSpatial = AnimationSpec.Spring(200f, 0.8f);
                SetEffects(m);
                return m;
            }
        }

        public static MotionScheme Standard
        {
            get
            {
                MotionScheme m = new MotionScheme();
                m.FastSpatial = AnimationSpec.Spring(1400f, 0.9f);
                m.DefaultSpatial = AnimationSpec.Spring(700f, 0.9f);
                m.SlowSpatial = AnimationSpec.Spring(300f, 0.9f);
                SetEffects(m);
                return m;
            }
        }

        private static void SetEffects(MotionScheme m)
        {
            m.FastEffects = AnimationSpec.Spring(3800f, 1f);
            m.DefaultEffects = AnimationSpec.Spring(1600f, 1f);
            m.SlowEffects = AnimationSpec.Spring(800f, 1f);
        }
    }

    /// State layers: a translucent layer of the content color over the
    /// container while hovered, focused, pressed or dragged.
    public static class StateLayer
    {
        public const float Hover = 0.08f;
        public const float Focus = 0.10f;
        public const float Pressed = 0.10f;
        public const float Dragged = 0.16f;
        /// Disabled containers and contents (on the surface color).
        public const float DisabledContainer = 0.10f;
        public const float DisabledContent = 0.38f;

        /// `layer` at `opacity` over `container` (which may be transparent).
        public static Color Over(Color container, Color layer, float opacity)
        {
            float outAlpha = opacity + container.a * (1f - opacity);
            if (outAlpha <= 0f)
            {
                return new Color(layer.r, layer.g, layer.b, 0f);
            }
            float k = container.a * (1f - opacity);
            return new Color((layer.r * opacity + container.r * k) / outAlpha, (layer.g * opacity + container.g * k) / outAlpha,
                (layer.b * opacity + container.b * k) / outAlpha, outAlpha);
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }
    }
}
