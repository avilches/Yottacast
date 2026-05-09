# Plan de scoring y ranking — Yottacast

Análisis del código actual (mayo 2026) y propuestas ordenadas por área. Todo lo relativo a puntuación de resultados, boost por uso, y calidad del ranking vive aquí.

---

## Mapa de scores actuales

Referencia rápida. El score final de cada item es `score_base + LaunchHistory.BonusFor(item)` (bonus solo para apps y archivos, máx +1.0).

| Source | Score base | Tipo |
|---|---|---|
| LocalPathSearch | 10.0 (fijo) | Instant |
| UrlSearch | 10.0 (fijo) | Instant |
| Calculator / Converter | 7 (fijo) | Instant |
| EmojiSearch (grid result) | 5.5 (fijo) | Instant |
| ApplicationSearch | NameMatcher × 4 → [0–4.0] (+bonus uso) | Instant |
| SystemSettingsSearch | NameMatcher × 4 → [0–4.0] | Instant |
| WebSearch PrefixOnly | 3.8 (fijo) | Instant |
| Dictionary PrefixOnly | 3.7 (fijo) | Deferred |
| UserDocumentSearch | FileScore × 3.5 → [1.75–3.5] (+bonus uso) | Deferred |
| WebSearch ShowAlways | 0.4 (fijo) | Instant |
| Dictionary ShowAlways | 0.3 (fijo) | Deferred |

---

## 1. Normalización de escalas entre categorías — COMPLETADO

~~**Problema.** Las escalas eran heterogéneas e incompatibles. WebSearch ShowAlways (3.0) siempre superaba a cualquier app (máx 1.0).~~

**Solución implementada.** Cada source escala sus scores a una banda global coherente:

| Banda | Rango | Sources |
|---|---|---|
| Intención explícita | 10 | LocalPath, URL |
| Sistema / calculadora | 7 | Calculator, Converter |
| Emoji | 5.5 | EmojiSearch grid |
| App / Settings prefijo exacto | 4.0 | NameMatcher 1.0 × 4 |
| Web/Dict prefijo activo | 3.7–3.8 | WebSearch PrefixOnly, Dictionary PrefixOnly |
| App prefijo parcial | 3.2 | NameMatcher 0.8 × 4 |
| Documento exacto | 3.5 | FileScore 1.0 × 3.5 |
| App iniciales/abrev/substring | 0.8–2.4 | NameMatcher 0.2–0.6 × 4 |
| Documento base | 1.75–2.625 | FileScore 0.5–0.75 × 3.5 |
| Web/Dict ShowAlways (fallback) | 0.3–0.4 | WebSearch ShowAlways, Dictionary ShowAlways |

Multiplicadores: Apps × 4, Docs × 3.5. Constantes fijas actualizadas en cada source.

**Tests actualizados:** `ApplicationSearchTests`, `CalculatorSearchTests`, `UnitConverterSearchTests`, `UserDocumentSearchTests`, `EmojiSearchTests`, `LocalPathSearchTests`, `UrlSearchTests`, `SystemSettingsSearchTests`.

---

## 2. NameMatcher: factor de cobertura

**Problema.** En `NameMatcher.ScoreWith` (`NameMatcher.cs:27`), el score de CamelHump es siempre 1.0 si el match empieza en token 0, y 0.8 si empieza en token > 0, sin importar cuánto cubre la query. Una query "S" que hace prefijo de "Safari" vale 1.0 igual que una query "Safari" completa. Esto produce empates entre queries cortas y largas, y hace que el usuario que escribe más no sea recompensado con un mejor ranking.

```csharp
// NameMatcher.cs:27 — score fijo independiente de cobertura
if (match) return start == 0 ? 1.0 : 0.8;
```

**Solución propuesta.** Incorporar un factor de cobertura: `coverage = totalCharsCoveredByQuery / name.Length`. Resultado: `baseScore * (0.5 + 0.5 * coverage)`. Así "Safari" (cobertura 1.0) vale 1.0, "Saf" (cobertura 0.5) vale ~0.75, "S" (cobertura ~0.16) vale ~0.58.

```csharp
if (match) {
    var covered = queryHumps.Select((h, k) => queryHumps[k].Length).Sum();
    var coverage = (double)covered / name.Length;
    var baseScore = start == 0 ? 1.0 : 0.8;
    return baseScore * (0.5 + 0.5 * coverage);
}
```

