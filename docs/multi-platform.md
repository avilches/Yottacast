# PlatformProvider / ProcessRunner / SharpHook

## PlatformProvider

Clase abstracta en `Yottacast.Core.Platform`. Centraliza toda la lógica OS-específica. Una única comprobación de OS en `App.axaml.cs` (y `Yottacast.Cli/Program.cs`) elige la instancia concreta; el resto del código no hace `RuntimeInformation.IsOSPlatform()`.

Responsabilidades:
- `IsSystemDarkMode()` / `DefaultTheme()` — detección del tema del SO
- `DefaultAppDirectories()` / `DefaultSearchFolders()` — valores por defecto según OS
- `ScanAppsAsync()` / `CreateAppWatchers()` / `LaunchApp()` — gestión de apps
- `SearchFilesAsync()` — búsqueda de archivos (mdfind / Windows Search / locate)
- `KnownBrowserNames` / `BrowserFallbackPaths` / `GetBrowserPaths()` / `OpenUrl()` — datos y lanzador de browsers
- `KnownTerminalNames` / `TerminalFallbackPaths` / `GetTerminalPaths()` / `ExecuteCommand()` — datos y lanzador de terminales
- `GetAppIconBytes(appPath)` / `GetFileIconBytes(filePath)` — icono en PNG (macOS: NSWorkspace + lockFocus; otros: null)
- `GetDefaultAppPath(filePath)` — ruta del `.app` que abriría el fichero (macOS: NSWorkspace `URLForApplicationToOpenURL:`; otros: null)
- `AreIconsSame(filePath, appPath)` — true si el icono del fichero es visualmente el mismo que el de la app (macOS: Info.plist semántico; otros: false)
- `ExpandPath(path)` — método estático. Convierte `$HOME` o `~` solos en el directorio home; `$HOME/...` o `~/...` en home + sufijo. Cualquier otra ruta se devuelve sin modificar.

**macOS — `BrowserFallbackPaths` y `TerminalFallbackPaths`**: ambos devuelven diccionarios vacíos. El descubrimiento de browsers y terminales en macOS se apoya en `ApplicationSearch`/Spotlight, no en rutas hardcoded. En Windows sí están poblados con rutas conocidas.

**Linux — soporte de browsers y terminales**: `LinuxPlatformProvider` no implementa browsers ni terminales: `KnownBrowserNames` y `KnownTerminalNames` son listas vacías, y `OpenUrl()` / `ExecuteCommand()` son no-ops. La búsqueda de archivos sí está implementada vía `plocate`/`locate`.

### Detección de dark mode por plataforma

- **macOS**: lanza `defaults read -g AppleInterfaceStyle` y comprueba si la salida es `"Dark"`. Devuelve `null` si el proceso falla.
- **Windows**: lee el valor de registro `AppsUseLightTheme` en `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize`; `val == 0` → dark. Devuelve `null` si la clave no existe o hay error.
- **Linux**: lanza `gsettings get org.gnome.desktop.interface color-scheme` y busca `"prefer-dark"` en la salida. Devuelve `null` si el comando falla (p. ej. entornos no GNOME).

### Escaneo de apps por plataforma

- **macOS**: delega en `SpotlightInterop.Query` con el predicado `kMDItemContentType == 'com.apple.application-bundle'`. El watcher observa `*.app` con `NotifyFilter = DirectoryName` (las apps son directorios).
- **Windows**: recorre los subdirectorios de cada `dir` configurado, busca el `.exe` con el mismo nombre que la carpeta y, si no existe, coge el primer `.exe` encontrado. El watcher observa `*.exe` con `IncludeSubdirectories = true`.
- **Linux**: enumera los ficheros `*.desktop` de los directorios configurados (sin subdirectorios). El watcher observa `*.desktop`.

`ScanAppsAsync` es sincrónico en Windows y Linux (devuelve `Task.CompletedTask` directamente); solo en macOS se envuelve en `Task.Run` porque `SpotlightInterop.Query` bloquea el hilo.

### `LaunchApp` por plataforma

- **macOS**: `open "<path>"` con `UseShellExecute = false`.
- **Windows**: `ProcessStartInfo(path) { UseShellExecute = true }` — delega en el shell de Windows para abrir el exe con permisos adecuados.
- **Linux**: `xdg-open "<path>"` con `UseShellExecute = false`.

