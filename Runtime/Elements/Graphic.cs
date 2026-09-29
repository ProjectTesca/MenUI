// Graphic: a leaf that draws shapes the box model cannot — morphing shapes,
// arcs and wavy lines (progress indicators), ripples. The draw function runs
// like a binding: it reruns (and the element repaints) when a signal it read
// changes, or when the element's size changes.
//
//   AnimatedFloat progress = Animated(0f);
//   Graphic(g =>
//   {
//       g.Arc(g.Width / 2, g.Height / 2, 20, 4, 0, 360, track);
//       g.Arc(g.Width / 2, g.Height / 2, 20, 4, 0, 360 * progress.Value, accent, waveAmplitude: 2, waves: 10);
//   }).Size(48, 48);
//
//   Graphic(g => g.Morph(0, 0, g.Width, g.Height, ShapeSpec.Cookie9, ShapeSpec.Pentagon, t.Value, color)).Size(48, 48);
//
// Coordinates are logical px from the content box's top-left; angles are
// degrees, clockwise from the top. Each command is one draw record; keep the
// number of commands steady (a change of count repaints the whole panel).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    /// One shape of the family Graphic draws and morphs between (Material 3
    /// Expressive's shapes, approximated): scalloped (cookies, clovers),
    /// rays (sunny, bursts), rounded polygons, ovals and pills.
    public sealed class ShapeSpec
    {
        internal const int KindScalloped = 0;
        internal const int KindRays = 1;
        internal const int KindPolygon = 2;
        internal const int KindOval = 3;
        internal const int KindPill = 4;

        internal readonly float kind;
        internal readonly float count;
        internal readonly float amount;
        internal readonly float rotation;   // degrees

        private ShapeSpec(int kind, float count, float amount, float rotation)
        {
            this.kind = kind;
            this.count = count;
            this.amount = amount;
            this.rotation = rotation;
        }

        /// `lobes` rounded bumps; `depth` 0..1 (0: a circle).
        public static ShapeSpec Scalloped(int lobes, float depth, float rotation = 0f) { return new ShapeSpec(KindScalloped, lobes, depth, rotation); }
        /// `rays` pointed tips; `depth` 0..1.
        public static ShapeSpec Rays(int rays, float depth, float rotation = 0f) { return new ShapeSpec(KindRays, rays, depth, rotation); }
        /// A regular polygon with a corner at the top; `rounding` 0..1.
        public static ShapeSpec Polygon(int sides, float rounding, float rotation = 0f) { return new ShapeSpec(KindPolygon, sides, rounding, rotation); }
        /// An oval `height` / `width` (0..1) tall.
        public static ShapeSpec Oval(float height, float rotation = 0f) { return new ShapeSpec(KindOval, 1f, height, rotation); }
        /// A pill (stadium) `height` / `width` (0..1) tall.
        public static ShapeSpec Pill(float height, float rotation = 0f) { return new ShapeSpec(KindPill, 1f, height, rotation); }

        /// The same shape turned by `degrees` more.
        public ShapeSpec Rotated(float degrees) { return new ShapeSpec((int)kind, count, amount, rotation + degrees); }

        public static ShapeSpec Circle { get { return Scalloped(1, 0f); } }
        public static ShapeSpec Square { get { return Polygon(4, 0.3f, 45f); } }
        public static ShapeSpec Diamond { get { return Polygon(4, 0.3f); } }
        public static ShapeSpec Triangle { get { return Polygon(3, 0.35f); } }
        public static ShapeSpec Pentagon { get { return Polygon(5, 0.3f); } }
        public static ShapeSpec Hexagon { get { return Polygon(6, 0.3f, 30f); } }
        public static ShapeSpec Cookie4 { get { return Scalloped(4, 0.16f, 45f); } }
        public static ShapeSpec Cookie6 { get { return Scalloped(6, 0.14f); } }
        public static ShapeSpec Cookie7 { get { return Scalloped(7, 0.13f); } }
        public static ShapeSpec Cookie9 { get { return Scalloped(9, 0.12f); } }
        public static ShapeSpec Cookie12 { get { return Scalloped(12, 0.1f); } }
        public static ShapeSpec Clover4 { get { return Scalloped(4, 0.42f, 45f); } }
        public static ShapeSpec Clover8 { get { return Scalloped(8, 0.32f); } }
        public static ShapeSpec Flower { get { return Scalloped(8, 0.22f); } }
        public static ShapeSpec Sunny { get { return Rays(8, 0.16f); } }
        public static ShapeSpec VerySunny { get { return Rays(8, 0.28f); } }
        public static ShapeSpec SoftBurst { get { return Rays(10, 0.22f); } }
        public static ShapeSpec Burst { get { return Rays(12, 0.34f); } }
        public static ShapeSpec Boom { get { return Rays(15, 0.45f); } }
        public static ShapeSpec Oval45 { get { return Oval(0.7f, -45f); } }
        public static ShapeSpec Pill45 { get { return Pill(0.55f, -45f); } }
    }

    /// What a Graphic's draw function draws on (see Graphic).
    public sealed class GraphicCanvas
    {
        internal const int CommandRipple = 0;
        internal const int CommandShape = 1;
        internal const int CommandArc = 2;
        internal const int CommandLine = 3;
        internal const int CommandRectangle = 4;
        internal const int FloatsPerCommand = 10;

        // Per command: kind, then its numbers (logical px, radians).
        internal readonly List<float> numbers = new List<float>();
        internal readonly List<Color> colors = new List<Color>();
        internal readonly List<ShapeSpec> shapes = new List<ShapeSpec>();
        internal int count;

        /// The content box's size in logical px.
        public float Width { get; internal set; }
        public float Height { get; internal set; }

        internal void Clear()
        {
            numbers.Clear();
            colors.Clear();
            shapes.Clear();
            count = 0;
        }

        private void Add(int kind, float a, float b, float c, float d, float e, float f, float g, float h, float i, Color color)
        {
            numbers.Add(kind);
            numbers.Add(a);
            numbers.Add(b);
            numbers.Add(c);
            numbers.Add(d);
            numbers.Add(e);
            numbers.Add(f);
            numbers.Add(g);
            numbers.Add(h);
            numbers.Add(i);
            colors.Add(color);
            count++;
        }

        /// A circle clipped to the graphic's box — or, with ClipToParent, to
        /// its parent's (rounded) box: the ink of a press.
        public void Ripple(float x, float y, float radius, Color color, float softness = 0f)
        {
            Add(CommandRipple, x, y, radius, softness, 0f, 0f, 0f, 0f, 0f, color);
            shapes.Add(null);
            shapes.Add(null);
        }

        /// `shape` fitted in the rectangle, turned by `rotation` degrees.
        public void Shape(float x, float y, float width, float height, ShapeSpec shape, Color color, float rotation = 0f)
        {
            Morph(x, y, width, height, shape, shape, 0f, color, rotation);
        }

        /// A shape between `from` and `to` (`progress` 0..1).
        public void Morph(float x, float y, float width, float height, ShapeSpec from, ShapeSpec to, float progress,
            Color color, float rotation = 0f)
        {
            Add(CommandShape, x, y, width, height, progress, rotation * Mathf.Deg2Rad, 0f, 0f, 0f, color);
            shapes.Add(from);
            shapes.Add(to);
        }

        public void Circle(float centerX, float centerY, float radius, Color color)
        {
            Shape(centerX - radius, centerY - radius, radius * 2f, radius * 2f, ShapeSpec.Circle, color);
        }

        /// A stroked arc with round caps; `sweep` 360 is a full ring. Wavy
        /// with `waveAmplitude` (px) and `waves` per full turn.
        public void Arc(float centerX, float centerY, float radius, float thickness, float start, float sweep, Color color,
            float waveAmplitude = 0f, float waves = 0f, float wavePhase = 0f)
        {
            Add(CommandArc, centerX, centerY, radius, thickness, Mathf.Repeat(start, 360f) * Mathf.Deg2Rad,
                Mathf.Clamp(sweep, 0f, 360f) * Mathf.Deg2Rad, waveAmplitude, waves, wavePhase * Mathf.Deg2Rad, color);
            shapes.Add(null);
            shapes.Add(null);
        }

        /// A stroked horizontal line from `x0` to `x1` with round caps; wavy
        /// with `waveAmplitude` (px), `wavelength` (px) and `wavePhase` (degrees).
        public void Line(float x0, float x1, float y, float thickness, Color color,
            float waveAmplitude = 0f, float wavelength = 0f, float wavePhase = 0f)
        {
            Add(CommandLine, x0, Mathf.Max(x0, x1), y, thickness, waveAmplitude, wavelength, wavePhase * Mathf.Deg2Rad, 0f, 0f, color);
            shapes.Add(null);
            shapes.Add(null);
        }

        public void Rectangle(float x, float y, float width, float height, float radius, Color color)
        {
            Rectangle(x, y, width, height, radius, radius, radius, radius, color);
        }

        /// A rectangle with a radius per corner (top-left, top-right,
        /// bottom-right, bottom-left).
        public void Rectangle(float x, float y, float width, float height, float topLeft, float topRight,
            float bottomRight, float bottomLeft, Color color)
        {
            Add(CommandRectangle, x, y, width, height, topLeft, topRight, bottomRight, bottomLeft, 0f, color);
            shapes.Add(null);
            shapes.Add(null);
        }
    }

    public sealed class Graphic : Element
    {
        private readonly Action<GraphicCanvas> draw;
        internal readonly GraphicCanvas canvas = new GraphicCanvas();
        private readonly Effect recorder;
        internal bool clipToParent;
        private int recordedWidth = -1, recordedHeight = -1;

        internal Graphic(MenUIContext context, Action<GraphicCanvas> draw) : base(context)
        {
            tag = "graphic";
            this.draw = draw;
            recorder = new Effect(context.runtime, context.runtime.currentOwner, () => Record());
            recorder.Run();
        }

        internal override bool IsLeaf { get { return true; } }

        private void Record()
        {
            canvas.Clear();
            float scale = context.scale;
            canvas.Width = Mathf.Max(0, recordedWidth) / scale;
            canvas.Height = Mathf.Max(0, recordedHeight) / scale;
            draw(canvas);
            MarkPaintDirty();
        }

        internal override void ArrangeContent(int contentWidth, int contentHeight)
        {
            if (contentWidth != recordedWidth || contentHeight != recordedHeight)
            {
                recordedWidth = contentWidth;
                recordedHeight = contentHeight;
                recorder.Run();
            }
        }

        internal override void PaintContent(Painter painter, float opacity)
        {
            if (!computed.IsVisible)
            {
                return;
            }
            painter.PaintGraphic(this, opacity);
        }
    }

    public static class GraphicExtensions
    {
        /// Ripples are clipped to the parent's rounded box (a press layer
        /// laid over its parent).
        public static Graphic ClipToParent(this Graphic self)
        {
            self.clipToParent = true;
            self.MarkPaintDirty();
            return self;
        }
    }
}
