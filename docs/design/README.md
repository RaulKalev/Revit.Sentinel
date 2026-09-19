# Sentinel UI redesign

Before: `before/` (baseline captured with the UI harness before any change).
After: `after/` (same harness states, plus sheets and accessibility fallbacks).
File names: `<state>_<dark|light>_<width>x<height>.png`, `*_scale150.png` = 144 dpi render.

| State | Before | After |
|---|---|---|
| Doors, missing component | ![](before/01_doors_missing_component_dark_1280x780.png) | ![](after/01_doors_missing_component_dark_1280x780.png) |
| Doors at 980×560 | ![](before/01_doors_missing_component_light_980x560.png) | ![](after/01_doors_missing_component_light_980x560.png) |
| Multi-selection | ![](before/02_doors_multiselect_dark_1280x780.png) | ![](after/02_doors_multiselect_dark_1280x780.png) |
| No selection (disabled actions) | ![](before/03_doors_no_selection_disabled_actions_light_1280x780.png) | ![](after/03_doors_no_selection_disabled_actions_light_1280x780.png) |
| Override validation error | ![](before/04_inspector_validation_error_light_1280x780.png) | ![](after/04_inspector_validation_error_light_1280x780.png) |
| Preview (review workspace) | ![](before/05_preview_dark_1280x780.png) | ![](after/05_preview_dark_1280x780.png) |
| Preview at 980×560 | ![](before/05_preview_light_980x560.png) | ![](after/05_preview_light_980x560.png) |
| Door Sets, validation | ![](before/07_door_sets_validation_error_dark_1280x780.png) | ![](after/07_door_sets_validation_error_dark_1280x780.png) |
| Components, unmapped family | ![](before/09_components_unmapped_light_1280x780.png) | ![](after/09_components_unmapped_light_1280x780.png) |
| Rules | ![](before/06_rules_dark_1280x780.png) | ![](after/06_rules_dark_1280x780.png) |
| Settings | ![](before/10_settings_light_1280x780.png) | ![](after/10_settings_light_1280x780.png) |
| Dialog → in-window sheet | ![](before/11_dialog_dark.png) | ![](after/11_sheet_confirm_dark_1280x780.png) |

Additional after-only captures: `12_sheet_path_*`, `13_sheet_error_*`, `14_doors_solid_materials_*`
(reduced transparency), `15_doors_high_contrast.png`.

## Design system

- **Tokens** (`UI/Themes/Tokens.xaml`): Segoe UI Variable. Type sizes: caption 11, secondary 12, body 13,
  emphasis 14, section 15, title 20. Controls are 32 DIP (28 compact), grid rows 36. Spacing uses 4/8/12/16/24.
  Radii: 4 small, 6 control, 8 inset, 10 card, 12 floating.
- **Palettes** (`DarkTheme.xaml`, `LightTheme.xaml`) hold semantic keys only: `Surface.*`, `Control.*`, `Text.*`,
  `Accent.*`, `Row.*`, `Status.*` with `.Subtle` tints, plus materials. Both themes define every key. Views never
  contain colours.
- **ThemeManager** adds a materials layer on top of the palette:
  - normal: light gradients and edge highlights
  - reduced transparency (Windows "Transparency effects" off): solid surfaces and no shadows
  - high contrast: every key is mapped to `SystemColors`
  It follows system changes at runtime.
- **Styles** (`SentinelStyles.xaml`, loaded once through `SharedDictionary`):
  - buttons: primary / secondary / plain / destructive / icon
  - inputs with inline validation
  - segmented filter, sidebar items, master lists
  - virtualised grid
  - menus, tooltips, scrollbars
  - badges and status glyphs
  Text styles are `Type.*`, so they cannot collide with the `Text.*` brushes.

## Doors workflow

- **Discovery** happens in the toolbar: linked model, scope, Find doors, and Refresh (F5). Search (Ctrl+F) is on
  the right.
- **Status filter** is a segmented control with a count on every segment.
- **Grid** priority is status (glyph and word), door, access direction, set, then reason. Level, Parts and Review
  are dropped first as the list narrows. Below 560 px the status column shrinks to its icon; the word stays in the
  tooltip and the review panel. Row virtualisation is unchanged.
- **Assignment and placement** live in a floating action bar.
  - The bar starts with the scope of every command ("3 doors selected", Clear).
  - Then Assign, then Review and place / Place without review / Update placement.
  - The next step for the current selection becomes the primary (filled) button: Assign, Review, or Update.
  - Infrequent commands go in the "…" menu: suggest from rules, remove set, ignore, zoom, select in Revit.
