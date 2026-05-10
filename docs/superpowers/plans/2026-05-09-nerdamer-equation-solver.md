# Nerdamer Equation Solver — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir resolución simbólica de ecuaciones al launcher usando nerdamer ejecutado en un engine Jint separado; el usuario escribe `2x-5=2` y obtiene `x = 3.5` que se copia al portapapeles.

**Architecture:** `NerdamerEngine` carga `nerdamer.core.min.js` + `Algebra.min.js` en su propio engine Jint, inicializa en background, y expone `TrySolve(query)`. `CalculatorSearch` detecta `=` en la query, llama a `NerdamerEngine` primero, y devuelve el resultado sin tocar math.js.

**Tech Stack:** Jint 3.1.0, nerdamer 1.1.13 (core + Algebra addon), xUnit, mstest

**Worktree:** `.claude/worktrees/feat/nerdamer-equation-solver`

---

## File Map

| Archivo | Acción |
|---------|--------|
| `Yottacast.Core/Yottacast.Core.csproj` | Añadir 2 targets descarga + 3 EmbeddedResource |
| `Yottacast.Core/Search/Calculator/nerdamer.core.min.js` | Descargado por build target |
| `Yottacast.Core/Search/Calculator/Algebra.min.js` | Descargado por build target |
| `Yottacast.Core/Search/Calculator/nerdamer-helpers.js` | Nuevo — wrapper JS |
| `Yottacast.Core/Search/Calculator/NerdamerEngine.cs` | Nuevo — engine Jint para nerdamer |
| `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` | Modificar — detección `=` + `BuildEquationResult` |
| `Yottacast/App.axaml.cs` | Añadir registro DI de `NerdamerEngine` |
| `Yottacast.Core.Tests/Search/NerdamerEngineFixture.cs` | Nuevo — fixture compartida |
| `Yottacast.Core.Tests/Search/Calculator/EquationSolverTests.cs` | Nuevo — tests de ecuaciones |

---

## Task 1: Build setup — descargar nerdamer y embeber como EmbeddedResource

**Files:**
- Modify: `Yottacast.Core/Yottacast.Core.csproj`

- [ ] **Añadir download targets y EmbeddedResource al csproj**

Buscar el bloque `DownloadMathJs` y añadir justo después:

```xml
  <!-- Download nerdamer on first build if not already present -->
  <Target Name="DownloadNerdamerCore" BeforeTargets="BeforeBuild">
    <MakeDir Directories="$(MSBuildThisFileDirectory)Search/Calculator" />
    <Exec
      Command="curl -fsSL https://cdn.jsdelivr.net/npm/nerdamer@1.1.13/nerdamer.core.min.js -o &quot;$(MSBuildThisFileDirectory)Search/Calculator/nerdamer.core.min.js&quot;"
      Condition="!Exists('$(MSBuildThisFileDirectory)Search/Calculator/nerdamer.core.min.js')" />
  </Target>

  <Target Name="DownloadNerdamerAlgebra" BeforeTargets="BeforeBuild">
    <MakeDir Directories="$(MSBuildThisFileDirectory)Search/Calculator" />
    <Exec
      Command="curl -fsSL https://cdn.jsdelivr.net/npm/nerdamer@1.1.13/Algebra.min.js -o &quot;$(MSBuildThisFileDirectory)Search/Calculator/Algebra.min.js&quot;"
      Condition="!Exists('$(MSBuildThisFileDirectory)Search/Calculator/Algebra.min.js')" />
  </Target>
```

En el `ItemGroup` de `EmbeddedResource`, añadir junto a `math.min.js`:

```xml
    <EmbeddedResource Include="Search/Calculator/nerdamer.core.min.js" Condition="Exists('Search/Calculator/nerdamer.core.min.js')" />
    <EmbeddedResource Include="Search/Calculator/Algebra.min.js" Condition="Exists('Search/Calculator/Algebra.min.js')" />
    <EmbeddedResource Include="Search/Calculator/nerdamer-helpers.js" />
```

- [ ] **Disparar el build para descargar los archivos**

```bash
cd Yottacast.Core && dotnet build 2>&1 | grep -E "(nerdamer|Algebra|error|warning)" | head -20
```

