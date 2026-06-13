# Soporte multi-plataforma

Yottacast se ejecuta en macOS, Windows y Linux. La logica especifica de cada sistema operativo esta encapsulada de forma
que el resto de la aplicacion no necesita saber en que plataforma corre. Este documento describe los comportamientos
esperados por plataforma y los contratos que deben cumplirse.

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

## 3. Descubrimiento e indexacion de aplicaciones

La aplicacion debe encontrar todas las aplicaciones instaladas en los directorios configurados por el usuario y mantener
la lista actualizada en tiempo real.

### 3.1 Escaneo inicial

| Plataforma | Que busca                         | Mecanismo                                                                    |
|------------|-----------------------------------|------------------------------------------------------------------------------|
| macOS      | Bundles `.app`                    | Spotlight (`kMDItemContentType == 'com.apple.application-bundle'`)           |
| Windows    | Archivos `.exe` en subdirectorios | Recorrido recursivo del filesystem (hasta `AppDefaults.WindowsAppScanMaxDepth`), añadiendo cada `.exe` que sea lanzable |
| Linux      | Archivos `.desktop`               | Enumeracion directa (sin subdirectorios)                                     |

**Invariante**: el escaneo en macOS es asincrono (envuelto en `Task.Run` porque Spotlight bloquea el hilo). En Windows y
Linux el escaneo es sincrono y devuelve `Task.CompletedTask`.

**Invariante**: si `UserSettings.EnableAppSearch` es `false`, `ApplicationSearch.Start()` marca la fuente como ready inmediatamente sin lanzar el escaneo, y `Search()` devuelve siempre una lista vacia.

**Invariante (Windows)**: el escaneo y el watcher comparten el mismo criterio de profundidad y el mismo filtro de ejecutables. El escaneo recorre recursivamente hasta `AppDefaults.WindowsAppScanMaxDepth` (cubre layouts anidados como `Google\Chrome\Application\chrome.exe`) y descarta ejecutables que no son apps lanzables (uninstallers, updaters, crash handlers) segun `AppDefaults.WindowsAppExeExcludeSubstrings`. El watcher aplica el mismo predicado (`WindowsPlatformProvider.IsLaunchableAppExe`) y el mismo limite de profundidad a cada evento, de modo que una app anidada instalada en caliente se trata exactamente igual que en el escaneo inicial. Ver `WindowsPlatformProvider.ScanAppsAsync()` y `CreateAppWatchers()`.

### 3.2 Vigilancia de cambios (watchers)

| Plataforma | Filtro del watcher | Eventos observados                                                          | Subdirectorios |
|------------|--------------------|-----------------------------------------------------------------------------|----------------|
| macOS      | `*.app`            | `Created`, `Changed`, `Deleted` (NotifyFilter: `DirectoryName + LastWrite`) | No             |
| Windows    | `*.exe`            | `Created`, `Deleted` (NotifyFilter: `FileName`); filtra helpers y respeta el limite de profundidad del scan | Si             |
| Linux      | `*.desktop`        | `Created`, `Deleted` (NotifyFilter: `FileName`)                             | No             |

**Invariante (macOS)**: el evento `Changed` existe a proposito. Cuando un `.app` se copia, el `Created` puede llegar
antes de que el bundle este completo; el `Changed` detecta cuando se terminan de copiar los archivos internos (el mtime
del directorio cambia), permitiendo recargar el icono.

> **Verificar en:** `MacOsPlatformProvider.ScanAppsAsync()` / `CreateAppWatchers()`,
`WindowsPlatformProvider.ScanAppsAsync()` / `CreateAppWatchers()`, `LinuxPlatformProvider.ScanAppsAsync()` /
`CreateAppWatchers()`.

---

## 4. Lanzamiento de aplicaciones

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

## 5. Busqueda de archivos

La aplicacion permite buscar archivos del usuario mediante un motor de busqueda nativo de cada plataforma.

### 5.1 Estrategia por plataforma

| Plataforma | Motor                                | Tratamiento de la query                                                                                                                                      |
|------------|--------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------|
| macOS      | Spotlight (via `SpotlightInterop`)   | Escapa comillas simples. Si contiene `*`, usa predicado literal. Si no, parte por espacios y genera clausulas `kMDItemFSName == '*token*'cd` unidas con `&&` |
| Windows    | Windows Search (ADODB + SystemIndex) | Elimina `'`, `"` y `*`. Genera clausula `CONTAINS(System.FileName, 'token*')` por cada token. El script PowerShell se codifica en Base64 Unicode             |
| Linux      | `plocate` (preferido) o `locate`     | Solo el primer token se pasa como argumento nativo (`-b -l maxResults *token*`). Tokens adicionales y filtro de carpetas se aplican en .NET                  |

