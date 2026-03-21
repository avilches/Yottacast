# BrowserDiscovery / TerminalDiscovery / FileSearch

## BrowserDiscovery / TerminalDiscovery

Inyectan `ApplicationSearch` y `PlatformProvider`. Los datos de browsers/terminales conocidos vienen de `platform.KnownBrowserNames` / `platform.BrowserFallbackPaths` etc.

Tres métodos con comportamientos distintos:

**`Discover()`** — apps realmente instaladas:
- Consulta caché de `ApplicationSearch`. Si no está en caché, usa `platform.BrowserFallbackPaths` para comprobar disco con `File.Exists()`.
- `TerminalDiscovery.Discover()` añade un filtro adicional: descarta rutas que contengan `*` antes de llamar a `File.Exists()`. Esto es necesario porque en Windows, `TerminalFallbackPaths` incluye rutas con wildcard como `Microsoft.WindowsTerminal*\wt.exe`. `BrowserDiscovery.Discover()` no aplica ese filtro porque `BrowserFallbackPaths` nunca contiene wildcards.
- *Linux*: devuelve lista vacía (no implementado).

**`GetCandidatePaths()`** — lista completa para el picker de Settings:
- Usa caché si disponible; si no, usa `platform.GetBrowserPaths(name)` / `platform.GetTerminalPaths(name)` (no `BrowserFallbackPaths`/`TerminalFallbackPaths`) y toma la primera ruta. Este es un data source distinto al de `Discover()`: en macOS, `GetBrowserPaths` devuelve rutas convencionales en `/Applications`, mientras que `BrowserFallbackPaths` está vacío.
- Filtra entradas cuya ruta sea null o vacía. Puede mostrar apps no instaladas.

**`Resolve(string name, PlatformProvider)`** — método **estático**, sin dependencia de `ApplicationSearch`. Comprueba disco directamente vía `platform.GetBrowserPaths()` / `platform.GetTerminalPaths()`, aceptando tanto `Directory.Exists(p)` como `File.Exists(p)` (OR). Lo usa `UserSettings.ActiveBrowser` / `ActiveTerminal`. Funciona aunque el caché esté vacío.

**`OpenUrl()` / `ExecuteCommand()`** — métodos de instancia que delegan en `platform.OpenUrl()` / `platform.ExecuteCommand()`.

## Terminal launch per app

Each terminal uses a platform-specific launch method (AppleScript, URL scheme, or `.command` file); see `MacOsPlatformProvider.ExecuteInTerminalAsync`.

Each Windows terminal receives the command with terminal-specific argument wrapping; see `WindowsPlatformProvider.ExecuteInTerminalAsync`.

## FileSearch

Clase instancia (no estática) en `Search/UserDocuments/FileSearch.cs`. Delega en `platform.SearchFilesAsync()`.
- **macOS** → Spotlight vía `SpotlightInterop.Query`. macOS builds a Spotlight predicate for the query (see `MacOsPlatformProvider.SearchFilesAsync`); the `cd` suffix makes matching case- and diacritic-insensitive.
- **Windows** → delegates to PowerShell for file search (see `WindowsPlatformProvider.SearchFilesAsync`).
- **Linux** → `plocate` o `locate -b`

API: `fileSearch.SearchAsync(query, onResult, maxResults, searchFolders, ct)`.
`onResult` es un callback `Action<FileResult>` — los resultados llegan conforme el proceso los emite.
