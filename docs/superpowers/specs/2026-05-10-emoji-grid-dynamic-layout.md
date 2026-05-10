# Emoji Grid — Dynamic Layout & Context Menu Fix

Date: 2026-05-10

## Problem

Two issues with the emoji grid:

1. **Context menu positioning**: the options menu appears at the top-right of the full emoji
   container (the whole grid is one `ListBoxItem`). The selected emoji cell may be anywhere
   in the grid, so the menu appears far from it and visually behind the emoji cells.

2. **Fixed grid size causes spurious scrollbar**: `EmojiColumns = 10` and
   `EmojiViewportRows = 8` are compile-time constants. When `Theme.Results.MaxHeight` is
   set smaller, the rendered grid overflows the ListBox and shows a useless vertical
   scrollbar. The scrollbar is useless because the ViewModel only exposes a viewport slice
   of emojis, not the full list.

---

## Design

### 1 — Context menu: position by selected emoji cell

**Root cause**: `PositionOptionsMenu()` uses the `ListBoxItem` container's top as the Y
coordinate for the menu. For emoji there is only one ListBoxItem (the whole grid), so
`itemTop ≈ 0` regardless of which cell is selected.

**Fix**:

- Add explicit `ZIndex="1"` to `OptionsMenuOverlay` in `MainWindow.axaml` to guarantee
  it always renders on top.

- In `PositionOptionsMenu()`, detect emoji mode and traverse the visual tree to find the
  `Border` with CSS class `emoji-selected`. Use its `Y` coordinate (translated to
  `ResultsPanel` space) as the menu's `top`, instead of the container's `Y`. Keep the
  existing clamping logic (menu stays within panel height).

- Horizontal position is unchanged: the menu remains right-aligned (`HorizontalAlignment=
  "Right"`, `Margin.Left=8`, `Margin.Right=8`).

```
PositionOptionsMenu():
  if vm.SelectedResult is EmojiGridResultViewModel:
    selectedBorder = ResultsList.visual-descendants.OfType<Border>
                       .FirstOrDefault(b => b.Classes.Contains("emoji-selected"))
    if selectedBorder != null:
      pos = selectedBorder.TranslatePoint((0,0), ResultsPanel)
      top = pos.Y  (clamped so menu stays in panel)
      OptionsMenuOverlay.Margin = Thickness(8, top, 8, 0)
      return
  // existing code for non-emoji items
```

**Files changed**:
- `Yottacast/Views/MainWindow.axaml` — add `ZIndex="1"` to `OptionsMenuOverlay`
- `Yottacast/Views/MainWindow.axaml.cs` — update `PositionOptionsMenu()`

---

### 2 — Dynamic grid layout

#### Architecture

Introduce `EmojiLayoutConfig` — a mutable singleton in Core that holds the current
computed `Columns` and `ViewportRows`. `ThemeService` (UI layer) calculates and writes it
when applying a theme; `EmojiSearch` (Core) reads it when constructing the grid ViewModel.

```
ThemeService  ──writes──▶  EmojiLayoutConfig  ◀──reads──  EmojiSearch
                                                                │
                                                  EmojiGridResultViewModel
                                                   (Columns, ViewportRows as init props)
```

#### EmojiLayoutConfig (new, Core)

```csharp
// Yottacast.Core/Search/Emoji/EmojiLayoutConfig.cs
public class EmojiLayoutConfig {
    public int Columns     { get; set; } = AppDefaults.EmojiColumns;
    public int ViewportRows { get; set; } = AppDefaults.EmojiViewportRows;
}
```

#### Calculation in ThemeService

After reading all theme values, `ThemeService` calculates columns and rows:

```
cellH = cellSize + 2 * cellMargin          // total cell height/width (square cells)

// Horizontal: outer-border-margin×2 (56) + results-padding×2 + listboxItem-padding×2 (20) + stackPanel-margin×2 (8)
horizontalOverhead = 56 + 2*resultsPadding + 28
columns = floor((windowWidth - horizontalOverhead) / cellH)

// Vertical: results-padding×2 + listboxItem-margin (2) + stackPanel-margin×2 (16)
//           + info-panel (18) + 3 section-header allowance (3 × (sectionHeaderSize + 8))
sectionHeaderH    = (int)(sectionHeaderSize + 8)
verticalOverhead  = 2*resultsPadding + 2 + 16 + 18 + 3 * sectionHeaderH
rows = floor((maxResultsHeight - verticalOverhead) / cellH)
```

With default theme values (windowWidth=730, maxHeight=540, cellSize=48, cellMargin=2,
resultsPadding=8, sectionHeaderSize=11):
- `cellH = 52`, `horizontalOverhead = 100`, `columns = floor(630/52) = 12`
- `sectionHeaderH = 19`, `verticalOverhead = 109`, `rows = floor(431/52) = 8`

`columns` is clamped to `max(1, columns)` and `rows` to `max(2, rows)`.

`ThemeService` sets:
- `emojiLayoutConfig.Columns = columns`
- `emojiLayoutConfig.ViewportRows = rows`
- `app.Resources["Theme.Emoji.Columns"] = columns` (for `UniformGrid.Columns` in AXAML)
- `app.Resources["Theme.Emoji.Cell.Margin"] = new Thickness(cellMargin)` (new resource)

