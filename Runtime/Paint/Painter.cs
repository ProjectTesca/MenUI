// Turns the laid-out tree into draw records, in CSS painting order.
//
// Order key: (stacking context, z-index, tree order, paint phase).
//   An element with a z-index starts a stacking context: its subtree paints
//   as one unit, placed among the contexts of its nearest ancestor context
//   by z-index (stable, so equal values keep tree order). Inside a context:
//     its own phases 0-3 → child contexts with z < 0 → the in-flow
//     descendants (each: phases 0-3, content, children) → child contexts
//     with z >= 0.
//   Paint phases of one element:
//     0 outer shadows  1 background  2 inset shadows  3 border  4 content
//   The overlay root paints after everything, outside every clip.
//
// Incremental: a full paint remembers, per element, which records its box
// (phases 0-3) and its content went to. A later change that only alters
// how an element looks rewrites exactly those records (Repaint). If the
// element now needs a different number of records — or the tree, display
// or z-index changed — everything is painted again.
//
// Opacity is multiplied into each primitive (group opacity — rendering the
// subtree offscreen first — is not done yet), so overlapping children of a
// translucent element show through each other. A fully transparent subtree
// is skipped — unless its opacity transitions: then it keeps its records at
// zero alpha, so fading in and out rewrites records instead of adding and
// dropping them (a full paint).

