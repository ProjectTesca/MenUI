// Style transitions (CSS `transition`): when a transitioned property of an
// element changes — a state, a class, a binding, a style sheet — the new
// value becomes a target, and the shown value moves there as the property's
// AnimationSpec says.
//
//   .Rule("button", s => s.Background(gray).Transition(TransitionGroup.Colors, AnimationSpec.Tween(0.15f)))
//   .Rule("button:hover", s => s.Background(lightGray))
//
//   Div().Width(() => open.Value ? 320 : 64).Transition(StyleProperty.Width, AnimationSpec.Spring())
//
//   Div().EnterFrom(s => s.Opacity(0)).ExitTo(s => s.Opacity(0))
//        .Transition(StyleProperty.Opacity, AnimationSpec.Tween(0.2f))      // fades in and out
//
// This is the fast path for animating styles: each frame writes the moving
// values straight into the element's computed style (no signals, no style
// resolution) and redraws only what they touch — a color or a radius
// rewrites the element's own records, opacity its subtree's, and a length
// lays out again only as far as the change reaches (LayoutEngine.Run).
// Inherited properties (text color, font size) go through style resolution,
// so the children follow.
//
// What animates: lengths in pixels or percent (on both ends; `auto` jumps),
// flex grow/shrink, font size, corner radii, opacity and the colors.
// Shadows, enums and fonts jump.

