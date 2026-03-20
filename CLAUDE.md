# CLAUDE.md

Yottacast is a macOS/Windows app launcher — similar to Spotlight or PowerToys Run.
It's a frameless, transparent dark-themed window where the user types to search and uses arrow keys + Enter to launch items.

**Stack**: Avalonia 11.3.12, .NET 9, CommunityToolkit.Mvvm 8.2.1, SharpHook 7.1.1.

Update the CLAUDE.md when something non-obvious is worth keeping in mind for later.

**Regla de mantenimiento**: describe siempre el estado actual del código. No documentes cambios respecto a versiones anteriores ni migraciones. Si al editar escribes algo como "ahora X en vez de Y", "ya no se usa Z", o "antes se hacía así", reformúlalo para describir solo el comportamiento actual. Los gotchas y precauciones sí se documentan, pero sin referenciar versiones pasadas.

## Estructura de la solución

```
Yottacast.sln
├── Yottacast/                          ← GUI app (Avalonia, WinExe, net9.0)
├── Yottacast.Core/                     ← Shared library (net9.0, sin UI)
├── Yottacast.Cli/                      ← CLI para testear servicios (Exe, net9.0)
└── Yottacast.Core.Tests/               ← Tests xUnit
```

### Yottacast/ (GUI)

```
├── Views/
│   ├── MainWindow.axaml/.cs            ← Ventana frameless; teclado: ESC, ↑↓, Enter, ⌘,
│   ├── SettingsWindow.axaml/.cs        ← Preferencias (decorada, no frameless)
│   └── ViewLocator.cs
├── ViewModels/
│   ├── MainWindowViewModel.cs          ← Búsqueda con debounce, resultado inmediato Google
│   └── SettingsWindowViewModel.cs      ← Browser, terminal, theme pickers
├── Services/
│   └── ThemeService.cs                 ← Aplica tema JSON en runtime
├── Themes/
│   ├── dark-default.json / dark-raycast.json / dark-macos.json
│   ├── light-blue.json / light-gray.json
│   └── settings.json                   ← Tema activo: { "theme": "dark-default" }
└── App.axaml / App.axaml.cs            ← DI, hotkey global, singleton SettingsWindow
```

### Yottacast.Core/ (lib compartida)

```
├── Platform/
│   ├── PlatformProvider.cs             ← abstract base: todo el código OS-específico
│   ├── MacOsPlatformProvider.cs        ← implementación macOS
│   ├── WindowsPlatformProvider.cs      ← implementación Windows
│   └── LinuxPlatformProvider.cs        ← implementación Linux
├── Process/
│   ├── StandardCommandRunner.cs        ← public: Process.RedirectStandardOutput; único runner disponible
│   └── ProcessResult.cs                ← (Elapsed, ExitCode, Cancelled, Error?)
├── Search/
│   ├── ISearchSource.cs                ← Interfaz: Start() void, Ready() Task, Stop(), SearchAsync → IAsyncEnumerable
│   ├── GlobalSearch.cs                 ← Agrega ISearchSource[], merge streaming vía Channel
│   ├── AppInfo.cs + ApplicationSearch.cs  ← ISearchSource: caché en memoria de apps
│   └── UserDocumentSearch.cs           ← ISearchSource: delega en FileSearch (streaming)
├── Services/
│   ├── FileSearch.cs                   ← Instancia que delega en PlatformProvider.SearchFilesAsync
│   ├── UserSettings.cs                 ← Config persistida en JSON
│   ├── BrowserDiscovery.cs             ← Detecta navegadores; OpenUrl() delega en PlatformProvider
│   └── TerminalDiscovery.cs            ← Detecta terminales; ExecuteCommand() delega en PlatformProvider
└── ViewModels/
    ├── ResultItemViewModel.cs           ← (Icon, Title, Subtitle, Category, Score, OnActivate)
    └── ViewModelBase.cs                 ← ObservableObject (CommunityToolkit.Mvvm)
```

### Yottacast.Cli/

CLI interactivo para probar servicios. Comandos: `browsers`, `terminals`, `apps`, `search <query>`, `run <binary> [args]`.

