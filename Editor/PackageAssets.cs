// Assets MenUI ships in its package, so a project that installs it needs
// nothing generated of its own: the quad pool mesh, the panel materials, the
// default font (Noto Sans JP, as a Dynamic TMP font asset) and the icon fonts
// (Material Symbols Rounded, outlined and filled).
//
// Each is created in the package on first use when missing — which only
// happens while developing MenUI itself; the result is committed with the
// package, so every other project finds it (with a stable GUID).
//
// TextMesh Pro's Essential Resources (its shaders and settings, which font
// assets need) are imported automatically when missing.

using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MenUI.Editor
{
    public static class PackageAssets
    {
        public const string PackageRoot = "Packages/io.tesca.menui";
        private const string RenderingFolder = PackageRoot + "/Runtime/Rendering/Assets";
        private const string FontsFolder = PackageRoot + "/Runtime/Fonts";
        public const string DefaultFontFamily = "Noto Sans JP";
        private const string DefaultFontFile = FontsFolder + "/NotoSansJP-Regular.ttf";
        private const string DefaultFontAssetPath = FontsFolder + "/NotoSansJP-Regular SDF.asset";
        /// The icon fonts every panel has (after the default font): Material
        /// Symbols Rounded, outlined and filled, used by code point.
        public const string IconFontFamily = "Material Symbols Rounded";
        public const string FilledIconFontFamily = "Material Symbols Rounded Filled";
        private const string IconFontFile = FontsFolder + "/MaterialSymbolsRounded.ttf";
        private const string FilledIconFontFile = FontsFolder + "/MaterialSymbolsRounded-Filled.ttf";

        public const int RecordCapacity = 4096;

        // Settings for Dynamic font assets MenUI creates (see the text
        // rendering verification: 48pt sampling keeps SDF edges clean at UI
        // sizes, and a 2048 atlas holds about 1,200 CJK glyphs per page).
        private const int SamplingPointSize = 48;
        private const int AtlasPadding = 5;
        private const int AtlasSize = 2048;

        private const string EssentialsShader = "TextMeshPro/Mobile/Distance Field";
        private const string EssentialsPackage = "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage";
        private static bool importingEssentials;

        // ---- TextMesh Pro essentials -------------------------------------------------

        public static bool TmpReady
        {
            get { return Shader.Find(EssentialsShader) != null && TMP_Settings.instance != null; }
        }

        /// Runs `action` once TMP's Essential Resources are in the project,
        /// importing them first if needed.
        public static void WhenTmpReady(Action action)
        {
            if (TmpReady)
            {
                action();
                return;
            }
            string path = Path.GetFullPath(EssentialsPackage);
            if (!File.Exists(path))
            {
                Debug.LogError("[MenUI] TextMesh Pro's Essential Resources were not found at " + EssentialsPackage
                    + "; import them with Window > TextMeshPro > Import TMP Essential Resources.");
                return;
            }
            AssetDatabase.ImportPackageCallback completed = null;
            completed = name =>
            {
                AssetDatabase.importPackageCompleted -= completed;
                importingEssentials = false;
                EditorApplication.delayCall += () =>
                {
                    if (TmpReady) action();
                    else Debug.LogError("[MenUI] TextMesh Pro's Essential Resources did not import correctly.");
                };
            };
            AssetDatabase.importPackageCompleted += completed;
            if (!importingEssentials)
            {
                importingEssentials = true;
                Debug.Log("[MenUI] importing TextMesh Pro's Essential Resources (needed for text).");
                AssetDatabase.ImportPackage(path, false);
            }
        }

        // ---- rendering ----------------------------------------------------------------

        public static Mesh QuadPoolMesh()
        {
            string path = RenderingFolder + "/QuadPool" + RecordCapacity + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                return existing;
            }
            EnsureFolder(RenderingFolder);
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
            var mesh = new Mesh { name = "MenUI Quad Pool " + RecordCapacity };
            mesh.vertices = vertices;
            mesh.uv = corners;
            mesh.uv2 = indices;
            mesh.triangles = triangles;
            // Positions come from the vertex shader; the bounds only keep the
            // render camera from culling the pool.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        public static Material PanelMaterial() { return LoadOrCreateMaterial("Panel", "MenUI/Panel"); }
        public static Material DisplayMaterial() { return LoadOrCreateMaterial("PanelDisplay", "MenUI/PanelDisplay"); }
        public static Material ImageCopyMaterial() { return LoadOrCreateMaterial("ImageCopy", "MenUI/ImageCopy"); }

        private static Material LoadOrCreateMaterial(string name, string shaderName)
        {
            string path = RenderingFolder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException("Shader '" + shaderName + "' was not found.");
            }
            EnsureFolder(RenderingFolder);
            var material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ---- fonts --------------------------------------------------------------------------

        /// The default font: Noto Sans JP Regular, shipped with the package.
        /// Needs TMP to be ready (see WhenTmpReady).
        public static TMP_FontAsset DefaultFontAsset()
        {
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultFontAssetPath);
            if (existing != null)
            {
                return existing;
            }
            Font font = AssetDatabase.LoadAssetAtPath<Font>(DefaultFontFile);
            if (font == null)
            {
                Debug.LogError("[MenUI] the bundled font is missing: " + DefaultFontFile);
                return null;
            }
            return CreateDynamicFontAsset(font, DefaultFontAssetPath);
        }

        /// The bundled icon fonts' families and font assets (made on first use).
        public static void IconFonts(System.Collections.Generic.List<string> families, System.Collections.Generic.List<TMP_FontAsset> assets)
        {
            AddBundled(IconFontFamily, IconFontFile, families, assets);
            AddBundled(FilledIconFontFamily, FilledIconFontFile, families, assets);
        }

        private static void AddBundled(string family, string file, System.Collections.Generic.List<string> families,
            System.Collections.Generic.List<TMP_FontAsset> assets)
        {
            string assetPath = Path.ChangeExtension(file, null) + " SDF.asset";
            TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (asset == null)
            {
                Font font = AssetDatabase.LoadAssetAtPath<Font>(file);
                if (font == null)
                {
                    Debug.LogError("[MenUI] the bundled icon font is missing: " + file);
                    return;
                }
                asset = CreateDynamicFontAsset(font, assetPath);
            }
            if (asset != null)
            {
                families.Add(family);
                assets.Add(asset);
            }
        }

        /// The bundled font file itself (for uGUI text).
        public static Font DefaultFont()
        {
            return AssetDatabase.LoadAssetAtPath<Font>(DefaultFontFile);
        }

        public static bool IsDefaultFontAsset(TMP_FontAsset asset)
        {
            return asset != null && AssetDatabase.GetAssetPath(asset) == DefaultFontAssetPath;
        }

        /// A Dynamic TMP font asset for `font` with MenUI's settings, saved at
        /// `path` with its atlas and material as sub-assets (as TMP's own
        /// "Create > TextMeshPro > Font Asset" does).
        public static TMP_FontAsset CreateDynamicFontAsset(Font font, string path)
        {
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding,
                GlyphRenderMode.SDFAA, AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, true);
            if (asset == null)
            {
                Debug.LogError("[MenUI] could not create a font asset for " + font.name + ".");
                return null;
            }
            string name = Path.GetFileNameWithoutExtension(path);
            asset.name = name;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateAsset(asset, path);
            asset.atlasTextures[0].name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);

            // Glyphs added while testing in the editor are not built into worlds.
            var serialized = new SerializedObject(asset);
            SerializedProperty clearOnBuild = serialized.FindProperty("m_ClearDynamicDataOnBuild");
            if (clearOnBuild != null)
            {
                clearOnBuild.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            // Start empty: glyphs are added as text needs them.
            asset.ClearFontAssetData(true);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("[MenUI] created the font asset " + path + ".");
            return asset;
        }

        /// Per unit font size: ascender, descender (negative), line height,
        /// and the padding TMP adds around each glyph quad.
        public static void FontMetrics(TMP_FontAsset font, out float ascender, out float descender,
            out float lineHeight, out float padding)
        {
            var face = font.faceInfo;
            float perUnit = face.scale / face.pointSize;
            ascender = face.ascentLine * perUnit;
            descender = face.descentLine * perUnit;
            lineHeight = face.lineHeight * perUnit;
            padding = ShaderUtilities.GetPadding(font.material, false, false) * perUnit;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
