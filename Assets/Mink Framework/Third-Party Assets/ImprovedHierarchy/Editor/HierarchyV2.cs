#if UNITY_6000_6_OR_NEWER
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Hierarchy;
using Unity.Hierarchy.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace ImprovedHierarchy
{
    /// <summary>
    /// Row decorations for the UI Toolkit Hierarchy window that Unity 6.6 uses by
    /// default (<see cref="HierarchyWindow"/>). That window doesn't raise the IMGUI
    /// per-row callback the classic Hierarchy uses, so rows are decorated as Unity
    /// binds them, through the public BindViewItem / UnbindViewItem events:
    ///
    ///   • colour gradient, tree lines  — UI Toolkit layers *under* the row content,
    ///   • custom icon, readable label  — applied to Unity's own icon / name elements,
    ///   • component strip, scene ▾, icon right-click — the same IMGUI code as the
    ///     classic Hierarchy, hosted in small IMGUIContainers inside the row.
    ///
    /// Everything Unity created is restored when a row is unbound.
    /// </summary>
    [InitializeOnLoad]
    internal static class HierarchyV2
    {
        private sealed class Decor
        {
            public HierarchyViewItem Item;
            public HierarchyView View;
            public VisualElement Tint;
            public VisualElement Lines;
            public Image IconImage;
            public IMGUIContainer IconHit;
            public IMGUIContainer Strip;
            public IMGUIContainer Chevron;

            public GameObject Go;
            public bool IsScene;
            public bool IconHidden;
            public bool NameColored;
            public bool PickerRequested;

            // Tree-line topology (from the view model) and geometry (from layout).
            public bool HasLines;
            public bool Last;
            public readonly List<bool> PassThrough = new List<bool>();
            public float ToggleCenterX = 12.5f, ToggleLeftX = 5f, IconX = 22f;
            public bool HasToggle;
        }

        private static readonly ConditionalWeakTable<HierarchyViewItem, Decor> decors = new ConditionalWeakTable<HierarchyViewItem, Decor>();
        private static readonly HashSet<Decor> bound = new HashSet<Decor>();
        private static bool refreshQueued;
        private static double suppressContextMenuUntil;

        static HierarchyV2()
        {
            HierarchyWindow.BindViewItem += OnBind;
            HierarchyWindow.UnbindViewItem += OnUnbind;
            HierarchyWindow.PopulateContextMenu += OnPopulateContextMenu;

            InspectorEditorSuite.HierarchyRepaintRequested += QueueRefresh;
            ComponentTools.Changed += QueueRefresh;
            Selection.selectionChanged += QueueRefresh;
            EditorApplication.hierarchyChanged += QueueRefresh;
            Undo.undoRedoPerformed += QueueRefresh;
            ObjectChangeEvents.changesPublished += OnObjectChanges;
            EditorApplication.playModeStateChanged += _ => QueueRefresh();
        }

        /// <summary>True while a Unity 6.6 UI Toolkit Hierarchy window is open.</summary>
        public static bool IsActive => Resources.FindObjectsOfTypeAll<HierarchyWindow>().Length > 0;

        // ---------------------------------------------------------------------
        //  Unity's own alternating-row preference (6.6 Hierarchy only)
        // ---------------------------------------------------------------------

        private static readonly Type PrefsType = typeof(Editor).Assembly.GetType("UnityEditor.HierarchyPreferences");

        /// <summary>Unity's built-in "alternating row background" preference, or null if unavailable.</summary>
        public static bool? UnityStripes
        {
            get
            {
                var saved = PrefsType?.GetField("AlternatingRowBackground", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var prop = saved?.GetType().GetProperty("value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return prop != null ? (bool?)(bool)prop.GetValue(saved) : null;
            }
            set
            {
                if (!value.HasValue) return;
                var saved = PrefsType?.GetField("AlternatingRowBackground", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                saved?.GetType().GetProperty("value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(saved, value.Value);
                PrefsType?.GetMethod("RefreshPreferences", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                foreach (var w in Resources.FindObjectsOfTypeAll<HierarchyWindow>()) w.Repaint();
            }
        }

        // ---------------------------------------------------------------------
        //  Binding
        // ---------------------------------------------------------------------

        private static void OnBind(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
        {
            try
            {
                var d = decors.GetValue(item, Create);
                d.View = view;
                bound.Add(d);
                Apply(d);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void OnUnbind(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
        {
            if (!decors.TryGetValue(item, out var d)) return;
            bound.Remove(d);
            Clear(d);
        }

        private static void OnObjectChanges(ref ObjectChangeEventStream stream) => QueueRefresh();

        // Coalesce bursts of change notifications into one pass on the next editor tick.
        private static void QueueRefresh()
        {
            if (refreshQueued || bound.Count == 0) return;
            refreshQueued = true;
            EditorApplication.update += RefreshTick;
        }

        private static void RefreshTick()
        {
            EditorApplication.update -= RefreshTick;
            refreshQueued = false;
            foreach (var d in new List<Decor>(bound))
            {
                try { Apply(d); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        // ---------------------------------------------------------------------
        //  Per-row elements (created once per pooled row, reused across binds)
        // ---------------------------------------------------------------------

        private static Decor Create(HierarchyViewItem item)
        {
            var d = new Decor { Item = item };

            // Colour gradient, behind everything in the row.
            d.Tint = Layer("ih-tint");
            d.Tint.style.backgroundImage = InspectorEditorSuite.GradientTexture;
            d.Tint.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            item.Insert(0, d.Tint);

            // Tree lines, also behind the row content.
            d.Lines = Layer("ih-lines");
            d.Lines.generateVisualContent += ctx => DrawLines(d, ctx);
            item.Insert(1, d.Lines);

            // Custom icon drawn inside Unity's icon element (whose own image is hidden while it shows).
            d.IconImage = new Image { name = "ih-icon", pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
            Fill(d.IconImage);
            item.Icon.Add(d.IconImage);

            // Right-click / Alt+click on the icon, handled in IMGUI so popups anchor correctly.
            // Events it doesn't use (plain clicks, drags) pass through to the row.
            d.IconHit = new IMGUIContainer(() => IconHitGUI(d)) { name = "ih-icon-hit" };
            Fill(d.IconHit);
            item.Icon.Add(d.IconHit);

            // // Component strip in the free space after the name.
            // d.Strip = new IMGUIContainer(() => StripGUI(d)) { name = "ih-strip" };
            // d.Strip.style.flexGrow = 1;
            // d.Strip.style.height = 16;
            // d.Strip.style.alignSelf = Align.Center;
            // d.Strip.RegisterCallback<PointerMoveEvent>(_ => d.Strip.MarkDirtyRepaint());
            // d.Strip.RegisterCallback<PointerLeaveEvent>(_ => d.Strip.MarkDirtyRepaint());
            // item.RightCustomContainer.Add(d.Strip);

            // // Scene-row ▾ right after the scene name.
            // d.Chevron = new IMGUIContainer(() => InspectorEditorSuite.DrawSceneChevron(new Rect(0, 0, 18, 16))) { name = "ih-chevron" };
            // d.Chevron.style.width = 18;
            // d.Chevron.style.height = 16;
            // d.Chevron.RegisterCallback<PointerMoveEvent>(_ => d.Chevron.MarkDirtyRepaint());
            // d.Chevron.RegisterCallback<PointerLeaveEvent>(_ => d.Chevron.MarkDirtyRepaint());
            // item.LeftCustomContainer.Add(d.Chevron);

            item.RegisterCallback<GeometryChangedEvent>(_ => UpdateGeometry(d));
            return d;
        }

        private static VisualElement Layer(string name)
        {
            var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            Fill(e);
            e.style.display = DisplayStyle.None;
            return e;
        }

        private static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0;
        }

        // ---------------------------------------------------------------------
        //  Apply / clear
        // ---------------------------------------------------------------------

        private static void Apply(Decor d)
        {
            var item = d.Item;
            var node = item.Node;
            d.Go = null;
            d.IsScene = false;
            switch (item.Handler)
            {
                case HierarchyGameObjectHandler h: d.Go = h.GetGameObject(node); break;
                case HierarchySceneHandler: d.IsScene = true; break;
            }

            var data = d.Go != null ? GameObjectIconStore.Get(d.Go) : null;

            // Colour gradient.
            bool tinted = data != null && !IHTheme.IsUnset(data.iconColor);
            d.Tint.style.display = tinted ? DisplayStyle.Flex : DisplayStyle.None;
            if (tinted)
            {
                var c = data.iconColor;
                d.Tint.style.unityBackgroundImageTintColor = new Color(c.r, c.g, c.b, c.a * InspectorEditorSuite.TintStrength);
            }

            // Custom icon.
            Texture custom = null;
            if (data != null)
                custom = data.customIconTexture != null ? data.customIconTexture
                       : !string.IsNullOrEmpty(data.internalIconName) ? IHIcons.Get(data.internalIconName) : null;
            if (custom != null)
            {
                d.IconImage.image = custom;
                d.IconImage.tintColor = d.Go.activeInHierarchy ? Color.white : new Color(1f, 1f, 1f, 0.5f);
                d.IconImage.style.display = DisplayStyle.Flex;
                item.Icon.style.unityBackgroundImageTintColor = Color.clear;
                d.IconHidden = true;
            }
            else
            {
                d.IconImage.style.display = DisplayStyle.None;
                RestoreIcon(d);
            }

            // Name stays readable over bright tints (selected rows keep Unity's styling).
            Color? text = null;
            if (tinted && !d.View.IsSelected(node))
            {
                text = InspectorEditorSuite.ContrastText(IHTheme.RowBackground, data.iconColor, IHTheme.Text);
                if (text.HasValue && !d.Go.activeInHierarchy) text = IHTheme.WithAlpha(text.Value, 0.6f);
            }
            if (text.HasValue) { item.Name.style.color = text.Value; d.NameColored = true; }
            else RestoreName(d);

            d.IconHit.style.display = d.Go != null ? DisplayStyle.Flex : DisplayStyle.None;
            // d.Strip.style.display = d.Go != null ? DisplayStyle.Flex : DisplayStyle.None;
            // d.Chevron.style.display = d.IsScene && InspectorEditorSuite.InlineSceneRowControlsEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            // d.Strip.MarkDirtyRepaint();

            ComputeLines(d);
            UpdateGeometry(d);
        }

        private static void Clear(Decor d)
        {
            d.Tint.style.display = DisplayStyle.None;
            d.Lines.style.display = DisplayStyle.None;
            d.IconImage.style.display = DisplayStyle.None;
            d.IconHit.style.display = DisplayStyle.None;
            // d.Strip.style.display = DisplayStyle.None;
            // d.Chevron.style.display = DisplayStyle.None;
            RestoreIcon(d);
            RestoreName(d);
            d.Go = null;
            d.PickerRequested = false;
        }

        private static void RestoreIcon(Decor d)
        {
            if (!d.IconHidden) return;
            d.Item.Icon.style.unityBackgroundImageTintColor = StyleKeyword.Null;
            d.IconHidden = false;
        }

        private static void RestoreName(Decor d)
        {
            if (!d.NameColored) return;
            d.Item.Name.style.color = StyleKeyword.Null;
            d.NameColored = false;
        }

        // ---------------------------------------------------------------------
        //  Geometry (positions of Unity's toggle / icon inside the row)
        // ---------------------------------------------------------------------

        private static void UpdateGeometry(Decor d)
        {
            var item = d.Item;
            if (float.IsNaN(item.Icon.layout.x) || item.Icon.layout.width <= 0) return;

            d.IconX = item.Icon.ChangeCoordinatesTo(item, Vector2.zero).x;
            var toggle = item.Toggle;
            float tx = toggle.ChangeCoordinatesTo(item, Vector2.zero).x;
            d.ToggleLeftX = tx;
            d.ToggleCenterX = tx + toggle.layout.width * 0.5f;
            d.HasToggle = !toggle.ClassListContains("hierarchy-item__toggle--hidden");

            d.Tint.style.left = d.IconX - 2f;
            d.Lines.MarkDirtyRepaint();
        }

        // ---------------------------------------------------------------------
        //  Tree lines
        // ---------------------------------------------------------------------

        // Width of ONE indentation level. (The row's own IndentWidth property is the
        // row's total indent, i.e. depth × this value.) Rows all start at the same x
        // and Unity indents their content, so rails are measured from this row's toggle.
        private static float? _levelIndent;
        private static float LevelIndent
        {
            get
            {
                if (_levelIndent.HasValue) return _levelIndent.Value;
                float v = 14f;
                try
                {
                    var f = typeof(HierarchyViewItem).GetField("k_IndentWidth", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (f != null && f.GetValue(null) is float w && w > 0f) v = w;
                }
                catch { }
                _levelIndent = v;
                return v;
            }
        }

        private static bool IsGameObjectNode(HierarchyViewModel vm, in HierarchyNode node) =>
            node != HierarchyNode.Null && vm.GetNodeTypeHandlerBase(node) is HierarchyGameObjectHandler;

        private static void ComputeLines(Decor d)
        {
            d.HasLines = false;
            d.PassThrough.Clear();
            if (!InspectorEditorSuite.HierarchyLinesEnabled || d.Go == null || d.View.Filtering) { d.Lines.style.display = DisplayStyle.None; return; }

            var vm = d.View.ViewModel;
            var node = d.Item.Node;
            var parent = vm.GetParent(node);
            // Top-level objects have no parent object to connect to (same as the classic Hierarchy).
            if (!IsGameObjectNode(vm, parent)) { d.Lines.style.display = DisplayStyle.None; return; }

            d.Last = vm.GetNextSibling(node) == HierarchyNode.Null;
            // Pass-through rails for ancestors that still have siblings below.
            var anc = parent;
            while (true)
            {
                var ap = vm.GetParent(anc);
                if (!IsGameObjectNode(vm, ap)) break;
                d.PassThrough.Add(vm.GetNextSibling(anc) != HierarchyNode.Null);
                anc = ap;
            }
            d.HasLines = true;
            d.Lines.style.display = DisplayStyle.Flex;
            d.Lines.MarkDirtyRepaint();
        }

        private static void DrawLines(Decor d, MeshGenerationContext ctx)
        {
            if (!d.HasLines) return;
            float h = d.Item.layout.height;
            if (float.IsNaN(h) || h <= 0) return;

            float indent = LevelIndent;
            float yMid = Mathf.Round(h * 0.5f);
            // The rail linking this node to its parent sits under the parent's
            // foldout column, so it never hides beneath this node's own arrow.
            float rail = Mathf.Round(d.ToggleCenterX - indent);
            float elbowEnd = d.HasToggle ? d.ToggleLeftX : d.IconX - 2f;

            var p = ctx.painter2D;
            p.fillColor = InspectorEditorSuite.HierarchyLineColor;
            Box(p, rail, yMid, elbowEnd - rail, 1f);
            Box(p, rail, 0f, 1f, d.Last ? yMid + 1f : h);
            for (int k = 0; k < d.PassThrough.Count; k++)
                if (d.PassThrough[k]) Box(p, Mathf.Round(rail - (k + 1) * indent), 0f, 1f, h);
        }

        private static void Box(Painter2D p, float x, float y, float w, float h)
        {
            if (w <= 0f || h <= 0f) return;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Fill();
        }

        // ---------------------------------------------------------------------
        //  IMGUI-hosted parts (shared code with the classic Hierarchy)
        // ---------------------------------------------------------------------

        private static void StripGUI(Decor d)
        {
            if (d.Go == null) return;
            var r = d.Strip.contentRect;
            if (float.IsNaN(r.width) || r.width < 18f) return;
            InspectorEditorSuite.DrawComponentStripInArea(d.Go, new Rect(0f, 0f, r.width - 2f, r.height));
        }

        private static void IconHitGUI(Decor d)
        {
            if (d.Go == null) return;
            var e = Event.current;
            var r = d.IconHit.contentRect;

            if (d.PickerRequested)
            {
                d.PickerRequested = false;
                IconPickerWindow.Show(r, d.Go);
                return;
            }
            if (!r.Contains(e.mousePosition)) return;

            // Right-click the icon: colour & icon picker instead of Unity's context menu.
            // The picker opens once, on the next GUI pass; the matching MouseUp /
            // ContextClick are swallowed so the row menu doesn't open as well.
            if (e.type == EventType.MouseDown && e.button == 1)
            {
                suppressContextMenuUntil = EditorApplication.timeSinceStartup + 0.6;
                d.PickerRequested = true;
                d.IconHit.MarkDirtyRepaint();
                e.Use();
            }
            else if ((e.type == EventType.MouseUp && e.button == 1) || e.type == EventType.ContextClick)
            {
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && e.alt)
            {
                EditorUtility.OpenPropertyEditor(d.Go);
                e.Use();
            }
        }

        // ---------------------------------------------------------------------
        //  Context menu
        // ---------------------------------------------------------------------

        private static void OnPopulateContextMenu(HierarchyWindow window, HierarchyView view, HierarchyViewItem item, DropdownMenu menu)
        {
            // A right-click on the icon opens our picker instead of the menu.
            if (EditorApplication.timeSinceStartup < suppressContextMenuUntil)
            {
                menu.MenuItems().Clear();
                return;
            }
            if (item == null || !decors.TryGetValue(item, out var d) || d.Go == null) return;

            var go = d.Go;
            menu.AppendSeparator();
            menu.AppendAction("Improved Hierarchy/Colour and Icon…", _ =>
            {
                d.PickerRequested = true;
                d.IconHit.MarkDirtyRepaint();
            });
            menu.AppendAction("Improved Hierarchy/Reset Colour and Icon", _ =>
            {
                GameObjectIconStore.Reset(go);
                InspectorEditorSuite.RepaintHierarchy();
            }, GameObjectIconStore.Get(go) != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendAction("Improved Hierarchy/Copy All Components", _ => ComponentTools.CopyAllComponents(go));
            menu.AppendAction("Improved Hierarchy/Paste Multi-Copy Buffer", _ => ComponentTools.PasteBuffer(Selection.gameObjects.Length > 0 ? Selection.gameObjects : new[] { go }),
                ComponentTools.BufferCount > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }
    }
}
#endif
