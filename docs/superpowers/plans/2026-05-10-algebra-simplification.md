# Algebra Simplification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cuando el usuario escribe una expresión con variables (ej. `x^2-5x+6`), mostrar un ítem navegable con celdas para cada forma útil: simplify, expand, factor, d/dx, ∫dx.

**Architecture:** `NerdamerEngine` adquiere `TryAlgebra()` para expresiones sin `=`. `CalculatorSearch` redirige `UnknownSymbol` a `TryAlgebra` en lugar de descartar silenciosamente. El resultado se muestra como `AlgebraResultItemViewModel` — celdas dinámicas navegables con ←/→, siguiendo el patrón de `DateSearchResultViewModel`.

**Tech Stack:** nerdamer (ya cargado: core + Algebra + Calculus + Solve), Jint, Avalonia 11, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-05-10-algebra-simplification-design.md`

---

## Estructura de ficheros

| Fichero | Acción |
|---------|--------|
| `Yottacast.Core/Search/Calculator/NerdamerEngine.cs` | Añadir records `AlgebraCell`, `AlgebraResult` + método `TryAlgebra` |
| `Yottacast.Core/Search/Calculator/nerdamer-helpers.js` | Añadir función `getAlgebraResults` |
| `Yottacast.Core/ViewModels/AlgebraCellItem.cs` | Nuevo — observable cell (Label + Result + IsSelected) |
| `Yottacast.Core/ViewModels/AlgebraResultItemViewModel.cs` | Nuevo — ViewModel navegable con CellItems + navigation |
| `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` | Routing UnknownSymbol + `BuildAlgebraResult` |
| `Yottacast/Views/Results/AlgebraResultItemView.axaml` | Nuevo — UniformGrid de celdas + label row |
| `Yottacast/Views/Results/AlgebraResultItemView.axaml.cs` | Nuevo — añade `conv-navigable` al ListBoxItem padre |
| `Yottacast/Views/MainWindow.axaml` | Añadir DataTemplate para `AlgebraResultItemViewModel` |
| `Yottacast.Core.Tests/Search/Calculator/AlgebraSearchTests.cs` | Nuevo — tests TDD de NerdamerEngine y CalculatorSearch |
| `docs/search-calculator.md` | Actualizar sección 1.1 + nuevo apartado álgebra simbólica |

---

## Task 1: Records + TryAlgebra stub + tests que fallan

**Files:**
- Modify: `Yottacast.Core/Search/Calculator/NerdamerEngine.cs`
- Create: `Yottacast.Core.Tests/Search/Calculator/AlgebraSearchTests.cs`

- [ ] **Step 1: Añadir records al principio de `NerdamerEngine.cs`, después de `VariableSolution`/`SolveResult`**

```csharp
public record AlgebraCell(
    [property: JsonPropertyName("label")]  string Label,
    [property: JsonPropertyName("result")] string Result);

public record AlgebraResult(AlgebraCell[] Cells);
```

- [ ] **Step 2: Añadir stub de `TryAlgebra` a `NerdamerEngine`, después de `TrySolve`**

```csharp
/// <summary>
/// Evaluates algebraic operations on <paramref name="expr"/> (no '=' required).
/// Returns simplify / expand / factor / derivatives / integral cells where result ≠ input.
/// Returns null if the engine is not ready, no variables found, or all results are trivial.
/// Thread-safe.
/// </summary>
public AlgebraResult? TryAlgebra(string expr) => null;
```

- [ ] **Step 3: Crear `Yottacast.Core.Tests/Search/Calculator/AlgebraSearchTests.cs`**

```csharp
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using Xunit;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search.Calculator;

[Collection("Nerdamer")]
public class AlgebraSearchTests(NerdamerEngineFixture fixture) {

    // ── NerdamerEngine.TryAlgebra direct tests ────────────────────────────────

    [Fact]
    public void TryAlgebra_SimplifiableExpression_HasSimplifyCell() {
        var result = fixture.Engine.TryAlgebra("2*x+3*x");
        Assert.NotNull(result);
        var cell = result.Cells.FirstOrDefault(c => c.Label == "simplify");
        Assert.NotNull(cell);
        Assert.Equal("5*x", cell.Result);
    }