using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    public sealed class Painter
    {
        internal const float KindRoundedRectangle = 1f;
        internal const float KindGlyph = 2f;
        internal const float KindOuterShadow = 3f;
        internal const float KindInsetShadow = 4f;
        internal const float KindImage = 5f;   // reserved: images and custom-shader surfaces
        internal const float KindRipple = 6f;
        internal const float KindShape = 7f;
        internal const float KindArc = 8f;
        internal const float KindWave = 9f;
        /// The record's kind texel holds kind + GroupStride * transform group.
        internal const float GroupStride = 16f;

        private readonly PanelRenderer renderer;
        private int generation;
        /// Elements drawn by the last full paint carry this paintGeneration.
        internal int Generation { get { return generation; } }
        // Every element whose box was painted, in paint order (bottom first),
        // published for hit testing (MenUIContext.paintOrder).
        private List<Element> paintOrder;
        /// For diagnostics: whether the last Paint rebuilt every record, and
        /// how many elements it rewrote otherwise.
        internal bool lastPaintWasFull;
        internal int lastRepaintedCount;
        /// Painting inside a subtree whose opacity transitions: keep records
        /// even at zero alpha.
        private bool keepTransparent;

        internal Painter(PanelRenderer renderer)
        {
            this.renderer = renderer;
        }

        /// Updates the records for everything queued; returns after a full
        /// paint or after rewriting just the queued elements' records.
        internal void Paint(MenUIContext context)
        {
            List<Element> queue = context.paintQueue;
            bool repainted = !context.paintStructureDirty && generation > 0 && Repaint(queue);
            lastPaintWasFull = !repainted;
            lastRepaintedCount = queue.Count;
            if (!repainted)
            {
                PaintAll(context);
            }
            for (int i = 0; i < queue.Count; i++)
            {
                queue[i].queuedForPaint = false;
            }
            queue.Clear();
            context.paintStructureDirty = false;
        }

        private bool Repaint(List<Element> queue)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                Element e = queue[i];
                if (e.paintGeneration != generation)
                {
                    // Not drawn last time (hidden, detached, in a transparent
                    // subtree). Whatever would make it drawn now changes an
                    // ancestor's records or the structure too.
                    continue;
                }
                float opacity = AncestorOpacity(e) * OwnOpacity(e);
                keepTransparent = FadesInTree(e);
                bool draw = opacity > 0f || keepTransparent;
                renderer.BeginOverwrite(e.boxRecordStart, e.boxRecordCount);
                if (draw)
                {
                    PaintBox(e, opacity);
                }
                if (!renderer.EndOverwrite())
                {
                    keepTransparent = false;
                    return false;
                }
                renderer.BeginOverwrite(e.contentRecordStart, e.contentRecordCount);
                if (draw)
                {
                    e.PaintContent(this, opacity);
                }
                keepTransparent = false;
                if (!renderer.EndOverwrite())
                {
                    return false;
                }
            }
            return true;
        }

        private void PaintAll(MenUIContext context)
        {
            generation++;
            renderer.BeginRecords();
            paintOrder = context.paintOrder;
            paintOrder.Clear();
            context.hitVersion++;
            Element root = context.root;
            Element overlayRoot = context.overlayRoot;
            keepTransparent = false;
            if (root.computed.Display != DisplayMode.None)
            {
                PaintContext(root, OwnOpacity(root));
            }
            if (overlayRoot.computed.Display != DisplayMode.None)
            {
                PaintContext(overlayRoot, OwnOpacity(overlayRoot));
            }
        }

        /// Its opacity transitions (so it may be fading).
        private static bool Fades(Element e)
        {
            return e.computed.TransitionOf(StyleProperty.Opacity) != null;
        }

        private static bool FadesInTree(Element e)
        {
            for (Element a = e; a != null; a = a.parent)
            {
                if (Fades(a))
                {
                    return true;
                }
            }
            return false;
        }

        private void PaintOwnBox(Element e, float opacity)
        {
            paintOrder.Add(e);
            e.paintGeneration = generation;
            e.boxRecordStart = renderer.RecordCount;
            PaintBox(e, opacity);
            e.boxRecordCount = renderer.RecordCount - e.boxRecordStart;
        }

        private void PaintOwnContent(Element e, float opacity)
        {
            e.contentRecordStart = renderer.RecordCount;
            e.PaintContent(this, opacity);
            e.contentRecordCount = renderer.RecordCount - e.contentRecordStart;
        }

        private static bool CreatesContext(Element e)
        {
            return e.computed.HasZIndex && e.computed.Display != DisplayMode.Contents;
        }

        private void PaintContext(Element context, float opacity)
        {
            bool saved = keepTransparent;
            keepTransparent = FadesInTree(context);
            if (opacity > 0f || keepTransparent)
            {
                PaintContextContent(context, opacity);
            }
            keepTransparent = saved;
        }

        private void PaintContextContent(Element context, float opacity)
        {
            PaintOwnBox(context, opacity);

            List<Element> contexts = new List<Element>();
            CollectContexts(context, contexts);
            SortByZIndex(contexts);

            int index = 0;
            for (; index < contexts.Count && contexts[index].computed.ZIndex < 0; index++)
            {
                Element child = contexts[index];
                PaintContext(child, AncestorOpacity(child) * OwnOpacity(child));
            }
            PaintOwnContent(context, opacity);
            PaintFlowChildren(context, opacity);
            for (; index < contexts.Count; index++)
            {
                Element child = contexts[index];
                PaintContext(child, AncestorOpacity(child) * OwnOpacity(child));
            }
        }

        private void PaintFlow(Element e, float opacity)
        {
            bool saved = keepTransparent;
            if (Fades(e))
            {
                keepTransparent = true;
            }
            if (opacity > 0f || keepTransparent)
            {
                PaintOwnBox(e, opacity);
                PaintOwnContent(e, opacity);
                PaintFlowChildren(e, opacity);
            }
            keepTransparent = saved;
        }

        private void PaintFlowChildren(Element e, float opacity)
        {
            for (int i = 0; i < e.children.Count; i++)
            {
                Element child = e.children[i];
                if (child.computed.Display == DisplayMode.None || CreatesContext(child))
                {
                    continue;
                }
                PaintFlow(child, opacity * OwnOpacity(child));
            }
        }

        private static void CollectContexts(Element e, List<Element> into)
        {
            for (int i = 0; i < e.children.Count; i++)
            {
                Element child = e.children[i];
                if (child.computed.Display == DisplayMode.None)
                {
                    continue;
                }
                if (CreatesContext(child))
                {
                    into.Add(child);
                }
                else
                {
                    CollectContexts(child, into);
                }
            }
        }

        /// Stable insertion sort; context lists are short.
        private static void SortByZIndex(List<Element> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                Element item = list[i];
                int z = item.computed.ZIndex;
                int j = i - 1;
                while (j >= 0 && list[j].computed.ZIndex > z)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = item;
            }
        }

        /// The opacity the records carry: none for an element with a
        /// transform group, whose opacity the GPU applies (see UpdateLayer).
        private static float OwnOpacity(Element e)
        {
            return e.translateGroup != TransformGroups.Root ? 1f : e.computed.Opacity;
        }

        private static float AncestorOpacity(Element e)
        {
            float opacity = 1f;
            for (Element a = e.parent; a != null; a = a.parent)
            {
                opacity *= OwnOpacity(a);
            }
            return opacity;
        }

        // ================================================================
        // Phases 0-3 of one element
        // ================================================================

        private void PaintBox(Element e, float opacity)
        {
            ComputedStyle s = e.computed;
            if (s.Display == DisplayMode.Contents || !s.IsVisible || e.width <= 0 || e.height <= 0)
            {
                return;
            }
            float x = e.x, y = e.y, width = e.width, height = e.height;
            Vector4 radii = CornerRadii(e);
            float radiusTopLeft = radii.x;
            float radiusTopRight = radii.y;
            float radiusBottomRight = radii.z;
            float radiusBottomLeft = radii.w;

            Shadow[] shadows = s.shadows;

            // Phase 0: outer shadows (the first listed is on top, so paint it last).
            if (shadows != null)
            {
                for (int i = shadows.Length - 1; i >= 0; i--)
                {
                    Shadow shadow = shadows[i];
                    if (shadow.Inset || shadow.Color.a <= 0f) continue;
                    float sigma = shadow.Blur * 0.5f;
                    float shapeX = x + shadow.OffsetX - shadow.Spread;
                    float shapeY = y + shadow.OffsetY - shadow.Spread;
                    float shapeWidth = width + shadow.Spread * 2f;
                    float shapeHeight = height + shadow.Spread * 2f;
                    if (shapeWidth <= 0f || shapeHeight <= 0f) continue;
                    float reach = Mathf.Ceil(sigma * 3f) + 1f;
                    int record = Begin(e, shapeX - reach, shapeY - reach, shapeWidth + reach * 2f, shapeHeight + reach * 2f);
                    if (record < 0) continue;
                    renderer.Texel(record, 2, Premultiplied(shadow.Color, opacity));
                    renderer.Texel(record, 3, radiusTopLeft, radiusTopRight, radiusBottomRight, radiusBottomLeft);
                    renderer.Texel(record, 4, KindOuterShadow + GroupStride * e.group, 0f, sigma, shadow.Spread);
                    renderer.Texel(record, 6, x, y, width, height);
                    renderer.Texel(record, 7, shapeX, shapeY, shapeWidth, shapeHeight);
                }
            }

            // Phases 1 and 3: background and border share one primitive.
            Color background = s.ColorOf(StyleProperty.Background);
            float borderTop = s.numbers[StyleProperty.BorderTopWidth];
            float borderRight = s.numbers[StyleProperty.BorderRightWidth];
            float borderBottom = s.numbers[StyleProperty.BorderBottomWidth];
            float borderLeft = s.numbers[StyleProperty.BorderLeftWidth];
            bool hasBorder = borderTop + borderRight + borderBottom + borderLeft > 0f
                && s.ColorOf(StyleProperty.BorderColor).a > 0f;
            bool hasInset = false;
            if (shadows != null)
            {
                for (int i = 0; i < shadows.Length; i++)
                {
                    if (shadows[i].Inset) hasInset = true;
                }
            }

            // An element whose look follows a state (a hover background) or
            // transitions keeps its record while transparent, so the change
            // rewrites that record instead of changing the record count (a
            // full paint).
            if (background.a > 0f || (hasBorder && !hasInset) || HasStateStyles(e))
            {
                int record = Begin(e, x, y, width, height);
                if (record >= 0)
                {
                    renderer.Texel(record, 2, Premultiplied(background, opacity));
                    renderer.Texel(record, 3, radiusTopLeft, radiusTopRight, radiusBottomRight, radiusBottomLeft);
                    renderer.Texel(record, 4, KindRoundedRectangle + GroupStride * e.group, 0f, 0f, 0f);
                    // With inset shadows the border is drawn after them (phase 3).
                    Color border = hasInset ? new Color(0f, 0f, 0f, 0f) : s.ColorOf(StyleProperty.BorderColor);
                    renderer.Texel(record, 5, Premultiplied(border, opacity));
                    if (hasInset) renderer.Texel(record, 6, 0f, 0f, 0f, 0f);
                    else renderer.Texel(record, 6, borderTop, borderRight, borderBottom, borderLeft);
                    renderer.Texel(record, 7, x, y, width, height);
                }
            }

            // Phase 2: inset shadows, then phase 3: the border on top of them.
            if (hasInset)
            {
                for (int i = shadows.Length - 1; i >= 0; i--)
                {
                    Shadow shadow = shadows[i];
                    if (!shadow.Inset || shadow.Color.a <= 0f) continue;
                    int record = Begin(e, x, y, width, height);
                    if (record < 0) continue;
                    renderer.Texel(record, 2, Premultiplied(shadow.Color, opacity));
                    renderer.Texel(record, 3, radiusTopLeft, radiusTopRight, radiusBottomRight, radiusBottomLeft);
                    renderer.Texel(record, 4, KindInsetShadow + GroupStride * e.group, 0f, shadow.Blur * 0.5f, shadow.Spread);
                    renderer.Texel(record, 6, x, y, width, height);
                    renderer.Texel(record, 7, x + shadow.OffsetX + shadow.Spread, y + shadow.OffsetY + shadow.Spread,
                        width - shadow.Spread * 2f, height - shadow.Spread * 2f);
                }
                if (hasBorder)
                {
                    int record = Begin(e, x, y, width, height);
                    if (record >= 0)
                    {
                        renderer.Texel(record, 2, 0f, 0f, 0f, 0f);
                        renderer.Texel(record, 3, radiusTopLeft, radiusTopRight, radiusBottomRight, radiusBottomLeft);
                        renderer.Texel(record, 4, KindRoundedRectangle + GroupStride * e.group, 0f, 0f, 0f);
                        renderer.Texel(record, 5, Premultiplied(s.ColorOf(StyleProperty.BorderColor), opacity));
                        renderer.Texel(record, 6, borderTop, borderRight, borderBottom, borderLeft);
                        renderer.Texel(record, 7, x, y, width, height);
                    }
                }
            }
        }

        /// The element's corner radii in physical px (tl, tr, br, bl), each at
        /// most half its shorter side. Animated radii may be fractional.
        internal static Vector4 CornerRadii(Element e)
        {
            ComputedStyle s = e.computed;
            float shorter = Mathf.Min(e.width, e.height);
            float half = shorter * 0.5f;
            return new Vector4(
                Mathf.Min(Corner(s, StyleProperty.CornerTopLeft, shorter), half),
                Mathf.Min(Corner(s, StyleProperty.CornerTopRight, shorter), half),
                Mathf.Min(Corner(s, StyleProperty.CornerBottomRight, shorter), half),
                Mathf.Min(Corner(s, StyleProperty.CornerBottomLeft, shorter), half));
        }

        private static float Corner(ComputedStyle s, int property, float reference)
        {
            int unit = s.units[property];
            if (unit == (int)LengthUnit.Pixels) return s.numbers[property];
            if (unit == (int)LengthUnit.Percent) return s.numbers[property] * 0.01f * reference;
            return 0f;
        }

        private static bool HasStateStyles(Element e)
        {
            return e.stateRuleMask != 0 || e.hoverStyle != null || e.activeStyle != null || e.focusStyle != null
                || e.disabledStyle != null || e.draggingStyle != null || e.dragOverStyle != null
                || e.enterFromStyle != null || e.exitToStyle != null
                || e.computed.TransitionOf(StyleProperty.Background) != null
                || e.computed.TransitionOf(StyleProperty.BorderColor) != null;
        }

        // ================================================================
        // Content primitives
        // ================================================================

        internal void Glyphs(Element e, ShapedText text, int originX, int originY, Color color, float opacity)
        {
            Glyphs(e, text, originX, originY, color, opacity, e.clipLeft, e.clipTop, e.clipRight, e.clipBottom);
        }

        /// Glyphs clipped to a rectangle of the element's own (already within
        /// its clip) — a text box clips its text to its content box.
        internal void Glyphs(Element e, ShapedText text, int originX, int originY, Color color, float opacity,
            int clipLeft, int clipTop, int clipRight, int clipBottom)
        {
            if (!e.computed.IsVisible)
            {
                return;
            }
            Color linear = Premultiplied(color, opacity);
            if (linear.a <= 0f && !keepTransparent)
            {
                return;
            }
            float[] glyphs = text.glyphs;
            for (int g = 0; g < text.glyphCount; g++)
            {
                int o = g * ShapedText.FloatsPerGlyph;
                float left = originX + glyphs[o];
                float top = originY + glyphs[o + 1];
                int record = Begin(left, top, glyphs[o + 2] - glyphs[o], glyphs[o + 3] - glyphs[o + 1],
                    clipLeft, clipTop, clipRight, clipBottom);
                if (record < 0) continue;
                renderer.Texel(record, 2, linear);
                renderer.Texel(record, 3, glyphs[o + 4], glyphs[o + 5], glyphs[o + 6], glyphs[o + 7]);
                renderer.Texel(record, 4, KindGlyph + GroupStride * e.group, glyphs[o + 8], 0f, 0f);
            }
        }

        // ================================================================
        // Graphics (see Graphic): ripples, shapes, arcs, lines. Each is one
        // record, drawn even when transparent (a steady record count).
        // Coordinates are physical px in layout space.
        // ================================================================

        /// A circle clipped to a rounded box (`box` x, y, width, height and
        /// its `radii`), with a soft edge of `soft` px.
        internal void Ripple(Element e, float centerX, float centerY, float radius, float soft,
            Vector4 box, Vector4 radii, Color color, float opacity)
        {
            int record = Begin(e, box.x, box.y, box.z, box.w);
            if (record < 0) return;
            renderer.Texel(record, 2, Premultiplied(color, opacity));
            renderer.Texel(record, 3, radii.x, radii.y, radii.z, radii.w);
            renderer.Texel(record, 4, KindRipple + GroupStride * e.group, 0f, 0f, 0f);
            renderer.Texel(record, 6, centerX, centerY, Mathf.Max(0f, radius), soft);
            renderer.Texel(record, 7, box.x, box.y, box.z, box.w);
        }

        /// A shape of the morphing family filling the square centered in
        /// the rectangle: `from` morphed toward `to` by `morph` (0..1), the
        /// whole turned by `rotation` radians.
        internal void Shape(Element e, float x, float y, float width, float height, ShapeSpec from, ShapeSpec to,
            float morph, float rotation, Color color, float opacity)
        {
            int record = Begin(e, x - 1f, y - 1f, width + 2f, height + 2f);
            if (record < 0) return;
            renderer.Texel(record, 2, Premultiplied(color, opacity));
            renderer.Texel(record, 3, from.kind, from.count, from.amount, from.rotation * Mathf.Deg2Rad);
            renderer.Texel(record, 4, KindShape + GroupStride * e.group, 0f, 0f, 0f);
            // A spring may overshoot a little past either shape.
            renderer.Texel(record, 5, Mathf.Clamp(morph, -0.3f, 1.3f), rotation, 0f, 0f);
            renderer.Texel(record, 6, to.kind, to.count, to.amount, to.rotation * Mathf.Deg2Rad);
            renderer.Texel(record, 7, x, y, width, height);
        }

        /// A stroked arc with round caps (angles in radians from the top,
        /// clockwise), optionally wavy.
        internal void Arc(Element e, float centerX, float centerY, float radius, float thickness, float start, float sweep,
            float amplitude, float waves, float phase, Color color, float opacity)
        {
            float reach = radius + Mathf.Abs(amplitude) + thickness * 0.5f + 1f;
            int record = Begin(e, centerX - reach, centerY - reach, reach * 2f, reach * 2f);
            if (record < 0) return;
            renderer.Texel(record, 2, Premultiplied(color, opacity));
            renderer.Texel(record, 3, amplitude, waves, phase, 0f);
            renderer.Texel(record, 4, KindArc + GroupStride * e.group, 0f, 0f, 0f);
            renderer.Texel(record, 6, radius, thickness, start, sweep);
            renderer.Texel(record, 7, centerX - radius, centerY - radius, radius * 2f, radius * 2f);
        }

        /// A stroked horizontal line from x0 to x1 with round caps,
        /// optionally wavy (amplitude, wavelength, phase in radians).
        internal void Line(Element e, float x0, float x1, float y, float thickness,
            float amplitude, float wavelength, float phase, Color color, float opacity)
        {
            float reachX = thickness * 0.5f + 1f;
            float reachY = Mathf.Abs(amplitude) + thickness * 0.5f + 1f;
            int record = Begin(e, x0 - reachX, y - reachY, (x1 - x0) + reachX * 2f, reachY * 2f);
            if (record < 0) return;
            renderer.Texel(record, 2, Premultiplied(color, opacity));
            renderer.Texel(record, 3, amplitude, wavelength, phase, 0f);
            renderer.Texel(record, 4, KindWave + GroupStride * e.group, 0f, 0f, 0f);
            renderer.Texel(record, 6, x0, x1, y, thickness);
        }

        /// Replays a Graphic's commands (logical px from its content box).
        internal void PaintGraphic(Graphic g, float opacity)
        {
            GraphicCanvas canvas = g.canvas;
            List<float> n = canvas.numbers;
            float scale = g.context.scale;
            float ox = g.contentX, oy = g.contentY;
            for (int i = 0; i < canvas.count; i++)
            {
                int o = i * GraphicCanvas.FloatsPerCommand;
                int kind = (int)n[o];
                Color color = canvas.colors[i];
                if (kind == GraphicCanvas.CommandRipple)
                {
                    Element clip = g.clipToParent && g.parent != null ? g.parent : g;
                    Vector4 box = new Vector4(clip.x, clip.y, clip.width, clip.height);
                    Ripple(g, ox + n[o + 1] * scale, oy + n[o + 2] * scale, n[o + 3] * scale, n[o + 4] * scale,
                        box, CornerRadii(clip), color, opacity);
                }
                else if (kind == GraphicCanvas.CommandShape)
                {
                    Shape(g, ox + n[o + 1] * scale, oy + n[o + 2] * scale, n[o + 3] * scale, n[o + 4] * scale,
                        canvas.shapes[i * 2], canvas.shapes[i * 2 + 1], n[o + 5], n[o + 6], color, opacity);
                }
                else if (kind == GraphicCanvas.CommandArc)
                {
                    Arc(g, ox + n[o + 1] * scale, oy + n[o + 2] * scale, n[o + 3] * scale, n[o + 4] * scale,
                        n[o + 5], n[o + 6], n[o + 7] * scale, n[o + 8], n[o + 9], color, opacity);
                }
                else if (kind == GraphicCanvas.CommandLine)
                {
                    Line(g, ox + n[o + 1] * scale, ox + n[o + 2] * scale, oy + n[o + 3] * scale, n[o + 4] * scale,
                        n[o + 5] * scale, n[o + 6] * scale, n[o + 7], color, opacity);
                }
                else
                {
                    float width = n[o + 3] * scale, height = n[o + 4] * scale;
                    float half = Mathf.Min(width, height) * 0.5f;
                    Box(g, new Vector4(ox + n[o + 1] * scale, oy + n[o + 2] * scale, width, height),
                        new Vector4(Mathf.Min(n[o + 5] * scale, half), Mathf.Min(n[o + 6] * scale, half),
                            Mathf.Min(n[o + 7] * scale, half), Mathf.Min(n[o + 8] * scale, half)), color, opacity);
                }
            }
        }

        /// A rounded rectangle (x, y, width, height, radii), no border.
        internal void Box(Element e, Vector4 box, Vector4 radii, Color color, float opacity)
        {
            int record = Begin(e, box.x, box.y, box.z, box.w);
            if (record < 0) return;
            renderer.Texel(record, 2, Premultiplied(color, opacity));
            renderer.Texel(record, 3, radii.x, radii.y, radii.z, radii.w);
            renderer.Texel(record, 4, KindRoundedRectangle + GroupStride * e.group, 0f, 0f, 0f);
            renderer.Texel(record, 7, box.x, box.y, box.z, box.w);
        }

        /// Starts a record with its quad and the element's clip; -1 when the
        /// quad is clipped away entirely or the buffer is full.
        private int Begin(Element e, float x, float y, float width, float height)
        {
            return Begin(x, y, width, height, e.clipLeft, e.clipTop, e.clipRight, e.clipBottom);
        }

        private int Begin(float x, float y, float width, float height, int clipLeft, int clipTop, int clipRight, int clipBottom)
        {
            if (x >= clipRight || y >= clipBottom || x + width <= clipLeft || y + height <= clipTop)
            {
                return -1;
            }
            int record = renderer.AllocateRecord();
            if (record < 0)
            {
                return -1;
            }
            renderer.Texel(record, 0, x, y, width, height);
            renderer.Texel(record, 1, clipLeft, clipTop, clipRight, clipBottom);
            return record;
        }

        /// Linear color with opacity applied (the shader premultiplies).
        private static Color Premultiplied(Color color, float opacity)
        {
            Color linear = color.linear;
            linear.a = color.a * opacity;
            return linear;
        }
    }
}
