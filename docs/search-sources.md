# Fuentes de busqueda

Este documento describe las fuentes de busqueda de Yottacast: que datos ofrece cada una al usuario, bajo que condiciones se activan y como se coordinan entre si. El algoritmo de scoring detallado de cada fuente esta documentado en `docs/search-scoring.md` (apps, emoji) y en `docs/search-files.md` (documentos).

---

## 1. Busqueda de aplicaciones

El usuario escribe un nombre (parcial o completo) y Yottacast muestra las aplicaciones instaladas que coinciden, con su icono, ordenadas por relevancia.

### Invariantes

- El usuario nunca espera a que se carguen las apps: la UI solo acepta input despues de que el cache de apps esta listo (`WhenInstantReady`).
- Las apps recien instaladas aparecen en la lista sin reiniciar Yottacast, gracias a los watchers de filesystem.
- Si el usuario no ha escrito nada, las apps recien detectadas se muestran como resultados pendientes. Si esta buscando, se refrescan los resultados instant para incluir la nueva app si coincide con la query.
- Solo se monitorizan directorios que existen en disco; los configurados pero inexistentes se ignoran silenciosamente.
- El arranque es idempotente: llamadas repetidas a `Start()` son no-op. El ciclo `Stop()` + `Start()` es valido para reinicio, aunque actualmente ningun codigo lo ejecuta.

### Escaneo por plataforma

| Plataforma | Metodo de escaneo | Watchers | Filtro |
|---|---|---|---|
| macOS | Spotlight via P/Invoke a CoreServices `MDQuery` (sincrono, no subprocess) | `FileSystemWatcher` en `*.app` | `kMDItemContentType == 'com.apple.application-bundle'` |
| Windows | Escaneo de directorios buscando `.exe` | `FileSystemWatcher` en `*.exe` | -- |
| Linux | Escaneo de directorios buscando `.desktop` | `FileSystemWatcher` en `*.desktop` | -- |

Los directorios de busqueda provienen de `UserSettings.ExpandedAppDirectories` (configurables por el usuario).

### Consultas directas al cache

Otros servicios (`BrowserDiscovery`, `TerminalDiscovery`) consultan el cache de apps sin pasar por la pipeline de busqueda:

| Metodo | Comportamiento |
|---|---|
| `Find(name)` | Busqueda exacta por clave de nombre (case-insensitive). Devuelve `AppInfo?` |
| `FindAll()` | Todas las apps en cache como `IReadOnlyList<AppInfo>` |

### Limitacion conocida

El cambio de `AppDirectories` en settings no recarga el cache de `ApplicationSearch`. Para que surta efecto, el usuario debe reiniciar la aplicacion.

> **Verificar en:** `ApplicationSearch.cs` (Start, Stop, ScanAndWatchAsync, AddApp, Search, Find, FindAll), `PlatformProvider.cs` (ScanAppsAsync, CreateAppWatchers), `MacOsPlatformProvider.cs` (ScanAppsAsync, CreateAppWatchers), `SpotlightInterop.cs` (Query).

---

## 2. Busqueda web

Yottacast permite lanzar busquedas web en multiples motores directamente desde el launcher. Los resultados son enlaces que se abren en el navegador configurado.

### Invariantes

- Las queries que empiezan por `:` (modo emoji) nunca generan resultados web.
- Cuando un motor con prefijo coincide, los motores `ShowAlways` se ocultan para evitar ruido. Solo se muestran los motores cuyo prefijo fue activado explicitamente.
- Los resultados de busqueda web no estan sujetos al limite global de resultados (`SearchSourceLimit`). Todos los motores habilitados en modo `ShowAlways` aparecen siempre, independientemente de cuantos sean. Esto se logra marcando los items con `BypassLimit = true`.
- Si el usuario ha personalizado la URL de un motor, se usa esa URL. Si no, se usa la URL por defecto del motor. Esto permite actualizar URLs por defecto entre versiones sin sobreescribir personalizaciones.
- Si falta el icono PNG embebido de un motor, el hueco del icono queda vacio sin error.
- Motores anadidos en versiones futuras aparecen automaticamente para usuarios existentes (merge de settings al cargar).

