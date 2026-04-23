# Preferencias del usuario

Las preferencias del usuario controlan el comportamiento del launcher: que browser y terminal se usan, el atajo de teclado global, las carpetas donde buscar aplicaciones y archivos, los motores de busqueda web, y las fuentes opcionales (calculadora, clipboard, emoji). Se persisten en un fichero JSON que la aplicacion gestiona de forma autonoma.

---

## 1. Almacenamiento y ciclo de vida

El fichero de preferencias se crea automaticamente en la primera ejecucion y se reescribe en cada arranque. Si el fichero no existe, esta corrupto o contiene JSON invalido, la aplicacion crea valores por defecto sin mostrar error ni interrumpir el arranque.

| Plataforma | Ruta del fichero |
|---|---|
| macOS | `~/Library/Application Support/Yottacast/settings.json` |
| Windows | `%APPDATA%\Yottacast\settings.json` |

**Invariantes:**

- El usuario nunca ve un error si el fichero de settings falta o es invalido; siempre se regenera con defaults.
- El directorio padre se crea automaticamente si no existe.
- Los settings se guardan en el momento en que cambian. La posición de la ventana (`WindowX`/`WindowY`) solo se persiste si el usuario la arrastró desde el último guardado — si la posición no cambió, el hide no genera ninguna escritura a disco.
- Si la escritura a disco falla (permisos, disco lleno), los cambios se mantienen en memoria pero no se persisten hasta el siguiente guardado exitoso. No se propaga excepcion.
- Solo existe una instancia de settings en toda la vida de la aplicacion (singleton). No hay recarga desde disco.
- La unica via de creacion es el metodo de fabrica `Load()`; el constructor es privado.

> **Verificar en:** `UserSettings.Load()`, `UserSettings.Save()`, constructor privado de `UserSettings` -- en `Yottacast.Core/Services/UserSettings.cs`. Rutas definidas en `AppPaths` -- en `Yottacast.Core/AppPaths.cs`. Registro DI singleton en `App.BuildServices()` -- en `Yottacast/App.axaml.cs`.

---

## 2. Preferencias disponibles

| Preferencia | Valor por defecto | Descripcion |
|---|---|---|
| Browser | `""` (auto-selecciona el primero disponible) | Navegador preferido para abrir URLs |
| Terminal | `""` (auto-selecciona el primero disponible) | Terminal preferido |
| Theme | Deteccion automatica del SO | Tema visual (oscuro o claro) |
| Hotkey | `Alt+Space` | Atajo global para mostrar/ocultar el launcher |
| SearchFolders | Carpetas por defecto de la plataforma | Directorios donde buscar archivos del usuario |
| AppDirectories | Directorios por defecto de la plataforma | Directorios donde buscar aplicaciones |
| EnableAppSearch | `true` | Activa/desactiva la busqueda de aplicaciones |
| EnableCalculator | `true` | Toggle de la fuente calculadora |
| EnableClipboard | `true` | Toggle de la fuente clipboard |
| EnableEmoji | `true` | Toggle de la fuente emoji |
| EnableFileSearch | `true` | Activa/desactiva la busqueda de ficheros |
| EnableWebSearch | `true` | Activa/desactiva la busqueda web; si `false`, `WebSearchSource.Search()` devuelve siempre lista vacia |
| FileSearchOnlySpecificFolders | `false` | Si `true`, solo busca en las carpetas configuradas en `SearchFolders`; si `false`, busca en toda la home |
| LastLaunchedVersion | `""` | Version del ultimo arranque (para migraciones) |
| ShowDisabledWebSearchEngines | `true` | Si muestra los motores deshabilitados en la UI de Settings |
| WebSearchEngines | Lista predeterminada de 20 motores | Configuracion por motor de busqueda web |
| DictionaryLanguages | `["en"]` | Idiomas en los que buscar definiciones de diccionario |
| WindowX | `null` | Posicion X de la ventana principal en coordenadas de pantalla (pixels fisicos) |
| WindowY | `null` | Posicion Y de la ventana principal en coordenadas de pantalla (pixels fisicos) |

