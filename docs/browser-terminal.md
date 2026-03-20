# BrowserDiscovery / TerminalDiscovery / FileSearch

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

## Terminal launch per app

### macOS
- **Terminal.app** → AppleScript `do script`
- **iTerm** → AppleScript `create window with default profile command`
- **Warp** → URL scheme `warp://action/new_tab?command=...`
- **Resto** → genera `.command` temporal con `chmod +x` y lo abre con `open -a`

### Windows
- **PowerShell** → `-NoExit -Command`
- **CMD** → `/K`
- **Windows Terminal** y **Git Bash** → caso `default`: el comando se pasa directamente sin wrapper

## FileSearch

Clase instancia (no estática). Delega en `platform.SearchFilesAsync()`.
- **macOS** → `mdfind` con predicado `kMDItemFSName == '*query*'cd` (Spotlight, case-insensitive)
- **Windows** → PowerShell + ADODB.Connection (`Provider=Search.CollatorDSO`)
- **Linux** → `plocate` o `locate -b`

API: `fileSearch.SearchAsync(query, onResult, maxResults, searchFolders, ct)`.
`onResult` es un callback `Action<FileResult>` — los resultados llegan conforme el proceso los emite.
