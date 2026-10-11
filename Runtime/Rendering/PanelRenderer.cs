// The GPU side of a panel: draw records live in an RGBAFloat texture (one
// row of 8 texels per record), and one draw of a pre-built quad pool mesh
// turns them into the panel's RenderTexture.
//
// Record layout (texel: contents):
//   0: quad x, y, width, height            (physical px, y down)
//   1: clip left, top, right, bottom
//   2: color (linear, straight alpha)
//   3: corner radii tl, tr, br, bl  |  glyph / image uv: left, bottom, right, top
//   4: kind, atlas index (image: page), shadow sigma, shadow spread
//   5: border color                  |  image: corner radii
//   6: border widths t, r, b, l     |  shadows: the element's rectangle
//   7: shape rectangle x, y, width, height

using UnityEngine;

namespace MenUI
{
    public sealed class PanelRenderer
    {
        internal const int TexelsPerRecord = RecordBuffer.TexelsPerRecord;

        private readonly Camera camera;
        private readonly Renderer quadPool;
        private readonly Material material;
        private readonly Texture2D recordTexture;
        /// The records the painter writes (uploaded when changed).
        internal readonly RecordBuffer records;
        private int uploadedCount = -1;

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
            records = new RecordBuffer(capacity);
            camera.enabled = false;
            quadPool.enabled = false;

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
                    Object.Destroy(target);
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

        /// Frees the GPU memory the panel holds: its RenderTexture, the
        /// record texture and its copies of the two materials. Nothing can be
        /// drawn afterwards; the display must not be shown any more either
        /// (its material is gone).
        internal void Release()
        {
            camera.targetTexture = null;
            if (target != null)
            {
                target.Release();
                Object.Destroy(target);
                target = null;
            }
            Object.Destroy(recordTexture);
            Object.Destroy(material);
            Object.Destroy(displayMaterial);
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

        /// An image atlas page (null: none) for the shader's slot `index`.
        internal void SetImagePage(int index, Texture page)
        {
            material.SetTexture("_Image" + index, page);
        }

        internal int RecordCount { get { return records.RecordCount; } }

        internal void Render()
        {
            // Upload only when a record changed: a frame that just redraws
            // (new glyphs reaching the atlas, later: scrolling) skips it.
            if (records.changed)
            {
                records.changed = false;
                recordTexture.SetPixels(records.pixels);
                recordTexture.Apply(false);
            }
            int count = records.RecordCount;
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
