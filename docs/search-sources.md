# Fuentes de busqueda

Este documento describe las fuentes de busqueda de Yottacast: que datos ofrece cada una al usuario, bajo que condiciones se activan y como se coordinan entre si. El algoritmo de scoring detallado de cada fuente esta documentado en `docs/search-scoring.md` (apps, emoji) y en `docs/search-files.md` (documentos).

---

## 1. Busqueda de aplicaciones

El usuario escribe un nombre (parcial o completo) y Yottacast muestra las aplicaciones instaladas que coinciden, con su icono, ordenadas por relevancia.

### Invariantes

- El usuario nunca espera a que se carguen las apps: la UI solo acepta input despues de que el cache de apps esta listo (`WhenInstantReady`).
- Las apps recien instaladas aparecen en la lista sin reiniciar Yottacast, gracias a los watchers de filesystem.
- Si el usuario no ha escrito nada, las apps recien detectadas se muestran via `NewlyInstalledAppsSource` (ver `docs/ui-main-window.md`). Si esta buscando, se refrescan los resultados instant para incluir la nueva app si coincide con la query.
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

### Rescan al cambiar AppDirectories

Al cambiar `AppDirectories` en Settings, la notificacion se difiere hasta que el usuario sale de la seccion AppSearch o cierra Settings. Esto evita refrescos innecesarios mientras el usuario añade/quita carpetas rapidamente. Cuando se notifica, `ApplicationSearch` re-escanea las carpetas nuevas mediante `RescanAsync()` y `BrowserDiscovery`/`TerminalDiscovery` invalidan su cache. El rescan hace un diff contra la cache actual: solo las apps genuinamente nuevas disparan `AppAdded`, las apps de carpetas eliminadas desaparecen de la cache, y las que ya existian se mantienen sin eventos.

> **Verificar en:** `ApplicationSearch.cs` (Start, Stop, ScanAndWatchAsync, RescanAsync, AddApp, Search, Find, FindAll), `UserSettings.cs` (AppDirectoriesChanged, NotifyAppDirectoriesChanged), `SettingsWindowViewModel.cs` (FlushAppDirectoryChanges), `PlatformProvider.cs` (ScanAppsAsync, CreateAppWatchers), `MacOsPlatformProvider.cs` (ScanAppsAsync, CreateAppWatchers), `SpotlightInterop.cs` (Query).

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
| Instant (`IInstantSearchSource`) | Respuesta sincrona, en memoria. Se consultan sin delay. | Apps, Web, Calculadora, Fechas, Emoji |
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

## 7. Búsqueda de paneles de System Settings (macOS 13+)

Permite al usuario buscar y abrir paneles y sub-secciones de System Settings directamente desde el launcher. Solo disponible en macOS 13+ (Ventura).

### Invariantes

- Solo se activa en macOS. En otras plataformas la fuente no se registra y no genera resultados.
- Si `EnableSystemSettings = false`, `Search()` devuelve `[]` siempre.
- Los paneles compiten por score con el resto de resultados usando `NameMatcher` (mismo algoritmo que apps, rango 0.0–1.0).
- Las queries que empiezan por `:` (modo emoji) no activan esta fuente.
- Al activar un resultado, abre System Settings en el panel o sub-sección correspondiente via URL scheme `x-apple.systempreferences:{identifier}` (con anchor opcional: `bundle?anchor`). Si un anchor no está soportado por la versión de macOS actual, `open` abre el panel padre — degradación silenciosa, sin error.
- Paneles de terceros con el mismo `CFBundleIdentifier` que uno builtin se omiten para evitar duplicados.

### Datos de paneles

- **Builtin**: ~110 entradas estáticas definidas en `BuiltinPanels.cs`, organizadas en dos grupos:
  - Paneles de primer nivel (~45): abren el panel raíz.
  - Sub-secciones (~65): tienen `ParentName` y usan anchors en el URL identifier (p.ej. `com.apple.preference.security?Privacy_Camera`). Verificados en macOS Ventura 13 / Sonoma 14.
- **Terceros**: se escanean `/Library/PreferencePanes/` y `~/Library/PreferencePanes/` en startup. El nombre se extrae del `Info.plist` del bundle (`CFBundleDisplayName` → `CFBundleName` → nombre de fichero). La lectura del plist usa `XDocument` con `DtdProcessing.Ignore` para no realizar peticiones de red al DTD de Apple.

