# Plan 2: Velocidad

Objetivo: reducir el trabajo por keystroke y la latencia de actualizacion de la UI. Estimacion de impacto conjunto: P50 de actualizacion de ~80-100 ms a ~50-60 ms en escenarios densos (200+ apps, modo emoji, ficheros).

Requisito: ejecutar despues del Plan 1 (varios ficheros se solapan; los fixes de estabilidad cambian lineas cercanas).

## Preparacion

1. Leer `docs/search-sources.md`, `docs/search-scoring.md`, `docs/app-design.md`, `docs/search-file-icons.md`, `docs/search-emoji.md`.
2. Antes de optimizar, medir base: anadir temporalmente un Stopwatch en `MainWindowViewModel.RefreshResults` y loguear a Debug la duracion. Repetir la medicion tras cada tarea para confirmar mejora real. Quitar la instrumentacion al final (o dejarla bajo nivel Trace si resulta util).
3. Regla: ningun cambio de comportamiento observable. Los tests existentes deben pasar sin modificarse (salvo que un test fije un detalle interno que cambie, en cuyo caso ajustar con cuidado).

## Tareas (orden por ratio impacto/esfuerzo)

### T1. Tooltips de score lazy (impacto alto, esfuerzo bajo) — DONE

> Hecho: `ScoreDisplayText`/`ScoreTooltipText` convertidos en getters computados perezosos en `BaseResultItemViewModel`; `RefreshResults` solo guarda los datos numericos (`FrequencyBonus`/`FrequencyCount`/`FrequencyAgeDays`). Doc `search-scoring.md` §10 y §12 actualizado. Tests verdes (Core 1387).


- **Donde**: `Yottacast/ViewModels/MainWindowViewModel.cs:635-714` (RefreshResults, lineas ~645-663).
- **Problema**: por cada keystroke se construyen `ScoreDisplayText` y `ScoreTooltipText` (interpolaciones de string + formato) para TODOS los resultados del merge, aunque solo se ven cuando el usuario pulsa Alt.
- **Cambio**: calcular el bonus numerico en el merge (se necesita para ordenar) pero diferir la construccion de los strings: solo cuando `IsAltPressed` es true, o convertir ScoreDisplayText/ScoreTooltipText en propiedades computadas perezosas que formatean al primer acceso.
- **Verificar**: con Alt pulsado los scores y tooltips se muestran identicos a antes.

### T2. Deduplicacion apps vs files en una sola pasada (impacto alto, esfuerzo medio)

- **Donde**: `MainWindowViewModel.cs:645-674`.
- **Problema**: tras el merge se itera la lista completa 3 veces (OfType+Where para extraer apps, construccion de HashSet, RemoveAll con Path.GetFileNameWithoutExtension por elemento).
- **Cambio**: mover la deduplicacion a `GlobalSearch` (en el punto de merge ya se conocen las apps) o, si se prefiere no tocar Core, hacer una sola pasada: primera iteracion construye el HashSet de nombres de app, segunda construye la lista final filtrando, sin RemoveAll.
- **Decision**: preguntar al usuario si prefiere la dedup en GlobalSearch (mas limpio, afecta tambien al daemon IPC) o en el ViewModel (mas local). Recomendada: GlobalSearch.
- **Verificar**: buscar el nombre de una app instalada que tambien exista como .app en las carpetas de busqueda; el fichero duplicado no debe aparecer.

### T3. Cachear tokens de NameMatcher en AppInfo (impacto medio, esfuerzo bajo) — DONE

> Hecho: nueva clase `MatchableName` (NameMatcher) que precomputa la tokenizacion una vez; `NameMatcher.Tokenize` + overload `Match(MatchableName, query)`. `AppInfo.MatchName` (eager en construccion) y `SystemSettingsPanel.MatchName` (lazy) cacheados; ambas Search usan el overload. Tests del area verdes (95). Nota: `EmojiSearch` aun re-tokeniza (el overload `Score(tokens,...)` es no-op) — se aborda en T5.