**Impacto.** Queries más específicas suben en el ranking. "Safari" supera a "S" al buscar Safari. Reduce empates arbitrarios. Aplica también a `SystemSettingsSearch` que usa el mismo `NameMatcher`.

**Complejidad.** Baja. Cambio localizado en `NameMatcher.ScoreWith`. Requiere actualizar `NameMatcherTests` y `ApplicationSearchTests` donde se fijan los valores exactos.

---

## 3. Score de substring interno demasiado alto

**Problema.** Una query "pdf" con score 0.2 puede aparecer antes que un documento con score 0.5 si no se ha normalizado la escala (ver §1). Más importante: "pdf" como substring de "OpenBSD PDF Viewer" tiene score 0.2 (`NameMatcher.cs:44`), pero el usuario buscando "pdf" probablemente quiere un visor, no cualquier cosa que contenga "pdf". El threshold de 3 chars es razonable pero el score 0.2 es demasiado alto comparado con los documentos.

**Solución propuesta.** Reducir el score de substring interno de 0.2 a 0.1 en `NameMatcher.cs`. Con la normalización de escalas (§1), este valor quedará por debajo de cualquier documento relevante.

**Ficheros a tocar**: `NameMatcher.cs`, tests asociados.

**Complejidad.** Baja.

---

## 4. Boost por frecuencia y recencia de uso (LaunchHistory) — COMPLETADO

~~**Problema.** El scoring era puramente textual.~~

**Solución implementada.** `LaunchHistory` registra cada lanzamiento y aplica un bonus con decay exponencial. El registro ocurre al activar cualquier item con `ItemPath` no nulo (apps y archivos) en los dos puntos de activación de `MainWindow.axaml.cs`.

**Fórmula:**
```
bonus = min(ln(count + 1) × e^(-ageDays / 30), 1.0)
```
Cap de 1.0 (`AppDefaults.LaunchHistoryMaxBonus`) para que ninguna app supere Calculator (7) ni Emoji (5.5).

**Ficheros creados/modificados:**
- `Yottacast.Core/Services/LaunchHistory.cs` — nuevo. JSON atómico, clock inyectable para tests.
- `Yottacast.Core.Tests/Services/LaunchHistoryTests.cs` — 10 tests (bonus, decay, persistencia, corrupto).
- `ResultItemViewModel.cs` — `string? ItemPath { get; init; }`.
- `ApplicationSearch.cs` + `UserDocumentSearch.cs` — propagan `ItemPath`.
- `MainWindowViewModel.cs` — `RecordLaunch()` + bonus en `RefreshResults()`.
- `MainWindow.axaml.cs` — `vm.RecordLaunch(result)` en Key.Return y OnResultsTapped.
- `App.axaml.cs` — singleton `LaunchHistory` en DI.
- `AppPaths.LaunchHistoryFile`, `AppDefaults.LaunchHistoryHalfLifeDays/MaxBonus`.

---

## 5. Score por relevancia del directorio en documentos

**Problema.** Un fichero `report.pdf` en `~/Desktop` tiene el mismo score que uno en `~/Library/Containers/com.something.app/report.pdf`. Los documentos en ubicaciones primarias (Desktop, Documents, Downloads) deberían puntuar más alto por ser más relevantes para el usuario.

El score en `UserDocumentSearch.SearchAsync` solo mira el nombre del fichero, nunca la ruta.

**Solución propuesta.** Aplicar un multiplicador según el directorio padre:

```csharp
var pathScore = GetPathRelevanceScore(r.Path, settings.ExpandedSearchFolders);
score *= pathScore;

// pathScore: 1.0 para Desktop/Downloads/Documents, 0.8 para raíz de carpeta configurada,
// 0.6 para subdirectorios de primer nivel, 0.4 para rutas más profundas
```

**Ficheros a tocar**: `UserDocumentSearch.SearchAsync`.

**Complejidad.** Baja.

---

## 6. Extraer FileNameMatcher de UserDocumentSearch

**Problema.** `UserDocumentSearch.cs` (bloque dentro de `SearchAsync`) implementa inline una heurística de scoring de nombres de archivo (multi-token, prefijo, sufijo, exacto). Esta lógica no está testeada de forma unitaria (solo de forma integrada en `UserDocumentSearchTests`), y es difícil de comparar con el scoring de `NameMatcher`.

**Solución propuesta.** Extraer la función de scoring a un método estático `FileNameMatcher.Score(string name, string query)`, análogo a `NameMatcher.Score`. Esto permite tests unitarios directos sin construir un `UserDocumentSearch` completo y hace que los dos sistemas de scoring (apps y ficheros) sean comparables y evolucionen de forma coherente.

