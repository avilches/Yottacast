## Tests

Al modificar esta area, actualizar los tests en `Yottacast.Core.Tests/Platform/`:
- `HotkeyConfigTests.cs` — parseo y validacion de configuracion de hotkeys
- `LinuxPlatformProviderTests.cs` — guardas de query vacia en busqueda de ficheros
- `MacOsPlatformProviderTests.cs` — scripts `.command` temporales: nombre unico, sin huerfanos `.tmp`, barrido de los previos
- `WindowsPlatformProviderTests.cs` — resolucion de globs en known paths (Windows Terminal), scan recursivo de exes con limite de profundidad y filtro de helpers/uninstallers, args de Git Bash (`-c`)
