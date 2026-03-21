# PlatformProvider / StandardCommandRunner / SharpHook

## PlatformProvider

Clase abstracta en `Yottacast.Core.Platform`. Centraliza toda la lógica OS-específica. Una única comprobación de OS en `App.axaml.cs` (y `Yottacast.Cli/Program.cs`) elige la instancia concreta; el resto del código no hace `RuntimeInformation.IsOSPlatform()`.

Responsabilidades:
- `IsSystemDarkMode()` / `DefaultTheme()` — detección del tema del SO
- `DefaultAppDirectories()` / `DefaultSearchFolders()` — valores por defecto según OS
- `ScanAppsAsync()` / `CreateAppWatchers()` / `LaunchApp()` — gestión de apps
- `SearchFilesAsync()` — búsqueda de archivos (mdfind / Windows Search / locate)
- `KnownBrowserNames` / `BrowserFallbackPaths` / `GetBrowserPaths()` / `OpenUrl()` — datos y lanzador de browsers
- `KnownTerminalNames` / `TerminalFallbackPaths` / `GetTerminalPaths()` / `ExecuteCommand()` — datos y lanzador de terminales
- `GetAppIconPath()` — icono de app (macOS: parsea Info.plist; otros: null)
- `ExpandPath(path)` — método estático. Convierte `$HOME` o `~` solos en el directorio home; `$HOME/...` o `~/...` en home + sufijo. Cualquier otra ruta se devuelve sin modificar.

**macOS — `BrowserFallbackPaths` y `TerminalFallbackPaths`**: ambos devuelven diccionarios vacíos. El descubrimiento de browsers y terminales en macOS se apoya en `ApplicationSearch`/Spotlight, no en rutas hardcoded. En Windows sí están poblados con rutas conocidas.

**Linux — soporte de browsers y terminales**: `LinuxPlatformProvider` no implementa browsers ni terminales: `KnownBrowserNames` y `KnownTerminalNames` son listas vacías, y `OpenUrl()` / `ExecuteCommand()` son no-ops. La búsqueda de archivos sí está implementada vía `plocate`/`locate`.

## StandardCommandRunner

Único runner: `StandardCommandRunner.RunAsync(binary, string[] args, string? cwd, onLine, ct)`.

`args` es un array `string[]`; el runner los ensambla en una sola cadena pasada al proceso. Los argumentos que contienen espacios se entrecomillan automáticamente (dobles comillas; las comillas dobles internas se escapan con `\"`). Esto lo hace el método privado `QuoteArg`. `cwd` es nullable: si es `null` se usa `Environment.CurrentDirectory`.

`RunAsync` devuelve `ProcessResult(Elapsed, ExitCode, Cancelled, Error?)`. La propiedad calculada `IsSuccess` es `true` cuando `Error is null && !Cancelled && ExitCode == 0`.

Acepta `Func<string, bool> onLine` — retorna `false` para parar antes del EOF. Redirige stdout al pipe del proceso (block-buffered). Al cancelar o recibir `false` de `onLine`, mata el proceso con `Kill(entireProcessTree: true)`.

Registrado en DI como singleton. Los `PlatformProvider`s lo reciben por inyección de constructor.

**Gotcha — Raw string literals con variables PowerShell**: usar `$$"""..."""` en lugar de `$"""..."""` cuando el contenido tiene `$var`. Con `$$`, interpolación C# pasa a `{{expr}}` y los `$` sueltos son literales.

## AppHandler

Clase abstracta en `Yottacast/Services/AppHandler.cs`. Define el ciclo de vida de la app en dos métodos abstractos: `OnShow()` y `OnHide()`. Expone un singleton estático `Instance` que elige la implementación concreta en función del OS (`MacAppHandler`, `WindowsAppHandler`, `LinuxAppHandler`).

`OnShow()` y `OnHide()` se llaman desde `App.axaml.cs` al mostrar/ocultar la ventana. `OnHide()` también orquesta el flujo de paste automático (`SimulatePasteAsync`): espera un breve delay para garantizar que la app de destino ha recuperado el foco antes de simular el atajo de teclado de pegar. Ver `MacAppHandler.cs` / `WindowsAppHandler.cs` para la implementación concreta.

### MacAppHandler

MacAppHandler usa P/Invoke a las APIs del runtime Objective-C de macOS para gestionar la política de activación de la app (sin Dock ni barra de menú) y la captura/restauración del foco al mostrar y ocultar la ventana. Ver `MacAppHandler.cs` para los detalles.

## SharpHook (global hotkey)

Los tipos de SharpHook están en los namespaces `SharpHook` y `SharpHook.Data`; ver `App.axaml.cs` para los checks exactos de tecla y modificador.

El hotkey muestra/oculta la ventana. La combinación se carga de `UserSettings.Hotkey` y se parsea en tiempo de arranque a través de `HotkeyConfig.Parse` (definido en `Yottacast.Core/Platform/HotkeyConfig.cs`). El valor por defecto es `"Alt+Space"`. Los cambios en Settings se reflejan inmediatamente, sin reiniciar.

`HotkeyConfig.Parse` acepta aliases de modificador además de los nombres canónicos: `"option"`/`"options"` → `Alt`; `"cmd"`/`"command"`/`"win"`/`"windows"` → `Meta`. Los modificadores canónicos son `Alt`, `Ctrl`, `Shift`, `Meta`.

**Matching exacto de modificadores**: los cuatro grupos de modificadores (Alt, Ctrl, Shift, Meta) deben coincidir exactamente. Si el usuario tiene configurado `Alt+Space` y pulsa `Alt+Cmd+Space`, el hotkey no se activa. Ver la lógica en `RegisterGlobalHotKey` en `App.axaml.cs`.

**Gotcha — `SimpleGlobalHook` requerido**: se usa `SimpleGlobalHook` (no `TaskPoolGlobalHook`) porque necesita `e.SuppressEvent = true` para evitar que el OS reciba la tecla. Con `TaskPoolGlobalHook` el handler corre en otro thread y la supresión no tiene efecto.

**Gotcha — Permiso Accessibility en macOS**: sin el permiso, el hook detecta la tecla pero no la suprime (llega también a la app activa). Se ignora silenciosamente sin error — puede ser confuso al depurar.
