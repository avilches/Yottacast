# Diseño de búsqueda

## Arranque (App.axaml.cs)

`App.OnFrameworkInitializationCompleted` es síncrono. Toda la inicialización de la aplicación ocurre dentro de este método, que Avalonia invoca desde `App.axaml.cs` tras arrancar el framework.

Orden de arranque en `OnFrameworkInitializationCompleted`:

1. `AppHandler.Instance.OnFrameworkInitializationCompleted()` — configuración OS-específica antes de nada (macOS: establece `NSApplicationActivationPolicyAccessory`)
2. `BuildServices()` — construye el contenedor DI
3. `ThemeService.Apply(userSettings.Theme)` — aplica el tema visual antes de que la ventana exista
4. `RunMigrations(userSettings, updateChecker, logger)` — compara `LastLaunchedVersion` con `UpdateChecker.CurrentVersion`; si difieren, ejecuta migraciones, actualiza el campo y persiste. No bloquea.
5. `DisableAvaloniaDataAnnotationValidation()` — elimina el plugin de validación de Avalonia para evitar conflictos con CommunityToolkit.Mvvm
6. `mainWindowViewModel.Initialize()` — dispara `CheckForUpdateAsync()` como fire-and-forget; comprueba en background si hay versión nueva y, si la hay, actualiza `UpdateAvailable`/`UpdateBannerText` en el UI thread cuando llega la respuesta. La MainWindow muestra un banner de actualización (ver `MainWindow.axaml`) cuando `UpdateAvailable` es `true`; el comando `UpdateBannerClickCommand` es un placeholder para la futura acción de actualización
7. Creación de `MainWindow` con el ViewModel como `DataContext`
8. `ClipboardService.Initialize(...)` — registra el callback de UI-thread para que Core pueda copiar al portapapeles sin depender de Avalonia
9. `desktop.Exit +=` — registra el handler de cierre de la app que llama `globalSearch.Stop()`; `RegisterGlobalHotKey(desktop)` — registra el hook global de SharpHook
10. `base.OnFrameworkInitializationCompleted()` — señala a Avalonia que la inicialización terminó
11. `AppHandler.Instance.OnShow()` — configura el comportamiento de plataforma antes de mostrar la ventana
12. `desktop.MainWindow.Show()` / `Activate()` — la ventana aparece
13. `globalSearch.Start()` — fire-and-forget; el arranque **no espera** a que termine
14. `_ = services.GetRequiredService<MathJsEngine>()` — resuelve el singleton desde DI para disparar el warm-up de Jint en background

El arranque no bloquea. La ventana ya es interactiva desde el paso 12 mientras `globalSearch.Start()` y `CheckForUpdateAsync()` trabajan en segundo plano.

**Qué hace `globalSearch.Start()`** — delega en cada fuente (tanto `IInstantSearchSource` como `IDeferredSearchSource`):

- **`ApplicationSearch.Start()`** — la única con trabajo real. Llama `ScanAndWatchAsync()` como fire-and-forget, que:
  1. `await platform.ScanAppsAsync(...)` — escaneo inicial (macOS: mdfind; Windows/Linux: scan de directorios)
  2. Completa la task `WhenReady()` al terminar el scan
  3. Instala `FileSystemWatcher`s vía `platform.CreateAppWatchers(...)`
- El resto de fuentes (`UserDocumentSearch` y demás `IDeferredSearchSource`) tienen `Start()` como no-op: no tienen estado de arranque propio y se invocan bajo demanda en cada búsqueda. El método existe para mantener el contrato simétrico con `IInstantSearchSource`.

**`WhenReady()`** — tanto `IInstantSearchSource` como `IDeferredSearchSource` exponen `Task WhenReady()`. `GlobalSearch.WhenReady()` hace `Task.WhenAll` sobre todas las fuentes (instant y deferred). Las fuentes sin arranque asíncrono devuelven `Task.CompletedTask`.

**Consecuencia para búsquedas**: hasta que `WhenReady()` complete en macOS, `Search` de `ApplicationSearch` devuelve vacío. La UI es interactiva desde el arranque; simplemente no hay apps en los resultados hasta que mdfind acaba.

**Consecuencia para Settings**: `App.OpenSettings()` es `async void` y hace `await applicationSearch.WhenReady()` antes de crear la `SettingsWindow`. Esto garantiza que `BrowserDiscovery.Discover()` y `TerminalDiscovery.Discover()` (llamados en el constructor del ViewModel) ya tienen el caché poblado. Si el caché ya está listo (usuario abre Settings tarde), el await es instantáneo. Si la ventana ya está visible (`IsVisible: true`), se activa sin crear nada nuevo. Si no está visible, crea siempre una nueva `SettingsWindow` con un nuevo `SettingsWindowViewModel` (transient).