```bash
cd Yottacast.Cli && dotnet run
```

## Build & Run

```bash
# GUI
cd Yottacast && dotnet run
dotnet publish -c Release -r osx-arm64 --self-contained

# Tests
cd Yottacast.Core.Tests && dotnet test
```

---

## Diseño de búsqueda (intención + estado actual)

### Arranque (App.axaml.cs)

`App.OnFrameworkInitializationCompleted` es síncrono. Orden de arranque:

1. `BuildServices()` — construye el contenedor DI
2. `ThemeService.Apply(...)` + creación de `MainWindow` — la ventana aparece inmediatamente
3. `base.OnFrameworkInitializationCompleted()` — señala a Avalonia que la inicialización terminó
4. `_ = globalSearch.Start()` — fire-and-forget; el arranque **no espera** a que termine

El arranque no bloquea en el paso 4. La ventana ya es interactiva y el usuario puede escribir mientras `Start()` trabaja en segundo plano.

**Qué hace `globalSearch.Start()`** — delega en cada `ISearchSource`:

- **`ApplicationSearch.Start()`** — la única con trabajo real. Llama `ScanAndWatchAsync()` como fire-and-forget, que:
  1. `await platform.ScanAppsAsync(...)` — escaneo inicial (macOS: mdfind; Windows/Linux: scan de directorios)
  2. Señala `Ready()` al terminar el scan
  3. Instala `FileSystemWatcher`s vía `platform.CreateAppWatchers(...)`
- **`UserDocumentSearch.Start()`** — no-op. `Ready()` retorna `Task.CompletedTask` inmediatamente.

**`Ready()`** — `ISearchSource` expone `Task Ready()` que se completa cuando el scan inicial ha terminado y el caché está poblado. `GlobalSearch.Ready()` hace `Task.WhenAll` sobre todos los sources.

**Consecuencia para búsquedas**: hasta que `Ready()` complete en macOS, `SearchAsync` de `ApplicationSearch` devuelve vacío. La UI es interactiva desde el arranque; simplemente no hay apps en los resultados hasta que mdfind acaba.

**Consecuencia para Settings**: `App.OpenSettings()` es `async void` y hace `await applicationSearch.Ready()` antes de crear `SettingsWindowViewModel`. Esto garantiza que `BrowserDiscovery.Discover()` y `TerminalDiscovery.Discover()` (llamados en el constructor del ViewModel) ya tienen el caché poblado. Si el caché ya está listo (usuario abre Settings tarde), el await es instantáneo.

`UserSettings.Load(platform)` carga (o crea) el JSON y siempre hace `Save()` al final. La validación de Browser/Terminal no ocurre en el arranque; `UserSettings` se auto-repara en el momento de uso, cuando se accede a `ActiveBrowser` / `ActiveTerminal` (ver `EnsureIntegrity` abajo).

Servicios registrados en DI:
- `PlatformProvider` (singleton, instancia concreta elegida en `BuildServices()` con una única comprobación de OS)
- `UserSettings` (singleton, cargado con `UserSettings.Load(platform)`)
- `ApplicationSearch` (singleton, ISearchSource)
- `UserDocumentSearch` (singleton, ISearchSource)
- `GlobalSearch` (singleton, recibe `IEnumerable<ISearchSource>`)
- `BrowserDiscovery`, `TerminalDiscovery`, `FileSearch` (singleton)
- `MainWindowViewModel`, `SettingsWindowViewModel` (transient)

### Motor de búsqueda: GlobalSearch

Clase: `Yottacast.Core.Search.GlobalSearch`

Agrega múltiples `ISearchSource` recibidas por inyección. `SearchAsync` devuelve `IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>>` — cada emisión es un snapshot completo (los mejores N resultados hasta ese momento). Cada fuente "posee" un slot; cuando emite un nuevo snapshot, el slot se actualiza y GlobalSearch emite la unión ordenada de todos los slots.

```
ISearchSource
├── ApplicationSearch    ← apps instaladas (desde caché en memoria)
└── UserDocumentSearch   ← documentos (delega en FileSearch, streaming via Channel)
```

