using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
// Pin to the Package Manager type. `UnityEditor` also exposes a legacy
// `PackageInfo` symbol which collides with `UnityEditor.PackageManager.PackageInfo`
// when both namespaces are imported, producing CS0104 in Unity 6.
using PmPackageInfo = UnityEditor.PackageManager.PackageInfo;
using PmPackageSource = UnityEditor.PackageManager.PackageSource;

namespace ImprovedHierarchy
{
    /// <summary>
    /// Searchable / favouritable scene list, drawn inside <see cref="SceneSelectorPopup"/>.
    /// Keyboard: type to filter · ↑↓ to move · Enter to open · Ctrl+Enter to open additively.
    /// </summary>
    internal static class SceneSelectorUI
    {
        public class Entry
        {
            public string Guid, Path, Name, Folder;
        }

        [System.Serializable]
        public class State
        {
            public string search = string.Empty;
            public Vector2 scroll;
            public int highlight;
            public bool focusSearch = true;
            [System.NonSerialized] public List<Entry> all;
            [System.NonSerialized] public string lastSearch;
            [System.NonSerialized] public List<Entry> favorites = new List<Entry>();
            [System.NonSerialized] public List<Entry> others = new List<Entry>();
        }

        private const float RowHeight = 22f;
        private const string SearchControl = "IH.SceneSearch";

