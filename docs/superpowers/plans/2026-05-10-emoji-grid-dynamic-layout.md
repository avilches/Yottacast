# Emoji Grid Dynamic Layout & Context Menu Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the emoji grid fill the available height dynamically (no scrollbar), compute column/row count from cell size, and position the options menu next to the selected emoji cell.

**Architecture:** Introduce `EmojiLayoutConfig` (Core singleton), populated by `ThemeService` on each theme change. `EmojiSearch` reads it when building the grid ViewModel. Context menu positioning is fixed in `PositionOptionsMenu()` by traversing the visual tree to find the selected-cell `Border`.

**Tech Stack:** .NET 9, Avalonia 11, CommunityToolkit.Mvvm, primary-constructor DI via `AddSingleton`

---

## File Map

| File | Action | What changes |
|------|--------|-------------|
| `Yottacast.Core/Search/Emoji/EmojiLayoutConfig.cs` | **CREATE** | New mutable singleton: `Columns`, `ViewportRows` |
| `Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs` | **MODIFY** | `const Columns` → `init` prop; add `ViewportRows` init prop; replace all `AppDefaults.Emoji*` refs |
| `Yottacast.Core/Search/Emoji/EmojiSearch.cs` | **MODIFY** | Accept `EmojiLayoutConfig` in constructor; pass to ViewModel |
| `Yottacast/Services/ThemeService.cs` | **MODIFY** | Accept `EmojiLayoutConfig`; add `CalculateEmojiLayout()`; remove JSON `columns` read; add `cell.margin` read |
| `Yottacast/Themes/dark-default.json` | **MODIFY** | Remove `"columns": 10`; add `"margin": 2` in `cell` |
| `Yottacast/Views/Results/EmojiGridResultView.axaml` | **MODIFY** | Cell `Margin="2"` → `Margin="{DynamicResource Theme.Emoji.Cell.Margin}"` |
| `Yottacast/App.axaml.cs` | **MODIFY** | Register `EmojiLayoutConfig`; update `EmojiSearch` factory; `ThemeService` gets it via DI constructor |
| `Yottacast/Views/MainWindow.axaml` | **MODIFY** | Add `ZIndex="1"` to `OptionsMenuOverlay` Border |
| `Yottacast/Views/MainWindow.axaml.cs` | **MODIFY** | `PositionOptionsMenu()`: emoji branch uses selected-cell position |
| `Yottacast.Core.Tests/Search/EmojiSearchTests.cs` | **MODIFY** | Add `new EmojiLayoutConfig()` to both `EmojiSearch` constructor calls |

---

## Task 1: Create EmojiLayoutConfig

**Files:**
- Create: `Yottacast.Core/Search/Emoji/EmojiLayoutConfig.cs`

- [ ] **Step 1: Create the file**

```csharp
// Yottacast.Core/Search/Emoji/EmojiLayoutConfig.cs
namespace Yottacast.Core.Search.Emoji;

/// <summary>
/// Mutable singleton holding the current emoji grid dimensions.
/// ThemeService writes these values on every theme change;
/// EmojiSearch reads them when constructing EmojiGridResultViewModel.
/// </summary>
public class EmojiLayoutConfig {
    public int Columns      { get; set; } = AppDefaults.EmojiColumns;
    public int ViewportRows { get; set; } = AppDefaults.EmojiViewportRows;
}
```

- [ ] **Step 2: Build to verify it compiles**

```bash
cd Yottacast.Core && dotnet build -v q 2>&1 | grep -E "error|warning|Build"
```
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
cd /path/to/project
git add Yottacast.Core/Search/Emoji/EmojiLayoutConfig.cs
git commit -m "feat: add EmojiLayoutConfig singleton"
```

---

## Task 2: EmojiGridResultViewModel — instance Columns/ViewportRows

**Files:**
- Modify: `Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs`

The ViewModel currently hard-codes `AppDefaults.EmojiColumns` and `AppDefaults.EmojiViewportRows` in 11 places. We convert the `const` to an `init` property so the grid ViewModel carries its own dimensions.

- [ ] **Step 1: Replace the const and add ViewportRows**

Find line:
```csharp
    public const int Columns = AppDefaults.EmojiColumns;