- **Donde**: `Yottacast.Core/Search/Application/NameMatcher.cs:19-30` y `ApplicationSearch.cs:161`.
- **Problema**: para cada app del cache (100-500) y cada keystroke se re-tokeniza el nombre (split CamelCase + List intermedia).
- **Cambio**: anadir a AppInfo (o a la entrada del cache de ApplicationSearch) los tokens precalculados en el momento del descubrimiento; NameMatcher.Match recibe los tokens ya hechos. Pre-splitear la query una sola vez por busqueda y pasarla a todos los matches.
- **Atencion**: SystemSettingsSearch tambien usa NameMatcher; aplicar el mismo patron a su catalogo.
- **Verificar**: `NameMatcherTests` (si existen) y busqueda de apps con CamelCase, espacios y guiones se comporta igual.

### T4. Top-N incremental en UserDocumentSearch (impacto medio, esfuerzo medio)

- **Donde**: `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs:249-256`.
- **Problema**: cada snapshot (200 ms) ejecuta `buffer.OrderByDescending().Take(limit).ToList()` sobre el buffer completo, que crece con cada resultado de mdfind: O(n log n) repetido.
- **Cambio**: mantener un top-N incremental (PriorityQueue<TElement, TPriority> de tamano limit, o insercion ordenada acotada) y emitir el snapshot desde ahi.
- **Verificar**: los tests de UserDocumentSearch pasan; los resultados progresivos llegan en el mismo orden final que antes.

### T5. Caches estaticos de EmojiSearch (impacto medio-bajo, esfuerzo bajo) — DONE

> Hecho: (a) `_charToEntry` y `_sortedDefault` derivados una vez al asignar `_entries` (no se reconstruyen en cada `:`); (b) `FilterEmojis` reescrito con bucle manual que solo materializa matches + un `OrderByDescending` estable (orden identico). Ademas se mato la re-tokenizacion por keystroke que dejo T3: `EmojiEntry` precomputa `MatchableName` para nombre, keywords y categoria, y `SingleTermScore` usa `NameMatcher.Match(MatchableName, term)`. Doc `search-emoji.md` §143/§147 actualizado. Tests verdes (109 emoji/namematcher).


- **Donde**: `Yottacast.Core/Search/Emoji/EmojiSearch.cs:47-86`.
- **Problema**: `GetDefaultEmojis()` reconstruye `charToEntry` (ToDictionary de ~2000 entradas) en cada invocacion con query vacia; `FilterEmojis` encadena Select/Where/OrderBy/Select/ToList con materializaciones intermedias.
- **Cambio**: (a) cachear `charToEntry` como campo construido en Start(); (b) reescribir FilterEmojis con bucle manual y un solo Sort sobre buffer reutilizable o preasignado.
- **Verificar**: tests de EmojiSearch; el orden de resultados no cambia.

### T6. Propiedades computadas del footer con cache (impacto bajo, esfuerzo bajo)

- **Donde**: `MainWindowViewModel.cs:74-120` (FooterHints, OptionsMenuItems, AvailableModes).
- **Problema**: se recomputan (cadenas LINQ) en cada cambio de SelectedResult, es decir por keystroke.
- **Cambio**: cachear en fields privados e invalidar solo cuando SelectedResult cambia a un item de tipo distinto o cambian los modos.
- **Verificar**: hints y menu de opciones siguen reflejando el item seleccionado al navegar con flechas.

### T7. Limpiezas menores (impacto bajo)

- `Yottacast/Converters/PathToAppIconConverter.cs:15`: valorar limite LRU si se observa presion de GC (medir antes; si no hay evidencia, no tocar).
- `Yottacast.Core/Services/AppIconCache.cs:32-35`: el doble ContainsKey se resuelve ya en el Plan 1 (dedup con `_loading`); no duplicar trabajo aqui.

## Verificacion final

- `cd Yottacast.Core.Tests && dotnet test` verde.
- Medicion antes/despues documentada en el commit o PR (numeros del Stopwatch de la preparacion, mismos escenarios: query de 3 letras con 200+ apps, `:` en modo emoji, query que dispara file search).
- Humo manual: teclear rapido (mantener una letra pulsada) no produce lag visible ni resultados desordenados.
- No se ha cambiado ningun contrato de docs/; si algun detalle documentado cambia (p.ej. donde se deduplica), actualizar `docs/search-sources.md` o `docs/search-scoring.md`.