**Ficheros a tocar**: nuevo `Yottacast.Core/Search/FileNameMatcher.cs`, `UserDocumentSearch.cs`.

**Complejidad.** Baja. Refactor puro.

---

## 7. Score visible en producción — COMPLETADO

~~**Problema.** `MainWindow.axaml` mostraba el score siempre visible.~~

**Solución implementada.** El score se muestra solo en modo debug: cuando el usuario mantiene Alt pulsado, la columna de categoría se reemplaza por el score numérico (`MainWindow.axaml:371`, `DictionaryResultItemView.axaml:115`, controlado por `IsAltPressed`). En uso normal no es visible. Mejor que eliminarlo: facilita la inspección del ranking durante el desarrollo sin contaminar la UX.

---

## 8. Spotlight metadata como alternativa al LaunchHistory

**Problema.** `MacOsPlatformProvider.ScanAppsAsync` no recupera `kMDItemLastUsedDate` (última vez que el usuario abrió la app). Estos metadatos permitirían implementar el boost por frecuencia (§4) sin necesidad de persistencia propia.

**Solución propuesta.** Ampliar `SpotlightInterop` para recuperar atributos adicionales junto con `kMDItemPath`. `kMDItemLastUsedDate` es un `CFDate` que requiere conversión adicional desde el runtime de CoreFoundation.

**Alternativa práctica.** Usar `MDItemCopyAttribute` con `kMDItemLastUsedDate` solo para apps, y mapear esa fecha a un score bonus. Si la app se usó en los últimos 7 días: +0.05; en los últimos 30 días: +0.02.

**Evaluación.** La serialización de `CFDate` desde P/Invoke es delicada y no es multiplataforma. Preferir la solución de persistencia propia (§4) por robustez y compatibilidad con Windows/Linux. Esta opción solo vale si se quiere evitar el fichero de persistencia en macOS.

**Complejidad.** Alta. Diferir hasta tener §4 implementado.

---

## 9. Tests de calidad de ranking

### 9.1 Tests de ranking end-to-end (ausentes)

Los tests actuales verifican scores individuales y comportamiento de la pipeline, pero no verifican que para una query concreta el resultado número 1 sea el correcto.

```csharp
[Theory]
// El match exacto de nombre siempre supera al prefijo parcial
[InlineData("safari", new[]{"Safari", "Safari Extensions"}, "Safari")]
// El prefijo de token 0 supera al prefijo de token > 0
[InlineData("Mon", new[]{"Activity Monitor", "Monster Hunter"}, "Monster Hunter")]
// Queries más largas superan a queries más cortas (tras fix §2)
[InlineData("Safari", new[]{"Safari", "Safarish App"}, "Safari")]
public void Ranking_Query_ReturnsExpectedFirstResult(
    string query, string[] apps, string expectedFirst)
```

### 9.2 Tests de normalización de scores entre categorías

```csharp
[Fact]
public void AppScore_AlwaysBeatsWebShowAlways_ForExactMatch() {
    // Un app con prefijo exacto (score ≥ 4 tras normalización) siempre supera
    // a WebSearch ShowAlways (score ≤ 0.5)
    Assert.True(appExactScore > webShowAlwaysScore);
}
```

**Complejidad.** Baja. Son tests de alto valor y bajo coste, añadir en paralelo con cada cambio de scoring.

---

## Resumen priorizado

| # | Propuesta | Impacto | Complejidad | Estado |
|---|---|---|---|---|
| 7 | Score visible en producción | — | — | **COMPLETADO** |
| 1 | Normalización de escalas | Alto | Media | **COMPLETADO** |
| 4 | Boost por frecuencia/recencia (LaunchHistory) | Alto | Media | **COMPLETADO** |
| 2 | Factor de cobertura en NameMatcher | Alto | Baja | Pendiente |
| 3 | Reducir score de substring (0.2 → 0.1) | Medio | Baja | Pendiente |
| 6 | Extraer FileNameMatcher | Bajo (calidad) | Baja | Pendiente |
| 5 | Score por relevancia de directorio | Medio | Baja | Pendiente |
| 9 | Tests de calidad de ranking | Alto (seguridad) | Baja | En paralelo con cada cambio |
| 8 | Spotlight kMDItemLastUsedDate | Medio | Alta | Diferir |