    [Fact]
    public void TryAlgebra_FactorableExpression_HasFactorCell() {
        var result = fixture.Engine.TryAlgebra("x^2-5*x+6");
        Assert.NotNull(result);
        var cell = result.Cells.FirstOrDefault(c => c.Label == "factor");
        Assert.NotNull(cell);
        // nerdamer returns "(x-2)*(x-3)" or "(x-3)*(x-2)"
        Assert.Contains("x-2", cell.Result);
        Assert.Contains("x-3", cell.Result);
    }

    [Fact]
    public void TryAlgebra_Polynomial_HasDerivativeCell() {
        var result = fixture.Engine.TryAlgebra("x^2");
        Assert.NotNull(result);
        var cell = result.Cells.FirstOrDefault(c => c.Label == "d/dx");
        Assert.NotNull(cell);
        Assert.Equal("2*x", cell.Result);
    }

    [Fact]
    public void TryAlgebra_SingleVariable_HasIntegralCell() {
        var result = fixture.Engine.TryAlgebra("x^2");
        Assert.NotNull(result);
        var cell = result.Cells.FirstOrDefault(c => c.Label == "∫dx");
        Assert.NotNull(cell);
        Assert.Contains("x^3", cell.Result);
    }

    [Fact]
    public void TryAlgebra_MultiVariable_NoIntegralCell() {
        var result = fixture.Engine.TryAlgebra("x*y+2*x");
        Assert.NotNull(result);
        Assert.DoesNotContain(result.Cells, c => c.Label.StartsWith("∫"));
    }

    [Fact]
    public void TryAlgebra_MultiVariable_HasDerivativePerVariable() {
        var result = fixture.Engine.TryAlgebra("x*y+2*x");
        Assert.NotNull(result);
        Assert.Contains(result.Cells, c => c.Label == "d/dx");
        Assert.Contains(result.Cells, c => c.Label == "d/dy");
    }

    [Fact]
    public void TryAlgebra_NoCells_WhenAllResultsMatchInput() {
        // "2+3" has no variables — nerdamer returns empty variables list
        var result = fixture.Engine.TryAlgebra("2+3");
        Assert.Null(result);
    }

    [Fact]
    public void TryAlgebra_PlainText_ReturnsNull() {
        var result = fixture.Engine.TryAlgebra("hello world");
        Assert.Null(result);
    }

    [Fact]
    public void TryAlgebra_DuplicateResults_Deduplicated() {
        var result = fixture.Engine.TryAlgebra("x^2-5*x+6");
        if (result == null) return;
        var values = result.Cells.Select(c => c.Result).ToList();
        Assert.Equal(values.Distinct().Count(), values.Count);
    }
}
```

- [ ] **Step 4: Ejecutar tests y verificar que fallan**

```bash
cd "Yottacast.Core.Tests" && dotnet test --filter "AlgebraSearchTests" -v
```

Resultado esperado: FAIL — `TryAlgebra_SimplifiableExpression_HasSimplifyCell` falla con "Expected: not null — Actual: null".

- [ ] **Step 5: Commit**

```bash
git add Yottacast.Core/Search/Calculator/NerdamerEngine.cs \
        Yottacast.Core.Tests/Search/Calculator/AlgebraSearchTests.cs
