# Fuentes de búsqueda

## ApplicationSearch

Clase: `Yottacast.Core.Search.Application.ApplicationSearch` (implementa `IInstantSearchSource`)

Mantiene un `ConcurrentDictionary<string, AppInfo>` en memoria con las apps instaladas.
Inyecta `UserSettings` para leer `AppDirectories`.

**Arranque por plataforma** — toda la lógica OS-específica está en `PlatformProvider`:
- **macOS**: `ScanAppsAsync` consulta Spotlight vía `SpotlightInterop.Query()` (P/Invoke a CoreServices `MDQuery`, no subprocess); `CreateAppWatchers` monta watchers en `*.app`.
- **Windows**: `ScanAppsAsync` escanea `AppDirectories` buscando `.exe`; `CreateAppWatchers` en `*.exe`.
- **Linux**: `ScanAppsAsync` escanea `AppDirectories` buscando `.desktop`; `CreateAppWatchers` en `*.desktop`.

Evento `AppAdded` notifica cuando se detecta una app nueva (disponible para suscriptores externos; actualmente ningún componente lo consume).

**Cambio de AppDirectories en settings** ⚠️ TODO: `ReloadAppDirectories()` no está implementado. `SettingsWindowViewModel` gestiona `AppDirectories` con un `ObservableCollection` y un `CollectionChanged` que llama `Save()`, pero los cambios no recargan el caché de `ApplicationSearch`. Para implementarlo correctamente habría que hacer `Stop()` + `Start()` limpiando el caché.

**Gotcha: Lazy icon en AppInfo** — `AppInfo` usa `Lazy<T>` para diferir la lectura de `Info.plist` hasta el primer acceso al icono, evitando parsear cientos de plists al arranque.

**Guard de arranque idempotente** — `Start()` comprueba `_started` antes de lanzar el escaneo; llamadas repetidas son no-op. `Stop()` resetea `_started = false`, haciendo el ciclo `Stop()` + `Start()` válido para un reinicio limpio, aunque actualmente ningún código lo ejecuta. ⚠️ `_started` es un `bool` plano sin sincronización — llamadas concurrentes a `Start()` podrían crear una race condition.

**`ApplicationSearch` implementa `IDisposable`** — `Dispose()` llama `Stop().GetAwaiter().GetResult()` de forma síncrona, limpiando watchers y caché.

**`AppInfo.IconPath` y thread safety** — el `Lazy<string?>` interno usa `LazyThreadSafetyMode.ExecutionAndPublication`: solo un thread ejecuta el factory de icono; los demás bloquean hasta que completa.

**Watchers solo en directorios existentes** — `CreateAppWatchers` filtra las dirs con `Directory.Exists()` antes de montar el watcher; los directorios configurados pero inexistentes se ignoran silenciosamente.

**Métodos de consulta directa** — usados por `BrowserDiscovery` y `TerminalDiscovery` para consultar el caché sin pasar por la pipeline de búsqueda:

| Método | Comportamiento |
|---|---|
| `Find(string name)` | Búsqueda exacta en el caché por clave de nombre (case-insensitive); devuelve `AppInfo?` |
| `FindAll()` | Devuelve todas las apps en caché como `IReadOnlyList<AppInfo>` |

## UserDocumentSearch

Clase: `Yottacast.Core.Search.UserDocuments.UserDocumentSearch` (implementa `IDeferredSearchSource`)

Ver `docs/search-files.md` para la documentación completa de esta fuente, el backend `FileSearch`, los backends por plataforma y el scoring.

**Mínimo de caracteres** — `SearchAsync` hace `yield break` si `query.Length < 2`. Nunca se lanza una búsqueda de un solo carácter.

**Timeout interno** — `UserDocumentSearch` crea un `CancellationTokenSource` vinculado al `ct` del caller y le aplica `CancelAfter(timeoutMs)` (defecto: 20 s). La task de background que llama a `fileSearch.SearchAsync` usa este CTS derivado. El `await foreach` del canal usa el `ct` original del caller.

**Task de background con `CancellationToken.None`** — la `Task.Run` que ejecuta la búsqueda se inicia con `CancellationToken.None`, así la tarea no se cancela externamente; la cancelación se propaga a través del CTS derivado a `fileSearch.SearchAsync`.

**Snapshots basados en tiempo, no en conteo** — los snapshots intermedios se emiten como máximo una vez cada 200 ms (`SnapshotIntervalMs`). Además, siempre se emite un snapshot final después de que `fileSearch.SearchAsync` termina o es cancelada (si el buffer no está vacío).

**Wildcards** — si la query contiene `*`, se salta toda la lógica de tokenización y scoring multi-token; el resultado recibe score fijo 0.5 independientemente del nombre del archivo.

## Scoring

El algoritmo de scoring de cada fuente está documentado en `docs/search-scoring.md` (apps, emoji y scores entre fuentes) y en `docs/search-files.md` (documentos).

## GlobalSearch

Clase: `Yottacast.Core.Search.GlobalSearch`

Orquesta todas las sources registradas por DI. Distingue dos listas: `_instantSources` y `_deferredSources`.

**Ciclo de vida**:
- `Start()` llama `Start()` en todas las sources (fire-and-forget).
- `WhenInstantReady()` — espera solo las instant sources; es el gate que usa la UI antes de aceptar input.
- `WhenReady()` — espera todas (instant + deferred).
- `Stop()` cancela y limpia todas las sources en paralelo.

