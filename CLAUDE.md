# CLAUDE.md

Yottacast is a macOS/Windows app launcher — similar to Spotlight or PowerToys Run.
It's a frameless, transparent dark-themed window where the user types to search and uses arrow keys + Enter to launch items.

Update the CLAUDE.md when something non-obvious is worth keeping in mind for later.

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
├── Process/
│   ├── CommandRunner.cs                ← Punto de entrada público: CommandRunner.RunAsync(backend, ...)
│   ├── ICommandRunner.cs               ← internal: abstracción stream de líneas
│   ├── StandardCommandRunner.cs        ← internal: Process.RedirectStandardOutput (block-buffered)
│   ├── PtyRunner.cs                    ← internal: Pty.Net (line-buffered, más rápido) — preferido
│   └── ProcessResult.cs                ← (Elapsed, ExitCode, Cancelled, Error?)
├── Search/
│   ├── ISearchSource.cs                ← Interfaz: Start(), Stop(), SearchAsync → IAsyncEnumerable
│   ├── GlobalSearch.cs                 ← Agrega ISearchSource[], merge streaming vía Channel
│   ├── AppInfo.cs + ApplicationSearch.cs  ← ISearchSource: caché en memoria de apps
│   └── UserDocumentSearch.cs           ← ISearchSource: delega en FileSearch (streaming)
├── Services/
│   ├── FileSearch.cs                   ← Búsqueda de archivos: mdfind / Windows Search / locate
│   ├── UserSettings.cs                 ← Config persistida en JSON
│   ├── BrowserDiscovery.cs             ← Detecta navegadores instalados (usa ApplicationSearch)
│   ├── BrowserLauncher.cs              ← Abre URL en navegador concreto
│   ├── TerminalDiscovery.cs            ← Detecta terminales instalados
│   └── TerminalLauncher.cs             ← Ejecuta comando en terminal concreto
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

`App.OnFrameworkInitializationCompleted` construye el contenedor DI (`BuildServices`) y arranca la búsqueda:

```csharp
var searchService = _services.GetRequiredService<GlobalSearch>();
_ = searchService.Start();   // arranca todas las ISearchSource en paralelo
```

Servicios registrados en DI:
- `UserSettings` (singleton, cargado con `UserSettings.Load()`)
- `ApplicationSearch` (singleton, ISearchSource)
- `UserDocumentSearch` (singleton, ISearchSource)
- `GlobalSearch` (singleton, recibe `IEnumerable<ISearchSource>`)
- `BrowserDiscovery`, `TerminalDiscovery` (singleton)
- `MainWindowViewModel`, `SettingsWindowViewModel` (transient)

### Motor de búsqueda: GlobalSearch

Clase: `Yottacast.Core.Search.GlobalSearch`

Agrega múltiples `ISearchSource` recibidas por inyección. `SearchAsync` devuelve `IAsyncEnumerable<ResultItemViewModel>` — las fuentes corren en paralelo y los resultados se entregan conforme llegan vía un `Channel<T>` interno.

```
ISearchSource
├── ApplicationSearch    ← apps instaladas (desde caché en memoria)
└── UserDocumentSearch   ← documentos (delega en FileSearch, streaming via Channel)
```

Para añadir una nueva fuente: implementar `ISearchSource` y registrarla en `BuildServices` como `services.AddSingleton<ISearchSource>(...)`.

### Debounce (MainWindowViewModel)

El debounce está en el ViewModel, **no** en las fuentes de búsqueda:

```
OnSearchTextChanged → cancela CTS anterior → espera 250ms → await foreach GlobalSearch.SearchAsync
```

Antes del debounce, se añade inmediatamente un resultado de "Search en Google" para que la UI siempre tenga algo mientras el usuario escribe.

### Resultados: streaming

Los resultados llegan incrementalmente a la UI conforme cada fuente los produce:
- `ApplicationSearch` → yield de resultados en memoria (rápido, primero)
- `UserDocumentSearch` → streaming via `Channel<T>` desde `FileSearch.SearchAsync` callback
- `GlobalSearch` → merge de fuentes via `Channel<T>`, `await foreach` en `MainWindowViewModel`

### Scoring

