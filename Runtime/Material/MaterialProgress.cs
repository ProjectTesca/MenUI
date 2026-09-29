// Material 3 Expressive progress: linear and circular indicators (flat or
// wavy, determinate or indeterminate) and the loading indicator — a shape
// morphing through the shape library while it turns. All are Graphics: a
// frame of animation rewrites a few draw records, nothing else.

using System;
using UnityEngine;

namespace MenUI.Material3
{
    internal static class MaterialProgress
    {
        private const float Gap = 4f;           // between the indicator and the track
        private const float WaveAmplitude = 3f;
        private const float Wavelength = 40f;

        internal static Graphic Linear(MaterialDesign m3, Func<float> progress, bool wavy, float thickness)
        {
            MenUIContext ui = m3.ui;
            // The waves travel; an indeterminate indicator's segments sweep across.
            AnimatedFloat clock = ui.Animated(0f);
            if (wavy || progress == null)
            {
                clock.Play(new Keyframes(0f).To(1f, progress == null ? 1.8f : 1f), true);
            }
            float amplitude = wavy ? WaveAmplitude : 0f;
            Graphic graphic = ui.Graphic(g =>
            {
                MaterialColorScheme c = m3.Scheme;
                float width = g.Width;
                float y = g.Height * 0.5f;
                float half = thickness * 0.5f;
                float t = clock.Value;
                if (progress != null)
                {
                    float p = Mathf.Clamp01(progress());
                    float end = p * width;
                    // Flat near the ends (the waves fade in and out).
                    float wave = amplitude * Mathf.Clamp01(Mathf.Min(p, 1f - p) * 10f);
                    Segment(g, half, end - half, y, thickness, c.Primary, wave, t * 360f);
                    Segment(g, end + Gap + half, width - half, y, thickness, c.SecondaryContainer, 0f, 0f);
                    // The stop indicator at the end of the track.
                    Color stop = p < 1f ? c.Primary : Colors.Transparent;
                    g.Circle(width - half, y, half, stop);
                }
                else
                {
                    // Two segments chasing each other.
                    float headA = Ease(t * 1.4f) * width, tailA = Ease(t * 1.4f - 0.35f) * width;
                    float headB = Ease(t * 1.4f - 0.6f) * width, tailB = Ease(t * 1.4f - 0.9f) * width;
                    g.Line(half, width - half, y, thickness, c.SecondaryContainer);
                    Segment(g, tailA + half, headA - half, y, thickness, c.Primary, amplitude, t * 720f);
                    Segment(g, tailB + half, headB - half, y, thickness, c.Primary, amplitude, t * 720f);
                }
            }).Tag("m3-linear-progress");
            return graphic.Height(thickness + amplitude * 2f).MinWidth(40);
        }