### Búsqueda de ficheros: estrategias específicas

**macOS** (`SpotlightInterop`): antes de construir el predicado, las comillas simples se escapan (`'` → `\'`). El predicado `kMDItemFSName` admite wildcard `*` literal en la query del usuario; en caso contrario, parte la query por espacios y genera una cláusula `&&` con `*token*cd` por cada token (case-insensitive, diacrítico-insensible). Si alguna carpeta del scope no existe, se omite con `LogWarning`. Si no quedan carpetas válidas, el scope cae al directorio home.

**Windows** (`WindowsPlatformProvider`): la query se sanitiza eliminando completamente `'`, `"` y `*` antes de construir el script. Emite un script PowerShell que abre una conexión ADODB a `Search.CollatorDSO` (Windows Search) y ejecuta SQL sobre `SystemIndex` con `CONTAINS(System.FileName, 'token*')`. El script se codifica en Base64 Unicode y se pasa a `powershell -EncodedCommand` para evitar problemas de escaping en shell. El filtro de carpetas se inyecta como cláusula `AND (System.ItemPathDisplay LIKE 'folder%' OR ...)`.

**Linux** (`LinuxPlatformProvider`): llama a `plocate` (si existe en `/usr/bin/plocate`) o `locate`, usando solo el primer token de la query como argumento nativo (`-b -l maxResults *token*`). El filtro de carpetas y los tokens adicionales se aplican en el callback de .NET: las líneas que no cumplan se descartan sin contar para el límite (retornando `true` para seguir leyendo). Como el post-filtrado ocurre en .NET después del límite nativo, el número de resultados entregados puede ser menor que `maxResults`.

### `ExecuteCommand` en macOS: despacho por terminal

