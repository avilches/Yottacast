# Soporte multi-plataforma: busqueda y procesos

Este documento cubre la parte de **busqueda y proceso por plataforma**: como cada sistema operativo descubre las
aplicaciones instaladas, busca archivos del usuario, e interopera con los motores nativos (Spotlight en macOS, Windows
Search, `plocate`/`locate` en Linux) y con procesos externos.

La parte de **ventana, foco, lanzamiento de apps, navegadores/terminales, iconos, hotkey global y aislamiento de
plataforma** vive en `docs/multi-platform.md`.

---

## 1. Descubrimiento e indexacion de aplicaciones

La aplicacion debe encontrar todas las aplicaciones instaladas en los directorios configurados por el usuario y mantener
la lista actualizada en tiempo real.

### 1.1 Escaneo inicial

| Plataforma | Que busca                         | Mecanismo                                                                    |
|------------|-----------------------------------|------------------------------------------------------------------------------|
| macOS      | Bundles `.app`                    | Spotlight (`kMDItemContentType == 'com.apple.application-bundle'`)           |
| Windows    | Archivos `.exe` en subdirectorios | Recorrido recursivo del filesystem (hasta `AppDefaults.WindowsAppScanMaxDepth`), añadiendo cada `.exe` que sea lanzable |
| Linux      | Archivos `.desktop`               | Enumeracion directa (sin subdirectorios)                                     |

**Invariante**: el escaneo en macOS es asincrono (envuelto en `Task.Run` porque Spotlight bloquea el hilo). En Windows y
Linux el escaneo es sincrono y devuelve `Task.CompletedTask`.

**Invariante**: si `UserSettings.EnableAppSearch` es `false`, `ApplicationSearch.Start()` marca la fuente como ready inmediatamente sin lanzar el escaneo, y `Search()` devuelve siempre una lista vacia.

**Invariante (Windows)**: el escaneo y el watcher comparten el mismo criterio de profundidad y el mismo filtro de ejecutables. El escaneo recorre recursivamente hasta `AppDefaults.WindowsAppScanMaxDepth` (cubre layouts anidados como `Google\Chrome\Application\chrome.exe`) y descarta ejecutables que no son apps lanzables (uninstallers, updaters, crash handlers) segun `AppDefaults.WindowsAppExeExcludeSubstrings`. El watcher aplica el mismo predicado (`WindowsPlatformProvider.IsLaunchableAppExe`) y el mismo limite de profundidad a cada evento, de modo que una app anidada instalada en caliente se trata exactamente igual que en el escaneo inicial. Ver `WindowsPlatformProvider.ScanAppsAsync()` y `CreateAppWatchers()`.

### 1.2 Vigilancia de cambios (watchers)

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

## 2. Busqueda de archivos

La aplicacion permite buscar archivos del usuario mediante un motor de busqueda nativo de cada plataforma.

### 2.1 Estrategia por plataforma

| Plataforma | Motor                                | Tratamiento de la query                                                                                                                                      |
|------------|--------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------|
| macOS      | Spotlight (via `SpotlightInterop`)   | Escapa comillas simples. Si contiene `*`, usa predicado literal. Si no, parte por espacios y genera clausulas `kMDItemFSName == '*token*'cd` unidas con `&&` |
| Windows    | Windows Search (ADODB + SystemIndex) | Elimina `'`, `"` y `*`. Genera clausula `CONTAINS(System.FileName, 'token*')` por cada token. El script PowerShell se codifica en Base64 Unicode             |
| Linux      | `plocate` (preferido) o `locate`     | Solo el primer token se pasa como argumento nativo (`-b -l maxResults *token*`). Tokens adicionales y filtro de carpetas se aplican en .NET                  |

### 2.2 Invariantes

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

## 3. Spotlight (macOS): interoperacion nativa

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

## 4. Ejecucion de procesos externos

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

## 5. Gotcha: raw string literals con variables PowerShell

Al generar scripts PowerShell en C#, usar `$$"""..."""` en lugar de `$"""..."""` cuando el contenido tiene `$var`. Con
`$$`, la interpolacion de C# pasa a `{{expr}}` y los `$` sueltos son literales para PowerShell.

> **Verificar en:** `WindowsPlatformProvider.SearchFilesAsync()`.