Para añadir una nueva fuente: implementar `ISearchSource` y registrarla en `BuildServices` como `services.AddSingleton<ISearchSource>(...)`.

### Debounce (MainWindowViewModel)

```
OnSearchTextChanged → cancela CTS anterior
  → Phase 1 (instant): SearchInstantAsync inmediato → actualiza _instantSnapshot → RefreshResults()
  → espera 250ms
  → Phase 2 (deferred): SearchDeferredAsync → cada snapshot actualiza _deferredSnapshot → RefreshResults()
```

`RefreshResults()` reconstruye `Results` fusionando `[googleItem] + _instantSnapshot + _deferredSnapshot`, ordenados por score descendente. Preserva la selección actual si el item sigue en la lista.

### Resultados: arquitectura snapshot-por-fuente

`ISearchSource.SearchAsync` devuelve `IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>>`: cada yield es un snapshot completo (los mejores N ordenados), no un item individual. Esto permite **reemplazar** en lugar de **acumular**:

- `ApplicationSearch` → emite un único snapshot con todas las apps coincidentes
- `UserDocumentSearch` → emite snapshots progresivos cada `SnapshotEvery=10` resultados y uno final
- `GlobalSearch` → mantiene un array `snapshots[sourceIndex]`; cada nuevo snapshot reemplaza su slot y se emite la unión ordenada
- `MainWindowViewModel` → mantiene `_instantSnapshot` y `_deferredSnapshot`; `RefreshResults()` los fusiona en cada actualización

### Scoring

`ApplicationSearch` usa tres modos de matching por prioridad:
1. **Prefix de token** (Score = 1.0): cualquier token CamelCase o palabra empieza por el query. "Saf" → "Safari", "Mon" → "Activity Monitor". "af" NO coincide con "Safari".
2. **Iniciales** (Score = 1.0): las iniciales de todos los tokens empiezan por el query. "AM" → "Activity Monitor", "MON" → "Microsoft OneNote" (M=Microsoft, O=One, N=Note del CamelCase).
3. **Substring interno** (Score = 0.25): el nombre contiene el query (solo para queries ≥ 2 letras). "ari" → "Safari".

El resultado de Google (`MakeGoogleItem`) asigna `Score = 1`.

`UserDocumentSearch` puntúa cada candidato antes de ordenar. Para **queries con wildcard** (`*`) todos los resultados puntúan 0.5 (base). Para **queries sin wildcard**:

| Condición | Bonus | Total |
|---|---|---|
| `name == query` o `stem == query` (case-insensitive) | +2.0 | **2.5** |
| `name.StartsWith(query)` o `stem.StartsWith(query)` | +1.0 | **1.5** |
| `name.EndsWith(query)` | +0.3 | **0.8** |
| Contains (base only) | — | **0.5** |

Stem = `Path.GetFileNameWithoutExtension(name)`, por lo que `"report"` puntúa 2.5 contra `"report.pdf"`. No hay bonus por ser directorio (solo afecta icono y categoría).

**Snapshots progresivos**: `UserDocumentSearch` emite un snapshot máximo cada 200ms (throttling por tiempo, `SnapshotIntervalMs`) y uno final al terminar o cancelar. Esto evita que queries con muchos resultados (p.ej. "a") saturen la UI con decenas de actualizaciones por segundo.

---

## Fuentes de búsqueda

### ApplicationSearch

Clase: `Yottacast.Core.Search.ApplicationSearch` (implementa `ISearchSource`)

Mantiene un `ConcurrentDictionary<string, AppInfo>` en memoria con las apps instaladas.
Inyecta `UserSettings` para leer `AppDirectories`.

**Arranque por plataforma** — toda la lógica OS-específica está en `PlatformProvider`:
- **macOS**: `ScanAppsAsync` ejecuta `mdfind` con `StandardCommandRunner`; `CreateAppWatchers` monta watchers en `*.app`.
- **Windows**: `ScanAppsAsync` escanea `AppDirectories` buscando `.exe`; `CreateAppWatchers` en `*.exe`.
- **Linux**: `ScanAppsAsync` escanea `AppDirectories` buscando `.desktop`; `CreateAppWatchers` en `*.desktop`.

