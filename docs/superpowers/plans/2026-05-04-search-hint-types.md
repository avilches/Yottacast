# Search Hint Types Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** SearchHint shows two visual styles (info=gris, error=rojo) y ocupa espacio fijo para evitar layout shift al aparecer/desaparecer.

**Architecture:** Se añade `SearchHintKind` enum al Core. `ISearchHintProvider` expone el kind junto al texto. `CalculatorSearch` clasifica: errores de unidades incompatibles → Error, hints de ambigüedad → Info. El ViewModel propaga el kind y expone `SearchHintIsError`/`SearchHintIsInfo`. En la UI, un `Grid` con `MinHeight` fijo siempre reserva espacio; dos TextBlocks superpuestos muestran el texto con el color correcto. Los temas añaden `hint.error` y `hint.info` con sus colores.

**Tech Stack:** .NET 9, Avalonia 11, CommunityToolkit.Mvvm, xUnit

---

## Mapa de archivos

- **Crear:** `Yottacast.Core/Search/SearchHintKind.cs` — enum Info/Error
- **Modificar:** `Yottacast.Core/Search/ISearchHintProvider.cs` — añadir `LastHintKind`
- **Modificar:** `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` — implementar `LastHintKind`
- **Modificar:** `Yottacast.Core/Search/GlobalSearch.cs` — devolver kind en `SearchInstant`
- **Modificar:** `Yottacast/ViewModels/MainWindowViewModel.cs` — `SearchHintIsError`, `SearchHintIsInfo`, actualizar call sites
- **Modificar:** `Yottacast/Views/MainWindow.axaml` — Grid con MinHeight + dos TextBlocks
- **Modificar:** `Yottacast/Themes/dark-default.json` — split `hint`
- **Modificar:** `Yottacast/Themes/dark-raycast.json` — split `hint`
- **Modificar:** `Yottacast/Themes/dark-macos.json` — split `hint`
- **Modificar:** `Yottacast/Themes/light-gray.json` — split `hint`
- **Modificar:** `Yottacast/Themes/light-blue.json` — split `hint`
- **Modificar:** `Yottacast/Services/ThemeService.cs` — mapear tokens nuevos
- **Test:** `Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs` — assertions de `LastHintKind`
- **Doc:** `docs/ui-main-window.md` — actualizar sección 13

---

### Task 1: Enum SearchHintKind + actualizar ISearchHintProvider

**Files:**
- Create: `Yottacast.Core/Search/SearchHintKind.cs`
- Modify: `Yottacast.Core/Search/ISearchHintProvider.cs`

- [ ] **Step 1: Crear el enum**

Crear `Yottacast.Core/Search/SearchHintKind.cs` con:

```csharp
namespace Yottacast.Core.Search;

public enum SearchHintKind { Info, Error }
```

- [ ] **Step 2: Añadir `LastHintKind` a la interfaz**

Reemplazar el contenido de `Yottacast.Core/Search/ISearchHintProvider.cs`:

```csharp
namespace Yottacast.Core.Search;

public interface ISearchHintProvider {
    string? LastHint { get; }
    SearchHintKind LastHintKind { get; }
}
```

- [ ] **Step 3: Compilar para verificar que no rompe nada**

```bash
cd "Yottacast.Core" && dotnet build
```

Esperado: error de compilación en `CalculatorSearch` — no implementa `LastHintKind`. Eso es correcto, lo arreglamos en la siguiente tarea.

- [ ] **Step 4: Commit**

```bash
git add Yottacast.Core/Search/SearchHintKind.cs Yottacast.Core/Search/ISearchHintProvider.cs
git commit -m "feat: añadir SearchHintKind enum y propiedad LastHintKind a ISearchHintProvider"
```

---

### Task 2: Implementar LastHintKind en CalculatorSearch (TDD)

**Files:**
- Modify: `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`
- Test: `Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs`

- [ ] **Step 1: Escribir los tests que fallarán**

Al final de la clase `CalculatorSearchTests`, añadir:

