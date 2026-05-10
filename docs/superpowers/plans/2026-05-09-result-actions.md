# Result Actions System — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace discrete action callbacks (`OnActivate`, `OnCopy`, `OnToggleFavorite`, `PasteAfterActivate`, `CopiedMessage`, `CopiedMessageProvider`) with a unified `Actions: IReadOnlyList<ResultAction>` per result, plus an overlay menu (Tab) that lists available options.

**Architecture:** Each search source declares a list of `ResultAction` objects with label, optional hotkey, display flags, and behavior flags. The UI consumes the list generically — footer hints, overlay menu, and keyboard handling all derive from it. A platform-agnostic `ActionHotkey` record lives in Core; `AppHandler` in the UI project handles formatting and matching.

**Tech Stack:** .NET 9, C#, Avalonia 11.3.12, CommunityToolkit.Mvvm, xUnit

---

## File Map

| File | Change |
|------|--------|
| `Yottacast.Core/ViewModels/ActionHotkey.cs` | **Create** — platform-agnostic hotkey descriptor |
| `Yottacast.Core/ViewModels/ResultAction.cs` | **Create** — unified action model |
| `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs` | **Modify** — remove old callbacks, add `Actions` |
| `Yottacast.Core/Search/Application/ApplicationSearch.cs` | **Modify** — migrate to Actions |
| `Yottacast.Core/Search/Emoji/EmojiSearch.cs` | **Modify** — migrate to Actions |
| `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` | **Modify** — migrate to Actions |
| `Yottacast.Core/Search/WebSearch/WebSearchSource.cs` | **Modify** — migrate to Actions |
| `Yottacast.Core/Search/Dictionary/DictionarySource.cs` | **Modify** — migrate to Actions |
| `Yottacast.Core/Search/LocalPath/LocalPathSearch.cs` | **Modify** — migrate to Actions |
| `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs` | **Modify** — migrate to Actions |
| `Yottacast.Core/Search/Url/UrlSearch.cs` | **Modify** — migrate to Actions |
| `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs` | **Modify** — migrate to Actions |
| `Yottacast.Core/Search/Clipboard/ClipboardSearch.cs` | **Modify** — migrate to Actions |
| `Yottacast/Services/AppHandler.cs` | **Modify** — add `MatchesHotkey`, `FormatHotkey` |
| `Yottacast/ViewModels/MainWindowViewModel.cs` | **Modify** — new FooterHints, overlay state |
| `Yottacast/Views/MainWindow.axaml.cs` | **Modify** — generic key handler, overlay navigation |
| `Yottacast/Views/MainWindow.axaml` | **Modify** — wrap ResultsList in Panel, add overlay |
| `Yottacast.Ipc/Mapping/ResultMapper.cs` | **Modify** — replace PasteAfterActivate lookup |
| `Yottacast.Ipc/Services/SearchGrpcService.cs` | **Modify** — dispatch by hotkey instead of callbacks |
| `Yottacast.Core.Tests/Search/ApplicationSearchTests.cs` | **Modify** — use Actions |
| `Yottacast.Core.Tests/Search/EmojiSearchTests.cs` | **Modify** — use Actions |
| `Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs` | **Modify** — use Actions |
| `Yottacast.Core.Tests/Search/Calculator/UnitConverterSearchTests.cs` | **Modify** — use Actions |
| `Yottacast.Core.Tests/Search/ClipboardSearchTests.cs` | **Modify** — use Actions |
| `Yottacast.Core.Tests/Search/LocalPathSearchTests.cs` | **Modify** — use Actions |
| `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs` | **Modify** — use Actions |
| `Yottacast.Core.Tests/Search/UrlSearchTests.cs` | **Modify** — use Actions |
| `Yottacast.Core.Tests/Search/UserDocumentSearchTests.cs` | **Modify** — use Actions |
| `Yottacast.Ipc.Tests/Mapping/ResultMapperTests.cs` | **Modify** — remove PasteAfterActivate |
| `docs/result-viewmodels.md` | **Modify** — document Actions |

> ⚠️ **Compilation note:** After Task 2 the solution won't compile until Tasks 3–9 are complete. Run `dotnet build` only after Task 9 (or after each task to see remaining errors).

---

### Task 1: Create `ActionHotkey` and `ResultAction`

**Files:**
- Create: `Yottacast.Core/ViewModels/ActionHotkey.cs`
- Create: `Yottacast.Core/ViewModels/ResultAction.cs`

- [ ] **Step 1: Create `ActionHotkey.cs`**

```csharp
// Yottacast.Core/ViewModels/ActionHotkey.cs
namespace Yottacast.Core.ViewModels;

public enum ActionModifiers { None = 0, Meta = 1, Shift = 2, MetaShift = 3 }

/// <summary>
/// Platform-agnostic hotkey descriptor. "Meta" resolves to Cmd (macOS) or Ctrl (Windows/Linux)
/// at the UI layer. Key names follow Avalonia's Key enum (e.g. "C", "F", "Return", "Tab").
/// </summary>
public sealed record ActionHotkey(string Key, ActionModifiers Modifiers = ActionModifiers.None) {
    public static readonly ActionHotkey Enter      = new("Return");
    public static readonly ActionHotkey MetaC      = new("C", ActionModifiers.Meta);
    public static readonly ActionHotkey MetaShiftF = new("F", ActionModifiers.MetaShift);
}
```

- [ ] **Step 2: Create `ResultAction.cs`**

```csharp
// Yottacast.Core/ViewModels/ResultAction.cs
namespace Yottacast.Core.ViewModels;

public sealed class ResultAction {
    /// <summary>Display label shown in overlay and footer (e.g. "Open", "Copy path").</summary>
    public required string Label { get; init; }

    /// <summary>Optional hotkey. Null = action only accessible via overlay or mouse.</summary>
    public ActionHotkey? Hotkey { get; init; }

    /// <summary>Whether to show this action's hint in the footer bar. Only meaningful when Hotkey != null.</summary>
    public bool ShowInFooter { get; init; }

    /// <summary>Whether to include this action in the Tab overlay menu.</summary>
    public bool ShowInMenu { get; init; }

    /// <summary>Whether to close the overlay after executing (when opened via Tab).</summary>
    public bool ClosesMenu { get; init; }

    /// <summary>Whether to hide the Yottacast window after executing.</summary>
    public bool ClosesWindow { get; init; }

    /// <summary>
    /// Whether to simulate Cmd+V / Ctrl+V after closing the window.
    /// Only meaningful when ClosesWindow = true.
    /// </summary>
    public bool PasteAfterClose { get; init; }

    /// <summary>
    /// When true, the main window calls RefreshSearch() after Execute().
    /// Used by EmojiSearch's Favorite action to re-rank the emoji grid.
    /// </summary>
    public bool RequiresRefresh { get; init; }

    /// <summary>
    /// Returns a message shown in the search hint area after executing (e.g. "Path copied!").
    /// Null = no message. Only shown when ClosesWindow = false.
    /// </summary>
    public Func<string?>? HintProvider { get; init; }

    /// <summary>The action callback invoked on execution.</summary>
    public required Action Execute { get; init; }
}
```