La búsqueda es substring case-insensitive sobre el nombre de la app (ej. "saf" encuentra "Safari").

Evento `AppAdded` notifica cuando se detecta una app nueva (disponible para suscriptores externos; actualmente ningún componente lo consume).

**Cambio de AppDirectories en settings**: `ApplicationSearch.ReloadAppDirectories()` hace `Stop()` + `Start()` limpiando el caché (`_apps.Clear()` en `Stop()`). `SettingsWindowViewModel` tiene `ApplicationSearch` inyectado — cuando se añada UI para `AppDirectories`, llamar `_applicationSearch.ReloadAppDirectories()`.

### UserDocumentSearch (búsqueda de documentos)

Clase: `Yottacast.Core.Search.UserDocumentSearch` (implementa `ISearchSource`)

Sin caché. Cada búsqueda llama a `FileSearch.SearchAsync` con `settings.ExpandedSearchFolders`.
Si los directorios cambian en settings, la siguiente búsqueda los usará automáticamente.

`Start()` y `Stop()` son no-ops (no hay estado que gestionar).

**Queries cortas**: `UserDocumentSearch.SearchAsync` hace `yield break` si `query.Length < 2` — la búsqueda de ficheros requiere al menos 2 letras. `FileSearch` y los `PlatformProvider` hacen early return (`Task.CompletedTask`) para queries vacías.

**Criterios de parada** — `SearchAsync` crea un `CancellationTokenSource` interno ligado al `ct` del caller con dos condiciones de parada:

1. **Timeout configurable** (por defecto `20_000ms`, parámetro `timeoutMs` del constructor): `cts.CancelAfter(timeoutMs)` detiene mdfind tras el tiempo configurado. Sin timeout, mdfind correría hasta agotar el índice completo.

El `OperationCanceledException` de cualquiera de las dos condiciones se captura — se emite un snapshot final con lo que hay en el buffer hasta ese momento.

No hay cap de líneas (`maxResults: int.MaxValue`): el timeout y el early exit son los únicos mecanismos de parada.

**Por qué `Directory.Exists(r.Path)` en el callback**: determina si el resultado es directorio o archivo para asignar icono, categoría y bonus de score. Se llama síncronamente dentro del callback de `onResult`, por lo que es una syscall en cada resultado — no es costoso a la escala de resultados esperada.

**Flujo completo**:
```
mdfind emite línea → onResult callback → puntúa → añade al buffer
                                        → cada 10 resultados → snapshot parcial al channel
cts.Token expira (timeoutMs) → OperationCanceledException → snapshot final → channel.Complete()
MainWindowViewModel recibe snapshots → RefreshResults() en cada uno
```

---

## UserSettings

Clase: `Yottacast.Core.Services.UserSettings`

Persiste en JSON. Todos los campos tienen defaults multiplataforma; nunca lanza excepción.

**Ruta del fichero:**
- macOS: `~/Library/Application Support/Yottacast/settings.json` (usa `SpecialFolder.ApplicationData`)
- Windows: `%APPDATA%\Yottacast\settings.json`

**Campos:**

| Campo | Tipo | Default |
|---|---|---|
| `Browser` | string | `""` (auto-selecciona el primero disponible) |
| `Terminal` | string | `""` |
| `Theme` | string | `"dark-default"` |
| `SearchFolders` | `List<string>` | Downloads, Desktop, Documents, Movies/Videos, Pictures |
| `AppDirectories` | `List<string>` | `/Applications`, `$HOME/Applications` (macOS) / `Program Files` (Win) / `.desktop` dirs (Linux) |

**Browser/Terminal preferido**: el usuario elige entre los detectados por `BrowserDiscovery`/`TerminalDiscovery` (solo apps instaladas). Se muestra en `SettingsWindowViewModel`.

**Detección del browser predeterminado del sistema** ⚠️ TODO: no implementado. El default es `""` y se selecciona el primero de la lista de `BrowserDiscovery`.

