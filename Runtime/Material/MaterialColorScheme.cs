// Material 3 color roles: what every component is colored with. Use the
// baseline (the purple of the Material 3 spec), a scheme generated from one
// color, or fill in your own:
//
//   M3.SetColorScheme(MaterialColorScheme.BaselineDark);
//   M3.SetColorScheme(MaterialColorScheme.FromSeed(Colors.Hex(0x3B82F6), dark: false));
//   var scheme = MaterialColorScheme.BaselineLight;  scheme.Primary = ...;  M3.SetColorScheme(scheme);
//
// FromSeed builds tonal palettes the way Material's "tonal spot" scheme does
// (the same tones for the same roles), but in CIELAB rather than Google's
// HCT color space: close, not identical, to what Material's own tools give.

using UnityEngine;

namespace MenUI.Material3
{
    public sealed class MaterialColorScheme
    {
        public Color Primary, OnPrimary, PrimaryContainer, OnPrimaryContainer;
        public Color Secondary, OnSecondary, SecondaryContainer, OnSecondaryContainer;
        public Color Tertiary, OnTertiary, TertiaryContainer, OnTertiaryContainer;
        public Color Error, OnError, ErrorContainer, OnErrorContainer;
        public Color PrimaryFixed, PrimaryFixedDim, OnPrimaryFixed, OnPrimaryFixedVariant;
        public Color SecondaryFixed, SecondaryFixedDim, OnSecondaryFixed, OnSecondaryFixedVariant;
        public Color TertiaryFixed, TertiaryFixedDim, OnTertiaryFixed, OnTertiaryFixedVariant;
        public Color Surface, OnSurface, SurfaceVariant, OnSurfaceVariant;
        public Color SurfaceDim, SurfaceBright;
        public Color SurfaceContainerLowest, SurfaceContainerLow, SurfaceContainer, SurfaceContainerHigh, SurfaceContainerHighest;
        public Color InverseSurface, InverseOnSurface, InversePrimary;
        public Color Outline, OutlineVariant, Shadow, Scrim;
        /// Whether this is a dark scheme.
        public bool IsDark;

        /// Material 3's baseline scheme (light).
        public static MaterialColorScheme BaselineLight
        {
            get
            {
                MaterialColorScheme s = new MaterialColorScheme();
                s.Primary = Hex(0x6750A4); s.OnPrimary = Hex(0xFFFFFF); s.PrimaryContainer = Hex(0xEADDFF); s.OnPrimaryContainer = Hex(0x4F378B);
                s.Secondary = Hex(0x625B71); s.OnSecondary = Hex(0xFFFFFF); s.SecondaryContainer = Hex(0xE8DEF8); s.OnSecondaryContainer = Hex(0x4A4458);
                s.Tertiary = Hex(0x7D5260); s.OnTertiary = Hex(0xFFFFFF); s.TertiaryContainer = Hex(0xFFD8E4); s.OnTertiaryContainer = Hex(0x633B48);
                s.Error = Hex(0xB3261E); s.OnError = Hex(0xFFFFFF); s.ErrorContainer = Hex(0xF9DEDC); s.OnErrorContainer = Hex(0x8C1D18);
                SetFixed(s);
                s.Surface = Hex(0xFEF7FF); s.OnSurface = Hex(0x1D1B20); s.SurfaceVariant = Hex(0xE7E0EC); s.OnSurfaceVariant = Hex(0x49454F);
                s.SurfaceDim = Hex(0xDED8E1); s.SurfaceBright = Hex(0xFEF7FF);
                s.SurfaceContainerLowest = Hex(0xFFFFFF); s.SurfaceContainerLow = Hex(0xF7F2FA); s.SurfaceContainer = Hex(0xF3EDF7);
                s.SurfaceContainerHigh = Hex(0xECE6F0); s.SurfaceContainerHighest = Hex(0xE6E0E9);
                s.InverseSurface = Hex(0x322F35); s.InverseOnSurface = Hex(0xF5EFF7); s.InversePrimary = Hex(0xD0BCFF);
                s.Outline = Hex(0x79747E); s.OutlineVariant = Hex(0xCAC4D0); s.Shadow = Hex(0x000000); s.Scrim = Hex(0x000000);
                return s;
            }
        }

