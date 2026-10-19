# Release notes

## 0.3.2

- **Unity 6.6 support:** Unity 6.6 replaced the Hierarchy window with a new UI Toolkit version, which hid every row decoration. The new Hierarchy now gets the full set: colours, custom icons, readable labels, tree lines, the component strip, the scene ▾, and right-click-icon for the colour & icon picker. There are also new **Improved Hierarchy** entries in its right-click menu (Colour and Icon…, Reset, Copy All Components, Paste Multi-Copy Buffer).
- On Unity 6.6, **Striped Rows** switches Unity's built-in alternating rows instead of drawing a second set.
- Tested on Unity 2022.3, 6.0, 6.2, 6.4 and 6.6.

## 0.3.1

First release on the Unity Asset Store.

- **Hierarchy:** per-GameObject colours and icons, striped rows, tree lines, a clickable component strip (enable / disable, select, pop out, missing-script warning) and a scene switcher next to each scene name.
- **Inspector:** Copy and Keep Play Mode Values buttons on every component header, the Multi-Copy Buffer (copy many components, paste them onto any selection in one Undo step) and attribute drawers: `[Tab]`, `[Foldout]`, `[Button]`, `[ShowIf]`, `[HideIf]`, `[ReadOnly]`, `[Required]`, `[MinMaxSlider]`, `[ProgressBar]`, `[InfoBox]`.
- **Project window:** folder colours, badge icons and Git status dots.
- **Scene Selector** (`Ctrl+Shift+O`) and **Icon Manager**.
- Editor-only: nothing is added to player builds. All types live in the `ImprovedHierarchy` namespace.