### 5.2 Invariantes

- **macOS**: si alguna carpeta del scope no existe, se omite con un warning. Si no queda ninguna carpeta valida, el
  scope es el directorio home del usuario.
- **Windows**: el script se pasa como `-EncodedCommand` (Base64 Unicode) para evitar problemas de escaping en shell.
- **Linux**: el post-filtrado de carpetas y tokens adicionales ocurre despues del limite nativo de `plocate`/`locate`,
  por lo que el numero de resultados entregados puede ser menor que `maxResults`.

Las tres plataformas descartan las queries que quedan vacias tras sanear (solo comillas o espacios) con un
early-return antes de acceder a los tokens, de modo que una busqueda asi nunca lanza ni invoca el backend nativo.
En Linux la query se sanea eliminando comillas y recortando espacios (`string.IsNullOrEmpty(safeQuery)`), igual que
en Windows.

> **Verificar en:** `MacOsPlatformProvider.SearchFilesAsync()`, `WindowsPlatformProvider.SearchFilesAsync()`,
`LinuxPlatformProvider.SearchFilesAsync()`, `SpotlightInterop.Query()`.

---

## 6. Spotlight (macOS): interoperacion nativa

`SpotlightInterop` es un wrapper P/Invoke sobre la API `MDQuery` de CoreServices. Es sincronico y bloquea el hilo; los
llamadores lo envuelven en `Task.Run`.

**Contrato de memoria**:

- `MDQueryGetResultAtIndex` no transfiere ownership (no se libera).
- `MDItemCopyAttribute` si transfiere ownership (se libera con `CFRelease` en un `finally` interno por cada resultado).
- Todos los `IntPtr` acumulados (predicado, query, scope refs, scope array, atributo) se liberan en un `finally`
  externo.
- Los paths se decodifican desde un buffer UTF-8 de 4096 bytes.

`kCFTypeArrayCallBacks` es una variable global exportada de CoreFoundation; se resuelve una vez en el constructor
estatico via `NativeLibrary.Load` + `NativeLibrary.GetExport`.

> **Verificar en:** `Yottacast.Core/Platform/SpotlightInterop.cs`.

---

## 7. Ejecucion de procesos externos

`ProcessRunner` es el runner generico para lanzar procesos con lectura linea a linea de stdout y stderr.

**Contrato**:

- Las comillas dobles internas de un argumento se escapan siempre con `\"` (tengan o no espacios). Un argumento se
  entrecomilla con comillas dobles cuando contiene espacios o comillas, para no perder caracteres ni romper el parseo
  del comando.
- `cwd` nullable: si es `null`, se usa `Environment.CurrentDirectory`.
- El callback `onLine` puede devolver `false` para parar la lectura antes del EOF.
- El proceso siempre se mata con `Kill(entireProcessTree: true)` en un bloque `finally` tras la lectura (garantiza
  limpieza en cancelacion, early exit por `false`, o finalizacion normal; si el proceso ya termino es un no-op).
- Resultado: `ProcessResult(Elapsed, ExitCode, Cancelled, Error?, StoppedByCallback)`. `StoppedByCallback` indica
  terminacion voluntaria porque un callback devolvio `false` (p.ej. limite de resultados alcanzado). `IsSuccess` es
  `true` cuando `Error is null && !Cancelled && (StoppedByCallback || ExitCode == 0)`: una parada por callback es exito
  funcional aunque el `ExitCode` sea distinto de 0 (el proceso se mata con `Kill` antes de terminar por su cuenta).
- Tanto stdout como stderr se drenan en paralelo (`Task.WhenAll`). Cuando cualquier callback devuelve `false`, un
  `CancellationTokenSource` vinculado cancela ambas lecturas.

**Uso por plataforma**: `WindowsPlatformProvider` y `LinuxPlatformProvider` lo reciben por inyeccion de constructor.
`MacOsPlatformProvider` no lo usa porque lanza procesos directamente con `System.Diagnostics.Process` y delega en
`SpotlightInterop`.

> **Verificar en:** `Yottacast.Core/Services/ProcessRunner.cs`.

