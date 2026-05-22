# Drag-and-drop de resultados — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir arrastrar cualquier resultado de la lista principal hacia apps externas (archivos al Finder, texto a editores), manteniendo la ventana visible durante el drag.

**Architecture:** El ViewModel declara intención vía `Func<DragPayload?>? GetDragPayload` en `BaseResultItemViewModel`. La vista (`MainWindow.axaml.cs`) detecta inicio de drag con handlers Tunnel sobre `ResultsList` y un umbral de 5 px, traduce el `DragPayload` a un `IDataObject` con `DragDataFactory`, y llama a `DragDrop.DoDragDrop` con `DragDropEffects.Copy`. Sin acoplamiento de Core a Avalonia; sin código OS-específico (Avalonia traduce nativo en macOS).

**Tech Stack:** Avalonia 11.3.12, .NET 9, xUnit (tests).

**Spec:** `docs/superpowers/specs/2026-05-22-drag-and-drop-results-design.md`.

---

## File Structure

| Fichero | Responsabilidad |
|---|---|
| `Yottacast.Core/ViewModels/DragPayload.cs` (NEW) | Record sellado `File` / `Text` con la intención de drag |
| `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs` (MOD) | Añadir `GetDragPayload` opcional |
| `Yottacast.Core/AppDefaults.cs` (MOD) | Añadir `DragStartThresholdPx` |
| `Yottacast.Core/Search/Application/ApplicationSearch.cs` (MOD) | Rellenar `GetDragPayload = File(path)` |
| `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs` (MOD) | Rellenar `GetDragPayload = File(path)` |
| `Yottacast.Core/ViewModels/CalculatorResultItemViewModel.cs` (MOD) | Helper `BuildDragPayload` con el valor formateado |
| `Yottacast.Core/ViewModels/ConversionResultItemViewModel.cs` (MOD) | Helper `BuildDragPayload` que lee la celda seleccionada |
| `Yottacast.Core/ViewModels/AlgebraResultItemViewModel.cs` (MOD) | Helper `BuildDragPayload` con la celda seleccionada |
| `Yottacast.Core/ViewModels/DateSearchResultViewModel.cs` (MOD) | Helper `BuildDragPayload` con la celda seleccionada |
| `Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs` (MOD) | Helper `BuildDragPayload` con `SelectedEmoji.Char` |
| `Yottacast.Core/ViewModels/DictionaryResultViewModel.cs` (MOD) | Helper `BuildDragPayload` con `Word` |
| `Yottacast/Services/DragDataFactory.cs` (NEW) | Traduce `DragPayload` → `IDataObject` (depende de Avalonia) |
| `Yottacast/Views/MainWindow.axaml.cs` (MOD) | Handlers Pointer + invocación de `DragDrop.DoDragDrop` |
| `docs/ui-drag-drop.md` (NEW) | Contrato y comportamiento esperado |
| `CLAUDE.md` (MOD) | Apuntar a `docs/ui-drag-drop.md` en la lista de docs |

Tests: archivos correspondientes en `Yottacast.Core.Tests/ViewModels/` (carpeta nueva) y modificaciones a `Yottacast.Core.Tests/Search/ApplicationSearchTests.cs` y `UserDocumentSearchTests.cs`.

---

### Task 1: `DragPayload` record en Core

**Files:**
- Create: `Yottacast.Core/ViewModels/DragPayload.cs`

- [ ] **Step 1: Create the file**

```csharp
// Yottacast.Core/ViewModels/DragPayload.cs
namespace Yottacast.Core.ViewModels;

/// <summary>
/// Declarative description of what the user is dragging out of a result item.
/// The view translates this into the platform-native IDataObject. Core stays
/// independent of Avalonia.
/// </summary>
public abstract record DragPayload {
    /// <summary>A file on disk identified by its absolute path. Translates to DataFormats.Files.</summary>
    public sealed record File(string AbsolutePath) : DragPayload;

    /// <summary>Plain text payload. Translates to DataFormats.Text.</summary>
    public sealed record Text(string Value) : DragPayload;
}
```

- [ ] **Step 2: Build to verify**

Run: `cd Yottacast.Core && dotnet build`
Expected: succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Yottacast.Core/ViewModels/DragPayload.cs
git commit -m "feat(core): añadir DragPayload (File/Text) para drag-and-drop"
```

---

### Task 2: `GetDragPayload` en `BaseResultItemViewModel`

**Files:**
- Modify: `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs`

- [ ] **Step 1: Add the property at the end of the class (before the closing brace)**

Insert just before line 63 (`}`) of `BaseResultItemViewModel.cs`:

```csharp
    // ============================================================================
    // Drag-and-drop (set by each search source / VM when the item is draggable)
    // ============================================================================

    /// <summary>
    /// If non-null, the item is draggable. The view invokes this on drag start and
    /// translates the returned <see cref="DragPayload"/> into a platform IDataObject.
    /// Returning null cancels the drag silently. Read on the UI thread.
    /// </summary>
    public Func<DragPayload?>? GetDragPayload { get; init; }
```

- [ ] **Step 2: Build to verify the type resolves**

Run: `cd Yottacast.Core && dotnet build`
Expected: succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Yottacast.Core/ViewModels/BaseResultItemViewModel.cs
git commit -m "feat(core): contrato GetDragPayload en BaseResultItemViewModel"
```

---

### Task 3: `DragStartThresholdPx` en `AppDefaults`

**Files:**
- Modify: `Yottacast.Core/AppDefaults.cs`

- [ ] **Step 1: Add the constant in a new "Drag-and-drop" section**

