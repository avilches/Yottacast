# Diseño de búsqueda

## Arranque (App.axaml.cs)

`App.OnFrameworkInitializationCompleted` es síncrono. `Program.Main` llama `AppHandler.Instance.OnStart()` antes de que Avalonia arranque, para configurar la plataforma (p.ej. ocultar el icono del Dock en macOS) antes de que `NSApplication` se inicialice.

Orden de arranque en `OnFrameworkInitializationCompleted`:

1. `BuildServices()` — construye el contenedor DI
2. `ThemeService.Apply(userSettings.Theme)` — aplica el tema visual antes de que la ventana exista
3. `RunMigrations(userSettings, updateChecker, logger)` — compara `LastLaunchedVersion` con `UpdateChecker.CurrentVersion`; si difieren, ejecuta migraciones, actualiza el campo y persiste. No bloquea.
4. `mainWindowViewModel.Initialize()` — dispara `CheckForUpdateAsync()` como fire-and-forget; comprueba en background si hay versión nueva y, si la hay, actualiza `UpdateAvailable`/`UpdateBannerText` en el UI thread cuando llega la respuesta
5. Creación de `MainWindow` con el ViewModel como `DataContext`
6. `ClipboardService.Initialize(...)` — registra el callback de UI-thread para que Core pueda copiar al portapapeles sin depender de Avalonia
7. `base.OnFrameworkInitializationCompleted()` — señala a Avalonia que la inicialización terminó
8. `AppHandler.Instance.OnShow()` — configura el comportamiento de plataforma **antes** de mostrar la ventana
9. `desktop.MainWindow.Show()` / `Activate()` — la ventana aparece
10. `globalSearch.Start()` — fire-and-forget; el arranque **no espera** a que termine
11. `_ = services.GetRequiredService<MathJsEngine>()` — resuelve el singleton desde DI para disparar el warm-up de Jint en background

El arranque no bloquea. La ventana ya es interactiva desde el paso 9 mientras `globalSearch.Start()` y `CheckForUpdateAsync()` trabajan en segundo plano.

**Qué hace `globalSearch.Start()`** — delega en cada fuente (tanto `IInstantSearchSource` como `IDeferredSearchSource`):

- **`ApplicationSearch.Start()`** — la única con trabajo real. Llama `ScanAndWatchAsync()` como fire-and-forget, que:
  1. `await platform.ScanAppsAsync(...)` — escaneo inicial (macOS: mdfind; Windows/Linux: scan de directorios)
  2. Completa la task `WhenReady()` al terminar el scan
  3. Instala `FileSystemWatcher`s vía `platform.CreateAppWatchers(...)`
- El resto de fuentes (`UserDocumentSearch` y demás `IDeferredSearchSource`) tienen `Start()` como no-op: no tienen estado de arranque propio y se invocan bajo demanda en cada búsqueda. El método existe para mantener el contrato simétrico con `IInstantSearchSource`.

**`WhenReady()`** — tanto `IInstantSearchSource` como `IDeferredSearchSource` exponen `Task WhenReady()`. `GlobalSearch.WhenReady()` hace `Task.WhenAll` sobre todas las fuentes (instant y deferred). Las fuentes sin arranque asíncrono devuelven `Task.CompletedTask`.

**Consecuencia para búsquedas**: hasta que `WhenReady()` complete en macOS, `Search` de `ApplicationSearch` devuelve vacío. La UI es interactiva desde el arranque; simplemente no hay apps en los resultados hasta que mdfind acaba.

**Consecuencia para Settings**: `App.OpenSettings()` es `async void` y hace `await applicationSearch.WhenReady()` antes de crear `SettingsWindowViewModel`. Esto garantiza que `BrowserDiscovery.Discover()` y `TerminalDiscovery.Discover()` (llamados en el constructor del ViewModel) ya tienen el caché poblado. Si el caché ya está listo (usuario abre Settings tarde), el await es instantáneo.

`UserSettings.Load(platform)` carga (o crea) el JSON y siempre hace `Save()` al final. La validación de Browser/Terminal no ocurre en el arranque; `UserSettings` se auto-repara en el momento de uso, cuando se accede a `ActiveBrowser` / `ActiveTerminal`.

## Servicios registrados en DI

- `PlatformProvider` (singleton, instancia concreta elegida en `BuildServices()` con una única comprobación de OS)
- `UserSettings` (singleton, cargado con `UserSettings.Load(platform)`)
- `ApplicationSearch` (singleton, `IInstantSearchSource`)
- `CalculatorSearch` (singleton, `IInstantSearchSource`)
- `EmojiSearch` (singleton, `IInstantSearchSource`)
- `UserDocumentSearch` (singleton, `IDeferredSearchSource`)
- `GlobalSearch` (singleton, recibe `IEnumerable<IInstantSearchSource>` + `IEnumerable<IDeferredSearchSource>`)
- `UpdateChecker` (singleton)
- `BrowserDiscovery`, `TerminalDiscovery`, `FileSearch`, `ClipboardService`, `MathJsEngine`, `EmojiDataLoader` (singleton)
- `MainWindowViewModel`, `SettingsWindowViewModel` (transient)

