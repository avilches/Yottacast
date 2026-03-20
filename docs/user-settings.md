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
| `SearchFolders` | `List<string>` | `PlatformProvider.DefaultSearchFolders()` de cada plataforma |
| `AppDirectories` | `List<string>` | `PlatformProvider.DefaultAppDirectories()` de cada plataforma |

**Browser/Terminal preferido**: el usuario elige entre los detectados por `BrowserDiscovery`/`TerminalDiscovery` (solo apps instaladas). Se muestra en `SettingsWindowViewModel`.

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

## SettingsWindowViewModel — fallback adicional en la UI

Tras llamar `EnsureIntegrity()`, el ViewModel inicializa los pickers con un segundo nivel de seguridad:

- Si `settings.Browser` no está en la lista descubierta → usa el primer browser descubierto.
- Si `settings.Terminal` no está en la lista descubierta → usa el primer terminal descubierto.
- Si `settings.Theme` no coincide con ningún tema cargado → usa el primer tema disponible.

Este fallback cubre casos en que la lista de pickers difiere del valor guardado (p.ej. browser instalado pero no en el picker actual).

## Gotchas

- **`Save()` silencia excepciones**: la escritura a disco está envuelta en try-catch. Si falla (permisos, disco lleno, etc.), registra un warning con `LogWarning` y continúa. Los cambios se mantienen en memoria pero no se persisten en disco hasta que un `Save()` posterior tenga éxito.
