// The GPU side of a panel: draw records live in an RGBAFloat texture (one
// row of 8 texels per record), and one draw of a pre-built quad pool mesh
// turns them into the panel's RenderTexture.
//
// Record layout (texel: contents):
//   0: quad x, y, width, height            (physical px, y down)
//   1: clip left, top, right, bottom
//   2: color (linear, straight alpha)
//   3: corner radii tl, tr, br, bl  |  glyph atlas uv: left, bottom, right, top
//   4: kind, atlas index, shadow sigma, shadow spread
//   5: border color
//   6: border widths t, r, b, l     |  shadows: the element's rectangle
//   7: shape rectangle x, y, width, height

using UnityEngine;

namespace MenUI
{
    public sealed class PanelRenderer
    {
        internal const int TexelsPerRecord = 8;

        private readonly Camera camera;
        private readonly Renderer quadPool;
        private readonly Material material;
        private readonly Texture2D recordTexture;
        private readonly Color[] pixels;
        private readonly int capacity;
        private int count;
        private bool warnedFull;
        private bool recordsChanged = true;
        private int uploadedCount = -1;

        // Overwrite mode: records are rewritten in place in [cursor, end).
        private bool overwriting;
        private int overwriteCursor;
        private int overwriteEnd;
        private bool overwriteOverflowed;

        // The panel is drawn into the top-left `width` x `height` of the
        // target, which can be larger: a live resize within it only changes
        // what the display shows, instead of making a new texture each frame.
        internal RenderTexture target;
        private readonly Material displayMaterial;
        private int width, height;

        internal PanelRenderer(Camera camera, Renderer quadPool, Renderer display, int capacity, int width, int height)
        {
            this.camera = camera;
            this.quadPool = quadPool;
            this.capacity = capacity;
            camera.enabled = false;
            quadPool.enabled = false;

            pixels = new Color[capacity * TexelsPerRecord];
            recordTexture = new Texture2D(TexelsPerRecord, capacity, TextureFormat.RGBAFloat, false, true);
            recordTexture.filterMode = FilterMode.Point;
            recordTexture.wrapMode = TextureWrapMode.Clamp;

            material = quadPool.material;   // an instance of its own
            material.SetTexture("_RecordTexture", recordTexture);
            material.SetVector("_RecordTextureSize", new Vector4(TexelsPerRecord, capacity, 0f, 0f));

            displayMaterial = display.material;
            Resize(width, height);
        }

        /// The panel's size in physical pixels. The texture is replaced only
        /// when it is too small (with room to spare, for a growing panel).
        internal void Resize(int newWidth, int newHeight)
        {
            width = newWidth;
            height = newHeight;
            if (target == null || target.width < width || target.height < height)
            {
                bool first = target == null;
                int textureWidth = first ? width : Mathf.Max(width, Mathf.CeilToInt(target.width * 1.25f));
                int textureHeight = first ? height : Mathf.Max(height, Mathf.CeilToInt(target.height * 1.25f));
                if (!first)
                {
                    textureWidth = Mathf.Max(textureWidth, Mathf.CeilToInt(width * 1.25f));
                    textureHeight = Mathf.Max(textureHeight, Mathf.CeilToInt(height * 1.25f));
                    target.Release();
                }
                target = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                camera.targetTexture = target;
                displayMaterial.SetTexture("_MainTex", target);
                material.SetVector("_TargetSize", new Vector4(textureWidth, textureHeight, 0f, 0f));
            }
            // Texture v runs bottom-up and the panel sits at the top.
            float usedWidth = (float)width / target.width;
            float usedHeight = (float)height / target.height;
            displayMaterial.SetVector("_MainTex_ST", new Vector4(usedWidth, usedHeight, 0f, 1f - usedHeight));
        }

        internal void SetGroups(Vector4[] offsets, Vector4[] clips)
        {
            material.SetVectorArray("_GroupOffsets", offsets);
            material.SetVectorArray("_GroupClips", clips);
        }

        internal void SetAtlas(int index, Texture atlas)
        {
            if (atlas != null)
            {
                material.SetTexture("_Atlas" + index, atlas);
            }
        }

        internal int RecordCount { get { return count; } }

        internal void BeginRecords()
        {
            count = 0;
        }

        /// Rewrites `length` existing records from `start` instead of
        /// appending. EndOverwrite says whether exactly that many were written.
        internal void BeginOverwrite(int start, int length)
        {
            overwriting = true;
            overwriteCursor = start;
            overwriteEnd = start + length;
            overwriteOverflowed = false;
        }

        internal bool EndOverwrite()
        {
            overwriting = false;
            return !overwriteOverflowed && overwriteCursor == overwriteEnd;
        }

        internal int AllocateRecord()
        {
            if (overwriting)
            {
                if (overwriteCursor >= overwriteEnd)
                {
                    overwriteOverflowed = true;
                    return -1;
                }
                return ClearRecord(overwriteCursor++);
            }
            if (count >= capacity)
            {
                if (!warnedFull)
                {
                    warnedFull = true;
                    Debug.LogWarning("[MenUI] the panel needs more than " + capacity + " draw records; the rest are not drawn.");
                }
                return -1;
            }
            return ClearRecord(count++);
        }

        /// Clears the texels a primitive may leave unwritten.
        private int ClearRecord(int record)
        {
            int b = record * TexelsPerRecord;
            Color zero = new Color(0f, 0f, 0f, 0f);
            pixels[b + 5] = zero;
            pixels[b + 6] = zero;
            pixels[b + 7] = zero;
            return record;
        }

        internal void Texel(int record, int texel, float r, float g, float b, float a)
        {
            pixels[record * TexelsPerRecord + texel] = new Color(r, g, b, a);
            recordsChanged = true;
        }

        internal void Texel(int record, int texel, Color color)
        {
            pixels[record * TexelsPerRecord + texel] = color;
            recordsChanged = true;
        }

        internal void Render()
        {
            // Upload only when a record changed: a frame that just redraws
            // (new glyphs reaching the atlas, later: scrolling) skips it.
            if (recordsChanged)
            {
                recordsChanged = false;
                recordTexture.SetPixels(pixels);
                recordTexture.Apply(false);
            }
            if (count != uploadedCount)
            {
                uploadedCount = count;
                material.SetFloat("_RecordCount", count);
            }
            quadPool.enabled = true;
            camera.Render();
            quadPool.enabled = false;
        }
    }
}
