# BrowserDiscovery

`BrowserDiscovery` encapsula la detección y el lanzamiento del navegador configurado por el usuario en sus settings. El navegador se usa para abrir búsquedas de Google desde el resultado de tipo Google suggestion.

## Descubrimiento e instancias

Inyecta `ApplicationSearch` y `PlatformProvider`. Los datos de navegadores conocidos vienen de `platform.KnownBrowserNames`, `platform.BrowserFallbackPaths` y `platform.GetBrowserPaths`.

**`Discover()`** — apps instaladas para poblar el picker de Settings:
- Consulta el caché de `ApplicationSearch`. Si no encuentra el nombre, usa `platform.BrowserFallbackPaths` para comprobar disco con `File.Exists()`.
- *Linux*: devuelve lista vacía (no implementado).
- *macOS*: `BrowserFallbackPaths` es un diccionario vacío, así que en macOS `Discover()` se apoya exclusivamente en el caché de `ApplicationSearch`; no hay fallback a disco.

**`DiscoverAsync()`** — wrapper no-op: devuelve `Task.FromResult(Discover())`. Existe para uniformidad de la API.

**`GetCandidatePaths()`** — lista completa para el picker de Settings:
- Usa el caché si está disponible; si no, usa `platform.GetBrowserPaths(name)` y toma la primera ruta. Este es un data source distinto al de `Discover()`: en macOS, `GetBrowserPaths` devuelve rutas convencionales en `/Applications`, mientras que `BrowserFallbackPaths` está vacío.
- Filtra entradas cuya ruta sea null o vacía. Puede mostrar apps no instaladas.

**`Resolve(string name, PlatformProvider)`** — método estático para auto-reparación de `UserSettings`, sin dependencia de `ApplicationSearch`. Comprueba disco directamente vía `platform.GetBrowserPaths()`, aceptando tanto `Directory.Exists(p)` como `File.Exists(p)` (OR). Funciona aunque el caché esté vacío.

- Si `preferredName` está vacío o es nulo, salta directamente al fallback e itera `KnownBrowserNames` en orden hasta encontrar uno en disco.
- Si `preferredName` no está en disco, también itera `KnownBrowserNames` completo (incluyendo el propio `preferredName` de nuevo si aparece en la lista). Devuelve el primero encontrado.
- Devuelve `null` si ningún browser conocido existe en disco.
- Cuando `GetBrowserPaths` devuelve varias rutas para un mismo nombre (Windows), toma la primera que exista — `Directory.Exists || File.Exists`.

Ver `docs/user-settings.md` §Auto-reparación para el flujo completo de `ActiveBrowser` y `EnsureIntegrity()`.

**`OpenUrl()`** — método de instancia que delega en `platform.OpenUrl()`. Recibe un `BrowserInfo` (name + executable path); solo usa el `Name` para la llamada al platform.

## `BrowserInfo`

`BrowserInfo` es un `record` con dos campos: `Name` (nombre de visualización, e.g. `"Google Chrome"`) y `ExecutablePath` (ruta en disco). Es el tipo de retorno de `Discover()`, `DiscoverAsync()` y `Resolve()`, y es lo que `UserSettings.ActiveBrowser` expone al resto de la app.

## Integración con la capa de presentación

- `SettingsWindowViewModel` llama a `browserDiscovery.Discover()` al construirse para poblar la lista de navegadores del picker. Solo aparecen navegadores realmente instalados.
- `MainWindowViewModel` accede a `settings.ActiveBrowser` en el `OnActivate` del resultado Google. Si `ActiveBrowser` devuelve `null` (ningún navegador disponible), la acción retorna sin hacer nada; no lanza excepción.
- `UserSettings.ActiveBrowser` tiene efecto secundario: si el nombre guardado ya no existe en disco pero hay otro disponible, actualiza `Browser` y llama a `Save()` — el cambio se persiste en ese momento, no en un paso de reparación separado.

## Lanzamiento por plataforma

**macOS `OpenUrl`** — llama `open -a <browserName> <url>`, pasando el nombre de visualización del browser. El OS resuelve el bundle. Excepciones se tragan silenciosamente (`catch { }`).

**Windows `OpenUrl`** — resuelve la ruta del exe desde `GetBrowserPaths` en el momento de la llamada (usando `File.Exists`); retorna silenciosamente si no se encuentra. Lanza el exe directamente con la URL como argumento. Excepciones se tragan silenciosamente.

**Linux `OpenUrl`** — no implementado: el método es un no-op.

## Datos de plataforma

**macOS** — `KnownBrowserNames` es una lista fija de nombres de app (ver `MacOsPlatformProvider`). `GetBrowserPaths` genera rutas convencionales en `/Applications/<name>.app` y `$HOME/Applications/<name>.app` — las rutas contienen el literal `$HOME` sin expandir, ya que `Resolve` usa `Directory.Exists`/`File.Exists` directamente (el OS no expande `$HOME`). `BrowserFallbackPaths` está vacío.

**Windows** — `KnownBrowserNames` se deriva de las claves de `_browserFallbackPaths`. `GetBrowserPaths` y `BrowserFallbackPaths` devuelven las mismas rutas absolutas (son la misma fuente). Los nombres de browser en Windows incluyen `"Mozilla Firefox"` (con nombre completo), a diferencia de macOS donde es `"Firefox"`.

**Linux** — todas las propiedades devuelven colecciones vacías; browser y terminal no están implementados.