Insert immediately after the "Window behavior" section (after line 186), before the closing brace of the class:

```csharp

    // ── Drag-and-drop ─────────────────────────────────────────────────────────
    /// Pixel distance the cursor must travel with the left button held before a drag is initiated.
    /// Below this threshold, click+release is treated as a normal click (selection).
    public const double DragStartThresholdPx = 5.0;
```

- [ ] **Step 2: Build to verify**

Run: `cd Yottacast.Core && dotnet build`
Expected: succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Yottacast.Core/AppDefaults.cs
git commit -m "feat(core): AppDefaults.DragStartThresholdPx"
```

---

### Task 4: ApplicationSearch genera `DragPayload.File`

**Files:**
- Modify: `Yottacast.Core/Search/Application/ApplicationSearch.cs:79-114`
- Test: `Yottacast.Core.Tests/Search/ApplicationSearchTests.cs:416-448`

- [ ] **Step 1: Add a failing test below `CreateResultItem_HasOpenAndCopyActions`**

Open `Yottacast.Core.Tests/Search/ApplicationSearchTests.cs` and append after the existing test (currently ends at line 448):

```csharp
    [Fact]
    public async Task CreateResultItem_GetDragPayload_ReturnsFileWithBundlePath() {
        var (search, _, _) = await CreateSearchAsync();

        var item = search.CreateResultItem(new AppInfo("Safari", "/Applications/Safari.app"));

        Assert.NotNull(item.GetDragPayload);
        var payload = item.GetDragPayload!();
        var file = Assert.IsType<DragPayload.File>(payload);
        Assert.Equal("/Applications/Safari.app", file.AbsolutePath);
    }
```

If the test file is missing the `Yottacast.Core.ViewModels` using, add it at the top.

- [ ] **Step 2: Run test to confirm it fails**

Run: `cd Yottacast.Core.Tests && dotnet test --filter "FullyQualifiedName~ApplicationSearchTests.CreateResultItem_GetDragPayload"`
Expected: FAIL — `item.GetDragPayload` is null.

- [ ] **Step 3: Wire `GetDragPayload` in `CreateResultItem`**

In `Yottacast.Core/Search/Application/ApplicationSearch.cs`, inside the object initializer of `CreateResultItem` (around line 83), add the property next to `ItemPath` (anywhere in the initializer is fine; place it after `Category`):

```csharp
            ItemPath = path,
            Category = "Application",
            Score = score,
            ScoreReason = scoreReason,
            TitleRanges = titleRanges,
            GetDragPayload = () => new DragPayload.File(path),
```

If `Yottacast.Core.ViewModels` is not yet imported, add `using Yottacast.Core.ViewModels;` at the top.

- [ ] **Step 4: Run the test, confirm it passes**

Run: `cd Yottacast.Core.Tests && dotnet test --filter "FullyQualifiedName~ApplicationSearchTests.CreateResultItem_GetDragPayload"`
Expected: PASS.

- [ ] **Step 5: Run the full ApplicationSearch test class to make sure nothing regressed**

Run: `cd Yottacast.Core.Tests && dotnet test --filter "FullyQualifiedName~ApplicationSearchTests"`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/Search/Application/ApplicationSearch.cs Yottacast.Core.Tests/Search/ApplicationSearchTests.cs
git commit -m "feat(apps): GetDragPayload con ruta del bundle"
```

---

### Task 5: UserDocumentSearch genera `DragPayload.File`

**Files:**
- Modify: `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs:155-189`
- Test: `Yottacast.Core.Tests/Search/UserDocumentSearchTests.cs`

- [ ] **Step 1: Inspect the test file to find the right insertion point**

Run: `grep -n "Search_\|public async Task\|class " Yottacast.Core.Tests/Search/UserDocumentSearchTests.cs | head -20`

Identify an existing test that drives a real search (one that produces a `ResultItemViewModel` you can assert against). If the file currently lacks one, add the test below alongside the existing tests.

- [ ] **Step 2: Add a failing test**

Append to `UserDocumentSearchTests.cs` (inside the same test class):

```csharp
    [Fact]
    public async Task FoundDocument_HasGetDragPayloadFile() {
        // Arrange a temporary directory with one file the platform stub will return.
        var tmp = Directory.CreateTempSubdirectory("yotta-doc-drag-").FullName;
        var file = Path.Combine(tmp, "Report.pdf");
        File.WriteAllText(file, "x");
        try {
            var platform = new TestPlatformReturning([new FileResult { Name = "Report.pdf", Path = file }]);
            var settings = UserSettings.Load(platform);
            // The exact constructor differs per existing tests — match the pattern used above.
            var search = CreateSearchUnderTest(platform, settings);

            var results = new List<BaseResultItemViewModel>();
            await foreach (var snapshot in search.SearchAsync("Report", AppDefaults.SearchSourceLimit, CancellationToken.None))
                results = snapshot.ToList();

            var item = Assert.Single(results) as ResultItemViewModel;
            Assert.NotNull(item);
            Assert.NotNull(item!.GetDragPayload);
            var payload = item.GetDragPayload!();
            var dragFile = Assert.IsType<DragPayload.File>(payload);
            Assert.Equal(file, dragFile.AbsolutePath);
        } finally {
            Directory.Delete(tmp, recursive: true);
        }
    }
```

If the existing tests don't already have a helper like `TestPlatformReturning` or `CreateSearchUnderTest`, **read the existing test file** and reuse the same helpers/fakes it uses. Don't introduce new fakes here — match the file's existing pattern. The important assertions are the four lines starting at `Assert.NotNull(item!.GetDragPayload);`.