- **Terminal** e **iTerm**: usan AppleScript vía `osascript -e`. Los comandos se escapan con `EscapeAppleScript` (escapa `\` y `"`).
- **Warp**: abre la URL `warp://action/new_tab?command=<urlencoded>` con `open`.
- **Otros terminales**: escribe un fichero temporal `*.command` en `/tmp`, le da permisos de ejecución con `chmod +x` y lo abre con `open -a <terminalName>`.

### `ExecuteCommand` en Windows: despacho por terminal

Los paths de terminales que contienen `*` (p. ej. Windows Terminal en `WindowsApps`) se excluyen del match de primer path existente. Argumentos según terminal: PowerShell → `-NoExit -Command "<cmd>"`, Command Prompt → `/K "<cmd>"`, resto → el comando verbatim.

### KeyNameMap (hotkey)

`App.axaml.cs` construye en startup un `Dictionary<string, KeyCode>` (case-insensitive) que cubre: teclas nombradas (`Space`, `Enter`, `Tab`, `Backspace`, `Delete`, `Escape`), A–Z, 0–9 y F1–F12. Los nombres se mapean quitando el prefijo `Vc` de `SharpHook.KeyCode`. Nombres no reconocidos devuelven `KeyCode.VcUndefined` y nunca activarán el hotkey.

## SpotlightInterop

Clase `internal static` en `Yottacast.Core.Platform`. Wrapper de P/Invoke sobre la API `MDQuery` de CoreServices y `CFString`/`CFArray` de CoreFoundation.

El método público `Query(predicate, scopes, onLine, ct)` es **sincrónico y bloquea el hilo**. Los llamadores (`ScanAppsAsync`, `SearchFilesAsync`) lo envuelven en `Task.Run`.

Flujo interno:
1. Crea un `CFString` con el predicado y construye un `MDQueryRef` con `MDQueryCreate`.
2. Si hay scopes, crea un `CFArray` de `CFString` y llama `MDQuerySetSearchScope`.
3. Ejecuta la query con `MDQueryExecute(query, kMDQuerySynchronous=1)` — bloquea hasta completar.
4. Comprueba cancelación antes de iterar resultados.
5. Por cada resultado: obtiene el ítem con `MDQueryGetResultAtIndex` (no transfiere ownership), copia el atributo `kMDItemPath` con `MDItemCopyAttribute` (sí transfiere ownership → `CFRelease` en `finally` interno), decodifica el path desde un buffer de 4096 bytes UTF-8.
6. Si `onLine` devuelve `false`, para la iteración.
7. En el `finally` externo libera todos los `IntPtr` acumulados en la lista `owned` (predicado, query, scope refs, scope array, attrName).

`kCFTypeArrayCallBacks` es una variable global exportada de CoreFoundation; se resuelve una vez en el constructor estático vía `NativeLibrary.Load` + `NativeLibrary.GetExport`.

## ProcessRunner

Único runner: `ProcessRunner.RunAsync(binary, string[] args, string? cwd, onLine, ct)`.

`args` es un array `string[]`; el runner los ensambla en una sola cadena pasada al proceso. Los argumentos que contienen espacios se entrecomillan automáticamente (dobles comillas; las comillas dobles internas se escapan con `\"`). Esto lo hace el método privado `QuoteArg`. `cwd` es nullable: si es `null` se usa `Environment.CurrentDirectory`.

`RunAsync` devuelve `ProcessResult(Elapsed, ExitCode, Cancelled, Error?)`. La propiedad calculada `IsSuccess` es `true` cuando `Error is null && !Cancelled && ExitCode == 0`.

Acepta `Func<string, bool> onLine` — retorna `false` para parar antes del EOF. Redirige stdout al pipe del proceso. Siempre llama `Kill(entireProcessTree: true)` en el bloque `finally` (garantiza limpieza tanto en cancelación como en early exit por `false`; si el proceso ya terminó es un no-op).

Registrado en DI como singleton. `WindowsPlatformProvider` y `LinuxPlatformProvider` lo reciben por inyección de constructor; `MacOsPlatformProvider` no lo necesita porque lanza procesos directamente vía `System.Diagnostics.Process` y `SpotlightInterop`.

### Iconos de app y fichero en macOS

**`GetAppIconBytes` / `GetFileIconBytes`**: obtienen el icono PNG de un path (app o fichero) usando `NSWorkspace iconForFile:` vía Objective-C P/Invoke. Renderizan la imagen a 64×64 puntos con el patrón `lockFocus` / `drawInRect:` / `unlockFocus` y la serializan a PNG vía `NSBitmapImageRep representationUsingType:properties:` (tipo 4 = PNG). En Retina el resultado son 128×128 píxeles físicos. Ver `GetAppIconBytes` en `MacOsPlatformProvider.cs` para los detalles de cada paso.

**`GetDefaultAppPath`**: crea un `NSURL fileURLWithPath:` para el fichero y llama `NSWorkspace URLForApplicationToOpenURL:`. Devuelve `nil` si el sistema no tiene ninguna app registrada para esa extensión.

**`AreIconsSame`**: compara semánticamente, no por píxeles. Lee `Contents/Info.plist` del bundle via `NSDictionary dictionaryWithContentsOfFile:` (soporta tanto XML como plist binario). Itera `CFBundleDocumentTypes`; retorna `true` si alguna entrada tiene `CFBundleTypeIconFile` y su `CFBundleTypeExtensions` incluye la extensión del fichero. Esto indica que la app registró un icono propio para ese tipo de documento, por lo que el icono del fichero ya lleva implícito el logo de la app (badging sería redundante).

**Gotcha — comparación de píxeles no es fiable para iconos en macOS**: macOS genera representaciones TIFF diferentes para el icono de un fichero (obtenido via `iconForFile:` sobre el fichero) y el icono de la app (mismo método sobre el `.app`), incluso cuando visualmente son idénticos. Comparar bytes, TIFF o píxeles renderizados produce falsos negativos. La única aproximación fiable es semántica: leer el `Info.plist`.

**Gotcha — Raw string literals con variables PowerShell**: usar `$$"""..."""` en lugar de `$"""..."""` cuando el contenido tiene `$var`. Con `$$`, interpolación C# pasa a `{{expr}}` y los `$` sueltos son literales.

## AppHandler

Clase abstracta en `Yottacast/Services/AppHandler.cs`. Define los métodos abstractos `OnFrameworkInitializationCompleted()`, `OnShow()` y `OnHide()`, la propiedad abstracta `CloseWindowShortcut`, y el método virtual `SimulatePasteAsync()` (default no-op). Expone un singleton estático `Instance` que elige la implementación concreta en función del OS (`MacAppHandler`, `WindowsAppHandler`, `LinuxAppHandler`).

- `OnFrameworkInitializationCompleted()` — invocado antes de crear la ventana; en macOS establece `NSApplicationActivationPolicyAccessory`.
- `OnShow()` / `OnHide()` — se llaman desde `App.axaml.cs` al mostrar/ocultar la ventana.
- `CloseWindowShortcut` — atajo de teclado para ocultar la ventana (Cmd+W en macOS, Ctrl+F4 en Windows, Ctrl+W en Linux).
- `SimulatePasteAsync()` — lo llama `MainWindow` tras activar un resultado con `PasteAfterActivate = true` (`OnHide()` se llama separadamente antes). Espera a que la app destino recupere el foco y luego simula el atajo de pegar. Ver `MacAppHandler.cs` / `WindowsAppHandler.cs` para la implementación concreta.

### MacAppHandler

MacAppHandler usa P/Invoke a las APIs del runtime Objective-C de macOS (`libobjc.dylib`) y CoreGraphics para gestionar la política de activación, el foco y el paste simulado.

- **`OnShow`**: captura `NSWorkspace.sharedWorkspace.frontmostApplication` con `objc_retain` y lo guarda en `_previousApp` (libera el valor previo si existía). Luego llama `activateIgnoringOtherApps:` sobre `NSApplication.sharedApplication`.
- **`OnHide`**: llama `activateWithOptions:` con `NSApplicationActivateIgnoringOtherApps = 2` sobre `_previousApp`, y libera la referencia con `objc_release`.
- **`SimulatePasteAsync`**: espera 150 ms para que la app destino recupere el foco. Luego usa CoreGraphics: crea dos `CGEvent` de teclado (key down y key up) con `CGEventCreateKeyboardEvent(source=null, virtualKey=0x09 ('v'), ...)`, establece el flag `kCGEventFlagMaskCommand = 0x100000` en ambos con `CGEventSetFlags`, los publica con `CGEventPost(kCGHIDEventTap=0, event)` y los libera con `CFRelease`.

**`WindowsAppHandler.SimulatePasteAsync`**: espera 150 ms y luego usa `keybd_event` de `user32.dll` para enviar VK_CONTROL (0x11) down, VK_V (0x56) down, VK_V up, VK_CONTROL up. `OnShow`/`OnHide` son no-ops (Windows gestiona el foco automáticamente).

**`LinuxAppHandler`**: todos los métodos son no-ops; `SimulatePasteAsync` usa la implementación base (no hace nada).

## SharpHook (global hotkey)

Los tipos de SharpHook están en los namespaces `SharpHook` y `SharpHook.Data`; ver `App.axaml.cs` para los checks exactos de tecla y modificador.

El hotkey muestra/oculta la ventana. La combinación se carga de `UserSettings.Hotkey` y se parsea en tiempo de arranque a través de `HotkeyConfig.Parse` (definido en `Yottacast.Core/Platform/HotkeyConfig.cs`). El valor por defecto es `"Alt+Space"`. Los cambios en Settings se reflejan inmediatamente, sin reiniciar.

`HotkeyConfig.Parse` acepta aliases de modificador además de los nombres canónicos: `"option"`/`"options"` → `Alt`; `"cmd"`/`"command"`/`"win"`/`"windows"` → `Meta`. Los modificadores canónicos son `Alt`, `Ctrl`, `Shift`, `Meta`.

**Matching exacto de modificadores**: los cuatro grupos de modificadores (Alt, Ctrl, Shift, Meta) deben coincidir exactamente. Si el usuario tiene configurado `Alt+Space` y pulsa `Alt+Cmd+Space`, el hotkey no se activa. Ver la lógica en `RegisterGlobalHotKey` en `App.axaml.cs`.

**`HotkeyConfig.ToString()`** serializa en el orden canónico Ctrl → Alt → Shift → Meta → Key, independientemente del orden en que se haya parseado.

**Gotcha — `SimpleGlobalHook` requerido**: se usa `SimpleGlobalHook` (no `TaskPoolGlobalHook`) porque necesita `e.SuppressEvent = true` para evitar que el OS reciba la tecla. Con `TaskPoolGlobalHook` el handler corre en otro thread y la supresión no tiene efecto.

**Gotcha — Permiso Accessibility en macOS**: sin el permiso, el hook detecta la tecla pero no la suprime (llega también a la app activa). Se ignora silenciosamente sin error — puede ser confuso al depurar.