```csharp
// ── SearchHintKind ────────────────────────────────────────────────────────

[Fact]
public void IncompatibleUnitsConvert_HintKind_IsError() {
    var search = BuildSearch(out _);
    search.Search("1 kg to meter", 5);
    Assert.Equal(SearchHintKind.Error, search.LastHintKind);
}

[Fact]
public void IncompatibleUnitsOp_HintKind_IsError() {
    var search = BuildSearch(out _);
    search.Search("1 km + 2 L", 5);
    Assert.Equal(SearchHintKind.Error, search.LastHintKind);
}

[Fact]
public void AmbiguousUnit_HintKind_IsInfo() {
    var search = BuildSearch(out _);
    search.Search("1 gt + 1 gt", 5);
    Assert.Equal(SearchHintKind.Info, search.LastHintKind);
}

[Fact]
public void NoHint_HintKind_DefaultsToInfo() {
    var search = BuildSearch(out _);
    search.Search("2+2", 5);
    Assert.Equal(SearchHintKind.Info, search.LastHintKind);
}
```

- [ ] **Step 2: Ejecutar tests para verificar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "IncompatibleUnitsConvert_HintKind_IsError|IncompatibleUnitsOp_HintKind_IsError|AmbiguousUnit_HintKind_IsInfo|NoHint_HintKind_DefaultsToInfo" -v
```

Esperado: falla con error de compilación (falta implementar `LastHintKind`).

- [ ] **Step 3: Implementar `LastHintKind` en CalculatorSearch**

En `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`, añadir la propiedad `LastHintKind` justo después de `LastHint` (línea 15) y actualizar los tres lugares donde se asigna `LastHint`:

```csharp
// Tras la línea: public string? LastHint { get; private set; }
public SearchHintKind LastHintKind { get; private set; }
```

Y al inicio de `Search(string query, int _)`, donde se resetea `LastHint = null` (línea 22), añadir:

```csharp
LastHint = null;
LastHintKind = SearchHintKind.Info;  // <-- añadir esta línea
```

Localizar la línea donde se asignan los ambiguity hints en el bloque `ConversionResult` (≈línea 50):
```csharp
LastHint = BuildHints(r.AmbiguityHints) is { Length: > 0 } h ? h : null;
// Añadir la siguiente línea inmediatamente después:
LastHintKind = SearchHintKind.Info;
```

Localizar la línea donde se asignan los ambiguity hints en el bloque `CalcResult` (≈línea 104):
```csharp
LastHint = BuildHints(r.AmbiguityHints) is { Length: > 0 } ch ? ch : null;
// Añadir la siguiente línea inmediatamente después:
LastHintKind = SearchHintKind.Info;
```

Localizar el bloque del case `ErrorResult` (≈línea 129-132):
```csharp
case ErrorResult r when r.ErrorKind is CalcErrorKind.IncompatibleUnitsConvert or CalcErrorKind.IncompatibleUnitsOp:
    LastHint = BuildErrorHint(r);
    LastHintKind = SearchHintKind.Error;  // <-- añadir esta línea
    logger.LogDebug("Calculator query=\"{Query}\" → error {Kind}: {Hint}", q, r.ErrorKind, LastHint);
    break;
```

- [ ] **Step 4: Ejecutar los tests nuevos para verificar que pasan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "IncompatibleUnitsConvert_HintKind_IsError|IncompatibleUnitsOp_HintKind_IsError|AmbiguousUnit_HintKind_IsInfo|NoHint_HintKind_DefaultsToInfo" -v
```

Esperado: los 4 tests pasan.

- [ ] **Step 5: Ejecutar suite completa de la calculadora**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "FullyQualifiedName~Calculator" -v
```

Esperado: todos los tests pasan.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/Search/Calculator/CalculatorSearch.cs Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs
git commit -m "feat: CalculatorSearch implementa LastHintKind (Error para unidades incompatibles, Info para ambigüedad)"
```

---

