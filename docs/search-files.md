# Búsqueda de ficheros

## UserDocumentSearch

Clase: `Yottacast.Core.Search.UserDocuments.UserDocumentSearch` (implementa `IDeferredSearchSource`)

Sin caché. Cada búsqueda llama a `FileSearch.SearchAsync` con `settings.ExpandedSearchFolders`.
Si los directorios cambian en settings, la siguiente búsqueda los usará automáticamente.

`Start()`, `WhenReady()` y `Stop()` son no-ops (no hay estado que gestionar).

**Queries cortas**: hace `yield break` si `query.Length < 2` — la búsqueda de ficheros requiere al menos 2 caracteres. `FileSearch` y los `PlatformProvider` hacen early return (`Task.CompletedTask`) para queries vacías.

**Criterios de parada**: `SearchAsync` crea un `CancellationTokenSource` interno ligado al `ct` del caller.
- **Timeout configurable** (por defecto `20_000ms`, parámetro `timeoutMs` del constructor): `cts.CancelAfter(timeoutMs)` detiene mdfind tras el tiempo configurado.
- El `OperationCanceledException` se captura — se emite un snapshot final con lo que hay en el buffer.
- No hay cap de líneas (`maxResults: int.MaxValue`): el timeout y el early exit son los únicos mecanismos de parada.

**Flujo completo**:
```
mdfind emite línea → onResult callback → puntúa → añade al buffer
                   → cada SnapshotIntervalMs → snapshot parcial al channel
cts.Token expira (timeoutMs) → OperationCanceledException → snapshot final → channel.Complete()
MainWindowViewModel recibe snapshots → RefreshResults() en cada uno
```

**Snapshots progresivos**: `UserDocumentSearch` emite un snapshot cuando ha transcurrido suficiente tiempo desde el último (el intervalo está definido como constante local `SnapshotIntervalMs` dentro de `SearchAsync`) — throttling puramente por tiempo, no por número de resultados — y uno final al terminar o cancelar. Esto evita que queries con muchos resultados (p.ej. `"a"`) saturen la UI con decenas de actualizaciones por segundo.

**Tarea background bajo `CancellationToken.None`**: el `Task.Run` interno de `SearchAsync` se lanza con `CancellationToken.None`, no con el `ct` del caller. El token del caller solo se usa en el `ReadAllAsync` del consumer. Esto garantiza que la tarea siempre llegue a emitir el snapshot final y completar el channel aunque el caller cancele, evitando que el channel quede incompleto.

`FileResult` solo contiene `Name` y `Path` — no hay distinción entre fichero y directorio en los resultados.

## FileSearch

`FileSearch` es un thin wrapper sobre `PlatformProvider.SearchFilesAsync`. No tiene lógica propia más allá de hacer early return si la query es vacía.

## PlatformProvider — backends por plataforma

**macOS — SpotlightInterop**:
- `SpotlightInterop.Query` es **síncrono y bloqueante** — siempre se llama desde `Task.Run`.
- Usa `MDQueryExecute` con `kMDQuerySynchronous` (flag `1`): espera a que el índice devuelva todos los resultados antes de retornar.
- Los resultados se leen via `MDQueryGetResultAtIndex` + `MDItemCopyAttribute("kMDItemPath")`. `MDItemCopyAttribute` transfiere ownership (se hace `CFRelease`); `MDQueryGetResultAtIndex` no transfiere ownership.
- Buffer de path de 4096 bytes; rutas más largas se truncarán silenciosamente.
- La cancelación se comprueba en cada iteración de resultado (`ct.ThrowIfCancellationRequested()`).

**macOS — predicado para file search**: para queries **sin wildcard**, construye un AND de `kMDItemFSName == '*token*'cd` por cada token (búsqueda de substring, case-insensitive y diacritic-insensitive). Para queries **con wildcard**, usa el patrón tal cual: `kMDItemFSName == 'pattern'cd`. Los `'` de la query se escapan con `\'`.

**macOS — scope de búsqueda**: si ninguna de las carpetas configuradas existe en el sistema de ficheros, el scope cae a `$HOME` (fallback, no error). Se loguean como warning las carpetas no existentes.

**Windows — file search**: ejecuta un script PowerShell codificado en Base64 que abre una conexión OLE DB al `SystemIndex` (`Provider=Search.CollatorDSO`) y lanza una query SQL con `CONTAINS(System.FileName, 'token*')` por cada token y `System.ItemPathDisplay LIKE 'folder%'` para el scope. Los caracteres `'`, `"` y `*` se eliminan de la query para evitar inyección SQL. Usa `ProcessRunner` (redirige stdout, no PTY).

**Linux — file search**: usa `plocate` si existe en `/usr/bin/plocate`, sino `locate`. Solo pasa el primer token al binario con pattern `*token*`; los tokens adicionales y el filtro de carpetas se aplican client-side en el callback. Usa `ProcessRunner`.