Score = 1 para todos los resultados. Los resultados llegan en orden de fuente (apps primero, luego archivos) ya que el streaming no permite un sort global sin buffering.

---

## Fuentes de búsqueda

### ApplicationSearch

Clase: `Yottacast.Core.Search.ApplicationSearch` (implementa `ISearchSource`)

Mantiene un `ConcurrentDictionary<string, AppInfo>` en memoria con las apps instaladas.
Inyecta `UserSettings` para leer `AppDirectories`.

**Arranque por plataforma:**
- **macOS**: `mdfind` one-shot con `StandardCommandRunner` para carga inicial síncrona, luego `FileSystemWatcher` en cada directorio de `AppDirectories` para actualizaciones en vivo (`*.app`).
- **Windows**: escaneo de `AppDirectories` buscando `.exe`, luego `FileSystemWatcher`.
- **Linux**: escaneo de `AppDirectories` buscando `.desktop`, luego `FileSystemWatcher`.

La búsqueda es substring case-insensitive sobre el nombre de la app (ej. "saf" encuentra "Safari").

Evento `AppAdded` notifica cuando se detecta una app nueva (lo usan `BrowserDiscovery` / `TerminalDiscovery`).

**Cambio de AppDirectories en settings**: `ApplicationSearch.ReloadAppDirectories()` hace `Stop()` + `Start()` limpiando el caché (`_apps.Clear()` en `Stop()`). `SettingsWindowViewModel` tiene `ApplicationSearch` inyectado — cuando se añada UI para `AppDirectories`, llamar `_applicationSearch.ReloadAppDirectories()`.

### UserDocumentSearch (búsqueda de documentos)

Clase: `Yottacast.Core.Search.UserDocumentSearch` (implementa `ISearchSource`)

Sin caché. Cada búsqueda llama a `FileSearch.SearchAsync` con los `SearchFolders` de `UserSettings`.
Si los directorios cambian en settings, la siguiente búsqueda los usará automáticamente.
Los resultados se entregan vía `Channel<T>` para streaming real hacia la UI.

`Start()` y `Stop()` son no-ops (no hay estado que gestionar).

---

## UserSettings

Clase: `Yottacast.Core.Services.UserSettings`

Persiste en JSON. Todos los campos tienen defaults multiplataforma; nunca lanza excepción.

**Ruta del fichero:**
- macOS: `~/Library/Application Support/Yottacast/settings.json` (usa `SpecialFolder.ApplicationData`)
- Windows: `%APPDATA%\Yottacast\settings.json`

> **Nota**: la ruta macOS es `~/Library/Application Support/`, **no** `~/.config/` (error en versiones anteriores de este doc).

**Campos:**

| Campo | Tipo | Default |
|---|---|---|
| `Browser` | string | `""` (auto-selecciona el primero disponible) |
| `Terminal` | string | `""` |
| `Theme` | string | `"dark-default"` |
| `SearchFolders` | `List<string>` | Downloads, Desktop, Documents, Movies/Videos, Pictures |
| `AppDirectories` | `List<string>` | `/Applications`, `~/Applications` (macOS) / `Program Files` (Win) / `.desktop` dirs (Linux) |

**Browser/Terminal preferido**: el usuario elige entre los detectados por `BrowserDiscovery`/`TerminalDiscovery` (solo apps instaladas). Se muestra en `SettingsWindowViewModel`.

**Detección del browser predeterminado del sistema** ⚠️ TODO: no implementado. El default es `""` y se selecciona el primero de la lista de `BrowserDiscovery`.

API: `UserSettings.Load()` → instancia. `settings.Save()` guarda cambios. Se guarda automáticamente al cambiar cada campo en SettingsWindow.

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

## Process runners (CommandRunner)

Punto de entrada público: `CommandRunner.RunAsync(backend, binary, args, cwd, onLine, ct)`.
`ICommandRunner`, `StandardCommandRunner` y `PtyRunner` son **internal** — no usar directamente.

Acepta `Func<string, bool> onLine` — retorna `false` para parar antes del EOF:

- **`RunnerBackend.Standard`** (`StandardCommandRunner`) — redirige stdout al pipe del proceso. El SO hace buffer del pipe hasta llenarlo o que el proceso acabe (block-buffered). Útil para comandos que terminan solos y donde no importa la latencia.
- **`RunnerBackend.Pty`** (`PtyRunner`) — abre un pseudo-terminal (Pty.Net). La terminal fuerza flush línea a línea. Resultados llegan en tiempo real: se puede consumir, hacer timeout y cortar el proceso antes de que termine. Preferido para `mdfind`, `locate`, etc.

`FileSearch` usa `RunnerBackend.Pty` por defecto (configurable via `RunnerBackend` enum).
`ApplicationSearch` usa `RunnerBackend.Standard` para la carga inicial de macOS.

**Gotcha tests PTY**: `PtyRunnerTests` deshabilita paralelización (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) por race conditions de kqueue en Pty.Net.

---

## BrowserDiscovery / TerminalDiscovery

- Usan `ApplicationSearch` como caché primaria (buscan por nombre exacto).
- Fallback a rutas hardcodeadas si la app no está en la caché.
- `Discover()` → solo apps instaladas. `GetCandidatePaths()` → lista completa (para el picker de settings).
- Linux: no implementado (devuelve lista vacía).

### BrowserLauncher
macOS: `open -a "Nombre" "url"`. Windows: lanza el `.exe` con la URL como argumento.

### TerminalLauncher
macOS varía por terminal:
- **Terminal.app** → AppleScript `do script`
- **iTerm** → AppleScript `create window with default profile command`
- **Warp** → URL scheme `warp://action/new_tab?command=...`
- **Resto** → genera `.command` temporal con `chmod +x` y lo abre con `open -a`

Windows: PowerShell usa `-NoExit -Command`, CMD usa `/K`.

### FileSearch
Búsqueda de archivos vía índice nativo del SO:
- **macOS** → `mdfind` con predicado `kMDItemFSName == '*query*'cd` (Spotlight, case-insensitive)
- **Windows** → PowerShell + ADODB.Connection (`Provider=Search.CollatorDSO`)
- **Linux** → `plocate` o `locate -b`

API: `FileSearch.SearchAsync(query, onResult, maxResults, backend, searchFolders, ct)`.
`onResult` es un callback `Action<FileResult>` — los resultados llegan conforme `mdfind` los emite.

---

## SharpHook (global hotkey)

En v7 los tipos están en `SharpHook.Data`, no en `SharpHook.Native` (v5). `ModifierMask` → `EventMask`.

```csharp
using SharpHook;       // TaskPoolGlobalHook
using SharpHook.Data;  // KeyCode, EventMask
```

ALT+Space muestra/oculta la ventana.

---

## Gotchas

- **No `BoxShadow` en el root Border** — Avalonia lo renderiza como rectángulo independientemente del `CornerRadius`. macOS provee sombra redondeada nativa vía la ventana frameless transparente.
- **Compiled bindings** habilitados globalmente (`AvaloniaUseCompiledBindingsByDefault=true`) — los bindings deben ser type-resolvable en compile time.
- **`DataAnnotationsValidationPlugin`** deshabilitado en `App.axaml.cs` para evitar conflictos con CommunityToolkit.Mvvm.
- **Window hide vs close** — `Hide()` en Escape (no `Close()`); `Show()` + `Activate()` restaura. El SettingsWindow evita duplicados: si ya está visible lo activa, si no crea una nueva instancia.
- **Raw string literals con variables PowerShell** — usar `$$"""..."""` en lugar de `$"""..."""` cuando el contenido tiene `$var`. Con `$$`, interpolación C# pasa a `{{expr}}` y los `$` sueltos son literales.
- **Lazy icon en AppInfo** — usa `Lazy<T>` para diferir la lectura de `Info.plist` hasta el primer acceso al icono (evita parsear cientos de plists al arranque).

## Keyboard shortcuts (MainWindow)

- `ESC` con texto → limpia el texto. Sin texto → oculta la ventana.
- `↑` / `↓` → navega resultados
- `Enter` → activa resultado seleccionado
- `⌘,` → abre SettingsWindow (si MainWindow está visible)
- `ALT+Space` → global hotkey para mostrar/ocultar