### Task 3: Actualizar GlobalSearch.SearchInstant para devolver el kind

**Files:**
- Modify: `Yottacast.Core/Search/GlobalSearch.cs`

- [ ] **Step 1: Actualizar la firma y el cuerpo de `SearchInstant`**

En `GlobalSearch.cs`, reemplazar el método `SearchInstant` completo (líneas 28-40):

```csharp
public (IReadOnlyList<BaseResultItemViewModel> Items, string? Hint, SearchHintKind HintKind) SearchInstant(string query, int limit) {
    var allItems = _instantSources
        .SelectMany(s => s.Search(query, limit))
        .OrderByDescending(x => x.Score)
        .ToList();

    var bypass = allItems.Where(x => x.BypassLimit).ToList();
    var limited = allItems.Where(x => !x.BypassLimit).Take(limit).ToList();

    var items = bypass.Concat(limited).OrderByDescending(x => x.Score).ToList();
    var hintProvider = _instantSources.OfType<ISearchHintProvider>().FirstOrDefault(s => s.LastHint != null);
    var hint = hintProvider?.LastHint;
    var hintKind = hintProvider?.LastHintKind ?? SearchHintKind.Info;
    return (items, hint, hintKind);
}
```

- [ ] **Step 2: Compilar Core para verificar que compila con el breaking change**

```bash
cd Yottacast.Core && dotnet build
```

Esperado: compila. Los errores aparecerán en `Yottacast` (el ViewModel usa la tupla antigua).

- [ ] **Step 3: Commit del cambio de Core**

```bash
git add Yottacast.Core/Search/GlobalSearch.cs
git commit -m "feat: GlobalSearch.SearchInstant devuelve SearchHintKind junto al hint"
```

---

### Task 4: Actualizar MainWindowViewModel

**Files:**
- Modify: `Yottacast/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Añadir propiedades SearchHintIsError y SearchHintIsInfo**

En `MainWindowViewModel.cs`, después de la línea `[ObservableProperty] private string? _searchHint;` (línea 44), añadir:

```csharp
[ObservableProperty] private bool _searchHintIsError;
[ObservableProperty] private bool _searchHintIsInfo;
```

- [ ] **Step 2: Añadir helper privado SetSearchHint**

Justo antes del método `ShowHintAfterDelayAsync` (≈línea 261), añadir:

```csharp
private void SetSearchHint(string? text, SearchHintKind kind = SearchHintKind.Info) {
    SearchHint = text;
    SearchHintIsError = text != null && kind == SearchHintKind.Error;
    SearchHintIsInfo  = text != null && kind == SearchHintKind.Info;
}
```

- [ ] **Step 3: Actualizar `ShowHintAfterDelayAsync` para aceptar el kind**

Reemplazar la firma y el cuerpo del método `ShowHintAfterDelayAsync`:

```csharp
private async Task ShowHintAfterDelayAsync(string hint, SearchHintKind kind, CancellationToken ct) {
    try {
        await Task.Delay(AppDefaults.ErrorHintDelayMs, ct);
        SetSearchHint(hint, kind);
    } catch (OperationCanceledException) { }
}
```

- [ ] **Step 4: Actualizar todos los call sites de SearchInstant**

En cada lugar donde se desestructura el resultado de `globalSearch.SearchInstant(...)`, añadir el tercer elemento `hintKind` y usar `SetSearchHint` en vez de asignar `SearchHint` directamente.

Hay 4 call sites:

**RefreshSearch** (≈línea 89-92): reemplazar el bloque:
```csharp
var (items, hint, hintKind) = globalSearch.SearchInstant(SearchText, limit: SearchSourceLimit);
_instantSnapshot = items;
SetSearchHint(hint, hintKind);
RefreshResults();
```

**OnNewAppInstalled** (≈línea 119-122): reemplazar el bloque:
```csharp
var (items, hint, hintKind) = globalSearch.SearchInstant(SearchText, limit: SearchSourceLimit);
_instantSnapshot = items;
SetSearchHint(hint, hintKind);
RefreshResults();
```

**OnSearchTextChanged** (≈línea 154-157, segundo call site del bloque de texto no vacío):
```csharp
var (items, hint, hintKind) = globalSearch.SearchInstant(SearchText, limit: SearchSourceLimit);
_instantSnapshot = items;
SetSearchHint(hint, hintKind);
RefreshResults();
```

**SearchAsync** (≈línea 219-229, la fase instant + ShowHintAfterDelay):
```csharp
// Phase 1: instant sources (in-memory cache) — no delay
if (ct.IsCancellationRequested) return;
var (instantItems, hint, hintKind) = globalSearch.SearchInstant(query, limit: SearchSourceLimit);
_instantSnapshot = instantItems;
SetSearchHint(null);
RefreshResults();