        /// Material 3's baseline scheme (dark).
        public static MaterialColorScheme BaselineDark
        {
            get
            {
                MaterialColorScheme s = new MaterialColorScheme();
                s.IsDark = true;
                s.Primary = Hex(0xD0BCFF); s.OnPrimary = Hex(0x381E72); s.PrimaryContainer = Hex(0x4F378B); s.OnPrimaryContainer = Hex(0xEADDFF);
                s.Secondary = Hex(0xCCC2DC); s.OnSecondary = Hex(0x332D41); s.SecondaryContainer = Hex(0x4A4458); s.OnSecondaryContainer = Hex(0xE8DEF8);
                s.Tertiary = Hex(0xEFB8C8); s.OnTertiary = Hex(0x492532); s.TertiaryContainer = Hex(0x633B48); s.OnTertiaryContainer = Hex(0xFFD8E4);
                s.Error = Hex(0xF2B8B5); s.OnError = Hex(0x601410); s.ErrorContainer = Hex(0x8C1D18); s.OnErrorContainer = Hex(0xF9DEDC);
                SetFixed(s);
                s.Surface = Hex(0x141218); s.OnSurface = Hex(0xE6E0E9); s.SurfaceVariant = Hex(0x49454F); s.OnSurfaceVariant = Hex(0xCAC4D0);
                s.SurfaceDim = Hex(0x141218); s.SurfaceBright = Hex(0x3B383E);
                s.SurfaceContainerLowest = Hex(0x0F0D13); s.SurfaceContainerLow = Hex(0x1D1B20); s.SurfaceContainer = Hex(0x211F26);
                s.SurfaceContainerHigh = Hex(0x2B2930); s.SurfaceContainerHighest = Hex(0x36343B);
                s.InverseSurface = Hex(0xE6E0E9); s.InverseOnSurface = Hex(0x322F35); s.InversePrimary = Hex(0x6750A4);
                s.Outline = Hex(0x938F99); s.OutlineVariant = Hex(0x49454F); s.Shadow = Hex(0x000000); s.Scrim = Hex(0x000000);
                return s;
            }
        }

        private static void SetFixed(MaterialColorScheme s)
        {
            s.PrimaryFixed = Hex(0xEADDFF); s.PrimaryFixedDim = Hex(0xD0BCFF); s.OnPrimaryFixed = Hex(0x21005D); s.OnPrimaryFixedVariant = Hex(0x4F378B);
            s.SecondaryFixed = Hex(0xE8DEF8); s.SecondaryFixedDim = Hex(0xCCC2DC); s.OnSecondaryFixed = Hex(0x1D192B); s.OnSecondaryFixedVariant = Hex(0x4A4458);
            s.TertiaryFixed = Hex(0xFFD8E4); s.TertiaryFixedDim = Hex(0xEFB8C8); s.OnTertiaryFixed = Hex(0x31111D); s.OnTertiaryFixedVariant = Hex(0x633B48);
        }

