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

Ver `docs/user-settings.md` §Auto-reparación para el flujo completo de `ActiveBrowser` y `EnsureIntegrity()`.

**`OpenUrl()`** — método de instancia que delega en `platform.OpenUrl()`.

## Lanzamiento por plataforma

**macOS `OpenUrl`** — llama `open -a <browserName> <url>`, pasando el nombre de visualización del browser. El OS resuelve el bundle.

**Windows `OpenUrl`** — resuelve la ruta del exe desde `GetBrowserPaths` en el momento de la llamada (usando `File.Exists`); retorna silenciosamente si no se encuentra. Lanza el exe directamente con la URL como argumento.
