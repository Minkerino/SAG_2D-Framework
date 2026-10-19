using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;

namespace ImprovedHierarchy
{
    /// <summary>
    /// Fallback editor for every MonoBehaviour without its own custom editor.
    /// Adds [Tab] / [Foldout] layout and [Button] methods; scripts that use none
    /// of these get Unity's default inspector untouched. Per-component Copy /
    /// Keep Values buttons live on the component header (ComponentHeaderButtons).
    /// </summary>
    [CustomEditor(typeof(MonoBehaviour), true, isFallback = true)]
    [CanEditMultipleObjects]
    public class GlobalInspectorEditor : Editor
    {
        private static readonly Dictionary<string, int> tabState = new Dictionary<string, int>();
        private static readonly Dictionary<string, bool> foldoutState = new Dictionary<string, bool>();

        private sealed class TypeInfo
        {
            public readonly Dictionary<string, TabAttribute> Tabs = new Dictionary<string, TabAttribute>();
            public readonly Dictionary<string, FoldoutAttribute> Foldouts = new Dictionary<string, FoldoutAttribute>();
            public readonly List<(MethodInfo method, string label)> Buttons = new List<(MethodInfo, string)>();
            public bool HasLayout => Tabs.Count > 0 || Foldouts.Count > 0;
        }

        private static readonly Dictionary<Type, TypeInfo> typeCache = new Dictionary<Type, TypeInfo>();

        private static TypeInfo GetInfo(Type type)
        {
            if (typeCache.TryGetValue(type, out var info)) return info;
            info = new TypeInfo();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            // Walk the whole hierarchy — private fields/methods of base classes
            // are not returned by a plain GetFields/GetMethods on the subclass.
            var chain = new List<Type>();
            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType) chain.Add(t);
            chain.Reverse(); // base first => declaration order matches the inspector

            var seenMethods = new HashSet<string>();
            foreach (var t in chain)
            {
                foreach (var f in t.GetFields(flags))
                {
                    var tab = f.GetCustomAttribute<TabAttribute>();
                    if (tab != null) info.Tabs[f.Name] = tab;
                    var fold = f.GetCustomAttribute<FoldoutAttribute>();
                    if (fold != null) info.Foldouts[f.Name] = fold;
                }
                foreach (var m in t.GetMethods(flags))
                {
                    var attr = m.GetCustomAttribute<ButtonAttribute>();
                    if (attr == null || m.GetParameters().Length != 0 || !seenMethods.Add(m.Name)) continue;
                    info.Buttons.Add((m, string.IsNullOrEmpty(attr.Name) ? ObjectNames.NicifyVariableName(m.Name) : attr.Name));
                }
            }
            typeCache[type] = info;
            return info;
        }

        // Unity 6.4 introduced the EntityId-based identity API; fall back to
        // GetInstanceID() on 2022.3 LTS / Unity 6.0–6.3.
        private string Key(string suffix)
    #if UNITY_6000_4_OR_NEWER
            => $"{target.GetType().FullName}.{target.GetEntityId()}.{suffix}";
    #else
    #pragma warning disable CS0618 // obsolete on some pre-6.4 versions that lack GetEntityId
            => $"{target.GetType().FullName}.{target.GetInstanceID()}.{suffix}";
    #pragma warning restore CS0618
    #endif

        public override void OnInspectorGUI()
        {
            if (target == null) { base.OnInspectorGUI(); return; }
            var info = GetInfo(target.GetType());

            if (info.HasLayout)
            {
                serializedObject.Update();
                DrawLayoutBody(info);
                serializedObject.ApplyModifiedProperties();
            }
            else
            {
                DrawDefaultInspector();
            }

            DrawButtons(info);
        }

        private void DrawLayoutBody(TypeInfo info)
        {
            // Everything Unity would show, in Unity's order (includes inherited
            // private [SerializeField]s, excludes [HideInInspector]).
            var props = new List<SerializedProperty>();
            var it = serializedObject.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false) props.Add(it.Copy());

            // Script reference, read-only, like the default inspector.
            var script = props.FirstOrDefault(p => p.propertyPath == "m_Script");
            if (script != null)
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(script);
                props.Remove(script);
            }

            // Tabs (declaration order).
            var tabOrder = new List<string>();
            foreach (var p in props)
                if (info.Tabs.TryGetValue(p.name, out var t) && !tabOrder.Contains(t.Name)) tabOrder.Add(t.Name);

            string activeTab = null;
            if (tabOrder.Count > 0)
            {
                string tabKey = Key("tab");
                if (!tabState.TryGetValue(tabKey, out int current)) current = 0;
                current = Mathf.Clamp(current, 0, tabOrder.Count - 1);
                EditorGUILayout.Space(2);
                current = GUILayout.Toolbar(current, tabOrder.ToArray(), GUILayout.Height(22));
                tabState[tabKey] = current;
                activeTab = tabOrder[current];
                EditorGUILayout.Space(4);
            }

            // Fields, grouped into foldouts. Untabbed fields are always visible.
            string currentFoldout = null;
            bool foldoutOpen = true;
            foreach (var p in props)
            {
                if (activeTab != null && info.Tabs.TryGetValue(p.name, out var tab) && tab.Name != activeTab) continue;

                if (info.Foldouts.TryGetValue(p.name, out var fold))
                {
                    if (currentFoldout != fold.Name)
                    {
                        if (currentFoldout != null) EditorGUI.indentLevel--;
                        currentFoldout = fold.Name;
                        string foldKey = Key("fold." + fold.Name);
                        if (!foldoutState.TryGetValue(foldKey, out foldoutOpen)) foldoutOpen = true;
                        foldoutOpen = EditorGUILayout.BeginFoldoutHeaderGroup(foldoutOpen, fold.Name);
                        EditorGUILayout.EndFoldoutHeaderGroup();
                        foldoutState[foldKey] = foldoutOpen;
                        EditorGUI.indentLevel++;
                    }
                    if (!foldoutOpen) continue;
                }
                else if (currentFoldout != null)
                {
                    EditorGUI.indentLevel--;
                    currentFoldout = null;
                    EditorGUILayout.Space(2);
                }

                EditorGUILayout.PropertyField(p, true);
            }
            if (currentFoldout != null) EditorGUI.indentLevel--;
        }

        private void DrawButtons(TypeInfo info)
        {
            if (info.Buttons.Count == 0) return;
            EditorGUILayout.Space(6);
            foreach (var (m, label) in info.Buttons)
            {
                if (!GUILayout.Button(label, GUILayout.Height(24))) continue;
                foreach (var t in targets)
                {
                    try { m.Invoke(m.IsStatic ? null : t, null); }
                    catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e, t); }
                    catch (Exception e) { Debug.LogException(e, t); }
                    if (m.IsStatic) break; // static methods run once, not once per target
                }
            }
        }
    }
}