Resultado esperado: los archivos se descargan sin errores de curl.

- [ ] **Verificar que los archivos existen**

```bash
ls -lh Yottacast.Core/Search/Calculator/nerdamer.core.min.js Yottacast.Core/Search/Calculator/Algebra.min.js
```

Ambos deben existir y tener tamaño > 0.

- [ ] **Commit**

```bash
git add Yottacast.Core/Yottacast.Core.csproj
git commit -m "build: add nerdamer download targets and EmbeddedResource entries"
```

---

## Task 2: Crear nerdamer-helpers.js

**Files:**
- Create: `Yottacast.Core/Search/Calculator/nerdamer-helpers.js`

- [ ] **Crear el archivo JS wrapper**

```javascript
// nerdamer-helpers.js
// Loaded in a dedicated Jint engine (separate from mathjs).
// Requires: nerdamer.core.min.js + Algebra.min.js loaded before this file.
//
// Exposes: solveEquation(query) → JSON string | null
//
// Returns null when:
//   - No '=' in query
//   - No variables found
//   - All solutions are trivial (solution === variable name)
//   - nerdamer throws (syntax error, unsupported expression)

function solveEquation(query) {
    try {
        var eqIdx = query.indexOf('=');
        if (eqIdx < 0) return null;

        var lhs = query.substring(0, eqIdx).trim();
        var rhs = query.substring(eqIdx + 1).trim();
        if (!lhs || !rhs) return null;

        // Extract all variables from both sides of the equation.
        // nerdamer(expr).variables() returns an array of variable name strings.
        var allVars;
        try {
            allVars = nerdamer('(' + lhs + ')+(' + rhs + ')').variables();
        } catch (e) {
            return null;
        }
        if (!allVars || allVars.length === 0) return null;

        var results = [];
        for (var i = 0; i < allVars.length; i++) {
            var v = allVars[i];
            try {
                // nerdamer supports the "lhs=rhs" equation format in solveFor.
                var solObj = nerdamer(lhs + '=' + rhs).solveFor(v);
                // solveFor returns a nerdamer object; toArray() yields individual solutions.
                var solArr = solObj.toArray ? solObj.toArray() : [solObj];
                if (!solArr || solArr.length === 0) continue;

                var solStrs = [];
                for (var j = 0; j < solArr.length; j++) {
                    var sol = solArr[j];
                    var solText = sol.text ? sol.text() : String(sol);

                    // Check for free variables in this solution (parametric case).
                    var freeVars = [];
                    try {
                        freeVars = nerdamer(solText).variables().filter(function (sv) {
                            return sv !== v;
                        });
                    } catch (e) { /* keep symbolic */ }

                    if (freeVars.length === 0) {
                        // No free variables: evaluate numerically (e.g. "7/2" → "3.5").
                        try {
                            var evaled = nerdamer(solText).evaluate().text();
                            solText = evaled;
                        } catch (e) { /* keep symbolic */ }
                    }

                    // Filter trivial: solution equals the variable itself.
                    if (solText !== v) {
                        solStrs.push(solText);
                    }
                }

                if (solStrs.length > 0) {
                    results.push({ variable: v, solutions: solStrs });
                }
            } catch (e) {
                // solveFor failed for this variable — skip it.
            }
        }

        if (results.length === 0) return null;
        return JSON.stringify(results);
    } catch (e) {
        return null;
    }
}
```

- [ ] **Commit**

```bash
git add Yottacast.Core/Search/Calculator/nerdamer-helpers.js
git commit -m "feat: add nerdamer-helpers.js with solveEquation wrapper"
```

---

## Task 3: Crear NerdamerEngine.cs (shell con records)

**Files:**
- Create: `Yottacast.Core/Search/Calculator/NerdamerEngine.cs`

- [ ] **Crear la clase con records y shell de TrySolve que siempre devuelve null**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Jint;

namespace Yottacast.Core.Search.Calculator;

public record VariableSolution(
    [property: JsonPropertyName("variable")] string Variable,
    [property: JsonPropertyName("solutions")] string[] Solutions);

