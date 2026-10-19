using UnityEditor;
using UnityEngine;

namespace ImprovedHierarchy
{
    [InitializeOnLoad]
    public static class ProjectFolderColorizer
    {
        /// <summary>
        /// Last-known screen-space position of the cursor over the Project
        /// window. Anchors the folder picker — by the time "Customize Folder…" is
        /// picked from the context menu, <c>Event.current</c> is gone.
        /// </summary>
        internal static Rect LastProjectMouseScreenRect = new Rect(200, 200, 1, 1);

        static ProjectFolderColorizer()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectGUI;
        }

        private static void OnProjectGUI(string guid, Rect rect)
        {
            if (Event.current != null && rect.Contains(Event.current.mousePosition))
            {
                try
                {
                    var p = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                    LastProjectMouseScreenRect = new Rect(p.x, p.y, 1, 1);
                }
                catch { /* GUIToScreenPoint outside a GUI scope can throw */ }
            }

            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (string.IsNullOrEmpty(guid)) return;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) return;

            bool isListView = rect.height <= 22f;
            Rect iconRect = isListView
                ? new Rect(rect.x, rect.y, rect.height, rect.height)
                : IconRectForGrid(rect);

            var lib = InspectorSuiteLibrary.LoadIfExists();
            var entry = lib != null ? lib.folderColors.Find(e => e.folderGuid == guid) : null;
            if (entry != null)
            {
                // ---------- Colour = tint on the folder icon itself ----------
                if (!IHTheme.IsUnset(entry.color))
                {
                    var folderTex = IHIcons.Get(IsEmptyFolder(path) ? IHIcons.FolderEmpty : IHIcons.Folder);
                    IHGui.DrawIcon(iconRect, folderTex, entry.color);
                }

                // ---------- Custom icon badge (grid view only — unreadable at 16 px) ----------
                if (!isListView && !string.IsNullOrEmpty(entry.iconName))
                {
                    var icon = IHIcons.Get(entry.iconName);
                    if (icon != null)
                    {
                        float size = Mathf.Floor(iconRect.height * 0.46f);
                        var badge = new Rect(iconRect.xMax - size, iconRect.yMax - size, size, size);
                        IHGui.RoundedRect(IHGui.Inset(badge, -2f), new Color(0f, 0f, 0f, 0.55f), size * 0.3f);
                        IHGui.DrawIcon(badge, icon, Color.white);
                    }
                }
            }