- [ ] **Step 3: Commit**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
git add Yottacast.Core/ViewModels/ActionHotkey.cs Yottacast.Core/ViewModels/ResultAction.cs
git commit -m "feat: add ActionHotkey and ResultAction data types"
```

---

### Task 2: Migrate `BaseResultItemViewModel`

**Files:**
- Modify: `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs`

> ⚠️ After this task the solution won't compile until all sources and UI are updated (Tasks 3–9).

- [ ] **Step 1: Replace content of `BaseResultItemViewModel.cs`**

```csharp
// Yottacast.Core/ViewModels/BaseResultItemViewModel.cs
namespace Yottacast.Core.ViewModels;

/// <summary>
/// Shared base for all result items. Contains properties needed for scoring,
/// key-event routing, and the unified action list.
/// </summary>
public abstract class BaseResultItemViewModel {
    public double Score { get; init; }
    public string Title { get; init; } = "";

    /// <summary>
    /// All available actions for this result. The UI derives footer hints, overlay menu,
    /// and keyboard shortcuts from this list.
    /// </summary>
    public IReadOnlyList<ResultAction> Actions { get; init; } = [];

    /// <summary>
    /// When non-null, the item captures LEFT/RIGHT/UP/DOWN arrow keys while selected.
    /// Used for grids (Emoji) and multi-cell converters.
    /// Returns true if the key was consumed, false to fall through to the default handler.
    /// </summary>
    public Func<bool>? OnLeft  { get; init; }
    public Func<bool>? OnRight { get; init; }
    public Func<bool>? OnUp   { get; init; }
    public Func<bool>? OnDown { get; init; }

    /// <summary>
    /// When true, the item is never discarded by the SearchSourceLimit cap.
    /// Used by WebSearch and Dictionary to always appear in results.
    /// </summary>
    public bool BypassLimit { get; init; }
}
```

- [ ] **Step 2: Commit**

```bash
git add Yottacast.Core/ViewModels/BaseResultItemViewModel.cs
git commit -m "feat: replace discrete action callbacks with Actions list in BaseResultItemViewModel"
```

---

### Task 3: Migrate `ApplicationSearch` + update tests

**Files:**
- Modify: `Yottacast.Core/Search/Application/ApplicationSearch.cs`
- Modify: `Yottacast.Core.Tests/Search/ApplicationSearchTests.cs`

- [ ] **Step 1: Update test — find the test `CreateResultItem_HasOnCopyAndCopiedMessage` in `ApplicationSearchTests.cs` and replace it**

```csharp
[Fact]
public async Task CreateResultItem_HasOpenAndCopyActions() {
    var (search, clipboard, _) = await CreateSearchAsync();
    var item = search.CreateResultItem(new AppInfo { Name = "Safari", Path = "/Applications/Safari.app" });

    Assert.Equal(2, item.Actions.Count);

    var open = item.Actions[0];
    Assert.Equal("Open", open.Label);
    Assert.Equal(ActionHotkey.Enter, open.Hotkey);
    Assert.True(open.ShowInFooter);
    Assert.True(open.ShowInMenu);
    Assert.True(open.ClosesWindow);

    var copy = item.Actions[1];
    Assert.Equal("Copy path", copy.Label);
    Assert.Equal(ActionHotkey.MetaC, copy.Hotkey);
    Assert.True(copy.ShowInFooter);
    Assert.True(copy.ShowInMenu);
    Assert.False(copy.ClosesWindow);

    copy.Execute();
    Assert.Equal("/Applications/Safari.app", clipboard.LastCopied);

    var hint = copy.HintProvider?.Invoke();
    Assert.Equal("Path copied!", hint);
}
```

Also find any test that calls `item.OnActivate` or checks `item.OnActivate != null` and update to use `item.Actions[0].Execute()` / `item.Actions[0] != null`.

- [ ] **Step 2: Run the test to see it fail**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Core.Tests"
dotnet test --filter "ApplicationSearchTests" 2>&1 | tail -20
```