git commit -m "test: failing tests for NerdamerEngine.TryAlgebra algebra operations"
```

---

## Task 2: Implementar `getAlgebraResults` en JS + `TryAlgebra` en C#

**Files:**
- Modify: `Yottacast.Core/Search/Calculator/nerdamer-helpers.js`
- Modify: `Yottacast.Core/Search/Calculator/NerdamerEngine.cs`

- [ ] **Step 1: Añadir `getAlgebraResults` al final de `nerdamer-helpers.js`**

```javascript
// getAlgebraResults(expr) → JSON string [{label, result}, ...] | null
//
// Tries simplify, expand, factor, diff(per variable), integrate(single var only).
// Filters: drops cells where result === normalized input.
// Deduplicates: keeps first cell per unique result string.
// Returns null when: no variables found, nerdamer can't parse, or all cells filtered.
function getAlgebraResults(expr) {
    try {
        var vars;
        try {
            vars = nerdamer(expr).variables();
        } catch (e) {
            return null;
        }
        if (!vars || vars.length === 0) return null;

        // Normalize input for no-op comparison
        var normalized;
        try {
            normalized = nerdamer(expr).text();
        } catch (e) {
            normalized = expr;
        }

        var results = [];
        var seenResults = {};

        function tryOp(label, fn) {
            try {
                var r = fn();
                if (!r) return;
                var text = r.text ? r.text() : String(r);
                if (text === normalized || text === expr) return; // no-op
                if (seenResults[text]) return;                    // deduplicate
                seenResults[text] = true;
                results.push({ label: label, result: text });
            } catch (e) { /* skip failed operations */ }
        }

        tryOp('simplify', function() { return nerdamer(expr); });
        tryOp('expand',   function() { return nerdamer.expand(expr); });
        tryOp('factor',   function() { return nerdamer.factor(expr); });

        // Derivatives — one per variable, alphabetical order
        var sortedVars = vars.slice().sort();
        for (var i = 0; i < sortedVars.length; i++) {
            (function(v) {
                tryOp('d/d' + v, function() { return nerdamer.diff(expr, v); });
            })(sortedVars[i]);
        }

        // Integral — only for single-variable expressions
        if (vars.length === 1) {
            tryOp('∫d' + vars[0], function() { return nerdamer.integrate(expr, vars[0]); });
        }

        if (results.length === 0) return null;
        return JSON.stringify(results);
    } catch (e) {
        return null;
    }
}
```

- [ ] **Step 2: Reemplazar el stub de `TryAlgebra` en `NerdamerEngine.cs`**

Reemplazar:
```csharp
public AlgebraResult? TryAlgebra(string expr) => null;
```

Por:
```csharp
public AlgebraResult? TryAlgebra(string expr) {
    if (_engine == null) return null;
    lock (_lock) {
        if (_engine == null) return null;
        try {
            var json = _engine.Evaluate($"getAlgebraResults({JsonSerializer.Serialize(expr)})");
            if (json.IsNull() || json.IsUndefined()) return null;
            var jsonStr = json.AsString();
            if (string.IsNullOrEmpty(jsonStr)) return null;
            var cells = JsonSerializer.Deserialize<AlgebraCell[]>(jsonStr);
            if (cells == null || cells.Length == 0) return null;
            return new AlgebraResult(cells);
        } catch {
            return null;
        }
    }
}
```

- [ ] **Step 3: Ejecutar tests de Task 1 y verificar que pasan**

```bash
cd "Yottacast.Core.Tests" && dotnet test --filter "AlgebraSearchTests" -v
```

Resultado esperado: todos los tests de `NerdamerEngine.TryAlgebra` PASS.

- [ ] **Step 4: Ejecutar suite completa para comprobar no hay regresiones**

```bash
cd "Yottacast.Core.Tests" && dotnet test -v
```

Resultado esperado: todos los tests pasan.

- [ ] **Step 5: Commit**

```bash
git add Yottacast.Core/Search/Calculator/nerdamer-helpers.js \
        Yottacast.Core/Search/Calculator/NerdamerEngine.cs
git commit -m "feat: implement getAlgebraResults in nerdamer-helpers + TryAlgebra in NerdamerEngine"
```

---

## Task 3: `AlgebraCellItem` + `AlgebraResultItemViewModel`

**Files:**
- Create: `Yottacast.Core/ViewModels/AlgebraCellItem.cs`
- Create: `Yottacast.Core/ViewModels/AlgebraResultItemViewModel.cs`

- [ ] **Step 1: Crear `Yottacast.Core/ViewModels/AlgebraCellItem.cs`**

```csharp
using System.ComponentModel;

namespace Yottacast.Core.ViewModels;

