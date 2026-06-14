# Soporte multi-plataforma

Yottacast se ejecuta en macOS, Windows y Linux. La logica especifica de cada sistema operativo esta encapsulada de forma
que el resto de la aplicacion no necesita saber en que plataforma corre. Este documento describe los comportamientos
esperados por plataforma y los contratos que deben cumplirse en torno a **ventana, foco, lanzamiento de apps,
navegadores/terminales, iconos, hotkey global y expansion de rutas**.

El descubrimiento/escaneo de aplicaciones, la busqueda de ficheros y la interoperacion con los motores nativos
(Spotlight, Windows Search, `plocate`/`locate`) y procesos externos vive en `docs/multi-platform-search.md`.

---

## 1. Aislamiento de la logica de plataforma

La aplicacion determina en que sistema operativo se ejecuta **una sola vez** al arrancar. A partir de ese momento, todo
el codigo accede a las capacidades del OS a traves de una abstraccion unica (`PlatformProvider`) y un singleton de
gestion de ventana/foco (`AppHandler`). Ninguna otra parte del codigo consulta directamente el sistema operativo.

**Invariante**: fuera de `App.axaml.cs` y las propias implementaciones de plataforma, no existe
ninguna llamada a `RuntimeInformation.IsOSPlatform()` ni a `OperatingSystem.Is*()`.

> **Verificar en:** `Yottacast.Core/Platform/PlatformProvider.cs` (clase abstracta), `Yottacast/App.axaml.cs` (seleccion
> de instancia), `Yottacast/Services/AppHandler.cs` (singleton `Instance`).

---

## 2. Deteccion del tema del sistema (claro/oscuro)

La aplicacion debe detectar si el usuario tiene configurado el tema oscuro del OS para aplicar el tema visual adecuado
por defecto.

| Plataforma | Metodo de deteccion                                              | Resultado oscuro         |
|------------|------------------------------------------------------------------|--------------------------|
| macOS      | Ejecuta `defaults read -g AppleInterfaceStyle`                   | La salida es `"Dark"`    |
| Windows    | Lee registro `HKCU\...\Themes\Personalize\AppsUseLightTheme`     | Valor == 0               |
| Linux      | Ejecuta `gsettings get org.gnome.desktop.interface color-scheme` | Contiene `"prefer-dark"` |

**Invariantes**:

- Si la deteccion falla (proceso con error, clave de registro inexistente, entorno no GNOME), el resultado es `null` y
  se usa el tema oscuro por defecto (`"dark-default"`).
- El tema por defecto si el modo es claro es `"light-gray"`.

> **Verificar en:** `MacOsPlatformProvider.IsSystemDarkMode()`, `WindowsPlatformProvider.IsSystemDarkMode()`,
`LinuxPlatformProvider.IsSystemDarkMode()`, `PlatformProvider.DefaultTheme()`.

---

## 3. Lanzamiento de aplicaciones

| Plataforma | Comando                          | UseShellExecute |
|------------|----------------------------------|-----------------|
| macOS      | `open "<path>"`                  | `false`         |
| Windows    | `ProcessStartInfo(path)` directo | `true`          |
| Linux      | `xdg-open "<path>"`              | `false`         |

**Invariante**: Windows usa `UseShellExecute = true` para que el shell gestione permisos (UAC). macOS y Linux lanzan el
proceso directamente.

> **Verificar en:** `MacOsPlatformProvider.LaunchApp()`, `WindowsPlatformProvider.LaunchApp()`,
`LinuxPlatformProvider.LaunchApp()`.

---

## 4. Navegadores y terminales

### 4.1 Descubrimiento de navegadores y terminales

El discovery busca en tres fuentes por orden de prioridad: carpetas de apps del usuario, carpetas por defecto de la plataforma (`DefaultAppDirectories`), y rutas conocidas de la plataforma (`BrowserKnownPaths`/`TerminalKnownPaths`). Las carpetas duplicadas se saltan automaticamente. Los resultados se cachean en memoria y la cache se invalida al cambiar las carpetas de apps en Settings.

| Plataforma | Navegadores conocidos | Terminales conocidos | Estrategia de busqueda |
|---|---|---|---|
| macOS | Safari, Google Chrome, Firefox, Brave Browser, Microsoft Edge, Opera, Arc, Vivaldi, Chromium, Tor Browser, DuckDuckGo, Orion | Terminal, iTerm, Warp, Alacritty, Kitty, Hyper, WezTerm, Tabby | Via `AppPathInDirectory` en carpetas del usuario + por defecto. Sin rutas conocidas adicionales |
| Windows | Google Chrome, Mozilla Firefox, Microsoft Edge, Brave Browser, Opera, Vivaldi | Windows Terminal, PowerShell, Command Prompt, Git Bash | Carpetas del usuario + `BrowserKnownPaths`/`TerminalKnownPaths` con rutas absolutas a ejecutables (las que llevan glob se expanden) |
| Linux | (ninguno) | (ninguno) | No soportado |