### Items dinámicos

En cada llamada a `Search()`, se generan items adicionales basados en el estado actual del sistema:

| Condición | Item | Subtítulo |
|-----------|------|-----------|
| Wi-Fi conectada a "MyNet" | `"Wi-Fi · MyNet"` | `"System Settings › Network"` |
| VPN "Work VPN" activa | `"VPN · Work VPN"` | `"System Settings › Network"` |

Los items dinámicos se cachean 10 s (ver `AppDefaults.SystemSettingsDynamicCacheTtl`) para no añadir latencia al tipado. Si la consulta al sistema falla, no aparecen items dinámicos (solo estáticos).

### Resultado visible

| Campo | Panel primer nivel | Sub-sección builtin | Tercero |
|-------|-------------------|---------------------|---------|
| Título | nombre del panel | nombre de la sub-sección | nombre del bundle |
| Subtítulo | `"System Settings"` | `"System Settings › {ParentName}"` | `"System Settings · Preference Pane"` |
| Categoría | `"System Settings"` | `"System Settings"` | `"System Settings"` |
| Icono | icono de System Settings.app | icono de System Settings.app | icono de System Settings.app |

### Verificación de anchors

Al actualizar macOS, ejecutar `tools/verify-settings-anchors.sh` para verificar visualmente que cada anchor navega a la sección correcta. El script abre cada URL con 1 s de delay entre ellas.

> **Verificar en:** `Search/SystemSettings/SystemSettingsSearch.cs` (Start, Search, GetDynamicPanels, Load, TryReadPlist, BuildResult), `Search/SystemSettings/BuiltinPanels.cs`, `Platform/PlatformProvider.cs` (GetCurrentWifiNetworkName, GetActiveVpnNames), `Platform/MacOsPlatformProvider.cs` (GetCurrentWifiNetworkName, GetActiveVpnNames), `AppDefaults.cs` (SystemSettingsDynamicCacheTtl), `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs`.

---

## 8. Búsqueda de fechas (DateSearch)

Detecta fechas y rangos de fechas en lenguaje natural dentro de la query y presenta un resultado con múltiples formatos copiables. Se activa sin prefijo, igual que la calculadora: si la query no contiene ninguna fecha reconocible, la fuente no genera ningún resultado.

Para fechas simples devuelve 2 celdas: ISO (`yyyy-MM-dd`) y formato largo localizado. Para rangos devuelve 4 celdas: ISO inicio, ISO fin, intervalo ISO 8601 (`inicio/fin`) y el texto original reconocido. El subtítulo muestra la distancia al día actual ("hoy", "mañana", "dentro de N días") o la duración del rango ("N días"). Las teclas ← → navegan entre celdas de forma circular; Enter o Cmd+C copian la celda seleccionada.

El reconocedor admite 11 idiomas configurables (`DateSearchLanguages`). Por defecto están activos español (`es-es`) e inglés (`en-us`). Si ningún idioma detecta una fecha en la query, la fuente devuelve `[]`.

### Invariantes

- Si `DateSearchEnabled = false` → devuelve `[]` siempre.
- Si `DateSearchLanguages` está vacío → devuelve `[]`.
- Produce como máximo un resultado por búsqueda.
- Los errores del reconocedor externo se capturan y loguean sin propagar la excepción.

> **Verificar en:** `Search/Date/DateSearch.cs` (Search, BuildDateViewModel, BuildDateRangeViewModel), `ViewModels/DateSearchResultViewModel.cs` (Cells, SelectedCell, MoveCellLeft, MoveCellRight), `Services/UserSettings.cs` (DateSearchEnabled, DateSearchLanguages), `AppDefaults.cs` (DateSearchScore, DateSearchDefaultLanguages, DateSearchAvailableLanguages), `Yottacast.Core.Tests/Search/Date/DateSearchTests.cs`.

---

## 9. RandomSearch (solo testing)

`RandomSearch` es una fuente deferred de prueba. Esta registrada como singleton en el contenedor DI pero la linea que la conecta como `IDeferredSearchSource` esta comentada en `App.axaml.cs`. Emite hasta 5 resultados con scores aleatorios (0.5 a 1.0) con delays progresivos (200 ms + 50 ms por resultado). Cada snapshot es acumulativo.

> **Verificar en:** `RandomSearch.cs`, `App.axaml.cs` (linea comentada de registro DI).