Expected: compile error (Actions property doesn't exist yet on item, OnActivate removed).

- [ ] **Step 3: Update `ApplicationSearch.cs` — replace `OnActivate`/`OnCopy`/`CopiedMessage` with `Actions`**

Find `CreateResultItem` method (around line 79) and replace:
```csharp
// OLD:
OnActivate = () => platform.LaunchApp(path),
OnCopy = () => clipboard.CopyText(path),
CopiedMessage = "Path copied!",

// NEW:
Actions = [
    new() {
        Label       = "Open",
        Hotkey      = ActionHotkey.Enter,
        ShowInFooter = true,
        ShowInMenu  = true,
        ClosesMenu  = true,
        ClosesWindow = true,
        Execute     = () => platform.LaunchApp(path),
    },
    new() {
        Label        = "Copy path",
        Hotkey       = ActionHotkey.MetaC,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        HintProvider = () => "Path copied!",
        Execute      = () => clipboard.CopyText(path),
    },
],
```

- [ ] **Step 4: Run the test**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Core.Tests"
dotnet test --filter "ApplicationSearchTests" 2>&1 | tail -20
```

Expected: tests still fail due to compile errors in other files. That's OK — the logic is correct.

- [ ] **Step 5: Commit**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
git add Yottacast.Core/Search/Application/ApplicationSearch.cs \
        Yottacast.Core.Tests/Search/ApplicationSearchTests.cs
git commit -m "feat: migrate ApplicationSearch to Actions"
```

---

### Task 4: Migrate `EmojiSearch` + update tests

**Files:**
- Modify: `Yottacast.Core/Search/Emoji/EmojiSearch.cs`
- Modify: `Yottacast.Core.Tests/Search/EmojiSearchTests.cs`

- [ ] **Step 1: Update EmojiSearchTests — replace action tests**

Find and replace these tests:

```csharp
// REPLACE test "OnActivate_CopiesCharToClipboard" with:
[Fact]
public async Task PasteAction_CopiesAndRecordsUsage() {
    var (search, clipboard, usageStore) = await CreateSearchAsync();
    var result = (EmojiGridResultViewModel)search.Search("smile", 10).Single();
    var paste = result.Actions.Single(a => a.Label == "Paste");

    paste.Execute();

    Assert.NotEmpty(clipboard.LastCopied ?? "");
    Assert.True(usageStore.GetUsageCount(result.Cells[0].Char) > 0);
}

// REPLACE test "OnCopy_CopiesWithoutPasteAfterActivate" with:
[Fact]
public async Task CopyAction_HasNoClosesWindow() {
    var (search, _, _) = await CreateSearchAsync();
    var result = (EmojiGridResultViewModel)search.Search("smile", 10).Single();
    var copy = result.Actions.Single(a => a.Label == "Copy");

    Assert.False(copy.ClosesWindow);
    Assert.True(copy.ShowInFooter);
    Assert.True(copy.ShowInMenu);
}

// REPLACE test "OnCopy_HasCopiedMessage" with:
[Fact]
public async Task CopyAction_HasDynamicHint() {
    var (search, _, _) = await CreateSearchAsync();
    var result = (EmojiGridResultViewModel)search.Search("smile", 10).Single();
    var copy = result.Actions.Single(a => a.Label == "Copy");
    var char0 = result.Cells[0].Char;

    var hint = copy.HintProvider?.Invoke();

    Assert.Equal($"Emoji {char0} copied!", hint);
}

// REPLACE test related to PasteAfterActivate on grid:
[Fact]
public async Task PasteAction_HasPasteAfterClose() {
    var (search, _, _) = await CreateSearchAsync();
    var result = (EmojiGridResultViewModel)search.Search("smile", 10).Single();
    var paste = result.Actions.Single(a => a.Label == "Paste");

    Assert.True(paste.PasteAfterClose);
    Assert.True(paste.ClosesWindow);
}

// REPLACE test "OnToggleFavorite_UpdatesCellAndStore" with:
[Fact]
public async Task FavoriteAction_TogglesFavoriteInStoreAndCells() {
    var (search, _, usageStore) = await CreateSearchAsync();
    var result = (EmojiGridResultViewModel)search.Search("smile", 10).Single();
    var fav = result.Actions.Single(a => a.Label == "Favorite");
    var char0 = result.Cells[0].Char;

    Assert.False(result.Cells[0].IsFavorite);
    fav.Execute();
    Assert.True(usageStore.IsFavorite(char0));
    Assert.True(result.Cells[0].IsFavorite);

    fav.Execute();
    Assert.False(usageStore.IsFavorite(char0));
    Assert.False(result.Cells[0].IsFavorite);
}

// ADD new test:
[Fact]
public async Task FavoriteAction_HasRequiresRefresh() {
    var (search, _, _) = await CreateSearchAsync();
    var result = (EmojiGridResultViewModel)search.Search("smile", 10).Single();
    var fav = result.Actions.Single(a => a.Label == "Favorite");

    Assert.True(fav.RequiresRefresh);
    Assert.False(fav.ClosesWindow);
    Assert.False(fav.ClosesMenu);
}
```

- [ ] **Step 2: Update `EmojiSearch.cs` — replace action assignments**

Find the section where `EmojiGridResultViewModel grid` is built (around line 98) and replace:
```csharp
// REMOVE these lines:
PasteAfterActivate = true,
CopiedMessageProvider = () => { ... },
OnActivate = () => { ... },
OnCopy = () => { ... },
OnToggleFavorite = () => { ... },

// ADD:
Actions = [
    new() {
        Label        = "Paste",
        Hotkey       = ActionHotkey.Enter,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        ClosesWindow  = true,
        PasteAfterClose = true,
        HintProvider = () => {
            var cell = grid.Cells[grid.SelectedEmojiIndex];
            return $"Emoji {cell.Char} copied!";
        },
        Execute = () => {
            var cell = grid.Cells[grid.SelectedEmojiIndex];
            logger.LogInformation("Emoji: copied {Char} ({Name})", cell.Char, cell.Name);
            clipboard.CopyText(cell.Char);
            usageStore.RecordUsage(cell.Char);
        },
    },
    new() {
        Label        = "Copy",
        Hotkey       = ActionHotkey.MetaC,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        HintProvider = () => {
            var cell = grid.Cells[grid.SelectedEmojiIndex];
            return $"Emoji {cell.Char} copied!";
        },
        Execute = () => {
            var cell = grid.Cells[grid.SelectedEmojiIndex];
            logger.LogInformation("Emoji: copied (no paste) {Char} ({Name})", cell.Char, cell.Name);
            clipboard.CopyText(cell.Char);
            usageStore.RecordUsage(cell.Char);
        },
    },
    new() {
        Label          = "Favorite",
        Hotkey         = ActionHotkey.MetaShiftF,
        ShowInFooter   = true,
        ShowInMenu     = true,
        ClosesMenu     = false,
        ClosesWindow   = false,
        RequiresRefresh = true,
        Execute = () => {
            var cell = grid.Cells[grid.SelectedEmojiIndex];
            usageStore.ToggleFavorite(cell.Char);
            var isFav = usageStore.IsFavorite(cell.Char);
            foreach (var c in grid.Cells.Where(c => c.Char == cell.Char))
                c.IsFavorite = isFav;
            logger.LogInformation("Emoji: favorite toggled {Char} ({Name}) -> {IsFav}",
                cell.Char, cell.Name, isFav);
        },
    },
],
```

- [ ] **Step 3: Commit**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
git add Yottacast.Core/Search/Emoji/EmojiSearch.cs \
        Yottacast.Core.Tests/Search/EmojiSearchTests.cs
git commit -m "feat: migrate EmojiSearch to Actions"
```

---

### Task 5: Migrate `CalculatorSearch` + update tests

**Files:**
- Modify: `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`
- Modify: `Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs`
- Modify: `Yottacast.Core.Tests/Search/Calculator/UnitConverterSearchTests.cs`

- [ ] **Step 1: Update `CalculatorSearchTests.cs`**

Find and replace these tests:
```csharp
// REPLACE "OnActivate_CopiesResultToClipboard" with:
[Fact]
public void CopyAction_CopiesResultToClipboard() {
    var (search, clipboard) = CreateSearch();
    var results = search.Search("2+3", 10);
    var item = results.OfType<CalculatorResultItemViewModel>().Single();

    var copy = item.Actions.Single(a => a.Hotkey == ActionHotkey.Enter);
    copy.Execute();

    Assert.Equal("5", clipboard.LastCopied);
}

// REPLACE "CalculatorResult_HasOnCopyAndCopiedMessage" with:
[Fact]
public void CalculatorResult_HasCopyActions() {
    var (search, _) = CreateSearch();
    var results = search.Search("2+3", 10);
    var result = results.OfType<CalculatorResultItemViewModel>().Single();

    // Enter action: copies and pastes
    var enterAction = result.Actions.Single(a => a.Hotkey == ActionHotkey.Enter);
    Assert.True(enterAction.PasteAfterClose);
    Assert.True(enterAction.ClosesWindow);

    // ⌘C action: copies without closing
    var copyAction = result.Actions.Single(a => a.Hotkey == ActionHotkey.MetaC);
    Assert.False(copyAction.ClosesWindow);
    Assert.Equal("Result copied!", copyAction.HintProvider?.Invoke());
}
```

- [ ] **Step 2: Update `UnitConverterSearchTests.cs`** — find all references to `OnActivate`, `OnCopy`, `PasteAfterActivate`, `CopiedMessage` and update to `Actions`:

```csharp
// Pattern for conversion tests:
var item = results.OfType<ConversionResultItemViewModel>().Single();
var enterAction = item.Actions.Single(a => a.Hotkey == ActionHotkey.Enter);
enterAction.Execute();
// assert clipboard content

var copyAction = item.Actions.Single(a => a.Hotkey == ActionHotkey.MetaC);
// assert copyAction properties
Assert.True(item.Actions.Any(a => a.PasteAfterClose)); // Enter action has PasteAfterClose
```

- [ ] **Step 3: Update `CalculatorSearch.cs`** — replace action assignments

For `ConversionResultItemViewModel` (around line 72), replace:
```csharp
// REMOVE:
PasteAfterActivate = true,
OnActivate = () => { var copied = ...; clipboard.CopyText(copied); },
OnCopy = () => { var copied = ...; clipboard.CopyText(copied); },
CopiedMessage = "Result copied!",

// ADD (note: both actions share the same copy logic):
Actions = [
    new() {
        Label        = "Copy value",
        Hotkey       = ActionHotkey.Enter,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        ClosesWindow  = true,
        PasteAfterClose = true,
        Execute = () => {
            var copied = vm.SelectedCell switch {
                ConversionCell.NormFrom => capturedNorm ?? capturedTo,
                _                       => capturedTo,
            };
            logger.LogInformation("Calculator: copied conversion result \"{Value}\"", copied);
            clipboard.CopyText(copied);
        },
    },
    new() {
        Label        = "Copy value",
        Hotkey       = ActionHotkey.MetaC,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        HintProvider = () => "Result copied!",
        Execute = () => {
            var copied = vm.SelectedCell switch {
                ConversionCell.NormFrom => capturedNorm ?? capturedTo,
                _                       => capturedTo,
            };
            logger.LogInformation("Calculator: copied conversion via Cmd+C \"{Value}\"", copied);
            clipboard.CopyText(copied);
        },
    },
],
```

For `CalculatorResultItemViewModel` (around line 116), replace:
```csharp
// REMOVE:
OnActivate = () => clipboard.CopyText(captured),
OnCopy = () => clipboard.CopyText(captured),
CopiedMessage = "Result copied!",
PasteAfterActivate = true,

// ADD:
Actions = [
    new() {
        Label        = "Copy result",
        Hotkey       = ActionHotkey.Enter,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        ClosesWindow  = true,
        PasteAfterClose = true,
        Execute = () => {
            logger.LogInformation("Calculator: copied result \"{Value}\"", captured);
            clipboard.CopyText(captured);
        },
    },
    new() {
        Label        = "Copy result",
        Hotkey       = ActionHotkey.MetaC,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        HintProvider = () => "Result copied!",
        Execute = () => {
            logger.LogInformation("Calculator: copied result via Cmd+C \"{Value}\"", captured);
            clipboard.CopyText(captured);
        },
    },
],
```

- [ ] **Step 4: Commit**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
git add Yottacast.Core/Search/Calculator/CalculatorSearch.cs \
        "Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs" \
        "Yottacast.Core.Tests/Search/Calculator/UnitConverterSearchTests.cs"
git commit -m "feat: migrate CalculatorSearch to Actions"
```

---

### Task 6: Migrate remaining sources + update tests

**Files:** WebSearchSource, DictionarySource, LocalPathSearch, SystemSettingsSearch, UrlSearch, UserDocumentSearch, ClipboardSearch + their tests.

- [ ] **Step 1: Update tests for remaining sources**

For each test file, find references to `OnActivate`, `OnCopy`, `CopiedMessage`, `PasteAfterActivate` and replace with `Actions` lookups. Pattern:

```csharp
// OLD: Assert.NotNull(item.OnActivate);  item.OnActivate();
// NEW: Assert.NotNull(item.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter));
//      item.Actions.First(a => a.Hotkey == ActionHotkey.Enter).Execute();

// OLD: Assert.NotNull(item.OnCopy);  Assert.Equal("Path copied!", item.CopiedMessage);
// NEW: var copy = item.Actions.Single(a => a.Hotkey == ActionHotkey.MetaC);
//      Assert.Equal("Path copied!", copy.HintProvider?.Invoke());
```

- [ ] **Step 2: Migrate `WebSearchSource.cs`**

Find the `ResultItemViewModel` creation (around line 109) and replace:
```csharp
// REMOVE:
OnActivate = () => {
    var browser = settings.ActiveBrowser;
    if (browser is null) return;
    var url = string.Format(capturedQueryUrl, Uri.EscapeDataString(capturedQuery));
    logger.LogInformation(...);
    browserDiscovery.OpenUrl(url, browser);
},

// ADD:
Actions = [
    new() {
        Label        = "Open in browser",
        Hotkey       = ActionHotkey.Enter,
        ShowInFooter = true,
        ClosesMenu   = true,
        ClosesWindow  = true,
        Execute = () => {
            var browser = settings.ActiveBrowser;
            if (browser is null) return;
            var url = string.Format(capturedQueryUrl, Uri.EscapeDataString(capturedQuery));
            logger.LogInformation("WebSearch: open engine={Engine} query=\"{Query}\" browser={Browser}",
                name, capturedQuery, browser.Name);
            browserDiscovery.OpenUrl(url, browser);
        },
    },
],
```

- [ ] **Step 3: Migrate `DictionarySource.cs`**

There are two places where `DictionaryResultViewModel` items are created (around lines 105–118 and 174–180). For each, replace:
```csharp
// REMOVE:
OnActivate = () => {
    var browser = settings.ActiveBrowser;
    if (browser is not null) browserDiscovery.OpenUrl(capturedUrl, browser);
},
OnCopy = () => clipboard.CopyText(capturedDef),
CopiedMessage = "Definition copied!",

// ADD:
Actions = [
    new() {
        Label        = "Open in Wiktionary",
        Hotkey       = ActionHotkey.Enter,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        ClosesWindow  = true,
        Execute = () => {
            var browser = settings.ActiveBrowser;
            if (browser is not null) browserDiscovery.OpenUrl(capturedUrl, browser);
        },
    },
    new() {
        Label        = "Copy definition",
        Hotkey       = ActionHotkey.MetaC,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        HintProvider = () => "Definition copied!",
        Execute      = () => clipboard.CopyText(capturedDef),
    },
],
```

- [ ] **Step 4: Migrate `LocalPathSearch.cs`**

Read the file first. The existing `OnActivate` body opens the path with the platform launcher. Keep it verbatim. Replace:
```csharp
// REMOVE:
OnActivate     = () => { ... existing body ... },
OnCopy         = () => clipboard.CopyText(capturedPath),
CopiedMessage  = "Path copied!",

// ADD:
Actions = [
    new() {
        Label        = "Open",
        Hotkey       = ActionHotkey.Enter,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        ClosesWindow  = true,
        Execute      = () => { /* paste the verbatim OnActivate body here */ },
    },
    new() {
        Label        = "Copy path",
        Hotkey       = ActionHotkey.MetaC,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        HintProvider = () => "Path copied!",
        Execute      = () => clipboard.CopyText(capturedPath),
    },
],
```

- [ ] **Step 5: Migrate `SystemSettingsSearch.cs`**

Read the file. The `OnActivate` body opens a macOS Settings URL via `platform.OpenUrl(...)`. Keep it verbatim.
```csharp
// REMOVE:
OnActivate = () => { ... existing body ... },

// ADD:
Actions = [
    new() {
        Label        = "Open",
        Hotkey       = ActionHotkey.Enter,
        ShowInFooter = true,
        ClosesMenu   = true,
        ClosesWindow  = true,
        Execute      = () => { /* paste the verbatim OnActivate body here */ },
    },
],
```

- [ ] **Step 6: Migrate `UrlSearch.cs`**

Read the file. The `OnActivate` body opens the URL via `browserDiscovery.OpenUrl(...)`. Keep it verbatim.
```csharp
// REMOVE:
OnActivate = () => { ... existing body ... },

// ADD:
Actions = [
    new() {
        Label        = "Open",
        Hotkey       = ActionHotkey.Enter,
        ShowInFooter = true,
        ClosesMenu   = true,
        ClosesWindow  = true,
        Execute      = () => { /* paste the verbatim OnActivate body here */ },
    },
],
```

- [ ] **Step 7: Migrate `UserDocumentSearch.cs`**

Read the file. The `OnActivate` body opens the file with `platform.OpenFile(path)`. Keep it verbatim.
```csharp
// REMOVE:
OnActivate = () => { ... existing body ... },
OnCopy = () => clipboard.CopyText(path),
CopiedMessage = "Path copied!",

// ADD:
Actions = [
    new() {
        Label        = "Open",
        Hotkey       = ActionHotkey.Enter,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        ClosesWindow  = true,
        Execute      = () => { /* paste the verbatim OnActivate body here */ },
    },
    new() {
        Label        = "Copy path",
        Hotkey       = ActionHotkey.MetaC,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        HintProvider = () => "Path copied!",
        Execute      = () => clipboard.CopyText(path),
    },
],
```

- [ ] **Step 8: Migrate `ClipboardSearch.cs`**

Read the file. `BuildUrlResult` has `OnActivate` that opens the URL via `browserDiscovery.OpenUrl(...)`. `BuildLocalPathResult` has `OnActivate` that opens the path + `OnCopy` that copies it.

```csharp
// BuildUrlResult — keep the verbatim OnActivate body in Execute:
Actions = [
    new() {
        Label        = "Open",
        Hotkey       = ActionHotkey.Enter,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        ClosesWindow  = true,
        Execute      = () => { /* paste the verbatim OnActivate body here */ },
    },
],

// BuildLocalPathResult — keep the verbatim OnActivate body in Execute:
Actions = [
    new() {
        Label        = "Open",
        Hotkey       = ActionHotkey.Enter,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        ClosesWindow  = true,
        Execute      = () => { /* paste the verbatim OnActivate body here */ },
    },
    new() {
        Label        = "Copy path",
        Hotkey       = ActionHotkey.MetaC,
        ShowInFooter = true,
        ShowInMenu   = true,
        ClosesMenu   = true,
        HintProvider = () => "Path copied!",
        Execute      = () => clipboardService.CopyText(capturedPath),
    },
],
```

- [ ] **Step 9: Commit**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
git add \
  Yottacast.Core/Search/WebSearch/WebSearchSource.cs \
  Yottacast.Core/Search/Dictionary/DictionarySource.cs \
  Yottacast.Core/Search/LocalPath/LocalPathSearch.cs \
  Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs \
  Yottacast.Core/Search/Url/UrlSearch.cs \
  Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs \
  Yottacast.Core/Search/Clipboard/ClipboardSearch.cs \
  "Yottacast.Core.Tests/Search/ClipboardSearchTests.cs" \
  "Yottacast.Core.Tests/Search/LocalPathSearchTests.cs" \
  "Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs" \
  "Yottacast.Core.Tests/Search/UrlSearchTests.cs" \
  "Yottacast.Core.Tests/Search/UserDocumentSearchTests.cs"
git commit -m "feat: migrate remaining search sources to Actions"
```

---

### Task 7: Migrate IPC project

**Files:**
- Modify: `Yottacast.Ipc/Mapping/ResultMapper.cs`
- Modify: `Yottacast.Ipc/Services/SearchGrpcService.cs`
- Modify: `Yottacast.Ipc.Tests/Mapping/ResultMapperTests.cs`

- [ ] **Step 1: Update `ResultMapper.cs`** — replace `PasteAfterActivate = vm.PasteAfterActivate` with:

```csharp
// Find the Enter action's PasteAfterClose flag
PasteAfterActivate = vm.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter)?.PasteAfterClose ?? false,
```

Also remove: `using Yottacast.Core.ViewModels;` is already there; just update the property.

- [ ] **Step 2: Update `SearchGrpcService.cs`** — replace the `switch (request.Action)` block:

```csharp
// OLD:
switch (request.Action) {
    case ActionType.Default:
        vm.OnActivate?.Invoke();
        break;
    case ActionType.Copy:
        vm.OnCopy?.Invoke();
        break;
    case ActionType.Favorite:
        vm.OnToggleFavorite?.Invoke();
        break;
}