---

## 8. Navegadores y terminales

### 8.1 Descubrimiento de navegadores y terminales

El discovery busca en tres fuentes por orden de prioridad: carpetas de apps del usuario, carpetas por defecto de la plataforma (`DefaultAppDirectories`), y rutas conocidas de la plataforma (`BrowserKnownPaths`/`TerminalKnownPaths`). Las carpetas duplicadas se saltan automaticamente. Los resultados se cachean en memoria y la cache se invalida al cambiar las carpetas de apps en Settings.

| Plataforma | Navegadores conocidos | Terminales conocidos | Estrategia de busqueda |
|---|---|---|---|
| macOS | Safari, Google Chrome, Firefox, Brave Browser, Microsoft Edge, Opera, Arc, Vivaldi, Chromium, Tor Browser, DuckDuckGo, Orion | Terminal, iTerm, Warp, Alacritty, Kitty, Hyper, WezTerm, Tabby | Via `AppPathInDirectory` en carpetas del usuario + por defecto. Sin rutas conocidas adicionales |
| Windows | Google Chrome, Mozilla Firefox, Microsoft Edge, Brave Browser, Opera, Vivaldi | Windows Terminal, PowerShell, Command Prompt, Git Bash | Carpetas del usuario + `BrowserKnownPaths`/`TerminalKnownPaths` con rutas absolutas a ejecutables (las que llevan glob se expanden) |
| Linux | (ninguno) | (ninguno) | No soportado |

> **Estado: incompleto (Linux)** - `LinuxPlatformProvider.KnownBrowserNames` y `KnownTerminalNames` estan vacios. Como consecuencia, el descubrimiento devuelve siempre lista vacia y la auto-reparacion de navegador/terminal no puede operar en Linux: `ActiveBrowser`/`ActiveTerminal` siempre resuelven a `null`. `OpenUrl()` y `ExecuteCommand()` ya NO son no-op silenciosos: loguean un warning ("no soportado en Linux") via el `logger` inyectado y no abren ni ejecutan nada. La integracion real de navegador/terminal sigue PENDIENTE. Ver `Yottacast.Core/Platform/LinuxPlatformProvider.cs`.

**Resolucion de rutas conocidas**: cada entrada de `TerminalKnownPaths`/`BrowserKnownPaths` se resuelve via `PlatformProvider.ResolveKnownPath`. Una ruta literal se acepta si existe en disco; una ruta con glob `*` (solo en un segmento de directorio) se expande a la primera coincidencia existente. La implementacion base trata cualquier glob como no resoluble; Windows la sobrescribe para expandirlos.

**Windows Terminal**: la entrada "Windows Terminal" tiene dos rutas: el stub de alias en `%LocalAppData%\Microsoft\WindowsApps\wt.exe` (sin glob, presente cuando el paquete esta instalado) y el glob `C:\Program Files\WindowsApps\Microsoft.WindowsTerminal*\wt.exe` como respaldo. Ambas se resuelven via `ResolveKnownPath`, asi que "Windows Terminal" aparece en el selector y se puede ejecutar.

### 8.3 Ejecucion de comandos en terminal (macOS)

El despacho depende del terminal seleccionado:

| Terminal | Mecanismo                                                                                                                     |
|----------|-------------------------------------------------------------------------------------------------------------------------------|
| Terminal | AppleScript: `tell application "Terminal" to do script "..."`                                                                 |
| iTerm    | AppleScript: `tell application "iTerm" to create window with default profile command "..."`                                   |
| Warp     | Abre URL `warp://action/new_tab?command=<urlencoded>` con `open`                                                              |
| Otros    | Escribe un script temporal `*.command` con nombre unico en `AppPaths.TerminalScriptsDir`, le da permisos de ejecucion (`chmod +x`) y lo abre con `open -a <terminal>`. Cada ejecucion barre primero los scripts previos para no dejar huerfanos |

