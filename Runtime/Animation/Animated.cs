// Values that animate: read them in bindings like signals, and send them
// somewhere with an AnimationSpec.
//
//   AnimatedFloat lift = Animated(0f);
//   card.Translate(() => 0f, () => -lift.Value)
//       .OnPointerEnter(e => lift.AnimateTo(4f, AnimationSpec.Spring()))
//       .OnPointerLeave(e => lift.AnimateTo(0f, AnimationSpec.Spring()));
//
//   AnimatedColor tint = Animated(Colors.White);
//   box.Background(() => tint.Value);
//   tint.AnimateTo(Colors.Hex(0x3B82F6), AnimationSpec.Tween(0.3f, Easing.EaseOut));
//
//   // Keyframes: 0 → 1 over 0.2 s, hold, back to 0 over 0.4 s, forever.
//   pulse.Play(new Keyframes(0f).To(1f, 0.2f).Hold(0.3f).To(0f, 0.4f, Easing.EaseIn), loop: true);
//
//   // Anything else, frame by frame (progress 0 → 1, eased):
//   Animate(0.5f, t => wave.Value = Mathf.Sin(t * Mathf.PI), Easing.EaseInOut);
//
// Every frame's changes are applied together (one batch), and an animation
// stops by itself when the scope that created it goes away.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    /// A number that moves smoothly to its targets. `Value` is tracked like
    /// a signal's.
    public sealed class AnimatedFloat : IFrameTask
    {
        private readonly MenUIContext context;
        private readonly Signal<float> signal;
        private readonly MotionTrack track = new MotionTrack(1);
        private Keyframes keyframes;
        private bool loop;
        private float keyframeTime;
        private Action onComplete;
        private bool ticking;
        private bool disposed;

        private readonly Owner scope;
        Owner IFrameTask.Scope { get { return scope; } }

        internal AnimatedFloat(MenUIContext context, float initial)
        {
            this.context = context;
            scope = context.runtime.currentOwner;
            signal = new Signal<float>(context.runtime, initial);
            track.value[0] = initial;
            track.to[0] = initial;
            track.precision = 0.001f;
            context.OnCleanup(() => disposed = true);
        }

        /// The current value (a dependency when read in a binding).
        public float Value { get { return signal.Value; } }

        /// The current value, without becoming a dependency.
        public float Peek()
        {
            return signal.Peek();
        }

        /// Where it is heading (the value itself when at rest).
        public float Target { get { return keyframes != null ? keyframes.EndValue : track.to[0]; } }

        /// Units per second.
        public float Velocity { get { return track.velocity[0]; } }

        public bool IsAnimating { get { return track.running || keyframes != null; } }

        /// How close counts as arrived for springs (default 0.001; raise it
        /// for large values such as pixels, e.g. 0.05).
        public float Precision
        {
            get { return track.precision; }
            set { track.precision = Mathf.Max(0.000001f, value); }
        }

        /// Jumps to `value` and stops.
        public void Set(float value)
        {
            keyframes = null;
            onComplete = null;
            track.to[0] = value;
            track.Snap();
            signal.Value = value;
        }

        /// Moves to `target` with `animation`, from where it is now (keeping its
        /// velocity for a spring). `onComplete` runs when it arrives, unless
        /// another AnimateTo, Play, Set or Stop comes first.
        public void AnimateTo(float target, AnimationSpec animation, Action onComplete = null)
        {
            keyframes = null;
            this.onComplete = onComplete;
            track.to[0] = target;
            if (track.Start(animation))
            {
                Run();
            }
            else
            {
                signal.Value = target;
                Complete();
            }
        }

        /// Plays `keyframes` from the start (again and again with `loop`).
        public void Play(Keyframes keyframes, bool loop = false, Action onComplete = null)
        {
            track.running = false;
            track.velocity[0] = 0f;
            this.keyframes = keyframes;
            this.loop = loop;
            this.onComplete = onComplete;
            keyframeTime = 0f;
            float first = keyframes.Evaluate(0f);
            track.value[0] = first;
            track.to[0] = first;
            signal.Value = first;
            Run();
        }

        /// Stops where it is.
        public void Stop()
        {
            keyframes = null;
            onComplete = null;
            track.to[0] = track.value[0];
            track.Snap();
        }

        private void Run()
        {
            if (!ticking && !disposed)
            {
                ticking = true;
                context.StartAnimating(this);
            }
        }

        private void Complete()
        {
            Action done = onComplete;
            onComplete = null;
            if (done != null)
            {
                done();
            }
        }

        public bool Tick(float seconds)
        {
            if (disposed)
            {
                ticking = false;
                return false;
            }
            if (keyframes != null)
            {
                keyframeTime += seconds;
                float duration = keyframes.Duration;
                bool finished = false;
                if (keyframeTime >= duration)
                {
                    if (loop && duration > 0f)
                    {
                        keyframeTime %= duration;
                    }
                    else
                    {
                        keyframeTime = duration;
                        finished = true;
                    }
                }
                float next = keyframes.Evaluate(keyframeTime);
                track.value[0] = next;
                track.to[0] = next;
                signal.Value = next;
                if (finished)
                {
                    keyframes = null;
                    ticking = false;
                    Complete();
                    return IsAnimating && Continue();
                }
                return true;
            }
            bool moving = track.Step(seconds);
            signal.Value = track.value[0];
            if (!moving)
            {
                ticking = false;
                Complete();
                return IsAnimating && Continue();
            }
            return true;
        }

        /// `onComplete` started another animation: keep ticking (the task is
        /// still in the list, so it must not be added twice).
        private bool Continue()
        {
            ticking = true;
            return true;
        }
    }

    /// A color that moves smoothly to its targets (each channel in step).
    public sealed class AnimatedColor : IFrameTask
    {
        private readonly MenUIContext context;
        private readonly Signal<Color> signal;
        private readonly MotionTrack track = new MotionTrack(4);
        private Action onComplete;
        private bool ticking;
        private bool disposed;

        private readonly Owner scope;
        Owner IFrameTask.Scope { get { return scope; } }

        internal AnimatedColor(MenUIContext context, Color initial)
        {
            this.context = context;
            scope = context.runtime.currentOwner;
            signal = new Signal<Color>(context.runtime, initial);
            Load(track.value, initial);
            Load(track.to, initial);
            track.precision = 0.001f;
            context.OnCleanup(() => disposed = true);
        }

        public Color Value { get { return signal.Value; } }

        public Color Peek()
        {
            return signal.Peek();
        }

        public Color Target { get { return new Color(track.to[0], track.to[1], track.to[2], track.to[3]); } }

        public bool IsAnimating { get { return track.running; } }

        public void Set(Color value)
        {
            onComplete = null;
            Load(track.to, value);
            track.Snap();
            signal.Value = value;
        }

        public void AnimateTo(Color target, AnimationSpec animation, Action onComplete = null)
        {
            this.onComplete = onComplete;
            Load(track.to, target);
            if (track.Start(animation))
            {
                if (!ticking && !disposed)
                {
                    ticking = true;
                    context.StartAnimating(this);
                }
            }
            else
            {
                signal.Value = target;
                Complete();
            }
        }

        public void Stop()
        {
            onComplete = null;
            for (int i = 0; i < 4; i++)
            {
                track.to[i] = track.value[i];
            }
            track.Snap();
        }

        private void Complete()
        {
            Action done = onComplete;
            onComplete = null;
            if (done != null)
            {
                done();
            }
        }

        public bool Tick(float seconds)
        {
            if (disposed)
            {
                ticking = false;
                return false;
            }
            bool moving = track.Step(seconds);
            float[] v = track.value;
            signal.Value = new Color(v[0], v[1], v[2], v[3]);
            if (!moving)
            {
                ticking = false;
                Complete();
                if (track.running)
                {
                    ticking = true;
                    return true;
                }
                return false;
            }
            return true;
        }

        private static void Load(float[] channels, Color color)
        {
            channels[0] = color.r;
            channels[1] = color.g;
            channels[2] = color.b;
            channels[3] = color.a;
        }
    }

    /// A path for AnimatedFloat.Play: a start value, then segments that each
    /// move to a value over a time along an easing curve.
    public sealed class Keyframes
    {
        private readonly List<float> values = new List<float>();
        private readonly List<float> times = new List<float>();     // end time of each segment
        private readonly List<Easing> easings = new List<Easing>();
        private float duration;

        public Keyframes(float start)
        {
            values.Add(start);
        }

        /// Moves to `value` over `seconds` (linear when `easing` is null).
        public Keyframes To(float value, float seconds, Easing easing = null)
        {
            duration += Mathf.Max(0f, seconds);
            values.Add(value);
            times.Add(duration);
            easings.Add(easing);
            return this;
        }

        /// Stays at the last value for `seconds`.
        public Keyframes Hold(float seconds)
        {
            return To(values[values.Count - 1], seconds);
        }

        /// Total length in seconds.
        public float Duration { get { return duration; } }

        internal float EndValue { get { return values[values.Count - 1]; } }

        /// The value `time` seconds in.
        public float Evaluate(float time)
        {
            float start = 0f;
            for (int i = 0; i < times.Count; i++)
            {
                float end = times[i];
                if (time < end)
                {
                    float progress = end > start ? (time - start) / (end - start) : 1f;
                    Easing easing = easings[i];
                    float eased = easing != null ? easing.Evaluate(progress) : progress;
                    return values[i] + (values[i + 1] - values[i]) * eased;
                }
                start = end;
            }
            return values[values.Count - 1];
        }
    }

    /// A running Animate / EveryFrame; stop it early with Stop.
    public sealed class Playback : IFrameTask
    {
        private readonly float duration;
        private readonly Easing easing;
        private readonly Action<float> frame;
        private readonly Action onComplete;
        private float elapsed;
        private bool stopped;

        /// `duration` below 0: every frame until stopped, `frame` getting the
        /// seconds since the last frame.
        private readonly Owner scope;
        Owner IFrameTask.Scope { get { return scope; } }

        internal Playback(MenUIContext context, float duration, Easing easing, Action<float> frame, Action onComplete)
        {
            scope = context.runtime.currentOwner;
            this.duration = duration;
            this.easing = easing;
            this.frame = frame;
            this.onComplete = onComplete;
            context.OnCleanup(Stop);
        }

        public bool IsPlaying { get { return !stopped; } }

        public void Stop()
        {
            stopped = true;
        }

        public bool Tick(float seconds)
        {
            if (stopped)
            {
                return false;
            }
            if (duration < 0f)
            {
                frame(seconds);
                return !stopped;
            }
            elapsed += seconds;
            float progress = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            frame(easing != null ? easing.Evaluate(progress) : progress);
            if (progress >= 1f)
            {
                stopped = true;
                if (onComplete != null)
                {
                    onComplete();
                }
                return false;
            }
            return !stopped;
        }
    }
}