**`ProcessRunner`**: lee stdout línea a línea con `ReadLineAsync(ct)`. Al cancelar o al salir del bucle (porque `onLine` devolvió `false`), llama `proc.Kill(entireProcessTree: true)` para asegurar que ningún subproceso quede huérfano.

## Scoring

`UserDocumentSearch` aplica scoring client-side sobre el nombre del fichero devuelto por el OS. Ver `Search/UserDocuments/UserDocumentSearch.cs` para los valores exactos.

Para **queries con wildcard** (`*`), todos los resultados reciben un score base.

Para **queries sin wildcard**, distingue dos modos:

**Query de un token** (ej. `"report"`): el score varía según si el nombre es exactamente el query, empieza por él, termina en él, o simplemente lo contiene. `Stem = Path.GetFileNameWithoutExtension(name)`, por lo que `"report"` puntúa igual contra `"report.pdf"` que contra el nombre completo `"report"`.

**Query multi-token** (ej. `"xls calc mis"`): la plataforma pre-filtra con un predicado AND en Spotlight/Windows Search/locate, pero el callback `onResult` aplica un segundo filtro client-side: descarta cualquier resultado donde no todos los tokens estén contenidos en el nombre. El scoring de los resultados que pasan ese filtro depende de si todos los tokens son prefijo de algún segmento del nombre (split por espacios, guiones, guiones bajos, puntos) o solo aparecen como substring.

Ejemplo: `"xls calc mis"` → `"mis calculos.xls"`: segmentos `["mis","calculos","xls"]`; "mis"→"mis"✓, "calc"→"calculos"✓, "xls"→"xls"✓ → score prefijo.

**Coincidencia con extensión**: en modo single-token, si la query coincide con la extensión del fichero (p.ej. query `"pdf"` vs `"report.pdf"`), la comparación construye el punto implícitamente (`extension == $".{queryLower}"`). El score resultante es `0.9` — por debajo del exact match (`1.0`) pero por encima de StartsWith (`0.75`), de manera que ficheros con esa extensión aparecen antes que carpetas o ficheros cuyo nombre empieza por el mismo término.

**ViewModel construido**: `UserDocumentSearch` construye `ResultItemViewModel` con `Icon` (emoji según extensión), `Category = "Files"`, `Title = r.Name`, `Subtitle = r.Path`, `OnActivate = () => platform.LaunchApp(path)`, e `IconBytes`/`BadgeIconBytes` tomados del caché en el momento de construcción (pueden ser `null` si aún no han cargado; `RefreshIconBytes` los rellena en los snapshots siguientes).

## Badge de aplicación predeterminada

Cada `ResultItemViewModel` de fichero puede mostrar un badge (18×18px) en la esquina inferior derecha del icono con el logo de la app que lo abrirá. El badge se obtiene de `_badgeByExtension`, un `ConcurrentDictionary<string, byte[]?>` indexado por extensión en minúsculas (e.g. `".pdf"`). `null` significa "sin badge" (suprimido explícitamente); si la clave no existe aún, el badge no se muestra hasta que la precarga termine.

**Precarga (`PreloadBadgeIconAsync`)**: se lanza en `Task.Run` por cada fichero nuevo, pero solo una vez por extensión (doble guarda: `ContainsKey` + `TryAdd` en `_badgePreloading`). Pasos:
1. Llama `platform.GetDefaultAppPath(filePath)` para obtener la ruta del `.app` que abriría el fichero.
2. Si `appPath == null` → `_badgeByExtension[ext] = null` (sin app predeterminada conocida).
3. Si `Path.GetFullPath(appPath) == Path.GetFullPath(filePath)` (OrdinalIgnoreCase) → `null` (el fichero es en sí mismo la app, p.ej. un `.app` bundle).
4. Si `platform.AreIconsSame(filePath, appPath)` → `null` (la app registra su propio icono para ese tipo; el badge sería redundante con el icono principal).
5. Si pasa todos los checks → `platform.GetAppIconBytes(appPath)` y se almacena el PNG resultante.

**Supresión de badge redundante (`AreIconsSame` en macOS)**: lee `Contents/Info.plist` del bundle de la app (funciona con plists XML y binarios vía `NSDictionary dictionaryWithContentsOfFile:`). Itera `CFBundleDocumentTypes`; si alguna entrada tiene `CFBundleTypeIconFile` definido y `CFBundleTypeExtensions` contiene la extensión del fichero, devuelve `true` — la app registró un icono propio para ese tipo, por lo que el icono del fichero ya lleva implícito el logo de la app.

