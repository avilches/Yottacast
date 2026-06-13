# Terminal del usuario

El usuario puede configurar su terminal preferido en los ajustes de Yottacast. La aplicacion detecta los terminales instalados, permite seleccionar uno y ejecuta comandos en el.

---

## 1. Deteccion de terminales instalados

La aplicacion mantiene una lista de terminales conocidos por plataforma. Al poblar el selector de Settings, se busca cada nombre en tres fuentes por orden de prioridad:

1. **Carpetas de apps del usuario** -- se busca en las carpetas configuradas en `AppDirectories` (via `AppPathInDirectory` del `PlatformProvider`).
2. **Carpetas por defecto de la plataforma** -- `DefaultAppDirectories()`, deduplicadas respecto a las del usuario.
3. **Rutas conocidas de la plataforma** -- `TerminalKnownPaths`, solo relevante en Windows con rutas absolutas a ejecutables. Cada ruta se resuelve via `PlatformProvider.ResolveKnownPath`: las literales se aceptan si existen y las que contienen un glob `*` (p. ej. Windows Terminal en `WindowsApps`) se expanden a la primera coincidencia existente en disco.

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
- En Windows, `TerminalKnownPaths` contiene rutas absolutas a ejecutables conocidos. "Windows Terminal" aporta el stub de alias `%LocalAppData%\Microsoft\WindowsApps\wt.exe` (sin glob) y, como respaldo, el glob `Microsoft.WindowsTerminal*\wt.exe`, que `ResolveKnownPath` expande a la version instalada.
- En Linux, las listas de terminales conocidos estan vacias. `Discover()` devuelve lista vacia.

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
| Otros (Alacritty, Kitty, etc.) | Script `.command` temporal: se escribe en `AppPaths.TerminalScriptsDir` con nombre unico, se marca ejecutable con `chmod +x`, se abre con `open -a <nombre>` | Sin escaping adicional |

Nota: el script `.command` no puede borrarse justo despues de `open` (el terminal quiza aun no lo ha leido). En su lugar, cada nueva ejecucion barre los scripts de ejecuciones anteriores de `AppPaths.TerminalScriptsDir` antes de crear el nuevo, de modo que el directorio no acumula basura.

### Windows

| Terminal | Argumentos | Escaping |
|----------|-----------|----------|
| PowerShell | `-NoExit -Command "<comando>"` | `"` -> `\"` |
| Command Prompt | `/K "<comando>"` | Sin escaping adicional |
| Git Bash | `-c "<comando>"` | `\` -> `\\`, `"` -> `\"` |
| Otros | Comando tal cual | Sin modificaciones |

Los argumentos se construyen en `WindowsPlatformProvider.BuildTerminalArgs`. El ejecutable se resuelve via `ResolveKnownPath`, que expande globs (p. ej. Windows Terminal). Si no se encuentra una ruta valida, el metodo retorna silenciosamente sin error.

**Invariante (Git Bash)**: el comando se pasa inline via `-c "<comando>"`. Un argumento crudo se interpretaria como ruta de script y no se ejecutaria; por eso se envuelve y se escapan comillas y barras invertidas.

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
