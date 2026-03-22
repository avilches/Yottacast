# Diseño general de la aplicación

## Entrada: Program.cs

`Program.Main` está marcado con `[STAThread]` (requerido por Avalonia en Windows). Llama `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`. `BuildAvaloniaApp()` está separado para que el diseñador de Avalonia pueda instanciar la app sin ejecutar `Main`.

## Arranque (App.axaml.cs)

`App.OnFrameworkInitializationCompleted` es síncrono. Toda la inicialización de la aplicación ocurre dentro de este método, que Avalonia invoca desde `App.axaml.cs` tras arrancar el framework.

Orden de arranque en `OnFrameworkInitializationCompleted`:

1. `AppHandler.Instance.OnFrameworkInitializationCompleted()` — configuración OS-específica antes de nada (macOS: establece `NSApplicationActivationPolicyAccessory`)
2. `BuildServices()` — construye el contenedor DI
3. `ThemeService.Apply(userSettings.Theme)` — aplica el tema visual antes de que la ventana exista
4. `RunMigrations(userSettings, updateChecker, logger)` — compara `LastLaunchedVersion` con `UpdateChecker.CurrentVersion`; si difieren, ejecuta migraciones, actualiza el campo y persiste. Es síncrono y bloquea el arranque intencionalmente: las migraciones deben completarse antes de que el resto del arranque consuma `UserSettings`.
5. `DisableAvaloniaDataAnnotationValidation()` — elimina el plugin de validación de Avalonia para evitar conflictos con CommunityToolkit.Mvvm
6. `mainWindowViewModel.Initialize()` — dispara `CheckForUpdateAsync()` como fire-and-forget; comprueba en background si hay versión nueva y actualiza `UpdateAvailable`/`UpdateBannerText` en el UI thread cuando llega la respuesta (ver `ui-main-window.md`)
7. Creación de `MainWindow` con el ViewModel como `DataContext`
8. `ClipboardService.Initialize(...)` — registra el callback de UI-thread para que Core pueda copiar al portapapeles sin depender de Avalonia
9. `desktop.Exit +=` — registra el handler de cierre de la app que llama `globalSearch.Stop()`; `RegisterGlobalHotKey(desktop)` — registra el hook global de SharpHook
10. `base.OnFrameworkInitializationCompleted()` — señala a Avalonia que la inicialización terminó
11. `globalSearch.Start()` — fire-and-forget; inicia el ciclo de vida de todas las fuentes de búsqueda
12. `ShowWhenInstantReadyAsync(globalSearch, desktop)` — fire-and-forget; bloquea internamente con `await globalSearch.WhenInstantReady()` hasta que todas las instant sources están listas, y solo entonces ejecuta `AppHandler.Instance.OnShow()`, `desktop.MainWindow.Show()` y `Activate()`

La ventana no aparece hasta que las instant sources están listas. `CheckForUpdateAsync()` trabaja en background mientras se espera.

**Qué hace `globalSearch.Start()`** — delega en cada fuente (tanto `IInstantSearchSource` como `IDeferredSearchSource`):

- **`ApplicationSearch.Start()`** — la única con trabajo real. Llama `ScanAndWatchAsync()` como fire-and-forget, que:
  1. `await platform.ScanAppsAsync(...)` — escaneo inicial (macOS: mdfind; Windows/Linux: scan de directorios)
  2. Completa la task `WhenReady()` al terminar el scan
  3. Instala `FileSystemWatcher`s vía `platform.CreateAppWatchers(...)`
- El resto de fuentes (`UserDocumentSearch` y demás `IDeferredSearchSource`) tienen `Start()` como no-op: no tienen estado de arranque propio y se invocan bajo demanda en cada búsqueda. El método existe para mantener el contrato simétrico con `IInstantSearchSource`.

**`WhenReady()`** — tanto `IInstantSearchSource` como `IDeferredSearchSource` exponen `Task WhenReady()`. `GlobalSearch.WhenReady()` hace `Task.WhenAll` sobre todas las fuentes (instant y deferred). Las fuentes sin arranque asíncrono devuelven `Task.CompletedTask`.

**Consecuencia para búsquedas**: la UI no se muestra hasta que `WhenInstantReady()` complete, es decir, hasta que todas las instant sources (incluyendo `ApplicationSearch`) están listas. El usuario nunca ve la ventana sin apps en los resultados.

**Consecuencia para Settings**: `App.OpenSettings()` es `async void` y hace `await applicationSearch.WhenReady()` antes de crear la `SettingsWindow`. Esto garantiza que `BrowserDiscovery.Discover()` y `TerminalDiscovery.Discover()` (llamados en el constructor del ViewModel) ya tienen el caché poblado. Si el caché ya está listo (usuario abre Settings tarde), el await es instantáneo. Si la ventana ya está visible (`IsVisible: true`), se activa sin crear nada nuevo. Si no está visible, crea siempre una nueva `SettingsWindow` con un nuevo `SettingsWindowViewModel` (transient).