/// <summary>
/// Represents a single algebra result cell inside AlgebraResultItemViewModel.
/// Holds the operation label (e.g. "factor"), the symbolic result, and observable selection state.
/// </summary>
public sealed class AlgebraCellItem : INotifyPropertyChanged {
    public string Label  { get; }
    public string Result { get; }

    private bool _isSelected;
    public bool IsSelected {
        get => _isSelected;
        set {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AlgebraCellItem(string label, string result, bool isSelected = false) {
        Label       = label;
        Result      = result;
        _isSelected = isSelected;
    }
}
```

- [ ] **Step 2: Crear `Yottacast.Core/ViewModels/AlgebraResultItemViewModel.cs`**

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.ViewModels;

/// <summary>
/// Result item for algebraic expressions (simplify / expand / factor / derivatives / integral).
/// Cells are navigated left/right; Enter copies the selected cell's Result.
/// Follows the same pattern as DateSearchResultViewModel.
/// </summary>
public class AlgebraResultItemViewModel : BaseResultItemViewModel, INotifyPropertyChanged {
    public string Icon     { get; init; } = "🧮";
    public string Category { get; init; } = "Calculator";

    // ── Cells ─────────────────────────────────────────────────────────────────
    private IReadOnlyList<AlgebraCell> _cells = [];

    public IReadOnlyList<AlgebraCell> Cells {
        get => _cells;
        init {
            _cells    = value;
            CellItems = value.Select((c, i) => new AlgebraCellItem(c.Label, c.Result, isSelected: i == 0))
                             .ToList();
        }
    }

    /// <summary>Per-cell items exposed to the view for UniformGrid rendering.</summary>
    public IReadOnlyList<AlgebraCellItem> CellItems { get; private set; } = [];

    // ── Selection ─────────────────────────────────────────────────────────────
    private int _selectedCell;

    public int SelectedCell {
        get => _selectedCell;
        set {
            if (_selectedCell == value) return;
            foreach (var (item, i) in CellItems.Select((c, i) => (c, i)))
                item.IsSelected = i == value;
            _selectedCell = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCell)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCellLabel)));
        }
    }

    /// <summary>Label of the currently selected cell (e.g. "factor", "d/dx").</summary>
    public string SelectedCellLabel =>
        CellItems.Count > _selectedCell ? CellItems[_selectedCell].Label : "";

    /// <summary>Move selection one cell to the left (circular). Returns false if Cells.Count ≤ 1.</summary>
    public bool MoveCellLeft() {
        if (Cells.Count <= 1) return false;
        SelectedCell = (_selectedCell - 1 + Cells.Count) % Cells.Count;
        return true;
    }

    /// <summary>Move selection one cell to the right (circular). Returns false if Cells.Count ≤ 1.</summary>
    public bool MoveCellRight() {
        if (Cells.Count <= 1) return false;
        SelectedCell = (_selectedCell + 1) % Cells.Count;
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
```

- [ ] **Step 3: Compilar para verificar que no hay errores**

```bash
cd "Yottacast.Core" && dotnet build
```

Resultado esperado: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Yottacast.Core/ViewModels/AlgebraCellItem.cs \
        Yottacast.Core/ViewModels/AlgebraResultItemViewModel.cs
git commit -m "feat: add AlgebraCellItem and AlgebraResultItemViewModel"
```

---

## Task 4: Routing en `CalculatorSearch` + tests de integración

**Files:**
- Modify: `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`
- Modify: `Yottacast.Core.Tests/Search/Calculator/AlgebraSearchTests.cs`

- [ ] **Step 1: Añadir tests de integración a `AlgebraSearchTests.cs`**

Añadir al final de la clase `AlgebraSearchTests` (antes del `}`):

```csharp
    // ── CalculatorSearch integration tests ───────────────────────────────────

    private CalculatorSearch MakeCalcSearch() {
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        clipboard.Initialize(copy: _ => { }, read: () => Task.FromResult<string?>(null));
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        var provider = new MathJsEngineProvider();
        var exchangeRateService = new ExchangeRateService(new HttpClient(), settings,
            NullLogger<ExchangeRateService>.Instance);
        return new CalculatorSearch(provider, exchangeRateService, clipboard, settings,
            NullLogger<CalculatorSearch>.Instance, fixture.Engine);
    }