        public static void Rebuild(State state)
        {
            state.all = new List<Entry>();
            foreach (var guid in AssetDatabase.FindAssets("t:SceneAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                // Scenes in read-only packages (URP / HDRP templates…) can't be opened.
                if (IsInReadOnlyPackage(path)) continue;
                string folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
                if (folder.StartsWith("Assets/")) folder = folder.Substring(7);
                else if (folder == "Assets") folder = "";
                state.all.Add(new Entry { Guid = guid, Path = path, Name = System.IO.Path.GetFileNameWithoutExtension(path), Folder = folder });
            }
            state.all = state.all.OrderBy(e => e.Name, System.StringComparer.OrdinalIgnoreCase).ToList();
            state.lastSearch = null;
        }

        private static void Filter(State state, IconLibrary library)
        {
            state.favorites.Clear();
            state.others.Clear();
            foreach (var e in state.all)
            {
                if (!string.IsNullOrEmpty(state.search)
                    && e.Name.IndexOf(state.search, System.StringComparison.OrdinalIgnoreCase) < 0
                    && e.Folder.IndexOf(state.search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                (library.favoriteScenes.Contains(e.Guid) ? state.favorites : state.others).Add(e);
            }
            state.lastSearch = state.search;
        }

        /// <summary>Returns true if the owning popup should close (a scene was opened).</summary>
        public static bool Draw(State state, IconLibrary library, float width)
        {
            if (state.all == null) Rebuild(state);
            if (state.lastSearch != state.search) { Filter(state, library); state.highlight = 0; }

            var visible = state.favorites.Concat(state.others).ToList();
            state.highlight = visible.Count == 0 ? 0 : Mathf.Clamp(state.highlight, 0, visible.Count - 1);

            // ---- Keyboard (before the search field can swallow the keys) ----
            var ev = Event.current;
            if (ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.DownArrow) { state.highlight = Mathf.Min(state.highlight + 1, visible.Count - 1); ScrollToHighlight(state); ev.Use(); }
                else if (ev.keyCode == KeyCode.UpArrow) { state.highlight = Mathf.Max(state.highlight - 1, 0); ScrollToHighlight(state); ev.Use(); }
                else if ((ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) && visible.Count > 0)
                {
                    ev.Use();
                    return OpenScene(visible[state.highlight].Path, ev.control || ev.command);
                }
            }

            // ---- Search ----
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUI.SetNextControlName(SearchControl);
            state.search = EditorGUILayout.TextField(state.search, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));
            if (state.focusSearch) { EditorGUI.FocusTextInControl(SearchControl); state.focusSearch = false; }
            var refresh = GUILayoutUtility.GetRect(20, 18, GUILayout.Width(20));
            if (IHGui.IconButton(refresh, IHIcons.Get(IHIcons.Refresh), "Rescan scenes")) { Rebuild(state); }
            EditorGUILayout.EndHorizontal();

            // ---- List ----
            bool close = false;
            state.scroll = EditorGUILayout.BeginScrollView(state.scroll);
            if (visible.Count == 0)
            {
                GUILayout.Space(24);
                GUILayout.Label(state.all.Count == 0 ? "No scenes in this project" : "No scenes match your search", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                var active = SceneManager.GetActiveScene();
                int index = 0;
                if (state.favorites.Count > 0)
                {
                    IHGui.SectionHeader("  FAVORITES");
                    foreach (var e in state.favorites)
                    {
                        close |= DrawRow(library, e, true, active, index++ == state.highlight);
                        if (close) break;
                    }
                    if (!close && state.others.Count > 0) IHGui.SectionHeader("  ALL SCENES");
                }
                foreach (var e in state.others)
                {
                    if (close) break;
                    close |= DrawRow(library, e, false, active, index++ == state.highlight);
                }
                GUILayout.Space(4);
            }
            EditorGUILayout.EndScrollView();

            // ---- Footer ----
            IHGui.Separator(0);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("Enter / click: open   ·   Ctrl: additive   ·   ★ favorite", IHGui.Styles.Hint);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);

            return close;
        }

        private static void ScrollToHighlight(State state)
        {
            float y = state.highlight * RowHeight;
            if (y < state.scroll.y) state.scroll.y = y;
            else if (y > state.scroll.y + 260f) state.scroll.y = y - 260f;
        }

        private static bool DrawRow(IconLibrary library, Entry e, bool fav, Scene active, bool highlighted)
        {
            var ev = Event.current;
            var row = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
            bool isActive = active.path == e.Path;
            var loaded = SceneManager.GetSceneByPath(e.Path);
            bool isLoaded = loaded.IsValid() && loaded.isLoaded;
            bool hover = row.Contains(ev.mousePosition);

            var starRect = new Rect(row.x + 4, row.y + 2, 18, 18);
            var pingRect = new Rect(row.xMax - 24, row.y + 1, 20, 20);
            var iconRect = new Rect(starRect.xMax + 4, row.y + 3, 16, 16);
            var textRect = new Rect(iconRect.xMax + 6, row.y, pingRect.x - iconRect.xMax - 10, row.height);

            if (ev.type == EventType.Repaint)
            {
                if (isActive) EditorGUI.DrawRect(row, IHTheme.AccentFill);
                if (hover || highlighted) EditorGUI.DrawRect(row, highlighted ? IHTheme.Pressed : IHTheme.Hover);
                if (isActive) EditorGUI.DrawRect(new Rect(row.x, row.y, 2, row.height), IHTheme.Accent);
            }

            // Star
            var prev = GUI.contentColor;
            GUI.contentColor = fav ? IHTheme.Highlight : (starRect.Contains(ev.mousePosition) ? IHTheme.Text : IHTheme.WithAlpha(IHTheme.Text, 0.35f));
            GUI.Label(starRect, new GUIContent(fav ? "★" : "☆", fav ? "Remove from favorites" : "Add to favorites"), IHGui.Styles.CenteredGlyph);
            GUI.contentColor = prev;
            EditorGUIUtility.AddCursorRect(starRect, MouseCursor.Link);
            if (IHGui.Clicked(starRect))
            {
                if (fav) library.favoriteScenes.Remove(e.Guid);
                else library.favoriteScenes.Add(e.Guid);
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssetIfDirty(library);
                return false;
            }

            // Ping (only while hovered, like a row action)
            if (hover && IHGui.IconButton(pingRect, IHIcons.Get(IHIcons.Ping), "Show in Project"))
            {
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(e.Path));
                return false;
            }

            IHGui.DrawIcon(iconRect, AssetPreview.GetMiniTypeThumbnail(typeof(SceneAsset)), isLoaded ? Color.white : new Color(1, 1, 1, 0.8f));

            var nameStyle = isActive ? EditorStyles.boldLabel : IHGui.Styles.RowTitle;
            var nameContent = new GUIContent(e.Name, e.Path);
            float nameW = Mathf.Min(nameStyle.CalcSize(nameContent).x, textRect.width);
            GUI.Label(new Rect(textRect.x, textRect.y, nameW, textRect.height), nameContent, nameStyle);

            string right = isActive ? "active" : isLoaded ? "loaded" : e.Folder;
            if (!string.IsNullOrEmpty(right))
                GUI.Label(new Rect(textRect.x + nameW + 8, textRect.y, textRect.width - nameW - 8, textRect.height), right, IHGui.Styles.RowSubtitle);

            if (IHGui.Clicked(row))
                return OpenScene(e.Path, ev.control || ev.command);
            return false;
        }

