using UnityEditor;
using UnityEngine;

namespace ImprovedHierarchy
{
    internal static class InspectorSuitePalette
    {
        public static readonly Color[] Swatches = new Color[]
        {
            new Color(0.85f, 0.30f, 0.30f), // red
            new Color(0.90f, 0.55f, 0.20f), // orange
            new Color(0.95f, 0.80f, 0.20f), // yellow
            new Color(0.65f, 0.85f, 0.25f), // yellow-green
            new Color(0.30f, 0.80f, 0.40f), // green
            new Color(0.25f, 0.80f, 0.65f), // mint
            new Color(0.25f, 0.70f, 0.85f), // cyan
            new Color(0.30f, 0.55f, 0.95f), // blue
            new Color(0.55f, 0.40f, 0.90f), // purple
            new Color(0.85f, 0.45f, 0.95f), // magenta-pink
            new Color(0.95f, 0.40f, 0.75f), // pink
            new Color(0.45f, 0.45f, 0.45f), // gray
        };
    }

    internal static class InspectorSuiteLibrary
    {
        // The IconLibrary asset is user data, NOT shipped with the package.
        // It lives in the user's project (Assets/Editor/) and is auto-created
        // on first use.
        public const string Path = "Assets/Editor/InspectorIconLibrary.asset";

        /// <summary>
        /// Curated default favourites pre-populated into a freshly created
        /// IconLibrary so the icon picker is useful immediately, without the
        /// user having to open the Inspector Manager first. Every entry below
        /// is a Unity built-in icon name (resolved via
        /// <c>EditorGUIUtility.IconContent</c>); users can unfavourite any
        /// of them with right-click in the picker, or via Tools › Improved
        /// Hierarchy › Icon Manager.
        /// </summary>
        public static readonly string[] DefaultFavoriteIcons = new string[]
        {
            // Asset / GameObject category essentials.
            "GameObject Icon",
            "Prefab Icon",
            "ScriptableObject Icon",
            "Folder Icon",
            "TextAsset Icon",
            "Shader Icon",
            // Scene-building essentials.
            "Camera Icon",
            "Light Icon",
            "MeshRenderer Icon",
            "Terrain Icon",
            "Avatar Icon",
            // Audio / animation.
            "preAudioPlayOn",
            "AnimatorController Icon",
            "Animation.Play",
            "Animation.Record",
            // Common manipulation tools.
            "MoveTool",
            "RotateTool",
            "ScaleTool",
            "ViewToolOrbit",
            // UI / playback / common controls.
            "PlayButton",
            "PauseButton",
            "StepButton",
            "SaveAs",
            "Refresh",
            "_Help",
            "_Popup",
            "Toolbar Plus",
            "Toolbar Minus",
            // Coloured label dots — great for grouping / tagging GameObjects.
            "sv_label_0",
            "sv_label_1",
            "sv_label_2",
            "sv_label_3",
            "sv_label_4",
            "sv_label_5",
            "sv_label_6",
            "sv_label_7",
        };

        public static IconLibrary LoadOrCreate()
        {
            var lib = AssetDatabase.LoadAssetAtPath<IconLibrary>(Path);
            if (lib != null)
            {
                // If a user upgraded from a pre-0.2.6 version, their library
                // has an empty favourites list. Seed it lazily, but ONLY when
                // it's still empty — never overwrite a curated user list.
                if (lib.favoriteInternalIcons.Count == 0)
                {
                    lib.favoriteInternalIcons.AddRange(DefaultFavoriteIcons);
                    EditorUtility.SetDirty(lib);
                }
                return lib;
            }

            if (System.IO.File.Exists(Path))
                AssetDatabase.DeleteAsset(Path);

            lib = ScriptableObject.CreateInstance<IconLibrary>();
            lib.favoriteInternalIcons.AddRange(DefaultFavoriteIcons);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Application.dataPath, "Editor"));
            AssetDatabase.CreateAsset(lib, Path);
            AssetDatabase.SaveAssets();
            return lib;
        }

        public static IconLibrary LoadIfExists()
            => AssetDatabase.LoadAssetAtPath<IconLibrary>(Path);
    }
}
