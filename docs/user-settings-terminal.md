# TerminalDiscovery

`TerminalDiscovery` encapsula la detección del terminal configurado por el usuario en sus settings y el lanzamiento de comandos en él. El terminal se define en settings y su infraestructura está implementada, pero actualmente no se invoca desde ninguna acción de búsqueda — está pendiente de uso funcional.

Los métodos `Discover()`, `DiscoverAsync()` y `Resolve()` devuelven instancias de `TerminalInfo`, un record con dos campos: `Name` (nombre lógico del terminal, p. ej. `"Warp"`) y `ExecutablePath` (ruta al ejecutable o bundle en disco).

## Descubrimiento e instancias

Inyecta `ApplicationSearch` y `PlatformProvider`. Los datos de terminales conocidos vienen de `platform.KnownTerminalNames`, `platform.TerminalFallbackPaths` y `platform.GetTerminalPaths`.

**`Discover()`** — apps instaladas para poblar el picker de Settings:
- Consulta el caché de `ApplicationSearch`. Si no encuentra el nombre, usa `platform.TerminalFallbackPaths` para comprobar disco con `File.Exists()`.
- Añade un filtro adicional frente a `BrowserDiscovery`: descarta rutas que contengan `*` antes de llamar a `File.Exists()`. Esto es necesario porque en Windows, `TerminalFallbackPaths` incluye rutas con wildcard como `Microsoft.WindowsTerminal*\wt.exe`.
- *Linux*: devuelve lista vacía (no implementado).
- *macOS*: `TerminalFallbackPaths` es un diccionario vacío, así que en macOS `Discover()` se apoya exclusivamente en el caché de `ApplicationSearch`; no hay fallback a disco.

**`DiscoverAsync()`** — wrapper no-op: devuelve `Task.FromResult(Discover())`. Existe para uniformidad de la API.

**`GetCandidatePaths()`** — lista completa para el picker de Settings:
- Usa el caché si está disponible; si no, usa `platform.GetTerminalPaths(name)` y toma la primera ruta.
- Filtra entradas cuya ruta sea null o vacía. Puede mostrar apps no instaladas.

**`Resolve(string name, PlatformProvider)`** — método estático para auto-reparación de `UserSettings`, sin dependencia de `ApplicationSearch`. Comprueba disco directamente vía `platform.GetTerminalPaths()`, aceptando tanto `Directory.Exists(p)` como `File.Exists(p)` (OR). Funciona aunque el caché esté vacío.
- Si el nombre preferido no existe en disco, itera `platform.KnownTerminalNames` en orden y devuelve el primero que tenga alguna ruta presente en disco. Si ninguno existe, devuelve `null`.

Ver `docs/user-settings.md` §Auto-reparación para el flujo completo de `ActiveTerminal` y `EnsureIntegrity()`.

**`ExecuteCommand()`** — método de instancia que delega en `platform.ExecuteCommand()`.

**CLI de diagnóstico** — `Yottacast.Cli` expone el subcomando `terminals` (alias `t`) que llama a `GetCandidatePaths()` e imprime los terminales detectados, útil para depurar sin arrancar la GUI.

## Lanzamiento por plataforma

**macOS** (`MacOsPlatformProvider.ExecuteCommand`): tres estrategias distintas según el terminal:
- `Terminal` — AppleScript `do script`. El comando se escapa con `EscapeAppleScript` (`\` → `\\`, `"` → `\"`) antes de inyectarlo en el script.
- `iTerm` — AppleScript `create window with default profile command`. Mismo escape con `EscapeAppleScript`.
- `Warp` — URL scheme `warp://action/new_tab?command=<encoded>`, lanzado vía `open`. El comando se codifica con `Uri.EscapeDataString`; no se aplica el escape de AppleScript.
- Todos los demás (Alacritty, Kitty, WezTerm…) — fallback: escribe un script `.command` temporal en disco (vía `Path.GetTempFileName() + ".command"`), lo hace ejecutable con `chmod +x` y lo abre con `open -a <terminalName>`. La extensión `.command` hace que las apps compatibles con el Terminal de macOS lo ejecuten en una nueva ventana. El archivo temporal no se elimina tras su uso.

Gotcha de `GetTerminalPaths` en macOS: la tercera ruta candidata está declarada como string literal en lugar de string interpolado, por lo que el literal `{name}` nunca se sustituye. En la práctica, la ruta `/System/Applications/Utilities/Terminal.app` nunca se genera correctamente por esta vía; Terminal.app se encuentra solo si está en `/Applications/` o en `~/Applications/`, o si ya existe en el caché de `ApplicationSearch`.

**Windows** (`WindowsPlatformProvider.ExecuteCommand`): resuelve la ruta del exe desde `TerminalFallbackPaths`, filtrando rutas con wildcard. Wrapping de argumentos según terminal:
- `PowerShell` → `-NoExit -Command "<command>"`. Las comillas dobles del comando se escapan (`"` → `\"`).
- `Command Prompt` → `/K "<command>"`. El comando se inyecta sin escaping adicional.
- Otros (`Windows Terminal`, `Git Bash`) → el comando se pasa tal cual, sin modificaciones.
- Retorna silenciosamente si no se encuentra una ruta válida.

**Linux** (`LinuxPlatformProvider.ExecuteCommand`): no implementado — el método es un no-op (cuerpo vacío, sin log ni error). `KnownTerminalNames`, `TerminalFallbackPaths` y `GetTerminalPaths` también devuelven colecciones vacías.
