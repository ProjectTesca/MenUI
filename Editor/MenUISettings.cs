// The project's MenUI settings: the font registry.
//
// Entry 0 is always the default font, Noto Sans JP (shipped with MenUI); the
// icon fonts "Material Symbols Rounded" and "Material Symbols Rounded Filled"
// are always there too. Add entries to use other fonts, by family name, from
// code and style sheets:
//
//   Text("Hello").FontFamily("Inter");
//   sheet.Rule(".title", s => s.FontFamily("M PLUS Rounded, Noto Sans JP"));
//
// An entry takes a font file (.ttf/.otf) — MenUI makes a Dynamic TMP font
// asset from it with the right settings — or an existing TMP font asset.
// Characters a font lacks fall back to the default font unless turned off.
// Changes are applied to every MenUI panel in the open scenes.
//
// MenUI > Settings opens (and, the first time, creates) the asset.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MenUI.Editor
{
    [Serializable]
    public sealed class FontEntry
    {
        [Tooltip("The name FontFamily(...) uses in code and style sheets.")]
        public string family;
        [Tooltip("A .ttf or .otf file. MenUI makes a Dynamic TMP font asset from it.")]
        public Font font;
        [Tooltip("The TMP font asset used for this family (made from the font file, or one of your own).")]
        public TMP_FontAsset fontAsset;
        [Tooltip("Characters this font lacks are taken from the default font (Noto Sans JP).")]
        public bool fallbackToDefault = true;
        [HideInInspector]
        public bool generated;   // fontAsset was made by MenUI (its atlas is emptied after play)
    }

    public sealed class MenUISettings : ScriptableObject
    {
        public const string AssetPath = "Assets/MenUI/MenUI Settings.asset";
        private const string GeneratedFontsFolder = "Assets/MenUI/Fonts";

        [Tooltip("Fonts panels can use. The first is the default (Noto Sans JP) and is always present.")]
        public List<FontEntry> fonts = new List<FontEntry>();

        /// The settings asset, or null when the project has none (then only
        /// the default font is used).
        public static MenUISettings Find()
        {
            return AssetDatabase.LoadAssetAtPath<MenUISettings>(AssetPath);
        }

        [MenuItem("MenUI/Settings")]
        private static void Open()
        {
            MenUISettings settings = Find();
            if (settings == null)
            {
                settings = CreateInstance<MenUISettings>();
                string folder = System.IO.Path.GetDirectoryName(AssetPath).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    AssetDatabase.CreateFolder("Assets", "MenUI");
                }
                AssetDatabase.CreateAsset(settings, AssetPath);
                settings.Apply();
            }
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        private void OnValidate()
        {
            // Asset work cannot happen inside OnValidate.
            EditorApplication.delayCall -= Apply;
            EditorApplication.delayCall += Apply;
        }

        /// Makes the registry consistent (default first, font assets made,
        /// fallbacks set) and re-sets up the panels in the open scenes.
        public void Apply()
        {
            if (this == null)
            {
                return;
            }
            PackageAssets.WhenTmpReady(ApplyNow);
        }

        private void ApplyNow()
        {
            TMP_FontAsset defaultFont = PackageAssets.DefaultFontAsset();
            if (defaultFont == null)
            {
                return;
            }
            bool changed = false;

            // Entry 0: the default font, whatever was typed over it.
            if (fonts.Count == 0 || !PackageAssets.IsDefaultFontAsset(fonts[0].fontAsset))
            {
                fonts.RemoveAll(entry => entry != null && PackageAssets.IsDefaultFontAsset(entry.fontAsset));
                fonts.Insert(0, new FontEntry());
                changed = true;
            }
            FontEntry first = fonts[0];
            if (first.family != PackageAssets.DefaultFontFamily || first.font != null || first.fontAsset != defaultFont
                || first.fallbackToDefault || !first.generated)
            {
                first.family = PackageAssets.DefaultFontFamily;
                first.font = null;
                first.fontAsset = defaultFont;
                first.fallbackToDefault = false;
                first.generated = true;
                changed = true;
            }

            HashSet<string> families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            families.Add(first.family);
            for (int i = 1; i < fonts.Count; i++)
            {
                FontEntry entry = fonts[i];
                if (entry == null)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(entry.family) && (entry.font != null || entry.fontAsset != null))
                {
                    entry.family = entry.font != null ? entry.font.name : entry.fontAsset.name;
                    changed = true;
                }
                // A font file: make (or remake, when the file changed) its font asset.
                if (entry.font != null && (entry.fontAsset == null
                    || (entry.generated && entry.fontAsset.sourceFontFile != entry.font)))
                {
                    string path = AssetDatabase.GenerateUniqueAssetPath(GeneratedFontsFolder + "/" + SafeName(entry.family) + " SDF.asset");
                    entry.fontAsset = PackageAssets.CreateDynamicFontAsset(entry.font, path);
                    entry.generated = entry.fontAsset != null;
                    changed = true;
                }
                if (entry.fontAsset != null && entry.fallbackToDefault && entry.fontAsset != defaultFont)
                {
                    List<TMP_FontAsset> table = entry.fontAsset.fallbackFontAssetTable;
                    if (table == null)
                    {
                        table = new List<TMP_FontAsset>();
                        entry.fontAsset.fallbackFontAssetTable = table;
                    }
                    if (!table.Contains(defaultFont))
                    {
                        table.Add(defaultFont);
                        EditorUtility.SetDirty(entry.fontAsset);
                    }
                }
                if (!string.IsNullOrEmpty(entry.family) && !families.Add(entry.family))
                {
                    Debug.LogWarning("[MenUI] the font family \"" + entry.family + "\" is registered twice; the first one is used.");
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(this);
            }
            AssetDatabase.SaveAssets();
            PanelSetup.SetUpAllPanels();
        }

        /// The families panels get, in order (index 0: the default), with
        /// their font assets. Without a settings asset: the default only.
        public static void RegisteredFonts(List<string> families, List<TMP_FontAsset> assets)
        {
            families.Clear();
            assets.Clear();
            families.Add(PackageAssets.DefaultFontFamily);
            assets.Add(PackageAssets.DefaultFontAsset());
            PackageAssets.IconFonts(families, assets);
            MenUISettings settings = Find();
            if (settings == null)
            {
                return;
            }
            HashSet<string> seen = new HashSet<string>(families, StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < settings.fonts.Count; i++)
            {
                FontEntry entry = settings.fonts[i];
                if (entry != null && entry.fontAsset != null && !string.IsNullOrEmpty(entry.family) && seen.Add(entry.family))
                {
                    families.Add(entry.family);
                    assets.Add(entry.fontAsset);
                }
            }
        }

        /// Font assets MenUI made, whose dynamic atlases it may empty.
        public static void GeneratedFontAssets(List<TMP_FontAsset> into)
        {
            into.Clear();
            TMP_FontAsset defaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PackageAssets.PackageRoot + "/Runtime/Fonts/NotoSansJP-Regular SDF.asset");
            if (defaultFont != null)
            {
                into.Add(defaultFont);
            }
            List<string> iconFamilies = new List<string>();
            List<TMP_FontAsset> iconAssets = new List<TMP_FontAsset>();
            PackageAssets.IconFonts(iconFamilies, iconAssets);
            into.AddRange(iconAssets);
            MenUISettings settings = Find();
            if (settings == null)
            {
                return;
            }
            for (int i = 1; i < settings.fonts.Count; i++)
            {
                FontEntry entry = settings.fonts[i];
                if (entry != null && entry.generated && entry.fontAsset != null && !into.Contains(entry.fontAsset))
                {
                    into.Add(entry.fontAsset);
                }
            }
        }

        private static string SafeName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }

    /// Glyphs a Dynamic font asset gains while testing are written into the
    /// asset. For the font assets MenUI made, the atlas is emptied again when
    /// play mode ends, so they stay small and unchanged in version control.
    [InitializeOnLoad]
    internal static class FontAtlasReset
    {
        static FontAtlasReset()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredEditMode)
                {
                    return;
                }
                List<TMP_FontAsset> assets = new List<TMP_FontAsset>();
                MenUISettings.GeneratedFontAssets(assets);
                bool any = false;
                foreach (TMP_FontAsset asset in assets)
                {
                    if (asset.atlasPopulationMode == AtlasPopulationMode.Dynamic)
                    {
                        asset.ClearFontAssetData(true);
                        EditorUtility.SetDirty(asset);
                        any = true;
                    }
                }
                if (any)
                {
                    AssetDatabase.SaveAssets();
                }
            };
        }
    }
}