    [Fact]
    public void CalculatorSearch_AlgebraExpression_ReturnsAlgebraResultViewModel() {
        var search = MakeCalcSearch();
        var results = search.Search("2*x+3*x", 5);
        var item = Assert.Single(results);
        Assert.IsType<AlgebraResultItemViewModel>(item);
    }

    [Fact]
    public void CalculatorSearch_AlgebraExpression_HasExpectedCells() {
        var search = MakeCalcSearch();
        var results = search.Search("2*x+3*x", 5);
        var vm = Assert.IsType<AlgebraResultItemViewModel>(Assert.Single(results));
        Assert.Contains(vm.CellItems, c => c.Label == "simplify");
    }

    [Fact]
    public void CalculatorSearch_AlgebraActivate_CopiesSelectedCellResult() {
        string? copied = null;
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        clipboard.Initialize(copy: text => copied = text, read: () => Task.FromResult<string?>(null));
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        var provider = new MathJsEngineProvider();
        var exchangeRateService = new ExchangeRateService(new HttpClient(), settings,
            NullLogger<ExchangeRateService>.Instance);
        var search = new CalculatorSearch(provider, exchangeRateService, clipboard, settings,
            NullLogger<CalculatorSearch>.Instance, fixture.Engine);

        var results = search.Search("2*x+3*x", 5);
        var vm = Assert.IsType<AlgebraResultItemViewModel>(Assert.Single(results));
        var enterAction = vm.Actions.First(a => a.Hotkey == ActionHotkey.Enter);
        enterAction.Execute();

        Assert.NotNull(copied);
        Assert.Equal(vm.CellItems[0].Result, copied);
    }

    [Fact]
    public void CalculatorSearch_PlainText_ReturnsEmpty() {
        var search = MakeCalcSearch();
        Assert.Empty(search.Search("safari to km", 5));
    }

    [Fact]
    public void CalculatorSearch_NumericExpression_NotRoutedToAlgebra() {
        // "2+3" is handled by math.js (returns CalcResult), never reaches TryAlgebra
        var search = MakeCalcSearch();
        var results = search.Search("2+3", 5);
        var item = Assert.Single(results);
        Assert.IsType<CalculatorResultItemViewModel>(item);
    }
```

- [ ] **Step 2: Ejecutar solo estos tests nuevos para verificar que fallan**

```bash
cd "Yottacast.Core.Tests" && dotnet test --filter "CalculatorSearch_AlgebraExpression_ReturnsAlgebraResultViewModel" -v
```

Resultado esperado: FAIL — `Assert.IsType<AlgebraResultItemViewModel>` falla porque el resultado es vacío.

- [ ] **Step 3: Añadir el caso `UnknownSymbol` al switch de `CalculatorSearch.Search()`**

En `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`, localizar el switch que termina en (línea ~178):

```csharp
            case ErrorResult r when r.ErrorKind is CalcErrorKind.IncompatibleUnitsConvert or CalcErrorKind.IncompatibleUnitsOp:
                LastHint = BuildErrorHint(r);
                LastHintKind = SearchHintKind.Error;
                logger.LogDebug("Calculator query=\"{Query}\" → error {Kind}: {Hint}", q, r.ErrorKind, LastHint);
                break;
        }

        return [];
```

Añadir el nuevo case **después** del bloque `IncompatibleUnits`, **antes** del `}` de cierre del switch:

```csharp
            case ErrorResult r when r.ErrorKind is CalcErrorKind.IncompatibleUnitsConvert or CalcErrorKind.IncompatibleUnitsOp:
                LastHint = BuildErrorHint(r);
                LastHintKind = SearchHintKind.Error;
                logger.LogDebug("Calculator query=\"{Query}\" → error {Kind}: {Hint}", q, r.ErrorKind, LastHint);
                break;
            case ErrorResult r when r.ErrorKind == CalcErrorKind.UnknownSymbol: {
                var algebraResult = nerdamerEngine.TryAlgebra(q);
                if (algebraResult != null) return BuildAlgebraResult(algebraResult, q);
                break;
            }
        }

        return [];