        /// A scheme built from one color (its hue; tonal spot palettes).
        public static MaterialColorScheme FromSeed(Color seed, bool dark = false)
        {
            float l, a, b;
            Lab(seed, out l, out a, out b);
            float hue = Mathf.Atan2(b, a) * Mathf.Rad2Deg;
            TonalPalette primary = new TonalPalette(hue, 44f);
            TonalPalette secondary = new TonalPalette(hue, 16f);
            TonalPalette tertiary = new TonalPalette(hue + 60f, 26f);
            TonalPalette neutral = new TonalPalette(hue, 5f);
            TonalPalette neutralVariant = new TonalPalette(hue, 9f);
            TonalPalette error = new TonalPalette(30f, 68f);

            MaterialColorScheme s = new MaterialColorScheme();
            s.IsDark = dark;
            // Accent roles: the color, on it, its container, on the container.
            float role = dark ? 80f : 40f, on = dark ? 20f : 100f, container = dark ? 30f : 90f, onContainer = dark ? 90f : 30f;
            s.Primary = primary.Tone(role); s.OnPrimary = primary.Tone(on);
            s.PrimaryContainer = primary.Tone(container); s.OnPrimaryContainer = primary.Tone(onContainer);
            s.Secondary = secondary.Tone(role); s.OnSecondary = secondary.Tone(on);
            s.SecondaryContainer = secondary.Tone(container); s.OnSecondaryContainer = secondary.Tone(onContainer);
            s.Tertiary = tertiary.Tone(role); s.OnTertiary = tertiary.Tone(on);
            s.TertiaryContainer = tertiary.Tone(container); s.OnTertiaryContainer = tertiary.Tone(onContainer);
            s.Error = error.Tone(role); s.OnError = error.Tone(on);
            s.ErrorContainer = error.Tone(container); s.OnErrorContainer = error.Tone(onContainer);
            // Fixed roles: the same in light and dark.
            s.PrimaryFixed = primary.Tone(90f); s.PrimaryFixedDim = primary.Tone(80f);
            s.OnPrimaryFixed = primary.Tone(10f); s.OnPrimaryFixedVariant = primary.Tone(30f);
            s.SecondaryFixed = secondary.Tone(90f); s.SecondaryFixedDim = secondary.Tone(80f);
            s.OnSecondaryFixed = secondary.Tone(10f); s.OnSecondaryFixedVariant = secondary.Tone(30f);
            s.TertiaryFixed = tertiary.Tone(90f); s.TertiaryFixedDim = tertiary.Tone(80f);
            s.OnTertiaryFixed = tertiary.Tone(10f); s.OnTertiaryFixedVariant = tertiary.Tone(30f);
            if (!dark)
            {
                s.Surface = neutral.Tone(98f); s.OnSurface = neutral.Tone(10f);
                s.SurfaceVariant = neutralVariant.Tone(90f); s.OnSurfaceVariant = neutralVariant.Tone(30f);
                s.SurfaceDim = neutral.Tone(87f); s.SurfaceBright = neutral.Tone(98f);
                s.SurfaceContainerLowest = neutral.Tone(100f); s.SurfaceContainerLow = neutral.Tone(96f);
                s.SurfaceContainer = neutral.Tone(94f); s.SurfaceContainerHigh = neutral.Tone(92f);
                s.SurfaceContainerHighest = neutral.Tone(90f);
                s.InverseSurface = neutral.Tone(20f); s.InverseOnSurface = neutral.Tone(95f); s.InversePrimary = primary.Tone(80f);
                s.Outline = neutralVariant.Tone(50f); s.OutlineVariant = neutralVariant.Tone(80f);
            }
            else
            {
                s.Surface = neutral.Tone(6f); s.OnSurface = neutral.Tone(90f);
                s.SurfaceVariant = neutralVariant.Tone(30f); s.OnSurfaceVariant = neutralVariant.Tone(80f);
                s.SurfaceDim = neutral.Tone(6f); s.SurfaceBright = neutral.Tone(24f);
                s.SurfaceContainerLowest = neutral.Tone(4f); s.SurfaceContainerLow = neutral.Tone(10f);
                s.SurfaceContainer = neutral.Tone(12f); s.SurfaceContainerHigh = neutral.Tone(17f);
                s.SurfaceContainerHighest = neutral.Tone(22f);
                s.InverseSurface = neutral.Tone(90f); s.InverseOnSurface = neutral.Tone(20f); s.InversePrimary = primary.Tone(40f);
                s.Outline = neutralVariant.Tone(60f); s.OutlineVariant = neutralVariant.Tone(30f);
            }
            s.Shadow = Colors.Black;
            s.Scrim = Colors.Black;
            return s;
        }