return Task.FromResult(new ActivateResponse {
    PasteAfterActivate = vm.PasteAfterActivate,
    ClipboardText = _lastCopiedText ?? "",
});

// NEW:
var action = request.Action switch {
    ActionType.Default  => vm.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter),
    ActionType.Copy     => vm.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.MetaC),
    ActionType.Favorite => vm.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.MetaShiftF),
    _ => null
};
action?.Execute();

var pasteAfter = request.Action == ActionType.Default
    && (vm.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter)?.PasteAfterClose ?? false);

return Task.FromResult(new ActivateResponse {
    PasteAfterActivate = pasteAfter,
    ClipboardText = _lastCopiedText ?? "",
});
```

- [ ] **Step 3: Update `ResultMapperTests.cs`** — remove `PasteAfterActivate = false` from the test VM:

```csharp
var vm = new ResultItemViewModel {
    Score    = 0.9,
    Title    = "Safari",
    Subtitle = "/Applications/Safari.app",
    Category = "Applications",
    Icon     = "/Applications/Safari.app",
    BypassLimit = false,
    // PasteAfterActivate removed — now on individual Actions
};
```

- [ ] **Step 4: Verify IPC tests compile and pass**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Ipc.Tests"
dotnet test 2>&1 | tail -20
```

Expected: PASS (or only remaining compile errors from UI side).