```

- [ ] **Step 4: Añadir el método `BuildAlgebraResult` al final de `CalculatorSearch`**

Añadir justo antes del `}` de cierre de la clase:

```csharp
    private IReadOnlyList<BaseResultItemViewModel> BuildAlgebraResult(AlgebraResult result, string originalQuery) {
        logger.LogDebug("Algebra query=\"{Query}\" → {Count} cells: {Labels}",
            originalQuery, result.Cells.Length,
            string.Join(", ", result.Cells.Select(c => c.Label)));

        AlgebraResultItemViewModel vm = null!;
        vm = new AlgebraResultItemViewModel {
            Title   = result.Cells[0].Result,
            Cells   = result.Cells,
            Score   = 7,
            OnLeft  = result.Cells.Length > 1 ? () => vm.MoveCellLeft()  : null,
            OnRight = result.Cells.Length > 1 ? () => vm.MoveCellRight() : null,
            Actions = [
                new() {
                    Label           = "Copy result",
                    Hotkey          = ActionHotkey.Enter,
                    ShowInFooter    = true,
                    ShowInMenu      = true,
                    ClosesMenu      = true,
                    ClosesWindow    = true,
                    PasteAfterClose = true,
                    Execute = () => {
                        var copied = vm.CellItems[vm.SelectedCell].Result;
                        logger.LogInformation("Algebra: copied \"{Value}\"", copied);
                        clipboard.CopyText(copied);
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
                        var copied = vm.CellItems[vm.SelectedCell].Result;
                        logger.LogInformation("Algebra: copied via Cmd+C \"{Value}\"", copied);
                        clipboard.CopyText(copied);
                    },
                },
            ],
        };
        return [vm];
    }
```

- [ ] **Step 5: Añadir `using Yottacast.Core.ViewModels;` si no está ya en `CalculatorSearch.cs`**

Verificar que el fichero tiene:
```csharp
using Yottacast.Core.ViewModels;
```

Si no está, añadirlo en los usings del principio.

- [ ] **Step 6: Ejecutar toda la suite de tests**

```bash
cd "Yottacast.Core.Tests" && dotnet test -v
```

Resultado esperado: todos los tests pasan, incluyendo los nuevos de integración de `AlgebraSearchTests`.

- [ ] **Step 7: Commit**

```bash
git add Yottacast.Core/Search/Calculator/CalculatorSearch.cs \
        Yottacast.Core.Tests/Search/Calculator/AlgebraSearchTests.cs
git commit -m "feat: route UnknownSymbol queries to NerdamerEngine.TryAlgebra in CalculatorSearch"
```

---

## Task 5: Vista Avalonia

**Files:**
- Create: `Yottacast/Views/Results/AlgebraResultItemView.axaml`
- Create: `Yottacast/Views/Results/AlgebraResultItemView.axaml.cs`
- Modify: `Yottacast/Views/MainWindow.axaml`

- [ ] **Step 1: Crear `Yottacast/Views/Results/AlgebraResultItemView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:viewModels="clr-namespace:Yottacast.Core.ViewModels;assembly=Yottacast.Core"
             x:Class="Yottacast.Views.Results.AlgebraResultItemView"
             x:DataType="viewModels:AlgebraResultItemViewModel">
    <Grid ColumnDefinitions="*" RowDefinitions="*,Auto" MinHeight="68"
          TextElement.FontFamily="{DynamicResource Theme.Conv.FontFamily}">

        <!-- Row 0: cells row — fills full width uniformly -->
        <ItemsControl Grid.Row="0"
                      ItemsSource="{Binding CellItems}"
                      VerticalAlignment="Center"
                      Margin="0,8">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <UniformGrid Rows="1"/>
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="viewModels:AlgebraCellItem">
                    <Border CornerRadius="{DynamicResource Theme.Conv.Cell.CornerRadius}"
                            Padding="6,4"
                            HorizontalAlignment="Stretch"
                            Classes.conv-cell-selected="{Binding IsSelected}">
                        <TextBlock Text="{Binding Result}"
                                   Foreground="{DynamicResource Theme.Conv.Value.Color}"
                                   FontSize="{DynamicResource Theme.Conv.Value.Size}"
                                   FontWeight="Medium"
                                   HorizontalAlignment="Center"
                                   TextAlignment="Center"
                                   TextWrapping="Wrap"/>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <!-- Row 1: label of the selected cell (e.g. "factor", "d/dx") -->
        <TextBlock Grid.Row="1"
                   Text="{Binding SelectedCellLabel}"
                   Foreground="{DynamicResource Theme.Conv.Subtitle.Color}"
                   FontSize="{DynamicResource Theme.Conv.Subtitle.Size}"
                   Opacity="{DynamicResource Theme.Conv.Subtitle.Opacity}"
                   HorizontalAlignment="Center"
                   TextAlignment="Center"
                   Margin="0,0,0,6"/>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Crear `Yottacast/Views/Results/AlgebraResultItemView.axaml.cs`**