`UserSettings.Load(platform)` carga (o crea) el JSON y siempre hace `Save()` al final. La validación de Browser/Terminal no ocurre en el arranque; `UserSettings` se auto-repara en el momento de uso, cuando se accede a `ActiveBrowser` / `ActiveTerminal`.

## Logging

Configurado con Serilog en `BuildServices()`. Los logs se escriben en fichero rotatorio diario (retención de 7 días):

- macOS: `~/Library/Logs/Yottacast/yottacast-<fecha>.log`
- Windows/Linux: `%LOCALAPPDATA%\Yottacast\Logs\yottacast-<fecha>.log`

El nivel mínimo es `Debug`. Todos los servicios reciben `ILogger<T>` por inyección.

## Servicios registrados en DI

- `PlatformProvider` (singleton, instancia concreta elegida en `BuildServices()` con una única comprobación de OS)
- `UserSettings` (singleton, cargado con `UserSettings.Load(platform)`)
- `ApplicationSearch` (singleton, `IInstantSearchSource`)
- `CalculatorSearch` (singleton, `IInstantSearchSource`)
- `EmojiSearch` (singleton, `IInstantSearchSource`)
- `UserDocumentSearch` (singleton, `IDeferredSearchSource`)
- `RandomSearch` (singleton, registrado en DI pero comentado como `IDeferredSearchSource` — solo para tests de la pipeline de streaming)
- `GlobalSearch` (singleton, recibe `IEnumerable<IInstantSearchSource>` + `IEnumerable<IDeferredSearchSource>`)
- `UpdateChecker` (singleton)
- `BrowserDiscovery`, `TerminalDiscovery`, `FileSearch`, `ClipboardService`, `MathJsEngine`, `EmojiDataLoader`, `ThemeService` (singleton)
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
  → Phase 1 (instant, sin delay): construye _googleItem → SearchInstant síncrono → actualiza _instantSnapshot → RefreshResults()
  → si la query empieza por ':' → termina aquí (solo fuentes instant; no hay búsqueda deferred ni Google)
  → espera debounce de 250ms
  → crea _deferredCts (linked a CT principal)
  → Phase 2 (deferred): SearchDeferredAsync con _deferredCts.Token → cada snapshot actualiza _deferredSnapshot → RefreshResults()
```

Nota sobre modo emoji (query empieza por `:`): el ítem de Google se incluye si `query.Length > 1` (usando `query[1..].Trim()` como término), o es `null` si la query es solo `:`. La fase deferred se omite completamente.

Ambas fases usan `SearchSourceLimit` como límite (ver `MainWindowViewModel.SearchSourceLimit`): cada fuente recibe ese valor como límite sugerido, y el resultado combinado también se trunca a ese límite.

El `_deferredCts` es un `CancellationTokenSource` enlazado al CT principal, creado justo antes de la fase deferred. Permite cancelar selectivamente solo la fase deferred (p.ej. al pulsar ESC con `CancelDeferredSearch()`) sin cancelar el flujo principal.

`RefreshResults()` reconstruye `Results` fusionando `[googleItem] + _instantSnapshot + _deferredSnapshot`, ordenados por score descendente. Lógica de selección:
- Si hay un resultado con Category "Calculator" o "Converter" y el usuario no ha navegado manualmente (`_userNavigated == false`), ese resultado queda seleccionado automáticamente.
- En caso contrario: si el resultado previamente seleccionado sigue en la lista, se preserva; si no, se selecciona el primero.

## Arquitectura snapshot-por-fuente

`IDeferredSearchSource.SearchAsync` devuelve `IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>>`: cada yield es un snapshot completo (los mejores N ordenados), no un item individual. `IInstantSearchSource.Search` devuelve directamente `IReadOnlyList` de forma síncrona. Ambos permiten **reemplazar** en lugar de **acumular**:

- `ApplicationSearch` → emite un único snapshot con todas las apps coincidentes
- `UserDocumentSearch` → emite snapshots progresivos con throttling por tiempo (intervalo definido como constante local en `SearchAsync`) y uno final; las queries cortas se omiten (ver `UserDocumentSearch.SearchAsync`); tiene un timeout configurable (ver parámetro `timeoutMs` del constructor) — si el file search tarda más, se cancela y se emite igualmente el snapshot final con los resultados acumulados hasta ese momento
- `GlobalSearch` → mantiene un array `snapshots[sourceIndex]`; cada nuevo snapshot reemplaza su slot y se emite la unión ordenada
- `MainWindowViewModel` → mantiene `_instantSnapshot` y `_deferredSnapshot`; `RefreshResults()` los fusiona en cada actualización