- **One review workspace** replaces the inspector and preview panels, so nothing is shown twice.
  - A sticky header shows the door, its status and review state, its rooms and its issues. While previewing, an
    accent strip adds "Reviewing door 1 of 3", the next door, Previous/Next (Alt+←/→) and End review (Esc).
  - The scrolling body holds the adjustments: set, direction + Flip, hinge + Swap, components, source details and
    notes. Adjustments during a preview update the preview immediately.
  - A sticky footer, shown only while previewing, has Skip and the confirmation. Its label says what happens:
    "Place and next", "Update and next", "Place door set" or "Update placement".
  - At small window sizes the panel takes at most ~38 % of the width. The panel can be hidden and resized, and both
    choices are remembered.
- **Component rows** start with a one-line summary, for example "Latch jamb +150 mm · Unsecured side · 1000 mm from
  bottom". They also show the state in words and the family mapping.
  - Badges: Override, Added, Removed, and No family (glyph plus word).
  - "Adjust" opens the per-door editor and scrolls it into view. Common fields come first; the rest are behind
    "More placement options", which opens by itself if one of its fields is invalid.
  - Validation runs as you type and is written under the fields. Apply stays disabled until the values are valid.
  - An unapplied edit survives refreshes. The panel's scroll position is kept for the same door and only resets
    when another door is shown.

## Other pages

- **Door Sets** shows collapsed summary cards with a disclosure. Expanded rows are remembered, and a row with an
  invalid value opens by itself.
- **Components**:
  - the missing mapping reads as a red "Unmapped" badge in the list and an error banner in the editor
  - parameters sit behind a disclosure
- **Rules** reads as "If … Then suggest …", with a separate test area.
- **Settings** uses grouped cards with label-left, value-right rows.
- **Dialogs** are now in-window sheets (`SheetDialogService`).
  - The dimmed content cannot be clicked, focus stays in the sheet and returns afterwards.
  - Enter confirms and Esc cancels.
  - Button labels name the result, e.g. "Place 32 sets" or "Delete 4 element(s)".
  - Nothing blocks, so the window stays modeless and `ShowDialog` is never used.

## Window and keyboard

- **Window frame**: WindowChrome keeps native Windows behaviour: Snap, Win+arrows, Alt+Space, native resize
  borders, the DWM shadow and Windows 11 corners.
- **Caption buttons** are the standard Windows minimise, maximise and close.
- **Sidebar**: below 1150 px it becomes icon-only.
- **Shortcuts**:
  - Ctrl+1…5: pages
  - Ctrl+F: search
  - F5: refresh
  - Ctrl+S: save
  - Esc: close sheet, end review, or clear the grid selection
  - Alt+←/→: previous / next door while previewing
- **Focus**: a 2 px ring appears for keyboard focus only. Every icon-only button has an `AutomationProperties.Name`
  and a tooltip. The status line and validation text are live regions.

## Materials and motion: what is approximated

- **No real blur.** WPF has no backdrop blur. The "glass" on the sidebar, toolbar and floating bars is a
  semi-opaque gradient, a 1 px edge highlight and one soft shadow. Only a single layer is translucent at a time.
  Tables, forms and rows sit on opaque surfaces, and nothing is blurred.
- **No springs.** WPF has no spring animation. Panel reveals (a new door, the editor opening, sheets, disclosures)
  use a short ease-out: 180 ms opacity plus a 4–10 px offset. They start from the current value, so they can be
  interrupted.
  - With Windows animations off, reveals are an opacity-only 100 ms fade.
  - Rows are never animated.
  - Nothing runs a continuous render loop.
- **Pressed feedback** is immediate: a darker fill and a 97 % scale on mouse-down.

## Verification

**UI harness** (`Tests/UiHarness`, real window plus fake host): 48 checks pass. New checks cover:

- sheets: open, cancel, path, and queueing
- the editor scrolls into view on Adjust
- an invalid value keeps the edit and its scroll position
- scroll resets when another door is shown
- invalid Door Sets rows open and are remembered

The harness also renders the images above in both themes and at 1280×780, 1440×900 and 980×560. Doors is also
rendered at 144 dpi (150 %), and there are solid-material and high-contrast renders.

Unit tests: 67/67 on net48 and net8. Both targets (net48 for Revit 2024, net8.0-windows for Revit 2026) build.

**Not verified inside Revit.**

- Real DPI scaling, Snap and maximise behaviour of the WindowChrome frame, and the system reduced-motion,
  transparency and high-contrast switches were not exercised live. The harness forces those states instead.
- Screenshots come from `RenderTargetBitmap`: no DWM shadow, and window corners are square in the images.
- Library export and import now ask for a path in a sheet instead of the Windows file dialogs, which are modal.
  There is no Browse button.