### Modos de activacion

| Modo | Cuando aparece | Score | Ejemplo |
|---|---|---|---|
| `ShowAlways` | Siempre (query no vacia, sin prefijo activo de otro motor) | 3.0 | Escribir "hola" muestra "Google: hola" |
| `PrefixOnly` | Solo si la query empieza por `"{prefijo} "` (prefijo + espacio) | 3.5 | Escribir "y gatos" muestra "YouTube: gatos" |

### Titulo del resultado

El formato es `"{NombreMotor}: {queryBusqueda}"`, p. ej. `"Google: hola"`. El subtitulo siempre es `"Open in browser"`.

### Motores disponibles por defecto

Los motores predefinidos cubren categorias generales (Google, Bing, DuckDuckGo), shopping (Amazon), video (YouTube, Twitch), social (Reddit, X, LinkedIn, Pinterest, TikTok), conocimiento (Wikipedia, Wolfram Alpha), desarrollo (GitHub, Stack Overflow, npm, PyPI, MDN), entretenimiento (IMDb, Spotify) y mapas (Google Maps). Cada uno tiene un prefijo por defecto y puede estar habilitado o deshabilitado de fabrica.

> **Verificar en:** `WebSearchSource.cs` (Search, LoadIcons), `WebSearchEngine.cs` (WebSearchDefaults.Engines, DefaultSettingsFor), `UserSettings.cs` (MergeWebSearchEngines).

---

## 3. Busqueda de documentos del usuario

Busca archivos en las carpetas configuradas del usuario (Downloads, Desktop, Documents, etc.) usando el indice nativo del sistema operativo.

### Invariantes

- Nunca se lanza una busqueda de archivos si la query tiene menos de 2 caracteres.
- Las queries que empiezan por `:` (modo emoji) nunca activan la busqueda de archivos (las fuentes deferred no se lanzan en modo emoji).
- La busqueda de archivos tiene un timeout de 20 segundos. Si Spotlight (u otro backend) no termina a tiempo, se cancelan y se muestran los resultados parciales obtenidos hasta ese momento.
- Los resultados se emiten progresivamente: como maximo un snapshot cada 200 ms, mas un snapshot final al terminar o cancelarse.
- Si la query contiene `*`, se usa como wildcard (se salta la tokenizacion y scoring multi-token; score fijo 0.5).
- Cada archivo muestra un badge con el icono de la aplicacion por defecto para su extension, salvo que el badge sea identico al icono principal del archivo (comparacion semantica via Info.plist en macOS).

### Backend por plataforma

| Plataforma | Backend | Metodo |
|---|---|---|
| macOS | Spotlight via P/Invoke a CoreServices `MDQuery` | Predicate `kMDItemFSName == '*query*'cd` |
| Windows | Windows Search Index | (pendiente de implementacion completa) |
| Linux | plocate / locate | (pendiente de implementacion completa) |

### Iconos de badge por extension

Para cada archivo encontrado, se intenta cargar el icono de la aplicacion por defecto asociada a su extension. El badge se suprime en dos casos:
1. La ruta de la app por defecto es la misma que la del archivo (el archivo ES una app).
2. La app registra un icono de documento personalizado para esa extension en su `Info.plist` (campo `CFBundleTypeIconFile` en `CFBundleDocumentTypes`), lo que indica que macOS ya usa el icono de la app como icono del documento.

> **Verificar en:** `UserDocumentSearch.cs` (SearchAsync, PreloadBadgeIconAsync), `FileSearch.cs` (SearchAsync), `MacOsPlatformProvider.cs` (SearchFilesAsync, AreIconsSame, GetDefaultAppPath), `SpotlightInterop.cs` (Query), `AppDefaults.cs` (FileSearchMinQueryLength, FileSearchTimeoutMs, FileSearchSnapshotIntervalMs).

