# Busqueda de ficheros

## Proposito

Cuando el usuario escribe en el launcher, Yottacast busca ficheros del usuario en las carpetas configuradas (Downloads, Desktop, Documents, etc.) y muestra resultados progresivos ordenados por relevancia. El objetivo es encontrar ficheros rapidamente usando el indice nativo del sistema operativo, sin necesidad de recorrer el sistema de ficheros manualmente.

---

## 1. Requisitos minimos de la query

La busqueda de ficheros no se ejecuta si la query tiene menos de 2 caracteres (`FileSearchMinQueryLength`). Para queries vacias o en blanco, la capa inferior (`FileSearch`) tambien hace early return sin invocar al sistema operativo.

**Invariante:** el usuario nunca vera resultados de ficheros si ha escrito menos de 2 caracteres.

> **Verificar en:** `UserDocumentSearch.SearchAsync` (guard `query.Length < AppDefaults.FileSearchMinQueryLength`), `FileSearch.SearchAsync` (guard `string.IsNullOrWhiteSpace`).

---

## 2. Visibilidad y modos de busqueda

`UserDocumentSearch` implementa `ISearchModeSource`. La visibilidad se controla mediante `FileSearchVisibility: SearchSourceVisibility` con tres valores posibles:

| Valor | Comportamiento |
|---|---|
| `Disabled` | GlobalSearch nunca invoca esta fuente, independientemente del modo activo |
| `Always` | La fuente esta activa en el modo All. En modos especificos (Files, Clipboard) esta fuente no participa, aunque este configurada como `Always` |
| `ModeOnly` | La fuente solo esta activa cuando el modo Files esta seleccionado |

El valor por defecto es `Always`. GlobalSearch consulta `IsActiveIn(mode)` en cada busqueda y omite la fuente si devuelve `false`.

---

## 3. Carpetas de busqueda

Cuando `FileSearchOnlySpecificFolders` es `false` (por defecto), la busqueda usa `null` como parametro de carpetas, lo que hace que cada plataforma busque en toda la home del usuario (Spotlight sin filtro de carpeta en macOS). Cuando es `true`, se usa `ExpandedSearchFolders` como scope explicito.

Las carpetas por defecto solo se aplican en el primer arranque (sin settings previos) o si la lista guardada esta vacia. En ese caso se filtran a las que existen en disco en ese momento. A partir de ahi, la lista refleja exactamente lo que el usuario haya configurado, exista o no en disco.

Cada plataforma define carpetas por defecto distintas:

| Plataforma | Carpetas por defecto |
|---|---|
| macOS | Downloads, Desktop, Documents, Movies, Pictures, Dropbox, Music, Public, iCloud Drive, Library/Application Support, Library/Containers, Creative Cloud Files, Google Drive, OneDrive, Box, Mega, pCloud, Nextcloud, Adobe Creative Cloud, Amazon Drive |
| Windows | Downloads, Desktop, Documents, Videos, Pictures |
| Linux | Downloads, Desktop, Documents, Videos, Pictures |

Si ninguna de las carpetas configuradas existe en disco:
- **macOS**: se usa `$HOME` como fallback (no produce error, solo warning en log).
- **Windows/Linux**: la busqueda se ejecuta sin filtro de carpetas explicito.

**Invariantes:**
- Si `FileSearchVisibility` es `Disabled`, GlobalSearch nunca llama a esta fuente.
- Si `FileSearchVisibility` es `Always`, la fuente participa en el modo All pero no en modos especificos (Files, Clipboard).
- Si `FileSearchVisibility` es `ModeOnly`, la fuente solo participa cuando el modo Files esta activo.
- Si `FileSearchOnlySpecificFolders` es `false`, se busca en toda la home (comportamiento amplio por defecto).
- Un cambio en las carpetas de settings se aplica automaticamente en la siguiente busqueda, sin reiniciar.

> **Verificar en:** `UserDocumentSearch.IsActiveIn` (logica de visibilidad por modo), `UserDocumentSearch.SearchAsync` (logica de `folders`), `MacOsPlatformProvider.SearchFilesAsync` (fallback a `home`), `MacOsPlatformProvider.DefaultSearchFolders`, `WindowsPlatformProvider.DefaultSearchFolders`, `LinuxPlatformProvider.DefaultSearchFolders`, `UserSettings.FileSearchVisibility`.

---

