using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImprovedHierarchy
{
    /// <summary>
    /// Draws the Copy (Multi-Copy Buffer) and — in Play Mode — Keep Values
    /// buttons on EVERY component header, just left of Unity's own help /
    /// presets / menu icons. Built-in components and custom scripts get the
    /// exact same buttons in the exact same place.
    ///
    /// Approach: Unity wraps each component editor in an internal
    /// UnityEditor.UIElements.EditorElement whose header is an IMGUIContainer.
    /// We wrap that container's onGUIHandler:
    ///   1. our buttons handle input FIRST, so a click never reaches the title
    ///      bar (which would fold / drag the component);
    ///   2. Unity draws its header;
    ///   3. we paint our buttons on top.
    /// Headers are re-checked continuously, so containers that Unity recycles
    /// (and resets) are re-wrapped instead of silently losing their buttons.
    ///
    /// Relies on internal Unity APIs. Kill-switch:
    ///     Tools › Improved Hierarchy › Component Header Buttons
    /// The same actions are always available from the component's context
    /// menu under "Improved Hierarchy".
    /// </summary>
    [InitializeOnLoad]
    internal static class ComponentHeaderButtons
    {
        private const string EnabledPref = "InspectorSuite.HeaderButtons.Enabled";
        private const string MenuPath = "Tools/Improved Hierarchy/Component Header Buttons";

        // Width of Unity's own icon cluster (help · presets · ⋮) at the right of the title bar.
        private const float UnityIconsWidth = 64f;
        private const float ButtonSize = 20f;
        private const float ButtonGap = 1f;
        // Title text starts after the foldout arrow, component icon and enabled toggle.
        private const float TitleTextX = 60f;

        private static Type editorElementType;
        private static FieldInfo headerField;
        private static PropertyInfo headerProp;
        private static FieldInfo editorTargetField;
        private static PropertyInfo editorProp;

        // header -> the wrapper we installed on it. If Unity replaces the header's
        // handler (container recycling), the mismatch tells us to wrap it again.
        private static readonly ConditionalWeakTable<IMGUIContainer, Action> installed =
            new ConditionalWeakTable<IMGUIContainer, Action>();
        private static readonly ConditionalWeakTable<IMGUIContainer, object> hoverHooked =
            new ConditionalWeakTable<IMGUIContainer, object>();

        private static double lastSweep;
        private static bool sweepRequested = true;

        static ComponentHeaderButtons()
        {
            InitReflection();
            EditorApplication.update += Tick;
            Selection.selectionChanged += () => sweepRequested = true;
            EditorApplication.playModeStateChanged += _ => sweepRequested = true;
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPref, true);
            set => EditorPrefs.SetBool(EnabledPref, value);
        }

        [MenuItem(MenuPath, priority = 23)]
        private static void ToggleEnabled()
        {
            Enabled = !Enabled;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void InitReflection()
        {
            var asm = typeof(Editor).Assembly;
            editorElementType = asm.GetType("UnityEditor.UIElements.EditorElement")
                             ?? asm.GetType("UnityEditor.EditorElement");
            if (editorElementType == null) return;

            const BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            headerField = editorElementType.GetField("m_Header", bf);
            headerProp = editorElementType.GetProperty("header", bf);
            editorTargetField = editorElementType.GetField("m_EditorTarget", bf);
            editorProp = editorElementType.GetProperty("editor", bf);
        }

        // -------------------------------------------------------------------------
        //  Discovery
        // -------------------------------------------------------------------------

        private static void Tick()
        {
            if (!Enabled || editorElementType == null) return;

            // Sweep ~5x per second, or right away after a selection change so the
            // buttons are there the moment a new inspector is built.
            double now = EditorApplication.timeSinceStartup;
            if (!sweepRequested && now - lastSweep < 0.2) return;
            sweepRequested = false;
            lastSweep = now;

            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w == null) continue;
                string n = w.GetType().Name;
                if (n != "InspectorWindow" && n != "PropertyEditor") continue;
                WalkTree(w.rootVisualElement);
            }
        }

        private static void WalkTree(VisualElement element)
        {
            if (element == null) return;
            if (editorElementType.IsInstanceOfType(element)) HookEditorElement(element);
            for (int i = 0, n = element.childCount; i < n; i++) WalkTree(element[i]);
        }

        private static void HookEditorElement(VisualElement editorElement)
        {
            IMGUIContainer header;
            try
            {
                header = (headerProp?.GetValue(editorElement) as IMGUIContainer)
                      ?? (headerField?.GetValue(editorElement) as IMGUIContainer);
            }
            catch { return; }
            if (header == null) return;

            if (installed.TryGetValue(header, out var ours) && header.onGUIHandler == ours) return;

            var existing = header.onGUIHandler;
            var weakElement = new WeakReference(editorElement);
            Action wrapper = () => OnHeaderGUI(header, weakElement, existing);

            installed.Remove(header);
            installed.Add(header, wrapper);
            header.onGUIHandler = wrapper;

            // Repaint while the mouse moves over / leaves the header so the hover
            // state of our buttons tracks the cursor.
            if (!hoverHooked.TryGetValue(header, out _))
            {
                hoverHooked.Add(header, null);
                header.RegisterCallback<MouseMoveEvent>(_ => header.MarkDirtyRepaint());
                header.RegisterCallback<MouseLeaveEvent>(_ => header.MarkDirtyRepaint());
            }
            header.MarkDirtyRepaint();
        }

        // -------------------------------------------------------------------------
        //  Drawing
        // -------------------------------------------------------------------------

        private struct Buttons
        {
            public Rect Copy;
            public Rect Save;
            public bool HasSave;
            public Rect Backdrop;
            public bool NeedsBackdrop;
        }

        private static void OnHeaderGUI(IMGUIContainer header, WeakReference weakElement, Action existing)
        {
            Editor editor = null;
            Buttons b = default;
            bool show = false;

            try
            {
                if (Enabled && weakElement.Target is VisualElement elem)
                {
                    editor = (editorProp?.GetValue(elem) as Editor) ?? (editorTargetField?.GetValue(elem) as Editor);
                    show = ShouldShow(editor);
                    if (show) b = Layout(header, (Component)editor.target);
                }
            }
            catch { show = false; }

            var type = Event.current.type;
            if (show && type != EventType.Repaint && type != EventType.Layout)
                HandleInput(b, editor);

            existing?.Invoke();

            if (show && type == EventType.Repaint)
            {
                try { Draw(b, editor); } catch { /* never break Unity's header */ }
            }
        }

        private static bool ShouldShow(Editor editor)
        {
            if (editor == null || editor.targets == null || editor.targets.Length == 0) return false;
            if (!(editor.target is Component comp) || comp == null) return false;
            return ComponentTools.CanCopy(comp);
        }

        private static Buttons Layout(IMGUIContainer header, Component comp)
        {
            float width = header.contentRect.width;
            if (float.IsNaN(width) || width <= 0f) width = EditorGUIUtility.currentViewWidth;
            float height = header.contentRect.height;
            if (float.IsNaN(height) || height < ButtonSize) height = 22f;

            float y = Mathf.Round((height - ButtonSize) * 0.5f);
            var b = new Buttons();
            b.Copy = new Rect(width - UnityIconsWidth - ButtonSize, y, ButtonSize, ButtonSize);
            b.HasSave = Application.isPlaying;
            b.Save = new Rect(b.Copy.x - ButtonSize - ButtonGap, y, ButtonSize, ButtonSize);

            float left = b.HasSave ? b.Save.x : b.Copy.x;
            float titleEnd = TitleTextX + EditorStyles.boldLabel.CalcSize(new GUIContent(ObjectNames.GetInspectorTitle(comp))).x;
            b.NeedsBackdrop = titleEnd > left - 4f;
            b.Backdrop = new Rect(left - 14f, 1f, b.Copy.xMax - left + 16f, height - 2f);
            return b;
        }

        private static void HandleInput(Buttons b, Editor editor)
        {
            if (b.HasSave && IHGui.Clicked(b.Save))
            {
                foreach (var t in editor.targets) if (t is Component c) ComponentTools.TogglePlayModeSave(c);
            }
            else if (IHGui.Clicked(b.Copy))
            {
                // Multi-selection: follow the primary target's state so the
                // whole selection ends up consistently in / out of the buffer.
                bool add = !ComponentTools.IsInBuffer(editor.target as Component);
                foreach (var t in editor.targets)
                {
                    if (!(t is Component c)) continue;
                    if (add) ComponentTools.AddToBuffer(c, notify: t == editor.target);
                    else ComponentTools.RemoveFromBuffer(c, notify: t == editor.target);
                }
            }
        }

        private static void Draw(Buttons b, Editor editor)
        {
            var comp = (Component)editor.target;
            var mouse = Event.current.mousePosition;

            // Long titles on a narrow Inspector would run under the buttons —
            // fade the title out behind them so the icons stay legible.
            if (b.NeedsBackdrop) DrawBackdrop(b.Backdrop);

            if (b.HasSave)
            {
                bool kept = ComponentTools.HasPlayModeSave(comp);
                IHGui.DrawIconButton(b.Save, IHIcons.Get(IHIcons.Save), kept, b.Save.Contains(mouse));
                IHGui.Tooltip(b.Save, kept
                    ? "Keeping Play Mode values — this component's values will be written back when you stop playing.\nClick to cancel."
                    : "Keep Play Mode values\nWhen you stop playing, this component keeps the values it has at that moment.");
            }

            bool queued = ComponentTools.IsInBuffer(comp);
            IHGui.DrawIconButton(b.Copy, IHIcons.Get(IHIcons.Copy), queued, b.Copy.Contains(mouse));
            int n = ComponentTools.BufferCount;
            IHGui.Tooltip(b.Copy, queued
                ? $"In the Multi-Copy Buffer ({n} component{(n == 1 ? "" : "s")}).\nClick to remove."
                : $"Add to the Multi-Copy Buffer{(n > 0 ? $" ({n} already)" : "")}.\nPaste from the GameObject header or Tools › Improved Hierarchy › Multi-Copy Buffer.");
        }

        private static Texture2D _fadeTex;

        private static void DrawBackdrop(Rect r)
        {
            // Title bar colour (Pro / Personal).
            Color bg = EditorGUIUtility.isProSkin ? new Color32(62, 62, 62, 255) : new Color32(203, 203, 203, 255);
            if (_fadeTex == null)
            {
                _fadeTex = new Texture2D(16, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                for (int x = 0; x < 16; x++) _fadeTex.SetPixel(x, 0, new Color(1, 1, 1, x / 15f));
                _fadeTex.Apply();
            }
            var prev = GUI.color;
            GUI.color = bg;
            GUI.DrawTexture(new Rect(r.x, r.y, 12f, r.height), _fadeTex);
            GUI.color = prev;
            EditorGUI.DrawRect(new Rect(r.x + 12f, r.y, r.width - 12f, r.height), bg);
        }
    }
}