```csharp
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Yottacast.Views.Results;

public partial class AlgebraResultItemView : UserControl {
    private ListBoxItem? _taggedItem;

    public AlgebraResultItemView() {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        var parent = this.GetVisualParent();
        while (parent != null) {
            if (parent is ListBoxItem lbi) {
                lbi.Classes.Add("conv-navigable");
                _taggedItem = lbi;
                break;
            }
            parent = parent.GetVisualParent();
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
        if (_taggedItem != null) {
            _taggedItem.Classes.Remove("conv-navigable");
            _taggedItem = null;
        }
        base.OnDetachedFromVisualTree(e);
    }
}
```

- [ ] **Step 3: Añadir DataTemplate en `Yottacast/Views/MainWindow.axaml`**

Localizar el bloque de DataTemplates (alrededor de la línea 297). Añadir **antes** del template de `CalculatorResultItemViewModel`:

```xml
                        <!-- Algebra template (navigable cells for simplify/expand/factor/diff/integrate) -->
                        <DataTemplate x:DataType="viewModels:AlgebraResultItemViewModel">
                            <results:AlgebraResultItemView/>
                        </DataTemplate>
```

Quedar así (orden importa — tipos más específicos primero):
```xml
                        <!-- Template for emoji grid (more specific type first) -->
                        <DataTemplate x:DataType="viewModels:EmojiGridResultViewModel">
                            <results:EmojiGridResultView/>
                        </DataTemplate>

                        <!-- Algebra template (navigable cells for simplify/expand/factor/diff/integrate) -->
                        <DataTemplate x:DataType="viewModels:AlgebraResultItemViewModel">
                            <results:AlgebraResultItemView/>
                        </DataTemplate>

                        <!-- Calculator template (title + subtitle, no icon/category) -->
                        <DataTemplate x:DataType="viewModels:CalculatorResultItemViewModel">
                            <results:CalculatorResultItemView/>
                        </DataTemplate>
```

- [ ] **Step 4: Compilar proyecto GUI**

```bash
cd "Yottacast" && dotnet build
```

Resultado esperado: Build succeeded, 0 errors, 0 warnings relevantes.

- [ ] **Step 5: Ejecutar app manualmente y probar**

```bash
cd "Yottacast" && dotnet run
```

Probar:
- Escribir `x^2-5x+6` → debe aparecer un ítem con celdas `factor`, `d/dx`
- Navegar con ←/→ entre celdas
- Celda inferior muestra el label de la seleccionada (ej. "factor")
- Enter copia el resultado de la celda seleccionada

- [ ] **Step 6: Commit**

```bash
git add Yottacast/Views/Results/AlgebraResultItemView.axaml \
        Yottacast/Views/Results/AlgebraResultItemView.axaml.cs \
        Yottacast/Views/MainWindow.axaml
git commit -m "feat: AlgebraResultItemView — navigable cells for algebraic results"
```

---

## Task 6: Documentación

**Files:**
- Modify: `docs/search-calculator.md`

