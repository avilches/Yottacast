# Iconos en resultados de ficheros

## Proposito

Cada resultado de busqueda de ficheros muestra hasta dos iconos: uno grande (el tipo de fichero) y un badge pequeno (la
app que lo abre). Obtener estos iconos del sistema operativo es lento, asi que se cachean para no bloquear la busqueda.

---

## 1. Dos iconos por resultado

| Icono               | Que representa                   | De donde sale                                                             | Cache                                                                | Cuando se recalcula                               |
|---------------------|----------------------------------|---------------------------------------------------------------------------|----------------------------------------------------------------------|---------------------------------------------------|
| **Grande**          | Tipo de fichero                  | `NSWorkspace.iconForFile:` via `PlatformProvider.GetFileIconBytes`        | `FileIconCache` (memoria + disco, por extension)                     | Cuando cambia una app instalada (`InvalidateAll`) |
| **Badge** (pequeno) | App predeterminada para ese tipo | `PlatformProvider.GetDefaultAppPath` + `PlatformProvider.GetAppIconBytes` | `UserDocumentSearch._badgeByExtension` (memoria + disco, por extension) | Cuando cambia una app instalada (`InvalidateAll`) |

El icono grande depende indirectamente de la app predeterminada: `NSWorkspace.iconForFile:` devuelve el icono generico
del sistema para ese tipo de fichero (UTI), **salvo** que la app predeterminada registre un `CFBundleTypeIconFile` en su
`Info.plist`. En ese caso, macOS usa el icono custom de la app como icono del fichero.

Ejemplos:

- `.swift` → icono de Xcode (registra `CFBundleTypeIconFile`)
- `.cs` → icono de Rider (registra `CFBundleTypeIconFile`)
- `.pdf` → icono generico de PDF, independiente de que app sea la predeterminada
- `.json` → icono generico, salvo que VS Code sea default y registre icono custom

### Supresion del badge

El badge se suprime cuando mostrarlo seria redundante: si la app predeterminada registra un `CFBundleTypeIconFile` para
la extension, el icono grande YA ES el icono de esa app, asi que el badge repetiria la misma informacion. `AreIconsSame`
comprueba exactamente esto leyendo `CFBundleDocumentTypes` del `Info.plist`.

Tambien se suprime cuando el fichero ES una app (la ruta del fichero coincide con la de la app predeterminada).

> **Verificar en:** `UserDocumentSearch.PreloadBadgeIconAsync` (logica de supresion),
`MacOsPlatformProvider.AreIconsSame` (lectura de `CFBundleDocumentTypes`).

---

## 2. Cache del icono grande (`FileIconCache`)

### Clave por extension

La cache esta indexada por **extension de fichero** (en minusculas, sin punto). Todos los ficheros del mismo tipo
comparten una sola entrada.

| Fichero                        | Clave   |
|--------------------------------|---------|
| `/Users/foo/Main.java`         | `java`  |
| `/tmp/report.PDF`              | `pdf`   |
| `/docs/README` (sin extension) | `_none` |

**Invariante:** dos ficheros con la misma extension siempre muestran el mismo icono grande.

> **Verificar en:** `FileIconCache.ExtKey`.

### Dos niveles de cache

| Nivel   | Soporte                                          | Alcance                 |
|---------|--------------------------------------------------|-------------------------|
| Memoria | `ConcurrentDictionary<string, byte[]?>`          | Sesion actual           |
| Disco   | PNG por extension en `AppPaths.FileIconCacheDir` | Persiste entre sesiones |

El flujo de resolucion es:

1. Si la clave esta en memoria, devuelve los bytes inmediatamente.
2. Si no, busca el fichero PNG en disco. Si existe, lo carga en memoria y lo devuelve.
3. Si tampoco esta en disco, encola una carga asincrona via la plataforma (NSWorkspace).

**Invariante:** una vez que un tipo de fichero ha sido cargado, todas las consultas posteriores en la misma sesion son
instantaneas (hit en memoria).

> **Verificar en:** `FileIconCache.GetOrPreload`, `FileIconCache.Load`, `FileIconCache.TryDiskCache`.

### Cache en disco

Los iconos se guardan en `AppPaths.FileIconCacheDir` (`~/.cache/yottacast/file-icons/`). Un fichero por extension:

```
{ext}_{version}.png
```

Ejemplos: `java_v1.png`, `pdf_v1.png`, `_none_v1.png`.

El sufijo de version (`CacheVersion` en `FileIconCache`) permite invalidar toda la cache de forma controlada: al cambiar
la constante, los ficheros de la version anterior quedan huerfanos y se ignoran automaticamente.

**Invariante:** al reiniciar la aplicacion, los iconos de tipos ya vistos aparecen desde el primer snapshot sin llamar
al sistema operativo.

> **Verificar en:** `FileIconCache.DiskCachePath`, `FileIconCache.CacheVersion`, `AppPaths.FileIconCacheDir`.

---

## 3. Cache del badge (`_badgeByExtension`)

El badge se cachea en **dos niveles** dentro de `UserDocumentSearch`, igual que el icono grande:

| Nivel   | Soporte                                          | Alcance                 |
|---------|--------------------------------------------------|-------------------------|
| Memoria | `ConcurrentDictionary<string, byte[]?>`          | Sesion actual           |
| Disco   | PNG por extension en `AppPaths.BadgeIconCacheDir`| Persiste entre sesiones |

El flujo de `PreloadBadgeIconAsync` es:

1. Si la extension ya esta en memoria → no hace nada.
2. Si hay PNG en disco → lo carga en memoria sincrónamente (el primer snapshot ya tiene el badge).
3. Si no hay caché → lanza en background:
    - `GetDefaultAppPath(filePath)` → obtiene la ruta de la app predeterminada.
    - Si no hay app, o el fichero ES la app, o `AreIconsSame` indica que el icono grande ya es el de la app → guarda `null` (badge suprimido).
    - Si no → `GetAppIconBytes(appPath)` → guarda en memoria, escribe PNG en disco, dispara `BadgeIconLoaded`.
4. Al recibir `BadgeIconLoaded`, el ViewModel actualiza los items visibles en el hilo UI.

La cache de disco se invalida (junto con la de iconos de fichero) cuando cambian las apps instaladas.

> **Verificar en:** `UserDocumentSearch.PreloadBadgeIconAsync`, `UserDocumentSearch.TryBadgeDiskCache`,
`UserDocumentSearch.InvalidateAll`, `UserDocumentSearch.RefreshIconBytes`,
`MainWindowViewModel.OnBadgeIconLoaded`.

---

## 4. Carga sincrona vs asincrona del icono grande

`GetOrPreload(filePath)` tiene dos caminos:

- **Cache hit (memoria o disco):** devuelve los bytes en el mismo hilo. El `ResultItemViewModel` se construye ya con
  `IconBytes` relleno.
- **Cache miss:** encola la carga en el thread pool (llamada a NSWorkspace) y devuelve `null`. El item se construye sin
  icono y se rellenara despues via `IconLoaded`.

`GetOrPreload` solo se llama para los **top-N items** del snapshot, no para todos los ficheros encontrados por `mdfind`.

**Invariante:** la emision de snapshots nunca espera a que los iconos esten disponibles. Los iconos nunca bloquean la
busqueda.

> **Verificar en:** `FileIconCache.GetOrPreload`, `UserDocumentSearch.SearchAsync` (bloques de snapshot y snapshot
> final).

---

## 5. Actualizacion reactiva de la UI

Cuando una carga asincrona via NSWorkspace termina con exito, `FileIconCache` dispara el evento `IconLoaded`.
`MainWindowViewModel` esta suscrito a este evento y ejecuta en el hilo UI:

1. Itera `_deferredSnapshot` buscando items con `IconBytes == null`.
2. Para cada uno, llama a `fileIconCache.Get(item.Subtitle)` (la ruta es el subtitle).
3. Si el icono ya esta en memoria, lo asigna al item.
4. Llama a `RefreshResults()` para que la UI refleje los cambios.

**Invariante:** los iconos que no estaban en cache aparecen en la UI en cuanto NSWorkspace los carga, sin que el usuario
tenga que repetir la busqueda.

> **Verificar en:** `MainWindowViewModel.OnFileIconLoaded`, `MainWindowViewModel.Initialize` (suscripcion a
`fileIconCache.IconLoaded`), `FileIconCache.Load` (disparo de `IconLoaded`).

---

## 6. Invalidacion por cambio de app

Los iconos cacheados (tanto el grande como el badge) pueden quedar obsoletos si el usuario instala o elimina una app
que maneja ese tipo de fichero.

Yottacast detecta esto via el `FileSystemWatcher` de `ApplicationSearch`: cuando un bundle `.app` cambia en los
directorios de apps (post-arranque), `ApplicationSearch` dispara el evento `AppsChanged`. `MainWindowViewModel` lo
suscribe a `FileIconCache.InvalidateAll` y a `UserDocumentSearch.InvalidateAll`, que limpian toda la cache de memoria y
disco de iconos grandes y badges respectivamente. Ambos se recargan lazy en la siguiente busqueda.

La invalidacion es **total** (no por extension): cualquier cambio en una app limpia toda la cache. Esto es aceptable
porque los cambios de apps son infrecuentes y la recarga es transparente para el usuario.

La invalidacion solo se dispara desde los callbacks del `FileSystemWatcher` (cambios en caliente), nunca durante el scan
inicial de arranque. De este modo, reiniciar la aplicacion aprovecha la cache de disco.

**Gap conocido y aceptado**: cambiar la app predeterminada via "Open With → Always Open With" no modifica ningun bundle
en disco, por lo que el `FileSystemWatcher` no lo detecta. Tanto el icono grande como el badge pueden quedar
desactualizados hasta el siguiente cambio de app instalada.

**Invariante:** tras instalar o eliminar una app, los iconos grandes y badges se recargan en la siguiente busqueda, sin
necesidad de reiniciar.

> **Verificar en:** `ApplicationSearch.AppsChanged` (evento), `FileIconCache.InvalidateAll`,
`UserDocumentSearch.InvalidateAll`, `MainWindowViewModel.Initialize` (suscripciones),
`ApplicationSearch.ScanAndWatchAsync` (callbacks del watcher).