`UserSettings.Load(platform)` carga (o crea) el JSON y siempre hace `Save()` al final. La validación de Browser/Terminal no ocurre en el arranque; `UserSettings` se auto-repara en el momento de uso, cuando se accede a `ActiveBrowser` / `ActiveTerminal`.

## Servicios registrados en DI

- `PlatformProvider` (singleton, instancia concreta elegida en `BuildServices()` con una única comprobación de OS)
- `ProcessRunner` (singleton, envuelve la ejecución de procesos del sistema; lo usan `WindowsPlatformProvider` y `LinuxPlatformProvider`)
- `UserSettings` (singleton, cargado con `UserSettings.Load(platform)`)
- `ApplicationSearch` (singleton, `IInstantSearchSource`)
- `CalculatorSearch` (singleton, `IInstantSearchSource`)
- `EmojiSearch` (singleton, `IInstantSearchSource`; recibe la ruta de caché como `Path.Combine(SpecialFolder.ApplicationData, "Yottacast")`)
- `UserDocumentSearch` (singleton, `IDeferredSearchSource`)
- `RandomSearch` (singleton, registrado en DI pero comentado como `IDeferredSearchSource` — solo para tests de la pipeline de streaming)
- `GlobalSearch` (singleton, recibe `IEnumerable<IInstantSearchSource>` + `IEnumerable<IDeferredSearchSource>`)
- `UpdateChecker` (singleton)
- `BrowserDiscovery`, `TerminalDiscovery`, `FileSearch`, `ClipboardService`, `MathJsEngine`, `EmojiDataLoader`, `ThemeService` (singleton)
- `MainWindowViewModel`, `SettingsWindowViewModel` (transient)

## Logging

`BuildServices()` configura Serilog antes de construir el contenedor DI. El logger escribe en fichero con rotación diaria y retención de 7 días. La ruta del fichero es OS-específica: en macOS `~/Library/Logs/Yottacast/yottacast-.log`; en Windows/Linux `%LOCALAPPDATA%/Yottacast/Logs/yottacast-.log`. Serilog se inyecta en el contenedor DI mediante `AddLogging(b => b.AddSerilog(..., dispose: true))`, de modo que todos los servicios reciben `ILogger<T>` estándar de Microsoft y el backend puede cambiarse sin tocar el código de negocio.

## Hotkey global (RegisterGlobalHotKey)

`RegisterGlobalHotKey` crea un `SimpleGlobalHook` (no `TaskPoolGlobalHook`). La diferencia es crítica: `SimpleGlobalHook` invoca los handlers síncronamente en el hilo del hook, requisito para que `e.SuppressEvent = true` tenga efecto a nivel de OS. Con `TaskPoolGlobalHook`, el evento ya ha sido entregado al OS antes de que el handler pueda suprimirlo.

Al coincidir la hotkey (tecla + modificadores comparados contra `UserSettings.ParsedHotkey`), se hace `e.SuppressEvent = true` para evitar pitidos en Yottacast y en la app anterior. Luego se despacha al UI thread: si la ventana está visible y activa → `Hide()` + `OnHide()`; si no → `OnShow()` + `Show()` + `Activate()`.

El mapa `KeyNameToKeyCode` se construye una sola vez en tiempo de carga de clase (`BuildKeyNameMap()`): incluye Space/Enter/Tab/Backspace/Delete/Escape, A–Z, 0–9 y F1–F12. La comparación de nombres de tecla es case-insensitive.

## ClipboardService — bridge UI/Core

`ClipboardService.Initialize(callback)` recibe una función `Action<string>` que ya encapsula el marshal al UI thread. La implementación en `App.axaml.cs` captura `mainWindow` en un closure y delega en `TopLevel.GetTopLevel(mainWindow)?.Clipboard?.SetTextAsync(text)` dentro de `Dispatcher.UIThread.InvokeAsync`. Esto permite que código en `Yottacast.Core` (sin dependencia de Avalonia) copie al portapapeles simplemente llamando `clipboardService.CopyToClipboard(text)`.

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

**`SearchInstant` — agregación cross-source**: llama `Search(query, limit)` en cada fuente instant por separado, hace `SelectMany` de todos los resultados, los ordena por score descendente y aplica un único `Take(limit)`. El límite no se aplica por fuente en la agregación: cada fuente puede recibir hasta `limit` resultados, pero la mezcla final también queda acotada a `limit`.

## Debounce (MainWindowViewModel)