            DrawVcsBadge(path, iconRect, isListView);
        }

        private static Rect IconRectForGrid(Rect rect)
        {
            // Must match Unity's grid icon rect exactly, or the native grey
            // folder peeks out around the tinted copy.
            float size = Mathf.Min(rect.width, rect.height - 14f);
            return new Rect(rect.x + (rect.width - size) * 0.5f, rect.y, size, size);
        }

        // Same rule Unity uses for the empty-folder icon: only entries Unity
        // imports count (hidden dot-files, "name~" folders and .meta don't).
        private static bool IsEmptyFolder(string path)
        {
            try
            {
                foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(path))
                {
                    string name = System.IO.Path.GetFileName(entry);
                    if (name.StartsWith(".") || name.EndsWith("~") || name.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase)) continue;
                    return false;
                }
                return true;
            }
            catch { return false; }
        }

        // -------------------------------------------------------------------------
        // VCS badge — round dot in the TOP-RIGHT of the folder icon (opposite the
        // custom-icon badge in the bottom-right, so both can show at once).
        // -------------------------------------------------------------------------
        private static void DrawVcsBadge(string folderPath, Rect iconRect, bool isListView)
        {
            if (!VcsStatusProvider.Enabled) return;

            Color fill;
            switch (VcsStatusProvider.GetFolderState(folderPath))
            {
                case VcsStatusProvider.State.Modified: fill = new Color(1.00f, 0.76f, 0.20f); break;
                case VcsStatusProvider.State.Untracked: fill = new Color(1.00f, 0.42f, 0.42f); break;
                default: return;
            }

            float size = isListView
                ? Mathf.Clamp(iconRect.height * 0.45f, 6f, 8f)
                : Mathf.Clamp(iconRect.height * 0.22f, 8f, 14f);
            var dot = new Rect(iconRect.xMax - size + (isListView ? 1f : 0f), iconRect.y + (isListView ? 0f : 1f), size, size);

            // Dark ring keeps the dot legible on any folder colour.
            IHGui.RoundedRect(IHGui.Inset(dot, -1.5f), new Color(0.1f, 0.1f, 0.1f, 0.85f), size);
            IHGui.RoundedRect(dot, fill, size);
        }

        // -------------------------------------------------------------------------
        // Right-click menu under Assets/
        // -------------------------------------------------------------------------
        [MenuItem("Assets/Customize Folder…", priority = 19)]
        private static void Ctx_OpenPicker()
        {
            var folder = GetSelectedFolder();
            if (folder != null) FolderColorPickerWindow.Show(folder);
        }

        [MenuItem("Assets/Customize Folder…", true)]
        private static bool Ctx_OpenPickerValidate() => GetSelectedFolder() != null;

        [MenuItem("Assets/Clear Folder Customization", priority = 20)]
        private static void Ctx_Clear()
        {
            var folder = GetSelectedFolder();
            if (folder != null) ClearFolder(folder);
        }

        [MenuItem("Assets/Clear Folder Customization", true)]
        private static bool Ctx_ClearValidate()
        {
            var f = GetSelectedFolder();
            if (f == null) return false;
            var lib = InspectorSuiteLibrary.LoadIfExists();
            if (lib == null) return false;
            string guid = AssetDatabase.AssetPathToGUID(f);
            return lib.folderColors.Exists(e => e.folderGuid == guid);
        }

        internal static void ClearFolder(string folder)
        {
            var lib = InspectorSuiteLibrary.LoadOrCreate();
            string guid = AssetDatabase.AssetPathToGUID(folder);
            if (lib.folderColors.RemoveAll(e => e.folderGuid == guid) > 0)
            {
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssetIfDirty(lib);
            }
            EditorApplication.RepaintProjectWindow();
        }

        private static string GetSelectedFolder()
        {
            if (Selection.activeObject == null) return null;
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return AssetDatabase.IsValidFolder(path) ? path : null;
        }

        public static FolderColorEntry GetOrCreateEntry(string folderPath, IconLibrary lib)
        {
            string guid = AssetDatabase.AssetPathToGUID(folderPath);
            var entry = lib.folderColors.Find(e => e.folderGuid == guid);
            if (entry == null)
            {
                entry = new FolderColorEntry { folderGuid = guid, color = Color.white };
                lib.folderColors.Add(entry);
            }
            return entry;
        }
    }

    // ---------------------------------------------------------------------------
    // FOLDER PICKER — same layout as the GameObject icon picker (IHPickerUI).
    //
    // An EditorWindow shown with ShowAsDropDown (rather than PopupWindowContent)
    // because it is opened from a context-menu callback, where there is no GUI
    // scope to convert a GUI rect to screen space. It still behaves like a
    // dropdown: no chrome, dismissed on focus loss, anchored at the cursor.
    // ---------------------------------------------------------------------------
    public class FolderColorPickerWindow : EditorWindow
    {
        private string folderPath;
        private IconLibrary lib;
        private Vector2 scroll;

        public static void Show(string folderPath)
        {
            var w = CreateInstance<FolderColorPickerWindow>();
            w.folderPath = folderPath;
            w.lib = InspectorSuiteLibrary.LoadOrCreate();
            w.wantsMouseMove = true;
            w.titleContent = new GUIContent($"Customize: {System.IO.Path.GetFileName(folderPath)}");

            Rect anchor = ProjectFolderColorizer.LastProjectMouseScreenRect;
            if (anchor.width < 1)
            {
                var focused = focusedWindow;
                anchor = focused != null
                    ? new Rect(focused.position.x + 40, focused.position.y + 60, 1, 1)
                    : new Rect(200, 200, 1, 1);
            }

            try
            {
                w.ShowAsDropDown(anchor, IHPickerUI.Size);
            }
            catch
            {
                w.minSize = IHPickerUI.Size;
                w.maxSize = IHPickerUI.Size + new Vector2(4, 4);
                w.ShowUtility();
            }
        }

        private void Changed()
        {
            EditorUtility.SetDirty(lib);
            EditorApplication.RepaintProjectWindow();
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(folderPath)) { Close(); return; }
            if (lib == null) lib = InspectorSuiteLibrary.LoadOrCreate();
            if (Event.current.type == EventType.MouseMove) Repaint();

            var entry = ProjectFolderColorizer.GetOrCreateEntry(folderPath, lib);

            // Thin border so the chromeless dropdown separates from what's behind it.
            if (Event.current.type == EventType.Repaint)
                IHGui.RoundedOutline(new Rect(0, 0, position.width, position.height), IHTheme.Border, 1f, 0f);

            IHPickerUI.Header(IHIcons.Get(IHIcons.Folder), IHTheme.IsUnset(entry.color) ? Color.white : entry.color,
                              System.IO.Path.GetFileName(folderPath), null);

            GUILayout.Space(2);
            IHGui.SectionHeader("   COLOR");
            if (IHPickerUI.ColorRow(entry.color, out var picked)) { entry.color = picked; Changed(); }

            IHGui.SectionHeader("   BADGE ICON");
            scroll = GUILayout.BeginScrollView(scroll);
            var res = IHPickerUI.IconGrid(lib, entry.iconName, null, IHPickerUI.Size.x - 14f, includeTextures: false);
            GUILayout.Space(4);
            GUILayout.EndScrollView();

            switch (res.Action)
            {
                case IHPickerUI.IconAction.PickNone: entry.iconName = ""; Changed(); break;
                case IHPickerUI.IconAction.PickName: entry.iconName = res.Name; Changed(); break;
                case IHPickerUI.IconAction.Unfavorite:
                case IHPickerUI.IconAction.UnfavoriteTexture:
                    if (IHPickerUI.ApplyUnfavorite(lib, res)) Changed();
                    break;
            }

            IHPickerUI.Hint("The badge shows in the Project grid view  ·  Right-click an icon to unfavorite");
            GUILayout.Space(4);

            if (IHPickerUI.Footer(entry.color, out var custom, out bool manager, out bool reset)) { entry.color = custom; Changed(); }
            if (manager) { IconManagerWindow.ShowWindow(); Close(); }
            if (reset) { ProjectFolderColorizer.ClearFolder(folderPath); Close(); }
        }

        private void OnDestroy()
        {
            // Don't leave empty (all-default) entries behind in the library.
            if (lib == null || string.IsNullOrEmpty(folderPath)) return;
            string guid = AssetDatabase.AssetPathToGUID(folderPath);
            if (lib.folderColors.RemoveAll(e => e.folderGuid == guid && IHTheme.IsUnset(e.color) && string.IsNullOrEmpty(e.iconName)) > 0)
                EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssetIfDirty(lib);
        }
    }
}