> **Estado: incompleto (Linux)** - `LinuxPlatformProvider.KnownBrowserNames` y `KnownTerminalNames` estan vacios. Como consecuencia, el descubrimiento devuelve siempre lista vacia y la auto-reparacion de navegador/terminal no puede operar en Linux: `ActiveBrowser`/`ActiveTerminal` siempre resuelven a `null`. `OpenUrl()` y `ExecuteCommand()` ya NO son no-op silenciosos: loguean un warning ("no soportado en Linux") via el `logger` inyectado y no abren ni ejecutan nada. La integracion real de navegador/terminal sigue PENDIENTE. Ver `Yottacast.Core/Platform/LinuxPlatformProvider.cs`.

**Resolucion de rutas conocidas**: cada entrada de `TerminalKnownPaths`/`BrowserKnownPaths` se resuelve via `PlatformProvider.ResolveKnownPath`. Una ruta literal se acepta si existe en disco; una ruta con glob `*` (solo en un segmento de directorio) se expande a la primera coincidencia existente. La implementacion base trata cualquier glob como no resoluble; Windows la sobrescribe para expandirlos.

**Windows Terminal**: la entrada "Windows Terminal" tiene dos rutas: el stub de alias en `%LocalAppData%\Microsoft\WindowsApps\wt.exe` (sin glob, presente cuando el paquete esta instalado) y el glob `C:\Program Files\WindowsApps\Microsoft.WindowsTerminal*\wt.exe` como respaldo. Ambas se resuelven via `ResolveKnownPath`, asi que "Windows Terminal" aparece en el selector y se puede ejecutar.

### 4.3 Ejecucion de comandos en terminal (macOS)

El despacho depende del terminal seleccionado:

| Terminal | Mecanismo                                                                                                                     |
|----------|-------------------------------------------------------------------------------------------------------------------------------|
| Terminal | AppleScript: `tell application "Terminal" to do script "..."`                                                                 |
| iTerm    | AppleScript: `tell application "iTerm" to create window with default profile command "..."`                                   |
| Warp     | Abre URL `warp://action/new_tab?command=<urlencoded>` con `open`                                                              |
| Otros    | Escribe un script temporal `*.command` con nombre unico en `AppPaths.TerminalScriptsDir`, le da permisos de ejecucion (`chmod +x`) y lo abre con `open -a <terminal>`. Cada ejecucion barre primero los scripts previos para no dejar huerfanos |