- [ ] **Step 3: Run test to confirm it fails**

Run: `cd Yottacast.Core.Tests && dotnet test --filter "FullyQualifiedName~UserDocumentSearchTests.FoundDocument_HasGetDragPayloadFile"`
Expected: FAIL — `GetDragPayload` is null.

- [ ] **Step 4: Wire `GetDragPayload` in the source**

In `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs`, modify the object initializer at line 155 (`buffer.Add(new ResultItemViewModel { … });`). Add the new property near `ItemPath`:

```csharp
                        buffer.Add(new ResultItemViewModel {
                            IconBytes = fileIconCache.Get(r.Path),
                            BadgeIconBytes = _badgeByExtension.GetValueOrDefault(ext),
                            Title = r.Name,
                            Subtitle = r.Path,
                            ItemPath = r.Path,
                            Category = "Files",
                            Score = score * 3.5,
                            ScoreReason = scoreReason,
                            TitleRanges = titleRanges,
                            SubtitleRanges = subtitleRanges,
                            GetDragPayload = () => new DragPayload.File(path),
                            Actions = [
                                // … existing actions unchanged
```

(The local `var path = r.Path;` already exists on line 153.)

- [ ] **Step 5: Run the test, confirm it passes**

Run: `cd Yottacast.Core.Tests && dotnet test --filter "FullyQualifiedName~UserDocumentSearchTests"`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs Yottacast.Core.Tests/Search/UserDocumentSearchTests.cs
git commit -m "feat(docs): GetDragPayload con la ruta del fichero"
```

---

### Task 6: `CalculatorResultItemViewModel` genera `DragPayload.Text`

**Files:**
- Modify: `Yottacast.Core/ViewModels/CalculatorResultItemViewModel.cs`
- Modify: `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` (call site)
- Test: `Yottacast.Core.Tests/ViewModels/CalculatorResultItemViewModelTests.cs` (NEW)

- [ ] **Step 1: Locate the existing call site that constructs the ViewModel**

Run: `grep -n "new CalculatorResultItemViewModel" Yottacast.Core/Search/Calculator/CalculatorSearch.cs`

Read the surrounding 30 lines to identify the local variable that holds the formatted result text (likely passed to `Title` or copied via an action). Confirm the exact field name used for "Enter copies this".

- [ ] **Step 2: Create the contract test**

Create `Yottacast.Core.Tests/ViewModels/CalculatorResultItemViewModelTests.cs`:

```csharp
using Xunit;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.ViewModels;

public class CalculatorResultItemViewModelTests {
    [Fact]
    public void GetDragPayload_AssignedAtConstruction_ReturnsText() {
        var subject = new CalculatorResultItemViewModel {
            Title = "42",
            GetDragPayload = () => new DragPayload.Text("42"),
        };

        var payload = subject.GetDragPayload!();
        var text = Assert.IsType<DragPayload.Text>(payload);
        Assert.Equal("42", text.Value);
    }
}
```

This test only proves the property/delegate plumbing on the VM. The fact that `CalculatorSearch` actually wires it is verified manually in Task 14 (drag a calc result to TextEdit) — the calc result has no mutable state worth a deeper unit test.

- [ ] **Step 3: Run the test to confirm it passes (it should, since we set the delegate explicitly)**

Run: `cd Yottacast.Core.Tests && dotnet test --filter "FullyQualifiedName~CalculatorResultItemViewModelTests"`
Expected: PASS — but it doesn't yet verify that `CalculatorSearch` actually wires `GetDragPayload`.

- [ ] **Step 4: Add wiring in `CalculatorSearch`**

In `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`, locate the call site found in Step 1. The construction looks like:

```csharp
return new CalculatorResultItemViewModel {
    Title = displayValue,
    Subtitle = …,
    …
};
```

Add the new property:

```csharp
    Title = displayValue,
    GetDragPayload = () => new DragPayload.Text(displayValue),
```

Use the same string the calculator's "copy on Enter" action copies. If the copied value is on a different field (e.g. a hidden raw value), use that one — the principle is "drag what Enter copies".

- [ ] **Step 5: Build to verify**

Run: `cd Yottacast.Core && dotnet build`
Expected: succeeds with 0 errors.

- [ ] **Step 6: Run all tests**

Run: `cd Yottacast.Core.Tests && dotnet test`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add Yottacast.Core/Search/Calculator/CalculatorSearch.cs Yottacast.Core.Tests/ViewModels/CalculatorResultItemViewModelTests.cs
git commit -m "feat(calc): GetDragPayload con el valor copiable"
```

---

### Task 7: `ConversionResultItemViewModel` — payload = celda seleccionada

**Files:**
- Modify: `Yottacast.Core/ViewModels/ConversionResultItemViewModel.cs`
- Modify: `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` (call site for conversions)
- Test: `Yottacast.Core.Tests/ViewModels/ConversionResultItemViewModelTests.cs` (NEW)

The payload must reflect the **currently selected cell**, so the delegate has to read mutable state — it cannot be set once via init and forgotten with a captured constant. Solution: add a public method on the VM that returns the right text, and the call site assigns `GetDragPayload = vm.BuildDragPayload`.

- [ ] **Step 1: Add `BuildDragPayload` method on the VM**

Open `Yottacast.Core/ViewModels/ConversionResultItemViewModel.cs`. Append before the closing brace (after the `MoveCellRight` method):

