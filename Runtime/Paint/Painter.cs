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
// translucent element show through each other.

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
        /// The record's kind texel holds kind + GroupStride * transform group.
        internal const float GroupStride = 8f;

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
                float opacity = AncestorOpacity(e) * e.computed.Opacity;
                renderer.BeginOverwrite(e.boxRecordStart, e.boxRecordCount);
                if (opacity > 0f)
                {
                    PaintBox(e, opacity);
                }
                if (!renderer.EndOverwrite())
                {
                    return false;
                }
                renderer.BeginOverwrite(e.contentRecordStart, e.contentRecordCount);
                if (opacity > 0f)
                {
                    e.PaintContent(this, opacity);
                }
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
            if (root.computed.Display != DisplayMode.None)
            {
                PaintContext(root, root.computed.Opacity);
            }
            if (overlayRoot.computed.Display != DisplayMode.None)
            {
                PaintContext(overlayRoot, overlayRoot.computed.Opacity);
            }
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
            if (opacity <= 0f)
            {
                return;
            }
            PaintOwnBox(context, opacity);

            List<Element> contexts = new List<Element>();
            CollectContexts(context, contexts);
            SortByZIndex(contexts);

            int index = 0;
            for (; index < contexts.Count && contexts[index].computed.ZIndex < 0; index++)
            {
                Element child = contexts[index];
                PaintContext(child, AncestorOpacity(child) * child.computed.Opacity);
            }
            PaintOwnContent(context, opacity);
            PaintFlowChildren(context, opacity);
            for (; index < contexts.Count; index++)
            {
                Element child = contexts[index];
                PaintContext(child, AncestorOpacity(child) * child.computed.Opacity);
            }
        }

        private void PaintFlow(Element e, float opacity)
        {
            if (opacity <= 0f)
            {
                return;
            }
            PaintOwnBox(e, opacity);
            PaintOwnContent(e, opacity);
            PaintFlowChildren(e, opacity);
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
                PaintFlow(child, opacity * child.computed.Opacity);
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

        private static float AncestorOpacity(Element e)
        {
            float opacity = 1f;
            for (Element a = e.parent; a != null; a = a.parent)
            {
                opacity *= a.computed.Opacity;
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
            float half = Mathf.Min(width, height) * 0.5f;
            float radiusTopLeft = Mathf.Min(s.Resolve(StyleProperty.CornerTopLeft, (int)Mathf.Min(width, height), 0), half);
            float radiusTopRight = Mathf.Min(s.Resolve(StyleProperty.CornerTopRight, (int)Mathf.Min(width, height), 0), half);
            float radiusBottomRight = Mathf.Min(s.Resolve(StyleProperty.CornerBottomRight, (int)Mathf.Min(width, height), 0), half);
            float radiusBottomLeft = Mathf.Min(s.Resolve(StyleProperty.CornerBottomLeft, (int)Mathf.Min(width, height), 0), half);

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

            if (background.a > 0f || (hasBorder && !hasInset))
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
            if (linear.a <= 0f)
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