**API de ciclo de vida**: `UserSettings.Load(platform)` carga el JSON (o crea defaults si no existe), y siempre llama `Save()` al final — el fichero se reescribe en cada arranque. `settings.Save()` puede llamarse manualmente; también se llama automáticamente al cambiar cada campo en SettingsWindow.

**Rutas en el JSON**: `SearchFolders` y `AppDirectories` se almacenan en crudo (`$HOME/Downloads`, `~/foo`, rutas absolutas…). La expansión `$HOME/` → ruta absoluta ocurre en el momento de uso, nunca al cargar ni guardar. `PlatformProvider.ExpandPath()` gestiona `$HOME/` y `~/`. Las propiedades `ExpandedSearchFolders` / `ExpandedAppDirectories` devuelven las listas expandidas; los consumidores (`UserDocumentSearch`, `ApplicationSearch`) las usan directamente.

**Detección automática de tema**: si el campo `"theme"` no está en el JSON (archivo nuevo o borrado), `Load` llama `platform.DefaultTheme()` que consulta el modo oscuro del SO una vez de forma síncrona. En macOS: `defaults read -g AppleInterfaceStyle`. En Windows: registro. En Linux: `gsettings`. Si falla, usa `"dark-default"`.

### Auto-reparación de Browser y Terminal

`UserSettings` se auto-repara sin depender de `ApplicationSearch`:

- **`ActiveBrowser`** / **`ActiveTerminal`** — se evalúan en cada acceso (son propiedades, no campos). Llaman a `BrowserDiscovery.Resolve` / `TerminalDiscovery.Resolve` (método estático, comprueba disco):
  1. Si `Browser` / `Terminal` no está vacío → busca ese nombre concreto en disco.
  2. Si no existe (o el campo era `""`): itera `KnownBrowserNames` / `KnownTerminalNames` y devuelve el primero encontrado en disco.
  3. Si ninguno existe en disco → devuelve `null`.
  - Auto-reparación: si el nombre guardado no existe pero Resolve encuentra un alternativo (`resolved.Name != Browser`), actualiza el campo y llama `Save()`. Si `Browser = ""`, devuelve el primero disponible sin tocar el JSON.
  - **Devuelve `null`** solo cuando ningún browser/terminal conocido está instalado en el sistema.
- **`EnsureIntegrity()`** — accede a ambas propiedades, forzando la validación y el guardado si algo cambió. Llamar en puntos naturales (p.ej. al abrir Settings).

`SettingsWindowViewModel` llama `settings.EnsureIntegrity()` en su constructor, antes de inicializar los pickers. `MainWindowViewModel` usa `settings.ActiveBrowser` directamente al construir el resultado de Google.

---

## Themes

Clase: `Yottacast.Services.ThemeService`

Lee `Themes/{name}.json`, aplica tokens en `Application.Current.Resources` en runtime.

`ThemeService.Apply(themeName)` — carga el JSON indicado.
`ThemeService.ApplyBuiltinDefault()` — aplica dark-default hardcodeado como fallback (no puede fallar).

Tokens: `Theme.*` (ej. `Theme.WindowBackground`). Colores: `#AARRGGBB` (no `#RRGGBBAA`).
Los JSON se copian al output vía `CopyToOutputDirectory=PreserveNewest`.

Temas incluidos: `dark-default`, `dark-raycast`, `dark-macos`, `light-blue`, `light-gray`.

**Metadata en JSON (author, url)**: todos los temas tienen `"author": ""` y `"url": ""`. `ThemeService` los ignora hoy; estarán disponibles cuando se implemente la descarga de temas.

---

## Process runners (StandardCommandRunner)

Único runner: `StandardCommandRunner.RunAsync(binary, args, cwd, onLine, ct)`.

Acepta `Func<string, bool> onLine` — retorna `false` para parar antes del EOF. Redirige stdout al pipe del proceso (block-buffered). Al cancelar o recibir `false` de `onLine`, mata el proceso con `Kill(entireProcessTree: true)`.

Registrado en DI como singleton. Los `PlatformProvider`s lo reciben por inyección de constructor.

---

## PlatformProvider