**Nota sobre `EnableClipboard`**: se expone en Settings y se persiste en JSON, pero no tiene efecto funcional porque no existe una fuente de búsqueda de clipboard todavía.

**Nota sobre `EnableAppSearch`**: cuando es `false`, `ApplicationSearch.Start()` marca la fuente como ready inmediatamente (sin escanear) y `Search()` devuelve siempre una lista vacia.

> **Verificar en:** campos de `UserSettings` y `UserSettingsData` en `Yottacast.Core/Services/UserSettings.cs`. Registro incondicional de fuentes en `App.BuildServices()` -- en `Yottacast/App.axaml.cs`.

---

## 3. Aplicacion de defaults al cargar

Cuando se carga el fichero JSON, los defaults de plataforma se aplican de forma selectiva, no globalmente. El objetivo es respetar siempre lo que el usuario haya configurado, y solo rellenar lo que falte.

| Campo | Cuando se aplica el default |
|---|---|
| Theme | Si el valor del JSON es `null` o `""` --> usa la deteccion automatica del SO |
| Hotkey | Si el valor del JSON es `null` o `""` --> usa `"Alt+Space"` |
| SearchFolders | Si la lista es `null` o vacia (0 elementos) --> usa los defaults de la plataforma, filtrados a los que existen en disco en ese momento |
| AppDirectories | Si la lista es `null` o vacia (0 elementos) --> usa los defaults de la plataforma |
| Browser / Terminal | Sin default en la carga; se cargan tal cual del JSON. La seleccion del primero disponible ocurre al acceder a `ActiveBrowser`/`ActiveTerminal` |
| WebSearchEngines | Se fusionan con la lista predeterminada: se conservan las personalizaciones del usuario y se anaden automaticamente los motores nuevos |