        private static Color Hex(int rgb)
        {
            return Colors.Hex(rgb);
        }

        // ---- CIELAB (D65) -------------------------------------------------------------

        internal static void Lab(Color c, out float l, out float a, out float b)
        {
            float r = Linear(c.r), g = Linear(c.g), bl = Linear(c.b);
            float x = (0.4124564f * r + 0.3575761f * g + 0.1804375f * bl) / 0.95047f;
            float y = 0.2126729f * r + 0.7151522f * g + 0.0721750f * bl;
            float z = (0.0193339f * r + 0.1191920f * g + 0.9503041f * bl) / 1.08883f;
            float fx = LabF(x), fy = LabF(y), fz = LabF(z);
            l = 116f * fy - 16f;
            a = 500f * (fx - fy);
            b = 200f * (fy - fz);
        }

        /// The sRGB color at CIELAB (l, a, b); `inGamut` says whether it fits.
        internal static Color FromLab(float l, float a, float b, out bool inGamut)
        {
            float fy = (l + 16f) / 116f;
            float fx = fy + a / 500f;
            float fz = fy - b / 200f;
            float x = LabInverse(fx) * 0.95047f;
            float y = LabInverse(fy);
            float z = LabInverse(fz) * 1.08883f;
            float r = 3.2404542f * x - 1.5371385f * y - 0.4985314f * z;
            float g = -0.9692660f * x + 1.8760108f * y + 0.0415560f * z;
            float bl = 0.0556434f * x - 0.2040259f * y + 1.0572252f * z;
            const float tolerance = 0.0001f;
            inGamut = r >= -tolerance && r <= 1f + tolerance && g >= -tolerance && g <= 1f + tolerance
                && bl >= -tolerance && bl <= 1f + tolerance;
            return new Color(Gamma(r), Gamma(g), Gamma(bl), 1f);
        }

        private static float LabF(float t)
        {
            return t > 0.008856452f ? Mathf.Pow(t, 1f / 3f) : (903.2963f * t + 16f) / 116f;
        }

        private static float LabInverse(float f)
        {
            float cube = f * f * f;
            return cube > 0.008856452f ? cube : (116f * f - 16f) / 903.2963f;
        }

        private static float Linear(float channel)
        {
            return channel <= 0.04045f ? channel / 12.92f : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }

        private static float Gamma(float channel)
        {
            channel = Mathf.Clamp01(channel);
            return channel <= 0.0031308f ? channel * 12.92f : 1.055f * Mathf.Pow(channel, 1f / 2.4f) - 0.055f;
        }
    }

    /// Tones (CIELAB lightness 0..100) of one hue at up to one chroma.
    internal sealed class TonalPalette
    {
        private readonly float hue;     // degrees
        private readonly float chroma;

        internal TonalPalette(float hue, float chroma)
        {
            this.hue = hue;
            this.chroma = chroma;
        }

        /// The color of `tone` with the most chroma up to the palette's that
        /// sRGB can show.
        internal Color Tone(float tone)
        {
            if (tone <= 0f) return Colors.Black;
            if (tone >= 100f) return Colors.White;
            float radians = hue * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            bool inGamut;
            Color best = MaterialColorScheme.FromLab(tone, chroma * cos, chroma * sin, out inGamut);
            if (inGamut)
            {
                return best;
            }
            float low = 0f, high = chroma;
            best = MaterialColorScheme.FromLab(tone, 0f, 0f, out inGamut);
            for (int i = 0; i < 12; i++)
            {
                float middle = (low + high) * 0.5f;
                Color candidate = MaterialColorScheme.FromLab(tone, middle * cos, middle * sin, out inGamut);
                if (inGamut)
                {
                    best = candidate;
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            return best;
        }
    }
}
