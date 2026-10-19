# Hierarchy v2

A Unity Editor productivity suite that augments the **Hierarchy**, **Inspector** and **Project** windows with the quality-of-life features Unity leaves out.

> Tested with Unity **2022.3 LTS**, **6.0**, **6.2**, **6.4** and **6.6** — including Unity 6.6's new Hierarchy window. Editor-only — does not affect runtime builds.

Everything lives under **`Tools › Improved Hierarchy`**, and every surface (hierarchy rows, component headers, pickers, windows) shares one visual language: the same icon buttons, the same green "active" state, the same swatches and grids, on both the Pro and Personal editor skins.

---

## Features

### Hierarchy
- **Per-GameObject colours and icons** — right-click any GameObject's icon to open the picker: a colour row, your favourite icons, a custom colour field and Reset. Hold **Alt** to apply to all children too.
- **Colour-tinted rows** — a soft gradient that sits behind the icon and name; text automatically switches to a dark colour on bright tints so it always stays readable. Prefab (blue), broken-prefab (red) and inactive (dimmed) name colours are preserved.
- **Striped rows** — alternate lighter / darker rows so long hierarchies are easy to follow.
- **Tree lines** — solid, pixel-snapped connector lines from parents to children (crisp at any display scaling).
- **Component icon strip** — one clickable icon per component type on the right of each row, with a count badge for duplicates:
  - **Click**: enable / disable (Behaviours, Renderers, Colliders…)
  - **Shift + Click**: select the component
  - **Alt + Click**: open the component in its own Inspector window
  - A **⚠ warning icon** flags GameObjects with missing scripts.
  - In Play Mode, a green underline marks components that will keep their values.
- **Scene switcher** — a `▾` button next to each scene name opens the Scene Selector.

### Inspector
- **Header buttons on every component** (built-ins and your own scripts alike), right next to Unity's own icons:
  - **Copy** — add / remove the component from the **Multi-Copy Buffer**.
  - **Keep Values** *(Play Mode)* — the component keeps the values it has **when you stop playing**. Tweak freely during play; everything you changed is written back to the scene (one Undo step, prefab-override aware).
  - Active buttons turn green so you can see at a glance what's queued.
- **Multi-Copy Buffer** — collect any number of components from any GameObjects (in Edit or Play Mode), then paste them all at once:
  - **Paste N components** bar on the GameObject header whenever the buffer has items.
  - Buffer window (`Tools › Improved Hierarchy › Multi-Copy Buffer`) with type icons, source names, ping, remove, *Copy Selected* and *Paste onto selection*.
  - Pasting overwrites existing components of the same type in order and adds the rest, so two copied BoxColliders give you two BoxColliders. Fully Undo-able.
  - The buffer survives script reloads and entering Play Mode — copy in Play Mode, paste after you stop.
  - Also in the component context menu and the Hierarchy right-click menu (`Improved Hierarchy › Copy All Components / Paste Multi-Copy Buffer`).
- **Attribute drawers** for ergonomic inspector layouts (add `using ImprovedHierarchy;` to your script — every type in the package lives in the `ImprovedHierarchy` namespace so it never collides with your own classes or other attribute libraries):
  - `[Button]` — clickable button bound to a method, multi-edit safe.
  - `[Tab("Name")]` — group fields under inspector tabs.
  - `[Foldout("Name")]` — collapsible sections.
  - `[ShowIf("memberName")]` / `[HideIf("memberName")]` — conditional visibility (field/property/method, must return `bool`).
  - `[ReadOnly]` — disable editing without removing the field.
  - `[Required("optional message")]` — warns when an `Object` reference is null.
  - `[MinMaxSlider(min, max)]` — Vector2 min/max range slider.
  - `[ProgressBar(max, "Label")]` — read-only progress bar from a numeric field.
  - `[InfoBox("Message", type)]` — decorator hint that sizes to its text; type 0=None, 1=Info, 2=Warning, 3=Error.

### Project window
- **Folder colours and badge icons** — right-click a folder › **Customize Folder…** (same picker as the Hierarchy). The colour tints the folder icon itself; the badge shows in grid view.
- **VCS badges** — a small dot on folders with uncommitted (amber) or untracked (red) files. Git only; silently disabled elsewhere.

### Scene Selector
`Ctrl+Shift+O`, or the `▾` next to a scene name. Type to filter by name or folder, **↑↓** to move, **Enter** to open, **Ctrl+Enter / Ctrl+Click** to open additively, **★** to pin favourites to the top.

### Icon Manager
`Tools › Improved Hierarchy › Icon Manager` — choose which built-in icons appear in the pickers, or add any `Texture2D` from your project.

---

## Menu

```text
Tools › Improved Hierarchy
├── Scene Selector              Ctrl+Shift+O
├── Multi-Copy Buffer
├── Icon Manager
├── ─────────
├── Hierarchy Lines             ✓
├── Striped Rows                ✓
├── Inline Scene Selector       ✓
├── Component Header Buttons    ✓
├── VCS Folder Badges           ✓
├── ─────────
├── Refresh VCS Badges          Ctrl+Alt+V
├── ─────────
├── Migrate Legacy IconData → Library
└── Clean Missing Scripts in Open Scenes
```

All toggles are on by default and persist per machine via `EditorPrefs`.

---

## Installation

1. `Window › Package Manager › My Assets`, select **Hierarchy v2**, then **Download** and **Import**.
2. Everything imports into `Assets/ImprovedHierarchy`. There's nothing to set up: the Hierarchy, Inspector and Project window features are active as soon as the scripts compile.
3. To use the inspector attributes in your own scripts, add `using ImprovedHierarchy;`.

Your colours, icons and favourites are saved in `Assets/Editor/InspectorIconLibrary.asset`, which is created the first time you customise something.

---

## Layout

```text
Assets/ImprovedHierarchy/
├── Documentation/
│   ├── README.md
│   └── CHANGELOG.md
├── Runtime/                        (attributes + data types; no editor code, nothing added to builds)
│   ├── ImprovedHierarchy.Runtime.asmdef
│   ├── InspectorAttributes.cs
│   ├── IconData.cs                 (legacy stub kept for clean upgrades)
│   └── IconLibrary.cs              (ScriptableObject, owns per-object data)
└── Editor/
    ├── ImprovedHierarchy.Editor.asmdef
    ├── IHStyle.cs                  (shared theme, icons and controls)
    ├── InspectorEditorSuite.cs     (hierarchy rows, stripes, lines, strip, scene chevron)
    ├── ComponentTools.cs           (Multi-Copy Buffer, Keep Play Mode Values, buffer window)
    ├── ComponentHeaderButtons.cs   (Copy / Keep Values on every component header)
    ├── IconPickerWindows.cs        (shared picker UI, GameObject picker, Icon Manager)
    ├── GameObjectIconStore.cs      (per-GameObject data + legacy migration)
    ├── GlobalInspectorEditor.cs    ([Tab] / [Foldout] / [Button] support)
    ├── InspectorPropertyDrawers.cs
    ├── InspectorSuiteShared.cs
    ├── ProjectFolderColorizer.cs
    ├── SceneSelectorWindow.cs
    └── VcsStatusProvider.cs
```

---

## Known caveats

- Component header buttons use **reflection on internal Unity APIs**. If a future Unity version breaks them, turn them off with `Tools › Improved Hierarchy › Component Header Buttons` — every action is also in the component's context menu under **Improved Hierarchy**.
- **Keep Play Mode Values** only applies to objects saved in the scene; objects spawned at runtime can't be written back (they're listed in a console warning).
