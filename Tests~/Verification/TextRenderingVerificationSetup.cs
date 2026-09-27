// Builds the scene objects and assets the text rendering verification needs:
// MenUI > Verification > Create Text Rendering Setup.
//
// Select a TextMeshPro font asset first (a Dynamic one made from a Japanese
// font, e.g. Noto Sans JP, to verify runtime glyph generation); without a
// selection the TMP default font asset is used.

using System.IO;
using MenUI.Verification;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MenUI.Editor.Verification
{
    public static class TextRenderingVerificationSetup
    {
        private const string AssetFolder = "Assets/MenUIVerification";
        private const int RenderLayer = 31;
        private const int RecordCapacity = 2048;
        private const int BufferWidth = 1024;
        private const int BufferHeight = 512;
        private static readonly Vector3 RenderRigPosition = new Vector3(0f, -1000f, 0f);

        [MenuItem("MenUI/Verification/Create Text Rendering Setup")]
        public static void Create()
        {
            TMP_FontAsset font = Selection.activeObject as TMP_FontAsset;
            if (font == null && TMP_Settings.instance != null)
            {
                font = TMP_Settings.defaultFontAsset;
            }
            if (font == null)
            {
                EditorUtility.DisplayDialog("MenUI Verification",
                    "Select a TextMeshPro font asset in the Project window first " +
                    "(or import TMP Essential Resources so a default font exists).", "OK");
                return;
            }
            if (font.atlasPopulationMode != AtlasPopulationMode.Dynamic)
            {
                Debug.LogWarning($"[MenUI Verification] '{font.name}' is not a Dynamic font asset: characters missing from its atlas will not be generated at runtime.");
            }

            Directory.CreateDirectory(AssetFolder);
            Mesh poolMesh = CreateQuadPoolMesh();
            RenderTexture panelTexture = CreatePanelTexture();
            Material poolMaterial = CreateMaterial("QuadPool", "MenUI/Verification/QuadPool");
            Material displayMaterial = CreateMaterial("PanelDisplay", "MenUI/Verification/PanelDisplay");
            displayMaterial.mainTexture = panelTexture;
            EditorUtility.SetDirty(displayMaterial);
            AssetDatabase.SaveAssets();

            GameObject root = new GameObject("MenUI Text Rendering Verification");
            Undo.RegisterCreatedObjectUndo(root, "Create MenUI verification");

            // Offscreen render rig: far below the world, visible only to its own camera.
            GameObject rig = new GameObject("Render Rig");
            rig.transform.SetParent(root.transform, false);
            rig.transform.position = RenderRigPosition;

            Camera camera = new GameObject("Render Camera").AddComponent<Camera>();
            camera.transform.SetParent(rig.transform, false);
            camera.orthographic = true;
            camera.orthographicSize = 1f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.cullingMask = 1 << RenderLayer;
            camera.targetTexture = panelTexture;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.enabled = false;

            GameObject pool = new GameObject("Quad Pool");
            pool.layer = RenderLayer;
            pool.transform.SetParent(rig.transform, false);
            pool.transform.localPosition = new Vector3(0f, 0f, 1f);
            pool.AddComponent<MeshFilter>().sharedMesh = poolMesh;
            MeshRenderer poolRenderer = pool.AddComponent<MeshRenderer>();
            poolRenderer.sharedMaterial = poolMaterial;
            poolRenderer.shadowCastingMode = ShadowCastingMode.Off;
            poolRenderer.receiveShadows = false;
            poolRenderer.lightProbeUsage = LightProbeUsage.Off;
            poolRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            poolRenderer.enabled = false;

            // Hidden shaping text, on the Default layer so the render camera ignores it.
            GameObject canvasObject = new GameObject("Shaping Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(rig.transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, 0f, 50f);
            canvasObject.transform.localScale = Vector3.one * 0.0001f;
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            GameObject textObject = new GameObject("Shaping Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(canvasObject.transform, false);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.raycastTarget = false;
            text.text = string.Empty;
            RectTransform textTransform = textObject.GetComponent<RectTransform>();
            textTransform.pivot = new Vector2(0f, 1f);

            // The panel the player looks at.
            GameObject display = GameObject.CreatePrimitive(PrimitiveType.Quad);
            display.name = "Panel Display";
            display.transform.SetParent(root.transform, false);
            display.transform.localPosition = new Vector3(0f, 1.5f, 2f);
            display.transform.localScale = new Vector3(2f, 2f * BufferHeight / BufferWidth, 1f);
            Object.DestroyImmediate(display.GetComponent<MeshCollider>());
            BoxCollider collider = display.AddComponent<BoxCollider>();
            collider.size = new Vector3(1f, 1f, 0.02f);
            MeshRenderer displayRenderer = display.GetComponent<MeshRenderer>();
            displayRenderer.sharedMaterial = displayMaterial;
            displayRenderer.shadowCastingMode = ShadowCastingMode.Off;

            TextRenderingVerification verification = display.AddComponent<TextRenderingVerification>();
            verification.shapingText = text;
            verification.shapingTextTransform = textTransform;
            verification.shapingCanvas = canvas;
            verification.renderCamera = camera;
            verification.quadPoolRenderer = poolRenderer;
            verification.recordCapacity = RecordCapacity;
            verification.bufferWidth = BufferWidth;
            verification.bufferHeight = BufferHeight;
            EditorUtility.SetDirty(verification);

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = display;
            Debug.Log($"[MenUI Verification] created with font '{font.name}'. Enter play mode (or Build & Test), then Interact with the panel or press T.");
        }

        private static Mesh CreateQuadPoolMesh()
        {
            var vertices = new Vector3[RecordCapacity * 4];
            var corners = new Vector2[RecordCapacity * 4];
            var indices = new Vector2[RecordCapacity * 4];
            var triangles = new int[RecordCapacity * 6];
            for (int quad = 0; quad < RecordCapacity; quad++)
            {
                int v = quad * 4;
                corners[v] = new Vector2(0f, 0f);
                corners[v + 1] = new Vector2(1f, 0f);
                corners[v + 2] = new Vector2(1f, 1f);
                corners[v + 3] = new Vector2(0f, 1f);
                for (int k = 0; k < 4; k++)
                {
                    indices[v + k] = new Vector2(quad, 0f);
                }
                int t = quad * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = "MenUI Quad Pool" };
            mesh.vertices = vertices;
            mesh.uv = corners;
            mesh.uv2 = indices;
            mesh.triangles = triangles;
            // Positions are computed in the vertex shader; the bounds only have to
            // keep the render camera from culling the pool.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            return ReplaceAsset(mesh, $"{AssetFolder}/QuadPool.asset");
        }

        private static RenderTexture CreatePanelTexture()
        {
            var texture = new RenderTexture(BufferWidth, BufferHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "MenUI Panel",
                useMipMap = false,
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
            };
            return ReplaceAsset(texture, $"{AssetFolder}/Panel.renderTexture");
        }

        private static Material CreateMaterial(string name, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new System.InvalidOperationException($"Shader '{shaderName}' was not found.");
            }
            return ReplaceAsset(new Material(shader) { name = name }, $"{AssetFolder}/{name}.mat");
        }

        private static T ReplaceAsset<T>(T asset, string path) where T : Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                Object.DestroyImmediate(asset);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
