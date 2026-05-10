# Algebra Simplification — Design Spec

**Date:** 2026-05-10  
**Status:** Approved

---

## Objetivo

Cuando el usuario escribe una expresión algebraica con variables (ej. `x^2-5x+6`, `2x+3x`, `(x+1)^2`), mostrar un único ítem navegable con todas las formas útiles del resultado: simplificada, factorizada, expandida, derivada e integral. Solo se muestran las celdas cuyo resultado difiere de la expresión de entrada.

---

## Comportamiento

### Trigger / Routing

El routing en `CalculatorSearch.Search()` funciona así:

```
query contiene '='  →  NerdamerEngine.TrySolve  (sin cambios)
query sin '='       →  MathJsEngine.Evaluate
                         └─ UnknownSymbol  →  NerdamerEngine.TryAlgebra
                                               ├─ celdas  →  AlgebraResultItemViewModel
                                               └─ null    →  silencio (igual que hoy)
```

`TryAlgebra` falla rápido para texto sin estructura matemática (`safari to km`) porque nerdamer no puede extraer variables de él.

### Operaciones y filtrado

`getAlgebraResults(expr)` en `nerdamer-helpers.js` calcula en orden:

| Operación | Label | Condición |
|-----------|-------|-----------|
| Simplify | `"simplify"` | Siempre |
| Expand | `"expand"` | Siempre |
| Factor | `"factor"` | Siempre |
| Derivada respecto a cada variable | `"d/dx"`, `"d/dy"`, … (orden alfabético) | Siempre |
| Integral indefinida | `"∫dx"` | Solo si exactamente 1 variable |

Tras calcular, se aplican dos filtros:
1. **Descartar no-ops:** resultado === expresión de entrada normalizada.
2. **Deduplicar:** si dos operaciones producen el mismo resultado, conservar la primera.

Si tras filtrar no queda ninguna celda, `TryAlgebra` devuelve `null` y el resultado no aparece.

### Celda seleccionada y copia

- La primera celda es la seleccionada por defecto.
- El usuario navega con ←/→ entre celdas (circular).
- **Encima**: resultado (ej. `(x-2)*(x-3)`).
- **Debajo**: label de la celda seleccionada (ej. `"factor"`).
- Enter copia el resultado de la celda seleccionada al portapapeles y pega en la app anterior.

---

## Componentes

### 1. `nerdamer-helpers.js` — nueva función

```js
// Devuelve JSON [{label, result}, ...] o null
function getAlgebraResults(expr) { ... }
```

- Usa `nerdamer(expr).variables()` para detectar variables. Si lanza → `null`.
- Calcula cada operación con try/catch individual; las que lanzan se saltan.
- Normaliza el input con `nerdamer(expr).text()` para la comparación de no-ops.
- Las derivadas se calculan con `nerdamer.diff(expr, variable)`.
- La integral con `nerdamer.integrate(expr, variable)`.

### 2. `NerdamerEngine.cs` — nuevo método

```csharp
public AlgebraResult? TryAlgebra(string expr)
```

- Llama a `getAlgebraResults(expr)` vía Jint.
- Deserializa JSON → `AlgebraCell[]`.
- Devuelve `AlgebraResult` o `null`.
- Thread-safe (mismo `lock` que `TrySolve`).

**Nuevos records (mismo fichero):**

```csharp
public record AlgebraCell(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("result")] string Result);

public record AlgebraResult(AlgebraCell[] Cells);
```

### 3. `AlgebraCellItem.cs` — nuevo fichero en `Yottacast.Core/ViewModels/`

Análogo a `DateCellItem`. Propiedades: `Label`, `Result`, `IsSelected` (observable).

### 4. `AlgebraResultItemViewModel.cs` — nuevo fichero en `Yottacast.Core/ViewModels/`

Sigue el mismo patrón que `DateSearchResultViewModel`:

- `Cells: IReadOnlyList<AlgebraCell>` (init) → construye `CellItems`.
- `CellItems: IReadOnlyList<AlgebraCellItem>` (para binding en la vista).
- `SelectedCell: int` (observable, actualiza `IsSelected` en `CellItems`).
- `SelectedCellLabel: string` (label de la celda actual, análogo a `SelectedCellSubtitle`).
- `MoveCellLeft() / MoveCellRight()` — circulares, devuelven `false` si `Cells.Count <= 1`.
- `Icon = "🧮"`, `Category = "Calculator"`.

