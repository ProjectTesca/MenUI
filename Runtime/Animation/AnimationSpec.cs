// How a value moves to a new target: a timed tween along an easing curve,
// or a spring.
//
//   AnimationSpec.Tween(0.2f)                          // CSS `ease`, 200 ms
//   AnimationSpec.Tween(0.3f, Easing.CubicBezier(0.2f, 0f, 0f, 1f))
//   AnimationSpec.Spring(380f, 0.8f)                   // stiffness, damping ratio
//   AnimationSpec.Tween(0.2f).Delay(0.1f)
//
// A spring has no fixed duration: it settles when it gets there. It keeps its
// velocity when the target changes midway, so a retargeted motion (hovering
// in and out quickly) never jumps. A damping ratio below 1 overshoots and
// bounces a little; 1 arrives as fast as possible without overshooting.
// Tweens restart from where the value is when retargeted.

using UnityEngine;

namespace MenUI
{
    public sealed class AnimationSpec
    {
        internal readonly bool isSpring;
        internal readonly float duration;
        internal readonly Easing easing;
        internal readonly float stiffness;
        internal readonly float dampingRatio;
        internal readonly float delay;

        private AnimationSpec(bool isSpring, float duration, Easing easing, float stiffness, float dampingRatio, float delay)
        {
            this.isSpring = isSpring;
            this.duration = duration;
            this.easing = easing;
            this.stiffness = stiffness;
            this.dampingRatio = dampingRatio;
            this.delay = delay;
        }

        /// Over `seconds`, along `easing` (CSS `ease` when null).
        public static AnimationSpec Tween(float seconds, Easing easing = null)
        {
            return new AnimationSpec(false, Mathf.Max(0f, seconds), easing ?? Easing.Ease, 0f, 0f, 0f);
        }

        /// A spring (mass 1): `stiffness` sets the speed (higher is faster),
        /// `dampingRatio` the bounce (below 1 overshoots; 1 is critical).
        public static AnimationSpec Spring(float stiffness = 380f, float dampingRatio = 0.8f)
        {
            return new AnimationSpec(true, 0f, null, Mathf.Max(1f, stiffness), Mathf.Max(0.01f, dampingRatio), 0f);
        }

        /// The same motion, starting `seconds` later.
        public AnimationSpec Delay(float seconds)
        {
            return new AnimationSpec(isSpring, duration, easing, stiffness, dampingRatio, Mathf.Max(0f, seconds));
        }
    }

    /// The moving part of an animation: one value of `channels` numbers (a
    /// color has four) heading for its target with an AnimationSpec.
    internal sealed class MotionTrack
    {
        internal readonly int channels;
        internal readonly float[] value;
        internal readonly float[] velocity;
        internal readonly float[] from;
        internal readonly float[] to;
        internal AnimationSpec spec;
        internal float elapsed;
        internal bool running;
        /// Close enough to rest (value units), for springs.
        internal float precision = 0.001f;

        internal MotionTrack(int channels)
        {
            this.channels = channels;
            value = new float[channels];
            velocity = new float[channels];
            from = new float[channels];
            to = new float[channels];
        }

        /// Jumps to the target and stops.
        internal void Snap()
        {
            for (int i = 0; i < channels; i++)
            {
                value[i] = to[i];
                velocity[i] = 0f;
            }
            running = false;
        }

        /// Sets out for `to` from where it is now. Returns whether it moves at all.
        internal bool Start(AnimationSpec newSpec)
        {
            spec = newSpec;
            elapsed = 0f;
            bool moves = false;
            for (int i = 0; i < channels; i++)
            {
                from[i] = value[i];
                if (to[i] != value[i] || velocity[i] != 0f)
                {
                    moves = true;
                }
            }
            if (!moves)
            {
                running = false;
                return false;
            }
            if (!spec.isSpring && spec.duration <= 0f && spec.delay <= 0f)
            {
                Snap();
                return false;
            }
            running = true;
            return true;
        }

        /// Advances by `seconds`; returns whether it is still moving.
        internal bool Step(float seconds)
        {
            if (!running)
            {
                return false;
            }
            elapsed += seconds;
            float active = elapsed - spec.delay;
            if (active <= 0f)
            {
                return true;   // still waiting
            }
            if (!spec.isSpring)
            {
                float progress = spec.duration > 0f ? active / spec.duration : 1f;
                if (progress >= 1f)
                {
                    Snap();
                    return false;
                }
                float eased = spec.easing.Evaluate(progress);
                for (int i = 0; i < channels; i++)
                {
                    float next = from[i] + (to[i] - from[i]) * eased;
                    velocity[i] = seconds > 0f ? (next - value[i]) / seconds : 0f;
                    value[i] = next;
                }
                return true;
            }
            // The first step after the delay only covers the time past it.
            float dt = Mathf.Min(seconds, active);
            bool resting = true;
            for (int i = 0; i < channels; i++)
            {
                StepSpring(i, dt);
                if (Mathf.Abs(value[i] - to[i]) > precision || Mathf.Abs(velocity[i]) > precision * 10f)
                {
                    resting = false;
                }
            }
            if (resting)
            {
                Snap();
                return false;
            }
            return true;
        }

        /// The damped spring's exact motion over `dt` (stable at any frame time).
        private void StepSpring(int i, float dt)
        {
            float omega = Mathf.Sqrt(spec.stiffness);
            float zeta = spec.dampingRatio;
            float x = value[i] - to[i];
            float v = velocity[i];
            float nextX, nextV;
            if (zeta < 0.9999f)
            {
                float damped = omega * Mathf.Sqrt(1f - zeta * zeta);
                float decay = Mathf.Exp(-zeta * omega * dt);
                float a = x;
                float b = (v + zeta * omega * x) / damped;
                float cos = Mathf.Cos(damped * dt);
                float sin = Mathf.Sin(damped * dt);
                nextX = decay * (a * cos + b * sin);
                nextV = decay * (-zeta * omega * (a * cos + b * sin) + (-a * damped * sin + b * damped * cos));
            }
            else if (zeta <= 1.0001f)
            {
                float decay = Mathf.Exp(-omega * dt);
                float a = x;
                float b = v + omega * x;
                nextX = decay * (a + b * dt);
                nextV = decay * (b - omega * (a + b * dt));
            }
            else
            {
                float root = Mathf.Sqrt(zeta * zeta - 1f);
                float r1 = -omega * (zeta - root);
                float r2 = -omega * (zeta + root);
                float c2 = (v - r1 * x) / (r2 - r1);
                float c1 = x - c2;
                float e1 = Mathf.Exp(r1 * dt);
                float e2 = Mathf.Exp(r2 * dt);
                nextX = c1 * e1 + c2 * e2;
                nextV = r1 * c1 * e1 + r2 * c2 * e2;
            }
            value[i] = to[i] + nextX;
            velocity[i] = nextV;
        }
    }
}