**Fast path — query vacía**: si `SearchText` es whitespace o vacía, `OnSearchTextChanged` limpia `Results`, pone `HasResults = false`, `ShowNoResults = false`, `IsSearching = false` y retorna inmediatamente sin invocar ninguna búsqueda.

```
OnSearchTextChanged → cancela CTS anterior, resetea _userNavigated
  → si query vacía → limpia estado y retorna
  → Phase 1 (instant, sin delay): construye _googleItem → RefreshResults() → SearchInstant síncrono → actualiza _instantSnapshot → RefreshResults()
  → si la query empieza por ':' → termina aquí (solo fuentes instant; no hay búsqueda deferred)
  → espera debounce de 250ms
  → crea _deferredCts (linked a CT principal)
  → IsSearching = true
  → Phase 2 (deferred): SearchDeferredAsync con _deferredCts.Token → cada snapshot actualiza _deferredSnapshot → RefreshResults()
  → IsSearching = false (en finally)
  → si completó sin cancelar y Results.Count == 0 → ShowNoResults = true
```

Nota sobre modo emoji (query empieza por `:`): la fase deferred se omite completamente. El comportamiento visual del ítem de Google en modo emoji está documentado en `ui-main-window.md`.

Ambas fases usan `SearchSourceLimit` como límite (ver `MainWindowViewModel.SearchSourceLimit`, constante local fija a 10): cada fuente recibe ese valor como límite sugerido. `RefreshResults()` no aplica ese límite al resultado combinado final.

El `_deferredCts` es un `CancellationTokenSource` enlazado al CT principal, creado justo antes de la fase deferred. Permite cancelar selectivamente solo la fase deferred (p.ej. al pulsar ESC con `CancelDeferredSearch()`) sin cancelar el flujo principal.

`RefreshResults()` reconstruye `Results` fusionando `[googleItem] + _instantSnapshot + _deferredSnapshot`, ordenados por score descendente. Lógica de selección:
- Si hay un resultado con Category "Calculator" o "Converter" y el usuario no ha navegado manualmente (`_userNavigated == false`), ese resultado queda seleccionado automáticamente.
- En caso contrario: si el resultado previamente seleccionado sigue en la lista, se preserva; si no, se selecciona el primero.

## ResultItemViewModel — contrato de resultados

`ResultItemViewModel` (en `Yottacast.Core.ViewModels`) es el tipo de dato que todas las fuentes producen y que la UI consume. Todos sus campos son `init`-only (inmutable tras construcción). Sus campos relevantes para el flujo de control:

- `OnActivate` — acción a ejecutar al pulsar Enter. Es `null` si el ítem no tiene acción.
- `PasteAfterActivate` — cuando es `true`, después de activar el ítem el launcher llama `AppHandler.Instance.OnHide()` y luego `SimulatePasteAsync()`. Permite que el resultado (p.ej. un emoji copiado) sea pegado automáticamente en la app anterior.
- `OnLeft` / `OnRight` — capturan las teclas izquierda/derecha antes de que el `TextBox` las consuma (útil para navegar dentro de un ítem grid).
- `OnUp` / `OnDown` — devuelven `bool`; si `true` el evento se considera consumido (el item lo procesó), si `false` la ventana cae al comportamiento estándar de navegación de lista.
- `Shortcut` — texto opcional para mostrar un atajo de teclado en la UI (decorativo, no genera lógica de teclado por sí mismo).

El enrutado de teclas de flecha ocurre en la fase tunnel de `MainWindow` para garantizar que los ítems de grid capturen las teclas antes que el `TextBox`.

**`EmojiGridResultViewModel`** es una subclase de `ResultItemViewModel` que además implementa `INotifyPropertyChanged`. Añade una lista `Cells` de `EmojiCellViewModel` y gestiona `SelectedEmojiIndex`. La navegación dentro del grid siempre wrappea en horizontal (`SelectNext`/`SelectPrevious` con módulo) pero devuelve `false` en `SelectDown`/`SelectUp` cuando el índice resultante saldría del rango, permitiendo que la ventana no consuma el evento y el usuario pueda salir del grid. `Columns` es una constante `= 8`. Los callbacks `OnLeft`/`OnRight`/`OnUp`/`OnDown` del `ResultItemViewModel` base se conectan a estos métodos al construir el ítem en `EmojiSearch`.

**`EmojiCellViewModel`** es la unidad mínima del grid: `Char`, `Name`, `Category`, `Keywords` (array) y `IsSelected` (mutable, con `INotifyPropertyChanged`). La propiedad `KeywordsText` devuelve los keywords unidos por coma para uso en bindings de tooltip o subtítulo.

## Teclado y ciclo de vida de la ventana (MainWindow)