        private static bool OpenScene(string path, bool additive)
        {
            try
            {
                if (additive)
                {
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    return true;
                }
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                return true;
            }
            catch (System.ArgumentException ex)
            {
                Debug.LogWarning($"Scene Selector: cannot open '{path}'. It lives in a read-only package — copy it into Assets/ to edit it.\n{ex.Message}");
                IHGui.Notify("Scene is read-only — see console");
                return false;
            }
        }

        /// <summary>
        /// True when <paramref name="assetPath"/> belongs to a package Unity treats
        /// as immutable (registry, built-in, git…) — its scenes can't be opened.
        /// </summary>
        private static bool IsInReadOnlyPackage(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Packages/", System.StringComparison.Ordinal))
                return false;
            try
            {
                var info = PmPackageInfo.FindForAssetPath(assetPath);
                if (info == null) return false;
                switch (info.source)
                {
                    case PmPackageSource.Embedded:
                    case PmPackageSource.Local:
                    case PmPackageSource.LocalTarball:
                        return false;
                    default:
                        return true;
                }
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Entry point for the scene selector (menu + Ctrl+Shift+O). Also closes any
    /// floating "Scene Selector" EditorWindow persisted in a layout by very old
    /// versions of the package.
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneSelectorMenu
    {
        /// <summary>Screen-space rect of the hierarchy chevron, updated when drawn.</summary>
        public static Rect LastHierarchyChevronScreenRect;

        static SceneSelectorMenu()
        {
            EditorApplication.delayCall += CloseOrphanWindows;
        }

        [MenuItem("Tools/Improved Hierarchy/Scene Selector %#o", priority = 0)] // Ctrl+Shift+O
        public static void ShowWindow()
        {
            CloseOrphanWindows();

            Rect anchor;
            if (LastHierarchyChevronScreenRect.width > 0f)
            {
                anchor = LastHierarchyChevronScreenRect;
            }
            else
            {
                var mouse = GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero);
                anchor = new Rect(mouse.x, mouse.y, 1f, 1f);
            }
            SceneSelectorPopup.ShowAtScreenRect(anchor);
        }

        public static void CloseOrphanWindows()
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var legacy = asm.GetType("SceneSelectorWindow", false, false);
                if (legacy == null) continue;
                foreach (var w in Resources.FindObjectsOfTypeAll(legacy))
                    if (w is EditorWindow ew) ew.Close();
            }
            foreach (var h in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (h != null && h.titleContent != null && h.titleContent.text == "Scene Selector" && !(h.GetType().Name == "PopupWindow"))
                    h.Close();
            }
        }
    }

    /// <summary>Popup form of the scene selector, anchored to its activator.</summary>
    public class SceneSelectorPopup : PopupWindowContent
    {
        private readonly SceneSelectorUI.State state = new SceneSelectorUI.State();
        private IconLibrary library;

        /// <summary>Open from inside a GUI callback (rect in GUI space).</summary>
        public static void ShowAt(Rect activatorRect) => PopupWindow.Show(activatorRect, new SceneSelectorPopup());

        /// <summary>Open from a menu / shortcut (rect in screen space).</summary>
        public static void ShowAtScreenRect(Rect screenRect)
        {
            // PopupWindow.Show converts from GUI space; with no GUI scope active
            // (menu / shortcut) GUI space == screen space, so this is safe.
            PopupWindow.Show(screenRect, new SceneSelectorPopup());
        }

        public override Vector2 GetWindowSize() => new Vector2(340, 420);

        public override void OnOpen()
        {
            library = InspectorSuiteLibrary.LoadOrCreate();
            editorWindow.wantsMouseMove = true;
            SceneSelectorUI.Rebuild(state);
        }

        public override void OnGUI(Rect rect)
        {
            if (library == null) library = InspectorSuiteLibrary.LoadOrCreate();
            if (Event.current.type == EventType.MouseMove) editorWindow.Repaint();
            if (SceneSelectorUI.Draw(state, library, rect.width))
                editorWindow.Close();
        }
    }
}