## 4. Entrega progresiva de resultados (snapshots)

Los resultados se entregan a la UI en snapshots parciales para que el usuario vea resultados tan pronto como estan disponibles, sin esperar a que termine la busqueda completa.

- **Intervalo entre snapshots**: 200ms (`FileSearchSnapshotIntervalMs`). No se emite un nuevo snapshot hasta que hayan pasado al menos 200ms desde el anterior.
- **Snapshot final**: siempre se emite al terminar o cancelarse la busqueda, con todos los resultados acumulados.
- **Cada snapshot** contiene los mejores N resultados (segun `limit`) ordenados por score descendente.

**Invariante:** el usuario nunca ve mas de un snapshot cada 200ms durante una busqueda activa. Siempre recibe un snapshot final con el mejor estado conocido.

> **Verificar en:** `UserDocumentSearch.SearchAsync` (logica de `SnapshotIntervalMs`, snapshot final tras el try/catch).

---

## 5. Timeout y cancelacion

La busqueda tiene un timeout configurable (por defecto 20 segundos, `FileSearchTimeoutMs`). Hay dos vias de cancelacion:

| Via | Causa | Comportamiento |
|---|---|---|
| Timeout interno | Han pasado 20s desde el inicio | Se emite snapshot final con resultados parciales |
| Cancelacion del caller | El usuario cambia la query o cierra el launcher | El channel de lectura se interrumpe |

Al backend del SO se le pasa `int.MaxValue` como `maxResults`, asi que el timeout y la cancelacion del caller son los unicos mecanismos de parada de la consulta nativa. Cada snapshot emitido a la UI si se recorta a los mejores `limit` resultados (el limite global, ver seccion 12).

**Invariante:** una busqueda de ficheros nunca bloquea el proceso mas de 20 segundos (por defecto). El snapshot final siempre se emite, incluso si se cancela.

La tarea background se lanza con `CancellationToken.None` para garantizar que siempre llegue a emitir el snapshot final y completar el channel, incluso si el caller cancela.

> **Verificar en:** `UserDocumentSearch.SearchAsync` (creacion de `cts.CancelAfter(timeoutMs)`, `Task.Run(..., CancellationToken.None)`), `AppDefaults.FileSearchTimeoutMs`.

---

## 6. Scoring y ordenacion

El scoring se aplica client-side sobre el nombre del fichero. El sistema operativo prefiltra, pero la puntuacion final la calcula Yottacast.

### 6.1. Queries con wildcard (`*`)

Todos los resultados reciben un score base de 0.5. No se aplica scoring diferenciado.

### 6.2. Query de un solo token

Se compara contra el nombre completo y contra el stem (nombre sin extension):

| Condicion | Score | Ejemplo (query `report`) |
|---|---|---|
| Stem exacto (fichero con extension) | 1.0 | `report.pdf` |
| Coincidencia de extension (query = extension del fichero) | 0.9 | query `pdf` contra `report.pdf` |
| Nombre completo exacto (sin extension o fichero = query) | 0.85 | carpeta `report` |
| Empieza por la query (nombre o stem) | 0.75 | `report-final.pdf` |
| Termina en la query | 0.5 | `mi-report` |
| Contiene la query (otros casos) | 0.5 | `unreported.txt` |

La comparacion de extension construye el punto implicitamente: `extension == $".{queryLower}"`.

### 6.3. Query multi-token

Ejemplo: `"xls calc mis"`.

1. **Prefiltro del SO**: cada plataforma aplica un AND de todos los tokens en su indice nativo.
2. **Filtro client-side**: se descarta cualquier resultado donde no todos los tokens esten contenidos en el nombre (comparacion case-insensitive).
3. **Scoring**: se divide el nombre en segmentos (separados por espacio, guion, guion bajo, punto). Si todos los tokens son prefijo de algun segmento, score = 0.75. En caso contrario (substring), score = 0.5.

Ejemplo: `"xls calc mis"` contra `"mis calculos.xls"` -- segmentos `["mis","calculos","xls"]`. Cada token es prefijo de un segmento, luego score = 0.75.

**Invariante:** el orden de los tokens en la query no afecta al resultado ni al score.

> **Verificar en:** `UserDocumentSearch.SearchAsync` (callback `onResult`, logica de scoring), `UserDocumentSearchTests` (casos de test `SingleFileCases`, `MultiFileCases`, `MultiToken_OrderIndependent_SameTitleAndScore`).