using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    /// Sets of properties for `.Transition(group, animation)`.
    public static class TransitionGroup
    {
        /// Everything that can animate (CSS `transition: all`).
        public const int All = 1000;
        /// Background, border and text colors.
        public const int Colors = 1001;
        /// The corner radii.
        public const int Corners = 1002;
        /// Width and height, with their minimums and maximums, and flex-basis.
        public const int Size = 1003;
        /// Padding, margin, border widths and gaps.
        public const int Spacing = 1004;
        /// The insets: top, right, bottom, left.
        public const int Position = 1005;
    }

    internal static class StyleTransitions
    {
        internal static bool IsAnimatable(int property)
        {
            return property == StyleProperty.FlexGrow || property == StyleProperty.FlexShrink
                || (property >= StyleProperty.FlexBasis && property <= StyleProperty.FontSize)
                || (property >= StyleProperty.CornerTopLeft && property <= StyleProperty.TextColor);
        }

        /// Whether `property` belongs to `selector` (a TransitionGroup or a
        /// single StyleProperty).
        internal static bool InGroup(int property, int selector)
        {
            if (!IsAnimatable(property))
            {
                return false;
            }
            if (selector < StyleProperty.Count) return property == selector;
            if (selector == TransitionGroup.All) return true;
            if (selector == TransitionGroup.Colors) return IsColor(property);
            if (selector == TransitionGroup.Corners) return property >= StyleProperty.CornerTopLeft && property <= StyleProperty.CornerBottomLeft;
            if (selector == TransitionGroup.Size) return property >= StyleProperty.FlexBasis && property <= StyleProperty.MaxHeight;
            if (selector == TransitionGroup.Spacing)
            {
                return (property >= StyleProperty.PaddingTop && property <= StyleProperty.BorderLeftWidth)
                    || property == StyleProperty.RowGap || property == StyleProperty.ColumnGap;
            }
            if (selector == TransitionGroup.Position) return property >= StyleProperty.Top && property <= StyleProperty.Left;
            return false;
        }

        internal static bool IsColor(int property)
        {
            return property >= StyleProperty.Background && property <= StyleProperty.TextColor;
        }

        /// Close enough to rest, in the property's computed units.
        internal static float Precision(int property)
        {
            if (IsColor(property) || property == StyleProperty.Opacity) return 0.002f;
            if (property == StyleProperty.FlexGrow || property == StyleProperty.FlexShrink) return 0.001f;
            if (property == StyleProperty.FontSize) return 0.02f;
            return 0.05f;   // physical pixels (or percent)
        }

        /// Pixel lengths that layout reads as whole pixels.
        private static bool IsWholePixels(int property)
        {
            return property >= StyleProperty.FlexBasis && property <= StyleProperty.ColumnGap;
        }

        /// Lengths that cannot be negative (springs overshoot).
        private static bool IsNonNegative(int property)
        {
            return (property >= StyleProperty.FlexBasis && property <= StyleProperty.PaddingLeft)
                || (property >= StyleProperty.BorderTopWidth && property <= StyleProperty.BorderLeftWidth)
                || property >= StyleProperty.RowGap;
        }

        /// Whether `unit` can be interpolated (pixels or percent).
        internal static bool IsInterpolable(int unit)
        {
            return unit == (int)LengthUnit.Pixels || unit == (int)LengthUnit.Percent;
        }

        internal static void LoadTarget(float[] into, ComputedStyle computed, int property)
        {
            if (IsColor(property))
            {
                Color c = computed.colors[property];
                into[0] = c.r;
                into[1] = c.g;
                into[2] = c.b;
                into[3] = c.a;
            }
            else
            {
                into[0] = computed.numbers[property];
            }
        }

        internal static bool DiffersFromTarget(float[] target, ComputedStyle computed, int property)
        {
            if (IsColor(property))
            {
                Color c = computed.colors[property];
                return target[0] != c.r || target[1] != c.g || target[2] != c.b || target[3] != c.a;
            }
            return target[0] != computed.numbers[property];
        }

        /// Writes a moving value into the computed style.
        internal static void Write(ComputedStyle computed, int property, float[] value)
        {
            if (IsColor(property))
            {
                computed.colors[property] = new Color(Mathf.Clamp01(value[0]), Mathf.Clamp01(value[1]),
                    Mathf.Clamp01(value[2]), Mathf.Clamp01(value[3]));
                return;
            }
            float v = value[0];
            if (property == StyleProperty.Opacity)
            {
                v = Mathf.Clamp01(v);
            }
            else
            {
                if (IsNonNegative(property) && v < 0f) v = 0f;
                if (IsWholePixels(property) && computed.units[property] == (int)LengthUnit.Pixels) v = Mathf.Round(v);
            }
            computed.numbers[property] = v;
        }
    }

    /// One property of one element on its way to a new value.
    internal sealed class PropertyAnimation
    {
        internal int property;
        internal int unit;
        internal MotionTrack track;
    }

    /// The running transitions of one element.
    internal sealed class StyleAnimator
    {
        internal readonly Element element;
        internal readonly List<PropertyAnimation> animations = new List<PropertyAnimation>();
        /// In StyleAnimations' list.
        internal bool active;

        internal StyleAnimator(Element element)
        {
            this.element = element;
        }

        internal PropertyAnimation Find(int property)
        {
            for (int i = 0; i < animations.Count; i++)
            {
                if (animations[i].property == property)
                {
                    return animations[i];
                }
            }
            return null;
        }

        internal bool IsRunning { get { return animations.Count > 0; } }
    }

    /// Every element's running transitions, advanced once a frame.
    internal sealed class StyleAnimations : IFrameTask
    {
        private readonly MenUIContext context;
        private readonly List<StyleAnimator> active = new List<StyleAnimator>();
        private bool ticking;

        internal StyleAnimations(MenUIContext context)
        {
            this.context = context;
        }

        /// Called by the style resolver once `e.computed` holds the new style
        /// (the targets). Starts, retargets or ends transitions, and writes
        /// the values to show now in place of the targets. `previous` is what
        /// the element showed before (unless this is its first style), and
        /// `entering` its `:enter-from` style on its first resolve (or null).
        internal void Apply(Element e, float[] previousNumbers, int[] previousUnits, Color[] previousColors,
            ComputedStyle entering)
        {
            ComputedStyle computed = e.computed;
            StyleAnimator animator = e.animator;
            bool hasPrevious = e.styleResolvedOnce;
            for (int p = 0; p < StyleProperty.Count; p++)
            {
                AnimationSpec spec = computed.TransitionOf(p);
                PropertyAnimation running = animator != null && animator.IsRunning ? animator.Find(p) : null;
                bool color = StyleTransitions.IsColor(p);
                if (running != null)
                {
                    if (spec == null || !running.track.running || (!color && computed.units[p] != running.unit))
                    {
                        // Transition removed, finished (an inherited property:
                        // Tick left the last step to us), or a unit change: jump.
                        animator.animations.Remove(running);
                        continue;
                    }
                    if (StyleTransitions.DiffersFromTarget(running.track.to, computed, p))
                    {
                        StyleTransitions.LoadTarget(running.track.to, computed, p);
                        if (!running.track.Start(spec))
                        {
                            animator.animations.Remove(running);
                            continue;
                        }
                    }
                    StyleTransitions.Write(computed, p, running.track.value);
                    continue;
                }
                if (spec == null)
                {
                    continue;
                }

                // Where it starts: the entering style, or what was shown.
                int fromUnit;
                if (entering != null)
                {
                    fromUnit = entering.units[p];
                }
                else if (hasPrevious)
                {
                    fromUnit = previousUnits[p];
                }
                else
                {
                    continue;
                }
                if (!color && (fromUnit != computed.units[p] || !StyleTransitions.IsInterpolable(fromUnit)))
                {
                    continue;
                }
                PropertyAnimation animation = new PropertyAnimation();
                animation.property = p;
                animation.unit = computed.units[p];
                animation.track = new MotionTrack(color ? 4 : 1);
                MotionTrack track = animation.track;
                track.precision = StyleTransitions.Precision(p);
                if (color)
                {
                    Color from = entering != null ? entering.colors[p] : previousColors[p];
                    track.value[0] = from.r;
                    track.value[1] = from.g;
                    track.value[2] = from.b;
                    track.value[3] = from.a;
                }
                else
                {
                    track.value[0] = entering != null ? entering.numbers[p] : previousNumbers[p];
                }
                StyleTransitions.LoadTarget(track.to, computed, p);
                if (!track.Start(spec))
                {
                    continue;   // already there
                }
                if (animator == null)
                {
                    animator = new StyleAnimator(e);
                    e.animator = animator;
                }
                animator.animations.Add(animation);
                StyleTransitions.Write(computed, p, track.value);
                Activate(animator);
            }
        }

        private void Activate(StyleAnimator animator)
        {
            if (!animator.active)
            {
                animator.active = true;
                active.Add(animator);
            }
            if (!ticking)
            {
                ticking = true;
                context.StartAnimating(this);
            }
        }

        public bool Tick(float seconds)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                StyleAnimator animator = active[i];
                Element e = animator.element;
                bool attached = context.IsAttached(e);
                bool layout = false, paint = false, opacity = false, restyle = false;
                List<PropertyAnimation> animations = animator.animations;
                for (int j = animations.Count - 1; j >= 0; j--)
                {
                    PropertyAnimation animation = animations[j];
                    int p = animation.property;
                    bool moving;
                    if (attached)
                    {
                        moving = animation.track.Step(seconds);
                    }
                    else
                    {
                        animation.track.Snap();   // nobody sees it: finish now
                        moving = false;
                    }
                    if (StyleProperty.IsInherited(p))
                    {
                        // The children inherit it: resolve the element's style
                        // (which reads the track), and let the resolver drop
                        // the finished animation after its last step.
                        restyle = true;
                        continue;
                    }
                    bool wasOpaque = e.computed.colors[StyleProperty.Background].a > 0f;
                    StyleTransitions.Write(e.computed, p, animation.track.value);
                    if (p == StyleProperty.Background && wasOpaque != e.computed.colors[p].a > 0f)
                    {
                        context.hitVersion++;   // an opaque background makes it hittable
                    }
                    if (StyleProperty.AffectsLayout(p)) layout = true;
                    else if (p == StyleProperty.Opacity) opacity = true;
                    else paint = true;
                    if (!moving)
                    {
                        animations.RemoveAt(j);
                    }
                }
                if (restyle) e.MarkStyleDirty();
                if (layout) e.MarkLayoutDirty(true);
                if (opacity) e.MarkSubtreePaintDirty();
                else if (paint) e.MarkPaintDirty();
                if (animations.Count == 0 || !attached)
                {
                    animations.Clear();
                    animator.active = false;
                    active.RemoveAt(i);
                }
            }
            if (active.Count == 0)
            {
                ticking = false;
                return false;
            }
            return true;
        }
    }

    /// Waits for leaving elements' exit transitions, then disposes their
    /// scope and removes them (MenUIContext.Leave).
    internal sealed class ExitTask : IFrameTask
    {
        private const float Timeout = 10f;   // seconds; a stuck exit still ends
        private readonly List<Element> leaving;
        private readonly Owner owner;
        private readonly System.Action remove;
        private float elapsed;

        internal ExitTask(List<Element> leaving, Owner owner, System.Action remove)
        {
            this.leaving = leaving;
            this.owner = owner;
            this.remove = remove;
        }

        public bool Tick(float seconds)
        {
            // The first tick comes after the frame that applied `:exit-to`
            // (and started its transitions).
            elapsed += seconds;
            if (elapsed < Timeout)
            {
                for (int i = 0; i < leaving.Count; i++)
                {
                    Element e = leaving[i];
                    if (e.animator != null && e.animator.IsRunning)
                    {
                        return true;
                    }
                }
            }
            owner.Dispose();
            remove();
            return false;
        }
    }
}
