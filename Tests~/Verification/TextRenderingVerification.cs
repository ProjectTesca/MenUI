// Verification of the text path MenUI intends to use:
//
//   1. a hidden TextMeshProUGUI shapes the text (ForceMeshUpdate), and its
//      dynamic font asset adds any missing glyph — Japanese included — to
//      its atlas at that moment;
//   2. the glyph quads are read back from textInfo.meshInfo (positions and
//      atlas UVs), which also gives the measured size of the text;
//   3. every glyph and a rounded rectangle behind them become records in a
//      float data texture;
//   4. one draw of a pre-built quad pool mesh, whose vertex shader reads
//      those records, renders everything into a RenderTexture via a manual
//      Camera.Render().
//
// Interact (or the T key on desktop) cycles through the samples. Timings of
// every stage are written to the console.

using MenSharp;
using TMPro;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MenUI.Verification
{
    public class TextRenderingVerification : MenSharpBehaviour
    {
        [Header("Shaping (hidden TextMeshProUGUI)")]
        public TextMeshProUGUI shapingText;
        public RectTransform shapingTextTransform;
        public Canvas shapingCanvas;
        [Tooltip("Disable the shaping canvas component to test whether ForceMeshUpdate still works without it.")]
        public bool disableShapingCanvas;

        [Header("Rendering")]
        public Camera renderCamera;
        public MeshRenderer quadPoolRenderer;
        [Tooltip("Must equal the quad count of the pool mesh.")]
        public int recordCapacity = 2048;

        [Header("Buffer and scale")]
        public int bufferWidth = 1024;
        public int bufferHeight = 512;
        public float scale = 1.5f;

        [Header("Text (logical pixels)")]
        public float fontSize = 24f;
        public float boxX = 16f;
        public float boxY = 16f;
        public float boxWidth = 600f;
        public float boxPadding = 12f;
        public float cornerRadius = 10f;
        public Color textColor = new Color(0.95f, 0.95f, 0.97f, 1f);
        public Color boxColor = new Color(0.16f, 0.18f, 0.24f, 1f);
        public Color borderColor = new Color(0.45f, 0.55f, 0.95f, 1f);
        public float borderWidth = 1f;

        [Header("Verification switches")]
        [Tooltip("Render only in the frame the text changed. Glyphs that are new to the dynamic atlas may then be missing until the next change, if TMP defers the atlas upload.")]
        public bool renderOnlyImmediately;

        public string[] samples;

        private const int TexelsPerRecord = 8;
        private const int FloatsPerRecord = TexelsPerRecord * 4;
        private const int MaxAtlasCount = 4;

        private const float KindRoundedRectangle = 1f;
        private const float KindGlyph = 2f;

        private Texture2D recordTexture;
        private float[] recordData;
        private Color[] recordPixels;
        private int recordCount;
        private Material quadPoolMaterial;
        private Texture[] atlases = new Texture[MaxAtlasCount];
        private int sampleIndex;
        private int pendingRenderFrames;

        private void Start()
        {
            if (samples == null || samples.Length == 0)
            {
                samples = new string[]
                {
                    "Hello, MenUI! The quick brown fox jumps over the lazy dog.",
                    "こんにちは、MenUI です。日本語の動的アトラス生成を検証しています。",
                    "吾輩は猫である。名前はまだ無い。どこで生れたかとんと見当がつかぬ。何でも薄暗いじめじめした所でニャーニャー泣いていた事だけは記憶している。",
                    "魑魅魍魎・薔薇・麒麟・蟷螂・鬱 — rarely used kanji stress the atlas.",
                    "Mixed 混在 text 123 「かぎ括弧」『二重』… ①②③ ￥＄％",
                };
            }

            if (shapingCanvas != null && disableShapingCanvas)
            {
                shapingCanvas.enabled = false;
            }

            quadPoolMaterial = quadPoolRenderer.sharedMaterial;
            quadPoolRenderer.enabled = false;
            renderCamera.enabled = false;

            recordData = new float[recordCapacity * FloatsPerRecord];
            recordPixels = new Color[recordCapacity * TexelsPerRecord];
            recordTexture = new Texture2D(TexelsPerRecord, recordCapacity, TextureFormat.RGBAFloat, false, true);
            recordTexture.filterMode = FilterMode.Point;
            recordTexture.wrapMode = TextureWrapMode.Clamp;

            quadPoolMaterial.SetTexture("_RecordTexture", recordTexture);
            quadPoolMaterial.SetVector("_RecordTextureSize", new Vector4(TexelsPerRecord, recordCapacity, 0f, 0f));
            quadPoolMaterial.SetVector("_TargetSize", new Vector4(bufferWidth, bufferHeight, 0f, 0f));

            Debug.Log($"[MenUI Verification] buffer {bufferWidth}x{bufferHeight}, scale {scale}, record capacity {recordCapacity}");
            ShowSample(0);
        }

        public override void Interact()
        {
            ShowSample((sampleIndex + 1) % samples.Length);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                ShowSample((sampleIndex + 1) % samples.Length);
            }

            if (pendingRenderFrames > 0)
            {
                pendingRenderFrames--;
                RenderPanel();
            }
        }

        private void ShowSample(int index)
        {
            sampleIndex = index;
            string text = samples[index];

            Stopwatch total = Stopwatch.StartNew();
            Stopwatch stage = Stopwatch.StartNew();

            // --- 1. shape with TextMeshPro, in physical pixels -------------------
            float physicalFontSize = fontSize * scale;
            float physicalPadding = Snap(boxPadding);
            float physicalTextWidth = Snap(boxWidth) - physicalPadding * 2f;

            shapingTextTransform.pivot = new Vector2(0f, 1f);
            shapingTextTransform.sizeDelta = new Vector2(physicalTextWidth, 100000f);
            shapingText.fontSize = physicalFontSize;
            shapingText.enableWordWrapping = true;
            shapingText.alignment = TextAlignmentOptions.TopLeft;
            shapingText.text = text;
            shapingText.ForceMeshUpdate(true, true);
            long shapeTicks = stage.ElapsedTicks;
            stage.Restart();

            // --- 2. read the glyph quads back ------------------------------------
            TMP_TextInfo info = shapingText.textInfo;
            TMP_MeshInfo[] meshes = info.meshInfo;
            int materialCount = info.materialCount;

            recordCount = 0;
            int backgroundRecord = recordCount++;   // filled in once the text is measured

            float originX = Snap(boxX) + physicalPadding;
            float originY = Snap(boxY) + physicalPadding;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            int glyphCount = 0;
            Color linearText = textColor.linear;

            for (int m = 0; m < materialCount && m < MaxAtlasCount; m++)
            {
                TMP_MeshInfo mesh = meshes[m];
                Vector3[] vertices = mesh.vertices;
                Vector2[] uvs = mesh.uvs0;
                int quadCount = mesh.vertexCount / 4;
                atlases[m] = mesh.material != null ? mesh.material.mainTexture : null;

                for (int q = 0; q < quadCount && recordCount < recordCapacity; q++)
                {
                    int v = q * 4;
                    // TMP vertex order per glyph: bottom-left, top-left, top-right, bottom-right.
                    Vector3 bottomLeft = vertices[v];
                    Vector3 topRight = vertices[v + 2];
                    float left = bottomLeft.x;
                    float right = topRight.x;
                    float top = -topRight.y;       // TMP's y points up; ours points down
                    float bottom = -bottomLeft.y;
                    if (right <= left || bottom <= top)
                    {
                        continue;                  // an invisible character
                    }

                    Vector2 uvBottomLeft = uvs[v];
                    Vector2 uvTopRight = uvs[v + 2];

                    int offset = recordCount * FloatsPerRecord;
                    WriteRectangle(offset, originX + left, originY + top, right - left, bottom - top);
                    WriteClipAll(offset);
                    WriteColor(offset + 8, linearText);
                    recordData[offset + 12] = uvBottomLeft.x;
                    recordData[offset + 13] = uvBottomLeft.y;
                    recordData[offset + 14] = uvTopRight.x;
                    recordData[offset + 15] = uvTopRight.y;
                    recordData[offset + 16] = KindGlyph;
                    recordData[offset + 17] = 0f;
                    recordData[offset + 18] = 0f;
                    recordData[offset + 19] = m;
                    recordCount++;
                    glyphCount++;

                    if (left < minX) minX = left;
                    if (top < minY) minY = top;
                    if (right > maxX) maxX = right;
                    if (bottom > maxY) maxY = bottom;
                }
            }
            long extractTicks = stage.ElapsedTicks;
            stage.Restart();

            // --- 3. the rounded rectangle behind the text, sized by the measurement
            float measuredWidth = glyphCount > 0 ? maxX - minX : 0f;
            float measuredHeight = glyphCount > 0 ? maxY : 0f;
            float boxHeight = Mathf.Ceil(measuredHeight) + physicalPadding * 2f;
            int backgroundOffset = backgroundRecord * FloatsPerRecord;
            WriteRectangle(backgroundOffset, Snap(boxX), Snap(boxY), Snap(boxWidth), boxHeight);
            WriteClipAll(backgroundOffset);
            WriteColor(backgroundOffset + 8, boxColor.linear);
            recordData[backgroundOffset + 16] = KindRoundedRectangle;
            recordData[backgroundOffset + 17] = Snap(cornerRadius);
            recordData[backgroundOffset + 18] = Snap(borderWidth);
            recordData[backgroundOffset + 19] = 0f;
            WriteColor(backgroundOffset + 20, borderColor.linear);

            // --- 4. upload ---------------------------------------------------------
            // SetPixelData<float> would take recordData as is, but MenSharp does
            // not bind that generic extern yet, so pack the used rows into Colors.
            int texelCount = recordCount * TexelsPerRecord;
            for (int t = 0; t < texelCount; t++)
            {
                int f = t * 4;
                recordPixels[t] = new Color(recordData[f], recordData[f + 1], recordData[f + 2], recordData[f + 3]);
            }
            recordTexture.SetPixels(recordPixels);
            recordTexture.Apply(false);
            for (int a = 0; a < MaxAtlasCount; a++)
            {
                if (atlases[a] != null)
                {
                    quadPoolMaterial.SetTexture("_Atlas" + a, atlases[a]);
                }
            }
            quadPoolMaterial.SetFloat("_RecordCount", recordCount);
            long uploadTicks = stage.ElapsedTicks;
            stage.Restart();

            // --- 5. render ---------------------------------------------------------
            RenderPanel();
            long renderTicks = stage.ElapsedTicks;
            pendingRenderFrames = renderOnlyImmediately ? 0 : 2;

            double tickToMilliseconds = 1000.0 / Stopwatch.Frequency;
            Debug.Log(
                $"[MenUI Verification] sample {index}: {text.Length} chars, {glyphCount} glyph quads, " +
                $"{info.lineCount} lines, {materialCount} atlas(es), measured {measuredWidth:0.0}x{measuredHeight:0.0} px\n" +
                $"  shape {shapeTicks * tickToMilliseconds:0.00} ms | extract {extractTicks * tickToMilliseconds:0.00} ms | " +
                $"upload {uploadTicks * tickToMilliseconds:0.00} ms | render {renderTicks * tickToMilliseconds:0.00} ms | " +
                $"total {total.ElapsedTicks * tickToMilliseconds:0.00} ms");
            if (materialCount > MaxAtlasCount)
            {
                Debug.LogWarning($"[MenUI Verification] {materialCount} atlases in use; only the first {MaxAtlasCount} are drawn.");
            }
        }

        private void RenderPanel()
        {
            quadPoolRenderer.enabled = true;
            renderCamera.Render();
            quadPoolRenderer.enabled = false;
        }

        private float Snap(float logical)
        {
            return Mathf.Round(logical * scale);
        }

        private void WriteRectangle(int offset, float x, float y, float width, float height)
        {
            recordData[offset] = x;
            recordData[offset + 1] = y;
            recordData[offset + 2] = width;
            recordData[offset + 3] = height;
        }

        private void WriteClipAll(int offset)
        {
            recordData[offset + 4] = 0f;
            recordData[offset + 5] = 0f;
            recordData[offset + 6] = bufferWidth;
            recordData[offset + 7] = bufferHeight;
        }

        private void WriteColor(int offset, Color color)
        {
            recordData[offset] = color.r;
            recordData[offset + 1] = color.g;
            recordData[offset + 2] = color.b;
            recordData[offset + 3] = color.a;
        }
    }
}
