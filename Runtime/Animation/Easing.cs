// Easing curves: how a timed animation spreads its progress (0 → 1) over
// its duration. CSS's named curves, any cubic Bézier, or a function of your
// own:
//
//   Easing.EaseOut
//   Easing.CubicBezier(0.2f, 0f, 0f, 1f)      // CSS cubic-bezier(0.2, 0, 0, 1)
//   Easing.Custom(t => t * t * (3f - 2f * t))
//
// The Bézier's y may leave 0..1 (overshoot, anticipation); x is clamped to
// 0..1 as in CSS. Build curves once (in Build, or in a style rule) and reuse
// them: making one samples the curve.

using System;
using UnityEngine;

namespace MenUI
{
    public sealed class Easing
    {
        private const int KindLinear = 0;
        private const int KindBezier = 1;
        private const int KindCustom = 2;
        // x(t) sampled at t = i / (SampleCount - 1): the start for Newton's method.
        private const int SampleCount = 11;
        private const float SampleStep = 1f / (SampleCount - 1);

        private readonly int kind;
        // Polynomial coefficients of x(t) and y(t): ((a t + b) t + c) t.
        private readonly float ax, bx, cx, ay, by, cy;
        private readonly float[] samples;
        private readonly Func<float, float> curve;

        private Easing(int kind, float x1, float y1, float x2, float y2, Func<float, float> curve)
        {
            this.kind = kind;
            this.curve = curve;
            if (kind != KindBezier)
            {
                return;
            }
            cx = 3f * x1;
            bx = 3f * (x2 - x1) - cx;
            ax = 1f - cx - bx;
            cy = 3f * y1;
            by = 3f * (y2 - y1) - cy;
            ay = 1f - cy - by;
            samples = new float[SampleCount];
            for (int i = 0; i < SampleCount; i++)
            {
                samples[i] = CurveX(i * SampleStep);
            }
        }

        /// No easing: constant speed.
        public static Easing Linear { get { return new Easing(KindLinear, 0f, 0f, 1f, 1f, null); } }
        /// CSS `ease`: a quick start, a long gentle end.
        public static Easing Ease { get { return CubicBezier(0.25f, 0.1f, 0.25f, 1f); } }
        /// CSS `ease-in`: starts slowly (things leaving).
        public static Easing EaseIn { get { return CubicBezier(0.42f, 0f, 1f, 1f); } }
        /// CSS `ease-out`: ends slowly (things arriving).
        public static Easing EaseOut { get { return CubicBezier(0f, 0f, 0.58f, 1f); } }
        /// CSS `ease-in-out`: slow at both ends.
        public static Easing EaseInOut { get { return CubicBezier(0.42f, 0f, 0.58f, 1f); } }

        /// CSS `cubic-bezier(x1, y1, x2, y2)`: the curve from (0, 0) to (1, 1)
        /// with those two control points.
        public static Easing CubicBezier(float x1, float y1, float x2, float y2)
        {
            x1 = Mathf.Clamp01(x1);
            x2 = Mathf.Clamp01(x2);
            if (x1 == y1 && x2 == y2)
            {
                return new Easing(KindLinear, 0f, 0f, 1f, 1f, null);
            }
            return new Easing(KindBezier, x1, y1, x2, y2, null);
        }

        /// Any curve: `progress` 0..1 in, eased progress out (0 at 0 and 1 at
        /// 1 for a smooth start and end; values past them overshoot).
        public static Easing Custom(Func<float, float> curve)
        {
            return new Easing(KindCustom, 0f, 0f, 1f, 1f, curve);
        }

        /// The eased progress at `progress` (clamped to 0..1).
        public float Evaluate(float progress)
        {
            if (progress <= 0f)
            {
                return kind == KindCustom ? curve(0f) : 0f;
            }
            if (progress >= 1f)
            {
                return kind == KindCustom ? curve(1f) : 1f;
            }
            if (kind == KindLinear)
            {
                return progress;
            }
            if (kind == KindCustom)
            {
                return curve(progress);
            }
            return CurveY(ParameterForX(progress));
        }

        private float CurveX(float t)
        {
            return ((ax * t + bx) * t + cx) * t;
        }

        private float CurveY(float t)
        {
            return ((ay * t + by) * t + cy) * t;
        }

        private float SlopeX(float t)
        {
            return (3f * ax * t + 2f * bx) * t + cx;
        }

        /// The curve parameter t where x(t) = x: start from the samples, then
        /// Newton's method, or bisection where the curve is too flat for it.
        private float ParameterForX(float x)
        {
            int interval = 0;
            while (interval < SampleCount - 2 && samples[interval + 1] <= x)
            {
                interval++;
            }
            float start = interval * SampleStep;
            float span = samples[interval + 1] - samples[interval];
            float t = span > 0f ? start + (x - samples[interval]) / span * SampleStep : start;

            float slope = SlopeX(t);
            if (slope >= 0.001f)
            {
                for (int i = 0; i < 4; i++)
                {
                    slope = SlopeX(t);
                    if (slope == 0f)
                    {
                        break;
                    }
                    t -= (CurveX(t) - x) / slope;
                }
                return Mathf.Clamp01(t);
            }
            if (slope == 0f)
            {
                return t;
            }
            float low = start;
            float high = start + SampleStep;
            for (int i = 0; i < 12; i++)
            {
                t = (low + high) * 0.5f;
                float difference = CurveX(t) - x;
                if (Mathf.Abs(difference) < 0.0001f)
                {
                    break;
                }
                if (difference > 0f) high = t;
                else low = t;
            }
            return t;
        }
    }
}