**Invariantes:**
- Si el JSON contiene al menos un elemento en `SearchFolders` o `AppDirectories`, se respeta esa lista como origen, sin mezclar con defaults.
- Independientemente del origen (JSON o defaults), ambas listas se normalizan al cargar: se elimina la barra final (`/` o `\`) de cada ruta y se deduplicanen con comparacion case-insensitive.

> **Verificar en:** `UserSettings.Load()` y `CreateDefaultUserSettings()` en `Yottacast.Core/Services/UserSettings.cs`. `PlatformProvider.DefaultTheme()` en `Yottacast.Core/Platform/PlatformProvider.cs`.

---

## 4. Normalizacion y expansion de rutas

Las rutas en `SearchFolders` y `AppDirectories` se almacenan en crudo en el JSON (con `$HOME`, `~`, o rutas absolutas). Al cargar, se aplica una normalizacion: se elimina la barra final y se deduplicanen (ver seccion 3). La expansion a rutas absolutas ocurre siempre en el momento de uso, nunca al cargar ni al guardar.

| Entrada | Resultado |
|---|---|
| `$HOME` o `~` | Directorio home del usuario |
| `$HOME/path` o `~/path` | Home + path |
| Cualquier otro valor | Sin modificacion |

**Colapso al anadir desde el picker**: cuando el usuario selecciona una carpeta con el picker nativo del SO, la ruta absoluta devuelta se colapsa automaticamente antes de guardar: si empieza por el directorio home del usuario, se sustituye por `$HOME`. Esto hace que el JSON sea portable y que la UI muestre el path colapsado. El colapso se aplica en `AddSearchFolder` y `AddAppDirectory` del ViewModel via `PlatformProvider.CollapseHomePath()`.

Las propiedades `ExpandedSearchFolders` y `ExpandedAppDirectories` proporcionan las listas ya expandidas para uso directo por las fuentes de busqueda.

> **Verificar en:** `PlatformProvider.ExpandPath()` y `PlatformProvider.CollapseHomePath()` en `Yottacast.Core/Platform/PlatformProvider.cs`. `AddSearchFolder()` y `AddAppDirectory()` en `Yottacast/ViewModels/SettingsWindowViewModel.cs`. Propiedades `ExpandedSearchFolders`/`ExpandedAppDirectories` en `Yottacast.Core/Services/UserSettings.cs`.

---

## 5. Deteccion automatica de tema

Al arrancar por primera vez (o si el campo `theme` del JSON esta vacio), la aplicacion consulta el modo del sistema operativo y selecciona un tema:

| Estado del SO | Tema seleccionado |
|---|---|
| Modo oscuro activo | `dark-default` |
| Modo claro activo | `light-gray` |
| No se puede determinar (`null`) | `dark-default` |

Este valor se persiste en el JSON. En arranques posteriores, se respeta el valor guardado sin volver a consultar el SO.

**Invariante:** el DTO de serializacion (`UserSettingsData`) usa `""` como default para `Theme`, mientras que la logica de dominio aplica `DefaultTheme()` si el valor es vacio. Esto permite que la deteccion de plataforma funcione sin que el DTO dependa de ella.

> **Verificar en:** `PlatformProvider.DefaultTheme()` y `IsSystemDarkMode()` en `Yottacast.Core/Platform/PlatformProvider.cs`. Logica de asignacion en `UserSettings.Load()`.

---

## 6. Atajo de teclado global (Hotkey)

El usuario configura una combinacion de teclas para mostrar/ocultar el launcher. El cambio tiene efecto inmediato, sin reiniciar la aplicacion.

### Formato y parsing

La hotkey se almacena como texto legible (p.ej. `"Alt+Space"`, `"Ctrl+Shift+F1"`). El parsing es case-insensitive y acepta alias:

| Alias aceptados | Modificador resultante |
|---|---|
| `Option`, `Options` | Alt |
| `Control` | Ctrl |
| `Cmd`, `Command`, `Win`, `Windows` | Meta |

La forma canonica al serializar sigue el orden fijo: `Ctrl > Alt > Shift > Meta > Tecla`.

Si la cadena no contiene ninguna tecla no-modificadora, se considera invalida y se usa el default (`Alt+Space`).

### Captura en la ventana de Settings

1. El usuario hace clic en el area de hotkey --> se inicia la captura y se muestra "Press keys...".
2. Pulsar solo teclas modificadoras (Alt, Ctrl, Shift, Meta) se ignora; no se registra nada hasta que se pulse una tecla principal.
3. Pulsar ESC o hacer clic fuera del area de hotkey cancela la captura y restaura el valor previo.
4. Cualquier otra tecla (con o sin modificadores) se guarda inmediatamente.

**Invariante:** el cache interno del parsing se invalida cada vez que se asigna un nuevo valor al campo `Hotkey`, de modo que la hotkey activa siempre refleja el ultimo valor configurado.

> **Verificar en:** `HotkeyConfig` en `Yottacast.Core/Platform/HotkeyConfig.cs`. Propiedad `ParsedHotkey` en `UserSettings`. Captura en `SettingsWindowViewModel.ProcessKeyCapture()` y `SettingsWindow.axaml.cs` (handlers `OnHotkeyAreaPointerPressed`, `OnPointerPressed`).

---

## 7. Auto-reparacion de browser y terminal

La aplicacion garantiza que siempre se use un browser y terminal validos, incluso si el usuario desinstala el que tenia configurado.

### Comportamiento de `ActiveBrowser` / `ActiveTerminal`

Cada vez que se accede a estas propiedades:

1. Se busca en disco el nombre configurado.
2. Si no existe (o el campo estaba vacio `""`): se itera la lista de browsers/terminales conocidos y se devuelve el primero que exista en disco.
3. Si se encontro un alternativo diferente al configurado, se actualiza el campo y se guarda automaticamente.
4. Si no se encuentra ningun browser/terminal conocido instalado en el sistema, se devuelve `null`.

**Invariantes:**

- `ActiveBrowser`/`ActiveTerminal` no son idempotentes en presencia de auto-reparacion: cada acceso comprueba disco y puede disparar un guardado. En flujos criticos de rendimiento, cachear el resultado localmente.
- La resolucion (`Resolve`) es estatica y no depende del cache de `ApplicationSearch`, por lo que es segura de llamar en cualquier punto del ciclo de vida.
- `EnsureIntegrity()` fuerza la validacion de ambos accediendo a las dos propiedades. Se llama automaticamente al abrir la ventana de Settings.

### Dos estrategias de descubrimiento

| Metodo | Proposito | Dependencia de ApplicationSearch |
|---|---|---|
| `Discover()` | Poblar los pickers de la UI | No -- comprueba existencia en disco. Resultados cacheados hasta invalidacion |
| `Resolve()` | Auto-reparacion de settings | No -- mismo mecanismo de busqueda que `Discover()`, sin cache |

Ambos metodos buscan en tres fuentes por orden de prioridad: primero las carpetas de apps del usuario (`AppDirectories`), luego las carpetas por defecto de la plataforma (`DefaultAppDirectories`), y por ultimo las rutas conocidas de la plataforma (`BrowserKnownPaths`/`TerminalKnownPaths`, solo relevantes en Windows). Las carpetas duplicadas se saltan automaticamente. En las carpetas se usa `PlatformProvider.AppPathInDirectory()` para construir la ruta candidata (ej: `{dir}/{name}.app` en macOS). En Windows, `AppPathInDirectory` no aplica y los ejecutables se encuentran via rutas conocidas.

`TerminalDiscovery` filtra las rutas conocidas que contienen `*` (patrones glob), ya que algunas rutas de terminal de plataforma incluyen versiones variables.

La cache de `Discover()` se invalida al cambiar `AppDirectories` en Settings (diferido al salir de la seccion AppSearch o cerrar Settings, via `FlushAppDirectoryChanges` en `SettingsWindowViewModel`).

> **Verificar en:** `BrowserDiscovery` en `Yottacast.Core/Services/BrowserDiscovery.cs`. `TerminalDiscovery` en `Yottacast.Core/Services/TerminalDiscovery.cs`. `SettingsWindowViewModel.FlushAppDirectoryChanges()`. Llamada a `EnsureIntegrity()` en el constructor de `SettingsWindowViewModel`.

---

## 8. Motores de busqueda web

Cada motor de busqueda web tiene su propia configuracion que el usuario puede personalizar:

| Campo | Descripcion |
|---|---|
| Id | Identificador unico del motor (p.ej. `"google"`) |
| Enabled | Si el motor aparece en resultados |
| Mode | `PrefixOnly` (solo se activa con el alias) o `ShowAlways` (aparece siempre) |
| Prefix | Alias de teclado que activa el motor (p.ej. `"g"` para Google) |
| QueryUrl | URL personalizada con placeholder `{0}`. `null` significa usar la URL por defecto |

La aplicacion incluye 20 motores preconfigurados en las categorias: General, Shopping, Video, Social, Knowledge, Dev, Entertainment y Maps. Por defecto solo Google usa el modo `ShowAlways`; el resto usa `PrefixOnly`.

Al cargar las preferencias, se fusionan los motores guardados con los predeterminados: las personalizaciones del usuario se conservan y los motores nuevos (anadidos en actualizaciones) aparecen automaticamente con sus defaults.

La UI de edicion (seccion "Web Search" en Settings) agrupa los motores por categoria (General, Shopping, Video, Social, Knowledge, Dev, Entertainment, Maps), cada grupo en su propia tabla estilo lista. Cada fila muestra icono, nombre, prefijo, checkbox Enabled y boton de settings (icono engranaje) que abre un flyout con toggle Mode, editar Prefix y editar QueryUrl. Un checkbox "Show disabled engines" en la parte superior controla si se muestran los motores deshabilitados; si un grupo no tiene ningun motor visible, el grupo entero se oculta. Los motores mantienen siempre su posicion original (no se reordenan al deshabilitar). Los cambios se guardan automaticamente.

**Invariante:** si el campo `Mode` del JSON contiene un valor no reconocido, se interpreta como `PrefixOnly`. El `QueryUrl` solo se escribe en el JSON si tiene un valor no vacio (se omite cuando es `null`, usando la URL por defecto del motor).

### Plugins

Ademas de los motores preconfigurados, el usuario puede instalar motores adicionales como plugins. Un plugin es un fichero JSON en `AppPaths.PluginsDir` (`~/Library/Application Support/Yottacast/plugins/` en macOS) con `"type": "WebSearch"`. `PluginService` los carga al arranque y vigila la carpeta para hot-reload.

Los plugins aparecen en la UI de Settings igual que los motores built-in, pero con un icono de plugin (puzzle piece) junto al nombre. El flyout de cada plugin incluye dos botones adicionales: "Show plugin folder" (abre la carpeta de plugins en el gestor de archivos) y "Edit plugin source" (abre el JSON del plugin con la app por defecto).

Al cargar un plugin nuevo, `UserSettings.EnsurePluginSettings()` crea su entrada en `WebSearchEngines` con defaults (`Enabled=true`, `Mode=PrefixOnly`, `Prefix` del plugin). A partir de ahi, la configuracion del plugin se persiste y personaliza igual que cualquier motor built-in.

Ver `docs/examples/hackernews.json` para un ejemplo de plugin.

> **Verificar en:** `WebSearchEngine`, `WebSearchEngineSettings`, `WebSearchDefaults` en `Yottacast.Core/Search/WebSearch/WebSearchEngine.cs`. Merge en `UserSettings.MergeWebSearchEngines()`. Plugins en `PluginService`, `WebSearchPlugin`. UI en `WebSearchEngineRowViewModel` y `SettingsWindow.axaml.cs`.

---

## 9. Ventana de Settings

### Secciones

La ventana de Settings se divide en secciones navegables: General, AppSearch, WebSearch, FileSearch, Calculator, Clipboard y Emoji. Cada apertura de la ventana inicia en la seccion General (el ViewModel es transient y se recrea en cada apertura).

### Flujo de apertura

Si la ventana ya esta visible, simplemente se activa sin recrearla. La apertura es sincrona — no necesita esperar a `ApplicationSearch` porque los pickers de browser y terminal usan sus propios mecanismos de deteccion en disco.

### Seguridad en los pickers

Tras la auto-reparacion (`EnsureIntegrity()`), el ViewModel aplica un segundo nivel de fallback en la UI:

- Si el browser guardado no esta en la lista descubierta, se selecciona el primero disponible.
- Si el terminal guardado no esta en la lista descubierta, se selecciona el primero disponible.
- Si el tema guardado no coincide con ningun tema cargado, se selecciona el primero disponible.

### Listas de carpetas

Las listas `SearchFolders` y `AppDirectories` son observables. Cualquier cambio (anadir, eliminar) se sincroniza inmediatamente a las preferencias y se guarda. La adicion deduplica: no se anade una ruta que ya exista en la lista (la comparacion se hace sobre la ruta expandida, para evitar duplicados entre `$HOME/X` y `/Users/user/X`). Las rutas se normalizan al cargar y al anadir: se elimina la barra final (`/` o `\`) y se deduplicaran con comparacion case-insensitive. El selector de carpetas usa el picker nativo del SO.

La seccion AppSearch tiene un checkbox adicional:
- **Enable app search**: si se desactiva, oculta el resto de opciones, el escaneo no se realiza y la busqueda no devuelve resultados.

La seccion AppSearch tambien tiene el boton **"Add common folders"**, que solo es visible cuando hay carpetas por defecto de la plataforma que existen en disco pero no estan aun en la lista. Al pulsarlo, se anaden todas esas carpetas de una vez.

La seccion FileSearch tiene dos checkboxes adicionales:
- **Enable file search**: si se desactiva, oculta el resto de opciones y la busqueda no devuelve resultados.
- **Only in specific folders**: si se activa, muestra la lista de carpetas y la busqueda se acota a ellas; si esta desactivado, la busqueda usa toda la home.

Las carpetas configuradas que ya no existen en disco se muestran atenuadas (opacidad reducida) en la lista, para que el usuario las identifique. Siguen guardadas en settings y vuelven a estar activas si el directorio se recrea. El boton "Add common folders" en FileSearch anade las carpetas por defecto de la plataforma que existen en disco en ese momento, sin duplicar las que ya esten en la lista.

> **Verificar en:** `SettingsWindowViewModel` en `Yottacast/ViewModels/SettingsWindowViewModel.cs`. `SearchFolderItem` (mismo archivo). `App.OpenSettings()` en `Yottacast/App.axaml.cs`. Code-behind en `Yottacast/Views/SettingsWindow.axaml.cs`.

---

## 10. Serializacion

El JSON usa `camelCase` para los nombres de campo (p.ej. `"searchFolders"`, `"enableCalculator"`, `"hotkey"`). Se escribe con indentacion para legibilidad.

Internamente se usa un record privado `UserSettingsData` como DTO de serializacion que nunca se expone fuera de la clase. Esto aisla la estructura del JSON de la interfaz publica de `UserSettings`.

> **Verificar en:** record `UserSettingsData` y `WebSearchEngineSettingsData` en `Yottacast.Core/Services/UserSettings.cs`.

---

## 11. Logging

| Situacion | Nivel |
|---|---|
| Carga exitosa del fichero | `Information` |
| Fichero no encontrado o invalido (se crean defaults) | `Information` |
| Guardado exitoso | `Debug` |
| Error al guardar | `Warning` |
| Auto-reparacion de browser/terminal | `Information` |

> **Verificar en:** `UserSettings.Load()`, `UserSettings.Save()`, propiedades `ActiveBrowser`/`ActiveTerminal` en `Yottacast.Core/Services/UserSettings.cs`.

---

## 12. Refresco automatico de resultados al cambiar settings

Cuando el usuario modifica un setting que afecta a los resultados de busqueda, la busqueda activa se re-ejecuta automaticamente sin que el usuario tenga que reescribir la query. Si no hay query activa (barra vacia), no ocurre nada.

**Settings que disparan refresco:**
- Toggles de fuentes: `EnableAppSearch`, `EnableCalculator`, `EnableClipboard`, `EnableEmoji`, `EnableFileSearch`, `EnableWebSearch`, `EnableDictionary`
- Configuracion de file search: `FileSearchOnlySpecificFolders`, cambios en `SearchFolders`
- Configuracion de diccionario: `DictionaryPrefix`, `DictionaryShowAlways`, `DictionaryLanguages`
- Configuracion de calculadora: `CalculatorCurrencyA`, `CalculatorCurrencyB`, `CalculatorDecimalPlaces`
- Configuracion por motor de web search (enabled, mode, prefix, queryUrl)
- Cambios en `AppDirectories` (al hacer flush)

**Settings que NO disparan refresco** (no afectan que resultados aparecen):
- `Browser`, `Terminal`, `Theme`, `Hotkey`, `StickyWindow`, `WindowX`/`WindowY`, `ShowDisabledWebSearchEngines`

**Mecanismo:** `UserSettings` expone un evento `SearchSettingsChanged`. `SettingsWindowViewModel` lo dispara tras cada cambio relevante. `MainWindowViewModel` se suscribe y re-lanza `SearchAsync` con la query actual, cancelando cualquier busqueda en vuelo.

> **Verificar en:** evento `SearchSettingsChanged` en `UserSettings`. Suscripcion en `MainWindowViewModel.Initialize()`. Llamadas a `NotifySearchSettingsChanged()` en `SettingsWindowViewModel` y `WebSearchEngineRowViewModel`.