- [ ] **Step 5: Commit**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
git add \
  Yottacast.Ipc/Mapping/ResultMapper.cs \
  Yottacast.Ipc/Services/SearchGrpcService.cs \
  Yottacast.Ipc.Tests/Mapping/ResultMapperTests.cs
git commit -m "feat: update IPC to use Actions instead of discrete callbacks"
```

---

### Task 8: Add `MatchesHotkey` and `FormatHotkey` to `AppHandler`

**Files:**
- Modify: `Yottacast/Services/AppHandler.cs`

- [ ] **Step 1: Add methods to `AppHandler.cs`**

Find the `CopyShortcut` property and below it add:

```csharp
// ── Action hotkey helpers ─────────────────────────────────────────────────

/// <summary>
/// Returns true if the Avalonia KeyEventArgs match the given ActionHotkey,
/// resolving Meta to the platform's command modifier (Cmd/Ctrl).
/// </summary>
public bool MatchesHotkey(KeyEventArgs e, ActionHotkey hotkey) {
    if (!Enum.TryParse<Key>(hotkey.Key, ignoreCase: true, out var key)) return false;
    var mods = hotkey.Modifiers switch {
        ActionModifiers.Meta      => MetaKeyModifier,
        ActionModifiers.Shift     => KeyModifiers.Shift,
        ActionModifiers.MetaShift => MetaKeyModifier | KeyModifiers.Shift,
        _                         => KeyModifiers.None,
    };
    return e.Key == key && e.KeyModifiers == mods;
}