`Theme.Emoji.Columns` is no longer read from the theme JSON (it is derived). Reading
`emoji["columns"]` is removed from `ThemeService.ApplyTheme()`.

#### Theme JSON changes (dark-default.json)

```json
// REMOVE:
"columns": 10

// ADD inside emoji.cell:
"margin": 2
```

The full emoji section becomes:
```json
"emoji": {
  "cell":     { "size": 48, "cornerRadius": 8, "margin": 2 },
  "char":     { "size": 48, "fontFamily": "..." },
  "keywords": { ... },
  "sectionHeader": { ... },
  "favorite":  { ... },
  "usageCount":{ ... }
}
```

#### EmojiGridResultViewModel changes

- `public const int Columns = AppDefaults.EmojiColumns;` → `public int Columns { get; init; } = AppDefaults.EmojiColumns;`
- Add `public int ViewportRows { get; init; } = AppDefaults.EmojiViewportRows;`
- Replace every `AppDefaults.EmojiColumns` reference in the ViewModel with `Columns`
- Replace every `AppDefaults.EmojiViewportRows` reference with `ViewportRows`

The object-initializer construction syntax in `EmojiSearch` and the IPC test are both
compatible; tests that don't set these properties get the `AppDefaults` fallback values.

#### EmojiSearch changes

- Constructor gains `EmojiLayoutConfig config` parameter
- `MakeGrid()` sets `Columns = _config.Columns` and `ViewportRows = _config.ViewportRows`
  in the object initializer for `EmojiGridResultViewModel`

#### AXAML changes (EmojiGridResultView.axaml)

```xml
<!-- BEFORE -->
<Border Width="{DynamicResource Theme.Emoji.Cell.Size}"
        Height="{DynamicResource Theme.Emoji.Cell.Size}"
        ... Margin="2">

<!-- AFTER -->
<Border Width="{DynamicResource Theme.Emoji.Cell.Size}"
        Height="{DynamicResource Theme.Emoji.Cell.Size}"
        ... Margin="{DynamicResource Theme.Emoji.Cell.Margin}">
```

#### DI registration (App.axaml.cs)

```csharp
// Add before EmojiSearch registration:
services.AddSingleton<EmojiLayoutConfig>();

// Update EmojiSearch factory to include EmojiLayoutConfig:
services.AddSingleton<EmojiSearch>(sp => new EmojiSearch(
    sp.GetRequiredService<ClipboardService>(),
    AppPaths.EmojiCacheFile,
    sp.GetRequiredService<EmojiDataLoader>(),
    sp.GetRequiredService<EmojiUsageStore>(),
    sp.GetRequiredService<EmojiLayoutConfig>(),   // NEW
    sp.GetRequiredService<ILogger<EmojiSearch>>(),
    sp.GetRequiredService<UserSettings>()));

// ThemeService: add EmojiLayoutConfig to constructor (resolved via DI automatically,
// no change needed in the AddSingleton<ThemeService>() registration since ThemeService
// uses constructor injection and DI resolves it)
```

`ThemeService` constructor gains `EmojiLayoutConfig emojiLayoutConfig` parameter.

#### ThemeService defaults (SetResourceDefaults)

```csharp
// REMOVE:
app.Resources["Theme.Emoji.Columns"] = AppDefaults.EmojiColumns;

// ADD:
app.Resources["Theme.Emoji.Cell.Margin"] = new Thickness(2);
// Theme.Emoji.Columns is set by CalculateEmojiLayout(), called after defaults
```

---

## Invariants

- The grid never shows a vertical scrollbar: `viewportRows` is sized so that
  `rendered height ≤ Theme.Results.MaxHeight`.
- `columns` and `viewportRows` are always `≥ 1` and `≥ 2` respectively.
- `Theme.Emoji.Columns` (Avalonia resource) always equals `EmojiLayoutConfig.Columns`;
  no split between visual and ViewModel column count.
- The options menu appears vertically adjacent to the selected emoji cell, not at the
  top of the grid.

---

## Files changed

| File | Change |
|------|--------|
| `Yottacast.Core/Search/Emoji/EmojiLayoutConfig.cs` | NEW |
| `Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs` | init props + replace AppDefaults refs |
| `Yottacast.Core/Search/Emoji/EmojiSearch.cs` | accept EmojiLayoutConfig, pass to ViewModel |
| `Yottacast/Services/ThemeService.cs` | calculate layout, write EmojiLayoutConfig |
| `Yottacast/Views/Results/EmojiGridResultView.axaml` | Margin → DynamicResource |
| `Yottacast/Themes/dark-default.json` | remove columns, add cell.margin |
| `Yottacast/Views/MainWindow.axaml` | ZIndex on OptionsMenuOverlay |
| `Yottacast/Views/MainWindow.axaml.cs` | PositionOptionsMenu emoji branch |
| `Yottacast/App.axaml.cs` | DI: add EmojiLayoutConfig, update EmojiSearch factory |

No test changes required: `EmojiSearchTests` and `EmojiDataLoaderTests` don't test
layout parameters; `Yottacast.Ipc.Tests` object-initializer construction is compatible
with `init` properties with defaults.