### 5. `CalculatorSearch.cs` — cambio de routing

En el `switch` sobre `engine.Evaluate(q)`, añadir tras los casos existentes:

```csharp
case ErrorResult r when r.ErrorKind == CalcErrorKind.UnknownSymbol: {
    var algebraResult = nerdamerEngine.TryAlgebra(q);
    if (algebraResult != null) return BuildAlgebraResult(algebraResult, q);
    break;
}
```

Nuevo método privado `BuildAlgebraResult` que construye `AlgebraResultItemViewModel` con:
- `OnLeft` / `OnRight` conectados a `MoveCellLeft/Right`.
- Acción Enter: copia `vm.CellItems[vm.SelectedCell].Result` + `PasteAfterClose = true`.
- Acción Cmd+C: copia sin cerrar.
- `Score = 7` (igual que calculo/conversiones).

### 6. `AlgebraResultItemView.axaml` — nuevo UserControl en `Yottacast/Views/Results/`

Prácticamente idéntico a `DateSearchResultView.axaml` con `x:DataType="AlgebraResultItemViewModel"`:

- Fila superior: `ItemsControl` con `UniformGrid Rows="1"` mostrando `Result` de cada `AlgebraCellItem`.
- Selección: `Classes.conv-cell-selected="{Binding IsSelected}"` en el `Border` de cada celda.
- Fila inferior: `TextBlock` con `SelectedCellLabel`.

### 7. `MainWindow.axaml` — cambios

- Añadir estilo `ListBoxItem.algebra-navigable` (idéntico a `conv-navigable`): suprime background de selección del ListBoxItem.
- Añadir `DataTemplate` para `AlgebraResultItemViewModel` → `AlgebraResultItemView`.
- Actualizar el selector `ListBoxItem:selected:not(.emoji-item):not(.conv-navigable)` para excluir también `.algebra-navigable`.

---

## Tests

### `AlgebraSearchTests.cs` — nuevo fichero en `Yottacast.Core.Tests/Search/Calculator/`

Tests de integración via `CalculatorSearch.Search()` (colección `"Nerdamer"`):

| Query | Expectativa |
|-------|-------------|
| `2x+3x` | celda `simplify: 5*x` |
| `x^2-5x+6` | celda `factor: (x-2)*(x-3)`, celda `d/dx: 2*x-5` |
| `(x+1)^2` | celda `expand: x^2+2*x+1` |
| `x^2` | celda `d/dx: 2*x`, celda `∫dx: x^3/3` |
| `x*y+2*x` | celda `factor: x*(y+2)`, celdas `d/dx`, `d/dy` — sin integral |
| `2+3` | vacío (math.js lo evalúa antes, no llega a TryAlgebra) |
| `safari` | vacío (nerdamer no extrae variables) |

Tests directos de `NerdamerEngine.TryAlgebra` (pueden ir en `EquationSolverTests.cs` o clase nueva):

- Expresiones con una variable: verifica celdas concretas.
- Expresiones con múltiples variables: verifica derivadas parciales, sin integral.
- Input sin variables (`2+3`): devuelve `null`.
- Input no matemático (`hello world`): devuelve `null`.
- Copia correcta: activar acción Enter copia `Result` de la celda seleccionada.

---

## Archivos afectados

| Fichero | Cambio |
|---------|--------|
| `Yottacast.Core/Search/Calculator/nerdamer-helpers.js` | Añadir `getAlgebraResults()` |
| `Yottacast.Core/Search/Calculator/NerdamerEngine.cs` | Añadir `TryAlgebra()`, records `AlgebraCell`, `AlgebraResult` |
| `Yottacast.Core/ViewModels/AlgebraCellItem.cs` | Nuevo |
| `Yottacast.Core/ViewModels/AlgebraResultItemViewModel.cs` | Nuevo |
| `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` | Routing UnknownSymbol + `BuildAlgebraResult` |
| `Yottacast/Views/Results/AlgebraResultItemView.axaml` | Nuevo |
| `Yottacast/Views/Results/AlgebraResultItemView.axaml.cs` | Nuevo (code-behind vacío) |
| `Yottacast/Views/MainWindow.axaml` | Estilo + DataTemplate |
| `Yottacast.Core.Tests/Search/Calculator/AlgebraSearchTests.cs` | Nuevo |
| `docs/search-calculator.md` | Actualizar sección 1.1 y añadir apartado álgebra simbólica |