```
Replace with:
```csharp
    public int Columns      { get; init; } = AppDefaults.EmojiColumns;
    public int ViewportRows { get; init; } = AppDefaults.EmojiViewportRows;
```

- [ ] **Step 2: Replace all AppDefaults.EmojiColumns → Columns**

There are 9 occurrences inside method bodies. Use search-and-replace on the file for `AppDefaults.EmojiColumns` → `Columns`. After replacement the file must contain zero remaining `AppDefaults.EmojiColumns` references.

Lines affected (for verification):
- `VisibleSections`: `var pinnedRows = (pinnedCount + AppDefaults.EmojiColumns - 1) / AppDefaults.EmojiColumns;`
- `ComputeVisibleDefaultCount`: `int sectionRows = (sectionCells + AppDefaults.EmojiColumns - 1) / AppDefaults.EmojiColumns;` and `count += remainingRows * AppDefaults.EmojiColumns;`
- `GroupIntoSections`: `int remainder = currentCells.Count % AppDefaults.EmojiColumns;` and `int pad = AppDefaults.EmojiColumns - remainder;`
- `EnsureVisible` (two blocks): `(pinnedCount + AppDefaults.EmojiColumns - 1) / AppDefaults.EmojiColumns` and `rawStart = defaultIndex - defaultVisibleRows * AppDefaults.EmojiColumns + 1` and two more alignment expressions

- [ ] **Step 3: Replace all AppDefaults.EmojiViewportRows → ViewportRows**

There are 2 occurrences in method bodies:
- `VisibleSections`: `var defaultVisibleRows = Math.Max(0, AppDefaults.EmojiViewportRows - pinnedRows);`
- `EnsureVisible`: `var defaultVisibleRows = Math.Max(1, AppDefaults.EmojiViewportRows - pinnedRows);`

Use search-and-replace for `AppDefaults.EmojiViewportRows` → `ViewportRows`.

- [ ] **Step 4: Build to verify zero compilation errors**

```bash
cd Yottacast.Core && dotnet build -v q 2>&1 | grep -E "error|warning|Build"
```
Expected: `Build succeeded.`

- [ ] **Step 5: Run emoji tests to verify behaviour unchanged**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "Emoji" -v n 2>&1 | grep -E "Passed|Failed|Error"
```
Expected: all pass (tests use `AppDefaults` fallback through `init` default).

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs
git commit -m "refactor: EmojiGridResultViewModel Columns/ViewportRows as init props"
```

---

## Task 3: EmojiSearch — accept and propagate EmojiLayoutConfig

**Files:**
- Modify: `Yottacast.Core/Search/Emoji/EmojiSearch.cs`

- [ ] **Step 1: Add EmojiLayoutConfig to the primary constructor**

Current constructor signature (line 14):
```csharp
public class EmojiSearch(ClipboardService clipboard, string emojiCachePath, EmojiDataLoader dataLoader, EmojiUsageStore usageStore, ILogger<EmojiSearch> logger, UserSettings settings) : IInstantSearchSource {
```
Replace with:
```csharp
public class EmojiSearch(ClipboardService clipboard, string emojiCachePath, EmojiDataLoader dataLoader, EmojiUsageStore usageStore, EmojiLayoutConfig emojiLayoutConfig, ILogger<EmojiSearch> logger, UserSettings settings) : IInstantSearchSource {
```

- [ ] **Step 2: Pass Columns/ViewportRows to EmojiGridResultViewModel in MakeGrid()**

Inside `MakeGrid()`, the `grid = new EmojiGridResultViewModel { ... }` initializer (around line 99) — add two properties:
```csharp
grid = new EmojiGridResultViewModel {
    Cells    = cells,
    Icon     = cells.Count > 0 ? cells[0].Char : "",
    Title    = cells.Count > 0 ? cells[0].Name : "",
    Category = "Emoji",
    Score    = 5.5,
    Columns      = emojiLayoutConfig.Columns,
    ViewportRows = emojiLayoutConfig.ViewportRows,
    HasPinnedSection    = hasPinned,
    PinnedSectionHeader = hasPinned ? "Favorites & recently used" : "",
    Actions = [ /* unchanged */ ],
    // ... rest unchanged
};
```

- [ ] **Step 3: Build Core to verify it compiles**

```bash
cd Yottacast.Core && dotnet build -v q 2>&1 | grep -E "error|warning|Build"
```
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add Yottacast.Core/Search/Emoji/EmojiSearch.cs
git commit -m "feat: EmojiSearch injects layout config into grid ViewModel"
```

---

## Task 4: Fix EmojiSearchTests constructor call

**Files:**
- Modify: `Yottacast.Core.Tests/Search/EmojiSearchTests.cs`

The tests create `EmojiSearch` directly. After Task 3, they fail to compile because the constructor has a new `EmojiLayoutConfig` parameter. Fix both factory methods.

- [ ] **Step 1: Update BuildSearchWithCache**

Find (line ~20):
```csharp
var search = new EmojiSearch(new ClipboardService(NullLogger<ClipboardService>.Instance), cachePath, new EmojiDataLoader(NullLogger<EmojiDataLoader>.Instance), usageStore, NullLogger<EmojiSearch>.Instance, settings);
```
Replace with:
```csharp
var search = new EmojiSearch(new ClipboardService(NullLogger<ClipboardService>.Instance), cachePath, new EmojiDataLoader(NullLogger<EmojiDataLoader>.Instance), usageStore, new EmojiLayoutConfig(), NullLogger<EmojiSearch>.Instance, settings);
```

- [ ] **Step 2: Update CreateSearchAsync**

Find (line ~37):
```csharp
var search = new EmojiSearch(clipboard, cachePath, new EmojiDataLoader(NullLogger<EmojiDataLoader>.Instance), usageStore, NullLogger<EmojiSearch>.Instance, settings);
```
Replace with:
```csharp
var search = new EmojiSearch(clipboard, cachePath, new EmojiDataLoader(NullLogger<EmojiDataLoader>.Instance), usageStore, new EmojiLayoutConfig(), NullLogger<EmojiSearch>.Instance, settings);
```

- [ ] **Step 3: Add the missing using if needed**

At the top of the file, add if not present:
```csharp
using Yottacast.Core.Search.Emoji;
```

- [ ] **Step 4: Run all emoji tests**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "Emoji" -v n 2>&1 | grep -E "Passed|Failed|Error"
```
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add Yottacast.Core.Tests/Search/EmojiSearchTests.cs
git commit -m "test: update EmojiSearch constructor call with EmojiLayoutConfig"
```

---

## Task 5: ThemeService — calculate emoji layout from theme values

**Files:**
- Modify: `Yottacast/Services/ThemeService.cs`

- [ ] **Step 1: Add EmojiLayoutConfig to the constructor**

Current declaration (line 21):
```csharp
public sealed class ThemeService(ILogger<ThemeService> logger) : IDisposable {
```
Replace with:
```csharp
public sealed class ThemeService(ILogger<ThemeService> logger, EmojiLayoutConfig emojiLayoutConfig) : IDisposable {
```

Add the required using at the top of the file:
```csharp
using Yottacast.Core.Search.Emoji;
```

- [ ] **Step 2: Add CalculateEmojiLayout private method**

Add this method anywhere in the class (e.g., after `ApplyBuiltinDefault`):

```csharp
private void CalculateEmojiLayout(
    Application app,
    double windowWidth, double maxHeight,
    double resultsPadding, double cellSize, double cellMargin,
    double sectionHeaderSize)
{
    var cellH = cellSize + 2 * cellMargin;

    // Horizontal overhead: outer window border margin (28×2=56)
    //   + ListBox padding (resultsPadding×2) + ListBoxItem padding (10×2=20)
    //   + EmojiGridResultView StackPanel margin (4×2=8)
    var horizontalOverhead = 56 + 2 * resultsPadding + 28;
    var columns = Math.Max(1, (int)Math.Floor((windowWidth - horizontalOverhead) / cellH));

    // Vertical overhead: ListBox padding (resultsPadding×2) + ListBoxItem margin (1×2=2)
    //   + StackPanel outer margin (8+8=16) + info panel TextBlock (~12+6=18)
    //   + 3 section headers each (sectionHeaderSize + margin 6+2=8)
    var sectionHeaderH = (int)(sectionHeaderSize + 8);
    var verticalOverhead = (int)(2 * resultsPadding) + 2 + 16 + 18 + 3 * sectionHeaderH;
    var rows = Math.Max(2, (int)Math.Floor((maxHeight - verticalOverhead) / cellH));

    emojiLayoutConfig.Columns      = columns;
    emojiLayoutConfig.ViewportRows = rows;
    app.Resources["Theme.Emoji.Columns"]       = columns;
    app.Resources["Theme.Emoji.Cell.Margin"]   = new Thickness(cellMargin);
}
```

- [ ] **Step 3: Update Apply() — remove columns read, add margin read, call calculate**

In the emoji block of `Apply()` (around lines 288–307):

**Remove** this line:
```csharp
                SetInt(app,    "Theme.Emoji.Columns",          emoji["columns"]);
```

**After** the existing `SetCornerRadius(app, "Theme.Emoji.Cell.CornerRadius", ...)` line, **add** the margin setter:
```csharp
                SetDouble(app, "Theme.Emoji.Cell.Margin",      emoji["cell"]?["margin"]);
```

**After** all the emoji resource setters (after the `SetOpacity` for `UsageCount.Opacity`), **add** the layout calculation call:
```csharp
                // Calculate columns/rows from theme dimensions
                var windowWidth       = window?["width"]?.GetValue<double>()               ?? 730.0;
                var maxHeight         = results?["maxHeight"]?.GetValue<double>()           ?? 540.0;
                var resultsPadding    = results?["padding"]?.GetValue<double>()             ?? 8.0;
                var cellSize          = emoji["cell"]?["size"]?.GetValue<double>()          ?? 48.0;
                var cellMargin        = emoji["cell"]?["margin"]?.GetValue<double>()        ?? 2.0;
                var sectionHeaderSize = emoji["sectionHeader"]?["size"]?.GetValue<double>() ?? 11.0;
                CalculateEmojiLayout(app, windowWidth, maxHeight, resultsPadding, cellSize, cellMargin, sectionHeaderSize);
```

- [ ] **Step 4: Update ApplyBuiltinDefault() — add cell.margin, call calculate**

In the emoji section of `ApplyBuiltinDefault()` (around lines 445–462):

**Keep** the existing `app.Resources["Theme.Emoji.Columns"] = AppDefaults.EmojiColumns;` line — it will be overwritten by CalculateEmojiLayout but serves as a safe fallback before CalculateEmojiLayout runs.

**After** the existing emoji resources block, **add**:
```csharp
        app.Resources["Theme.Emoji.Cell.Margin"] = new Thickness(2);
        CalculateEmojiLayout(app, windowWidth: 730.0, maxHeight: 540.0,
            resultsPadding: 8.0, cellSize: 48.0, cellMargin: 2.0, sectionHeaderSize: 11.0);
```

- [ ] **Step 5: Build UI project to verify it compiles**

```bash
cd Yottacast && dotnet build -v q 2>&1 | grep -E "error|warning|Build"
```
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add Yottacast/Services/ThemeService.cs
git commit -m "feat: ThemeService calculates emoji grid columns/rows from theme dimensions"
```

---

## Task 6: DI registration — wire EmojiLayoutConfig

**Files:**
- Modify: `Yottacast/App.axaml.cs`

`ThemeService` uses constructor injection, so `EmojiLayoutConfig` just needs to be registered. The `EmojiSearch` factory is explicit, so it needs updating too.

- [ ] **Step 1: Register EmojiLayoutConfig before EmojiSearch**

Find the block:
```csharp
        services.AddSingleton<EmojiUsageStore>(sp => new EmojiUsageStore(
            AppPaths.EmojiUsageFile,
            sp.GetRequiredService<ILogger<EmojiUsageStore>>()));
        services.AddSingleton<EmojiSearch>(sp => new EmojiSearch(
```
Add `EmojiLayoutConfig` registration before `EmojiSearch`:
```csharp
        services.AddSingleton<EmojiUsageStore>(sp => new EmojiUsageStore(
            AppPaths.EmojiUsageFile,
            sp.GetRequiredService<ILogger<EmojiUsageStore>>()));
        services.AddSingleton<EmojiLayoutConfig>();
        services.AddSingleton<EmojiSearch>(sp => new EmojiSearch(
```

- [ ] **Step 2: Add EmojiLayoutConfig to the EmojiSearch factory**

Find:
```csharp
        services.AddSingleton<EmojiSearch>(sp => new EmojiSearch(
            sp.GetRequiredService<ClipboardService>(),
            AppPaths.EmojiCacheFile,
            sp.GetRequiredService<EmojiDataLoader>(),
            sp.GetRequiredService<EmojiUsageStore>(),
            sp.GetRequiredService<ILogger<EmojiSearch>>(),
            sp.GetRequiredService<UserSettings>()));
```
Replace with:
```csharp
        services.AddSingleton<EmojiSearch>(sp => new EmojiSearch(
            sp.GetRequiredService<ClipboardService>(),
            AppPaths.EmojiCacheFile,
            sp.GetRequiredService<EmojiDataLoader>(),
            sp.GetRequiredService<EmojiUsageStore>(),
            sp.GetRequiredService<EmojiLayoutConfig>(),
            sp.GetRequiredService<ILogger<EmojiSearch>>(),
            sp.GetRequiredService<UserSettings>()));
```

- [ ] **Step 3: Build and run all tests**

```bash
cd Yottacast && dotnet build -v q 2>&1 | grep -E "error|warning|Build"
cd ../Yottacast.Core.Tests && dotnet test -v n 2>&1 | grep -E "Passed|Failed|Error|passed|failed"
```
Expected: build succeeded, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add Yottacast/App.axaml.cs
git commit -m "feat: register EmojiLayoutConfig in DI, wire to ThemeService and EmojiSearch"
```

---

## Task 7: Theme JSON + AXAML — cell.margin token

**Files:**
- Modify: `Yottacast/Themes/dark-default.json`
- Modify: `Yottacast/Views/Results/EmojiGridResultView.axaml`

- [ ] **Step 1: Update dark-default.json**

Find the emoji section:
```json
  "emoji": {
    "columns": 10,
    "cell":     { "size": 48, "cornerRadius": 8 },
```
Replace with:
```json
  "emoji": {
    "cell":     { "size": 48, "cornerRadius": 8, "margin": 2 },
```

The `"columns": 10` line is fully removed. The rest of the emoji section is unchanged.

- [ ] **Step 2: Update EmojiGridResultView.axaml — use DynamicResource for margin**

Find in `EmojiGridResultView.axaml` (around line 27):
```xml
                                    <Border Width="{DynamicResource Theme.Emoji.Cell.Size}"
                                            Height="{DynamicResource Theme.Emoji.Cell.Size}"
                                            CornerRadius="{DynamicResource Theme.Emoji.Cell.CornerRadius}" Margin="2"
```
Replace with:
```xml
                                    <Border Width="{DynamicResource Theme.Emoji.Cell.Size}"
                                            Height="{DynamicResource Theme.Emoji.Cell.Size}"
                                            CornerRadius="{DynamicResource Theme.Emoji.Cell.CornerRadius}"
                                            Margin="{DynamicResource Theme.Emoji.Cell.Margin}"
```

- [ ] **Step 3: Build to verify**

```bash
cd Yottacast && dotnet build -v q 2>&1 | grep -E "error|warning|Build"
```
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add Yottacast/Themes/dark-default.json Yottacast/Views/Results/EmojiGridResultView.axaml
git commit -m "feat: emoji cell.margin theme token, remove hardcoded columns from JSON"
```

---

## Task 8: Context menu — position by selected emoji cell

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml`
- Modify: `Yottacast/Views/MainWindow.axaml.cs`

- [ ] **Step 1: Add ZIndex to OptionsMenuOverlay in AXAML**

Find in `MainWindow.axaml` (around line 414):
```xml
                <Border x:Name="OptionsMenuOverlay"
                        IsVisible="{Binding IsOptionsMenuOpen}"
                        Background="{DynamicResource Theme.Menu.Background}"
```
Replace with:
```xml
                <Border x:Name="OptionsMenuOverlay"
                        IsVisible="{Binding IsOptionsMenuOpen}"
                        ZIndex="1"
                        Background="{DynamicResource Theme.Menu.Background}"
```

- [ ] **Step 2: Rewrite PositionOptionsMenu() in MainWindow.axaml.cs**

Find the existing `PositionOptionsMenu()` method (around line 501) — the entire method body:
```csharp
    private void PositionOptionsMenu() {
        if (DataContext is not MainWindowViewModel vm || !vm.IsOptionsMenuOpen || vm.SelectedResult == null)
            return;
        var container = ResultsList.ContainerFromItem(vm.SelectedResult) as ListBoxItem;
        if (container == null) return;
        var pos = container.TranslatePoint(new Point(0, 0), ResultsPanel);
        if (!pos.HasValue) return;

        // Menu top aligned with item top, clamped so the menu stays within the results
        // panel → no window growth. If menu is taller than the panel, starts at 0 and
        // the window grows DOWN only (never up+down).
        var itemTop = pos.Value.Y;
        var panelH  = ResultsList.Bounds.Height;
        var menuH   = OptionsMenuOverlay.Bounds.Height;

        var top = itemTop;
        if (panelH > 0 && menuH > 0 && top + menuH > panelH)
            top = Math.Max(0, panelH - menuH);

        OptionsMenuOverlay.Margin = new Thickness(8, top, 8, 0);
    }
```
Replace with:
```csharp
    private void PositionOptionsMenu() {
        if (DataContext is not MainWindowViewModel vm || !vm.IsOptionsMenuOpen || vm.SelectedResult == null)
            return;

        double? rawTop = null;

        // For the emoji grid, the whole grid is a single ListBoxItem.
        // Traverse the visual tree to find the selected-cell Border so the menu
        // appears next to the actual emoji rather than at the top of the grid.
        if (vm.SelectedResult is EmojiGridResultViewModel) {
            var selectedCell = ResultsList
                .GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("emoji-selected"));
            if (selectedCell != null) {
                var pos = selectedCell.TranslatePoint(new Point(0, 0), ResultsPanel);
                if (pos.HasValue) rawTop = pos.Value.Y;
            }
        }

        if (!rawTop.HasValue) {
            var container = ResultsList.ContainerFromItem(vm.SelectedResult) as ListBoxItem;
            if (container == null) return;
            var pos = container.TranslatePoint(new Point(0, 0), ResultsPanel);
            if (!pos.HasValue) return;
            rawTop = pos.Value.Y;
        }

        var top    = rawTop.Value;
        var panelH = ResultsList.Bounds.Height;
        var menuH  = OptionsMenuOverlay.Bounds.Height;
        if (panelH > 0 && menuH > 0 && top + menuH > panelH)
            top = Math.Max(0, panelH - menuH);

        OptionsMenuOverlay.Margin = new Thickness(8, top, 8, 0);
    }
```

- [ ] **Step 3: Build to verify**

```bash
cd Yottacast && dotnet build -v q 2>&1 | grep -E "error|warning|Build"
```
Expected: `Build succeeded.`

- [ ] **Step 4: Run all tests**

```bash
cd Yottacast.Core.Tests && dotnet test -v n 2>&1 | grep -E "passed|failed|error"
```
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add Yottacast/Views/MainWindow.axaml Yottacast/Views/MainWindow.axaml.cs
git commit -m "fix: emoji options menu positioned at selected cell, not grid top"
```

---

## Task 9: Manual verification

- [ ] **Step 1: Run the app**

```bash
cd Yottacast && dotnet run
```

- [ ] **Step 2: Verify dynamic grid**
  - Type `:` — the emoji grid should display without a vertical scrollbar
  - Check that the number of visible columns has changed (expect ~12 columns with the default 730px window vs the previous 10)
  - Resize behavior: if you change `results.maxHeight` in `dark-default.json` (e.g., to 400), restart, type `:`, and verify fewer rows are visible and no scrollbar appears

- [ ] **Step 3: Verify context menu positioning**
  - Type `:`
  - Navigate to an emoji in the middle or bottom of the grid with arrow keys
  - Press `Tab` to open the options menu
  - Verify the menu appears vertically aligned with the selected emoji cell, not at the top of the grid

- [ ] **Step 4: Verify hot-reload**
  - With the app running, change `emoji.cell.margin` in `dark-default.json` (e.g., from 2 to 4)
  - The grid should immediately reflow with larger cell gaps and re-computed column/row count
