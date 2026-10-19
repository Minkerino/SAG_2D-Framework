using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ImprovedHierarchy
{
    /// <summary>
    /// Owns ALL per-GameObject icon / colour data, stored externally inside the
    /// <see cref="IconLibrary"/> ScriptableObject (keyed by GlobalObjectId). This
    /// is intentionally NOT a MonoBehaviour — adding a component to user
    /// GameObjects means uninstalling the package leaves "Missing (Mono Script)"
    /// references on every customised object. With external storage, removing
    /// the package only leaves one orphan asset (the library) behind, which is
    /// trivial to delete.
    ///
    /// Legacy <see cref="IconData"/> components (from package versions &lt;= 0.2.7)
    /// are migrated to the library on first read and then removed asynchronously
    /// from the scene. A scene + project-wide migration is also exposed via the
    /// Tools menu so users can do a one-shot pass before uninstalling.
    /// </summary>
    internal static class GameObjectIconStore
    {
        // Per-object cache so hot hierarchy redraws don't keep hammering
        // GlobalObjectId.GetGlobalObjectIdSlow (which is, as documented, slow).
        // Keyed by a version-stable object id (see StableKey).
        private static readonly Dictionary<ulong, GameObjectIconEntry> _cache = new Dictionary<ulong, GameObjectIconEntry>();

        // Track which objects we've already scheduled for component removal so we
        // don't queue the same legacy IconData repeatedly during a redraw.
        private static readonly HashSet<ulong> _pendingLegacyRemoval = new HashSet<ulong>();

        // Version-stable per-object key.
        //   • Unity 6.4+ deprecated Object.GetInstanceID(); the replacement is the
        //     EntityId API. EntityId must NOT be cast to int (the bit layout is an
        //     implementation detail), but EntityId.ToULong() gives a stable raw
        //     value we can use as a dictionary key.
        //   • 2022.3 LTS / Unity 6.0–6.3 don't expose GetEntityId() at all, so we
        //     fall back to GetInstanceID() (not obsolete on those versions).
        private static ulong StableKey(Object o)
        {
    #if UNITY_6000_4_OR_NEWER
            return EntityId.ToULong(o.GetEntityId());
    #else
    #pragma warning disable CS0618 // obsolete on some pre-6.4 versions that lack GetEntityId
            return unchecked((ulong)(uint)o.GetInstanceID());
    #pragma warning restore CS0618
    #endif
        }

        static GameObjectIconStore()
        {
            // Instance IDs are stable within a session but the same GameObject
            // can get a different one after a scene reload or domain reload.
            // Clear the cache aggressively — the lookup itself is cheap, the
            // GlobalObjectId resolution is the bit we're caching.
            EditorSceneManager.sceneOpened += (s, m) => _cache.Clear();
            EditorSceneManager.sceneClosed += s => _cache.Clear();
            EditorSceneManager.sceneSaved += s => _cache.Clear();
            AssemblyReloadEvents.beforeAssemblyReload += () => _cache.Clear();
            EditorApplication.playModeStateChanged += s => _cache.Clear();
        }

        // ---------------------------------------------------------------------
        //  Public read / write API used by the hierarchy drawer + picker window
        // ---------------------------------------------------------------------

        /// <summary>
        /// Returns the entry for this GameObject, or null if it has no custom
        /// icon / colour. Performs a lazy migration from legacy IconData
        /// components on the way through.
        /// </summary>
        public static GameObjectIconEntry Get(GameObject obj)
        {
            if (obj == null) return null;
            ulong id = StableKey(obj);
            if (_cache.TryGetValue(id, out var cached)) return cached;
            var entry = Resolve(obj);
            _cache[id] = entry;
            return entry;
        }

        /// <summary>
        /// Returns an existing entry or creates a new one in the library.
        /// </summary>
        public static GameObjectIconEntry GetOrCreate(GameObject obj)
        {
            if (obj == null) return null;
            var existing = Get(obj);
            if (existing != null) return existing;

            var lib = InspectorSuiteLibrary.LoadOrCreate();
            string gid = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
            var entry = new GameObjectIconEntry { globalObjectId = gid };
            lib.gameObjectIcons.Add(entry);
            EditorUtility.SetDirty(lib);
            _cache[StableKey(obj)] = entry;
            return entry;
        }

        /// <summary>
        /// Marks the library asset dirty after an in-place edit of an entry.
        /// </summary>
        public static void MarkLibraryDirty()
        {
            var lib = InspectorSuiteLibrary.LoadIfExists();
            if (lib != null) EditorUtility.SetDirty(lib);
        }

        /// <summary>
        /// Removes the entry for this GameObject (and any leftover legacy
        /// IconData component).
        /// </summary>
        public static void Reset(GameObject obj)
        {
            if (obj == null) return;
            var lib = InspectorSuiteLibrary.LoadIfExists();
            if (lib != null)
            {
                string gid = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
                int removed = lib.gameObjectIcons.RemoveAll(e => e.globalObjectId == gid);
                if (removed > 0) EditorUtility.SetDirty(lib);
            }
            _cache[StableKey(obj)] = null;

            var legacy = obj.GetComponent<IconData>();
            if (legacy != null) Undo.DestroyObjectImmediate(legacy);
        }

        /// <summary>Clears the in-memory cache (used after bulk migrations).</summary>
        public static void InvalidateAll() => _cache.Clear();

        // ---------------------------------------------------------------------
        //  Lazy migration internals
        // ---------------------------------------------------------------------

        private static GameObjectIconEntry Resolve(GameObject obj)
        {
            var legacy = obj.GetComponent<IconData>();
            var lib = InspectorSuiteLibrary.LoadIfExists();

            // Fast path: no legacy component and no library -> nothing to do.
            if (legacy == null && lib == null) return null;

            string gid = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();

            GameObjectIconEntry entry = null;
            if (lib != null)
            {
                // Linear scan — fine for any realistic project size, and avoids
                // having to keep a parallel dictionary in sync with the
                // serialised list.
                entry = lib.gameObjectIcons.Find(e => e.globalObjectId == gid);
            }

            if (legacy != null)
            {
                if (entry == null)
                {
                    if (lib == null) lib = InspectorSuiteLibrary.LoadOrCreate();
                    entry = new GameObjectIconEntry
                    {
                        globalObjectId = gid,
                        iconColor = legacy.iconColor,
                        internalIconName = legacy.internalIconName ?? "",
                        customIconTexture = legacy.customIconTexture
                    };
                    lib.gameObjectIcons.Add(entry);
                    EditorUtility.SetDirty(lib);
                }
                // Even if a library entry already existed (e.g. user upgraded
                // mid-edit), the legacy component is now redundant. Schedule
                // its removal so the scene becomes clean enough to uninstall.
                ScheduleLegacyRemoval(obj);
            }

            return entry;
        }

        private static void ScheduleLegacyRemoval(GameObject obj)
        {
            ulong id = StableKey(obj);
            if (!_pendingLegacyRemoval.Add(id)) return;

            // Capture the GameObject reference directly rather than re-resolving
            // it from an id later (which would need the obsolete
            // EditorUtility.InstanceIDToObject(int)). Unity's overloaded == safely
            // reports a destroyed object as null.
            EditorApplication.delayCall += () =>
            {
                _pendingLegacyRemoval.Remove(id);
                if (obj == null) return;
                var legacy = obj.GetComponent<IconData>();
                if (legacy == null) return;

                // Undo so the change can be reverted if the user did not expect
                // the migration to mutate the scene.
                Undo.DestroyObjectImmediate(legacy);
                if (obj.scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(obj.scene);
            };
        }

        // ---------------------------------------------------------------------
        //  One-shot migration + cleanup menus
        // ---------------------------------------------------------------------

        [MenuItem("Tools/Improved Hierarchy/Migrate Legacy IconData \u2192 Library", priority = 50)]
        public static void MigrateAllScenes()
        {
            if (!EditorUtility.DisplayDialog(
                    "Migrate Legacy IconData",
                    "This will open every scene in your project, migrate any legacy " +
                    "IconData components into the library, and remove the components " +
                    "from the scenes.\n\n" +
                    "Unsaved changes in currently-open scenes will be saved as part of " +
                    "the migration. Continue?",
                    "Migrate", "Cancel"))
            {
                return;
            }

            int migratedComponents = 0;
            int touchedScenes = 0;

            // 1) Open scenes first — they're already loaded, just process them.
            var openScenes = new List<Scene>();
            for (int s = 0; s < EditorSceneManager.sceneCount; s++)
            {
                var sc = EditorSceneManager.GetSceneAt(s);
                if (sc.isLoaded) openScenes.Add(sc);
            }

            // Make sure we don't lose work — prompt to save dirty open scenes.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            foreach (var sc in openScenes)
            {
                if (MigrateScene(sc, ref migratedComponents))
                {
                    touchedScenes++;
                    EditorSceneManager.MarkSceneDirty(sc);
                    EditorSceneManager.SaveScene(sc);
                }
            }

            // 2) Closed scene assets in the project (skip read-only packages).
            var sceneGuids = AssetDatabase.FindAssets("t:Scene");
            var openPaths = new HashSet<string>();
            foreach (var sc in openScenes) openPaths.Add(sc.path);

            for (int i = 0; i < sceneGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                if (openPaths.Contains(path)) continue;
                if (path.StartsWith("Packages/")) continue;

                EditorUtility.DisplayProgressBar(
                    "Improved Hierarchy",
                    $"Scanning {path}",
                    (float)i / Mathf.Max(1, sceneGuids.Length));

                Scene loaded;
                try
                {
                    loaded = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                }
                catch
                {
                    continue;
                }

                if (MigrateScene(loaded, ref migratedComponents))
                {
                    touchedScenes++;
                    EditorSceneManager.MarkSceneDirty(loaded);
                    EditorSceneManager.SaveScene(loaded);
                }

                try { EditorSceneManager.CloseScene(loaded, true); }
                catch { /* leave it; user will close manually */ }
            }

            EditorUtility.ClearProgressBar();

            InvalidateAll();
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog(
                "Improved Hierarchy",
                $"Migrated {migratedComponents} IconData component(s) across {touchedScenes} scene(s).\n\n" +
                "Your customisations now live in Assets/Editor/InspectorIconLibrary.asset, " +
                "and your scenes no longer carry the IconData MonoBehaviour. " +
                "Removing the Improved Hierarchy package will no longer leave 'Missing Script' references.",
                "OK");
        }

        private static bool MigrateScene(Scene scene, ref int migratedCount)
        {
            if (!scene.IsValid()) return false;

            bool touched = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = t.gameObject;
                    var legacy = go.GetComponent<IconData>();
                    if (legacy == null) continue;

                    // Migrate (via the same Resolve path that the cache uses)
                    // and then force synchronous removal — we don't want to wait
                    // for delayCall when the user explicitly asked for cleanup.
                    Resolve(go);
                    legacy = go.GetComponent<IconData>();
                    if (legacy != null)
                    {
                        Object.DestroyImmediate(legacy, true);
                    }

                    migratedCount++;
                    touched = true;
                }
            }
            return touched;
        }

        [MenuItem("Tools/Improved Hierarchy/Clean Missing Scripts in Open Scenes", priority = 51)]
        public static void CleanMissingScriptsOpenScenes()
        {
            int removed = 0;
            int touchedScenes = 0;
            for (int s = 0; s < EditorSceneManager.sceneCount; s++)
            {
                var scene = EditorSceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                bool touched = false;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        var go = t.gameObject;
                        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                        if (count > 0)
                        {
                            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                            removed += count;
                            touched = true;
                        }
                    }
                }

                if (touched)
                {
                    touchedScenes++;
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }

            Debug.Log($"[Improved Hierarchy] Removed {removed} missing script reference(s) across {touchedScenes} open scene(s).");
        }
    }
}