/// <summary>
/// Returns a display string for the hotkey (e.g. "⌘C", "↵", "⌘⇧F").
/// Uses platform-specific MetaSymbol / ShiftSymbol.
/// </summary>
public string FormatHotkey(ActionHotkey hotkey) {
    var modStr = hotkey.Modifiers switch {
        ActionModifiers.Meta      => MetaSymbol,
        ActionModifiers.Shift     => ShiftSymbol,
        ActionModifiers.MetaShift => $"{MetaSymbol}{ShiftSymbol}",
        _                         => ""
    };
    var keyStr = hotkey.Key switch {
        "Return" => "↵",
        "Tab"    => "⇥",
        _        => hotkey.Key
    };
    return $"{modStr}{keyStr}";
}

/// <summary>The platform's Meta key modifier (Meta on macOS, Control elsewhere).</summary>
protected KeyModifiers MetaKeyModifier => CopyShortcut.Modifiers;
```

Make sure `using Avalonia.Input;` and `using Yottacast.Core.ViewModels;` are at the top of the file.

- [ ] **Step 2: Commit**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
git add Yottacast/Services/AppHandler.cs
git commit -m "feat: add MatchesHotkey and FormatHotkey to AppHandler"
```

---

### Task 9: Update `MainWindowViewModel`

**Files:**
- Modify: `Yottacast/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add overlay state properties and helper type**

At the top of the file (near the other `[ObservableProperty]` fields), add:

```csharp
[ObservableProperty] private bool _isOptionsMenuOpen;
[ObservableProperty] private int _optionsMenuSelectedIndex;
```

Add a small display DTO after the class body (or as a nested record, either works):
```csharp
// At the bottom of MainWindowViewModel.cs, outside the class:
public sealed record OptionsMenuItemVm(string Label, string? FormattedHotkey);
```

Add computed properties to the class:
```csharp
public IReadOnlyList<ResultAction> OptionsMenuActions =>
    SelectedResult?.Actions.Where(a => a.ShowInMenu).ToList()
    ?? (IReadOnlyList<ResultAction>)[];

public IReadOnlyList<OptionsMenuItemVm> OptionsMenuItems =>
    OptionsMenuActions.Select(a => new OptionsMenuItemVm(
        Label: a.Label,
        FormattedHotkey: a.Hotkey != null ? AppHandler.Instance.FormatHotkey(a.Hotkey) : null
    )).ToList();

public bool HasOptionsMenu => OptionsMenuActions.Count > 0;
```

- [ ] **Step 2: Add overlay management methods**

```csharp
public void OpenOptionsMenu() {
    if (!HasOptionsMenu) return;
    IsOptionsMenuOpen = true;
    OptionsMenuSelectedIndex = 0;
}

public void CloseOptionsMenu() {
    IsOptionsMenuOpen = false;
    OptionsMenuSelectedIndex = 0;
}

public void NavigateOptionsMenu(int delta) {
    var count = OptionsMenuActions.Count;
    if (count == 0) return;
    OptionsMenuSelectedIndex = ((OptionsMenuSelectedIndex + delta) % count + count) % count;
}

public ResultAction? SelectedMenuAction =>
    IsOptionsMenuOpen && OptionsMenuSelectedIndex < OptionsMenuActions.Count
        ? OptionsMenuActions[OptionsMenuSelectedIndex]
        : null;
```

- [ ] **Step 3: Replace `FooterHints`**

Replace the entire `FooterHints` property (lines 59–72):

```csharp
public IReadOnlyList<string> FooterHints {
    get {
        var actions = SelectedResult?.Actions;
        if (actions is null or { Count: 0 }) return ["Esc  clear"];

        var hints = new List<string>();
        foreach (var a in actions.Where(a => a.ShowInFooter && a.Hotkey != null))
            hints.Add($"{AppHandler.Instance.FormatHotkey(a.Hotkey!)}  {a.Label}");

        if (actions.Any(a => a.ShowInMenu))
            hints.Add("Tab  Options");

        hints.Add("Esc  clear");
        return hints;
    }
}
```

- [ ] **Step 4: Update `OnSelectedResultChanged`**

```csharp
partial void OnSelectedResultChanged(BaseResultItemViewModel? value) {
    OnPropertyChanged(nameof(IsEmojiMode));
    OnPropertyChanged(nameof(FooterHints));
    OnPropertyChanged(nameof(OptionsMenuActions));
    OnPropertyChanged(nameof(OptionsMenuItems));
    OnPropertyChanged(nameof(HasOptionsMenu));
    CloseOptionsMenu();
}
```

- [ ] **Step 5: Build to check for remaining compile errors**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
dotnet build Yottacast/Yottacast.csproj 2>&1 | grep -E "error|warning" | head -30
```

At this point only `MainWindow.axaml.cs` should have errors (still references old callbacks).

- [ ] **Step 6: Commit**

```bash
git add Yottacast/ViewModels/MainWindowViewModel.cs
git commit -m "feat: update MainWindowViewModel with FooterHints from Actions and overlay state"
```

---

