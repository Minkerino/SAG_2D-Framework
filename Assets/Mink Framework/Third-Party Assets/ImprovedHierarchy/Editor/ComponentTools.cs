using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ImprovedHierarchy
{
    /// <summary>
    /// The two per-component tools surfaced on every component header:
    ///
    ///   • <b>Multi-Copy Buffer</b> — collect any number of components (from any
    ///     GameObjects, in Edit or Play Mode), then paste them all onto one or
    ///     many GameObjects at once. Values are snapshotted when a component is
    ///     added, so a component copied in Play Mode can be pasted after exiting.
    ///     The buffer survives script reloads and entering Play Mode.
    ///
    ///   • <b>Keep Play Mode Values</b> — mark a component while playing and its
    ///     values at the moment you stop are written back to the scene object.
    /// </summary>
    [InitializeOnLoad]
    public static class ComponentTools
    {
        public static event Action Changed;

        static ComponentTools()
        {
            LoadBuffer();
            RecoverPlaySavesAfterReload();
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Editor.finishedDefaultHeaderGUI += DrawGameObjectPasteBar;
        }

        /// <summary>
        /// Flags a component written via FromJsonOverwrite as changed. The scene
        /// must be marked explicitly: otherwise Unity can treat it as unchanged
        /// and, on the next Play Mode exit, restore an older scene backup over
        /// the new values.
        /// </summary>
        private static void MarkChanged(Component c)
        {
            EditorUtility.SetDirty(c);
            if (!EditorUtility.IsPersistent(c) && c.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }

        private static void RaiseChanged()
        {
            Changed?.Invoke();
            InternalEditorUtility.RepaintAllViews();
        }

        // =========================================================================
        //  MULTI-COPY BUFFER
        // =========================================================================

        public sealed class CopyEntry
        {
            public Type Type;
            public string TypeName;
            public string Json;
            public string SourceName;
            public string SourceGlobalId;
            internal Component Source;
            internal bool SourceResolved;

            /// <summary>Live source component, re-resolved after a domain reload.</summary>
            public Component GetSource()
            {
                if (Source == null && !SourceResolved)
                {
                    SourceResolved = true;
                    if (!string.IsNullOrEmpty(SourceGlobalId) && GlobalObjectId.TryParse(SourceGlobalId, out var gid))
                        Source = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) as Component;
                }
                return Source;
            }
        }

        private static readonly List<CopyEntry> buffer = new List<CopyEntry>();

        public static IReadOnlyList<CopyEntry> Buffer => buffer;
        public static int BufferCount => buffer.Count;

        public static bool CanCopy(Component c) => c != null && !(c is IconData);

        public static bool IsInBuffer(Component c)
        {
            if (c == null) return false;
            for (int i = 0; i < buffer.Count; i++)
                if (buffer[i].GetSource() == c) return true;
            return false;
        }

        public static void AddToBuffer(Component c, bool notify = true)
        {
            if (!CanCopy(c) || IsInBuffer(c)) return;
            buffer.Add(new CopyEntry
            {
                Type = c.GetType(),
                TypeName = ObjectNames.GetInspectorTitle(c),
                Json = EditorJsonUtility.ToJson(c),
                SourceName = c.gameObject.name,
                SourceGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(c).ToString(),
                Source = c,
                SourceResolved = true,
            });
            SaveBuffer();
            if (notify) IHGui.Notify($"Copied {ObjectNames.GetInspectorTitle(c)}  ·  {buffer.Count} in buffer");
            RaiseChanged();
        }

        public static void RemoveFromBuffer(Component c, bool notify = true)
        {
            if (c == null) return;
            int idx = buffer.FindIndex(e => e.GetSource() == c);
            if (idx < 0) return;
            buffer.RemoveAt(idx);
            SaveBuffer();
            if (notify) IHGui.Notify($"Removed {ObjectNames.GetInspectorTitle(c)}  ·  {buffer.Count} in buffer");
            RaiseChanged();
        }

        public static void ToggleInBuffer(Component c)
        {
            if (IsInBuffer(c)) RemoveFromBuffer(c);
            else AddToBuffer(c);
        }

        public static void RemoveAt(int index)
        {
            if (index < 0 || index >= buffer.Count) return;
            buffer.RemoveAt(index);
            SaveBuffer();
            RaiseChanged();
        }

        public static void ClearBuffer()
        {
            if (buffer.Count == 0) return;
            buffer.Clear();
            SaveBuffer();
            RaiseChanged();
        }

        /// <summary>Adds every component on the GameObject (except its Transform) to the buffer.</summary>
        public static void CopyAllComponents(GameObject go)
        {
            if (go == null) return;
            int added = 0;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null || c is Transform || !CanCopy(c) || IsInBuffer(c)) continue;
                AddToBuffer(c, notify: false);
                added++;
            }
            IHGui.Notify(added > 0
                ? $"Copied {added} component{(added == 1 ? "" : "s")} from {go.name}  ·  {buffer.Count} in buffer"
                : "Nothing new to copy");
        }

        /// <summary>
        /// Pastes the whole buffer onto each target. For every buffered component
        /// the Nth entry of a type overwrites the Nth existing component of that
        /// type, and anything beyond that is added — so copying two BoxColliders
        /// produces two BoxColliders. One Undo step for the whole paste.
        /// </summary>
        public static void PasteBuffer(IEnumerable<GameObject> targets)
        {
            if (buffer.Count == 0) return;
            var gos = targets.Where(g => g != null).Distinct().ToList();
            if (gos.Count == 0) { IHGui.Notify("Select a GameObject to paste onto"); return; }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paste Components");

            int pasted = 0, skipped = 0;
            foreach (var go in gos)
            {
                var usedPerType = new Dictionary<Type, int>();
                foreach (var entry in buffer)
                {
                    if (entry.Type == null) { skipped++; continue; }
                    usedPerType.TryGetValue(entry.Type, out int n);
                    usedPerType[entry.Type] = n + 1;

                    Component target = null;
                    if (typeof(Transform).IsAssignableFrom(entry.Type))
                    {
                        // Transforms can't be added — only overwrite a matching one.
                        if (go.transform.GetType() == entry.Type) target = go.transform;
                        if (target != null) Undo.RecordObject(target, "Paste Components");
                    }
                    else
                    {
                        var existing = go.GetComponents(entry.Type).Where(x => x != null && x.GetType() == entry.Type).ToArray();
                        if (n < existing.Length)
                        {
                            target = existing[n];
                            Undo.RecordObject(target, "Paste Components");
                        }
                        else
                        {
                            target = Undo.AddComponent(go, entry.Type);
                        }
                    }

                    if (target == null) { skipped++; continue; }
                    EditorJsonUtility.FromJsonOverwrite(entry.Json, target);
                    if (PrefabUtility.IsPartOfPrefabInstance(target))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                    MarkChanged(target);
                    pasted++;
                }
            }
            Undo.CollapseUndoOperations(group);

            string where = gos.Count == 1 ? gos[0].name : $"{gos.Count} GameObjects";
            IHGui.Notify(skipped == 0
                ? $"Pasted {pasted} component{(pasted == 1 ? "" : "s")} onto {where}"
                : $"Pasted {pasted}, skipped {skipped} (not allowed on {where})");
            RaiseChanged();
        }

        // ---- Persistence (SessionState: survives script reloads + entering Play Mode) ----

        private const string BufferSessionKey = "ImprovedHierarchy.MultiCopyBuffer";

        [Serializable] private class PersistedEntry { public string type; public string typeName; public string json; public string sourceName; public string sourceGid; }
        [Serializable] private class PersistedBuffer { public List<PersistedEntry> items = new List<PersistedEntry>(); }

        private static void SaveBuffer()
        {
            var p = new PersistedBuffer();
            foreach (var e in buffer)
            {
                p.items.Add(new PersistedEntry
                {
                    type = e.Type?.AssemblyQualifiedName,
                    typeName = e.TypeName,
                    json = e.Json,
                    sourceName = e.SourceName,
                    sourceGid = e.SourceGlobalId,
                });
            }
            SessionState.SetString(BufferSessionKey, JsonUtility.ToJson(p));
        }

        private static void LoadBuffer()
        {
            buffer.Clear();
            var raw = SessionState.GetString(BufferSessionKey, "");
            if (string.IsNullOrEmpty(raw)) return;
            try
            {
                var p = JsonUtility.FromJson<PersistedBuffer>(raw);
                foreach (var i in p.items)
                {
                    var t = string.IsNullOrEmpty(i.type) ? null : Type.GetType(i.type);
                    if (t == null) continue;
                    buffer.Add(new CopyEntry
                    {
                        Type = t,
                        TypeName = i.typeName,
                        Json = i.json,
                        SourceName = i.sourceName,
                        SourceGlobalId = i.sourceGid,
                    });
                }
            }
            catch { /* corrupt session data — start empty */ }
        }

        // =========================================================================
        //  KEEP PLAY MODE VALUES
        // =========================================================================

        private sealed class PlaySave
        {
            public Component Target;
            public string GlobalId;
            public string Json;
            public string Title;
        }

        private static readonly List<PlaySave> playSaves = new List<PlaySave>();

        public static int PlayModeSaveCount => playSaves.Count;

        public static bool HasPlayModeSave(Component c)
        {
            if (c == null) return false;
            for (int i = 0; i < playSaves.Count; i++)
                if (playSaves[i].Target == c) return true;
            return false;
        }

        /// <summary>True when any component on <paramref name="go"/> is marked to keep its Play Mode values.</summary>
        public static bool HasPlayModeSave(GameObject go)
        {
            if (go == null || playSaves.Count == 0) return false;
            for (int i = 0; i < playSaves.Count; i++)
            {
                var t = playSaves[i].Target;
                if (t != null && t.gameObject == go) return true;
            }
            return false;
        }

        public static void TogglePlayModeSave(Component c)
        {
            if (c == null) return;
            if (!Application.isPlaying) { IHGui.Notify("Keep Play Mode Values is only available while playing"); return; }

            string title = ObjectNames.GetInspectorTitle(c);
            int idx = playSaves.FindIndex(s => s.Target == c);
            if (idx >= 0)
            {
                playSaves.RemoveAt(idx);
                PersistPlaySaves();
                IHGui.Notify($"{title} will reset when Play Mode ends");
                RaiseChanged();
                return;
            }

            // Only objects that exist in the saved scene can be written back.
            var gid = GlobalObjectId.GetGlobalObjectIdSlow(c);
            if (gid.identifierType == 0 || gid.targetObjectId == 0)
            {
                IHGui.Notify($"Can't keep {title}: {c.gameObject.name} was created at runtime", 2.0);
                return;
            }

            playSaves.Add(new PlaySave
            {
                Target = c,
                GlobalId = gid.ToString(),
                Json = EditorJsonUtility.ToJson(c),
                Title = title,
            });
            PersistPlaySaves();
            IHGui.Notify($"{title} will keep its values after Play Mode");
            RaiseChanged();
        }

        // Pending saves are mirrored into SessionState so they survive a domain
        // reload — which Unity performs whenever scripts or packages changed
        // during Play Mode (mid-play, or right as Play Mode ends).
        private const string PlaySavesSessionKey = "ImprovedHierarchy.PlayModeSaves";

        [Serializable] private class PersistedSave { public string gid; public string json; public string title; }
        [Serializable] private class PersistedSaves { public List<PersistedSave> items = new List<PersistedSave>(); }

        private static void PersistPlaySaves()
        {
            var p = new PersistedSaves();
            foreach (var s in playSaves) p.items.Add(new PersistedSave { gid = s.GlobalId, json = s.Json, title = s.Title });
            SessionState.SetString(PlaySavesSessionKey, JsonUtility.ToJson(p));
        }

        private static List<PersistedSave> ReadPersistedSaves()
        {
            var raw = SessionState.GetString(PlaySavesSessionKey, "");
            if (string.IsNullOrEmpty(raw)) return new List<PersistedSave>();
            try { return JsonUtility.FromJson<PersistedSaves>(raw).items; }
            catch { return new List<PersistedSave>(); }
        }

        private static Component Resolve(string globalId) =>
            GlobalObjectId.TryParse(globalId, out var gid)
                ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) as Component
                : null;

        /// <summary>Called from the static constructor, i.e. after every domain reload.</summary>
        private static void RecoverPlaySavesAfterReload()
        {
            var pending = ReadPersistedSaves();
            if (pending.Count == 0) return;

            if (EditorApplication.isPlaying)
            {
                // Reloaded mid-play: re-attach to the live objects.
                playSaves.Clear();
                foreach (var p in pending)
                    playSaves.Add(new PlaySave { GlobalId = p.gid, Json = p.json, Title = p.title, Target = Resolve(p.gid) });
            }
            else if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Reloaded between leaving Play Mode and restoring: restore now.
                ScheduleRestore();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    playSaves.Clear();
                    PersistPlaySaves();
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    // Snapshot the values as they are when the user stops, not
                    // when they clicked — later tweaks are kept too.
                    foreach (var s in playSaves)
                        if (s.Target != null) s.Json = EditorJsonUtility.ToJson(s.Target);
                    PersistPlaySaves();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    ScheduleRestore();
                    break;
            }
        }

        // Restores run on the first editor tick after Play Mode has fully ended.
        // Edits made inside the EnteredEditMode callback itself land before Unity
        // has finished restoring the scene, so they aren't registered as real
        // edits and the next Play Mode session silently reverts them.
        // EditorApplication.update is used rather than delayCall because
        // delayCall isn't flushed while the editor is in the background.
        private static void ScheduleRestore()
        {
            EditorApplication.update -= RestoreTick;
            EditorApplication.update += RestoreTick;
        }

        private static void RestoreTick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= RestoreTick;
            RestorePlayModeSaves();
        }

        private static void RestorePlayModeSaves()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // SessionState is the source of truth (the in-memory list may have
            // been wiped by a domain reload on the way out of Play Mode).
            var pending = ReadPersistedSaves();
            SessionState.EraseString(PlaySavesSessionKey);
            playSaves.Clear();
            if (pending.Count == 0) return;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Keep Play Mode Values");

            var kept = new List<string>();
            var lost = new List<string>();
            foreach (var s in pending)
            {
                var obj = Resolve(s.gid);
                if (obj == null) { lost.Add(s.title); continue; }
                Undo.RecordObject(obj, "Keep Play Mode Values");
                EditorJsonUtility.FromJsonOverwrite(s.json, obj);
                if (PrefabUtility.IsPartOfPrefabInstance(obj))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
                MarkChanged(obj);
                kept.Add($"{obj.gameObject.name} › {s.title}");
            }
            Undo.CollapseUndoOperations(group);

            if (kept.Count > 0)
            {
                Debug.Log($"Improved Hierarchy: kept Play Mode values on {kept.Count} component(s):\n  " + string.Join("\n  ", kept));
                IHGui.Notify($"Kept Play Mode values on {kept.Count} component{(kept.Count == 1 ? "" : "s")}", 2.0);
            }
            if (lost.Count > 0)
            {
                Debug.LogWarning($"Improved Hierarchy: couldn't keep values for {lost.Count} component(s) — they only existed at runtime or were deleted:\n  " + string.Join("\n  ", lost));
            }
            RaiseChanged();
        }

        // =========================================================================
        //  GAMEOBJECT HEADER — "Paste N components" bar (only while the buffer has items)
        // =========================================================================

        private static void DrawGameObjectPasteBar(Editor editor)
        {
            if (buffer.Count == 0 || editor == null || editor.targets.Length == 0) return;
            if (!(editor.target is GameObject)) return;

            int n = buffer.Count;
            string names = string.Join("\n", buffer.Select(e => "•  " + e.TypeName));
            var paste = new GUIContent(
                $" Paste {n} component{(n == 1 ? "" : "s")}",
                IHIcons.Get(IHIcons.Paste),
                $"Paste the Multi-Copy Buffer onto {(editor.targets.Length == 1 ? "this GameObject" : $"these {editor.targets.Length} GameObjects")}:\n{names}");

            GUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = Color.Lerp(Color.white, IHTheme.Accent, 0.35f);
            if (GUILayout.Button(paste, GUILayout.Height(22)))
                PasteBuffer(editor.targets.OfType<GameObject>());
            GUI.backgroundColor = prevBg;

            GUILayout.Space(2);
            var winRect = GUILayoutUtility.GetRect(22, 22, GUILayout.Width(22), GUILayout.Height(22));
            if (IHGui.IconButton(winRect, IHIcons.Get("UnityEditor.InspectorWindow"), "Open the Multi-Copy Buffer window", fallback: "≡"))
                MultiCopyBufferWindow.ShowWindow();
            var clearRect = GUILayoutUtility.GetRect(22, 22, GUILayout.Width(22), GUILayout.Height(22));
            if (IHGui.IconButton(clearRect, IHIcons.Get(IHIcons.Clear), "Clear the Multi-Copy Buffer"))
                ClearBuffer();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        // =========================================================================
        //  MENUS
        // =========================================================================

        private const string CtxRoot = "CONTEXT/Component/Improved Hierarchy/";

        [MenuItem(CtxRoot + "Add or Remove from Multi-Copy Buffer")]
        private static void Ctx_ToggleBuffer(MenuCommand cmd)
        {
            if (cmd.context is Component c) ToggleInBuffer(c);
        }

        [MenuItem(CtxRoot + "Copy All Components on This GameObject")]
        private static void Ctx_CopyAll(MenuCommand cmd)
        {
            if (cmd.context is Component c) CopyAllComponents(c.gameObject);
        }

        [MenuItem(CtxRoot + "Paste Multi-Copy Buffer Here")]
        private static void Ctx_Paste(MenuCommand cmd)
        {
            if (cmd.context is Component c) PasteBuffer(new[] { c.gameObject });
        }

        [MenuItem(CtxRoot + "Paste Multi-Copy Buffer Here", true)]
        private static bool Ctx_Paste_Validate() => buffer.Count > 0;

        [MenuItem(CtxRoot + "Keep Play Mode Values")]
        private static void Ctx_TogglePlayModeSave(MenuCommand cmd)
        {
            if (cmd.context is Component c) TogglePlayModeSave(c);
        }

        [MenuItem(CtxRoot + "Keep Play Mode Values", true)]
        private static bool Ctx_TogglePlayModeSave_Validate() => Application.isPlaying;

        // Hierarchy right-click menu (and the main GameObject menu). No MenuCommand
        // parameter, so these run once for the whole selection.
        [MenuItem("GameObject/Improved Hierarchy/Copy All Components", false, 40)]
        private static void Go_CopyAll()
        {
            foreach (var go in Selection.gameObjects) CopyAllComponents(go);
        }

        [MenuItem("GameObject/Improved Hierarchy/Copy All Components", true)]
        private static bool Go_CopyAll_Validate() => Selection.gameObjects.Length > 0;

        [MenuItem("GameObject/Improved Hierarchy/Paste Multi-Copy Buffer", false, 41)]
        private static void Go_Paste() => PasteBuffer(Selection.gameObjects);

        [MenuItem("GameObject/Improved Hierarchy/Paste Multi-Copy Buffer", true)]
        private static bool Go_Paste_Validate() => buffer.Count > 0 && Selection.gameObjects.Length > 0;
    }

    // ---------------------------------------------------------------------------
    //  MULTI-COPY BUFFER WINDOW
    // ---------------------------------------------------------------------------
    public class MultiCopyBufferWindow : EditorWindow
    {
        private Vector2 scroll;

        [MenuItem("Tools/Improved Hierarchy/Multi-Copy Buffer", priority = 1)]
        public static void ShowWindow()
        {
            var w = GetWindow<MultiCopyBufferWindow>();
            w.titleContent = new GUIContent("Multi-Copy Buffer", IHIcons.Get(IHIcons.Copy));
            w.minSize = new Vector2(300, 180);
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            titleContent = new GUIContent("Multi-Copy Buffer", IHIcons.Get(IHIcons.Copy));
            ComponentTools.Changed += Repaint;
            Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            ComponentTools.Changed -= Repaint;
            Selection.selectionChanged -= Repaint;
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove) Repaint();
            var buf = ComponentTools.Buffer;

            // Toolbar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(buf.Count == 1 ? "1 component" : $"{buf.Count} components", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
            {
                if (GUILayout.Button(new GUIContent("Copy Selected", "Add every component of the selected GameObject(s), except Transform"), EditorStyles.toolbarButton))
                    foreach (var go in Selection.gameObjects) ComponentTools.CopyAllComponents(go);
            }
            using (new EditorGUI.DisabledScope(buf.Count == 0))
            {
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton)) ComponentTools.ClearBuffer();
            }
            EditorGUILayout.EndHorizontal();

            if (buf.Count == 0)
            {
                GUILayout.FlexibleSpace();
                var icon = IHIcons.Get(IHIcons.Copy);
                var r = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
                IHGui.DrawIcon(IHGui.CenteredSquare(r, 16), icon, IHTheme.MutedText);
                GUILayout.Label("The buffer is empty", EditorStyles.centeredGreyMiniLabel);
                var hint = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true };
                GUILayout.Label("Click the copy icon on any component header to collect it, then paste everything onto other GameObjects in one go.", hint);
                GUILayout.FlexibleSpace();
                return;
            }

            // Rows
            scroll = EditorGUILayout.BeginScrollView(scroll);
            int removeAt = -1;
            for (int i = 0; i < buf.Count; i++)
            {
                var e = buf[i];
                var row = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                {
                    if (i % 2 == 1) EditorGUI.DrawRect(row, IHTheme.AltRow);
                    if (row.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(row, IHTheme.Hover);
                }

                var iconRect = new Rect(row.x + 6, row.y + 4, 16, 16);
                var typeIcon = e.Type != null ? AssetPreview.GetMiniTypeThumbnail(e.Type) : null;
                IHGui.DrawIcon(iconRect, typeIcon, Color.white);

                var removeRect = new Rect(row.xMax - 26, row.y + 2, 20, 20);
                var pingRect = new Rect(removeRect.x - 22, row.y + 2, 20, 20);
                var textRect = new Rect(iconRect.xMax + 6, row.y, pingRect.x - iconRect.xMax - 10, row.height);

                float titleW = Mathf.Min(IHGui.Styles.RowTitle.CalcSize(new GUIContent(e.TypeName)).x, textRect.width);
                GUI.Label(new Rect(textRect.x, textRect.y, titleW, textRect.height), e.TypeName, IHGui.Styles.RowTitle);
                GUI.Label(new Rect(textRect.x + titleW + 8, textRect.y, textRect.width - titleW - 8, textRect.height),
                          "from " + e.SourceName, IHGui.Styles.RowSubtitle);

                var src = e.GetSource();
                using (new EditorGUI.DisabledScope(src == null))
                {
                    if (IHGui.IconButton(pingRect, IHIcons.Get(IHIcons.Ping), src != null ? $"Ping {e.SourceName}" : "The source object is no longer loaded"))
                        EditorGUIUtility.PingObject(src);
                }
                if (IHGui.IconButton(removeRect, IHIcons.Get(IHIcons.Remove), "Remove from buffer"))
                    removeAt = i;
            }
            EditorGUILayout.EndScrollView();
            if (removeAt >= 0) ComponentTools.RemoveAt(removeAt);

            // Footer
            IHGui.Separator(0);
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(6);
            int sel = Selection.gameObjects.Length;
            using (new EditorGUI.DisabledScope(sel == 0))
            {
                var label = sel == 0 ? " Select GameObjects to paste onto"
                          : sel == 1 ? $" Paste onto {Selection.gameObjects[0].name}"
                          : $" Paste onto {sel} GameObjects";
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = Color.Lerp(Color.white, IHTheme.Accent, 0.35f);
                if (GUILayout.Button(new GUIContent(label, IHIcons.Get(IHIcons.Paste)), GUILayout.Height(26)))
                    ComponentTools.PasteBuffer(Selection.gameObjects);
                GUI.backgroundColor = prevBg;
            }
            GUILayout.Space(6);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);
        }
    }
}