public record SolveResult(VariableSolution[] Variables);

/// <summary>
/// Wraps a Jint engine loaded with nerdamer (core + Algebra addon).
/// Solves algebraic equations symbolically: "2x-5=2" → x = 3.5.
/// Initializes in background; TrySolve returns null while not ready.
/// Thread-safe: a lock guards the engine during evaluation.
/// </summary>
public sealed class NerdamerEngine : IDisposable {
    private readonly Lock _lock = new();
    private volatile Engine? _engine;
    private readonly Task _initTask;

    public NerdamerEngine() {
        _initTask = Task.Run(Initialize);
    }

    private void Initialize() {
        var engine = new Engine(opts => opts.LimitRecursion(64));
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.nerdamer.core.min.js"));
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.Algebra.min.js"));
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.nerdamer-helpers.js"));
        lock (_lock) {
            _engine = engine;
        }
    }

    public Task WhenReady() => _initTask;

    /// <summary>
    /// Solves the equation in <paramref name="query"/> (must contain '=').
    /// Returns null if the engine is not ready, the query has no variables,
    /// all solutions are trivial, or nerdamer throws.
    /// Thread-safe.
    /// </summary>
    public SolveResult? TrySolve(string query) {
        if (_engine == null) return null;
        lock (_lock) {
            if (_engine == null) return null;
            try {
                var json = _engine.Evaluate($"solveEquation({JsonSerializer.Serialize(query)})");
                if (json.IsNull() || json.IsUndefined()) return null;
                var jsonStr = json.AsString();
                if (string.IsNullOrEmpty(jsonStr)) return null;
                var vars = JsonSerializer.Deserialize<VariableSolution[]>(jsonStr);
                if (vars == null || vars.Length == 0) return null;
                return new SolveResult(vars);
            } catch {
                return null;
            }
        }
    }