**Invariante**: los comandos enviados via AppleScript se escapan con `EscapeAppleScript` (escapa `\` y `"`).

### 4.4 Ejecucion de comandos en terminal (Windows)

El ejecutable se resuelve via `ResolveKnownPath` (que expande globs como el de Windows Terminal). Los argumentos varian segun el terminal y se construyen en `WindowsPlatformProvider.BuildTerminalArgs`:

| Terminal       | Argumentos                       |
|----------------|----------------------------------|
| PowerShell     | `-NoExit -Command "<cmd>"`       |
| Command Prompt | `/K "<cmd>"`                     |
| Git Bash       | `-c "<cmd>"` (escapa `\` y `"`)  |
| Otros          | El comando tal cual              |

**Invariante (Git Bash)**: el comando se pasa inline via `-c "<cmd>"`. Un argumento crudo se interpretaria como ruta de script y no se ejecutaria. Las comillas dobles y barras invertidas internas se escapan.

> **Verificar en:** `MacOsPlatformProvider.ExecuteCommand()`, `MacOsPlatformProvider.OpenUrl()`,
`WindowsPlatformProvider.ExecuteCommand()`, `WindowsPlatformProvider.OpenUrl()`, `LinuxPlatformProvider`.

---

## 5. Iconos de aplicaciones y archivos

Solo macOS implementa la obtencion de iconos. En Windows y Linux, los metodos devuelven `null` (iconos) o `false` (
comparacion).

### 5.1 Obtencion del icono (`GetAppIconBytes` / `GetFileIconBytes`)

El icono se obtiene via `NSWorkspace iconForFile:` (Objective-C P/Invoke). Se renderiza a 64x64 puntos logicos con el
patron `lockFocus` / `drawInRect:` / `unlockFocus` y se serializa a PNG via
`NSBitmapImageRep representationUsingType:properties:` (tipo 4 = PNG).

**Invariante**: en pantallas Retina (2x) el resultado son 128x128 pixeles fisicos, suficiente para la visualizacion a
28x28 logicos. `GetFileIconBytes` delega internamente en `GetAppIconBytes` (mismo metodo para ambos).

### 5.2 App por defecto para un archivo (`GetDefaultAppPath`)

Crea un `NSURL fileURLWithPath:` y llama `NSWorkspace URLForApplicationToOpenURL:`. Devuelve `null` si el sistema no
tiene ninguna app registrada para esa extension.

### 5.3 Deteccion de iconos redundantes (`AreIconsSame`)

Determina si el icono de un archivo ya incorpora visualmente el logo de la app que lo abre, para evitar badging
redundante.

**Metodo**: lee `Contents/Info.plist` del bundle de la app (via `NSDictionary dictionaryWithContentsOfFile:`, soporta
XML y plist binario). Itera `CFBundleDocumentTypes`; retorna `true` si alguna entrada tiene `CFBundleTypeIconFile` y su
`CFBundleTypeExtensions` incluye la extension del archivo.

**Invariante (critico)**: la comparacion es semantica, nunca por pixeles. macOS genera representaciones TIFF diferentes
para el mismo icono obtenido desde el archivo vs. desde la app (incluso cuando visualmente son identicos). Comparar
bytes, TIFF o pixeles renderizados produce falsos negativos. La unica aproximacion fiable es leer el `Info.plist`.

> **Verificar en:** `MacOsPlatformProvider.GetAppIconBytes()`, `MacOsPlatformProvider.GetFileIconBytes()`,
`MacOsPlatformProvider.GetDefaultAppPath()`, `MacOsPlatformProvider.AreIconsSame()`.

---

## 6. Gestion de ventana y foco (`AppHandler`)

`AppHandler` gestiona el ciclo de vida de la ventana principal: que ocurre al mostrarla, al ocultarla, y como simular el
pegado tras activar un resultado.

### 6.1 Inicializacion

| Plataforma | Comportamiento                                                                                   |
|------------|--------------------------------------------------------------------------------------------------|
| macOS      | Establece `NSApplicationActivationPolicyAccessory` (sin icono en Dock, sin barra de menu propia) |
| Windows    | No-op                                                                                            |
| Linux      | No-op                                                                                            |

### 6.2 Mostrar la ventana (`ShowWindow`)

| Plataforma | Comportamiento                                                                                                                                                                                      |
|------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| macOS      | Captura `NSWorkspace.frontmostApplication` con `objc_retain`. Llama `window.Show()` y hace la ventana de Yottacast key con `makeKeyWindow`. No se llama a `activateWithOptions:` en ShowWindow (solo en `OnHide`) |
| Windows    | `window.Show()` + `window.Activate()` (Windows gestiona el foco automaticamente)                                                                                                                    |
| Linux      | `window.Show()` + `window.Activate()`                                                                                                                                                               |

**Invariante (macOS)**: la app anterior permanece como la aplicacion "activa" del sistema (semaforos con color),
mientras que la ventana de Yottacast es la "key window" y recibe los eventos de teclado. Esto evita que las ventanas
de otras apps se desactiven visualmente al invocar Yottacast (comportamiento igual a Alfred/Raycast).

Si habia una referencia previa a `_previousApp`, se libera con `objc_release` antes de retener la nueva.

**Invariante (toggle)**: el hotkey global usa `window.IsVisible` (no `IsActive`) para decidir si ocultar o mostrar,
porque con la nueva activacion macOS la ventana nunca tiene `IsActive = true`.

### 6.3 Ocultar la ventana (`OnHide`)

| Plataforma | Comportamiento                                                                                                                   |
|------------|----------------------------------------------------------------------------------------------------------------------------------|
| macOS      | Restaura el foco a la app previa con `activateWithOptions:` (`NSApplicationActivateIgnoringOtherApps = 2`), libera la referencia |
| Windows    | No-op                                                                                                                            |
| Linux      | No-op                                                                                                                            |

**Invariante**: tras ocultar, el usuario debe ver la app que tenia en foco antes de invocar Yottacast.

### 6.4 Simulacion de pegado (`SimulatePasteAsync`)

Se invoca cuando el usuario activa un resultado con `PasteAfterActivate = true`. Se ejecuta despues de `OnHide()`.

| Plataforma | Mecanismo                                                                                                                   | Delay previo |
|------------|-----------------------------------------------------------------------------------------------------------------------------|--------------|
| macOS      | CoreGraphics: `CGEventCreateKeyboardEvent` (key 0x09 = 'v') con flag `kCGEventFlagMaskCommand`, publicado con `CGEventPost` | 150 ms       |
| Windows    | `keybd_event` de `user32.dll`: VK_CONTROL down, VK_V down, VK_V up, VK_CONTROL up                                           | 150 ms       |
| Linux      | Loguea un warning ("paste no soportado en Linux") y no pega nada. Integracion real PENDIENTE (xdotool/wtype)               | --           |

**Invariante**: el delay de 150 ms (`AppDefaults.PasteDelayMs`) existe para que la app destino recupere el foco antes de
recibir el evento de teclado.

### 6.5 Ocultacion del cursor

| Plataforma | Mecanismo                            |
|------------|--------------------------------------|
| macOS      | `NSCursor setHiddenUntilMouseMoves:` |
| Windows    | `ShowCursor` de `user32.dll`         |
| Linux      | No-op                                |

### 6.6 Atajo para cerrar ventana

Cada plataforma define un `KeyModifiers` + `Key` distinto para "cerrar ventana" via `AppHandler.CloseWindowShortcut`
(Cmd+W en macOS, Ctrl+F4 en Windows, Ctrl+W en Linux). Lo unico especifico de plataforma es la combinacion en si; el
comportamiento (redirigir a `Hide()`, nunca destruir la ventana) se documenta en `docs/ui-hotkeys.md`.

> **Verificar en:** `AppHandler.CloseWindowShortcut`, `MacAppHandler.cs`, `WindowsAppHandler.cs`, `LinuxAppHandler.cs`.

### 6.7 Modificador de "comando" (`MetaKeyModifier`)

`AppHandler` expone la propiedad `MetaKeyModifier` con el `KeyModifiers` que representa la tecla de "comando" de cada
plataforma: `Meta` (Cmd) en macOS, `Control` en Windows/Linux. Se deriva de `CopyShortcut.Modifiers`. Existe para que
las Views no hardcodeen `KeyModifiers.Meta` (que en Windows seria la tecla Windows/Super fisica).

El uso concreto en atajos de accion (`Cmd/Ctrl+Enter`, `Cmd/Ctrl+doble-click`, matching via `AppHandler.MatchesHotkey`)
se documenta en `docs/ui-hotkeys.md`.

> **Verificar en:** `AppHandler.MetaKeyModifier`, `AppHandler.MatchesHotkey`.

> **Verificar en:** `Yottacast/Services/AppHandler.cs`, `Yottacast/Services/MacAppHandler.cs`,
`Yottacast/Services/WindowsAppHandler.cs`, `Yottacast/Services/LinuxAppHandler.cs`, `Yottacast.Core/AppDefaults.cs` (
> constante `PasteDelayMs`).

---

## 7. Hotkey global (SharpHook): especifico de plataforma

El hotkey global (mostrar/ocultar la ventana) se captura a nivel de sistema operativo con SharpHook. Aqui solo se
documenta lo especifico de plataforma; el mapeo de teclas (`KeyNameMap`), el parseo de la combinacion y el matching
exacto de modificadores se documentan en `docs/ui-hotkeys.md`.

**Captura a nivel de OS**: SharpHook instala un hook global de teclado a nivel del sistema operativo (no a nivel de
ventana Avalonia), por lo que el hotkey funciona aunque Yottacast no tenga el foco. La supresion del evento
(`e.SuppressEvent = true`, evitar que la tecla llegue tambien a la app activa) solo tiene efecto si el handler corre en
el hilo del hook, por eso se usa `SimpleGlobalHook` y no `TaskPoolGlobalHook` (ver `docs/ui-hotkeys.md`).

**Permiso de Accesibilidad (macOS)**: sin el permiso de Accesibilidad, el hook detecta la tecla pero no la suprime (la
tecla llega tambien a la app activa). No produce error; se ignora silenciosamente. En Windows y Linux la supresion no
requiere permiso adicional.

> **Verificar en:** `Yottacast/App.axaml.cs` (`RegisterGlobalHotKey`), `Yottacast.Core/Platform/HotkeyConfig.cs`.

---

## 8. Expansion de rutas (`ExpandPath`)

Metodo estatico en `PlatformProvider`. Convierte rutas con prefijo `$HOME` o `~` al directorio home del usuario.

| Entrada               | Salida          |
|-----------------------|-----------------|
| `$HOME` o `~`         | Directorio home |
| `$HOME/...` o `~/...` | Home + sufijo   |
| Cualquier otra ruta   | Sin modificar   |

**Invariante**: este metodo es la unica forma de resolver `$HOME`/`~` en toda la aplicacion. Las listas de directorios
por defecto usan `$HOME/...` y se expanden con este metodo.

> **Verificar en:** `PlatformProvider.ExpandPath()`.