// Error hints (e.g. incompatible units) are shown after a delay so they don't flash on every keystroke
if (hint != null)
    _ = ShowHintAfterDelayAsync(hint, hintKind, ct);
```

- [ ] **Step 5: Actualizar el clear de SearchHint (texto vacío)**

En el bloque donde el texto está vacío (≈línea 202-208):
```csharp
if (string.IsNullOrWhiteSpace(value)) {
    IsSearching = false;
    _instantSnapshot = [];
    _deferredSnapshot = [];
    SetSearchHint(null);
    ShowPendingApps();
    return;
}
```

- [ ] **Step 6: Actualizar ShowCopiedMessage y ClearCopiedMessageAsync**

```csharp
public void ShowCopiedMessage(string msg) {
    _copiedMsgCts?.Cancel();
    _copiedMsgCts = new CancellationTokenSource();
    SetSearchHint(msg, SearchHintKind.Info);
    _ = ClearCopiedMessageAsync(msg, _copiedMsgCts.Token);
}

private async Task ClearCopiedMessageAsync(string msg, CancellationToken ct) {
    try {
        await Task.Delay(1500, ct);
        if (SearchHint == msg) SetSearchHint(null);
    } catch (OperationCanceledException) { }
}
```

- [ ] **Step 7: Compilar para verificar que no hay errores**

```bash
cd Yottacast && dotnet build
```

Esperado: compila sin errores.

- [ ] **Step 8: Commit**

```bash
git add Yottacast/ViewModels/MainWindowViewModel.cs
git commit -m "feat: ViewModel propaga SearchHintKind y expone SearchHintIsError/IsInfo"
```

---

### Task 5: Actualizar MainWindow.axaml (no-layout-shift + dos colores)

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml`

- [ ] **Step 1: Reemplazar el TextBlock del hint por un Grid con dos TextBlocks**

Localizar el bloque del hint (líneas 182-204):
```xml
<!-- Error hint (shown below the input when calculator detects an actionable error) -->
<TextBlock Text="{Binding SearchHint}"
           ...
</TextBlock>
```

Reemplazarlo por:
```xml
<!-- Hint area - fixed height to avoid layout shift when hint appears/disappears -->
<Grid Margin="20,0,20,8" MinHeight="18">
    <!-- Info hint (gray) -->
    <TextBlock Text="{Binding SearchHint}"
               Foreground="{DynamicResource Theme.Search.Hint.Info}"
               FontSize="{DynamicResource Theme.Results.Category.Size}"
               Opacity="0"
               TextTrimming="CharacterEllipsis"
               IsVisible="{Binding SearchHintIsInfo}">
        <TextBlock.Styles>
            <Style Selector="TextBlock[IsVisible=true]">
                <Style.Animations>
                    <Animation Duration="0:0:0.4" FillMode="Forward">
                        <KeyFrame Cue="0%">
                            <Setter Property="Opacity" Value="0"/>
                        </KeyFrame>
                        <KeyFrame Cue="100%">
                            <Setter Property="Opacity" Value="1"/>
                        </KeyFrame>
                    </Animation>
                </Style.Animations>
            </Style>
        </TextBlock.Styles>
    </TextBlock>
    <!-- Error hint (red) -->
    <TextBlock Text="{Binding SearchHint}"
               Foreground="{DynamicResource Theme.Search.Hint.Error}"
               FontSize="{DynamicResource Theme.Results.Category.Size}"
               Opacity="0"
               TextTrimming="CharacterEllipsis"
               IsVisible="{Binding SearchHintIsError}">
        <TextBlock.Styles>
            <Style Selector="TextBlock[IsVisible=true]">
                <Style.Animations>
                    <Animation Duration="0:0:0.4" FillMode="Forward">
                        <KeyFrame Cue="0%">
                            <Setter Property="Opacity" Value="0"/>
                        </KeyFrame>
                        <KeyFrame Cue="100%">
                            <Setter Property="Opacity" Value="1"/>
                        </KeyFrame>
                    </Animation>
                </Style.Animations>
            </Style>
        </TextBlock.Styles>
    </TextBlock>
</Grid>
```

