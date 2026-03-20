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
| `Theme` | string | `"dark-default"` |
| `SearchFolders` | `List<string>` | Downloads, Desktop, Documents, Movies/Videos, Pictures (+ proveedores cloud: Dropbox, Google Drive, OneDrive, iCloud, etc.) |
| `AppDirectories` | `List<string>` | `/Applications`, `$HOME/Applications`, `/System/Applications` (macOS) / `Program Files` (Win) / `.desktop` dirs (Linux) |

**Browser/Terminal preferido**: el usuario elige entre los detectados por `BrowserDiscovery`/`TerminalDiscovery` (solo apps instaladas). Se muestra en `SettingsWindowViewModel`.

**Detección del browser predeterminado del sistema** ⚠️ TODO: no implementado. El default es `""` y se selecciona el primero de la lista de `BrowserDiscovery`.

## API de ciclo de vida

`UserSettings.Load(platform, logger?)` carga el JSON (o crea defaults si no existe), y siempre llama `Save()` al final — el fichero se reescribe en cada arranque. `settings.Save()` puede llamarse manualmente; también se llama automáticamente al cambiar cada campo en SettingsWindow.

## Rutas en el JSON

`SearchFolders` y `AppDirectories` se almacenan en crudo (`$HOME/Downloads`, `~/foo`, rutas absolutas…). La expansión `$HOME/` → ruta absoluta ocurre en el momento de uso, nunca al cargar ni guardar. `PlatformProvider.ExpandPath()` gestiona `$HOME/` y `~/`. Las propiedades `ExpandedSearchFolders` / `ExpandedAppDirectories` devuelven las listas expandidas; los consumidores (`UserDocumentSearch`, `ApplicationSearch`) las usan directamente.

## Detección automática de tema

Si el campo `"theme"` no está en el JSON (archivo nuevo o borrado), `Load` llama `platform.DefaultTheme()` que consulta el modo oscuro del SO una vez de forma síncrona. En macOS: `defaults read -g AppleInterfaceStyle`. En Windows: registro. En Linux: `gsettings`. Si falla, usa `"dark-default"`.

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