## Motor de búsqueda: GlobalSearch

Clase: `Yottacast.Core.Search.GlobalSearch`

Agrega dos grupos de fuentes recibidas por inyección: `IInstantSearchSource` (síncrono, caché en memoria) e `IDeferredSearchSource` (asíncrono, acceso a disco). Las búsquedas siguen dos fases separadas: `SearchInstant` (síncrono, devuelve `IReadOnlyList`) y `SearchDeferredAsync` (devuelve `IAsyncEnumerable<IReadOnlyList>`). Cada emisión de la fase deferred es un snapshot completo (los mejores N resultados hasta ese momento). Cada fuente "posee" un slot; cuando emite un nuevo snapshot, el slot se actualiza y GlobalSearch emite la unión ordenada de todos los slots.

Internamente usa un `Channel.CreateUnbounded<(int, IReadOnlyList<...>)>()`. Cada fuente se lanza con `Task.Run(..., CancellationToken.None)` — se pasa `CancellationToken.None` (no el CT de búsqueda) para desacoplar el ciclo de vida de la tarea de la cancelación de la búsqueda. Las `OperationCanceledException` lanzadas por las fuentes individuales se capturan y se descartan. El channel se completa mediante `Task.WhenAll(tasks).ContinueWith(_ => channel.Writer.TryComplete(), ...)` una vez que todas las tareas de fuente han terminado.

```
IInstantSearchSource  (síncrono, Start()/WhenReady()/Stop()/Search())
├── ApplicationSearch    ← apps instaladas (desde caché en memoria)
├── CalculatorSearch     ← expresiones math y conversiones de unidades
└── EmojiSearch          ← grid de emojis, filtrado por nombre/keyword

IDeferredSearchSource  (asíncrono, Start()/WhenReady()/Stop()/SearchAsync() → IAsyncEnumerable)
└── UserDocumentSearch   ← documentos (delega en FileSearch, streaming via Channel)
```

Para añadir una fuente instant: implementar `IInstantSearchSource` y registrar en `BuildServices` como `services.AddSingleton<IInstantSearchSource>(...)`.
Para añadir una fuente deferred: implementar `IDeferredSearchSource` y registrar como `services.AddSingleton<IDeferredSearchSource>(...)`.

## Debounce (MainWindowViewModel)

```
OnSearchTextChanged → cancela CTS anterior, resetea _userNavigated
  → Phase 1 (instant): SearchInstant síncrono → actualiza _instantSnapshot → RefreshResults()
  → espera un breve debounce (ver `SearchAsync` en `MainWindowViewModel`)
  → crea _deferredCts (linked a CT principal)
  → Phase 2 (deferred): SearchDeferredAsync con _deferredCts.Token → cada snapshot actualiza _deferredSnapshot → RefreshResults()
```

Cada fase limita los resultados por fuente a un máximo configurable (ver `MainWindowViewModel.SearchSourceLimit`).

El `_deferredCts` es un `CancellationTokenSource` enlazado al CT principal, creado justo antes de la fase deferred. Permite cancelar selectivamente solo la fase deferred (p.ej. al pulsar ESC con `CancelDeferredSearch()`) sin cancelar el flujo principal.

`RefreshResults()` reconstruye `Results` fusionando `[googleItem] + _instantSnapshot + _deferredSnapshot`, ordenados por score descendente. Lógica de selección:
- Si hay un resultado con Category "Calculator" o "Converter" y el usuario no ha navegado manualmente (`_userNavigated == false`), ese resultado queda seleccionado automáticamente.
- En caso contrario: si el resultado previamente seleccionado sigue en la lista, se preserva; si no, se selecciona el primero.

## Arquitectura snapshot-por-fuente

`IDeferredSearchSource.SearchAsync` devuelve `IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>>`: cada yield es un snapshot completo (los mejores N ordenados), no un item individual. `IInstantSearchSource.Search` devuelve directamente `IReadOnlyList` de forma síncrona. Ambos permiten **reemplazar** en lugar de **acumular**:

- `ApplicationSearch` → emite un único snapshot con todas las apps coincidentes
- `UserDocumentSearch` → emite snapshots progresivos con throttling por tiempo (ver `SnapshotIntervalMs`) y uno final; las queries cortas se omiten (ver `UserDocumentSearch.SearchAsync`); tiene un timeout configurable (ver parámetro `timeoutMs` del constructor) — si el file search tarda más, se cancela y se emite igualmente el snapshot final con los resultados acumulados hasta ese momento
- `GlobalSearch` → mantiene un array `snapshots[sourceIndex]`; cada nuevo snapshot reemplaza su slot y se emite la unión ordenada
- `MainWindowViewModel` → mantiene `_instantSnapshot` y `_deferredSnapshot`; `RefreshResults()` los fusiona en cada actualización