**Invariante**: los comandos enviados via AppleScript se escapan con `EscapeAppleScript` (escapa `\` y `"`).

### 8.4 Ejecucion de comandos en terminal (Windows)

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

## 9. Iconos de aplicaciones y archivos

Solo macOS implementa la obtencion de iconos. En Windows y Linux, los metodos devuelven `null` (iconos) o `false` (
comparacion).

### 9.1 Obtencion del icono (`GetAppIconBytes` / `GetFileIconBytes`)

El icono se obtiene via `NSWorkspace iconForFile:` (Objective-C P/Invoke). Se renderiza a 64x64 puntos logicos con el
patron `lockFocus` / `drawInRect:` / `unlockFocus` y se serializa a PNG via
`NSBitmapImageRep representationUsingType:properties:` (tipo 4 = PNG).

**Invariante**: en pantallas Retina (2x) el resultado son 128x128 pixeles fisicos, suficiente para la visualizacion a
28x28 logicos. `GetFileIconBytes` delega internamente en `GetAppIconBytes` (mismo metodo para ambos).

### 9.2 App por defecto para un archivo (`GetDefaultAppPath`)

Crea un `NSURL fileURLWithPath:` y llama `NSWorkspace URLForApplicationToOpenURL:`. Devuelve `null` si el sistema no
tiene ninguna app registrada para esa extension.

### 9.3 Deteccion de iconos redundantes (`AreIconsSame`)

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

## 10. Gestion de ventana y foco (`AppHandler`)

`AppHandler` gestiona el ciclo de vida de la ventana principal: que ocurre al mostrarla, al ocultarla, y como simular el
pegado tras activar un resultado.

### 10.1 Inicializacion

| Plataforma | Comportamiento                                                                                   |
|------------|--------------------------------------------------------------------------------------------------|
| macOS      | Establece `NSApplicationActivationPolicyAccessory` (sin icono en Dock, sin barra de menu propia) |
| Windows    | No-op                                                                                            |
| Linux      | No-op                                                                                            |

### 10.2 Mostrar la ventana (`ShowWindow`)

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

### 10.3 Ocultar la ventana (`OnHide`)

| Plataforma | Comportamiento                                                                                                                   |
|------------|----------------------------------------------------------------------------------------------------------------------------------|
| macOS      | Restaura el foco a la app previa con `activateWithOptions:` (`NSApplicationActivateIgnoringOtherApps = 2`), libera la referencia |
| Windows    | No-op                                                                                                                            |
| Linux      | No-op                                                                                                                            |

**Invariante**: tras ocultar, el usuario debe ver la app que tenia en foco antes de invocar Yottacast.

### 10.4 Simulacion de pegado (`SimulatePasteAsync`)

Se invoca cuando el usuario activa un resultado con `PasteAfterActivate = true`. Se ejecuta despues de `OnHide()`.

| Plataforma | Mecanismo                                                                                                                   | Delay previo |
|------------|-----------------------------------------------------------------------------------------------------------------------------|--------------|
| macOS      | CoreGraphics: `CGEventCreateKeyboardEvent` (key 0x09 = 'v') con flag `kCGEventFlagMaskCommand`, publicado con `CGEventPost` | 150 ms       |
| Windows    | `keybd_event` de `user32.dll`: VK_CONTROL down, VK_V down, VK_V up, VK_CONTROL up                                           | 150 ms       |
| Linux      | Loguea un warning ("paste no soportado en Linux") y no pega nada. Integracion real PENDIENTE (xdotool/wtype)               | --           |

**Invariante**: el delay de 150 ms (`AppDefaults.PasteDelayMs`) existe para que la app destino recupere el foco antes de
recibir el evento de teclado.

### 10.5 Ocultacion del cursor

| Plataforma | Mecanismo                            |
|------------|--------------------------------------|
| macOS      | `NSCursor setHiddenUntilMouseMoves:` |
| Windows    | `ShowCursor` de `user32.dll`         |
| Linux      | No-op                                |

### 10.6 Atajo para cerrar ventana

| Plataforma | Atajo   |
|------------|---------|
| macOS      | Cmd+W   |
| Windows    | Ctrl+F4 |
| Linux      | Ctrl+W  |

### 10.7 Modificador de "comando" (`MetaKeyModifier`)

`AppHandler` expone la propiedad `MetaKeyModifier` con el `KeyModifiers` que representa la tecla de "comando"
de cada plataforma: `Meta` (Cmd) en macOS, `Control` en Windows/Linux. Se deriva de `CopyShortcut.Modifiers`.

Las Views la usan para detectar atajos de accion sin hardcodear `KeyModifiers.Meta` (que en Windows seria la tecla
Windows/Super fisica): por ejemplo `Cmd/Ctrl+Enter` y `Cmd/Ctrl+doble-click` para "ejecutar sin cerrar". El resto de
atajos de accion se resuelven via `AppHandler.MatchesHotkey`, que internamente usa el mismo `MetaKeyModifier`.

> **Verificar en:** `AppHandler.MetaKeyModifier`, `AppHandler.MatchesHotkey`, `MainWindow.axaml.cs`
(`OnKeyDown` case `Key.Return`, `OnResultsDoubleTapped`).

> **Verificar en:** `Yottacast/Services/AppHandler.cs`, `Yottacast/Services/MacAppHandler.cs`,
`Yottacast/Services/WindowsAppHandler.cs`, `Yottacast/Services/LinuxAppHandler.cs`, `Yottacast.Core/AppDefaults.cs` (
> constante `PasteDelayMs`).

---

## 11. Hotkey global (SharpHook)

El hotkey global muestra/oculta la ventana principal. La combinacion se configura en `UserSettings.Hotkey` (valor por
defecto: `"Alt+Space"`) y se parsea con `HotkeyConfig.Parse` al arrancar. Los cambios en la configuracion se reflejan
inmediatamente, sin reiniciar.

### 11.1 Parseo de la combinacion

`HotkeyConfig.Parse` acepta una cadena con modificadores y una tecla separados por `+`. Es case-insensitive.

| Alias aceptados                            | Modificador canonico |
|--------------------------------------------|----------------------|
| `alt`, `option`, `options`                 | Alt                  |
| `ctrl`, `control`                          | Ctrl                 |
| `shift`                                    | Shift                |
| `meta`, `cmd`, `command`, `win`, `windows` | Meta                 |

**Invariante**: `HotkeyConfig.ToString()` serializa siempre en orden canonico Ctrl, Alt, Shift, Meta, Key,
independientemente del orden de parseo.

### 11.2 Mapa de teclas (`KeyNameMap`)

Cubre: teclas nombradas (`Space`, `Enter`, `Tab`, `Backspace`, `Delete`, `Escape`), A-Z, 0-9, F1-F12 y teclas de puntuacion (`,`, `.`, `-`, `=`, `;`, `/`, `[`, `]`, `\`, `'`, `` ` ``). Los nombres se
mapean a los valores de `SharpHook.KeyCode` (quitando el prefijo `Vc`). Un nombre no reconocido produce
`KeyCode.VcUndefined` y nunca activara el hotkey.

### 11.3 Matching exacto de modificadores

Los cuatro grupos de modificadores (Alt, Ctrl, Shift, Meta) deben coincidir exactamente con la configuracion. Si el
hotkey es `Alt+Space` y el usuario pulsa `Alt+Cmd+Space`, no se activa.

### 11.4 Gotchas

- **`SimpleGlobalHook` requerido**: se usa `SimpleGlobalHook` (no `TaskPoolGlobalHook`) porque el handler debe correr en
  el hilo del hook para que `e.SuppressEvent = true` tenga efecto. Con `TaskPoolGlobalHook`, el handler corre en otro
  thread y la supresion no funciona.
- **Permiso Accessibility en macOS**: sin este permiso, el hook detecta la tecla pero no la suprime (llega tambien a la
  app activa). No produce error; se ignora silenciosamente.

> **Verificar en:** `Yottacast/App.axaml.cs` (metodos `RegisterGlobalHotKey`, `BuildKeyNameMap`, `KeyNameToKeyCode`),
`Yottacast.Core/Platform/HotkeyConfig.cs`.

---

## 12. Expansion de rutas (`ExpandPath`)

Metodo estatico en `PlatformProvider`. Convierte rutas con prefijo `$HOME` o `~` al directorio home del usuario.

| Entrada               | Salida          |
|-----------------------|-----------------|
| `$HOME` o `~`         | Directorio home |
| `$HOME/...` o `~/...` | Home + sufijo   |
| Cualquier otra ruta   | Sin modificar   |

**Invariante**: este metodo es la unica forma de resolver `$HOME`/`~` en toda la aplicacion. Las listas de directorios
por defecto usan `$HOME/...` y se expanden con este metodo.

> **Verificar en:** `PlatformProvider.ExpandPath()`.

---

## 13. Gotcha: raw string literals con variables PowerShell

Al generar scripts PowerShell en C#, usar `$$"""..."""` en lugar de `$"""..."""` cuando el contenido tiene `$var`. Con
`$$`, la interpolacion de C# pasa a `{{expr}}` y los `$` sueltos son literales para PowerShell.

> **Verificar en:** `WindowsPlatformProvider.SearchFilesAsync()`.
