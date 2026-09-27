// Sets a MenUI panel up the moment a MenUIApp component is added: the quad
// that shows the panel, the offscreen camera and quad pool that draw it, and
// one hidden TextMeshPro object per registered font that shapes its text.
// Udon cannot create GameObjects, so all of this has to exist in the scene
// beforehand — but the user never has to build it by hand.
//
// Everything it uses ships with the package (PackageAssets), and the fonts
// come from MenUI Settings (default: Noto Sans JP). Panels are set up again
// when the font registry changes, and repaired when a scene is opened with a
// panel from an older setup.
//
// MenUI > Set Up Selected Panel (or the component's context menu) rebuilds a
// panel by hand, for example after changing its buffer size.

using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MenUI.Editor
{
    [InitializeOnLoad]
    public static class PanelSetup
    {
        private const string RigName = "MenUI Rig";
        private const string DisplayName = "MenUI Display";
        private const int RenderLayer = 31;
        private const float MetersPerLogicalPixel = 0.0015f;
        private static readonly Vector3 RigOffset = new Vector3(0f, -1000f, 0f);

        static PanelSetup()
        {
            ObjectFactory.componentWasAdded += component =>
            {
                if (component is MenUIApp app)
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (app != null)
                        {
                            SetUp(app);
                        }
                    };
                }
            };
            EditorApplication.delayCall += RepairPanels;
            EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += RepairPanels;
        }

        [MenuItem("MenUI/Set Up Selected Panel")]
        private static void SetUpSelected()
        {
            bool any = false;
            foreach (GameObject selected in Selection.gameObjects)
            {
                foreach (MenUIApp app in selected.GetComponents<MenUIApp>())
                {
                    SetUp(app);
                    any = true;
                }
            }
            if (!any)
            {
                EditorUtility.DisplayDialog("MenUI", "Select a GameObject that has a MenUIApp component (your panel behaviour).", "OK");
            }
        }

        [MenuItem("CONTEXT/MenUIApp/Rebuild MenUI Panel Setup")]
        private static void SetUpFromContext(MenuCommand command)
        {
            SetUp((MenUIApp)command.context);
        }

        /// Sets up every panel in the open scenes (after the fonts changed).
        public static void SetUpAllPanels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            foreach (MenUIApp app in PanelsInOpenScenes())
            {
                SetUp(app);
            }
        }

        /// Panels set up by an older MenUI (or not at all) are set up again.
        private static void RepairPanels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            foreach (MenUIApp app in PanelsInOpenScenes())
            {
                if (app.shapingTexts == null || app.shapingTexts.Length == 0 || app.shapingTexts[0] == null
                    || app.renderCamera == null || app.quadPoolRenderer == null || app.displayRenderer == null)
                {
                    SetUp(app);
                }
            }
        }

        private static List<MenUIApp> PanelsInOpenScenes()
        {
            List<MenUIApp> panels = new List<MenUIApp>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                {
                    continue;
                }
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    panels.AddRange(root.GetComponentsInChildren<MenUIApp>(true));
                }
            }
            return panels;
        }

        public static void SetUp(MenUIApp app)
        {
            PackageAssets.WhenTmpReady(() =>
            {
                if (app != null)
                {
                    SetUpNow(app);
                }
            });
        }

        private static void SetUpNow(MenUIApp app)
        {
            List<string> families = new List<string>();
            List<TMP_FontAsset> fonts = new List<TMP_FontAsset>();
            MenUISettings.RegisteredFonts(families, fonts);
            if (fonts[0] == null)
            {
                return;   // the bundled font is missing; already reported
            }

            GameObject panel = app.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(panel, "Set up MenUI panel");
            RemoveChild(panel.transform, RigName);
            RemoveChild(panel.transform, DisplayName);

            Mesh poolMesh = PackageAssets.QuadPoolMesh();
            Material panelMaterial = PackageAssets.PanelMaterial();
            Material displayMaterial = PackageAssets.DisplayMaterial();

            // Offscreen rig, far below the panel.
            GameObject rig = new GameObject(RigName);
            Undo.RegisterCreatedObjectUndo(rig, "Set up MenUI panel");
            rig.transform.SetParent(panel.transform, false);
            rig.transform.position = panel.transform.position + RigOffset;
            rig.transform.rotation = Quaternion.identity;

            Camera camera = new GameObject("Render Camera").AddComponent<Camera>();
            camera.transform.SetParent(rig.transform, false);
            camera.orthographic = true;
            camera.orthographicSize = 1f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.cullingMask = 1 << RenderLayer;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.enabled = false;

            GameObject pool = new GameObject("Quad Pool");
            pool.layer = RenderLayer;
            pool.transform.SetParent(camera.transform, false);
            pool.transform.localPosition = new Vector3(0f, 0f, 1f);
            pool.AddComponent<MeshFilter>().sharedMesh = poolMesh;
            MeshRenderer poolRenderer = pool.AddComponent<MeshRenderer>();
            poolRenderer.sharedMaterial = panelMaterial;
            poolRenderer.shadowCastingMode = ShadowCastingMode.Off;
            poolRenderer.receiveShadows = false;
            poolRenderer.lightProbeUsage = LightProbeUsage.Off;
            poolRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            poolRenderer.enabled = false;

            // One shaping text per font; on the Default layer, so the render
            // camera ignores them.
            GameObject canvasObject = new GameObject("Shaping Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(rig.transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, 0f, 50f);
            canvasObject.transform.localScale = Vector3.one * 0.0001f;
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            int count = fonts.Count;
            TextMeshProUGUI[] texts = new TextMeshProUGUI[count];
            RectTransform[] transforms = new RectTransform[count];
            float[] ascenders = new float[count];
            float[] descenders = new float[count];
            float[] lineHeights = new float[count];
            float[] paddings = new float[count];
            for (int i = 0; i < count; i++)
            {
                GameObject textObject = new GameObject("Shaping Text (" + families[i] + ")", typeof(RectTransform), typeof(TextMeshProUGUI));
                textObject.transform.SetParent(canvasObject.transform, false);
                TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
                text.font = fonts[i];
                text.raycastTarget = false;
                text.text = string.Empty;
                RectTransform textTransform = textObject.GetComponent<RectTransform>();
                textTransform.pivot = new Vector2(0f, 1f);
                texts[i] = text;
                transforms[i] = textTransform;
                PackageAssets.FontMetrics(fonts[i], out ascenders[i], out descenders[i], out lineHeights[i], out paddings[i]);
            }

            // What the player sees.
            float widthMeters = app.bufferWidth / app.scale * MetersPerLogicalPixel;
            float heightMeters = app.bufferHeight / app.scale * MetersPerLogicalPixel;
            GameObject display = new GameObject(DisplayName);
            display.transform.SetParent(panel.transform, false);
            display.transform.localScale = new Vector3(widthMeters, heightMeters, 1f);
            display.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            MeshRenderer displayRenderer = display.AddComponent<MeshRenderer>();
            displayRenderer.sharedMaterial = displayMaterial;
            displayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            displayRenderer.receiveShadows = false;

            BoxCollider collider = panel.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = Undo.AddComponent<BoxCollider>(panel);
            }
            collider.center = Vector3.zero;
            collider.size = new Vector3(widthMeters, heightMeters, 0.02f);

            Undo.RecordObject(app, "Set up MenUI panel");
            app.renderCamera = camera;
            app.quadPoolRenderer = poolRenderer;
            app.displayRenderer = displayRenderer;
            app.recordCapacity = PackageAssets.RecordCapacity;
            app.fontFamilies = families.ToArray();
            app.shapingTexts = texts;
            app.shapingTextTransforms = transforms;
            app.fontAscenders = ascenders;
            app.fontDescenders = descenders;
            app.fontLineHeights = lineHeights;
            app.fontPaddings = paddings;
            EditorUtility.SetDirty(app);
            EditorSceneManager.MarkSceneDirty(panel.scene);

            Debug.Log("[MenUI] set up '" + panel.name + "' (" + app.bufferWidth + "x" + app.bufferHeight + " px, scale "
                + app.scale + ", fonts: " + string.Join(", ", families) + ").", panel);
        }

        private static void RemoveChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }
        }
    }
}
