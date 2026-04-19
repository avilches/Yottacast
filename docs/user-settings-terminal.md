# Terminal del usuario

El usuario puede configurar su terminal preferido en los ajustes de Yottacast. La aplicacion detecta los terminales instalados, permite seleccionar uno y ejecuta comandos en el.

---

## 1. Deteccion de terminales instalados

La aplicacion mantiene una lista de terminales conocidos por plataforma. Al poblar el selector de Settings, se busca cada nombre en dos fuentes por orden de prioridad:

1. **Cache de aplicaciones** -- si `ApplicationSearch` ya indexo la app, se usa esa ruta.
2. **Rutas de fallback en disco** -- si la cache no contiene el terminal, se comprueban rutas predefinidas con `File.Exists`. Las rutas con wildcards (`*`) se descartan antes de la comprobacion.

Solo se muestran al usuario los terminales que realmente existen en disco.

### Terminales conocidos por plataforma

| Plataforma | Terminales conocidos |
|------------|---------------------|
| macOS | Terminal, iTerm, Warp, Alacritty, Kitty, Hyper, WezTerm, Tabby |
| Windows | Windows Terminal, PowerShell, Command Prompt, Git Bash |
| Linux | (ninguno -- no implementado) |

### Invariantes

- El usuario nunca ve un terminal en el selector que no tenga ruta resuelta (se filtran entradas con ruta vacia o nula).
- En macOS, `TerminalFallbackPaths` es un diccionario vacio: toda la deteccion depende exclusivamente de la cache de `ApplicationSearch`. Si la cache no esta poblada, no se detecta ningun terminal por fallback.
- En Windows, las rutas de fallback incluyen wildcards para Windows Terminal (`Microsoft.WindowsTerminal*\wt.exe`); estas se filtran correctamente y solo aplican las rutas literales.
- En Linux, las listas de terminales conocidos estan vacias. `Discover()` devuelve lista vacia.

> **Verificar en:**
> - `TerminalDiscovery.Discover()` y `TerminalDiscovery.GetCandidatePaths()` en `Yottacast.Core/Services/TerminalDiscovery.cs`
> - `KnownTerminalNames`, `TerminalFallbackPaths` en cada `*PlatformProvider.cs`

---

## 2. Auto-reparacion del terminal configurado

Cuando se accede al terminal activo (propiedad `ActiveTerminal` en `UserSettings`), el sistema verifica que el terminal guardado siga existiendo en disco. Si no existe:

1. Se itera la lista de terminales conocidos en orden.
2. Se selecciona el primero que tenga alguna ruta valida en disco (comprobando tanto `File.Exists` como `Directory.Exists`).
3. Se actualiza el ajuste y se persiste automaticamente.
4. Si ningun terminal existe, se devuelve `null`.

Este proceso usa `TerminalDiscovery.Resolve()`, un metodo estatico que NO depende de la cache de `ApplicationSearch`. Comprueba disco directamente via `GetTerminalPaths()`.

### Invariantes

- La auto-reparacion siempre ocurre antes de que el terminal se use, al acceder a `ActiveTerminal`.
- `EnsureIntegrity()` fuerza la auto-reparacion tanto del browser como del terminal al acceder a los ajustes.
- Si el terminal preferido sigue existiendo, no se modifica el ajuste ni se guarda.

### Limitacion conocida (macOS)

Las rutas devueltas por `GetTerminalPaths` en macOS incluyen `$HOME/Applications/{name}.app` como string literal. Sin embargo, `$HOME` no se expande a la ruta real del usuario (no se llama a `ExpandPath`). En la practica, un terminal instalado en `~/Applications/` solo se encontrara si ya esta en la cache de `ApplicationSearch`, no por comprobacion directa en disco.

> **Verificar en:**
> - `UserSettings.ActiveTerminal` en `Yottacast.Core/Services/UserSettings.cs`
> - `TerminalDiscovery.Resolve()` en `Yottacast.Core/Services/TerminalDiscovery.cs`
> - `MacOsPlatformProvider.GetTerminalPaths()` en `Yottacast.Core/Platform/MacOsPlatformProvider.cs`

---

## 3. Ejecucion de comandos en el terminal

Una vez resuelto el terminal, la aplicacion puede ejecutar comandos en el. El lanzamiento se delega a cada `PlatformProvider`, que implementa estrategias distintas segun el terminal.

### macOS

| Terminal | Estrategia | Escaping |
|----------|-----------|----------|
| Terminal | AppleScript: `tell application "Terminal" to do script "..."` | `\` -> `\\`, `"` -> `\"` (EscapeAppleScript) |
| iTerm | AppleScript: `create window with default profile command "..."` | Mismo EscapeAppleScript |
| Warp | URL scheme: `warp://action/new_tab?command=<encoded>`, lanzado con `open` | `Uri.EscapeDataString` |
| Otros (Alacritty, Kitty, etc.) | Script `.command` temporal: se escribe en disco, se marca ejecutable con `chmod +x`, se abre con `open -a <nombre>` | Sin escaping adicional |

Nota: el archivo temporal `.command` no se elimina tras su uso.

### Windows

| Terminal | Argumentos | Escaping |
|----------|-----------|----------|
| PowerShell | `-NoExit -Command "<comando>"` | `"` -> `\"` |
| Command Prompt | `/K "<comando>"` | Sin escaping adicional |
| Otros (Windows Terminal, Git Bash) | Comando tal cual | Sin modificaciones |

Si no se encuentra una ruta valida para el ejecutable, el metodo retorna silenciosamente sin error.

### Linux

No implementado. El metodo `ExecuteCommand` tiene cuerpo vacio (no-op).

> **Verificar en:**
> - `MacOsPlatformProvider.ExecuteCommand()` en `Yottacast.Core/Platform/MacOsPlatformProvider.cs`
> - `WindowsPlatformProvider.ExecuteCommand()` en `Yottacast.Core/Platform/WindowsPlatformProvider.cs`
> - `LinuxPlatformProvider.ExecuteCommand()` en `Yottacast.Core/Platform/LinuxPlatformProvider.cs`

---

## 4. CLI de diagnostico

`Yottacast.Cli` expone el subcomando `terminals` (alias `t`) que llama a `GetCandidatePaths()` e imprime cada terminal con su ruta, marcando cuales existen en disco y cuales no. Util para depurar la deteccion sin arrancar la GUI.

> **Verificar en:**
> - `CmdTerminals()` en `Yottacast.Cli/Program.cs`

---

## 5. Persistencia

El terminal seleccionado se guarda en el archivo de settings como la propiedad JSON `"terminal"`, cuyo valor es el nombre logico del terminal (p. ej. `"Warp"`, `"iTerm"`). Al cargar settings, si el valor esta vacio se deja vacio y la auto-reparacion asignara el primer terminal disponible en el primer acceso a `ActiveTerminal`.

> **Verificar en:**
> - `UserSettingsData.Terminal` y `UserSettings.Load()` en `Yottacast.Core/Services/UserSettings.cs`
