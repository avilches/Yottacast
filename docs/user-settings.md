# UserSettings

Clase: `Yottacast.Core.Services.UserSettings`

Persiste en JSON. Todos los campos tienen defaults multiplataforma; nunca lanza excepción.

## Ruta del fichero

- macOS: `~/Library/Application Support/Yottacast/settings.json` (usa `SpecialFolder.ApplicationData`)
- Windows: `%APPDATA%\Yottacast\settings.json`

## Campos

| Campo | Tipo | Default |
|---|---|---|
| `Browser` | string | `""` (auto-selecciona el primero disponible) |
| `Terminal` | string | `""` |
| `Theme` | string | ver §Detección automática de tema |
| `Hotkey` | string | `"Alt+Space"` |
| `SearchFolders` | `List<string>` | `PlatformProvider.DefaultSearchFolders()` de cada plataforma |
| `AppDirectories` | `List<string>` | `PlatformProvider.DefaultAppDirectories()` de cada plataforma |
| `EnableCalculator` | bool | `true` |
| `EnableClipboard` | bool | `true` |
| `EnableEmoji` | bool | `true` |
| `LastLaunchedVersion` | string | `""` |

Los tres toggles `EnableCalculator`, `EnableClipboard` y `EnableEmoji` están expuestos en el SettingsWindow y se persisten en el JSON, pero actualmente no tienen efecto funcional sobre los resultados de búsqueda — las fuentes correspondientes se registran siempre en DI con independencia de su valor. `LastLaunchedVersion` se usa para detectar actualizaciones y ejecutar migraciones; ver el paso `RunMigrations` en `docs/app-design.md`.

**Browser/Terminal preferido**: el usuario elige entre los detectados por `BrowserDiscovery`/`TerminalDiscovery` (solo apps instaladas). Se muestra en `SettingsWindowViewModel`.

**Hotkey**: combinación de teclas para mostrar/ocultar el launcher. El valor en memoria se parsea de forma lazy a un `HotkeyConfig` cacheado en `ParsedHotkey`; el cache se invalida cada vez que se asigna el setter de `Hotkey`. El campo se edita desde SettingsWindow: el usuario hace clic en el campo GLOBAL HOTKEY, pulsa la combinación deseada y se guarda automáticamente. ESC o clic fuera del campo cancela sin guardar. El cambio tiene efecto inmediato, sin reiniciar.

**Detección del browser predeterminado del sistema** ⚠️ TODO: no implementado. El default es `""` y se selecciona el primero de la lista de `BrowserDiscovery`.

## HotkeyConfig

`HotkeyConfig` (en `Yottacast.Core/Platform/HotkeyConfig.cs`) es un record inmutable con campos `bool Alt, Ctrl, Shift, Meta` y `string KeyName`. Se usa tanto para registrar la hotkey global en SharpHook como para mostrarla en la UI.

- **`Parse(string?)`** — parsea cadenas como `"Alt+Space"` o `"Ctrl+Shift+F1"`. Es case-insensitive y acepta alias: `Option`/`Options` → Alt; `Control` → Ctrl; `Cmd`/`Command`/`Win`/`Windows` → Meta. Si no hay ningún token que no sea modificador, devuelve `null`.
- **`ToString()`** — produce la forma canónica con modificadores en orden fijo `Ctrl→Alt→Shift→Meta`, luego la tecla. Ejemplo: `"Ctrl+Alt+Space"`.
- **`Default`** — `Alt+Space`.
- **`UserSettings.ParsedHotkey`** — lazy-parsea `Hotkey` la primera vez que se accede y cachea el resultado; el setter de `Hotkey` invalida el caché poniendo `_parsedHotkey = null`. Si `Parse` devuelve `null` (hotkey inválida), `ParsedHotkey` devuelve `HotkeyConfig.Default`.

## Flujo de captura de hotkey en SettingsWindow

El code-behind (`SettingsWindow.axaml.cs`) coordina dos handlers de puntero:

1. **`OnHotkeyAreaPointerPressed`** — se dispara al hacer clic sobre el área de hotkey; llama `StartHotkeyCapture()` y marca `e.Handled = true` para que el evento no burbujee al handler de ventana.
2. **`OnPointerPressed` (override de ventana)** — cancela la captura si está activa y el clic no fue sobre el área de hotkey (porque ese caso ya consumió el evento con `Handled`).
3. **`ProcessKeyCapture`** en el ViewModel — ignora pulsaciones de teclas modificadoras solas (Alt, Ctrl, Shift, Meta/Win); ESC cancela restaurando el valor guardado; cualquier otra tecla construye un `HotkeyConfig`, lo serializa y llama `Save()`.

`HotkeyDisplayText` es la propiedad que muestra el texto en la UI: mientras `IsCapturingHotkey` es `true` muestra `"Press keys…"`, y en caso contrario muestra `HotkeyText`.

## API de ciclo de vida

`UserSettings.Load(platform, logger?)` carga el JSON (o crea defaults si no existe), y siempre llama `Save()` al final — el fichero se reescribe en cada arranque. `settings.Save()` puede llamarse manualmente; también se llama automáticamente al cambiar cada campo en SettingsWindow.

`Load()` acepta un parámetro opcional `settingsPath` que sobreescribe la ruta por defecto; en tests se usa para apuntar a un fichero temporal sin tocar el fichero real del usuario. Ante cualquier excepción durante la carga (fichero no encontrado, JSON malformado), registra un mensaje de nivel `LogInformation` (no warning) y crea los defaults de plataforma, sin propagar la excepción. Si la deserialización devuelve `null`, también crea los defaults silenciosamente (sin log adicional, ya que la línea anterior habría logueado "Settings loaded from…").

### Condiciones de aplicación de defaults en Load()

Los defaults de plataforma se aplican de forma selectiva, no globalmente:

- **`Theme`**: usa `platform.DefaultTheme()` solo si el valor cargado del JSON es null o vacío (`""`).
- **`SearchFolders`** y **`AppDirectories`**: usan los defaults de plataforma solo si la lista cargada es null o está vacía (0 elementos). Si el JSON contiene aunque sea un elemento, se respeta tal cual.
- **`Browser`** y **`Terminal`**: no tienen default — se cargan tal cual desde el JSON (pueden ser `""`). La selección del primero disponible ocurre en `ActiveBrowser`/`ActiveTerminal`, no en `Load()`.

## Rutas en el JSON

`SearchFolders` y `AppDirectories` se almacenan en crudo (`$HOME/Downloads`, `~/foo`, rutas absolutas…). La expansión `$HOME/` → ruta absoluta ocurre en el momento de uso, nunca al cargar ni guardar. `PlatformProvider.ExpandPath()` gestiona la expansión. Las propiedades `ExpandedSearchFolders` / `ExpandedAppDirectories` devuelven las listas expandidas; los consumidores (`UserDocumentSearch`, `ApplicationSearch`) las usan directamente.

### ExpandPath() — casos soportados

- `$HOME` o `~` → directorio home del usuario
- `$HOME/path` o `~/path` → home + path
- Cualquier otro valor → devuelto sin modificación

## Detección automática de tema

`platform.DefaultTheme()` elige un tema oscuro o claro según el modo del sistema. `Load()` lo llama solo si el campo `theme` en el JSON está ausente o vacío. La implementación base de `DefaultTheme()` en `PlatformProvider` llama a `IsSystemDarkMode()` y devuelve `"dark-default"` si el valor es `true` o `null`, y `"light-gray"` si es `false`. Las subclases de plataforma pueden sobrescribir `DefaultTheme()` o solo `IsSystemDarkMode()`.

**Doble default para Theme**: `UserSettingsData` (el DTO de serialización) usa `""` como default para `Theme`, mientras que `UserSettings` (la clase de dominio) aplica `platform.DefaultTheme()` si el valor cargado es vacío. Esta distinción permite que el JSON omita el campo en la primera ejecución y que la lógica de plataforma elija el tema correcto sin que el DTO tenga dependencia de `PlatformProvider`.