        /// A line segment, or (too short to show) a transparent one: the
        /// number of draw commands stays the same.
        private static void Segment(GraphicCanvas g, float x0, float x1, float y, float thickness, Color color,
            float amplitude, float phase)
        {
            if (x1 < x0)
            {
                g.Line(x0, x0, y, thickness, Colors.Transparent);
                return;
            }
            g.Line(x0, x1, y, thickness, color, amplitude, Wavelength, phase);
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        internal static Graphic Circular(MaterialDesign m3, Func<float> progress, bool wavy, float size)
        {
            MenUIContext ui = m3.ui;
            AnimatedFloat clock = ui.Animated(0f);
            if (wavy || progress == null)
            {
                clock.Play(new Keyframes(0f).To(1f, progress == null ? 1.333f : 2f), true);
            }
            const float thickness = 4f;
            float amplitude = wavy ? 1.6f : 0f;
            Graphic graphic = ui.Graphic(g =>
            {
                MaterialColorScheme c = m3.Scheme;
                float cx = g.Width * 0.5f, cy = g.Height * 0.5f;
                float radius = Mathf.Min(cx, cy) - thickness * 0.5f - amplitude;
                float t = clock.Value;
                float waves = Mathf.Max(3f, Mathf.Round(radius * 2f * Mathf.PI / 18f));
                if (progress != null)
                {
                    float p = Mathf.Clamp01(progress());
                    float sweep = p * 360f;
                    // The gap, in degrees, between the indicator and the track (caps included).
                    float gap = p > 0f && p < 1f ? (Gap + thickness) / radius * Mathf.Rad2Deg : 0f;
                    float wave = amplitude * Mathf.Clamp01(Mathf.Min(p, 1f - p) * 10f);
                    g.Arc(cx, cy, radius, thickness, 0f, sweep, p > 0f ? c.Primary : Colors.Transparent,
                        wave, waves, t * 360f);
                    float trackStart = sweep + gap;
                    float trackSweep = 360f - sweep - gap * 2f;
                    g.Arc(cx, cy, radius, thickness, trackStart, Mathf.Max(0f, trackSweep),
                        trackSweep > 0f ? c.SecondaryContainer : Colors.Transparent);
                }
                else
                {
                    // Turning, the arc growing and shrinking.
                    float turn = ui.time * 300f;
                    float grow = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f);
                    float sweep = 20f + 250f * grow;
                    float start = turn + t * 360f - sweep * 0.5f;
                    g.Arc(cx, cy, radius, thickness, start, sweep, c.Primary, amplitude, waves, t * 720f);
                    g.Arc(cx, cy, radius, thickness, start + sweep, 0f, Colors.Transparent);
                }
            }).Tag("m3-circular-progress");
            graphic.Size(size, size);
            return graphic;
        }

        /// The shapes the loading indicator morphs through, in order.
        private static ShapeSpec LoadingShape(int index)
        {
            switch (index % 7)
            {
                case 0: return ShapeSpec.SoftBurst;
                case 1: return ShapeSpec.Cookie9;
                case 2: return ShapeSpec.Pentagon;
                case 3: return ShapeSpec.Pill45;
                case 4: return ShapeSpec.Sunny;
                case 5: return ShapeSpec.Cookie4;
                default: return ShapeSpec.Oval45;
            }
        }

        internal static Element Loading(MaterialDesign m3, bool contained, float size)
        {
            MenUIContext ui = m3.ui;
            // Every 650 ms: morph to the next shape and turn a quarter more
            // (springs), while turning slowly all along.
            Signal<int> index = ui.Signal(0);
            AnimatedFloat morph = ui.Animated(1f);
            AnimatedFloat turn = ui.Animated(0f);
            AnimationSpec morphSpring = AnimationSpec.Spring(200f, 0.6f);
            AnimationSpec turnSpring = AnimationSpec.Spring(200f, 0.8f);
            // A slow, steady turn (read in the drawing: it redraws every frame).
            AnimatedFloat spin = ui.Animated(0f);
            spin.Play(new Keyframes(0f).To(360f, 9f), true);
            float elapsed = 0f;
            ui.EveryFrame(dt =>
            {
                elapsed += dt;
                if (elapsed < 0.65f)
                {
                    return;
                }
                elapsed -= 0.65f;
                index.Value = index.Peek() + 1;
                morph.Set(0f);
                morph.AnimateTo(1f, morphSpring);
                turn.AnimateTo(turn.Target + 90f, turnSpring);
            });
            Graphic graphic = ui.Graphic(g =>
            {
                MaterialColorScheme c = m3.Scheme;
                float full = Mathf.Min(g.Width, g.Height);
                float shape = full * 0.79f;
                float x = (g.Width - shape) * 0.5f, y = (g.Height - shape) * 0.5f;
                int i = index.Value;
                float rotation = turn.Value + spin.Value;
                if (contained)
                {
                    g.Circle(g.Width * 0.5f, g.Height * 0.5f, full * 0.5f, c.PrimaryContainer);
                    shape = full * 0.62f;
                    x = (g.Width - shape) * 0.5f;
                    y = (g.Height - shape) * 0.5f;
                }
                g.Morph(x, y, shape, shape, LoadingShape(i + 6), LoadingShape(i), morph.Value,
                    contained ? c.OnPrimaryContainer : c.Primary, rotation);
            }).Tag("m3-loading-indicator");
            graphic.Size(size, size);
            return graphic;
        }
    }
}