```csharp
    /// <summary>
    /// Returns a text payload with the value of the currently selected cell.
    /// Used by the view's drag-and-drop layer.
    /// </summary>
    public DragPayload BuildDragPayload() => new DragPayload.Text(SelectedCellText);

    private string SelectedCellText => SelectedCell switch {
        ConversionCell.OrigFrom => FromShort,
        ConversionCell.NormFrom => NormFromShort ?? FromShort,
        _                       => ToShort,
    };
```

- [ ] **Step 2: Create the failing test**

Create `Yottacast.Core.Tests/ViewModels/ConversionResultItemViewModelTests.cs`:

```csharp
using Xunit;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.ViewModels;

public class ConversionResultItemViewModelTests {
    private static ConversionResultItemViewModel Build() => new() {
        FromShort     = "1 m",
        NormFromShort = "100 cm",
        ToShort       = "3.28 ft",
        FromWasNormalized = true,
    };

    [Fact]
    public void BuildDragPayload_DefaultsToToCell() {
        var vm = Build();
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("3.28 ft", text.Value);
    }

    [Fact]
    public void BuildDragPayload_FollowsSelectedCell_NormFrom() {
        var vm = Build();
        vm.MoveCellLeft();
        Assert.Equal(ConversionCell.NormFrom, vm.SelectedCell);
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("100 cm", text.Value);
    }
}
```

- [ ] **Step 3: Run the test to confirm it passes**

Run: `cd Yottacast.Core.Tests && dotnet test --filter "FullyQualifiedName~ConversionResultItemViewModelTests"`
Expected: both PASS.

- [ ] **Step 4: Wire `GetDragPayload` at the call site**

Run: `grep -n "new ConversionResultItemViewModel" Yottacast.Core/Search/Calculator/CalculatorSearch.cs`

Adapt the construction so the delegate calls into the instance:

```csharp
var vm = new ConversionResultItemViewModel {
    // … existing init properties
};
vm = vm with { GetDragPayload = vm.BuildDragPayload };
return vm;
```

`ConversionResultItemViewModel` is a class, not a record, so `with` does not apply. Use this pattern instead:

```csharp
var vm = new ConversionResultItemViewModel { /* existing init fields */ };
SetDragPayload(vm);
return vm;
```

…where `SetDragPayload` is a tiny local function or you assign directly via reflection-free trick. **Simpler:** make `GetDragPayload` non-init by adding a setter alongside `init`. Avoid that — keep the contract pure.

**Cleanest solution:** use an object initializer that captures the instance via a local variable through a constructor or factory. Since `GetDragPayload` is `init`, all init values must be set in the same initializer. Replace the init-only declaration in `BaseResultItemViewModel`:

Open `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs`. Change `init` to `set` for `GetDragPayload`:

```csharp
public Func<DragPayload?>? GetDragPayload { get; set; }
```

(Updates the property's accessor only; no other change.) Now the call site can do:

```csharp
var vm = new ConversionResultItemViewModel { /* … */ };
vm.GetDragPayload = vm.BuildDragPayload;
return vm;
```

- [ ] **Step 5: Update `BaseResultItemViewModel` and rebuild Task 2's commit retroactively (within this task's commit)**

Edit `BaseResultItemViewModel.cs` so the property becomes `{ get; set; }`. Run: `cd Yottacast.Core && dotnet build`. Expected: succeeds.

- [ ] **Step 6: Build the whole solution**

Run: `cd Yottacast.Core && dotnet build`
Expected: succeeds.

- [ ] **Step 7: Run all tests**

Run: `cd Yottacast.Core.Tests && dotnet test`
Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add Yottacast.Core/ViewModels/ConversionResultItemViewModel.cs \
        Yottacast.Core/ViewModels/BaseResultItemViewModel.cs \
        Yottacast.Core/Search/Calculator/CalculatorSearch.cs \
        Yottacast.Core.Tests/ViewModels/ConversionResultItemViewModelTests.cs
git commit -m "feat(conv): GetDragPayload sigue la celda seleccionada"
```

---

### Task 8: `AlgebraResultItemViewModel` — payload = celda seleccionada

**Files:**
- Modify: `Yottacast.Core/ViewModels/AlgebraResultItemViewModel.cs`
- Modify: call site that constructs `AlgebraResultItemViewModel`
- Test: `Yottacast.Core.Tests/ViewModels/AlgebraResultItemViewModelTests.cs` (NEW)

- [ ] **Step 1: Add `BuildDragPayload` to the VM**

Append before the closing brace of `AlgebraResultItemViewModel`:

```csharp
    /// <summary>Returns a text payload with the result of the currently selected cell.</summary>
    public DragPayload BuildDragPayload() {
        if (CellItems.Count == 0) return new DragPayload.Text("");
        var idx = Math.Clamp(SelectedCell, 0, CellItems.Count - 1);
        return new DragPayload.Text(CellItems[idx].Result);
    }
```

If `AlgebraCellItem` exposes the result text under a different name than `Result`, use that name. Verify by reading `Yottacast.Core/ViewModels/AlgebraCellItem.cs`.

- [ ] **Step 2: Create the failing test**

Create `Yottacast.Core.Tests/ViewModels/AlgebraResultItemViewModelTests.cs`:

```csharp
using Xunit;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.ViewModels;

public class AlgebraResultItemViewModelTests {
    private static AlgebraResultItemViewModel Build() => new() {
        Cells = [
            new AlgebraCell("simplify", "x + 1"),
            new AlgebraCell("factor",   "(x+1)"),
            new AlgebraCell("expand",   "x + 1"),
        ],
    };