## Auto-reparación de Browser y Terminal

`UserSettings` se auto-repara sin depender de `ApplicationSearch`:

- **`ActiveBrowser`** / **`ActiveTerminal`** — se evalúan en cada acceso (son propiedades, no campos). Llaman a `BrowserDiscovery.Resolve` / `TerminalDiscovery.Resolve` (método estático, comprueba disco):
  1. Si `Browser` / `Terminal` no está vacío → busca ese nombre concreto en disco.
  2. Si no existe (o el campo era `""`): itera `KnownBrowserNames` / `KnownTerminalNames` y devuelve el primero encontrado en disco.
  3. Si ninguno existe en disco → devuelve `null`.
  - Auto-reparación: si el nombre guardado no existe pero Resolve encuentra un alternativo (`resolved.Name != Browser`), actualiza el campo y llama `Save()`. Esto incluye el caso `Browser = ""`: si Resolve encuentra un browser disponible, su nombre diferirá de `""`, por lo que también actualiza `Browser` y llama `Save()`.
  - **Devuelve `null`** solo cuando ningún browser/terminal conocido está instalado en el sistema.
- **`EnsureIntegrity()`** — accede a ambas propiedades, forzando la validación y el guardado si algo cambió. Llamar en puntos naturales (p.ej. al abrir Settings).

`SettingsWindowViewModel` llama `settings.EnsureIntegrity()` en su constructor, después de construir las listas de opciones de los pickers (`Discover()`) pero antes de leer el valor seleccionado de `settings.Browser`/`settings.Terminal`. `MainWindowViewModel` usa `settings.ActiveBrowser` directamente al construir el resultado de Google.

### BrowserDiscovery y TerminalDiscovery: dos estrategias de resolución

Ambas clases exponen dos métodos de resolución con propósitos distintos:

- **`Discover()`** — para poblar los pickers de la UI: consulta primero el caché de `ApplicationSearch` (en memoria), y si no encuentra el nombre, cae en `BrowserFallbackPaths` / `TerminalFallbackPaths` del `PlatformProvider` buscando el primer path existente en disco. Requiere que `ApplicationSearch` esté inicializado.
- **`Resolve(string, PlatformProvider)`** — estático, para auto-reparación: llama a `platform.GetBrowserPaths(name)` y comprueba existencia en disco con `File.Exists` / `Directory.Exists`. No depende del caché de apps, por lo que es seguro llamarlo en cualquier momento del ciclo de vida.
- **`GetCandidatePaths()`** — variante de `Discover()` que devuelve tuplas `(name, path)` incluyendo apps que solo tienen ruta primaria del platform provider aunque no existan en el caché; usada desde el CLI.

La diferencia clave es que `Resolve` es el único safe para ser llamado antes de que `ApplicationSearch` haya terminado su escaneo.

## SettingsWindowViewModel — navegación por secciones

`SettingsWindowViewModel` usa un enum `SettingsSection` para dividir el panel en secciones: `General`, `AppSearch`, `InternetSearch`, `FileSearch`, `Calculator`, `Clipboard`, `Emoji`. La sección activa se controla con `SelectedSection` y los comandos `SelectX()` generados por `[RelayCommand]`.

## SettingsWindowViewModel — fallback adicional en la UI

Tras llamar `EnsureIntegrity()`, el ViewModel inicializa los pickers con un segundo nivel de seguridad:

- Si `settings.Browser` no está en la lista descubierta → usa el primer browser descubierto.
- Si `settings.Terminal` no está en la lista descubierta → usa el primer terminal descubierto.
- Si `settings.Theme` no coincide con ningún tema cargado → usa el primer tema disponible.

Este fallback cubre casos en que la lista de pickers difiere del valor guardado (p.ej. browser instalado pero no en el picker actual).