Clase abstracta en `Yottacast.Core.Platform`. Centraliza toda la lógica OS-específica. Una única comprobación de OS en `App.axaml.cs` (y `Yottacast.Cli/Program.cs`) elige la instancia concreta; el resto del código no hace `RuntimeInformation.IsOSPlatform()`.

Responsabilidades:
- `IsSystemDarkMode()` / `DefaultTheme()` — detección del tema del SO
- `DefaultAppDirectories()` / `DefaultSearchFolders()` — valores por defecto según OS
- `ScanAppsAsync()` / `CreateAppWatchers()` / `LaunchApp()` — gestión de apps
- `SearchFilesAsync()` — búsqueda de archivos (mdfind / Windows Search / locate)
- `KnownBrowserNames` / `BrowserFallbackPaths` / `GetBrowserPaths()` / `OpenUrl()` — datos y lanzador de browsers
- `KnownTerminalNames` / `TerminalFallbackPaths` / `GetTerminalPaths()` / `ExecuteCommand()` — datos y lanzador de terminales
- `GetAppIconPath()` — icono de app (macOS: parsea Info.plist; otros: null)

## BrowserDiscovery / TerminalDiscovery

Inyectan `ApplicationSearch` y `PlatformProvider`. Los datos de browsers/terminales conocidos vienen de `platform.KnownBrowserNames` / `platform.BrowserFallbackPaths` etc.

Tres métodos con comportamientos distintos:

**`Discover()`** — apps realmente instaladas:
- Consulta caché de `ApplicationSearch`. Si no está en caché, usa `platform.BrowserFallbackPaths` (Windows) para comprobar disco.
- *Linux*: devuelve lista vacía (no implementado).

**`GetCandidatePaths()`** — lista completa para el picker de Settings:
- Usa caché si disponible; si no, la primera ruta de `platform.GetBrowserPaths(name)`. Puede mostrar apps no instaladas.

**`Resolve(string name, PlatformProvider)`** — método **estático**, sin dependencia de `ApplicationSearch`. Comprueba disco directamente vía `platform.GetBrowserPaths()`. Lo usa `UserSettings.ActiveBrowser` / `ActiveTerminal`. Funciona aunque el caché esté vacío.

**`OpenUrl()` / `ExecuteCommand()`** — métodos de instancia que delegan en `platform.OpenUrl()` / `platform.ExecuteCommand()`.

macOS terminal launch per app:
- **Terminal.app** → AppleScript `do script`
- **iTerm** → AppleScript `create window with default profile command`
- **Warp** → URL scheme `warp://action/new_tab?command=...`
- **Resto** → genera `.command` temporal con `chmod +x` y lo abre con `open -a`

Windows: PowerShell usa `-NoExit -Command`, CMD usa `/K`.

### FileSearch
Clase instancia (no estática). Delega en `platform.SearchFilesAsync()`.
- **macOS** → `mdfind` con predicado `kMDItemFSName == '*query*'cd` (Spotlight, case-insensitive)
- **Windows** → PowerShell + ADODB.Connection (`Provider=Search.CollatorDSO`)
- **Linux** → `plocate` o `locate -b`

API: `fileSearch.SearchAsync(query, onResult, maxResults, searchFolders, ct)`.
`onResult` es un callback `Action<FileResult>` — los resultados llegan conforme el proceso los emite.

---

## SharpHook (global hotkey)

Los tipos están en `SharpHook.Data`, **no** en `SharpHook.Native`. El modificador de teclas es `EventMask`, **no** `ModifierMask`.

```csharp
using SharpHook;       // TaskPoolGlobalHook
using SharpHook.Data;  // KeyCode, EventMask
```

ALT+Space muestra/oculta la ventana.

---

## Gotchas

- **No animar `RenderTransform` con keyframes CSS en Avalonia 11** — No hay animator registrado para `ITransform`, por lo que `<Setter Property="RenderTransform" Value="rotate(...)"/>` en un `<Animation>` lanza `InvalidOperationException: No animator registered for the property RenderTransform`. Usar animaciones CSS solo con propiedades de tipo simple (`double`, `Color`, `Thickness`…) que tienen animators built-in. Para indicadores de carga, animar `Opacity` con `PlaybackDirection="Alternate"` en lugar de rotación. Nota: `AutoReverse` no existe en Avalonia — el equivalente es `PlaybackDirection="Alternate"`.

