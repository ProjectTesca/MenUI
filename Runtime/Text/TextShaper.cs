// Text shaping through a hidden TextMeshProUGUI: TMP lays the glyphs out (and
// adds missing glyphs to its dynamic atlas), MenUI reads the quads back and
// draws them itself. Everything is in physical pixels, y pointing down.
//
// One shaper per font registered in MenUI Settings (a TMP font asset cannot
// be held by Udon, so each font gets a TMP component of its own). Atlas
// textures — of every font and its fallbacks — share the panel's atlas
// slots (MenUIContext.AtlasSlot).

using TMPro;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MenUI
{
    /// The glyph quads of one shaped string.
    public sealed class ShapedText
    {
        internal const int FloatsPerGlyph = 9;   // left, top, right, bottom, u0, v0, u1, v1, atlas

        internal string text;
        internal float fontSize;
        internal int maxWidth;                   // -1: no wrapping
        internal TextAlignMode align;
        internal TextShaper shaper;              // which font shaped it

        internal float[] glyphs = new float[0];
        internal int glyphCount;
        internal int width;
        internal int height;
        internal int lineCount;

        internal bool Matches(TextShaper shaper, string text, float fontSize, int maxWidth, TextAlignMode align)
        {
            return this.shaper == shaper && this.text == text && this.fontSize == fontSize
                && this.maxWidth == maxWidth && this.align == align;
        }
    }

    public sealed class TextShaper
    {
        private const float UnboundedWidth = 100000f;
        private const int MaxMaterials = 8;

        private readonly TextMeshProUGUI tmp;
        private readonly RectTransform tmpTransform;
        private readonly float ascender;     // per unit font size
        private readonly float descender;    // negative, per unit font size
        private readonly float lineHeight;   // per unit font size
        private readonly float padding;      // quad padding per unit font size

        internal MenUIContext context;

        internal TextShaper(TextMeshProUGUI tmp, RectTransform tmpTransform,
            float ascender, float descender, float lineHeight, float padding)
        {
            this.tmp = tmp;
            this.tmpTransform = tmpTransform;
            this.ascender = ascender;
            this.descender = descender;
            this.lineHeight = lineHeight;
            this.padding = padding;
            tmpTransform.pivot = new Vector2(0f, 1f);
            tmp.enableWordWrapping = true;
        }

        /// Shapes into `result` unless it already holds this exact shaping.
        internal void Shape(ShapedText result, string text, float fontSize, int maxWidth, TextAlignMode align)
        {
            if (result.Matches(this, text, fontSize, maxWidth, align))
            {
                return;
            }
            result.shaper = this;
            result.text = text;
            result.fontSize = fontSize;
            result.maxWidth = maxWidth;
            result.align = align;
            result.glyphCount = 0;
            result.width = 0;
            result.height = 0;
            result.lineCount = 0;
            if (string.IsNullOrEmpty(text) || fontSize <= 0f)
            {
                return;
            }

            tmpTransform.sizeDelta = new Vector2(maxWidth >= 0 ? maxWidth : UnboundedWidth, UnboundedWidth);
            tmp.enableWordWrapping = maxWidth >= 0;
            tmp.fontSize = fontSize;
            tmp.alignment = align == TextAlignMode.Center ? TextAlignmentOptions.Top
                : align == TextAlignMode.End ? TextAlignmentOptions.TopRight
                : TextAlignmentOptions.TopLeft;
            long start = Stopwatch.GetTimestamp();
            tmp.text = text;
            tmp.ForceMeshUpdate(true, true);
            context.shapeTicks += Stopwatch.GetTimestamp() - start;
            context.shapeCount++;
            context.shapedThisFrame = true;

            TMP_TextInfo info = tmp.textInfo;
            TMP_MeshInfo[] meshes = info.meshInfo;
            int materialCount = info.materialCount;

            int total = 0;
            for (int m = 0; m < materialCount && m < MaxMaterials; m++)
            {
                total += meshes[m].vertexCount / 4;
            }
            if (result.glyphs.Length < total * ShapedText.FloatsPerGlyph)
            {
                result.glyphs = new float[total * ShapedText.FloatsPerGlyph];
            }

            float pad = padding * fontSize;
            float right = 0f;
            int count = 0;
            float[] glyphs = result.glyphs;
            for (int m = 0; m < materialCount && m < MaxMaterials; m++)
            {
                TMP_MeshInfo mesh = meshes[m];
                Material material = mesh.material;
                int slot = material != null ? context.AtlasSlot(material.mainTexture) : 0;
                Vector3[] vertices = mesh.vertices;
                Vector2[] uvs = mesh.uvs0;
                int quads = mesh.vertexCount / 4;
                for (int q = 0; q < quads; q++)
                {
                    int v = q * 4;
                    // TMP order: bottom-left, top-left, top-right, bottom-right; y up.
                    Vector3 bottomLeft = vertices[v];
                    Vector3 topRight = vertices[v + 2];
                    if (topRight.x <= bottomLeft.x || topRight.y <= bottomLeft.y)
                    {
                        continue;
                    }
                    Vector2 uvBottomLeft = uvs[v];
                    Vector2 uvTopRight = uvs[v + 2];
                    int o = count * ShapedText.FloatsPerGlyph;
                    glyphs[o] = bottomLeft.x;
                    glyphs[o + 1] = -topRight.y;
                    glyphs[o + 2] = topRight.x;
                    glyphs[o + 3] = -bottomLeft.y;
                    glyphs[o + 4] = uvBottomLeft.x;
                    glyphs[o + 5] = uvBottomLeft.y;
                    glyphs[o + 6] = uvTopRight.x;
                    glyphs[o + 7] = uvTopRight.y;
                    glyphs[o + 8] = slot;
                    if (topRight.x - pad > right)
                    {
                        right = topRight.x - pad;
                    }
                    count++;
                }
            }

            int lines = info.lineCount;
            if (lines < 1)
            {
                lines = 1;
            }
            result.glyphCount = count;
            result.lineCount = lines;
            // Rounded up to a quarter of the font size: glyph ink differs a
            // little between strings of the same length ("1234" / "1238"),
            // and a steady width lets a changed text keep its box (and skip
            // the parent's layout — see Element.ContentChanged).
            float step = Mathf.Max(2f, Mathf.Round(fontSize * 0.25f));
            result.width = Mathf.CeilToInt(Mathf.Ceil(right / step) * step);
            result.height = Mathf.CeilToInt(((ascender - descender) + (lines - 1) * lineHeight) * fontSize);
        }
    }
}