Las listas `SearchFolders` y `AppDirectories` en el ViewModel son `ObservableCollection<string>`. Sus eventos `CollectionChanged` están suscritos en el constructor y, ante cualquier cambio (añadir, eliminar, reordenar), sincronizan inmediatamente la lista a `settings.SearchFolders` / `settings.AppDirectories` y llaman `Save()`. El code-behind gestiona el picker de carpetas del SO (`StorageProvider.OpenFolderPickerAsync`) y llama a `AddSearchFolder` / `AddAppDirectory` del ViewModel, que deduplican (no añaden si la ruta ya existe).

## Serialización interna

`UserSettings` usa un record privado `UserSettingsData` como DTO de serialización/deserialización JSON. Este DTO nunca se expone fuera de la clase; actúa como buffer entre el JSON en disco y la clase de dominio. Los nombres de campo JSON usan `camelCase` (p. ej. `"searchFolders"`, `"enableCalculator"`) definidos mediante `[JsonPropertyName]`. El JSON se escribe con `WriteIndented = true`.

## Ciclo de vida en DI

`UserSettings` se registra como **singleton** en el contenedor DI de `App`. La carga ocurre en el momento de construcción del contenedor (`BuildServices`), antes de que se muestre cualquier ventana. Una vez cargado, la instancia vive durante toda la vida de la aplicación; no hay recarga desde disco salvo que se destruya el contenedor.

`SettingsWindowViewModel`, en cambio, se registra como **transient**. Cada vez que se abre la ventana de Settings se crea una nueva instancia, lo que implica que el estado de la UI (sección activa, scroll position, etc.) se reinicia a `SettingsSection.General` en cada apertura.

## Apertura de la ventana de Settings

`App.OpenSettings()` espera `await appSearch.WhenReady()` antes de construir el `SettingsWindowViewModel`. Esto garantiza que `BrowserDiscovery.Discover()` / `TerminalDiscovery.Discover()` encuentren el caché de `ApplicationSearch` ya poblado cuando el usuario abre Settings. Si Settings se abre antes de que `ApplicationSearch` haya terminado su escaneo, la llamada bloquea hasta que esté listo. Si la ventana ya está visible y activa, se activa sin recrearla.

## Default de Hotkey en Load()

El campo `Hotkey` en `UserSettingsData` tiene default `"Alt+Space"`. Adicionalmente, `Load()` normaliza el valor: si `data.Hotkey` es null o vacío después de deserializar, sustituye por `"Alt+Space"`. Este comportamiento es paralelo al de `Theme`: ambos campos tienen un default de último recurso en `Load()` independiente del default del DTO.

## Logging de Save()

`Save()` registra `LogDebug` cuando el guardado tiene éxito (`"Settings saved to {Path}"`). Ante cualquier excepción registra `LogWarning`. Los mensajes de auto-reparación de browser/terminal en `ActiveBrowser`/`ActiveTerminal` se emiten a nivel `LogInformation`.

## UserSettings.Load() — constructor privado

El constructor de `UserSettings` es privado. La única vía de creación es `UserSettings.Load(...)`. Esto garantiza que toda instancia ha pasado por la lógica de carga y de `Save()` inicial.

## TerminalDiscovery.Discover() — filtro de wildcards

`TerminalDiscovery.Discover()` filtra las rutas del `TerminalFallbackPaths` que contienen `*`, ya que algunas rutas de terminal en la plataforma son patrones glob (p.ej. rutas con versiones). `BrowserDiscovery.Discover()` no tiene esta restricción.

## Gotchas

- **`Save()` silencia excepciones**: la escritura a disco está envuelta en try-catch. Si falla (permisos, disco lleno, etc.), registra un warning con `LogWarning` y continúa. Los cambios se mantienen en memoria pero no se persisten en disco hasta que un `Save()` posterior tenga éxito.
- **`Save()` crea el directorio automáticamente**: antes de escribir, llama `Directory.CreateDirectory(dir)`. Si la carpeta `Yottacast/` no existe bajo `ApplicationData`, se crea sin error.
- **`ActiveBrowser` y `ActiveTerminal` no son idempotentes en presencia de auto-reparación**: cada acceso comprueba disco y puede llamar `Save()`. En flujos críticos de rendimiento, preferir acceder una sola vez y cachear el resultado en la llamada.