- [ ] **Step 2: Compilar para verificar AXAML**

```bash
cd Yottacast && dotnet build
```

Esperado: compila. Los recursos `Theme.Search.Hint.Info` y `Theme.Search.Hint.Error` aún no existen en los temas (no es error de compilación en Avalonia, son DynamicResource), pero los tokens faltan en runtime. Se añaden en la siguiente tarea.

- [ ] **Step 3: Commit**

```bash
git add Yottacast/Views/MainWindow.axaml
git commit -m "feat: hint area con altura fija y dos TextBlocks para info/error"
```

---

### Task 6: Actualizar temas JSON y ThemeService

**Files:**
- Modify: `Yottacast/Themes/dark-default.json`
- Modify: `Yottacast/Themes/dark-raycast.json`
- Modify: `Yottacast/Themes/dark-macos.json`
- Modify: `Yottacast/Themes/light-gray.json`
- Modify: `Yottacast/Themes/light-blue.json`
- Modify: `Yottacast/Services/ThemeService.cs`

- [ ] **Step 1: Actualizar dark-default.json**

Reemplazar la línea `"hint": { "color": "#FF3B30" }` por:
```json
"hint": {
      "error": { "color": "#FF3B30" },
      "info":  { "color": "#9A9AA4" }
    }
```

- [ ] **Step 2: Actualizar dark-raycast.json**

Mismo cambio — reemplazar `"hint": { "color": "#FF3B30" }`:
```json
"hint": {
      "error": { "color": "#FF3B30" },
      "info":  { "color": "#9A9AA4" }
    }
```

- [ ] **Step 3: Actualizar dark-macos.json**

Mismo cambio — reemplazar `"hint": { "color": "#FF3B30" }`:
```json
"hint": {
      "error": { "color": "#FF3B30" },
      "info":  { "color": "#9A9AA4" }
    }
```

- [ ] **Step 4: Actualizar light-gray.json**

Reemplazar `"hint": { "color": "#D32F2F" }`:
```json
"hint": {
      "error": { "color": "#D32F2F" },
      "info":  { "color": "#9CA3AF" }
    }
```

- [ ] **Step 5: Actualizar light-blue.json**

Reemplazar `"hint": { "color": "#D32F2F" }`:
```json
"hint": {
      "error": { "color": "#D32F2F" },
      "info":  { "color": "#9CA3AF" }
    }
```

- [ ] **Step 6: Actualizar ThemeService — mapping de tokens**

En `ThemeService.cs`, localizar la línea:
```csharp
SetBrush(app, "Theme.Search.Hint", search["hint"]?["color"]);
```

Reemplazarla por:
```csharp
var hint = search["hint"];
SetBrush(app, "Theme.Search.Hint.Error", hint?["error"]?["color"]);
SetBrush(app, "Theme.Search.Hint.Info",  hint?["info"]?["color"]);
```

- [ ] **Step 7: Actualizar ThemeService — valores por defecto**