---

## 7. Construccion del resultado visible

Cada resultado se presenta como un `ResultItemViewModel` con:

| Campo | Valor |
|---|---|
| `Title` | Nombre del fichero |
| `Subtitle` | Ruta completa |
| `Category` | `"Files"` |
| `Score` | Segun la tabla de scoring |
| `IconBytes` | Icono del fichero obtenido de `FileIconCache` (puede ser `null` en el primer snapshot) |
| `BadgeIconBytes` | Icono miniatura de la app predeterminada (puede ser `null`) |
| `OnActivate` | Abre el fichero con la app predeterminada del SO |

La accion principal ("Open") usa una etiqueta dinamica via `LabelProvider`: si se conoce el nombre de la app predeterminada para la extension, la etiqueta pasa a ser `"Open in {AppName}"` (p. ej. "Open in Preview"). Si no se conoce todavia o no hay app por defecto, queda como `"Open"`. La etiqueta se actualiza en footer y menu de opciones cuando `BadgeIconLoaded` se dispara.

El nombre de la app se cachea por extension en `_appNameByExtension` (memoria, paralelo al cache de badge). Se resuelve a la vez que el badge en `PreloadBadgeIconAsync`, pero esta desacoplado de la supresion del icono: el badge puede estar suprimido (mismo icono que el fichero, p. ej. .cs → Rider) y la etiqueta sigue mostrando "Open in Rider". Solo se suprime el nombre cuando no hay app por defecto o cuando el fichero **es** la app (`.app` bundles).

> **Verificar en:** `UserDocumentSearch.PreloadBadgeIconAsync` (resolucion de `_appNameByExtension`), `UserDocumentSearch.GetDefaultAppName`, `MainWindowViewModel.FooterHints` y `OptionsMenuItems` (uso de `LabelProvider`), `ResultAction.LabelProvider`.

> **Verificar en:** `UserDocumentSearch.SearchAsync` (construccion de `ResultItemViewModel`).

---

## 8. Iconos de fichero

Los iconos se cargan mediante `FileIconCache`, que mantiene una cache en dos niveles (memoria y disco) indexada por extension de fichero. En el momento de emitir un snapshot, se intenta obtener el icono sincrona e instantaneamente desde cache; si no esta disponible, se encola una carga asincrona via NSWorkspace. Cuando la carga termina, `FileIconCache` dispara `IconLoaded` y la UI se actualiza sola.

**Invariante:** la ausencia de icono nunca retrasa la emision de resultados. Los iconos aparecen en la UI en cuanto estan disponibles, sin que el usuario repita la busqueda.

> **Verificar en:** `FileIconCache` (metodos `Get`, `GetOrPreload`, `Load`, evento `IconLoaded`), `UserDocumentSearch.SearchAsync` (uso de `GetOrPreload` en snapshots), `MainWindowViewModel.OnFileIconLoaded`. Ver `docs/search-file-icons.md` para la especificacion completa.

---

## 9. Badge de aplicacion predeterminada

Cada resultado de fichero puede mostrar un badge (icono pequeno) en la esquina del icono principal, indicando que app lo abrira. El badge se cachea por extension (e.g. `.pdf`) y se precarga una sola vez por extension.

### Reglas de supresion del badge

El badge NO se muestra cuando se cumple alguna de estas condiciones:

| Condicion | Motivo |
|---|---|
| No hay app predeterminada para el fichero | No hay nada que mostrar |
| La ruta del fichero y la de la app son la misma | El fichero ES la app (p.ej. un `.app` bundle) |
| La app registra un icono propio para ese tipo de fichero | El badge seria redundante con el icono principal |

La deteccion de "icono propio" en macOS se hace leyendo `Info.plist` del bundle de la app: si `CFBundleDocumentTypes` contiene una entrada con `CFBundleTypeIconFile` definido y `CFBundleTypeExtensions` incluye la extension del fichero, se considera que la app ya aporta su logo al icono del fichero.

**Invariante:** el usuario nunca ve un badge que sea identico al icono principal del fichero.

> **Verificar en:** `UserDocumentSearch.PreloadBadgeIconAsync` (logica de supresion), `MacOsPlatformProvider.AreIconsSame` (lectura de `Info.plist`).

---

## 10. Backend de busqueda por plataforma