### Task 10: Update `MainWindow.axaml.cs` — key handler

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml.cs`

- [ ] **Step 1: Add `ExecuteAction` helper method**

Add this private method to the `MainWindow` class:

```csharp
private void ExecuteAction(MainWindowViewModel vm, ResultAction action) {
    var result = vm.SelectedResult;
    action.Execute();

    if (action.ClosesWindow || action.ClosesMenu)
        vm.CloseOptionsMenu();

    if (action.ClosesWindow) {
        if (result != null) {
            vm.RecordLaunch(result);
            vm.CleanAndSaveHistory(result.Title);
        }
        Hide();
        AppHandler.Instance.OnHide();
        if (action.PasteAfterClose)
            _ = AppHandler.Instance.SimulatePasteAsync();
    } else {
        var hint = action.HintProvider?.Invoke();
        if (hint != null) vm.ShowCopiedMessage(hint);
    }
}
```

- [ ] **Step 2: Rewrite `OnTunnelKeyDown`**

Replace the entire `OnTunnelKeyDown` method:

```csharp
private void OnTunnelKeyDown(object? sender, KeyEventArgs e) {
    if (e.Key is not (Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)) {
        HideCursor();
    }

    var vm = DataContext as MainWindowViewModel;
    if (vm is null) return;

    // ── Overlay navigation (when options menu is open) ──────────────────────
    if (vm.IsOptionsMenuOpen) {
        switch (e.Key) {
            case Key.Up:
                vm.NavigateOptionsMenu(-1);
                e.Handled = true;
                return;
            case Key.Down:
                vm.NavigateOptionsMenu(+1);
                e.Handled = true;
                return;
            case Key.Return:
                if (vm.SelectedMenuAction is { } menuAction) {
                    // Pre-capture emoji cursor state for RequiresRefresh actions
                    EmojiGridResultViewModel? emojiGrid = null;
                    int previousIndex = 0;
                    string? selectedChar = null;
                    if (menuAction.RequiresRefresh && vm.SelectedResult is EmojiGridResultViewModel eg) {
                        emojiGrid = eg;
                        previousIndex = eg.SelectedEmojiIndex;
                        var prevSection = previousIndex < eg.Cells.Count
                            ? eg.Cells[previousIndex].Section : EmojiSection.Default;
                        selectedChar = prevSection == EmojiSection.Default ? eg.SelectedEmoji?.Char : null;
                    }
                    ExecuteAction(vm, menuAction);
                    if (menuAction.RequiresRefresh) {
                        vm.RefreshSearch();
                        RepositionEmojiCursor(vm, emojiGrid, previousIndex, selectedChar);
                    }
                }
                e.Handled = true;
                return;
            case Key.Escape:
            case Key.Tab:
                vm.CloseOptionsMenu();
                e.Handled = true;
                return;
        }
        // While overlay is open, block arrow keys from navigating the results list
        if (e.Key is Key.Left or Key.Right) { e.Handled = true; return; }
    }

    // ── Tab opens overlay ───────────────────────────────────────────────────
    if (e.Key == Key.Tab) {
        if (vm.HasOptionsMenu) vm.OpenOptionsMenu();
        e.Handled = true;
        return;
    }

    // ── Grid navigation (OnLeft/OnRight/OnUp/OnDown) ────────────────────────
    switch (e.Key) {
        case Key.Left when vm.SelectedResult?.OnLeft is { } onLeft:
            onLeft();
            break;
        case Key.Right when vm.SelectedResult?.OnRight is { } onRight:
            onRight();
            break;
        case Key.Up when vm.SelectedResult?.OnUp is { } onUp:
            e.Handled = onUp();
            break;
        case Key.Down when vm.SelectedResult?.OnDown is { } onDown:
            e.Handled = onDown();
            break;
        case Key.Prior:
            SelectDelta(vm, -AppDefaults.SearchSourceLimit);
            e.Handled = true;
            break;
        case Key.Next:
            SelectDelta(vm, +AppDefaults.SearchSourceLimit);
            e.Handled = true;
            break;
    }

    // ── Generic action hotkeys (excluding Enter, handled in OnKeyDown) ───────
    foreach (var action in vm.SelectedResult?.Actions ?? []) {
        if (action.Hotkey == null || action.Hotkey == ActionHotkey.Enter) continue;
        if (!AppHandler.Instance.MatchesHotkey(e, action.Hotkey)) continue;

        // Block ⌘C when text is selected in the search box (let the TextBox handle it)
        if (action.Hotkey.Modifiers == ActionModifiers.Meta
            && Math.Abs(SearchBox.SelectionEnd - SearchBox.SelectionStart) > 0)
            continue;

        // Pre-capture emoji cursor state for RequiresRefresh actions
        EmojiGridResultViewModel? emojiGrid = null;
        int previousIndex = 0;
        string? selectedChar = null;
        if (action.RequiresRefresh && vm.SelectedResult is EmojiGridResultViewModel eg) {
            emojiGrid = eg;
            previousIndex = eg.SelectedEmojiIndex;
            var prevSection = previousIndex < eg.Cells.Count
                ? eg.Cells[previousIndex].Section : EmojiSection.Default;
            selectedChar = prevSection == EmojiSection.Default ? eg.SelectedEmoji?.Char : null;
        }

        ExecuteAction(vm, action);

        if (action.RequiresRefresh) {
            vm.RefreshSearch();
            RepositionEmojiCursor(vm, emojiGrid, previousIndex, selectedChar);
        }

        e.Handled = true;
        return;
    }
}

private static void RepositionEmojiCursor(
    MainWindowViewModel vm,
    EmojiGridResultViewModel? previousGrid,
    int previousIndex,
    string? selectedChar)
{
    if (previousGrid == null) return;
    if (vm.SelectedResult is not EmojiGridResultViewModel newGrid) return;

    if (selectedChar != null) {
        var idx = newGrid.Cells.ToList()
            .FindIndex(c => c.Char == selectedChar && c.Section == EmojiSection.Default);
        newGrid.SelectedEmojiIndex = idx >= 0 ? idx : Math.Min(previousIndex, newGrid.Cells.Count - 1);
    } else {
        newGrid.SelectedEmojiIndex = Math.Min(previousIndex, newGrid.Cells.Count - 1);
    }
}
```

- [ ] **Step 3: Update `OnKeyDown` — replace `Key.Return` case**

Find the `case Key.Return:` block (around line 393) and replace:

```csharp
case Key.Return:
    if (!vm.IsOptionsMenuOpen) {
        var enterAction = vm.SelectedResult?.Actions
            .FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter);
        if (enterAction != null)
            ExecuteAction(vm, enterAction);
    }
    e.Handled = true;
    break;
```

- [ ] **Step 4: Update `OnKeyDown` — add overlay close to Escape**

Find the `case Key.Escape:` block and update:

```csharp
case Key.Escape:
    if (vm.IsOptionsMenuOpen) {
        vm.CloseOptionsMenu();
    } else if (vm.IsSearching) {
        vm.CancelDeferredSearch();
        vm.CleanAndSaveHistory(null);
    } else if (!string.IsNullOrEmpty(vm.SearchText)) {
        vm.CleanAndSaveHistory(null);
    } else {
        Hide();
    }
    e.Handled = true;
    break;
```

- [ ] **Step 5: Update `OnResultsTapped`** — replace `OnActivate` dispatch:

Find `OnResultsTapped` (around line 462) and replace the action dispatch:

```csharp
private void OnResultsTapped(object? sender, TappedEventArgs e) {
    if (DataContext is not MainWindowViewModel vm) return;
    var enterAction = vm.SelectedResult?.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter);
    if (enterAction != null)
        ExecuteAction(vm, enterAction);
}
```

- [ ] **Step 6: Build the solution**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
dotnet build 2>&1 | grep -E "^.*error" | head -20
```