Localizar la línea (en la sección de defaults, ≈línea 369):
```csharp
app.Resources["Theme.Search.Hint"] = B("#FF3B30");
```

Reemplazarla por:
```csharp
app.Resources["Theme.Search.Hint.Error"] = B("#FF3B30");
app.Resources["Theme.Search.Hint.Info"]  = B("#9A9AA4");
```

- [ ] **Step 8: Compilar y ejecutar para verificar visualmente**

```bash
cd Yottacast && dotnet build && dotnet run
```

Verificar:
1. Escribir `1 kg to meter` → aparece hint rojo "Can't convert kilogram to meter" sin cambiar el tamaño de la ventana
2. Escribir `2+2` y luego borrar → el espacio del hint se mantiene igual (sin salto)
3. Copiar un resultado (Cmd+C) → aparece hint gris "Copied ..." sin cambiar la ventana
4. Escribir `1 MG + 1 MG` → aparece hint gris "Maybe you meant..."

- [ ] **Step 9: Commit**

```bash
git add Yottacast/Themes/dark-default.json Yottacast/Themes/dark-raycast.json Yottacast/Themes/dark-macos.json Yottacast/Themes/light-gray.json Yottacast/Themes/light-blue.json Yottacast/Services/ThemeService.cs
git commit -m "feat: temas split hint en error/info, ThemeService mapea Theme.Search.Hint.Error y Theme.Search.Hint.Info"
```

---

### Task 7: Actualizar documentación

**Files:**
- Modify: `docs/ui-main-window.md`

- [ ] **Step 1: Actualizar sección 13 (Hint de búsqueda)**

Localizar la sección 13 en `docs/ui-main-window.md` y reemplazarla:

```markdown
## 13. Hint de búsqueda

El área de hint siempre reserva espacio fijo debajo del campo de búsqueda (no hay salto de layout al aparecer o desaparecer). El texto aparece con fade-in de 0.4 s.

Hay dos estilos visuales:

| Estilo | Color | Cuándo se usa |
|---|---|---|
| Error | Rojo (`Theme.Search.Hint.Error`) | Unidades incompatibles en la calculadora (`IncompatibleUnitsConvert`, `IncompatibleUnitsOp`) |
| Info | Gris (`Theme.Search.Hint.Info`) | Hints de ambigüedad de unidades ("Maybe you meant…") y mensajes de copia ("Copied …") |

El hint se limpia automáticamente en cada nueva búsqueda o cuando el texto se vacía.

> **Verificar en:** `MainWindowViewModel.cs` — `SetSearchHint`, `SearchHintIsError`, `SearchHintIsInfo`. `MainWindow.axaml` — `Grid` con `MinHeight` y dos TextBlocks. `GlobalSearch.cs` — `SearchInstant` devuelve `SearchHintKind`. `CalculatorSearch.cs` — `LastHintKind`. `ThemeService.cs` — `Theme.Search.Hint.Error`, `Theme.Search.Hint.Info`.
```

- [ ] **Step 2: Commit**

```bash
git add docs/ui-main-window.md
git commit -m "docs: actualizar sección 13 con hint tipos info/error y no-layout-shift"
```

---

## Self-Review

**Cobertura de la spec:**
- ✅ No layout shift → Grid con MinHeight en Task 5
- ✅ Dos estilos (info/error) → Tasks 1-4 (kind en Core) + Task 5 (AXAML) + Task 6 (temas)
- ✅ Error = rojo → `hint.error.color` conserva el rojo actual
- ✅ Info = gris → `hint.info.color` nuevo token gris en todos los temas
- ✅ ShowCopiedMessage usa Info → Task 4 Step 6
- ✅ Tests de clasificación → Task 2

**Placeholders:** ninguno — todos los steps tienen código real.

**Consistencia de tipos:** `SearchHintKind` usado en Task 1, implementado en Task 2, propagado en Task 3, consumido en Task 4. El helper `SetSearchHint(string?, SearchHintKind)` definido en Task 4 Step 2 y usado en Steps 4-6.