Cada plataforma usa el indice nativo del SO. `FileSearch` es solo un intermediario que delega en `PlatformProvider.SearchFilesAsync`.

### macOS -- Spotlight

- Usa `MDQuery` (API de CoreServices) en modo sincrono (`kMDQuerySynchronous`).
- Predicado para queries sin wildcard: AND de `kMDItemFSName == '*token*'cd` por cada token (substring, case y diacritic insensitive).
- Predicado para queries con wildcard: `kMDItemFSName == 'pattern'cd` literal.
- Los `'` en la query se escapan con `\'`.
- Buffer de path de 4096 bytes; rutas mas largas se truncan silenciosamente.
- La cancelacion se comprueba en cada resultado (`ct.ThrowIfCancellationRequested()`).
- Los errores que no son cancelacion se capturan y registran en log, sin relanzarse. La busqueda termina silenciosamente con resultados parciales.

> **Verificar en:** `MacOsPlatformProvider.SearchFilesAsync`, `SpotlightInterop.Query`.

### Windows -- Windows Search Index

- Ejecuta un script PowerShell codificado en Base64 (UTF-16LE) que consulta `SystemIndex` via OLE DB.
- Usa `CONTAINS(System.FileName, 'token*')` por cada token (busqueda por prefijo, no substring).
- Scope: `System.ItemPathDisplay LIKE 'folder%'` por cada carpeta.
- Sanitizacion: se eliminan `'`, `"` y `*` de la query. Si queda vacia, no se lanza PowerShell.
- Flags: `-NoProfile -NonInteractive -EncodedCommand`.

> **Verificar en:** `WindowsPlatformProvider.SearchFilesAsync`.

### Linux -- plocate/locate

- Usa `/usr/bin/plocate` si existe; sino `/usr/bin/locate`.
- Solo pasa el primer token al binario con patron `*token*` y flags `-b -l maxResults`.
- Los tokens adicionales y el filtro de carpetas se aplican client-side en un callback intermedio.
- Sanitizacion: solo se eliminan `"` de la query.
- Las lineas que no pasan el filtro client-side no consumen el limite de `maxResults` del proceso OS, por lo que el numero efectivo de resultados entregados puede ser menor que `maxResults`.

> **Verificar en:** `LinuxPlatformProvider.SearchFilesAsync`.

---

## 11. Ejecucion de procesos externos (ProcessRunner)

`ProcessRunner` gestiona la ejecucion de procesos (PowerShell, plocate/locate) con lectura asincrona de stdout y stderr linea a linea.

- Lee stdout y stderr en paralelo usando un `CancellationTokenSource` vinculado al token del caller.
- Si un callback devuelve `false`, se cancela el token compartido, desbloqueando ambas lecturas.
- Al terminar (por cualquier motivo), siempre llama `Kill(entireProcessTree: true)` y luego `WaitForExitAsync` para asegurar limpieza completa.
- Los argumentos con espacios se envuelven en comillas dobles, escapando las comillas internas.

**Invariante:** ningun subproceso lanzado por `ProcessRunner` queda huerfano; siempre se mata el arbol completo al finalizar.

> **Verificar en:** `ProcessRunner.RunAsync`, `ProcessRunner.ExitProcess`.

---

## 12. Constantes configurables

| Constante | Valor | Proposito |
|---|---|---|
| `FileSearchMinQueryLength` | 2 | Caracteres minimos para iniciar busqueda |
| `FileSearchTimeoutMs` | 20,000 ms | Timeout maximo por busqueda |
| `FileSearchSnapshotIntervalMs` | 200 ms | Intervalo minimo entre snapshots |
| `SearchSourceLimit` | 500 | Limite global pasado a las fuentes; `UserDocumentSearch` lo recibe como `limit` y lo usa en el `Take(limit)` de cada snapshot |

> **Verificar en:** `AppDefaults.cs`.

---

## 13. Tests

Los tests de `UserDocumentSearch` usan un `FakePlatformProvider` que emite todos los `FileResult` de su constructor ignorando la query y las carpetas. Esto aisla la verificacion del scoring y filtrado client-side del comportamiento del SO.

Los casos de test cubren:
- Score exacto por tipo de coincidencia (stem exacto, prefijo, substring, multi-token).
- Filtrado de resultados que no contienen todos los tokens.
- Independencia del orden de los tokens en queries multi-token.

> **Verificar en:** `UserDocumentSearchTests`, `FakePlatformProvider`.
