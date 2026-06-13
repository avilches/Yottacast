# Plan 2: Velocidad

Objetivo: reducir el trabajo por keystroke y la latencia de actualizacion de la UI. Estimacion de impacto conjunto: P50 de actualizacion de ~80-100 ms a ~50-60 ms en escenarios densos (200+ apps, modo emoji, ficheros).

Estado: las optimizaciones faciles estan hechas (T1, T2, T3, T5). Queda **T4** (esfuerzo medio) y **T7** (limpiezas, mayormente "no tocar sin medir"). T6 se descarto.

## Preparacion (para retomar T4)

1. Leer `docs/search-sources.md`, `docs/search-scoring.md`, `docs/app-design.md`, `docs/search-file-icons.md`, `docs/search-emoji.md`.
2. Antes de optimizar, medir base: anadir temporalmente un Stopwatch en `MainWindowViewModel.RefreshResults` y loguear a Debug la duracion. Repetir la medicion tras cada tarea para confirmar mejora real. Quitar la instrumentacion al final (o dejarla bajo nivel Trace si resulta util).
3. Regla: ningun cambio de comportamiento observable. Los tests existentes deben pasar sin modificarse (salvo que un test fije un detalle interno que cambie, en cuyo caso ajustar con cuidado).

## Pendiente

### T4. Top-N incremental en UserDocumentSearch (impacto medio, esfuerzo medio)

- **Donde**: `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs:249-256`.
- **Problema**: cada snapshot (200 ms) ejecuta `buffer.OrderByDescending().Take(limit).ToList()` sobre el buffer completo, que crece con cada resultado de mdfind: O(n log n) repetido.
- **Cambio**: mantener un top-N incremental (PriorityQueue<TElement, TPriority> de tamano limit, o insercion ordenada acotada) y emitir el snapshot desde ahi.
- **Verificar**: los tests de UserDocumentSearch pasan; los resultados progresivos llegan en el mismo orden final que antes.

### T7. Limpiezas menores (impacto bajo)

- `Yottacast/Converters/PathToAppIconConverter.cs:15`: valorar limite LRU si se observa presion de GC (medir antes; si no hay evidencia, no tocar).
- `Yottacast.Core/Services/AppIconCache.cs:32-35`: el doble ContainsKey se resolvio en el Plan 1 (dedup con `_loading`); no duplicar trabajo aqui.

## Hecho

- **T1. Tooltips de score lazy** — `ScoreDisplayText`/`ScoreTooltipText` son getters perezosos en `BaseResultItemViewModel`; `RefreshResults` solo guarda los datos numericos (`FrequencyBonus`/`FrequencyCount`/`FrequencyAgeDays`). Ya no se formatean para todos los resultados del merge en cada keystroke. Doc `search-scoring.md` §10/§12.
- **T2. Dedup apps vs files** — logica centralizada en `GlobalSearch` (`AppResultPaths`, `RemoveFilesDuplicatingApps`, `DeduplicateFilesAgainstApps`), usada por la GUI (`RefreshResults`) y por el daemon IPC (`SearchGrpcService.SearchDeferred`, que antes NO deduplicaba). Bug de comportamiento corregido: la dedup era por NOMBRE (escondia `Safari.txt` por la app Safari); ahora es por RUTA (`ItemPath`), conservando documentos homonimos. Tests: `GlobalSearchDedupTests`, `SearchGrpcServiceTests`. Docs `search-scoring.md` §1, `search-sources.md` §4.
- **T3. Cachear tokens de NameMatcher** — clase `MatchableName` precomputa la tokenizacion una vez; `AppInfo.MatchName` (eager) y `SystemSettingsPanel.MatchName` (lazy) la cachean; las Search usan el overload `NameMatcher.Match(MatchableName, query)`.
- **T5. Caches de EmojiSearch** — `_charToEntry` y el grid por defecto (`_sortedDefault`) se derivan una vez al cargar; `FilterEmojis` reescrito (solo materializa matches + sort estable); `EmojiEntry` precomputa `MatchableName` para nombre/keywords/categoria (mata la re-tokenizacion por keystroke). Doc `search-emoji.md` §143/§147.
- **T6. Cache de hints del footer** — DESCARTADO: impacto muy bajo (2-5 acciones por keystroke) y riesgo de congelar el label dinamico "Open in <App>" de documentos (depende de `_appNameByExtension`, poblado async, y `OnBadgeIconLoaded` re-emite a proposito). Relacion riesgo/beneficio desfavorable.

## Verificacion (al retomar T4)

- `cd Yottacast.Core.Tests && dotnet test` verde.
- Humo manual: teclear rapido (mantener una letra pulsada) no produce lag visible ni resultados desordenados; los resultados de fichero llegan en el mismo orden.
- Si algun detalle documentado cambia (p. ej. el orden de los snapshots), actualizar `docs/search-sources.md` o `docs/search-scoring.md`.