    private static string LoadResource(string name) {
        using var stream = typeof(NerdamerEngine).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded resource not found: {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose() {
        lock (_lock) {
            _engine = null;
        }
    }
}
```

- [ ] **Verificar que compila**

```bash
cd Yottacast.Core && dotnet build 2>&1 | tail -5
```

Esperado: `Build succeeded` sin errores.

- [ ] **Commit**

```bash
git add Yottacast.Core/Search/Calculator/NerdamerEngine.cs
git commit -m "feat: add NerdamerEngine shell with VariableSolution and SolveResult records"
```

---

## Task 4: Crear fixture y tests de NerdamerEngine (failing)

**Files:**
- Create: `Yottacast.Core.Tests/Search/NerdamerEngineFixture.cs`
- Create: `Yottacast.Core.Tests/Search/Calculator/EquationSolverTests.cs`

- [ ] **Crear la fixture compartida**

```csharp
// Yottacast.Core.Tests/Search/NerdamerEngineFixture.cs
using Xunit;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.Tests.Search;

/// <summary>
/// Shared fixture that initializes NerdamerEngine once for all equation test classes.
/// Engine init loads nerdamer (~500KB of JS); sharing avoids re-parsing per class.
/// </summary>
public sealed class NerdamerEngineFixture : IAsyncLifetime {
    public NerdamerEngine Engine { get; } = new();

    public Task InitializeAsync() => Engine.WhenReady();

    public Task DisposeAsync() {
        Engine.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("Nerdamer")]
public class NerdamerCollection : ICollectionFixture<NerdamerEngineFixture>;
```

- [ ] **Crear los tests (todos deben fallar porque NerdamerEngine aún no está implementado)**

```csharp
// Yottacast.Core.Tests/Search/Calculator/EquationSolverTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using Xunit;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search.Calculator;

[Collection("Nerdamer")]
public class EquationSolverTests(NerdamerEngineFixture fixture) {

    // ── NerdamerEngine.TrySolve direct tests ─────────────────────────────────

    [Theory]
    [InlineData("2x-5=2",     "x", "3.5")]
    [InlineData("x+3=7",      "x", "4")]
    [InlineData("3x=9",       "x", "3")]
    public void TrySolve_LinearEquation_ReturnsSolution(string query, string variable, string expected) {
        var result = fixture.Engine.TrySolve(query);
        Assert.NotNull(result);
        var v = result.Variables.First(v => v.Variable == variable);
        Assert.Equal(expected, Assert.Single(v.Solutions));
    }

    [Fact]
    public void TrySolve_QuadraticTwoRealSolutions_ReturnsBoth() {
        var result = fixture.Engine.TrySolve("x^2-5*x+6=0");
        Assert.NotNull(result);
        var v = result.Variables.First(v => v.Variable == "x");
        Assert.Equal(2, v.Solutions.Length);
        Assert.Contains("2", v.Solutions);
        Assert.Contains("3", v.Solutions);
    }

    [Fact]
    public void TrySolve_QuadraticComplexSolutions_ReturnsBoth() {
        var result = fixture.Engine.TrySolve("x^2=-1");
        Assert.NotNull(result);
        var v = result.Variables.First(v => v.Variable == "x");
        Assert.Equal(2, v.Solutions.Length);
        // Solutions are complex (contain 'i')
        Assert.True(v.Solutions.All(s => s.Contains('i')));
    }

    [Fact]
    public void TrySolve_MultiVariableEquation_ReturnsParametricSolution() {
        var result = fixture.Engine.TrySolve("2*x+3*y=10");
        Assert.NotNull(result);
        // At least one variable solved in terms of the other
        Assert.True(result.Variables.Length >= 1);
        var xSol = result.Variables.FirstOrDefault(v => v.Variable == "x");
        Assert.NotNull(xSol);
        Assert.True(xSol.Solutions.Length > 0);
        // Solution for x contains y (parametric)
        Assert.True(xSol.Solutions.Any(s => s.Contains('y')));
    }

    [Theory]
    [InlineData("1+1=2")]    // no variables
    [InlineData("x=x")]      // trivial solution
    [InlineData("2x-=5")]    // syntax error
    [InlineData("abc")]      // no equals sign
    public void TrySolve_InvalidOrTrivial_ReturnsNull(string query) {
        var result = fixture.Engine.TrySolve(query);
        Assert.Null(result);
    }

    // ── CalculatorSearch integration tests ────────────────────────────────────

    private CalculatorSearch MakeCalcSearch() {
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        clipboard.Initialize(copy: _ => { }, read: () => Task.FromResult<string?>(null));
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        // math.js engine is null (not needed — equations bypass it entirely)
        var provider = new MathJsEngineProvider();
        var exchangeRateService = new ExchangeRateService(new HttpClient(), settings, NullLogger<ExchangeRateService>.Instance);
        return new CalculatorSearch(provider, exchangeRateService, clipboard, settings,
            NullLogger<CalculatorSearch>.Instance, fixture.Engine);
    }

    [Fact]
    public void CalculatorSearch_EquationQuery_ReturnsCalculatorResult() {
        var search = MakeCalcSearch();
        var results = search.Search("2x-5=2", 5);
        var item = Assert.Single(results);
        var calc = Assert.IsType<CalculatorResultItemViewModel>(item);
        Assert.Equal("x = 3.5", calc.Title);
        Assert.Equal("2x-5=2", calc.Subtitle);
    }

    [Fact]
    public void CalculatorSearch_EquationQuery_ActivateCopiesValue() {
        string? copied = null;
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        clipboard.Initialize(copy: text => copied = text, read: () => Task.FromResult<string?>(null));
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        var provider = new MathJsEngineProvider();
        var exchangeRateService = new ExchangeRateService(new HttpClient(), settings, NullLogger<ExchangeRateService>.Instance);
        var search = new CalculatorSearch(provider, exchangeRateService, clipboard, settings,
            NullLogger<CalculatorSearch>.Instance, fixture.Engine);

        var results = search.Search("2x-5=2", 5);
        var item = Assert.IsType<CalculatorResultItemViewModel>(Assert.Single(results));
        item.OnActivate?.Invoke();

        Assert.Equal("3.5", copied);
    }

    [Theory]
    [InlineData("1+1=2")]
    [InlineData("x=x")]
    public void CalculatorSearch_NoSolution_ReturnsEmpty(string query) {
        var search = MakeCalcSearch();
        Assert.Empty(search.Search(query, 5));
    }
}
```

- [ ] **Ejecutar los tests para confirmar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "EquationSolverTests" 2>&1 | tail -15
```

Esperado: errores de compilación (constructor de `CalculatorSearch` no acepta `NerdamerEngine` todavía) o fallos de runtime. Los tests deben fallar en este punto.

- [ ] **Commit de los tests (en rojo)**

```bash
git add Yottacast.Core.Tests/Search/NerdamerEngineFixture.cs Yottacast.Core.Tests/Search/Calculator/EquationSolverTests.cs
git commit -m "test: add failing EquationSolverTests for NerdamerEngine and CalculatorSearch"
```

---

## Task 5: Modificar CalculatorSearch — añadir NerdamerEngine y detección de ecuaciones

**Files:**
- Modify: `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`

- [ ] **Añadir `nerdamerEngine` al constructor y la detección de `=`**

Cambiar la declaración de la clase (primary constructor):

```csharp
public class CalculatorSearch(
    MathJsEngineProvider engineProvider,
    ExchangeRateService exchangeRateService,
    ClipboardService clipboard,
    UserSettings settings,
    ILogger<CalculatorSearch> logger,
    NerdamerEngine nerdamerEngine) : IInstantSearchSource, ISearchHintProvider {
```

Al inicio del método `Search()`, antes de la comprobación `var engine = engineProvider.Current`, añadir el bloque de detección de ecuaciones:

```csharp
public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _) {
    LastHint = null;
    LastHintKind = SearchHintKind.Info;
    if (!settings.EnableCalculator) return [];
    var q = query.Trim();

    // Equation detection: queries containing '=' are routed to NerdamerEngine.
    // math.js already rejects assignments, so these queries would return empty anyway.
    if (q.Contains('=')) {
        var solveResult = nerdamerEngine.TrySolve(q);
        if (solveResult != null) return BuildEquationResult(solveResult, q);
        return [];
    }

    var engine = engineProvider.Current;
    if (engine == null) return [];
    // ... resto del método sin cambios
```

Al final de la clase (antes del último `}`), añadir el método privado:

```csharp
    private IReadOnlyList<BaseResultItemViewModel> BuildEquationResult(SolveResult result, string originalQuery) {
        var first = result.Variables[0];
        var solutionsText = string.Join(", ", first.Solutions);
        var title = $"{first.Variable} = {solutionsText}";
        var captured = solutionsText;

        logger.LogDebug("Equation query=\"{Query}\" → {Title}", originalQuery, title);

        return [new CalculatorResultItemViewModel {
            Icon = "🧮",
            Title = title,
            Subtitle = originalQuery,
            Category = "Calculator",
            Score = 7,
            PasteAfterActivate = true,
            OnActivate = () => {
                logger.LogInformation("Equation: copied result \"{Value}\"", captured);
                clipboard.CopyText(captured);
            },
            OnCopy = () => {
                logger.LogInformation("Equation: copied result via Cmd+C \"{Value}\"", captured);
                clipboard.CopyText(captured);
            },
            CopiedMessage = "Result copied!",
        }];
    }
```

- [ ] **Actualizar `CreateSearch()` en `CalculatorSearchTests.cs` para pasar un `NerdamerEngine`**

En `Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs`, el método `CreateSearch()` debe construir un `NerdamerEngine` (no se awaita porque ningún test aritmético usa `=`):

```csharp
private (CalculatorSearch Search, Func<string?> GetLastCopied) CreateSearch() {
    var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
    string? lastCopied = null;
    clipboard.Initialize(copy: text => lastCopied = text, read: () => Task.FromResult<string?>(null));
    var settings = UserSettings.Load(new FakePlatformProvider([]));
    var provider = MathJsEngineProvider.ForTesting(fixture.Engine);
    var exchangeRateService = new ExchangeRateService(new HttpClient(), settings, NullLogger<ExchangeRateService>.Instance);
    var nerdamer = new NerdamerEngine(); // not awaited — arithmetic queries never hit '=' so TrySolve is never called
    var search = new CalculatorSearch(provider, exchangeRateService, clipboard, settings, NullLogger<CalculatorSearch>.Instance, nerdamer);
    return (search, () => lastCopied);
}
```

- [ ] **Verificar compilación**

```bash
cd Yottacast.Core && dotnet build 2>&1 | tail -5
cd Yottacast.Core.Tests && dotnet build 2>&1 | tail -5
```

Esperado: `Build succeeded` en ambos.

- [ ] **Ejecutar los tests de ecuaciones**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "EquationSolverTests" 2>&1 | tail -15
```

Esperado: todos pasan. Si algún test de `TrySolve` falla por diferencias en el formato de nerdamer (ej. `"7/2"` en vez de `"3.5"`), ajustar la lógica de `evaluate()` en `nerdamer-helpers.js` según el output real que devuelva nerdamer.

- [ ] **Ejecutar todos los tests para asegurar que los existentes no rompieron**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
```

Esperado: `Passed! - Failed: 0, Passed: ~1185, Skipped: 1`

- [ ] **Commit**

```bash
git add Yottacast.Core/Search/Calculator/CalculatorSearch.cs Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs
git commit -m "feat: route equation queries (containing '=') to NerdamerEngine in CalculatorSearch"
```

---

## Task 6: Registrar NerdamerEngine en el contenedor DI

**Files:**
- Modify: `Yottacast/App.axaml.cs`

- [ ] **Añadir el registro singleton de `NerdamerEngine`**

En `App.axaml.cs`, buscar la línea `services.AddSingleton<MathJsEngineProvider>();` (aprox. línea 240) y añadir justo después:

```csharp
services.AddSingleton<MathJsEngineProvider>();
services.AddSingleton<NerdamerEngine>();
services.AddSingleton<CalculatorSearch>();
```

- [ ] **Verificar compilación de la app GUI**

```bash
cd Yottacast && dotnet build 2>&1 | tail -5
```

Esperado: `Build succeeded`.

- [ ] **Commit**

```bash
git add Yottacast/App.axaml.cs
git commit -m "feat: register NerdamerEngine as singleton in DI container"
```

---

## Task 7: Verificación final

- [ ] **Ejecutar suite completa de tests**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
```

Esperado: `Passed! - Failed: 0, Passed: ≥1185, Skipped: 1`

- [ ] **Build completo de la app**

```bash
cd Yottacast && dotnet build 2>&1 | tail -5
```

Esperado: `Build succeeded`.

---

## Notas de implementación

### Si nerdamer devuelve fracciones en vez de decimales
Si `TrySolve("2x-5=2")` devuelve `"7/2"` en vez de `"3.5"`, el problema está en que `nerdamer(solText).evaluate().text()` devuelve `"7/2"` en algunos builds. Solución en `nerdamer-helpers.js`:

```javascript
// Añadir antes de asignar solText:
if (/^-?\d+\/\d+$/.test(solText)) {
    var parts = solText.split('/');
    var decimal = parseFloat(parts[0]) / parseFloat(parts[1]);
    solText = String(decimal);
}
```

### Si `.variables()` no existe en nerdamer core
Alternativa para extraer variables de la expresión:

```javascript
// Reemplazar allVars = nerdamer(...).variables() por:
var allVars = [];
var varPattern = /\b([a-zA-Z][a-zA-Z0-9]*)\b/g;
var match;
var combined = lhs + rhs;
while ((match = varPattern.exec(combined)) !== null) {
    var token = match[1];
    // Excluir funciones conocidas de nerdamer
    if (!['sin','cos','tan','sqrt','log','exp','abs','pi','e','i'].includes(token)) {
        if (!allVars.includes(token)) allVars.push(token);
    }
}
```

### Si el build falla por el EmbeddedResource con nombre de fichero con puntos
Los nombres de recursos embebidos en .NET preservan los puntos del nombre de fichero. `nerdamer.core.min.js` → resource name `Yottacast.Core.Search.Calculator.nerdamer.core.min.js`. Si `GetManifestResourceStream` devuelve null, verificar el nombre exacto con:
```csharp
var names = typeof(NerdamerEngine).Assembly.GetManifestResourceNames();
// Log names to see the exact format
```