**Recarga diferida de iconos**: `RefreshIconBytes` rellena `IconBytes` y `BadgeIconBytes` nulos en el buffer con lo que haya cargado desde el último snapshot. Los iconos que no estén listos al emitir el snapshot final simplemente no se muestran en esa búsqueda; en búsquedas posteriores ya están en caché y aparecen desde el primer snapshot. Esto solo ocurre en la primera búsqueda de cada extensión, mientras `fileIconCache` y `_badgeByExtension` están fríos.

**Logging en `UserDocumentSearch`**: al iniciar emite `LogDebug` con query, timeout y carpetas; al completar emite `LogInformation` con query y total de resultados acumulados. Al cancelar, el `LogInformation` distingue si fue el caller (`ct.IsCancellationRequested`) o el timeout interno (`cts.IsCancellationRequested && !ct.IsCancellationRequested`) quien originó la cancelación.

## SpotlightInterop — detalles de inicialización y alcance

**`kCFTypeArrayCallBacks`**: no es una constante — es un símbolo exportado de CoreFoundation. Se carga en el constructor estático de `SpotlightInterop` vía `NativeLibrary.GetExport`, una sola vez para toda la vida del proceso.

**Scope vacío o null**: si `scopes` es `null` o vacío, `MDQuerySetSearchScope` no se llama, y Spotlight usa su ámbito global por defecto. Este comportamiento difiere del fallback de `MacOsPlatformProvider.SearchFilesAsync`, que nunca llama a `SpotlightInterop.Query` sin scope — siempre inyecta al menos `$HOME` si todas las carpetas configuradas son inválidas.

**`maxResults` en macOS**: el callback `onLine` dentro de `MacOsPlatformProvider.SearchFilesAsync` devuelve `false` cuando `count >= maxResults`, lo que hace que `SpotlightInterop.Query` salga del bucle de resultados antes de procesarlos todos. Como `UserDocumentSearch` pasa `int.MaxValue`, en la práctica el límite lo impone el timeout, no este contador.

**Manejo de errores en `MacOsPlatformProvider.SearchFilesAsync`**: las excepciones que no son `OperationCanceledException` se capturan, se almacenan en `error` y se registran en `LogDebug` al terminar, pero no se re-lanzan. La búsqueda termina silenciosamente con los resultados parciales emitidos hasta ese momento.

## Windows Search — detalles adicionales

**`CONTAINS` es prefijo, no substring**: `CONTAINS(System.FileName, 'token*')` ancla al inicio del token. A diferencia del predicado de Spotlight `'*token*'cd`, no hace búsqueda de substring arbitraria.

**Sanitización y segunda comprobación de query vacía**: `WindowsPlatformProvider.SearchFilesAsync` elimina `'`, `"` y `*` de la query. Si el resultado tras la eliminación es una cadena vacía, retorna `Task.CompletedTask` sin lanzar PowerShell.

**PowerShell con `-NoProfile -NonInteractive -EncodedCommand`**: el script se codifica en Base64 (UTF-16LE) para evitar problemas de escaping en la línea de comandos. El cwd del proceso se establece en `$HOME`.

## Linux — detalles adicionales

**Sanitización de query**: Linux solo elimina `"` de la query (no `'` ni `*`), a diferencia de Windows.

**`filteredOnLine` no consume el límite de `maxResults` en líneas filtradas**: cuando una línea no cumple el filtro de carpetas o de tokens extra, `filteredOnLine` devuelve `true` (continuar) sin llamar a `onLine`. El `-l maxResults` pasado a `plocate/locate` limita la salida del proceso OS, pero el número efectivo de resultados entregados a `UserDocumentSearch` puede ser menor.

## `ProcessRunner` — detalles adicionales

**`WaitForExitAsync(ct)` tras el bucle de stdout**: cuando el bucle termina porque `onLine` devolvió `false` (no por cancelación), `ProcessRunner` llama `WaitForExitAsync(ct)` con el token aún activo. Independientemente del resultado, `Kill(entireProcessTree: true)` se ejecuta siempre en el bloque `finally`.

**Quoting de argumentos**: los argumentos con espacios se envuelven en comillas dobles con las comillas internas escapadas (`\"`). Los argumentos sin espacios se pasan literalmente.

**`ProcessResult`**: `RunAsync` devuelve `ProcessResult(Elapsed, ExitCode, Cancelled, Error)`. `IsSuccess` es `true` solo si `Error` es null, `Cancelled` es false y `ExitCode == 0`. `UserDocumentSearch` no usa este valor de retorno — los resultados llegan vía el callback `onLine`.

## Tests

`FakePlatformProvider` emite todos los `FileResult` que se le pasan al constructor ignorando la query y las carpetas de búsqueda. Esto permite que los tests de `UserDocumentSearch` verifiquen exclusivamente la lógica de scoring y filtrado client-side, sin depender del comportamiento del OS.