- [ ] **Step 1: Actualizar tabla de tipos de entrada en la sección 1.1**

Añadir filas a la tabla "Qué puede hacer el usuario" en `docs/search-calculator.md`:

```markdown
| Simplificación algebraica | `2*x+3*x`             | Celdas navegables: `simplify: 5*x`, `d/dx: 5`, `∫dx: 5x^2/2` |
| Factorización             | `x^2-5x+6`            | Celdas navegables: `factor: (x-2)*(x-3)`, `d/dx: 2*x-5`      |
| Derivada/integral         | `sin(x)`              | Celdas navegables: `d/dx: cos(x)`, `∫dx: -cos(x)`            |
```

- [ ] **Step 2: Añadir sección "Álgebra simbólica" en `docs/search-calculator.md`**

Añadir tras la sección "Conversiones de unidades" (sección 3) y antes de "Normalización natural" (sección 4):

```markdown
## 3b. Álgebra simbólica

Cuando la query no contiene `=` y math.js no puede evaluarla (contiene variables como `x`, `y`, `t`), la query se redirige al motor nerdamer para álgebra simbólica.

El resultado es un ítem navegable con hasta N celdas, una por operación útil. Las celdas se navegan con ←/→; Enter copia el resultado de la celda seleccionada y lo pega en la app anterior.

| Operación  | Cuándo aparece                          | Ejemplo entrada  | Ejemplo celda           |
|------------|-----------------------------------------|------------------|-------------------------|
| simplify   | Resultado ≠ input normalizado           | `2*x+3*x`        | `5*x`                   |
| expand     | Resultado ≠ input normalizado           | `(x+1)^2`        | `x^2+2*x+1`             |
| factor     | Resultado ≠ input normalizado           | `x^2-5*x+6`      | `(x-2)*(x-3)`           |
| d/dx       | Siempre (una celda por variable, a–z)   | `x^2`            | `2*x`                   |
| ∫dx        | Solo si la expresión tiene 1 variable   | `x^2`            | `x^3/3`                 |

Las celdas donde el resultado es igual al input se descartan (no-ops). Las celdas con resultado duplicado se deduplicarán, conservando la primera (prioridad: simplify > expand > factor > d/dx > ∫dx).

Si tras el filtrado no queda ninguna celda útil, no se muestra ningún resultado.

El texto plano sin estructura matemática (`safari to km`) no produce resultado: nerdamer no detecta variables y devuelve `null`.

> **Verificar en:** `getAlgebraResults()` en `nerdamer-helpers.js`; `NerdamerEngine.TryAlgebra()` en `NerdamerEngine.cs`; routing `UnknownSymbol` y `BuildAlgebraResult()` en `CalculatorSearch.cs`
```

- [ ] **Step 3: Commit**

```bash
git add docs/search-calculator.md
git commit -m "docs: document symbolic algebra feature in search-calculator.md"
```

---

## Self-review

**Cobertura del spec:**
- ✅ Routing UnknownSymbol → TryAlgebra (Task 4)
- ✅ `getAlgebraResults`: simplify, expand, factor, d/dx por variable, ∫dx solo 1 var (Task 2)
- ✅ Filtrado no-ops + deduplicación (Task 2 JS)
- ✅ `AlgebraCell` + `AlgebraResult` records (Task 1)
- ✅ `AlgebraCellItem` observable (Task 3)
- ✅ `AlgebraResultItemViewModel` con navegación circular ←/→ (Task 3)
- ✅ `SelectedCellLabel` para subtítulo (Task 3)
- ✅ `BuildAlgebraResult`: Score=7, Enter copia+pega, Cmd+C copia sin cerrar (Task 4)
- ✅ Vista AXAML: UniformGrid, `conv-cell-selected`, code-behind añade `conv-navigable` (Task 5)
- ✅ DataTemplate en MainWindow.axaml (Task 5)
- ✅ Tests: TryAlgebra directo + integración CalculatorSearch (Tasks 1, 4)
- ✅ Documentación (Task 6)

**Sin placeholders ni referencias rotas.**
