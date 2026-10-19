using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ImprovedHierarchy
{
    // ---------------------------------------------------------------------------
    //  Shared picker body — used by the GameObject icon picker (Hierarchy) and
    //  the folder picker (Project window) so both look and behave the same:
    //
    //    [header: target icon + name]
    //    COLOR   ⊘ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■
    //    ICON    ⊘ ◻ ◻ ◻ ◻ ◻ ◻ ◻   (favourites + custom textures)
    //    [hint]
    //    [footer: custom colour · Manager · Reset]
    // ---------------------------------------------------------------------------
    internal static class IHPickerUI
    {
        public static readonly Vector2 Size = new Vector2(304f, 400f);
        public const float Pad = 10f;

        public static void Header(Texture icon, Color iconTint, string title, string subtitle)
        {
            var r = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, IHTheme.PanelBackground);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), IHTheme.Separator);
            }
            var iconRect = new Rect(r.x + Pad, r.y + 7, 16, 16);
            IHGui.DrawIcon(iconRect, icon, iconTint);
            var titleRect = new Rect(iconRect.xMax + 6, r.y, r.width - iconRect.xMax - 6 - Pad, r.height);
            GUI.Label(titleRect, title, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(subtitle)) GUI.Label(titleRect, subtitle, IHGui.Styles.HintRight);
        }

        /// <summary>Colour row. Returns true when a swatch was clicked (white = "no colour").</summary>
        public static bool ColorRow(Color current, out Color picked)
        {
            picked = default;
            bool unset = IHTheme.IsUnset(current);
            float s = IHGui.SwatchSize, gap = IHGui.SwatchGap;
            var row = GUILayoutUtility.GetRect(0, s + 6, GUILayout.ExpandWidth(true));
            var r = new Rect(row.x + Pad, row.y + 3, s, s);

            bool changed = false;
            if (IHGui.NoneSwatch(r, unset, "No colour")) { picked = Color.white; changed = true; }
            foreach (var c in InspectorSuitePalette.Swatches)
            {
                r.x += s + gap;
                bool sel = !unset && SameColor(current, c);
                if (IHGui.ColorSwatch(r, c, sel)) { picked = c; changed = true; }
            }
            return changed;
        }

        private static bool SameColor(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        public enum IconAction { None, PickNone, PickName, PickTexture, Unfavorite, UnfavoriteTexture }

        public struct IconResult
        {
            public IconAction Action;
            public string Name;
            public Texture2D Texture;
        }

        /// <summary>
        /// Grid of the library's favourite icons + custom textures, led by a
        /// "no icon" cell. Left-click picks, right-click unfavourites.
        /// </summary>
        public static IconResult IconGrid(IconLibrary lib, string currentName, Texture currentTex, float width, bool includeTextures = true)
        {
            var result = new IconResult();
            var cells = new List<(Texture tex, string name, Texture2D custom)>();
            cells.Add((null, null, null)); // "no icon"
            var seen = new HashSet<string>();
            foreach (var n in lib.favoriteInternalIcons)
            {
                if (!seen.Add(IHIcons.Canonical(n))) continue;
                var t = IHIcons.Get(n);
                if (t != null) cells.Add((t, n, null));
            }
            if (includeTextures)
                foreach (var t in lib.customTextures)
                    if (t != null) cells.Add((t, null, t));

            float inner = width - Pad * 2;
            int cols = IHGui.Columns(inner);
            float cell = (inner - (cols - 1) * IHGui.GridGap) / cols;
            var area = GUILayoutUtility.GetRect(width, IHGui.GridHeight(cells.Count, cols, cell), GUILayout.ExpandWidth(true));
            var origin = new Rect(area.x + Pad, area.y, cell, cell);

            bool noneSelected = string.IsNullOrEmpty(currentName) && currentTex == null;
            for (int i = 0; i < cells.Count; i++)
            {
                var r = IHGui.GridRect(origin, i, cols, cell);
                var (tex, name, custom) = cells[i];

                if (i == 0)
                {
                    if (IHGui.NoneSwatch(IHGui.CenteredSquare(r, 22f), noneSelected, "Default icon"))
                        result.Action = IconAction.PickNone;
                    continue;
                }

                bool selected = custom != null ? currentTex == custom
                              : !string.IsNullOrEmpty(currentName) && IHIcons.Canonical(currentName) == IHIcons.Canonical(name);
                string label = custom != null ? custom.name : name;
                switch (IHGui.IconCell(r, tex, selected, label + "\n\nClick: apply  ·  Right-click: remove from favorites"))
                {
                    case IHGui.CellClick.Left:
                        result.Action = custom != null ? IconAction.PickTexture : IconAction.PickName;
                        result.Name = name;
                        result.Texture = custom;
                        break;
                    case IHGui.CellClick.Right:
                        result.Action = custom != null ? IconAction.UnfavoriteTexture : IconAction.Unfavorite;
                        result.Name = name;
                        result.Texture = custom;
                        break;
                }
            }

            if (cells.Count == 1)
            {
                GUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(Pad);
                IHGui.Hint("No favorite icons yet — add some in the Icon Manager.");
                EditorGUILayout.EndHorizontal();
            }
            return result;
        }

        /// <summary>Applies an unfavourite action to the library. Returns true if something was removed.</summary>
        public static bool ApplyUnfavorite(IconLibrary lib, IconResult r)
        {
            bool removed = false;
            if (r.Action == IconAction.Unfavorite && r.Name != null)
                removed = lib.favoriteInternalIcons.RemoveAll(n => IHIcons.Canonical(n) == IHIcons.Canonical(r.Name)) > 0;
            else if (r.Action == IconAction.UnfavoriteTexture && r.Texture != null)
                removed = lib.customTextures.RemoveAll(t => t == r.Texture) > 0;
            if (removed) EditorUtility.SetDirty(lib);
            return removed;
        }

        public static void Hint(string text)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);
            GUILayout.Label(text, IHGui.Styles.Hint);
            GUILayout.Space(Pad);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Footer bar: custom colour field · Manager · Reset. Returns true when the colour field changed.</summary>
        public static bool Footer(Color current, out Color custom, out bool manager, out bool reset)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            custom = EditorGUILayout.ColorField(new GUIContent("", "Custom colour"), IHTheme.IsUnset(current) ? Color.white : current,
                                                true, true, false, GUILayout.Width(52));
            bool changed = EditorGUI.EndChangeCheck();
            GUILayout.Label("Custom", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            manager = GUILayout.Button(new GUIContent("Manager…", "Choose which icons appear here"), EditorStyles.toolbarButton);
            reset = GUILayout.Button(new GUIContent("Reset", "Remove the colour and icon"), EditorStyles.toolbarButton);
            EditorGUILayout.EndHorizontal();
            return changed;
        }
    }

    // ---------------------------------------------------------------------------
    //  GAMEOBJECT ICON PICKER (right-click a GameObject's icon in the Hierarchy)
    // ---------------------------------------------------------------------------
    public class IconPickerWindow : PopupWindowContent
    {
        private GameObject targetObj;
        private Vector2 scroll;

        public static void Show(Rect rect, GameObject obj) => PopupWindow.Show(rect, new IconPickerWindow { targetObj = obj });
        public override Vector2 GetWindowSize() => IHPickerUI.Size;

        public override void OnOpen() => editorWindow.wantsMouseMove = true;

        // Writes go through GameObjectIconStore, which keeps data in the
        // IconLibrary asset rather than on a MonoBehaviour — so uninstalling the
        // package never leaves "Missing (Mono Script)" behind.
        private IEnumerable<GameObject> Targets(bool recursive)
        {
            yield return targetObj;
            if (!recursive) yield break;
            foreach (var t in targetObj.GetComponentsInChildren<Transform>(true))
                if (t.gameObject != targetObj) yield return t.gameObject;
        }

        private void ApplyColor(Color c, bool recursive)
        {
            foreach (var go in Targets(recursive)) GameObjectIconStore.GetOrCreate(go).iconColor = c;
            GameObjectIconStore.MarkLibraryDirty();
            InspectorEditorSuite.RepaintHierarchy();
        }

        private void ApplyIcon(string iconName, Texture2D customTex, bool recursive)
        {
            foreach (var go in Targets(recursive))
            {
                var entry = GameObjectIconStore.GetOrCreate(go);
                entry.internalIconName = iconName ?? "";
                entry.customIconTexture = customTex;
            }
            GameObjectIconStore.MarkLibraryDirty();
            InspectorEditorSuite.RepaintHierarchy();
        }

        private void ResetAll(bool recursive)
        {
            foreach (var go in Targets(recursive)) GameObjectIconStore.Reset(go);
            InspectorEditorSuite.RepaintHierarchy();
        }

        public override void OnGUI(Rect rect)
        {
            if (targetObj == null) { editorWindow.Close(); return; }
            if (Event.current.type == EventType.MouseMove) editorWindow.Repaint();

            // LoadOrCreate: on a fresh install this creates + seeds the library so
            // the default favourites are there the very first time.
            var lib = InspectorSuiteLibrary.LoadOrCreate();
            var data = GameObjectIconStore.Get(targetObj);
            bool recursive = Event.current.alt;
            int children = targetObj.transform.childCount;

            Color current = data != null ? data.iconColor : Color.white;
            Texture currentIcon = data?.customIconTexture;
            if (currentIcon == null && data != null && !string.IsNullOrEmpty(data.internalIconName)) currentIcon = IHIcons.Get(data.internalIconName);
            if (currentIcon == null) currentIcon = AssetPreview.GetMiniThumbnail(targetObj);

            IHPickerUI.Header(currentIcon, Color.white, targetObj.name,
                              recursive && children > 0 ? "+ children" : null);

            GUILayout.Space(2);
            IHGui.SectionHeader("   COLOR");
            if (IHPickerUI.ColorRow(current, out var picked)) ApplyColor(picked, recursive);

            IHGui.SectionHeader("   ICON");
            scroll = GUILayout.BeginScrollView(scroll);
            var res = IHPickerUI.IconGrid(lib, data?.internalIconName, data?.customIconTexture, IHPickerUI.Size.x - 14f);
            GUILayout.Space(4);
            GUILayout.EndScrollView();

            switch (res.Action)
            {
                case IHPickerUI.IconAction.PickNone: ApplyIcon(null, null, recursive); break;
                case IHPickerUI.IconAction.PickName: ApplyIcon(res.Name, null, recursive); break;
                case IHPickerUI.IconAction.PickTexture: ApplyIcon(null, res.Texture, recursive); break;
                case IHPickerUI.IconAction.Unfavorite:
                case IHPickerUI.IconAction.UnfavoriteTexture:
                    if (IHPickerUI.ApplyUnfavorite(lib, res)) InspectorEditorSuite.RepaintHierarchy();
                    break;
            }

            IHPickerUI.Hint(children > 0
                ? "Hold Alt to apply to all children too  ·  Right-click an icon to unfavorite"
                : "Right-click an icon to remove it from favorites");
            GUILayout.Space(4);

            if (IHPickerUI.Footer(current, out var custom, out bool manager, out bool reset)) ApplyColor(custom, recursive);
            if (manager) { IconManagerWindow.ShowWindow(); editorWindow.Close(); }
            if (reset) { ResetAll(recursive); editorWindow.Close(); }
        }
    }

    // ---------------------------------------------------------------------------
    //  ICON MANAGER — choose which built-in icons / textures are favourites
    // ---------------------------------------------------------------------------
    public class IconManagerWindow : EditorWindow
    {
        private IconLibrary library;
        private string search = "";
        private Vector2 scroll;

        // Curated built-in icons. Missing names are filtered out at draw time,
        // so this list is safe across Unity versions.
        private static readonly string[] BuiltIn =
        {
            // Objects & assets
            "GameObject Icon", "Prefab Icon", "PrefabVariant Icon", "ScriptableObject Icon", "Folder Icon", "FolderFavorite Icon",
            "TextAsset Icon", "cs Script Icon", "Shader Icon", "Material Icon", "Mesh Icon", "Texture Icon", "Font Icon",
            "AudioClip Icon", "AnimationClip Icon", "AnimatorController Icon", "Avatar Icon", "SceneAsset Icon",
            // Components
            "Camera Icon", "Light Icon", "ReflectionProbe Icon", "LightProbeGroup Icon", "AudioSource Icon", "AudioListener Icon",
            "Rigidbody Icon", "BoxCollider Icon", "SphereCollider Icon", "CapsuleCollider Icon", "MeshCollider Icon",
            "CharacterController Icon", "Animator Icon", "ParticleSystem Icon", "MeshRenderer Icon", "SpriteRenderer Icon",
            "Terrain Icon", "WindZone Icon", "NavMeshAgent Icon", "Tilemap Icon", "Grid Icon",
            // UI
            "Canvas Icon", "RectTransform Icon", "EventSystem Icon", "Button Icon", "Image Icon", "Text Icon",
            "VerticalLayoutGroup Icon", "HorizontalLayoutGroup Icon", "GridLayoutGroup Icon",
            // Tools & controls
            "MoveTool", "RotateTool", "ScaleTool", "RectTool", "ViewToolOrbit", "ViewToolZoom", "ViewToolMove",
            "PlayButton", "PauseButton", "StepButton", "Animation.Play", "Animation.Record", "preAudioPlayOn", "preAudioPlayOff",
            "SaveAs", "Refresh", "SettingsIcon", "_Help", "_Popup", "Search Icon", "FilterByType", "Clipboard",
            "TreeEditor.Duplicate", "TreeEditor.Trash", "TreeEditor.Refresh", "Toolbar Plus", "Toolbar Minus",
            "animationvisibilitytoggleon", "animationvisibilitytoggleoff", "Linked", "Unlinked", "Favorite",
            "console.infoicon", "console.warnicon", "console.erroricon", "Mirror", "SpeedScale", "EyeDropper.Large",
            "tree_icon", "tree_icon_leaf", "UnityLogo",
            // Coloured labels — handy for tagging / grouping
            "sv_label_0", "sv_label_1", "sv_label_2", "sv_label_3", "sv_label_4", "sv_label_5", "sv_label_6", "sv_label_7",
            "sv_icon_dot0_pix16_gizmo", "sv_icon_dot1_pix16_gizmo", "sv_icon_dot2_pix16_gizmo", "sv_icon_dot3_pix16_gizmo",
            "sv_icon_dot4_pix16_gizmo", "sv_icon_dot5_pix16_gizmo", "sv_icon_dot6_pix16_gizmo", "sv_icon_dot7_pix16_gizmo",
        };

        [MenuItem("Tools/Improved Hierarchy/Icon Manager", priority = 2)]
        public static void ShowWindow()
        {
            var w = GetWindow<IconManagerWindow>();
            w.minSize = new Vector2(320, 360);
        }

        private void OnEnable()
        {
            library = InspectorSuiteLibrary.LoadOrCreate();
            wantsMouseMove = true;
            titleContent = new GUIContent("Icon Manager", IHIcons.Get("Favorite"));
        }

        private void OnGUI()
        {
            if (library == null) library = InspectorSuiteLibrary.LoadOrCreate();
            if (Event.current.type == EventType.MouseMove) Repaint();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            float width = position.width - 14f;
            scroll = EditorGUILayout.BeginScrollView(scroll);

            // ---- Favourites --------------------------------------------------
            var favs = library.favoriteInternalIcons
                .GroupBy(IHIcons.Canonical).Select(g => g.First())
                .Where(n => IHIcons.Get(n) != null).ToList();
            int favCount = favs.Count + library.customTextures.Count(t => t != null);
            IHGui.SectionHeader($"  FAVORITES  ({favCount})", "Click to remove  ");

            string removeName = null;
            Texture2D removeTex = null;
            int i = 0;
            int cols = IHGui.Columns(width - 12f);
            var total = favs.Count + library.customTextures.Count;
            var area = GUILayoutUtility.GetRect(width, Mathf.Max(IHGui.GridCell, IHGui.GridHeight(total, cols)));
            var origin = new Rect(area.x + 6f, area.y, IHGui.GridCell, IHGui.GridCell);
            foreach (var n in favs)
            {
                var r = IHGui.GridRect(origin, i++, cols);
                if (IHGui.IconCell(r, IHIcons.Get(n), false, n + "\n\nClick to remove from favorites") != IHGui.CellClick.None)
                    removeName = n;
            }
            foreach (var t in library.customTextures)
            {
                if (t == null) continue;
                var r = IHGui.GridRect(origin, i++, cols);
                if (IHGui.IconCell(r, t, false, t.name + " (custom texture)\n\nClick to remove from favorites") != IHGui.CellClick.None)
                    removeTex = t;
            }
            if (total == 0) GUI.Label(area, "No favorites yet — click icons below to add them.", EditorStyles.centeredGreyMiniLabel);

            // Custom texture drop / pick
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            var added = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Add texture", "Add any Texture2D from your project as a favorite icon"),
                                                              null, typeof(Texture2D), false, GUILayout.Height(18));
            if (EditorGUI.EndChangeCheck() && added != null && !library.customTextures.Contains(added))
            {
                library.customTextures.Add(added);
                EditorUtility.SetDirty(library);
            }
            GUILayout.Space(6);
            EditorGUILayout.EndHorizontal();

            // ---- Built-in library ------------------------------------------
            var fav = new HashSet<string>(library.favoriteInternalIcons.Select(IHIcons.Canonical));
            var list = BuiltIn
                .Where(n => string.IsNullOrEmpty(search) || n.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(n => IHIcons.Get(n) != null)
                .ToList();
            IHGui.SectionHeader($"  BUILT-IN ICONS  ({list.Count})", "Click to add / remove  ");
            area = GUILayoutUtility.GetRect(width, Mathf.Max(IHGui.GridCell, IHGui.GridHeight(list.Count, cols)));
            origin = new Rect(area.x + 6f, area.y, IHGui.GridCell, IHGui.GridCell);
            string toggleName = null;
            for (int k = 0; k < list.Count; k++)
            {
                var n = list[k];
                bool isFav = fav.Contains(IHIcons.Canonical(n));
                var r = IHGui.GridRect(origin, k, cols);
                if (IHGui.IconCell(r, IHIcons.Get(n), isFav, n + (isFav ? "\n\nFavorite — click to remove" : "\n\nClick to add to favorites")) == IHGui.CellClick.Left)
                    toggleName = n;
            }
            if (list.Count == 0) GUI.Label(area, "No icons match your search.", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(8);
            EditorGUILayout.EndScrollView();

            IHGui.Separator(0);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(6);
            IHGui.Hint("Favorites appear in the Hierarchy icon picker (right-click a GameObject's icon) and the folder picker (right-click a folder › Customize Folder…).");
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);

            // Apply mutations after layout.
            if (removeName != null)
            {
                library.favoriteInternalIcons.RemoveAll(n => IHIcons.Canonical(n) == IHIcons.Canonical(removeName));
                EditorUtility.SetDirty(library);
            }
            if (removeTex != null)
            {
                library.customTextures.RemoveAll(t => t == removeTex);
                EditorUtility.SetDirty(library);
            }
            if (toggleName != null)
            {
                if (fav.Contains(IHIcons.Canonical(toggleName)))
                    library.favoriteInternalIcons.RemoveAll(n => IHIcons.Canonical(n) == IHIcons.Canonical(toggleName));
                else
                    library.favoriteInternalIcons.Add(toggleName);
                EditorUtility.SetDirty(library);
            }
            if (removeName != null || removeTex != null || toggleName != null)
            {
                InspectorEditorSuite.RepaintHierarchy();
                EditorApplication.RepaintProjectWindow();
            }
        }
    }
}
