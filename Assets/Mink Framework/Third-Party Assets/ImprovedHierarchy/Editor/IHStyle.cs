using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ImprovedHierarchy
{
    // ---------------------------------------------------------------------------
    // Shared look & feel for every Improved Hierarchy surface (hierarchy rows,
    // component headers, pickers, windows). Anything that draws a button, an
    // icon, a swatch or a coloured state should come through here so the whole
    // package reads as one tool:
    //
    //   • IHTheme  — skin-aware colours (Pro / Personal) + native row colours.
    //   • IHIcons  — one name per concept, skin-aware and silently cached.
    //   • IHGui    — the handful of controls every surface shares.
    // ---------------------------------------------------------------------------

    internal static class IHTheme
    {
        private static bool Pro => EditorGUIUtility.isProSkin;

        /// <summary>"On" state — a component is queued for Save / in the Multi-Copy buffer, an item is selected.</summary>
        public static Color Accent => Pro ? new Color(0.35f, 0.80f, 0.48f) : new Color(0.10f, 0.56f, 0.26f);
        public static Color AccentFill => WithAlpha(Accent, Pro ? 0.22f : 0.20f);

        /// <summary>Favourite star, warnings.</summary>
        public static Color Highlight => Pro ? new Color(1.00f, 0.80f, 0.25f) : new Color(0.80f, 0.55f, 0.00f);
        public static Color Warning => Pro ? new Color(1.00f, 0.76f, 0.03f) : new Color(0.75f, 0.50f, 0.00f);

        public static Color Hover => Pro ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.08f);
        public static Color Pressed => Pro ? new Color(1f, 1f, 1f, 0.14f) : new Color(0f, 0f, 0f, 0.14f);
        public static Color Border => Pro ? new Color(0f, 0f, 0f, 0.45f) : new Color(0f, 0f, 0f, 0.25f);
        public static Color Separator => Pro ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.10f);
        public static Color PanelBackground => Pro ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.87f, 0.87f, 0.87f);
        public static Color AltRow => Pro ? new Color(1f, 1f, 1f, 0.025f) : new Color(0f, 0f, 0f, 0.035f);

        public static Color Text => EditorStyles.label.normal.textColor;
        public static Color MutedText => WithAlpha(Text, 0.55f);

        /// <summary>Tint for idle icon buttons — matches Unity's own header icons.</summary>
        public static Color IconIdle => Pro ? new Color(1f, 1f, 1f, 0.70f) : new Color(1f, 1f, 1f, 0.80f);

        // Native Hierarchy / Project tree row colours (identical on 2022.3 and Unity 6).
        public static Color RowBackground => Pro ? new Color32(56, 56, 56, 255) : new Color32(200, 200, 200, 255);
        public static Color RowHover => Pro ? new Color32(69, 69, 69, 255) : new Color32(178, 178, 178, 255);
        public static Color RowSelected => Pro ? new Color32(44, 93, 135, 255) : new Color32(58, 114, 176, 255);
        public static Color RowSelectedUnfocused => Pro ? new Color32(77, 77, 77, 255) : new Color32(174, 174, 174, 255);

        public static Color RowColor(bool selected, bool focused, bool hover)
        {
            if (selected) return focused ? RowSelected : RowSelectedUnfocused;
            return hover ? RowHover : RowBackground;
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        /// <summary>Rec. 709 perceptual luminance.</summary>
        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        public static bool IsUnset(Color c) =>
            c.a <= 0.01f ||
            (Mathf.Approximately(c.r, 1f) && Mathf.Approximately(c.g, 1f) &&
             Mathf.Approximately(c.b, 1f) && Mathf.Approximately(c.a, 1f));
    }

    internal static class IHIcons
    {
        // One icon per concept, used everywhere that concept appears.
        public const string Save = "SaveAs";
        public const string Copy = "TreeEditor.Duplicate";
        public const string Paste = "Clipboard";
        public const string Clear = "TreeEditor.Trash";
        public const string Remove = "Toolbar Minus";
        public const string Ping = "Search Icon";
        public const string Dropdown = "icon dropdown";
        public const string Warning = "console.warnicon.sml";
        public const string Refresh = "Refresh";
        public const string Scene = "SceneAsset Icon";
        public const string Folder = "Folder Icon";
        public const string FolderEmpty = "FolderEmpty Icon";

        private static readonly Dictionary<string, Texture> Cache = new Dictionary<string, Texture>();
        private static bool _cachedPro;

        /// <summary>
        /// Resolves a built-in editor icon by name. Skin-aware: pass the plain
        /// name ("SaveAs") and the Pro-skin variant ("d_SaveAs") is picked
        /// automatically. Legacy "d_" names stored in older libraries are
        /// normalised so they also render correctly on the Personal skin.
        /// Returns null (silently, without console errors) when missing.
        /// </summary>
        public static Texture Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_cachedPro != EditorGUIUtility.isProSkin) { Cache.Clear(); _cachedPro = EditorGUIUtility.isProSkin; }
            if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;

            string plain = name.StartsWith("d_", StringComparison.Ordinal) ? name.Substring(2) : name;
            Texture tex = EditorGUIUtility.FindTexture(plain) ?? EditorGUIUtility.FindTexture(name);
            if (tex == null) tex = LoadViaIconContent(plain) ?? LoadViaIconContent(name);

            Cache[name] = tex;
            return tex;
        }

        public static bool Exists(string name) => Get(name) != null;

        // Some icons (e.g. "Camera Icon") only resolve through IconContent, which
        // logs an error when the name is unknown. Silence the logger for the probe.
        private static Texture LoadViaIconContent(string name)
        {
            var logger = Debug.unityLogger;
            bool prev = logger.logEnabled;
            try
            {
                logger.logEnabled = false;
                return EditorGUIUtility.IconContent(name)?.image;
            }
            catch { return null; }
            finally { logger.logEnabled = prev; }
        }

        /// <summary>Canonical (skin-neutral) name, used to de-duplicate icon lists.</summary>
        public static string Canonical(string name) =>
            name != null && name.StartsWith("d_", StringComparison.Ordinal) ? name.Substring(2) : name;
    }

    internal static class IHGui
    {
        public const float IconButtonSize = 20f;
        public const float SwatchSize = 18f;
        public const float SwatchGap = 3f;
        public const float GridCell = 34f;
        public const float GridGap = 3f;
        public const float Radius = 3f;

        // ---------------------------------------------------------------------
        //  Primitives
        // ---------------------------------------------------------------------

        public static void RoundedRect(Rect r, Color c, float radius = Radius)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);
        }

        public static void RoundedOutline(Rect r, Color c, float width = 1f, float radius = Radius)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, width, radius);
        }

        public static void DrawIcon(Rect r, Texture icon, Color tint)
        {
            if (icon == null || Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit);
            GUI.color = prev;
        }

        public static Rect Inset(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - 2 * d, r.height - 2 * d);

        public static Rect CenteredSquare(Rect r, float size) =>
            new Rect(Mathf.Round(r.x + (r.width - size) * 0.5f), Mathf.Round(r.y + (r.height - size) * 0.5f), size, size);

        /// <summary>Left-click inside <paramref name="r"/>? Consumes the event.</summary>
        public static bool Clicked(Rect r, int button = 0)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == button && r.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }
            return false;
        }

        public static void Tooltip(Rect r, string tooltip)
        {
            if (!string.IsNullOrEmpty(tooltip)) GUI.Label(r, new GUIContent(string.Empty, tooltip), GUIStyle.none);
        }

        // ---------------------------------------------------------------------
        //  Icon button — the one clickable icon used across the package
        //  (component headers, hierarchy strip, windows, pickers).
        //  States: idle (soft icon) · hover (backdrop) · active (accent pill + accent icon).
        // ---------------------------------------------------------------------

        public static void DrawIconButton(Rect r, Texture icon, bool active, bool hover, string fallback = null)
        {
            if (Event.current.type != EventType.Repaint) return;

            if (active) RoundedRect(r, IHTheme.AccentFill);
            else if (hover) RoundedRect(r, IHTheme.Hover);

            Color tint = active ? IHTheme.Accent : (hover ? Color.white : IHTheme.IconIdle);
            if (icon != null)
            {
                DrawIcon(CenteredSquare(r, Mathf.Min(16f, r.height - 2f)), icon, tint);
            }
            else if (!string.IsNullOrEmpty(fallback))
            {
                var prev = GUI.contentColor;
                GUI.contentColor = active ? IHTheme.Accent : IHTheme.Text;
                GUI.Label(r, fallback, Styles.CenteredGlyph);
                GUI.contentColor = prev;
            }
        }

        /// <summary>Draws and handles an icon button. Returns true when clicked.</summary>
        public static bool IconButton(Rect r, Texture icon, string tooltip, bool active = false, string fallback = null)
        {
            bool hover = r.Contains(Event.current.mousePosition) && GUI.enabled;
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            DrawIconButton(r, icon, active, hover, fallback);
            Tooltip(r, tooltip);
            return GUI.enabled && Clicked(r);
        }

        public static bool IconButton(string iconName, string tooltip, bool active = false, float size = IconButtonSize)
        {
            var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            return IconButton(r, IHIcons.Get(iconName), tooltip, active);
        }

        // ---------------------------------------------------------------------
        //  Swatches + icon grid cells (shared by both pickers)
        // ---------------------------------------------------------------------

        public static bool ColorSwatch(Rect r, Color c, bool selected, string tooltip = null)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            if (Event.current.type == EventType.Repaint)
            {
                if (selected)
                {
                    RoundedOutline(Inset(r, -2f), IHTheme.Text, 1.5f, Radius + 1f);
                }
                RoundedRect(r, c);
                RoundedOutline(r, IHTheme.Border, 1f);
                if (hover && !selected) RoundedOutline(r, IHTheme.WithAlpha(Color.white, 0.6f), 1f);
            }
            Tooltip(r, tooltip);
            return Clicked(r);
        }

        /// <summary>The "no colour / no icon" swatch: an outlined box with a slash.</summary>
        public static bool NoneSwatch(Rect r, bool selected, string tooltip)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            if (Event.current.type == EventType.Repaint)
            {
                if (selected) RoundedOutline(Inset(r, -2f), IHTheme.Text, 1.5f, Radius + 1f);
                RoundedRect(r, hover ? IHTheme.Hover : IHTheme.WithAlpha(IHTheme.Text, 0.04f));
                RoundedOutline(r, IHTheme.WithAlpha(IHTheme.Text, 0.35f), 1f);
                // Diagonal "none" slash.
                var m = GUI.matrix;
                var c = r.center;
                GUIUtility.RotateAroundPivot(-45f, c);
                EditorGUI.DrawRect(new Rect(c.x - r.width * 0.42f, c.y - 0.75f, r.width * 0.84f, 1.5f),
                    new Color(0.90f, 0.35f, 0.35f, 0.9f));
                GUI.matrix = m;
            }
            Tooltip(r, tooltip);
            return Clicked(r);
        }

        public enum CellClick { None, Left, Right }

        /// <summary>A square icon cell: hover backdrop, accent ring when selected, left / right click.</summary>
        public static CellClick IconCell(Rect r, Texture tex, bool selected, string tooltip)
        {
            var e = Event.current;
            bool hover = r.Contains(e.mousePosition);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            if (e.type == EventType.Repaint)
            {
                if (selected) RoundedRect(r, IHTheme.AccentFill, 4f);
                else if (hover) RoundedRect(r, IHTheme.Hover, 4f);
                if (selected) RoundedOutline(r, IHTheme.Accent, 1.5f, 4f);
                DrawIcon(CenteredSquare(r, Mathf.Min(24f, r.width - 8f)), tex, Color.white);
            }
            Tooltip(r, tooltip);
            if (e.type == EventType.MouseDown && hover)
            {
                if (e.button == 0) { e.Use(); return CellClick.Left; }
                if (e.button == 1) { e.Use(); return CellClick.Right; }
            }
            if (e.type == EventType.ContextClick && hover) e.Use();
            return CellClick.None;
        }

        // ---------------------------------------------------------------------
        //  Layout helpers
        // ---------------------------------------------------------------------

        public static void SectionHeader(string title, string right = null)
        {
            GUILayout.Space(6);
            var r = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
            GUI.Label(r, title, Styles.Section);
            if (!string.IsNullOrEmpty(right)) GUI.Label(r, right, Styles.HintRight);
            GUILayout.Space(2);
        }

        public static void Separator(float padding = 4f)
        {
            GUILayout.Space(padding);
            var r = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(r, IHTheme.Separator);
            GUILayout.Space(padding);
        }

        public static void Hint(string text)
        {
            GUILayout.Label(text, Styles.Hint);
        }

        /// <summary>Number of grid columns that fit in <paramref name="width"/>.</summary>
        public static int Columns(float width, float cell = GridCell, float gap = GridGap) =>
            Mathf.Max(1, Mathf.FloorToInt((width + gap) / (cell + gap)));

        /// <summary>Rect of the i-th cell in a grid that starts at <paramref name="origin"/>.</summary>
        public static Rect GridRect(Rect origin, int index, int cols, float cell = GridCell, float gap = GridGap) =>
            new Rect(origin.x + (index % cols) * (cell + gap), origin.y + (index / cols) * (cell + gap), cell, cell);

        public static float GridHeight(int count, int cols, float cell = GridCell, float gap = GridGap) =>
            count <= 0 ? 0 : Mathf.CeilToInt(count / (float)cols) * (cell + gap) - gap;

        /// <summary>Transient toast in the focused window (falls back to the console).</summary>
        public static void Notify(string msg, double seconds = 1.4)
        {
            var win = EditorWindow.focusedWindow ?? EditorWindow.mouseOverWindow;
            if (win != null) win.ShowNotification(new GUIContent(msg), seconds);
            else Debug.Log("Improved Hierarchy: " + msg);
        }

        public static class Styles
        {
            private static GUIStyle _section, _hint, _hintRight, _glyph, _badge, _rowTitle, _rowSub;

            public static GUIStyle Section => _section ??= new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = IHTheme.MutedText },
                padding = new RectOffset(2, 2, 0, 0),
            };

            public static GUIStyle Hint => _hint ??= new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal = { textColor = IHTheme.MutedText },
            };

            public static GUIStyle HintRight => _hintRight ??= new GUIStyle(Hint)
            {
                alignment = TextAnchor.MiddleRight,
                wordWrap = false,
            };

            public static GUIStyle CenteredGlyph => _glyph ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
            };

            /// <summary>Small count badge drawn on the bottom-right of an icon.</summary>
            public static GUIStyle Badge => _badge ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.LowerRight,
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Color.white },
            };

            public static GUIStyle RowTitle => _rowTitle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip,
            };

            public static GUIStyle RowSubtitle => _rowSub ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip,
                normal = { textColor = IHTheme.MutedText },
            };
        }

        /// <summary>Draws a count badge ("3", "99+") on the bottom-right of <paramref name="iconRect"/>.</summary>
        public static void CountBadge(Rect iconRect, int count)
        {
            if (count <= 1 || Event.current.type != EventType.Repaint) return;
            string text = count > 99 ? "99+" : count.ToString();
            var size = Styles.Badge.CalcSize(new GUIContent(text));
            var r = new Rect(iconRect.xMax - size.x + 2f, iconRect.yMax - 9f, size.x + 2f, 10f);
            RoundedRect(r, new Color(0f, 0f, 0f, 0.65f), 3f);
            GUI.Label(new Rect(r.x, r.y - 1f, r.width - 1f, r.height + 1f), text, Styles.Badge);
        }
    }
}