---

## 4. Orquestacion de la busqueda (GlobalSearch)

`GlobalSearch` coordina todas las fuentes registradas. Las fuentes se dividen en dos categorias:

| Categoria | Comportamiento | Ejemplos |
|---|---|---|
| Instant (`IInstantSearchSource`) | Respuesta sincrona, en memoria. Se consultan sin delay. | Apps, Web, Calculadora, Emoji |
| Deferred (`IDeferredSearchSource`) | Respuesta asincrona con snapshots progresivos. Se lanzan tras un debounce. | Documentos |

### Invariantes del ciclo de vida

- `Start()` inicia todas las fuentes (fire-and-forget). Cada fuente arranca en background.
- `WhenInstantReady()` completa cuando todas las fuentes instant estan listas. La UI no acepta input antes de este punto.
- `WhenReady()` completa cuando todas las fuentes (instant + deferred) estan listas.
- `Stop()` cancela y limpia todas las fuentes en paralelo.

### Contrato de las interfaces

| | `IInstantSearchSource` | `IDeferredSearchSource` |
|---|---|---|
| `Start()` | `void` — fire-and-forget | `void` — fire-and-forget |
| `WhenReady()` | `Task` — completa cuando esta lista | `Task` — completa cuando esta lista |
| `Stop()` | `Task` | `Task` |
| Busqueda | `Search(query, limit)` → `IReadOnlyList<BaseResultItemViewModel>` (sincrono) | `SearchAsync(query, limit, ct)` → `IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>>` (cada elemento es un snapshot completo) |

### Busqueda instant con hints

`SearchInstant` devuelve una tupla `(Items, Hint)`. Los items son los resultados ordenados por score y limitados. El hint es un texto opcional proporcionado por fuentes que implementan `ISearchHintProvider` (p. ej. la calculadora), que la UI muestra como sugerencia bajo el campo de busqueda.

### Merge de resultados deferred por slots

Cada fuente deferred ocupa un slot. Cuando cualquier fuente emite un nuevo snapshot, su slot se actualiza y se emite la union ordenada de todos los slots. Asi la UI refleja la mejor combinacion disponible en cada instante, incluso si una fuente es mas lenta que otra.

La coordinacion interna usa un `Channel` unbounded. Cada tarea de fuente se inicia con `CancellationToken.None` (la cancelacion llega via el token pasado a `SearchAsync`). Las `OperationCanceledException` se capturan silenciosamente dentro de cada tarea.

> **Verificar en:** `GlobalSearch.cs` (SearchInstant, SearchDeferredAsync, SearchSourcesAsync), `IInstantSearchSource.cs`, `IDeferredSearchSource.cs`, `ISearchHintProvider.cs`.

---

## 5. Flujo de busqueda en la UI

Cuando el usuario escribe en el campo de busqueda, la UI ejecuta un flujo en dos fases.

### Fase 1 -- Instant (sin delay)

1. Se cancela cualquier busqueda anterior.
2. Si el texto esta vacio: se limpian los snapshots, se muestran las apps pendientes (recien instaladas) y se termina.
3. Si el texto no esta vacio: se limpian las apps pendientes y se consultan todas las fuentes instant.
4. Se actualizan los resultados en pantalla.
5. Si la query empieza por `:` (modo emoji): se detiene aqui. Las fuentes deferred no se lanzan.

### Fase 2 -- Deferred (debounce 250 ms)

6. Se espera 250 ms. Si el usuario sigue escribiendo, la espera se cancela y se vuelve al paso 1.
7. Se crea un CTS vinculado para poder cancelar la busqueda deferred independientemente.
8. Se iteran los snapshots de las fuentes deferred, actualizando la UI en cada snapshot.
9. Al terminar (si no fue cancelada), se muestra "sin resultados" si la lista esta vacia.

