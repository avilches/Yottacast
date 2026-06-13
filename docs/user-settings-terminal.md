# Terminal del usuario

El usuario puede configurar su terminal preferido en los ajustes de Yottacast. La aplicacion detecta los terminales instalados, permite seleccionar uno y ejecuta comandos en el.

---

## 1. Deteccion de terminales instalados

La aplicacion mantiene una lista de terminales conocidos por plataforma. Al poblar el selector de Settings, se busca cada nombre en tres fuentes por orden de prioridad:

1. **Carpetas de apps del usuario** -- se busca en las carpetas configuradas en `AppDirectories` (via `AppPathInDirectory` del `PlatformProvider`).
2. **Carpetas por defecto de la plataforma** -- `DefaultAppDirectories()`, deduplicadas respecto a las del usuario.
3. **Rutas conocidas de la plataforma** -- `TerminalKnownPaths`, solo relevante en Windows con rutas absolutas a ejecutables. Las rutas con wildcards (`*`) se descartan.

Los resultados se cachean en memoria. La cache se invalida al cambiar las carpetas de apps en Settings (diferido al salir de la seccion AppSearch o cerrar Settings). La deteccion no depende de la cache de `ApplicationSearch`. Solo se muestran al usuario los terminales que realmente existen en disco.

### Terminales conocidos por plataforma

| Plataforma | Terminales conocidos |
|------------|---------------------|
| macOS | Terminal, iTerm, Warp, Alacritty, Kitty, Hyper, WezTerm, Tabby |
| Windows | Windows Terminal, PowerShell, Command Prompt, Git Bash |
| Linux | (ninguno -- no implementado) |

### Invariantes

- El usuario nunca ve un terminal en el selector que no tenga ruta resuelta (se filtran entradas con ruta vacia o nula).
- En macOS, los terminales se buscan via `AppPathInDirectory` en las carpetas del usuario y las por defecto (`/Applications`, `~/Applications`, `/System/Applications`, `/System/Applications/Utilities`).
- En Windows, `TerminalKnownPaths` contiene rutas absolutas a ejecutables conocidos; las que incluyen wildcards (`Microsoft.WindowsTerminal*\wt.exe`) se filtran correctamente.
- En Linux, las listas de terminales conocidos estan vacias. `Discover()` devuelve lista vacia.

> **Bug conocido (Windows)** - la unica ruta conocida de "Windows Terminal" es un glob (`Microsoft.WindowsTerminal*\wt.exe`). El descubrimiento descarta rutas con `*`, asi que "Windows Terminal" nunca se resuelve y nunca aparece en el selector: es codigo muerto. Ver `WindowsPlatformProvider.TerminalKnownPaths` y `TerminalDiscovery.FindTerminal()`.

> **Estado: incompleto (Linux)** - `LinuxPlatformProvider.KnownTerminalNames` esta vacio y `ExecuteCommand()` es un no-op. La auto-reparacion no puede operar (`ActiveTerminal` siempre resuelve a `null`) y ejecutar comandos en terminal no hace nada en Linux.

> **Verificar en:**
> - `TerminalDiscovery.Discover()`, `TerminalDiscovery.InvalidateCache()` en `Yottacast.Core/Services/TerminalDiscovery.cs`
> - `KnownTerminalNames`, `TerminalKnownPaths` en cada `*PlatformProvider.cs`

---

## 2. Auto-reparacion del terminal configurado

Cuando se accede al terminal activo (propiedad `ActiveTerminal` en `UserSettings`), el sistema verifica que el terminal guardado siga existiendo en disco. Si no existe:

1. Se itera la lista de terminales conocidos en orden.
2. Se selecciona el primero que tenga alguna ruta valida en disco (comprobando tanto `File.Exists` como `Directory.Exists`).
3. Se actualiza el ajuste y se persiste automaticamente.
4. Si ningun terminal existe, se devuelve `null`.

Este proceso usa `TerminalDiscovery.Resolve()`, un metodo estatico que NO depende de la cache de `ApplicationSearch` ni de la cache de discovery. Comprueba disco directamente (carpetas del usuario, carpetas por defecto, rutas conocidas).

### Invariantes

- La auto-reparacion siempre ocurre antes de que el terminal se use, al acceder a `ActiveTerminal`.
- `EnsureIntegrity()` fuerza la auto-reparacion tanto del browser como del terminal al acceder a los ajustes.
- Si el terminal preferido sigue existiendo, no se modifica el ajuste ni se guarda.

> **Verificar en:**
> - `UserSettings.ActiveTerminal` en `Yottacast.Core/Services/UserSettings.cs`
> - `TerminalDiscovery.Resolve()` en `Yottacast.Core/Services/TerminalDiscovery.cs`

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
| Otros (Git Bash) | Comando tal cual | Sin modificaciones |

Si no se encuentra una ruta valida para el ejecutable (incluido el descarte de rutas con `*`), el metodo retorna silenciosamente sin error.

> **Bug conocido (Windows)** - "Git Bash" cae en el caso `Otros` y se invoca como `bash.exe <comando>` sin `-c`. `bash.exe` trata ese argumento como nombre de script a ejecutar, no como orden inline, por lo que el comando no se ejecuta como se espera. Faltaria envolverlo en `-c "<comando>"`. Ver `WindowsPlatformProvider.ExecuteCommand()`.

### Linux

No implementado. El metodo `ExecuteCommand` tiene cuerpo vacio (no-op).

> **Verificar en:**
> - `MacOsPlatformProvider.ExecuteCommand()` en `Yottacast.Core/Platform/MacOsPlatformProvider.cs`
> - `WindowsPlatformProvider.ExecuteCommand()` en `Yottacast.Core/Platform/WindowsPlatformProvider.cs`
> - `LinuxPlatformProvider.ExecuteCommand()` en `Yottacast.Core/Platform/LinuxPlatformProvider.cs`

---

## 4. Persistencia

El terminal seleccionado se guarda en el archivo de settings como la propiedad JSON `"terminal"`, cuyo valor es el nombre logico del terminal (p. ej. `"Warp"`, `"iTerm"`). Al cargar settings, si el valor esta vacio se deja vacio y la auto-reparacion asignara el primer terminal disponible en el primer acceso a `ActiveTerminal`.

> **Verificar en:**
> - `UserSettingsData.Terminal` y `UserSettings.Load()` en `Yottacast.Core/Services/UserSettings.cs`
