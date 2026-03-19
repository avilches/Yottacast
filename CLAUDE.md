# CLAUDE.md

Yottacast is a macOS/Windows app launcher — similar to Spotlight or PowerToys Run.
It's a frameless, transparent dark-themed window where the user types to search and uses arrow keys + Enter to launch items.

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
├── Process/
│   ├── CommandRunner.cs                ← Punto de entrada público: CommandRunner.RunAsync(backend, ...)
│   ├── ICommandRunner.cs               ← internal: abstracción stream de líneas
│   ├── StandardCommandRunner.cs        ← internal: Process.RedirectStandardOutput (block-buffered)
│   ├── PtyRunner.cs                    ← internal: Pty.Net (line-buffered, más rápido) — preferido
│   └── ProcessResult.cs                ← (Elapsed, ExitCode, Cancelled, Error?)
├── Search/
│   ├── ISearchSource.cs                ← Interfaz: Start() void, Ready() Task, Stop(), SearchAsync → IAsyncEnumerable
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

`App.OnFrameworkInitializationCompleted` es síncrono. Orden de arranque:

1. `BuildServices()` — construye el contenedor DI
2. `ThemeService.Apply(...)` + creación de `MainWindow` — la ventana aparece inmediatamente
3. `base.OnFrameworkInitializationCompleted()` — señala a Avalonia que la inicialización terminó
4. `_ = globalSearch.Start()` — fire-and-forget; el arranque **no espera** a que termine

El arranque no bloquea en el paso 4. La ventana ya es interactiva y el usuario puede escribir mientras `Start()` trabaja en segundo plano.

**Qué hace `globalSearch.Start()`** — delega en cada `ISearchSource`:

- **`ApplicationSearch.Start()`** — la única con trabajo real:
  - *macOS*: lanza internamente `StartMacAsync()` como fire-and-forget. Esa tarea ejecuta `mdfind` vía `StandardCommandRunner` y espera a que termine (puede tardar varios segundos). Una vez termina, señala `Ready()` e instala `FileSystemWatcher` en cada `AppDirectory` para actualizaciones en vivo.
  - *Windows / Linux*: escaneo síncrono de `AppDirectories` (milisegundos) + `FileSystemWatcher`. Señala `Ready()` al terminar el scan.
- **`UserDocumentSearch.Start()`** — no-op. `Ready()` retorna `Task.CompletedTask` inmediatamente.

**`Ready()`** — `ISearchSource` expone `Task Ready()` que se completa cuando el scan inicial ha terminado y el caché está poblado. `GlobalSearch.Ready()` hace `Task.WhenAll` sobre todos los sources.

**Consecuencia para búsquedas**: hasta que `Ready()` complete en macOS, `SearchAsync` de `ApplicationSearch` devuelve vacío. La UI es interactiva desde el arranque; simplemente no hay apps en los resultados hasta que mdfind acaba.

**Consecuencia para Settings**: `App.OpenSettings()` es `async void` y hace `await applicationSearch.Ready()` antes de crear `SettingsWindowViewModel`. Esto garantiza que `BrowserDiscovery.Discover()` y `TerminalDiscovery.Discover()` (llamados en el constructor del ViewModel) ya tienen el caché poblado. Si el caché ya está listo (usuario abre Settings tarde), el await es instantáneo.

No hay validación de settings en el arranque. `UserSettings` se auto-repara en el momento de uso (ver `EnsureIntegrity` abajo).

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

Evento `AppAdded` notifica cuando se detecta una app nueva (disponible para suscriptores externos; actualmente ningún componente lo consume).

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

### Auto-reparación de Browser y Terminal

`UserSettings` se auto-repara sin depender de `ApplicationSearch`:

- **`ActiveBrowser`** — llama `BrowserDiscovery.Resolve(Browser)` (comprueba disco). Si el nombre guardado ya no existe, actualiza `Browser` al primero disponible y llama `Save()`. Si `Browser` es `""`, devuelve el primero disponible sin persistir nada.
- **`ActiveTerminal`** — ídem para terminales.
- **`EnsureIntegrity()`** — accede a ambas propiedades. Llamar en puntos naturales (p.ej. al abrir Settings).

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

Tres métodos con comportamientos distintos:

**`Discover()`** — apps realmente instaladas, para uso en runtime:
- *macOS*: solo consulta el caché de `ApplicationSearch` (`appSearch.Find(name)`). Si la app no está en el caché (p.ej. `Start()` aún no terminó), no se devuelve aunque esté instalada. Sin fallback a disco.
- *Windows*: caché primero; si no está, comprueba `File.Exists()` en las rutas hardcodeadas.
- *Linux*: devuelve lista vacía (no implementado).

**`GetCandidatePaths()`** — lista completa de candidatos para el picker de Settings:
- *macOS*: todos los browsers/terminales conocidos. Usa la ruta del caché si está disponible; si no, devuelve la ruta por defecto (`/Applications/Nombre.app`) sin verificar si existe. El picker puede mostrar apps no instaladas.
- *Windows*: caché primero; si no, primera ruta hardcodeada que exista en disco; si ninguna, la primera hardcodeada sin verificar.

**`Resolve(string name)`** — método **estático**, sin dependencia de `ApplicationSearch`. Comprueba disco directamente. Lo usa `UserSettings.ActiveBrowser` / `ActiveTerminal`. Funciona aunque el caché esté vacío (arranque temprano, Settings antes de que `Start()` termine).

`TerminalDiscovery` también busca en `/System/Applications/Utilities` (donde vive `Terminal.app` en macOS).

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

Los tipos están en `SharpHook.Data`, **no** en `SharpHook.Native`. El modificador de teclas es `EventMask`, **no** `ModifierMask`.

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