    [Fact]
    public void BuildDragPayload_DefaultsToFirstCell() {
        var vm = Build();
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("x + 1", text.Value);
    }

    [Fact]
    public void BuildDragPayload_FollowsSelection() {
        var vm = Build();
        vm.MoveCellRight();
        Assert.Equal(1, vm.SelectedCell);
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("(x+1)", text.Value);
    }
}
```

If `AlgebraCell`'s positional ctor differs (it's defined in `Yottacast.Core/Search/Calculator/`), adjust the construction to match the real signature.

- [ ] **Step 3: Run test to confirm both pass**

Run: `cd Yottacast.Core.Tests && dotnet test --filter "FullyQualifiedName~AlgebraResultItemViewModelTests"`
Expected: PASS.

- [ ] **Step 4: Wire `GetDragPayload` at the call site**

Run: `grep -n "new AlgebraResultItemViewModel" Yottacast.Core/Search/Calculator/CalculatorSearch.cs`

Update the construction:

```csharp
var vm = new AlgebraResultItemViewModel { /* … */ };
vm.GetDragPayload = vm.BuildDragPayload;
return vm;
```

- [ ] **Step 5: Build and run all tests**

Run: `cd Yottacast.Core && dotnet build && cd ../Yottacast.Core.Tests && dotnet test`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/ViewModels/AlgebraResultItemViewModel.cs \
        Yottacast.Core/Search/Calculator/CalculatorSearch.cs \
        Yottacast.Core.Tests/ViewModels/AlgebraResultItemViewModelTests.cs
git commit -m "feat(algebra): GetDragPayload sigue la celda seleccionada"
```

---

### Task 9: `DateSearchResultViewModel` — payload = celda seleccionada

**Files:**
- Modify: `Yottacast.Core/ViewModels/DateSearchResultViewModel.cs`
- Modify: `Yottacast.Core/Search/Date/DateSearch.cs` (call site)
- Test: `Yottacast.Core.Tests/ViewModels/DateSearchResultViewModelTests.cs` (NEW)

- [ ] **Step 1: Add `BuildDragPayload` to the VM**

Append before the closing brace of `DateSearchResultViewModel`:

```csharp
    /// <summary>Returns a text payload with the value of the currently selected cell.</summary>
    public DragPayload BuildDragPayload() {
        if (Cells.Count == 0) return new DragPayload.Text("");
        var idx = Math.Clamp(SelectedCell, 0, Cells.Count - 1);
        return new DragPayload.Text(Cells[idx]);
    }
```

- [ ] **Step 2: Create the failing test**

Create `Yottacast.Core.Tests/ViewModels/DateSearchResultViewModelTests.cs`:

```csharp
using Xunit;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.ViewModels;

public class DateSearchResultViewModelTests {
    private static DateSearchResultViewModel Build() => new() {
        Cells = ["2026-05-22", "May 22, 2026", "Friday"],
    };

    [Fact]
    public void BuildDragPayload_DefaultsToFirstCell() {
        var vm = Build();
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("2026-05-22", text.Value);
    }

    [Fact]
    public void BuildDragPayload_FollowsSelection() {
        var vm = Build();
        vm.MoveCellRight();
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("May 22, 2026", text.Value);
    }
}
```

- [ ] **Step 3: Run test to confirm it passes**

Run: `cd Yottacast.Core.Tests && dotnet test --filter "FullyQualifiedName~DateSearchResultViewModelTests"`
Expected: PASS.

- [ ] **Step 4: Wire `GetDragPayload` at the call site**

Run: `grep -n "new DateSearchResultViewModel" Yottacast.Core/Search/Date/DateSearch.cs`

Update the construction:

```csharp
var vm = new DateSearchResultViewModel { /* … */ };
vm.GetDragPayload = vm.BuildDragPayload;
return vm;
```

- [ ] **Step 5: Build and run all tests**

Run: `cd Yottacast.Core && dotnet build && cd ../Yottacast.Core.Tests && dotnet test`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/ViewModels/DateSearchResultViewModel.cs \
        Yottacast.Core/Search/Date/DateSearch.cs \
        Yottacast.Core.Tests/ViewModels/DateSearchResultViewModelTests.cs
git commit -m "feat(date): GetDragPayload sigue la celda seleccionada"
```

---

### Task 10: `EmojiGridResultViewModel` — payload = emoji seleccionado

**Files:**
- Modify: `Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs`
- Modify: `Yottacast.Core/Search/Emoji/EmojiSearch.cs` (call site)
- Test: `Yottacast.Core.Tests/ViewModels/EmojiGridResultViewModelTests.cs` (NEW)

- [ ] **Step 1: Add `BuildDragPayload` to the VM**

Append before the closing brace of `EmojiGridResultViewModel`:

```csharp
    /// <summary>Returns a text payload with the currently selected emoji character.</summary>
    public DragPayload BuildDragPayload() => new DragPayload.Text(SelectedEmoji?.Char ?? "");
```

- [ ] **Step 2: Create the failing test**

Create `Yottacast.Core.Tests/ViewModels/EmojiGridResultViewModelTests.cs`:

```csharp
using Xunit;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.ViewModels;

public class EmojiGridResultViewModelTests {
    private static EmojiGridResultViewModel Build() => new() {
        Cells = [
            new EmojiCellViewModel { Char = "😀", Section = EmojiSection.Default, Category = "Smileys" },
            new EmojiCellViewModel { Char = "🐶", Section = EmojiSection.Default, Category = "Animals" },
        ],
    };

    [Fact]
    public void BuildDragPayload_DefaultsToFirstEmoji() {
        var vm = Build();
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("😀", text.Value);
    }

