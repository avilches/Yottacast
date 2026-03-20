# Diseño de búsqueda

## Arranque (App.axaml.cs)

`App.OnFrameworkInitializationCompleted` es síncrono. `Program.Main` llama `AppHandler.Instance.OnStart()` antes de que Avalonia arranque, para configurar la plataforma (p.ej. ocultar el icono del Dock en macOS) antes de que `NSApplication` se inicialice.

Orden de arranque en `OnFrameworkInitializationCompleted`:

1. `BuildServices()` — construye el contenedor DI
2. `ThemeService.Apply(...)` + creación de `MainWindow` con su `DataContext`
3. `ClipboardService.Initialize(...)` — registra el callback de UI-thread para que Core pueda copiar al portapapeles sin depender de Avalonia
4. `base.OnFrameworkInitializationCompleted()` — señala a Avalonia que la inicialización terminó
5. `AppHandler.Instance.OnShow()` — configura el comportamiento de plataforma **antes** de mostrar la ventana
6. `desktop.MainWindow.Show()` / `Activate()` — la ventana aparece
7. `globalSearch.Start()` — fire-and-forget; el arranque **no espera** a que termine
8. `_ = services.GetRequiredService<MathJsEngine>()` — resuelve el singleton desde DI para disparar el warm-up de Jint en background

El arranque no bloquea en el paso 4. La ventana ya es interactiva y el usuario puede escribir mientras `Start()` trabaja en segundo plano.

**Qué hace `globalSearch.Start()`** — delega en cada `ISearchSource`:

- **`ApplicationSearch.Start()`** — la única con trabajo real. Llama `ScanAndWatchAsync()` como fire-and-forget, que:
  1. `await platform.ScanAppsAsync(...)` — escaneo inicial (macOS: mdfind; Windows/Linux: scan de directorios)
  2. Completa la task `WhenReady()` al terminar el scan
  3. Instala `FileSystemWatcher`s vía `platform.CreateAppWatchers(...)`
- **`UserDocumentSearch.Start()`** — no-op. `WhenReady()` retorna `Task.CompletedTask` inmediatamente.

**`WhenReady()`** — `ISearchSource` expone `Task WhenReady()` que se completa cuando el scan inicial ha terminado y el caché está poblado. `GlobalSearch.WhenReady()` hace `Task.WhenAll` sobre todos los sources.

**Consecuencia para búsquedas**: hasta que `WhenReady()` complete en macOS, `SearchAsync` de `ApplicationSearch` devuelve vacío. La UI es interactiva desde el arranque; simplemente no hay apps en los resultados hasta que mdfind acaba.

**Consecuencia para Settings**: `App.OpenSettings()` es `async void` y hace `await applicationSearch.WhenReady()` antes de crear `SettingsWindowViewModel`. Esto garantiza que `BrowserDiscovery.Discover()` y `TerminalDiscovery.Discover()` (llamados en el constructor del ViewModel) ya tienen el caché poblado. Si el caché ya está listo (usuario abre Settings tarde), el await es instantáneo.

`UserSettings.Load(platform)` carga (o crea) el JSON y siempre hace `Save()` al final. La validación de Browser/Terminal no ocurre en el arranque; `UserSettings` se auto-repara en el momento de uso, cuando se accede a `ActiveBrowser` / `ActiveTerminal`.

## Servicios registrados en DI

- `PlatformProvider` (singleton, instancia concreta elegida en `BuildServices()` con una única comprobación de OS)
- `UserSettings` (singleton, cargado con `UserSettings.Load(platform)`)
- `ApplicationSearch` (singleton, ISearchSource, IsInstant=true)
- `CalculatorSearch` (singleton, ISearchSource, IsInstant=true)
- `UserDocumentSearch` (singleton, ISearchSource, IsInstant=false)
- `GlobalSearch` (singleton, recibe `IEnumerable<ISearchSource>`)
- `BrowserDiscovery`, `TerminalDiscovery`, `FileSearch` (singleton)
- `MainWindowViewModel`, `SettingsWindowViewModel` (transient)

## Motor de búsqueda: GlobalSearch

Clase: `Yottacast.Core.Search.GlobalSearch`

Agrega múltiples `ISearchSource` recibidas por inyección. `SearchAsync` devuelve `IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>>` — cada emisión es un snapshot completo (los mejores N resultados hasta ese momento). Cada fuente "posee" un slot; cuando emite un nuevo snapshot, el slot se actualiza y GlobalSearch emite la unión ordenada de todos los slots.

Internamente usa un `Channel.CreateUnbounded<(int, IReadOnlyList<...>)>()`. Cada fuente se lanza con `Task.Run(..., CancellationToken.None)` — se pasa `CancellationToken.None` (no el CT de búsqueda) para desacoplar el ciclo de vida de la tarea de la cancelación de la búsqueda. Las `OperationCanceledException` lanzadas por las fuentes individuales se capturan y se descartan. El channel se completa mediante `Task.WhenAll(tasks).ContinueWith(_ => channel.Writer.TryComplete(), ...)` una vez que todas las tareas de fuente han terminado.

```
ISearchSource
├── ApplicationSearch    ← apps instaladas (desde caché en memoria)          IsInstant=true
├── CalculatorSearch     ← expresiones math y conversiones de unidades        IsInstant=true
└── UserDocumentSearch   ← documentos (delega en FileSearch, streaming via Channel)  IsInstant=false
```

Para añadir una nueva fuente: implementar `ISearchSource` y registrarla en `BuildServices` como `services.AddSingleton<ISearchSource>(...)`.

## Debounce (MainWindowViewModel)

```
OnSearchTextChanged → cancela CTS anterior, resetea _userNavigated
  → Phase 1 (instant): SearchInstantAsync inmediato → actualiza _instantSnapshot → RefreshResults()
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

`ISearchSource.SearchAsync` devuelve `IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>>`: cada yield es un snapshot completo (los mejores N ordenados), no un item individual. Esto permite **reemplazar** en lugar de **acumular**:

- `ApplicationSearch` → emite un único snapshot con todas las apps coincidentes
- `UserDocumentSearch` → emite snapshots progresivos con throttling por tiempo (ver `SnapshotIntervalMs`) y uno final; las queries cortas se omiten (ver `UserDocumentSearch.SearchAsync`); tiene un timeout configurable (ver parámetro `timeoutMs` del constructor) — si el file search tarda más, se cancela y se emite igualmente el snapshot final con los resultados acumulados hasta ese momento
- `GlobalSearch` → mantiene un array `snapshots[sourceIndex]`; cada nuevo snapshot reemplaza su slot y se emite la unión ordenada
- `MainWindowViewModel` → mantiene `_instantSnapshot` y `_deferredSnapshot`; `RefreshResults()` los fusiona en cada actualización