**Búsqueda instant**: `SearchInstant(query, limit)` consulta todas las instant sources en secuencia, combina sus resultados, los ordena por score y aplica el limit global. Cada source recibe el mismo `limit` individualmente.

**Búsqueda deferred — merge por slots**: `SearchDeferredAsync` usa `SearchSourcesAsync`, que asigna un slot por source. Cuando cualquier source emite un nuevo snapshot, su slot se actualiza y se yields la unión ordenada de todos los slots. Así la UI refleja la mejor combinación disponible en cada instante, incluso si una source es más lenta.

La coordinación interna usa un `Channel<(int sourceIndex, snapshot)>` y `Task.WhenAll` para completar el canal cuando todas las sources terminan.

**Cancelación en las tasks de source** — cada task de source se inicia con `CancellationToken.None` (no con el `ct` del caller), de modo que no puede ser abortada externamente. La cancelación llega a la source vía el `ct` pasado a su `SearchAsync`. `OperationCanceledException` se captura silenciosamente dentro de cada task; la cancelación no produce errores en el canal.

## Interfaces de ciclo de vida

`IInstantSearchSource` y `IDeferredSearchSource` comparten el mismo contrato de ciclo de vida:

| Método | Contrato |
|---|---|
| `Start()` | Fire-and-forget. No devuelve Task — el arranque es siempre async interno. |
| `WhenReady()` | Task que completa cuando la source está lista para servir queries. |
| `Stop()` | Cancela y limpia. Devuelve Task (puede ser no-op). |

`IInstantSearchSource` expone `Search(string query, int limit) → IReadOnlyList<ResultItemViewModel>` (síncrono).
`IDeferredSearchSource` expone `SearchAsync(string query, int limit, CancellationToken) → IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>>` — cada elemento es un snapshot completo y ordenado, no un resultado individual.

## Flujo de búsqueda en MainWindowViewModel

`OnSearchTextChanged` cancela la búsqueda anterior (CTS) y arranca `SearchAsync`. Si el texto está vacío, limpia sin buscar.

`SearchAsync` opera en dos fases:

**Fase 1 — instant (sin delay)**:
1. Construye el `_googleItem` (o `null` si la query es solo `:`).
2. Llama `globalSearch.SearchInstant(query, limit: SearchSourceLimit)` — síncrono, en memoria.
3. Llama `RefreshResults()` para actualizar la UI.
4. Si la query empieza por `:` (modo emoji), se detiene aquí — las fuentes deferred no se lanzan.

**Fase 2 — deferred (debounce 250ms)**:
5. Espera 250ms con `Task.Delay(250, ct)`. Si el usuario sigue escribiendo, la CTS se cancela y se sale aquí.
6. Crea un nuevo `_deferredCts` vinculado al `ct` del caller para poder cancelar la búsqueda deferred independientemente (via `CancelDeferredSearch()`).
7. Itera `globalSearch.SearchDeferredAsync(...)` actualizando `_deferredSnapshot` en cada snapshot y llamando `RefreshResults()`.
8. Al terminar, si la búsqueda completó (no fue cancelada), actualiza `ShowNoResults`.

**`RefreshResults()`** — merge y selección:
- Combina `_googleItem` + `_instantSnapshot` + `_deferredSnapshot`, ordena por score, actualiza `Results`.
- La lógica de selección visual (auto-selección de calculadora, preservación del ítem previo, score del Google item) está documentada en `ui-main-window.md`.

**`SearchSourceLimit`**: cada source recibe un límite de 10 resultados. El merge global también aplica un límite de 10.

## FileSearch y PlatformProvider

Ver `docs/search-files.md` para la documentación de `FileSearch` y los backends por plataforma (macOS Spotlight, Windows Search, Linux locate/plocate).

## ApplicationSearch — detalles de implementación

**`Stop()`** limpia el caché, cancela el CTS de background, elimina todos los watchers y resetea `_readyTcs`. Esto permite un reinicio limpio llamando de nuevo a `Start()`, aunque actualmente ningún código lo hace.

**`AddApp()`** solo dispara el evento `AppAdded` si la clave no existía previamente en el caché (`isNew`). Las reescrituras (update de una app ya conocida) no disparan el evento.

**Scoring en `Search()`**: el score es el devuelto por `NameMatcher.Score` sin ninguna transformación adicional. La categoría es `"Applications"` y el icono es `"📱"`.

## Iconos de apps

**macOS**: `GetAppIconPath` lee `Contents/Info.plist` como texto (búsqueda de string, no parse XML), extrae el valor de `CFBundleIconFile`, añade `.icns` si no tiene extensión y verifica que el archivo exista en `Contents/Resources/`. Si cualquier paso falla, devuelve `null`.

**Windows y Linux**: `GetAppIconPath` devuelve siempre `null` — los iconos de apps no están implementados en estas plataformas.

## RandomSearch

`RandomSearch` es una `IDeferredSearchSource` de prueba. Está registrada como singleton en el contenedor DI, pero la línea que la conecta como `IDeferredSearchSource` activa en `GlobalSearch` está comentada en `App.axaml.cs`. Emite hasta 5 resultados con scores aleatorios (0.5–1.0) con delays progresivos entre ellos. Cada snapshot es acumulativo (añade al array existente antes de yield). Ver `Yottacast.Core.Search.RandomSearch`.