**Foco del `SearchBox`**: el constructor de `MainWindow` suscribe `SearchBox.Focus()` al evento `Opened`. Además, `OnPropertyChanged` detecta cambios en `IsVisibleProperty`: cuando la ventana se hace visible, reactiva el `SearchBox` (`IsEnabled = true`) y le da el foco; cuando se oculta, lo desactiva (`IsEnabled = false`). Esto evita que el `TextBox` reciba input de fondo mientras la ventana está oculta.

**Apertura de Settings**: `Key.OemComma` con modificador `Meta` (Cmd+, en macOS) invoca `App.OpenSettings()` desde el handler de teclado de `MainWindow`. El shortcut solo existe en `OnKeyDown`, no en el handler tunnel.

**Alt+Space consumido**: la combinación Alt+Space se marca como `e.Handled = true` explícitamente para evitar que macOS produzca un pitido al recibir una tecla no manejada.

**Escape — tres niveles**:
1. Si `IsSearching == true` → cancela solo la fase deferred (`CancelDeferredSearch()`).
2. Si `SearchText` no está vacío → limpia el texto.
3. Si el texto ya está vacío → oculta la ventana.

**Navegación de lista — circular**: `SelectNext(vm, delta)` calcula el índice como `(current + delta + Count) % Count`, de modo que la navegación wrapalea: flecha abajo desde el último ítem salta al primero y viceversa. Llama `vm.NotifyUserNavigated()` para desactivar la selección automática de resultados de calculadora.

**Enter — activación**:
1. Llama `SelectedResult.OnActivate()`.
2. Limpia `SearchText` y oculta la ventana (`Hide()`).
3. Si `result.PasteAfterActivate == true`, llama además `AppHandler.Instance.OnHide()` (restaura foco a la app anterior) y luego `SimulatePasteAsync()` (simula Cmd+V / Ctrl+V con un delay de 150 ms para dar tiempo al foco).

**Cierre nativo bloqueado**: `OnClosing` siempre cancela el cierre (`e.Cancel = true`) y llama `Hide()`. La ventana nunca se destruye en runtime — es necesario para que el proceso permanezca vivo sin icono en el Dock/taskbar. Nota: en macOS, cuando `SettingsWindow` se cierra, el sistema puede enrutar un `performClose:` a `MainWindow`; este handler lo intercepta y lo convierte en hide.

**`CloseWindowShortcut` en `MainWindow`**: antes de procesar cualquier otra tecla en `OnKeyDown`, se comprueba si coincide con `AppHandler.Instance.CloseWindowShortcut`; si es así, se oculta la ventana y se marca `e.Handled = true` sin llegar al `base.OnKeyDown`.


## AppHandler — contrato OS-específico de UI

`AppHandler` (en `Yottacast/Services/`) es una clase abstracta con un singleton estático `Instance` seleccionado en tiempo de carga según el OS. Expone:

- `OnFrameworkInitializationCompleted()` — configuración OS al arrancar (macOS: `NSApplicationActivationPolicyAccessory`).
- `OnShow()` — captura la app en primer plano y activa Yottacast.
- `OnHide()` — restaura el foco a la app capturada en `OnShow()`.
- `CloseWindowShortcut` — atajo de teclado para cerrar ventana: Cmd+W (macOS), Ctrl+F4 (Windows), Ctrl+W (Linux). Lo usa `MainWindow` para consumir el shortcut y ocultar en vez de cerrar.
- `SimulatePasteAsync()` — espera 150 ms y sintetiza Cmd+V (macOS, vía CGEvent) o Ctrl+V (Windows, vía `keybd_event`). `LinuxAppHandler` no implementa el paste simulado (usa la implementación base que devuelve `Task.CompletedTask`).

Los detalles de implementación por plataforma están en `docs/multi-platform.md`.

## Arquitectura snapshot-por-fuente

`IDeferredSearchSource.SearchAsync` devuelve `IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>>`: cada yield es un snapshot completo (los mejores N ordenados), no un item individual. `IInstantSearchSource.Search` devuelve directamente `IReadOnlyList` de forma síncrona. Ambos permiten **reemplazar** en lugar de **acumular**:

- `ApplicationSearch` → emite un único snapshot con todas las apps coincidentes
- `UserDocumentSearch` → emite snapshots progresivos con throttling por tiempo (intervalo definido como constante local en `SearchAsync`) y uno final; las queries cortas se omiten (ver `UserDocumentSearch.SearchAsync`); tiene un timeout configurable (ver parámetro `timeoutMs` del constructor) — si el file search tarda más, se cancela y se emite igualmente el snapshot final con los resultados acumulados hasta ese momento
- `GlobalSearch` → mantiene un array `snapshots[sourceIndex]`; cada nuevo snapshot reemplaza su slot y se emite la unión ordenada
- `MainWindowViewModel` → mantiene `_instantSnapshot` y `_deferredSnapshot`; `RefreshResults()` los fusiona en cada actualización