### Merge y seleccion de resultados

Los resultados instant y deferred se combinan, se ordenan por score descendente y se muestran. La logica de seleccion automatica:
- Si hay un resultado de calculadora/conversion y el usuario no ha navegado manualmente, se auto-selecciona.
- Si el usuario navego manualmente y su seleccion anterior sigue en la lista, se preserva.
- En caso contrario, se selecciona el primer resultado.

### Limites

Cada fuente recibe un limite de 10 resultados. El merge global tambien se limita a 10 resultados visibles (configurable en `AppDefaults.SearchSourceLimit`).

> **Verificar en:** `MainWindowViewModel.cs` (OnSearchTextChanged, SearchAsync, RefreshResults, OnNewAppInstalled, OnAppCacheChanged), `AppDefaults.cs` (SearchSourceLimit, SearchDebouncedMs).

---

## 6. Iconos de aplicaciones

Los iconos de apps se gestionan en dos capas: carga (plataforma + cache) y renderizado (UI).

### Cache de dos niveles (AppIconCache)

| Nivel | Estructura | Acceso |
|---|---|---|
| Memoria | `ConcurrentDictionary<string, byte[]?>` por ruta de bundle | `Get(appPath)` — O(1), se llama en cada `Search()` |
| Disco | `~/.cache/yottacast/app-icons/{sha1(ruta)}_{mtime_unix}_v2.png` | Consultado en `PreloadAsync` antes de llamar a la plataforma |

### Invariantes

- El sufijo `_v2` y el mtime en el nombre del fichero invalidan la entrada automaticamente cuando la app se actualiza.
- `PreloadAsync` ignora llamadas duplicadas para la misma ruta (comprueba `ContainsKey` antes de lanzar la tarea).
- `Reload` invalida la entrada en memoria y relanza la carga. Se usa cuando una app conocida es re-detectada por el watcher (p. ej. el bundle seguia copiandose al primer evento).
- Cuando un icono termina de cargarse con bytes no nulos, se dispara `IconLoaded`. La UI se suscribe y re-ejecuta `SearchInstant` en el hilo UI para refrescar los iconos visibles.
- Los archivos huerfanos de versiones anteriores en la cache de disco no se limpian activamente.

### Obtencion del icono por plataforma

- **macOS**: usa `NSWorkspace.iconForFile:` via P/Invoke a `libobjc.dylib`. Dibuja la imagen en un `NSImage` de 64x64 puntos (128x128 pixeles en Retina), extrae `TIFFRepresentation`, convierte a PNG via `NSBitmapImageRep` y devuelve los bytes.
- **Windows y Linux**: no implementado, devuelve `null`.

### Renderizado en la UI

`PathToAppIconConverter` recibe `byte[]?` de `IconBytes` y devuelve un `Bitmap` de Avalonia. Usa `ConditionalWeakTable<byte[], Bitmap>` como cache para que los bitmaps se liberen junto con los bytes que los originaron. Si `IconBytes` es `null`, se muestra el emoji fallback.

> **Verificar en:** `AppIconCache.cs` (PreloadAsync, Reload, Load, DiskCachePath), `MacOsPlatformProvider.cs` (GetAppIconBytes), `PathToAppIconConverter.cs`, `ApplicationSearch.cs` (CreateResultItem, IconLoaded).

---

## 7. RandomSearch (solo testing)

`RandomSearch` es una fuente deferred de prueba. Esta registrada como singleton en el contenedor DI pero la linea que la conecta como `IDeferredSearchSource` esta comentada en `App.axaml.cs`. Emite hasta 5 resultados con scores aleatorios (0.5 a 1.0) con delays progresivos (200 ms + 50 ms por resultado). Cada snapshot es acumulativo.

> **Verificar en:** `RandomSearch.cs`, `App.axaml.cs` (linea comentada de registro DI).