- **No `BoxShadow` en el root Border** — Avalonia lo renderiza como rectángulo independientemente del `CornerRadius`. macOS provee sombra redondeada nativa vía la ventana frameless transparente.
- **Compiled bindings** habilitados globalmente (`AvaloniaUseCompiledBindingsByDefault=true`) — los bindings deben ser type-resolvable en compile time.
- **`DataAnnotationsValidationPlugin`** deshabilitado en `App.axaml.cs` para evitar conflictos con CommunityToolkit.Mvvm.
- **Window hide vs close** — `Hide()` en Escape (no `Close()`); `Show()` + `Activate()` restaura. Al ocultar la ventana (ALT+Space o ESC sin texto) el estado del ViewModel se preserva intacto: texto, resultados y búsquedas en curso continúan; al volver a mostrarla el usuario ve exactamente lo que había. El SettingsWindow evita duplicados: si ya está visible lo activa; si está oculto lo muestra; solo crea instancia nueva en el primer arranque o tras `Close()`.
- **ALT+Space toggle con foco**: ALT+Space oculta la ventana solo si está visible **y activa** (`window.IsVisible && window.IsActive`). Si está visible pero sin foco (tapada por otra ventana), la trae al frente (`Show()` + `Activate()`) en lugar de ocultarla.
- **Temas cargados síncronamente en SettingsWindow** — `SettingsWindowViewModel` llama `LoadThemes()` en su constructor, que lee del disco los JSON de `Themes/`. Si ninguno carga, añade `"dark-default"` como fallback.
- **`ResultItemViewModel.Shortcut`** — propiedad definida pero sin uso: nunca se asigna desde las fuentes de búsqueda ni se muestra en la UI. Placeholder para futuros atajos de teclado por resultado.

- **Raw string literals con variables PowerShell** — usar `$$"""..."""` en lugar de `$"""..."""` cuando el contenido tiene `$var`. Con `$$`, interpolación C# pasa a `{{expr}}` y los `$` sueltos son literales.
- **Lazy icon en AppInfo** — usa `Lazy<T>` para diferir la lectura de `Info.plist` hasta el primer acceso al icono (evita parsear cientos de plists al arranque).

## Keyboard shortcuts (MainWindow)

- `ESC` con búsqueda en curso → para la búsqueda diferida (mantiene texto y resultados parciales)
- `ESC` sin búsqueda en curso y texto no vacío → limpia el texto
- `ESC` sin búsqueda y sin texto → oculta la ventana
- `↑` / `↓` → navega resultados
- `Enter` → activa resultado seleccionado
- `⌘,` → abre SettingsWindow (si MainWindow está visible)
- `ALT+Space` → global hotkey para mostrar/ocultar

## Indicador de búsqueda en curso (IsSearching)

`MainWindowViewModel.IsSearching` es `true` mientras la fase diferida (`SearchDeferredAsync`) está activa. Se activa justo antes de iterar la fase diferida y se desactiva en el `finally` al completar, cancelar o fallar.

**Spinner en la UI**: cuando `IsSearching` es `true`, la search row muestra un `Ellipse` giratorio (`Classes="spinner"`, animación CSS en `Window.Styles`) en lugar del badge "ESC". Cuando `IsSearching` baja a `false`, la animación se detiene y el badge ESC reaparece (si el texto está vacío).

**`CancelDeferredSearch()`**: cancela solo la fase diferida sin tocar el texto ni la búsqueda instant. Llamado por el handler de ESC cuando `IsSearching == true`. Internamente cancela `_deferredCts`, que es un `CancellationTokenSource` enlazado al `ct` principal — si se teclea texto nuevo, el `ct` padre cancela ambas fases.

**`ShowNoResults`**: solo se activa si la búsqueda diferida completó sin cancelación (`completed = true`). Si se paró con ESC o por nueva búsqueda, los resultados parciales permanecen visibles sin mostrar "No results".
