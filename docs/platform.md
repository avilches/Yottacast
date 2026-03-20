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
- `ExpandPath()` — expande `$HOME/` y `~/` a rutas absolutas

## StandardCommandRunner

Único runner: `StandardCommandRunner.RunAsync(binary, args, cwd, onLine, ct)`.

Acepta `Func<string, bool> onLine` — retorna `false` para parar antes del EOF. Redirige stdout al pipe del proceso (block-buffered). Al cancelar o recibir `false` de `onLine`, mata el proceso con `Kill(entireProcessTree: true)`.

Registrado en DI como singleton. Los `PlatformProvider`s lo reciben por inyección de constructor.

**Gotcha — Raw string literals con variables PowerShell**: usar `$$"""..."""` en lugar de `$"""..."""` cuando el contenido tiene `$var`. Con `$$`, interpolación C# pasa a `{{expr}}` y los `$` sueltos son literales.

## SharpHook (global hotkey)

Los tipos están en `SharpHook.Data`, **no** en `SharpHook.Native`. El modificador de teclas es `EventMask`, **no** `ModifierMask`.

```csharp
using SharpHook;       // SimpleGlobalHook
using SharpHook.Data;  // KeyCode, EventMask
```

ALT+Space muestra/oculta la ventana.

**Gotcha — `SimpleGlobalHook` requerido**: se usa `SimpleGlobalHook` (no `TaskPoolGlobalHook`) porque necesita `e.SuppressEvent = true` para evitar que el OS reciba ALT+Space. Con `TaskPoolGlobalHook` el handler corre en otro thread y la supresión no tiene efecto.

**Gotcha — Permiso Accessibility en macOS**: sin el permiso, el hook detecta la tecla pero no la suprime (llega también a la app activa). Se ignora silenciosamente sin error — puede ser confuso al depurar.
