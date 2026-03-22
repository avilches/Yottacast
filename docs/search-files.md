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

**Query multi-token** (ej. `"xls calc mis"`): la plataforma pre-filtra con un predicado AND en Spotlight/Windows Search/locate, pero el callback `onResult` aplica un segundo filtro client-side: descarta cualquier resultado donde no todos los tokens estén contenidos en el nombre. El scoring de los resultados que pasan ese filtro depende de si todos los tokens son prefijo de algún segmento del nombre (split por espacios, guiones, puntos) o solo aparecen como substring.

Ejemplo: `"xls calc mis"` → `"mis calculos.xls"`: segmentos `["mis","calculos","xls"]`; "mis"→"mis"✓, "calc"→"calculos"✓, "xls"→"xls"✓ → score prefijo.
