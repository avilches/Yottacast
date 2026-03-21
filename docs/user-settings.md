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

**Hotkey**: combinación de teclas para mostrar/ocultar el launcher. Formato: modificadores separados por `+` seguidos de la tecla, p. ej. `"Alt+Space"`, `"Ctrl+Shift+A"`. Modificadores reconocidos: `Alt`, `Ctrl`, `Shift`, `Meta`. El campo se edita desde SettingsWindow: el usuario hace clic en el campo GLOBAL HOTKEY, pulsa la combinación deseada y se guarda automáticamente. ESC o clic fuera del campo cancela sin guardar. El cambio tiene efecto inmediato, sin reiniciar.

**Detección del browser predeterminado del sistema** ⚠️ TODO: no implementado. El default es `""` y se selecciona el primero de la lista de `BrowserDiscovery`.

## API de ciclo de vida

`UserSettings.Load(platform, logger?)` carga el JSON (o crea defaults si no existe), y siempre llama `Save()` al final — el fichero se reescribe en cada arranque. `settings.Save()` puede llamarse manualmente; también se llama automáticamente al cambiar cada campo en SettingsWindow.

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

`platform.DefaultTheme()` elige un tema oscuro o claro según el modo del sistema. `Load()` lo llama solo si el campo `theme` en el JSON está ausente o vacío. Los temas concretos y la lógica de detección por plataforma están en `PlatformProvider.DefaultTheme()` de cada implementación.

**Doble default para Theme**: `UserSettingsData` (el DTO de serialización) usa `""` como default para `Theme`, mientras que `UserSettings` (la clase de dominio) aplica `platform.DefaultTheme()` si el valor cargado es vacío. Esta distinción permite que el JSON omita el campo en la primera ejecución y que la lógica de plataforma elija el tema correcto sin que el DTO tenga dependencia de `PlatformProvider`.

## Auto-reparación de Browser y Terminal

`UserSettings` se auto-repara sin depender de `ApplicationSearch`:

- **`ActiveBrowser`** / **`ActiveTerminal`** — se evalúan en cada acceso (son propiedades, no campos). Llaman a `BrowserDiscovery.Resolve` / `TerminalDiscovery.Resolve` (método estático, comprueba disco):
  1. Si `Browser` / `Terminal` no está vacío → busca ese nombre concreto en disco.
  2. Si no existe (o el campo era `""`): itera `KnownBrowserNames` / `KnownTerminalNames` y devuelve el primero encontrado en disco.
  3. Si ninguno existe en disco → devuelve `null`.
  - Auto-reparación: si el nombre guardado no existe pero Resolve encuentra un alternativo (`resolved.Name != Browser`), actualiza el campo y llama `Save()`. Si `Browser = ""`, devuelve el primero disponible sin tocar el JSON.
  - **Devuelve `null`** solo cuando ningún browser/terminal conocido está instalado en el sistema.
- **`EnsureIntegrity()`** — accede a ambas propiedades, forzando la validación y el guardado si algo cambió. Llamar en puntos naturales (p.ej. al abrir Settings).

`SettingsWindowViewModel` llama `settings.EnsureIntegrity()` en su constructor, antes de inicializar los pickers. `MainWindowViewModel` usa `settings.ActiveBrowser` directamente al construir el resultado de Google.

## SettingsWindowViewModel — navegación por secciones

`SettingsWindowViewModel` usa un enum `SettingsSection` para dividir el panel en secciones: `General`, `AppSearch`, `InternetSearch`, `FileSearch`, `Calculator`, `Clipboard`, `Emoji`. La sección activa se controla con `SelectedSection` y los comandos `SelectX()` generados por `[RelayCommand]`.

## SettingsWindowViewModel — fallback adicional en la UI

Tras llamar `EnsureIntegrity()`, el ViewModel inicializa los pickers con un segundo nivel de seguridad:

- Si `settings.Browser` no está en la lista descubierta → usa el primer browser descubierto.
- Si `settings.Terminal` no está en la lista descubierta → usa el primer terminal descubierto.
- Si `settings.Theme` no coincide con ningún tema cargado → usa el primer tema disponible.

Este fallback cubre casos en que la lista de pickers difiere del valor guardado (p.ej. browser instalado pero no en el picker actual).

## Gotchas

- **`Save()` silencia excepciones**: la escritura a disco está envuelta en try-catch. Si falla (permisos, disco lleno, etc.), registra un warning con `LogWarning` y continúa. Los cambios se mantienen en memoria pero no se persisten en disco hasta que un `Save()` posterior tenga éxito.