Expected: solution compiles with no errors.

- [ ] **Step 7: Run all tests**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
cd ../Yottacast.Ipc.Tests && dotnet test 2>&1 | tail -10
```

Expected: all tests pass.

- [ ] **Step 8: Commit**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
git add Yottacast/Views/MainWindow.axaml.cs
git commit -m "feat: replace discrete key handlers with generic Actions loop in MainWindow"
```

---

### Task 11: Add overlay UI to `MainWindow.axaml`

> ⚠️ **Before implementing:** The overlay needs theme tokens. Check if the existing tokens suffice or if new ones are needed. The plan uses existing tokens; if the user wants a different visual treatment, create the tokens first per the CLAUDE.md rule.

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml`

- [ ] **Step 1: Wrap `ResultsList` in a `Panel`**

Find the `<!-- ── Results list ── -->` comment and the `<ListBox>` below it. Wrap them together:

```xaml
<!-- ── Results list + Options overlay ── -->
<Panel DockPanel.Dock="Top">

    <ListBox x:Name="ResultsList"
             ... (all existing attributes unchanged) ...
    />

    <!-- ── Options overlay (Tab) ── -->
    <Border IsVisible="{Binding IsOptionsMenuOpen}"
            Background="{DynamicResource Theme.Results.Background}"
            BorderBrush="{DynamicResource Theme.Footer.Border}"
            BorderThickness="1"
            CornerRadius="{DynamicResource Theme.Results.CornerRadius}"
            Margin="8"
            Padding="0,6,0,6"
            BoxShadow="0 4 16 0 #40000000">

        <DockPanel>
            <!-- Header -->
            <TextBlock DockPanel.Dock="Top"
                       Text="Actions"
                       Foreground="{DynamicResource Theme.Footer.Color}"
                       FontSize="{DynamicResource Theme.Footer.Size}"
                       Margin="14,4,14,8"/>

            <!-- Actions list -->
            <ListBox ItemsSource="{Binding OptionsMenuItems}"
                     SelectedIndex="{Binding OptionsMenuSelectedIndex, Mode=OneWay}"
                     Background="Transparent"
                     BorderThickness="0"
                     IsHitTestVisible="False"
                     Padding="6,0">
                <ListBox.ItemTemplate>
                    <DataTemplate x:DataType="vm:OptionsMenuItemVm">
                        <Grid ColumnDefinitions="*,Auto" Margin="0,0">
                            <TextBlock Grid.Column="0"
                                       Text="{Binding Label}"
                                       Foreground="{DynamicResource Theme.Results.Title.Color}"
                                       FontSize="{DynamicResource Theme.Results.Title.Size}"
                                       VerticalAlignment="Center"/>
                            <TextBlock Grid.Column="1"
                                       Text="{Binding FormattedHotkey}"
                                       Foreground="{DynamicResource Theme.Results.Category.Color}"
                                       FontSize="{DynamicResource Theme.Results.Category.Size}"
                                       VerticalAlignment="Center"
                                       Margin="16,0,0,0"
                                       IsVisible="{Binding FormattedHotkey, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"/>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </DockPanel>
    </Border>

</Panel>
```

Make sure to add the `vm` namespace alias to the Window element if not already present:
```xaml
xmlns:vm="using:Yottacast.ViewModels"
```

- [ ] **Step 2: Build**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
dotnet build Yottacast/Yottacast.csproj 2>&1 | grep -E "error" | head -20
```

Expected: no errors.

- [ ] **Step 3: Smoke test manually**

```bash
cd Yottacast && dotnet run
```

Verify:
1. Type "code" → footer shows `↵ Open`, `⌘C Copy path`, `Tab Options`, `Esc clear`
2. Press Tab → overlay appears with "Open (↵)" and "Copy path (⌘C)"
3. Press ↓ → second item highlighted
4. Press Enter → "Copy path" executes, overlay closes, hint "Path copied!" appears
5. Press ⌘C directly (without overlay) → hint "Path copied!" appears, window stays open
6. Press Enter directly → app launches, window closes
7. Press Tab, then Esc → overlay closes

- [ ] **Step 4: Commit**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
git add Yottacast/Views/MainWindow.axaml
git commit -m "feat: add options overlay to MainWindow"
```

---

### Task 12: Update documentation

**Files:**
- Modify: `docs/result-viewmodels.md`

- [ ] **Step 1: Update `docs/result-viewmodels.md`**

Replace section **2. BaseResultItemViewModel → Callbacks de acción** with:

```markdown
### Lista de acciones

| Propiedad | Tipo | Descripcion |
|---|---|---|
| `Actions` | `IReadOnlyList<ResultAction>` | Todas las acciones disponibles. El footer, el overlay (Tab) y los hotkeys se derivan de esta lista |

Cada `ResultAction` tiene:

| Campo | Tipo | Descripcion |
|---|---|---|
| `Label` | `string` | Texto mostrado en overlay y footer |
| `Hotkey` | `ActionHotkey?` | Atajo de teclado. Null = solo accesible via overlay |
| `ShowInFooter` | `bool` | Muestra hint en la barra inferior (solo si Hotkey != null) |
| `ShowInMenu` | `bool` | Incluye en el overlay de opciones (Tab) |
| `ClosesMenu` | `bool` | Cierra el overlay al ejecutar |
| `ClosesWindow` | `bool` | Oculta Yottacast al ejecutar |
| `PasteAfterClose` | `bool` | Simula Cmd+V tras cerrar (solo si ClosesWindow = true) |
| `RequiresRefresh` | `bool` | Llama a RefreshSearch() tras Execute(). Usado por EmojiSearch favorito |
| `HintProvider` | `Func<string?>?` | Mensaje en SearchHint tras ejecutar (p.ej. "Path copied!"). Solo visible si no cierra la ventana |
| `Execute` | `Action` | Callback de la acción |

`ActionHotkey` usa `ActionModifiers.Meta` como modificador agnóstico de plataforma (resuelve a Cmd en macOS y Ctrl en Windows).
```

Also remove references to the old `OnActivate`, `OnCopy`, `OnToggleFavorite`, `CopiedMessage`, `CopiedMessageProvider`, `PasteAfterActivate` callbacks.

Update **section 8 Invariantes**: remove the old callback invariant and add:
```
- `Actions` se establece en el constructor por la fuente y no cambia durante la vida del resultado.
```

- [ ] **Step 2: Run all tests one final time**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -5
cd ../Yottacast.Ipc.Tests && dotnet test 2>&1 | tail -5
```

Expected: all tests PASS.

- [ ] **Step 3: Commit**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
git add docs/result-viewmodels.md
git commit -m "docs: update result-viewmodels.md to reflect Actions system"
```