    [Fact]
    public void BuildDragPayload_FollowsSelection() {
        var vm = Build();
        vm.SelectNext();
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("🐶", text.Value);
    }
}
```

- [ ] **Step 3: Run test to confirm it passes**

Run: `cd Yottacast.Core.Tests && dotnet test --filter "FullyQualifiedName~EmojiGridResultViewModelTests"`
Expected: PASS.

- [ ] **Step 4: Wire `GetDragPayload` at the call site**

Run: `grep -n "new EmojiGridResultViewModel" Yottacast.Core/Search/Emoji/EmojiSearch.cs`

Update the construction:

```csharp
var vm = new EmojiGridResultViewModel { /* … */ };
vm.GetDragPayload = vm.BuildDragPayload;
return vm;
```

- [ ] **Step 5: Build and run all tests**

Run: `cd Yottacast.Core && dotnet build && cd ../Yottacast.Core.Tests && dotnet test`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs \
        Yottacast.Core/Search/Emoji/EmojiSearch.cs \
        Yottacast.Core.Tests/ViewModels/EmojiGridResultViewModelTests.cs
git commit -m "feat(emoji): GetDragPayload con el emoji seleccionado"
```

---

### Task 11: `DictionaryResultViewModel` — payload = la palabra

**Files:**
- Modify: `Yottacast.Core/Search/Dictionary/DictionarySource.cs` (call site)
- Test: `Yottacast.Core.Tests/ViewModels/DictionaryResultViewModelTests.cs` (NEW)

The dictionary VM is plain data (no selectable cells), so the payload is set at construction time without a method.

- [ ] **Step 1: Create the failing test**

Create `Yottacast.Core.Tests/ViewModels/DictionaryResultViewModelTests.cs`:

```csharp
using Xunit;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.ViewModels;

public class DictionaryResultViewModelTests {
    [Fact]
    public void GetDragPayload_ReturnsWordAsText() {
        var word = "ephemeral";
        var vm = new DictionaryResultViewModel {
            Word = word,
            GetDragPayload = () => new DragPayload.Text(word),
        };
        var text = Assert.IsType<DragPayload.Text>(vm.GetDragPayload!());
        Assert.Equal("ephemeral", text.Value);
    }
}
```

- [ ] **Step 2: Run test to confirm it passes**

Run: `cd Yottacast.Core.Tests && dotnet test --filter "FullyQualifiedName~DictionaryResultViewModelTests"`
Expected: PASS.

- [ ] **Step 3: Wire `GetDragPayload` at the call site**

Run: `grep -n "new DictionaryResultViewModel" Yottacast.Core/Search/Dictionary/DictionarySource.cs`

Update the construction. Inside the initializer, near `Word = word`:

```csharp
return new DictionaryResultViewModel {
    Word = word,
    Language = language,
    Definitions = definitions,
    GetDragPayload = () => new DragPayload.Text(word),
    /* … existing fields … */
};
```

- [ ] **Step 4: Build and run all tests**

Run: `cd Yottacast.Core && dotnet build && cd ../Yottacast.Core.Tests && dotnet test`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add Yottacast.Core/Search/Dictionary/DictionarySource.cs \
        Yottacast.Core.Tests/ViewModels/DictionaryResultViewModelTests.cs
git commit -m "feat(dict): GetDragPayload con la palabra"
```

---

### Task 12: `DragDataFactory` (vista, traduce payload → IDataObject)

**Files:**
- Create: `Yottacast/Services/DragDataFactory.cs`

- [ ] **Step 1: Create the file**

```csharp
// Yottacast/Services/DragDataFactory.cs
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Yottacast.Core.ViewModels;

namespace Yottacast.Services;

/// <summary>
/// Translates a Core <see cref="DragPayload"/> into the Avalonia <see cref="IDataObject"/> that
/// the OS expects. Lives in the UI project because it depends on Avalonia.
/// </summary>
public static class DragDataFactory {
    /// <summary>
    /// Builds an IDataObject for the given payload. Returns null if the payload cannot be
    /// resolved (e.g. file disappeared between search and drag) — caller treats null as "abort".
    /// </summary>
    public static async Task<IDataObject?> BuildAsync(Visual visual, DragPayload payload) {
        return payload switch {
            DragPayload.Text t => Text(t.Value),
            DragPayload.File f => await FileAsync(visual, f.AbsolutePath),
            _                  => null,
        };
    }

    private static IDataObject Text(string text) {
        var data = new DataObject();
        data.Set(DataFormats.Text, text);
        return data;
    }

