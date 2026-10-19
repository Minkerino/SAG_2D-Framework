using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ImprovedHierarchy
{
    /// <summary>
    /// Hierarchy window decorations:
    ///   • per-GameObject colour gradient + custom icon (right-click the icon),
    ///   • tree connector lines,
    ///   • clickable component-icon strip on the right,
    ///   • scene selector chevron next to each scene name.
    /// All colours / icons / buttons come from <see cref="IHTheme"/>,
    /// <see cref="IHIcons"/> and <see cref="IHGui"/>.
    /// </summary>
    [InitializeOnLoad]
    public static class InspectorEditorSuite
    {
        static InspectorEditorSuite()
        {
            // Unity 6.4 introduced EntityId + a new hierarchy callback that uses
            // it. On 2022.3 LTS / Unity 6.0–6.3, fall back to the int-based
            // callback. Some of those versions already mark the int-based APIs
            // obsolete (CS0618) while the EntityId callback isn't complete yet,
            // so the warning is suppressed on this fallback path only.
    #if UNITY_6000_4_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyGUI;
    #else
    #pragma warning disable CS0618
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
    #pragma warning restore CS0618
    #endif
        }

        /// <summary>Skin-aware built-in icon lookup (kept for API compatibility).</summary>
        public static Texture GetIconSafe(string name) => IHIcons.Get(name);

        /// <summary>Raised whenever row decorations should be rebuilt (colours, icons, toggles changed).</summary>
        public static event Action HierarchyRepaintRequested;

        /// <summary>
        /// Repaints every Hierarchy window: the classic IMGUI one directly, and
        /// the Unity 6.6+ UI Toolkit one through <see cref="HierarchyRepaintRequested"/>.
        /// </summary>
        public static void RepaintHierarchy()
        {
            EditorApplication.RepaintHierarchyWindow();
            HierarchyRepaintRequested?.Invoke();
        }

    #if UNITY_6000_4_OR_NEWER
        private static void OnHierarchyGUI(EntityId id, Rect rect)
        {
            if (EditorUtility.EntityIdToObject(id) is GameObject obj) { DrawGameObjectRow(obj, rect); return; }
            // Scene header rows pass the scene handle as the id.
            ulong raw = EntityId.ToULong(id);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.handle.GetRawData() == raw) { DrawSceneRow(s, rect); return; }
            }
        }
    #else
    #pragma warning disable CS0618 // int-based ids: obsolete on some pre-6.4 versions, see the static constructor
        private static void OnHierarchyGUI(int instanceId, Rect rect)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is GameObject obj) { DrawGameObjectRow(obj, rect); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.handle == instanceId) { DrawSceneRow(s, rect); return; }
            }
        }
    #pragma warning restore CS0618
    #endif

        private static void DrawGameObjectRow(GameObject obj, Rect rect)
        {
            // Stripes + lines first so everything else paints on top.
            DrawStripe(rect, Selection.Contains(obj));
            if (HierarchyLinesEnabled) DrawHierarchyLines(obj, rect);
            DrawRowDecoration(obj, rect);
            DrawComponentIconStrip(obj, rect);
        }

        // =========================================================================
        //  STRIPED ROWS — alternate lighter / darker rows so rows are easy to follow
        // =========================================================================

        private const string StripedRowsPrefKey = "ImprovedHierarchy.StripedRows";
        private const string StripedRowsMenu = "Tools/Improved Hierarchy/Striped Rows";

        public static bool StripedRowsEnabled
        {
            get => EditorPrefs.GetBool(StripedRowsPrefKey, true);
            set => EditorPrefs.SetBool(StripedRowsPrefKey, value);
        }

        [MenuItem(StripedRowsMenu, priority = 21)]
        private static void ToggleStripedRows()
        {
    #if UNITY_6000_6_OR_NEWER
            // The 6.6 Hierarchy has built-in alternating rows: drive that instead
            // of drawing a second set of stripes on top.
            if (HierarchyV2.IsActive && HierarchyV2.UnityStripes.HasValue)
            {
                HierarchyV2.UnityStripes = !HierarchyV2.UnityStripes.Value;
                return;
            }
    #endif
            StripedRowsEnabled = !StripedRowsEnabled;
            RepaintHierarchy();
        }

        [MenuItem(StripedRowsMenu, true)]
        private static bool ToggleStripedRowsValidate()
        {
            bool on = StripedRowsEnabled;
    #if UNITY_6000_6_OR_NEWER
            if (HierarchyV2.IsActive && HierarchyV2.UnityStripes.HasValue) on = HierarchyV2.UnityStripes.Value;
    #endif
            Menu.SetChecked(StripedRowsMenu, on);
            return true;
        }

        // Overlay for the "lighter" rows. Kept subtle so text contrast is unchanged.
        private static Color StripeOverlay => EditorGUIUtility.isProSkin
            ? new Color(1f, 1f, 1f, 0.045f)
            : new Color(0f, 0f, 0f, 0.055f);

        private static bool IsStripedRow(Rect rect) =>
            StripedRowsEnabled && (Mathf.RoundToInt(rect.y / Mathf.Max(1f, rect.height)) & 1) == 1;

        private static void DrawStripe(Rect rect, bool selected)
        {
            if (selected || Event.current.type != EventType.Repaint || !IsStripedRow(rect)) return;
            // Full width, including the visibility / picking gutter on the left.
            EditorGUI.DrawRect(new Rect(0f, rect.y, rect.xMax, rect.height), StripeOverlay);
        }

        /// <summary>Exact background colour of a row (native colour + stripe), used when covering the native icon.</summary>
        private static Color RowBackground(Rect rect, bool selected, bool focused)
        {
            Color bg = IHTheme.RowColor(selected, focused, hover: false);
            if (!selected && IsStripedRow(rect))
            {
                var o = StripeOverlay;
                bg = Color.Lerp(bg, new Color(o.r, o.g, o.b, 1f), o.a);
            }
            bg.a = 1f;
            return bg;
        }

        // =========================================================================
        //  ROW COLOUR + ICON + LABEL
        // =========================================================================

        // Overall strength of the row tint (multiplies the colour's own alpha).
        internal const float TintStrength = 0.60f;

        // Gradient shape (parametric 0..1 across the row): short soft fade-in,
        // a plateau covering the icon + name, then a long fade-out so the
        // component strip on the right sits over a faint tint.
        private const float GradientFadeInEnd = 0.04f;
        private const float GradientPlateauEnd = 0.45f;

        private static float SampleGradient(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < GradientFadeInEnd) return Mathf.SmoothStep(0.35f, 1f, t / GradientFadeInEnd);
            if (t < GradientPlateauEnd) return 1f;
            return Mathf.SmoothStep(1f, 0f, (t - GradientPlateauEnd) / (1f - GradientPlateauEnd));
        }

        private static Texture2D _gradientTex;
        internal static Texture2D GradientTexture
        {
            get
            {
                if (_gradientTex != null) return _gradientTex;
                const int w = 256;
                _gradientTex = new Texture2D(w, 1, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
                var px = new Color32[w];
                for (int x = 0; x < w; x++)
                    px[x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(SampleGradient(x / (float)(w - 1)) * 255f));
                _gradientTex.SetPixels32(px);
                _gradientTex.Apply(false, true);
                return _gradientTex;
            }
        }

        private static bool HierarchyHasFocus
        {
            get
            {
                var f = EditorWindow.focusedWindow;
                return f != null && f.GetType().Name == "SceneHierarchyWindow";
            }
        }

        private static void DrawRowDecoration(GameObject obj, Rect rect)
        {
            var e = Event.current;
            var data = GameObjectIconStore.Get(obj);
            Rect iconRect = new Rect(rect.x, rect.y, rect.height, rect.height);

            // --- Input on the GameObject icon -----------------------------------
            if (iconRect.Contains(e.mousePosition))
            {
                if (e.type == EventType.ContextClick)
                {
                    IconPickerWindow.Show(iconRect, obj);
                    e.Use();
                    return;
                }
                if (e.type == EventType.MouseDown && e.button == 0 && e.alt)
                {
                    EditorUtility.OpenPropertyEditor(obj);
                    e.Use();
                    return;
                }
            }
            if (e.type != EventType.Repaint) return;

            bool tinted = data != null && !IHTheme.IsUnset(data.iconColor);
            Texture custom = null;
            if (data != null)
            {
                if (data.customIconTexture != null) custom = data.customIconTexture;
                else if (!string.IsNullOrEmpty(data.internalIconName)) custom = IHIcons.Get(data.internalIconName);
            }
            if (custom == null) custom = GetAutoIcon(obj);
            if (!tinted && custom == null) return; // native row stays untouched

            bool selected = Selection.Contains(obj);
            bool focused = HierarchyHasFocus;
            Color rowBg = RowBackground(rect, selected, focused);

            // 1. Hide Unity's icon when we're replacing it.
            if (custom != null) EditorGUI.DrawRect(iconRect, rowBg);

            // 2. Colour gradient across the row (over icon + name).
            if (tinted)
            {
                var prev = GUI.color;
                GUI.color = new Color(data.iconColor.r, data.iconColor.g, data.iconColor.b, data.iconColor.a * TintStrength);
                GUI.DrawTexture(new Rect(rect.x - 2f, rect.y, rect.xMax - rect.x + 2f, rect.height), GradientTexture, ScaleMode.StretchToFill);
                GUI.color = prev;
            }

            // 3. Icon on top at its true colours.
            Texture icon = custom != null ? custom : NativeIcon(obj, selected && focused);
            IHGui.DrawIcon(iconRect, icon, obj.activeInHierarchy ? Color.white : new Color(1f, 1f, 1f, 0.5f));

            // 4. Name on top so the tint never washes out the text.
            if (tinted) DrawLabel(obj, rect, data.iconColor, rowBg, selected, focused);
        }

        private static Texture NativeIcon(GameObject obj, bool selectedFocused)
        {
            var tex = AssetPreview.GetMiniThumbnail(obj);
            if (selectedFocused && tex != null && tex.name.EndsWith(" Icon", StringComparison.Ordinal))
            {
                // Unity swaps to the white "On" variant on focused selections.
                var on = IHIcons.Get(IHIcons.Canonical(tex.name).Replace(" Icon", " On Icon"));
                if (on != null) return on;
            }
            return tex;
        }

        private static GUIStyle _labelStyle;

        private static void DrawLabel(GameObject obj, Rect rect, Color tint, Color rowBg, bool selected, bool focused)
        {
            if (_labelStyle == null)
                _labelStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), richText = false, wordWrap = false };

            // Native colour for this row (normal / prefab / broken prefab / inactive / selected).
            string styleName =
                PrefabUtility.IsPrefabAssetMissing(obj) ? "PR BrokenPrefabLabel" :
                PrefabUtility.IsPartOfPrefabInstance(obj) ? (obj.activeInHierarchy ? "PR PrefabLabel" : "PR DisabledPrefabLabel") :
                (obj.activeInHierarchy ? "PR Label" : "PR DisabledLabel");
            var native = GUI.skin.FindStyle(styleName) ?? EditorStyles.label;
            Color text = selected && focused ? native.onFocused.textColor : native.normal.textColor;
            if (text.a <= 0f) text = IHTheme.Text;

            if (!(selected && focused)) text = ContrastText(rowBg, tint, text) ?? text;
            _labelStyle.normal.textColor = text;
            _labelStyle.font = native.font;
            _labelStyle.fontStyle = native.fontStyle;

            float textX = rect.x + rect.height + 2f;
            float reserve = GetStripReserve(obj, rect);
            var textRect = new Rect(textX, rect.y, Mathf.Max(0, rect.xMax - textX - reserve), rect.height);
            GUI.Label(textRect, obj.name, _labelStyle);
        }

        /// <summary>
        /// Keeps the name readable over a tinted row: flips light text to dark over
        /// bright tints (yellow, cyan, lime…) and vice versa on the Personal skin.
        /// Returns null when the native colour already has enough contrast.
        /// </summary>
        internal static Color? ContrastText(Color rowBg, Color tint, Color text)
        {
            Color bg = Color.Lerp(rowBg, tint, Mathf.Clamp01(tint.a * TintStrength));
            float bgL = IHTheme.Luminance(bg);
            float textL = IHTheme.Luminance(text);
            if (bgL > 0.52f && textL > 0.5f) return new Color(0.06f, 0.06f, 0.06f, text.a < 1f ? 0.65f : 1f);
            if (bgL < 0.30f && textL < 0.5f) return new Color(0.92f, 0.92f, 0.92f, text.a < 1f ? 0.65f : 1f);
            return null;
        }

        private static Texture GetAutoIcon(GameObject obj)
        {
            if (obj.GetComponent<Camera>()) return IHIcons.Get("Camera Icon");
            if (obj.GetComponent<Light>()) return IHIcons.Get("Light Icon");
            if (obj.GetComponent<AudioSource>()) return IHIcons.Get("AudioSource Icon");
            return null;
        }

        // =========================================================================
        //  COMPONENT ICON STRIP (right side of each row)
        // =========================================================================
        // Identical component types are grouped (twenty MeshRenderers ⇒ one icon +
        // count badge). The strip only uses the space left after the name, so on
        // narrow windows it drops slots (then hides) instead of covering the name.

        private const int StripMaxSlots = 8;
        private const float StripStride = 18f;
        private const float StripButton = 16f;
        private const float StripMinName = 32f;
        private const float StripNameGap = 8f;

        private struct ComponentGroup
        {
            public Type Type;              // null => missing scripts
            public string Title;
            public List<Component> Instances;
            public int MissingCount;
            public Texture Icon;
        }

        private struct StripLayout
        {
            public int VisibleSlots;
            public bool UseOverflow;
            public float Width;
        }

        private static GUIStyle _measureStyle;
        private static readonly GUIContent _tmp = new GUIContent();

        private static StripLayout ComputeStripLayout(GameObject obj, Rect rect, int groupCount)
        {
            var layout = new StripLayout();
            if (groupCount <= 0) return layout;

            if (_measureStyle == null) _measureStyle = new GUIStyle(EditorStyles.label) { wordWrap = false, richText = false };
            _tmp.text = obj.name;
            float textX = rect.x + rect.height + 2f;
            float nameRight = textX + _measureStyle.CalcSize(_tmp).x;
            float stripLeft = Mathf.Max(textX + StripMinName, nameRight) + StripNameGap;

            float available = rect.xMax - stripLeft;
            int maxByWidth = available <= 0f ? 0 : Mathf.FloorToInt(available / StripStride);
            if (maxByWidth <= 0) return layout;

            int visible = Mathf.Min(Mathf.Min(groupCount, StripMaxSlots), maxByWidth);
            layout.UseOverflow = visible < groupCount;
            layout.VisibleSlots = visible;
            layout.Width = StripStride * visible + 4f;
            return layout;
        }

        private static List<ComponentGroup> BuildGroups(GameObject obj)
        {
            var result = new List<ComponentGroup>();
            var components = obj.GetComponents<Component>();
            int missing = 0;
            var drawable = new List<(int idx, Component c)>();
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) { missing++; continue; }
                if (c is Transform || c is IconData) continue;
                drawable.Add((i, c));
            }

            // Right-to-left visual order = Unity's higher component indices first.
            result.AddRange(drawable
                .GroupBy(t => t.c.GetType())
                .OrderByDescending(g => g.Max(t => t.idx))
                .Select(g =>
                {
                    var inst = g.OrderBy(t => t.idx).Select(t => t.c).ToList();
                    return new ComponentGroup
                    {
                        Type = g.Key,
                        Title = ObjectNames.GetInspectorTitle(inst[0]),
                        Instances = inst,
                        Icon = AssetPreview.GetMiniThumbnail(inst[0]),
                    };
                })
                .Where(g => g.Icon != null));

            // Missing scripts are always the right-most slot so they're never hidden.
            if (missing > 0)
                result.Insert(0, new ComponentGroup { MissingCount = missing, Instances = new List<Component>(), Icon = IHIcons.Get(IHIcons.Warning) });

            return result;
        }

        private static float GetStripReserve(GameObject obj, Rect rect)
        {
            var groups = BuildGroups(obj);
            return groups.Count == 0 ? 0f : ComputeStripLayout(obj, rect, groups.Count).Width;
        }

        // Enabled state of a group: 1 = all enabled, 0 = all disabled, -1 = not toggleable / mixed.
        private static int GroupEnabledState(ComponentGroup g)
        {
            int state = -2;
            foreach (var c in g.Instances)
            {
                int s = EditorUtility.GetObjectEnabled(c);
                if (s < 0) return -1;
                if (state == -2) state = s;
                else if (state != s) return -1;
            }
            return state == -2 ? -1 : state;
        }

        private static void DrawComponentIconStrip(GameObject obj, Rect rect)
        {
            var groups = BuildGroups(obj);
            if (groups.Count == 0) return;
            DrawStrip(obj, groups, ComputeStripLayout(obj, rect, groups.Count), rect);
        }

        /// <summary>
        /// Draws the strip right-aligned inside <paramref name="area"/>, the free space
        /// after the name (used by the Unity 6.6+ Hierarchy, which lays the name out itself).
        /// </summary>
        internal static void DrawComponentStripInArea(GameObject obj, Rect area)
        {
            var groups = BuildGroups(obj);
            if (groups.Count == 0) return;
            int visible = Mathf.Min(Mathf.Min(groups.Count, StripMaxSlots), Mathf.FloorToInt(area.width / StripStride));
            var layout = new StripLayout { VisibleSlots = visible, UseOverflow = visible < groups.Count, Width = StripStride * visible + 4f };
            DrawStrip(obj, groups, layout, area);
        }

        private static void DrawStrip(GameObject obj, List<ComponentGroup> groups, StripLayout layout, Rect rect)
        {
            if (layout.VisibleSlots <= 0) return;

            var e = Event.current;
            float x = rect.xMax - StripStride;
            float y = rect.y + (rect.height - StripButton) * 0.5f;
            bool playing = Application.isPlaying;

            for (int slot = 0; slot < layout.VisibleSlots; slot++)
            {
                var r = new Rect(x, y, StripButton, StripButton);
                bool hover = r.Contains(e.mousePosition);

                // Overflow summary slot.
                if (layout.UseOverflow && slot == layout.VisibleSlots - 1)
                {
                    int hiddenTypes = groups.Count - slot;
                    int hiddenInst = 0;
                    for (int h = slot; h < groups.Count; h++) hiddenInst += Math.Max(1, groups[h].Instances.Count);
                    IHGui.DrawIconButton(r, null, false, hover, "⋯");
                    IHGui.CountBadge(r, hiddenInst);
                    IHGui.Tooltip(r, $"{hiddenTypes} more component type{(hiddenTypes > 1 ? "s" : "")} — click to select and see them all in the Inspector.");
                    EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                    if (IHGui.Clicked(r)) { Selection.activeObject = obj; EditorGUIUtility.PingObject(obj); }
                    break;
                }

                var g = groups[slot];

                if (g.Type == null)
                {
                    // Missing scripts.
                    if (e.type == EventType.Repaint)
                    {
                        if (hover) IHGui.RoundedRect(r, IHTheme.Hover);
                        IHGui.DrawIcon(IHGui.CenteredSquare(r, 14f), g.Icon, Color.white);
                    }
                    IHGui.CountBadge(r, g.MissingCount);
                    IHGui.Tooltip(r, $"{g.MissingCount} missing script{(g.MissingCount > 1 ? "s" : "")} on {obj.name}.\nClick to select the GameObject.");
                    EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                    if (IHGui.Clicked(r)) Selection.activeObject = obj;
                    x -= StripStride;
                    continue;
                }

                int enabled = GroupEnabledState(g);
                bool kept = playing && g.Instances.Any(ComponentTools.HasPlayModeSave);

                if (e.type == EventType.Repaint)
                {
                    if (hover) IHGui.RoundedRect(r, IHTheme.Hover);
                    IHGui.DrawIcon(IHGui.CenteredSquare(r, 14f), g.Icon,
                        enabled == 0 ? new Color(1f, 1f, 1f, 0.3f) : Color.white);
                    if (kept)
                        IHGui.RoundedRect(new Rect(r.x + 3f, r.yMax - 1f, r.width - 6f, 2f), IHTheme.Accent, 1f);
                }
                IHGui.CountBadge(r, g.Instances.Count);
                IHGui.Tooltip(r, BuildGroupTooltip(g, enabled, kept));
                EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

                if (IHGui.Clicked(r))
                {
                    var first = g.Instances[0];
                    if (e.alt) EditorUtility.OpenPropertyEditor(first);
                    else if (e.shift) Selection.activeObject = first;
                    else if (enabled >= 0 || g.Instances.Any(c => EditorUtility.GetObjectEnabled(c) >= 0))
                    {
                        var toggleable = g.Instances.Where(c => EditorUtility.GetObjectEnabled(c) >= 0).ToArray();
                        bool target = enabled != 1;
                        Undo.RecordObjects(toggleable, target ? "Enable Component" : "Disable Component");
                        foreach (var c in toggleable) EditorUtility.SetObjectEnabled(c, target);
                    }
                    else
                    {
                        Selection.activeObject = obj;
                    }
                }

                x -= StripStride;
            }
        }

        private static string BuildGroupTooltip(ComponentGroup g, int enabled, bool kept)
        {
            int n = g.Instances.Count;
            string head = n > 1 ? $"{g.Title}  ×{n}" : g.Title;
            if (enabled == 0) head += "  (disabled)";
            if (kept) head += "\nKeeping Play Mode values";
            string click = enabled >= 0 ? (enabled == 1 ? "Click: disable" : "Click: enable") : "Click: select GameObject";
            return head + "\n\n" + click + "  ·  Shift: select component  ·  Alt: open in its own window";
        }

        // =========================================================================
        //  SCENE ROW — scene selector chevron right after the scene name
        // =========================================================================

        private const string InlineSceneRowControlsPrefKey = "InspectorSuite.InlineSceneRowControls";
        private const string InlineSceneMenu = "Tools/Improved Hierarchy/Inline Scene Selector";

        internal static bool InlineSceneRowControlsEnabled
        {
            get => EditorPrefs.GetBool(InlineSceneRowControlsPrefKey, true);
            set => EditorPrefs.SetBool(InlineSceneRowControlsPrefKey, value);
        }

        [MenuItem(InlineSceneMenu, priority = 22)]
        private static void ToggleInlineSceneControls()
        {
            InlineSceneRowControlsEnabled = !InlineSceneRowControlsEnabled;
            RepaintHierarchy();
        }

        [MenuItem(InlineSceneMenu, true)]
        private static bool ToggleInlineSceneControlsValidate()
        {
            Menu.SetChecked(InlineSceneMenu, InlineSceneRowControlsEnabled);
            return true;
        }

        private static GUIStyle _sceneNameStyle;

        private static void DrawSceneRow(Scene scene, Rect rect)
        {
            if (!InlineSceneRowControlsEnabled) return;

            if (_sceneNameStyle == null) _sceneNameStyle = new GUIStyle(EditorStyles.boldLabel);
            string label = string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;
            if (scene.isDirty) label += "*";
            float nameWidth = _sceneNameStyle.CalcSize(new GUIContent(label)).x;

            // Scene row: [scene icon 16] [2px] [bold name] — chevron follows the name.
            float x = rect.x + rect.height + 2f + nameWidth + 4f;
            var r = new Rect(x, rect.y, 18f, rect.height);
            if (r.xMax > rect.xMax - 20f) return; // no room (very narrow window)
            DrawSceneChevron(r);
        }

        /// <summary>The scene-selector ▾ button (shared by both Hierarchy implementations).</summary>
        internal static void DrawSceneChevron(Rect r)
        {
            if (IHGui.IconButton(r, IHIcons.Get(IHIcons.Dropdown), "Switch scene…  (Ctrl+Shift+O)", fallback: "▾"))
                SceneSelectorPopup.ShowAt(r);

            if (Event.current.type == EventType.Repaint)
            {
                // Remember where the chevron is so the Ctrl+Shift+O shortcut can
                // anchor the same popup here.
                try
                {
                    var tl = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y));
                    SceneSelectorMenu.LastHierarchyChevronScreenRect = new Rect(tl.x, tl.y, r.width, r.height);
                }
                catch { /* outside a GUI scope */ }
            }
        }

        // =========================================================================
        //  TREE LINES — connectors from parents to children
        // =========================================================================

        private const string HierarchyLinesPrefKey = "InspectorSuite.HierarchyLines";
        private const string HierarchyLinesMenu = "Tools/Improved Hierarchy/Hierarchy Lines";
        private const float HierarchyIndentWidth = 14f;

        public static bool HierarchyLinesEnabled
        {
            get => EditorPrefs.GetBool(HierarchyLinesPrefKey, true);
            set => EditorPrefs.SetBool(HierarchyLinesPrefKey, value);
        }

        [MenuItem(HierarchyLinesMenu, priority = 20)]
        private static void ToggleHierarchyLines()
        {
            HierarchyLinesEnabled = !HierarchyLinesEnabled;
            RepaintHierarchy();
        }

        [MenuItem(HierarchyLinesMenu, true)]
        private static bool ToggleHierarchyLinesValidate()
        {
            Menu.SetChecked(HierarchyLinesMenu, HierarchyLinesEnabled);
            return true;
        }

        // Opaque, so overlapping segments never show seams.
        internal static Color HierarchyLineColor =>
            EditorGUIUtility.isProSkin
                ? new Color32(102, 102, 102, 255)
                : new Color32(140, 140, 140, 255);

        // Snap to physical pixels. At 125% / 150% display scaling, 1-point lines at
        // fractional positions were rasterised unevenly, and each row's rail
        // segment rounded differently — which read as a dotted line. Snapping
        // both ends of every segment makes adjacent rows share exact edges.
        private static float Snap(float v)
        {
            float ppp = EditorGUIUtility.pixelsPerPoint;
            return Mathf.Round(v * ppp) / ppp;
        }

        private static float LineWidth => Mathf.Max(1f, Mathf.Round(EditorGUIUtility.pixelsPerPoint)) / EditorGUIUtility.pixelsPerPoint;

        private static void VLine(float x, float y0, float y1, Color c)
        {
            float top = Snap(y0), bottom = Snap(y1);
            if (bottom > top) EditorGUI.DrawRect(new Rect(Snap(x), top, LineWidth, bottom - top), c);
        }

        private static void HLine(float x0, float x1, float y, Color c)
        {
            float left = Snap(x0), right = Snap(x1);
            if (right > left) EditorGUI.DrawRect(new Rect(left, Snap(y), right - left, LineWidth), c);
        }

        private static bool IsLastSibling(Transform t)
        {
            if (t.parent != null) return t.GetSiblingIndex() == t.parent.childCount - 1;
            var scene = t.gameObject.scene;
            return !scene.IsValid() || t.GetSiblingIndex() == scene.rootCount - 1;
        }

        private static void DrawHierarchyLines(GameObject obj, Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;

            Transform t = obj.transform;
            if (t.parent == null) return; // top-level objects have no parent to connect to

            Color col = HierarchyLineColor;
            float indent = HierarchyIndentWidth;
            float yMid = Mathf.Round(rect.y + rect.height * 0.5f);

            // The rail linking this node to its parent sits under the PARENT's
            // column, so it never hides beneath this node's own foldout arrow.
            float railX = Mathf.Round(rect.x - indent * 1.5f);
            bool last = IsLastSibling(t);

            // Elbow: into the icon for leaves; for nodes with children it stops
            // short of the node's own foldout arrow instead of running under it.
            float elbowEnd = t.childCount > 0 ? rect.x - indent + 1f : rect.x - 2f;
            HLine(railX, elbowEnd, yMid, col);
            // Vertical rail: full height, or down to the elbow for the last child.
            VLine(railX, rect.y, last ? yMid + LineWidth : rect.yMax, col);

            // Pass-through rails for ancestors that still have siblings below.
            Transform anc = t.parent;
            int up = 1;
            while (anc != null && anc.parent != null)
            {
                if (!IsLastSibling(anc))
                    VLine(railX - up * indent, rect.y, rect.yMax, col);
                anc = anc.parent;
                up++;
            }
        }
    }
}
