# Spec: Equation Solver con nerdamer

**Fecha:** 2026-05-09  
**Estado:** aprobado

---

## Propósito

Añadir resolución simbólica de ecuaciones al launcher. El usuario escribe `2x-5=2` y obtiene `x = 3.5`; al activar, copia el valor `3.5` al portapapeles. Soporta ecuaciones polinómicas de cualquier grado, sistemas multi-variable y soluciones complejas.

---

## Comportamiento esperado

| Query del usuario          | Título mostrado        | Valor copiado (Enter) |
|----------------------------|------------------------|-----------------------|
| `2x-5=2`                   | `x = 3.5`              | `3.5`                 |
| `x^2-5x+6=0`               | `x = 2, 3`             | `2, 3`                |
| `x^2=-1`                   | `x = i, -i`            | `i, -i`               |
| `2x+3y=10`                 | `x = (10-3*y)/2`       | `(10-3*y)/2`          |
| `1+1=2` (sin variables)    | sin resultado          | —                     |
| `x=x` (trivial)            | sin resultado          | —                     |
| `2x-=5` (sintaxis inválida)| sin resultado          | —                     |

**Invariantes:**
- El resultado solo aparece si hay al menos una variable y la solución no es trivialmente idéntica a la variable.
- Soluciones complejas (con `i`) se muestran sin filtrar — son matemáticamente correctas.
- Si el engine aún no está listo en startup, devuelve vacío silenciosamente.
- Errores de nerdamer se capturan silenciosamente (sin hint, sin ruido).
- El VM reutiliza `CalculatorResultItemViewModel` con `PasteAfterActivate = true`.

**Prioridad y detección:**
- Si la query contiene `=`, nerdamer se intenta **primero** y math.js no se llama (math.js rechaza asignaciones de todas formas).
- Si nerdamer devuelve `null`, se devuelve vacío (math.js tampoco daría resultado).

---

## Arquitectura

### Build: descarga de assets JS

`Yottacast.Core.csproj` añade dos targets nuevos (similares a `DownloadMathJs`):

```xml
<Target Name="DownloadNerdamerCore" BeforeTargets="BeforeBuild">
  <!-- descarga nerdamer.core.min.js de jsDelivr si no existe -->
</Target>
<Target Name="DownloadNerdamerAlgebra" BeforeTargets="BeforeBuild">
  <!-- descarga Algebra.min.js (addon solve) de jsDelivr si no existe -->
</Target>
```

Ambos se embeben como `EmbeddedResource` con `Condition="Exists(...)"`, igual que `math.min.js`.

**Versión fijada:** nerdamer 1.1.13 (última estable, compatible con ES5/ES6 — requerido por Jint 3.x).

### `nerdamer-helpers.js` (archivo nuevo)

Wrapper JS fino cargado en el engine de nerdamer. Expone una función global:

```js
function solveEquation(query) // → JSON string | null
```

Internamente:
1. Verifica que la query contiene `=`.
2. Usa `nerdamer.getVars(lhs + '+' + rhs)` para extraer variables.
3. Si no hay variables → devuelve `null`.
4. Para cada variable, llama `nerdamer(lhs + '=' + rhs).solveFor(variable)`.
5. Evalúa numéricamente si la solución es una expresión sin variables libres; si tiene variables libres (caso multi-variable), la devuelve como string simbólico.
6. Filtra soluciones idénticas a la variable misma.
7. Devuelve JSON: `[{"variable":"x","solutions":["3.5"]}, ...]` o `null` en error.

Todo en try/catch: cualquier excepción de nerdamer devuelve `null`.

### `NerdamerEngine` (clase nueva)

```
Yottacast.Core/Search/Calculator/NerdamerEngine.cs
```

Responsabilidades:
- Carga `nerdamer.core.min.js` y `Algebra.min.js` y `nerdamer-helpers.js` en un engine Jint propio.
- Inicializa en background (`Task.Run(Initialize)`) igual que `MathJsEngine`.
- Expone `SolveResult? TrySolve(string query)`:
  - Si engine no listo → `null`.
  - Llama `solveEquation(query)` en JS.
  - Deserializa el JSON de respuesta a `SolveResult`.
  - Thread-safe: lock en cada llamada.

```csharp
public record VariableSolution(string Variable, string[] Solutions);
public record SolveResult(VariableSolution[] Variables);
```

### Modificaciones a `CalculatorSearch`

- Recibe `NerdamerEngine` por constructor (DI).
- En `Search()`, antes de llamar a math.js:

```csharp
if (q.Contains('=')) {
    var solveResult = _nerdamerEngine.TrySolve(q);
    if (solveResult != null) return BuildEquationResult(solveResult, q);
    return [];
}
```

- `BuildEquationResult()` construye un `CalculatorResultItemViewModel`:
  - `Title` = `"x = 3.5"` (o `"x = 2, 3"` para múltiples soluciones; o primera variable si hay varias)
  - `Subtitle` = query original
  - `OnActivate` / `OnCopy` copia el valor (parte derecha del título)
  - `PasteAfterActivate = true`
  - `Score = 7` (igual que resultados de calculadora)
  - `Icon = "🧮"`

**Para múltiples variables** (sistema): se muestra solo la primera variable resuelta en el título. El valor copiado es la solución de esa primera variable.

### DI

`NerdamerEngine` se registra como singleton en el contenedor DI de la app, igual que `MathJsEngineProvider`.

---

## Tests

Clase nueva `EquationSolverTests.cs` en `Yottacast.Core.Tests/Search/Calculator/`:

| Test                              | Query            | Resultado esperado          |
|-----------------------------------|------------------|-----------------------------|
| Ecuación lineal simple            | `2x-5=2`         | `x = 3.5`                   |
| Cuadrática dos soluciones reales  | `x^2-5x+6=0`     | `x = 2, 3`                  |
| Sin solución real (compleja)      | `x^2=-1`         | `x = i, -i`                 |
| Sistema multi-variable            | `2x+3y=10`       | solución paramétrica de `x` |
| Sin variables                     | `1+1=2`          | null                        |
| Trivial                           | `x=x`            | null                        |
| Sintaxis inválida                 | `2x-=5`          | null                        |

`NerdamerEngine` usa una fixture compartida (mismo patrón que `MathJsEngineFixture`) para no recrear el engine en cada test.

---

## Archivos afectados

| Archivo                                                   | Cambio            |
|-----------------------------------------------------------|-------------------|
| `Yottacast.Core/Yottacast.Core.csproj`                    | 2 targets descarga + 3 EmbeddedResource |
| `Yottacast.Core/Search/Calculator/NerdamerEngine.cs`      | nuevo             |
| `Yottacast.Core/Search/Calculator/nerdamer-helpers.js`    | nuevo             |
| `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`    | detección `=` + `BuildEquationResult` |
| `Yottacast/App.axaml.cs` (o donde se registra DI)         | registro singleton `NerdamerEngine` |
| `Yottacast.Core.Tests/Search/Calculator/EquationSolverTests.cs` | nuevo       |

> **Verificar en:** `NerdamerEngine.TrySolve()`, `CalculatorSearch.Search()`, `nerdamer-helpers.js:solveEquation()`