    private static async Task<IDataObject?> FileAsync(Visual visual, string absolutePath) {
        var topLevel = TopLevel.GetTopLevel(visual);
        var storage = topLevel?.StorageProvider;
        if (storage is null) return null;
        IStorageItem? file;
        try {
            file = await storage.TryGetFileFromPathAsync(new Uri(absolutePath));
        } catch (Exception) {
            // Invalid URI, permission denied, etc. — treat as a non-startable drag.
            return null;
        }
        if (file is null) return null;
        var data = new DataObject();
        data.Set(DataFormats.Files, new[] { file });
        return data;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `cd Yottacast && dotnet build`
Expected: succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Yottacast/Services/DragDataFactory.cs
git commit -m "feat(ui): DragDataFactory traduce DragPayload a IDataObject"
```

---

### Task 13: Pointer handlers + `DragDrop.DoDragDrop` en `MainWindow`

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml.cs`

This task adds the actual drag detection. There are no unit tests for view code in this codebase; verification is manual (Task 14).

- [ ] **Step 1: Add the new field**

In `Yottacast/Views/MainWindow.axaml.cs`, add a private field next to the other state fields (after line 27, near `_positionDirty`):

```csharp
    private (Point Origin, BaseResultItemViewModel Vm)? _dragCandidate;
```

- [ ] **Step 2: Register the new handlers in the constructor**

In the constructor of `MainWindow`, just below the existing `ResultsList.AddHandler(...)` calls (around line 49), append:

```csharp
        ResultsList.AddHandler(PointerPressedEvent, OnResultsPointerPressedForDrag, RoutingStrategies.Tunnel);
        ResultsList.AddHandler(PointerMovedEvent, OnResultsPointerMovedForDrag, RoutingStrategies.Tunnel);
        ResultsList.AddHandler(PointerReleasedEvent, OnResultsPointerReleasedForDrag, RoutingStrategies.Tunnel);
        ResultsList.AddHandler(PointerCaptureLostEvent, OnResultsPointerCaptureLostForDrag, RoutingStrategies.Tunnel);
```

- [ ] **Step 3: Add the handler methods at the end of the class (just before the final `}`)**

Place these immediately after `ShowCursor()` (around line 629), before the class's closing brace:

```csharp
    private void OnResultsPointerPressedForDrag(object? sender, PointerPressedEventArgs e) {
        if (e.GetCurrentPoint(ResultsList).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;
        var item = FindListBoxItem(e.Source as Control);
        if (item?.DataContext is BaseResultItemViewModel vm && vm.GetDragPayload is not null) {
            _dragCandidate = (e.GetPosition(ResultsList), vm);
        } else {
            _dragCandidate = null;
        }
    }

    private async void OnResultsPointerMovedForDrag(object? sender, PointerEventArgs e) {
        if (_dragCandidate is not { } candidate) return;
        var props = e.GetCurrentPoint(ResultsList).Properties;
        if (!props.IsLeftButtonPressed) {
            _dragCandidate = null;
            return;
        }
        var current = e.GetPosition(ResultsList);
        var dx = current.X - candidate.Origin.X;
        var dy = current.Y - candidate.Origin.Y;
        if (Math.Abs(dx) < AppDefaults.DragStartThresholdPx && Math.Abs(dy) < AppDefaults.DragStartThresholdPx)
            return;

        // Consume the candidate before awaiting so we don't double-start a drag.
        _dragCandidate = null;

        try {
            var payload = candidate.Vm.GetDragPayload?.Invoke();
            if (payload is null) return;
            var data = await DragDataFactory.BuildAsync(this, payload);
            if (data is null) {
                _logger.LogDebug("Drag aborted: factory returned null payload for {Type}", payload.GetType().Name);
                return;
            }
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Drag-and-drop failed");
        }
    }

    private void OnResultsPointerReleasedForDrag(object? sender, PointerReleasedEventArgs e) {
        _dragCandidate = null;
    }

    private void OnResultsPointerCaptureLostForDrag(object? sender, PointerCaptureLostEventArgs e) {
        _dragCandidate = null;
    }
```

The existing `using` directives at the top of the file already include the needed namespaces (`Avalonia`, `Avalonia.Controls`, `Avalonia.Input`, `Avalonia.Interactivity`, `Yottacast.Core`, `Yottacast.Core.ViewModels`, `Yottacast.Services`). If `Yottacast.Services` is missing, add `using Yottacast.Services;`.

- [ ] **Step 4: Build the GUI project**

Run: `cd Yottacast && dotnet build`
Expected: succeeds with 0 errors.

- [ ] **Step 5: Run the full Core test suite once more**

Run: `cd Yottacast.Core.Tests && dotnet test`
Expected: all PASS (no Core code was changed in this task, but confirms nothing slipped).

- [ ] **Step 6: Commit**

```bash
git add Yottacast/Views/MainWindow.axaml.cs
git commit -m "feat(ui): drag-and-drop desde la lista de resultados"
```

---

### Task 14: Verificación manual + documentación

**Files:**
- Create: `docs/ui-drag-drop.md`
- Modify: `CLAUDE.md` (lista de docs disponibles)

- [ ] **Step 1: Run the app and verify the invariants from the spec**

Run: `cd Yottacast && dotnet run`

Manual checks (mark in the checklist below as you go):

1. **Archivo a Finder**: buscar un documento (p. ej. "Report"); arrastrarlo a una carpeta de Finder. Resultado esperado: copia del fichero en la carpeta.
2. **App al escritorio**: buscar "Safari"; arrastrar al escritorio. Esperado: alias o copia del bundle (comportamiento estándar de Finder al soltar `.app`s).
3. **Calculadora a TextEdit**: escribir `2+2`; arrastrar el resultado a TextEdit. Esperado: pega `4`.
4. **Conversion celda seleccionada**: escribir `100 cm to inches`; con flecha izquierda mover la celda seleccionada; arrastrar. Esperado: pega el texto de la celda activa.
5. **Emoji a Notes**: escribir `:smile`; arrastrar el emoji seleccionado. Esperado: pega el carácter.
6. **Click corto sigue seleccionando**: click + soltar inmediato sobre un item. Esperado: se selecciona, NO se inicia drag.
7. **Drag con item no arrastrable**: si hubiera algún resultado sin `GetDragPayload` (Random/Clipboard si no se cablearon en este plan), arrastrarlo debe NO iniciar drag.
8. **Ventana visible durante el drag**: la ventana de Yottacast permanece visible en todos los puntos anteriores.

- [ ] **Step 2: Create `docs/ui-drag-drop.md`**

Create the file:

```markdown
# Drag-and-drop de resultados

## Que debe hacer

Cualquier resultado de la lista principal puede arrastrarse fuera de Yottacast hacia otras aplicaciones:

- Apps y documentos se arrastran como ficheros (DataFormats.Files). Soltarlos en Finder copia el archivo.
- Resultados de calculadora, conversor, álgebra, fechas, emoji y diccionario se arrastran como texto plano (DataFormats.Text). Soltarlos en un editor pega el contenido.

## Comportamiento esperado

- El drag se inicia cuando el cursor se mueve más de `AppDefaults.DragStartThresholdPx` píxeles con el botón izquierdo presionado sobre un item arrastrable.
- Click corto (sin movimiento) selecciona el item normalmente; nunca inicia drag.
- La ventana de Yottacast permanece visible durante todo el drag — no se oculta al iniciar ni al soltar.
- En resultados con celdas navegables (conversion, álgebra, fechas, emoji) el drag usa el contenido de la celda **actualmente seleccionada**, no la celda bajo el cursor.
- Si el payload no puede resolverse (fichero borrado, URI inválida) el drag se cancela silenciosamente — no hay excepción visible al usuario.
- Sólo se admite `DragDropEffects.Copy`; mover archivos no es una operación soportada.

## Plataformas

v1 sólo se valida en macOS. El código debería funcionar en Windows/Linux con los formatos estándar de Avalonia, pero no se garantiza hasta que se pruebe.

## Contrato

Cada `BaseResultItemViewModel` declara su intención de drag con `GetDragPayload: Func<DragPayload?>?`. Si es null, el item no es arrastrable.

`DragPayload` (en `Yottacast.Core/ViewModels/DragPayload.cs`) tiene dos variantes:
- `DragPayload.File(string AbsolutePath)` — para apps y documentos.
- `DragPayload.Text(string Value)` — para todo lo demás.

La vista (`Yottacast/Views/MainWindow.axaml.cs`) traduce el payload a un `IDataObject` usando `Yottacast/Services/DragDataFactory.cs`.

## Verificar en

- Contrato: `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs`, `Yottacast.Core/ViewModels/DragPayload.cs`.
- Disparo del drag: `Yottacast/Views/MainWindow.axaml.cs` métodos `OnResultsPointerPressedForDrag` / `OnResultsPointerMovedForDrag`.
- Traducción payload→IDataObject: `Yottacast/Services/DragDataFactory.cs`.
- Tests por VM: `Yottacast.Core.Tests/ViewModels/`.
```

- [ ] **Step 3: Add the new doc to `CLAUDE.md`**

Open `CLAUDE.md`, locate the "Settings y UI" section in the docs index (around the line that mentions `docs/ui-themes.md`). Append a new bullet:

```markdown
- `docs/ui-drag-drop.md` — Drag-and-drop de resultados al sistema operativo (Finder, editores). Contrato `GetDragPayload` y disparo desde `MainWindow.axaml.cs`.
```

- [ ] **Step 4: Final full-suite test**

Run: `cd Yottacast.Core.Tests && dotnet test`
Expected: all PASS.

Run: `cd Yottacast && dotnet build`
Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add docs/ui-drag-drop.md CLAUDE.md
git commit -m "docs: drag-and-drop de resultados"
```

---

## Self-Review Notes

**Spec coverage check (each spec section → task):**

- Contract `DragPayload` + `GetDragPayload` → Task 1, Task 2.
- `DragStartThresholdPx` in `AppDefaults` → Task 3.
- Apps source → Task 4.
- UserDocuments source → Task 5.
- Calculator VM → Task 6.
- Conversion VM (selected cell) → Task 7.
- Algebra VM → Task 8.
- Date VM → Task 9.
- Emoji VM → Task 10.
- Dictionary VM → Task 11.
- `DragDataFactory` → Task 12.
- Pointer handlers + `DragDrop.DoDragDrop` → Task 13.
- Window visible during drag → handlers don't touch visibility (Task 13).
- Copy-only effect → `DragDropEffects.Copy` (Task 13).
- Silent cancellation → `DragDataFactory.BuildAsync` returns null on failure; handler logs and returns (Task 13).
- Docs (`docs/ui-drag-drop.md` + `CLAUDE.md` index) → Task 14.

**Type-consistency check:**

- `GetDragPayload` is declared `{ get; set; }` (Task 7 changes this from `init` to `set` to allow `vm.GetDragPayload = vm.BuildDragPayload`). All call sites use the setter form after Task 7.
- `BuildDragPayload` is the consistent name for the cell-aware helper across `ConversionResultItemViewModel`, `AlgebraResultItemViewModel`, `DateSearchResultViewModel`, `EmojiGridResultViewModel`.
- `DragPayload.File` / `DragPayload.Text` names match the spec.
- `DragDataFactory.BuildAsync(Visual, DragPayload)` signature matches the call in Task 13.

**Placeholder scan:** No "TBD" / "TODO" / "implement later" / vague handlers. All steps include concrete code.

**Note on Task 7's mid-task contract change**: Tasks 1-6 use `{ get; init; }`; Task 7 widens it to `{ get; set; }`. This is intentional because Tasks 4-6 work with `init` (the delegate captures a constant), while Tasks 7-11 need post-construction assignment. The change is backward-compatible — existing `init` users still work via the setter.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-22-drag-and-drop-results.md`. Two execution options:

**1. Subagent-Driven (recommended)** — Dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
