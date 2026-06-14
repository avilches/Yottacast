# Definicion del proyecto

Yottacast es un lanzador de aplicaciones para macOS/Windows — similar a Spotlight, Alfred o Raycast.

**Stack**: Avalonia 11.3.12, .NET 9, CommunityToolkit.Mvvm 8.2.1, SharpHook 7.1.1, Jint 3.1.0 (JS engine).

## Arquitectura clave

- Es una ventana sin marco, transparente, con una unica entrada de texto donde el usuario escribe para buscar en
  multiples fuentes.
- El flujo principal de busqueda: el usuario escribe -> debounce -> `GlobalSearch` lanza en paralelo todas las sources
  registradas -> las instant devuelven resultados sincronamente desde memoria; las deferred emiten resultados via
  `IAsyncEnumerable` en streaming -> `GlobalSearch` combina y ordena los resultados -> el ViewModel actualiza la UI.
- Hay dos tipos de search sources: **instant** (responden en memoria: apps, calculadora, emoji, busqueda web) y *
  *deferred** (requieren I/O: busqueda de ficheros). Cada tipo tiene su propia interfaz con ciclo de vida
  `Start/WhenReady/Stop`.
- Cada fuente devuelve uno o mas elementos, cada uno con un score, que luego son mezclados, opcionalmente filtrados,
  ordenados por score y mostrados al usuario, y donde este podra usar las teclas de flecha + Enter para hacer acciones
  en los elementos (abrir, copiar, lanzar un comando).
- Hay algunos elementos de una sola linea y otros en forma de grid. En los de grid se podra navegar con los cursores
  arriba abajo izquierda derecha dentro del elemento (por ejemplo un selector de Emojis).
- El usuario podra pulsar enter y otras teclas para ejecutar acciones sobre cada elemento.

Los temas visuales son ficheros JSON que se aplican en runtime via `ThemeService`. La hotkey global (mostrar/ocultar la
ventana) se captura con SharpHook.

## Comportamiento de la ventana

Yottacast es una **app accesoria**: no muestra icono en el Dock (macOS) ni en la taskbar (Windows), y no tiene barra de
menu. En macOS esto se logra con `NSApplicationActivationPolicyAccessory` via P/Invoke a Objective-C.

La ventana nunca se cierra realmente — se oculta y se muestra con una hotkey global (toggle).
Al mostrarse, captura la referencia a la app que tenia el foco en ese momento.
Al ocultarse, restaura el foco a esa app. La ventana se posiciona centrada en pantalla.

## Search sources

Yottacast busca en varias fuentes simultaneamente. Cada source tiene un proposito concreto:

- **Apps**: escanea las aplicaciones instaladas del sistema, las cachea en memoria y vigila cambios en el sistema de
  archivos para mantener la cache actualizada. Si una aplicación se acaba de instalar, aparece directamente.
- **Calculadora/Conversor**: evalua expresiones matematicas y conversiones de unidades usando math.js (ejecutado en
  Jint).
  Responde en linea mientras el usuario escribe. Si se usa una unidad (c, kg) siempre tiene otra unidad a la que
  convertir.
- **Emoji**: se activa con el prefijo `:`. Muestra los resultados en un grid navegable con cursores. Tras seleccionar un
  emoji, lo copia y lo pega automaticamente en la app anterior.
- **Busqueda de documentos**: busca archivos en las carpetas configuradas del usuario usando indexacion nativa del
  sistema operativo. Los resultados llegan progresivamente (deferred source).
- **Busqueda web**: permite abrir una busqueda web en el navegador configurado con el motor seleccionado por el
  usuario (Google, DuckDuckGo, etc.). En modo normal usa la query del usuario (segun el modo de cada motor: presente
  siempre, o activado por un prefijo). En modo emoji (query empieza por `:`) la busqueda web no se muestra.

## Acciones

Cada tipo de resultado tiene una accion por defecto al activarlo (Enter):

- **Apps** -> lanzar la aplicacion.
- **Calculadora** -> copiar el resultado al portapapeles.
- **Emoji** -> copiar al portapapeles + ocultar ventana + restaurar app anterior + simular paste (Cmd+V / Ctrl+V con
  delay).
- **Busqueda web** -> abrir la URL de busqueda en el navegador configurado.
- **Documentos** -> abrir el archivo con la aplicacion por defecto del sistema.

## Settings

La configuracion del usuario incluye: hotkey global, navegador preferido, terminal, tema visual, carpetas de busqueda,
directorios de apps, motor de busqueda web, y toggles para features individuales (calculadora, clipboard, emoji).
Los toggles se persisten en `UserSettings`, se muestran en Settings, y al cambiarlos se refresca automaticamente la
busqueda activa via el evento `SearchSettingsChanged`.

La ventana de settings es una ventana modal separada, accesible con Cmd+, (macOS).

**Auto-reparacion**: si el navegador o terminal configurado desaparece del sistema, Yottacast selecciona automaticamente
el primero disponible.

## Temas

Los temas son ficheros JSON que definen colores, fuentes y parametros de layout. Se aplican en runtime inyectando
recursos en el arbol de Avalonia y son hot-swappable (cambiar de tema no requiere reiniciar).
Yottacast detecta automaticamente el modo dark/light del sistema operativo y selecciona el tema acorde.

## Startup no bloqueante

La ventana es interactiva inmediatamente al arrancar.
Las search sources se inicializan en background mediante su ciclo de vida `Start/WhenReady`.
La UI no se arranca hasta que las instant sources estan todas Ready.
Se abre la UI y se acepta input del usuario, la busqueda nunca espera: las instant sources ya estaran listas, mientras
que las deferred siempre responden al momento de la busqueda, solo que quiza tarden mas en devolver resultados.

## Recursos embebidos

math.js y emoji-data se descargan automaticamente durante el build si no existen localmente.
El cache compacto de emojis se genera en runtime y se copia al source tree en build.
El build es autosuficiente: no requiere pasos manuales de descarga ni preparacion.

## Actualizaciones y versiones

La version de la aplicacion se define en ambos `.csproj` (Yottacast y Yottacast.Core).
Un `UpdateChecker` consulta un endpoint remoto para detectar nuevas versiones.
Existe un sistema de migraciones basado en `LastLaunchedVersion` que ejecuta transformaciones al detectar que el usuario
ha actualizado desde una version anterior.

## Estructura de la solucion

```
Yottacast.sln
+-- Yottacast/                 <- GUI app (Avalonia, WinExe). Views, ViewModels, Themes, AppHandler (codigo OS-especifico de UI)
+-- Yottacast.Core/            <- Shared library (sin UI). Search sources, PlatformProvider, Services, ViewModels base
+-- Yottacast.Core.Tests/      <- Tests xUnit
+-- Yottacast.Ipc/             <- Daemon headless gRPC. Expone Core via Unix socket para UI Swift futura
+-- Yottacast.Ipc.Tests/       <- Tests xUnit de mappers IPC
```

## Build & Run

```bash
# GUI (Avalonia)
cd Yottacast && dotnet run
dotnet publish -c Release -r osx-arm64 --self-contained

# Daemon IPC (headless)
cd Yottacast.Ipc && dotnet run

# Tests
cd Yottacast.Core.Tests && dotnet test
cd Yottacast.Ipc.Tests && dotnet test
```

## Reglas

`CLAUDE.md` es la **fuente de intencion** del proyecto: describe que debe hacer y como debe estar estructurado. Si el
codigo contradice algo descrito aqui, se considera un gap a resolver — no al reves.

`docs/` es la **especificacion de comportamiento**: contratos, invariantes y comportamientos esperados (ver seccion
Documentacion).

El **codigo** es la fuente de verdad de la implementacion. `docs/` valida que el codigo cumple los contratos; el codigo
se ajusta a `CLAUDE.md`.

**Aprendizajes de feedback**: cuando el usuario da una correccion o confirma una forma de trabajar que debe aplicarse
siempre (una regla estable de proceso o de codigo), documentarla directamente aqui en `CLAUDE.md`, no solo en la memoria
de la sesion. Lo de `CLAUDE.md` se carga completo y con prioridad en cada sesion; la memoria solo llega como contexto de
fondo ocasional. Las reglas que deben cumplirse siempre viven aqui.

**Mantenimiento general**: describe siempre el estado actual del codigo. No documentes cambios respecto a versiones
anteriores ni migraciones. Si al editar escribes algo como "ahora X en vez de Y", "ya no se usa Z", o "antes se hacia
asi", reformulalo para describir solo el comportamiento actual. Los gotchas y precauciones si se documentan, pero sin
referenciar versiones pasadas.

**Codigo multiplataforma (UI)**: todo codigo OS-especifico que dependa de Avalonia o de la capa de UI debe vivir en
`Yottacast/Services/AppHandler` y sus subclases (`MacAppHandler`, `WindowsAppHandler`, `LinuxAppHandler`).

- El codigo de las Views y ViewModels no debe contener `OperatingSystem.IsMacOS()` ni similares; en su lugar, delega en
  `AppHandler.Instance`.
- La logica OS-especifica que no depende de UI (busqueda de archivos, lanzar procesos, etc.) va en
  `Yottacast.Core/Platform/PlatformProvider` y sus subclases, para que sea reutilizable desde los tests.

**Inyeccion de dependencias**: no usar clases `static` para logica de negocio o servicios.
Las clases estaticas no permiten inyectar `ILogger`, `IConfiguration` ni otros servicios, lo que imposibilita el logging
y el testing. En su lugar, usar clases instanciables registradas en el contenedor DI. Los metodos `static` solo son
aceptables para utilidades puras sin dependencias (helpers de conversion, parsers sin estado, etc.).

**Loggers inyectados**: nunca eliminar un `ILogger` inyectado aunque este sin usar (warning CS9113). Los loggers son
utiles para diagnostico futuro aunque hoy no se usen, y los sitios sin usar suelen coincidir justo con puntos donde
faltan logs. La solucion correcta ante un `ILogger` sin usar es USARLO en los puntos de fallo silenciosos (`catch` vacios,
no-ops, early-returns de guardas de pre-condicion) con `logger.LogWarning(...)` y contexto estructurado, no quitarlo.

**Tests**: al modificar funcionalidad cubierta por tests, actualizar los tests correspondientes en
`Yottacast.Core.Tests/`.
Cada `CLAUDE.md` de paquete lista los ficheros de test relevantes para su area. Ejecutar
`cd Yottacast.Core.Tests && dotnet test` para verificar que todo pasa antes de dar la tarea por terminada.

**Acceso rapido a datos de runtime**: el directorio `user-data/` en la raiz del proyecto contiene symlinks a los
directorios de datos de la app en la maquina local (config, logs, cache). Usar estos symlinks para inspeccionar
ficheros de configuracion, cache y logs sin necesidad de navegar a rutas del sistema.
- `user-data/config/` → `~/Library/Application Support/Yottacast` (settings.json, exchange-rates.json, etc.)
- `user-data/logs/` → `~/Library/Logs/Yottacast` (logs diarios `yottacast-*.log`)
- `user-data/cache/` → `~/.cache/yottacast` (app-icons/, exchange-rates.json, etc.)
Ver `docs/app-paths.md` para el inventario completo de ficheros y sus rutas.

**IMPORTANTE — Cambios de color o estilo en temas**: cada vez que se modifique un color, fuente u otro estilo visual,
el cambio debe hacerse en el fichero JSON del tema que el usuario indique. Si no especifica cual, preguntarle antes de
hacer ningun cambio. Si hace falta anadir un nuevo token de tema (nuevo color, nueva propiedad), preguntar al usuario
antes de crearlo.

**Centralizacion de constantes y rutas**: toda ruta de fichero o directorio que la app lee o escribe en runtime debe
definirse en `AppPaths.cs`. Todo valor numerico o parametro por defecto debe definirse en `AppDefaults.cs`. Nunca
hardcodear rutas ni constantes en las clases que las consumen.

## Trabajo con subagentes

**Particion por fichero, build central al final**: al arreglar muchos bugs a la vez, lanzar **un agente por
fichero/area** (conjuntos de ficheros disjuntos, sin solape) en paralelo, en vez de un agente por bug suelto. Bugs que
comparten fichero (p.ej. `MainWindow.axaml.cs`, `WindowsPlatformProvider.cs`, los servicios IPC) se agrupan en un solo
agente. Los agentes **solo editan y actualizan tests**; NO ejecutan `dotnet build` ni `dotnet test`. La verificacion es
**una unica pasada central al final** (build de `Yottacast.sln` + `dotnet test` en `Core.Tests` e `Ipc.Tests`),
corrigiendo lo que rompa. Motivo: todos los agentes comparten el mismo working tree y los builds concurrentes de .NET se
pisan los `obj/` (locks) y fallan; centralizar da una unica fuente de verdad de verde. A cada agente se le pasan las
reglas del proyecto (leer docs del area primero, centralizar en `AppDefaults`/`AppPaths`, actualizar tests, nada de
"Generated by Claude"). Para bugs "dudosos" o con opciones A/B, el usuario suele preferir que se elija el fix obvio y se
siga, o que se salten. Gaps/Inconsistencias/Arquitectura se dejan fuera salvo que se pidan.

**Scope estricto de los implementers**: en los prompts de subagentes implementers, instruir SIEMPRE de forma explicita:
(1) `git add` solo de los ficheros concretos listados, NUNCA `git add -A` ni `git add .`; (2) no crear, borrar ni mover
ningun fichero fuera de los indicados; (3) si creen que algo sobra, REPORTARLO, no actuarlo. Motivo: los subagentes
toman iniciativas de "limpieza" no solicitadas si el prompt no lo prohibe, y cualquier borrado fuera de scope corrompe el
trabajo del usuario. Verificar el `git log`/`git show` de los commits que hagan los subagentes por si tocaron algo de
mas.

## Pendientes y TODOs

- **TODOs (features futuras)**: cuando el usuario pida "recordar hacer una feature mas adelante", anadirla a `docs/TODO.md`.
- **PENDING (trabajo aplazado)**: cuando algo quede pendiente y el usuario diga que no lo quiere hacer ahora, meterlo en `docs/PENDING.md`.
- `docs/PENDING.md` es el indice del trabajo aplazado y referencia todos los ficheros de la carpeta `docs/pending/`. Al anadir un documento de plan a `docs/pending/`, anadir su entrada en `docs/PENDING.md`.
- **Cuando el usuario pregunte "que queda por hacer"**: responder primero con lo de `docs/PENDING.md` (y su carpeta `docs/pending/`) y despues con los TODOs de `docs/TODO.md`.
- **Handoffs**: al revisar lo pendiente, mirar tambien si hay varios documentos de handoff por si el usuario quiere continuar con alguno. Listarlos junto con PENDING y TODO, y ofrecer al usuario la opcion de limpiarlos (borrar los obsoletos), unificarlos (fusionar varios en uno) o moverlos a `docs/PENDING.md`/`docs/pending/` o a `docs/TODO.md` segun corresponda.

## Documentacion

Los docs estan en `docs/`. **Antes de modificar cualquier feature o area del codigo, leer SI O SI los ficheros
relacionados de esta lista. No se puede tocar codigo sin haber leido primero los contratos y comportamientos
documentados de esa area.**

Si el codigo contradice un doc, el doc describe la intencion correcta y el codigo debe corregirse. Si no queda claroa,
PREGUNTA

Cuando se modifique codigo que afecte al comportamiento descrito en `docs/`, actualizar el doc correspondiente
manteniendo el mismo enfoque:

- Describir **que debe hacer** la aplicacion y **por que**, no como lo implementa el codigo.
- Estructurar por **comportamientos y contratos**, no por ficheros fuente.
- Incluir **invariantes verificables** (ej: "el usuario nunca ve la ventana vacia", "Escape siempre tiene una salida").
- Terminar cada seccion con un bloque `> **Verificar en:**` que apunte a los ficheros y metodos donde se puede validar
  el comportamiento.
- Los docs no duplican constantes, rutas, scores ni otros detalles de implementacion — señalan donde viven en el codigo
  (p. ej. "ver `ClassName.Method`"). Los docs responden "que debe hacer esto?" y "donde lo verifico?".

Ficheros disponibles por area:

RECUERDA LEERLOS ANTES DE HACER CUALQUIER CAMBIO. Si para lo que se pide, no queda claro que fichero leer, puedes
leerlos
todos hasta descrubir cual y luego actualizar CLAUDE.md para que sea mas facil buscarlo despues.

Si un fichero de doc empieza a ser demasiado grande, sugiere dividirlo en dos.

**Diseno general y arranque:**

- `docs/app-design.md` — Debes leer este fichero cuando la feature tenga que ver con el ciclo de vida de la aplicacion
  arranque, mostrar/ocultar, cierre), arquitectura de busqueda en dos fases, contratos de resultados, integracion
  multiplataforma. Es el punto de entrada para entender la app completa.

**Fuentes de busqueda:**

- `docs/search-sources.md` — Interfaces de fuentes (instant/deferred), ciclo de vida Start/WhenReady/Stop, mecanismo de
  merge por slots, flujo completo de busqueda con debounce.
- `docs/search-calculator.md` — Motor de calculo (math.js en Jint), conversiones de unidades, formato de resultados,
  seleccion automatica vs manual, clasificacion de errores (cuando ignorar la query vs mostrar hint), deteccion de
  unidades sueltas y expresiones invalidas. Es la fuente unica del comportamiento de la calculadora; el catalogo de
  unidades aceptadas y bloqueadas vive en `docs/unit-catalog.md`.
- `docs/unit-catalog.md` — Catalogo de referencia de unidades soportadas por categoria y unidades bloqueadas con sus
  motivos. Solo referencia de datos: el comportamiento se documenta en `docs/search-calculator.md`.
- `docs/search-emoji.md` — Modo emoji (prefijo `:`), grid navegable, carga de datos, cache compacta, paste automatico.
- `docs/emoji-grid-gotchas.md` — Gotchas de implementacion del grid de emoji: alineacion section-row, padding de
  placeholders, viewport y secciones visibles. Capa de bajo nivel complementaria a `docs/search-emoji.md`.
- `docs/search-files.md` — Busqueda de documentos del usuario, indexacion nativa (Spotlight/Windows Search), resultados
  progresivos.
- `docs/search-file-icons.md` — Cache de iconos de ficheros: niveles de cache (memoria+disco), clave por extension,
  carga sincrona/asincrona, actualizacion reactiva de UI via IconLoaded.
- `docs/search-scoring.md` — Algoritmo de puntuacion y ordenacion de resultados entre fuentes.
- `docs/search-dictionary.md` — Definiciones de diccionario: fuente local (kaikki/SQLite) con fallback a API Wiktionary, modos prefix/showAlways, conversion automatica JSONL→SQLite, settings. Leer tambien `tools/kaikki/README.md` si se toca la generacion de datos.
- `docs/search-history.md` — Historial de búsquedas: qué se guarda, persistencia, navegación con ↑/Ctrl+↑/Ctrl+↓, settings.
- `docs/search-clipboard.md` — Historial de portapapeles: captura por polling (macOS/Windows), store con dedup y límites, scoring con decay, acciones Paste/Delete, modos de visibilidad, settings.

**IPC daemon:**

- `docs/ipc-daemon.md` — Proyecto `Yottacast.Ipc`: para que sirve, servicios gRPC expuestos, secuencia de arranque,
  como probar con grpcurl.

**Internals:**

- `docs/app-paths.md` — Rutas centralizadas (AppPaths) y constantes numericas (AppDefaults). Convencion para anadir
  nuevas.
- `docs/result-viewmodels.md` — Jerarquia de ViewModels de resultado: Base, ResultItem, Calculator, Conversion (3
  celdas navegables), EmojiGrid (viewport + secciones), Dictionary. Contrato de datos entre fuentes y UI.
- `docs/plugin-system.md` — Sistema de plugins: PluginService, formato de plugins WebSearch y temas, FileSystemWatcher,
  iconos, evento PluginsChanged.
- `docs/release-workflow.md` — Versionado, migraciones, comprobacion de actualizaciones, flujo de publicacion.
- `docs/multi-platform.md` — Diferencias por OS en ventana/foco/plataforma: PlatformProvider (Core) y AppHandler (UI),
  P/Invoke, aislamiento, lanzamiento de apps, navegadores/terminales, iconos, hotkey global SharpHook, paste simulado.
- `docs/multi-platform-search.md` — Diferencias por OS en busqueda y proceso: escaneo de apps (Spotlight/recursion
  Windows/.desktop), busqueda de ficheros, SpotlightInterop, ProcessRunner, gotcha de PowerShell.
- `docs/logging.md` — Politica de logging, niveles por componente, rotacion de ficheros.
- `docs/TESTS.md` — Tests manuales de verificacion en runtime (plugins WebSearch, ShowAlways, recarga de settings) y
  descripcion de la suite automatizada xUnit. Complementa la regla de Tests de este `CLAUDE.md`.

**Settings y UI:**

- `docs/user-settings.md` — Persistencia JSON, auto-reparacion, migraciones de settings, propiedades del modelo,
  secciones de la ventana de Settings.
- `docs/user-settings-browser.md` — Descubrimiento de navegadores, auto-reparacion, lanzamiento de URLs por plataforma.
- `docs/user-settings-terminal.md` — Descubrimiento de terminales, ejecucion de comandos, escaping por plataforma.
- `docs/user-settings-websearch.md` — Motores de busqueda web: configuracion por motor, modos ShowAlways/prefix, merge
  con defaults, integracion con plugins WebSearch.
- `docs/ui-themes.md` — Temas JSON, deteccion dark/light, hot-swap, estructura de un tema. IMPORTANTE: los themes JSON
  solo aplican al buscador, no a Settings. La ventana de Settings esta troceada en UserControls por seccion bajo
  `Yottacast/Views/Settings/` (General, AppSearch, FileSearch, FileEditor, Clipboard, Emoji, Dictionary, DateSearch,
  History, Permissions) mas los recursos compartidos `SettingsResources.axaml` (iconos) y `SettingsStyles.axaml`
  (estilos de campos); WebSearch, Calculator y el chrome/sidebar siguen inline en `SettingsWindow.axaml`.
  Sus colores de tema (tokens `Theme.*`) NO estan en el AXAML: se inyectan en runtime en C# via
  `AppHandler.ApplySettingsTheme()` de cada plataforma (diccionarios Light/Dark segun el OS). Algunos colores literales
  si estan hardcodeados en el AXAML, p.ej. el rojo de captura de hotkey `#FF3B30` (estilo `hotkey-field.capturing` en
  `SettingsStyles.axaml` y en `SettingsGeneralView.axaml`/`SettingsClipboardView.axaml`). Si se pide un cambio de
  fuente/color/estilo en Settings: buscar primero en `SettingsStyles.axaml` (estilos compartidos) y en el
  `Settings<Seccion>View.axaml` correspondiente (o en `SettingsWindow.axaml` para WebSearch/Calculator/sidebar); para
  los colores de tema nativos del OS, en `ApplySettingsTheme()` del `AppHandler`. La referencia completa de tokens de
  tema (JSON → recurso Avalonia) vive en `docs/ui-themes-tokens.md`.
- `docs/ui-themes-tokens.md` — Referencia canonica de tokens de tema: tabla token JSON → recurso Avalonia por seccion,
  match highlight, lista de temas incluidos. Complemento de `docs/ui-themes.md` (comportamiento).
- `docs/ui-drag-drop.md` — Drag-and-drop de resultados al sistema operativo (Finder, editores). Contrato `GetDragPayload` y disparo desde `MainWindow.axaml.cs`.
- `docs/ui-hotkeys.md` — Hotkey global configurable, supresion a nivel de OS, mapa de teclas soportadas.
- `docs/ui-main-window.md` — Ciclo de vida y layout de la ventana, posicionamiento, arrastre, decay timer, ocultacion
  del cursor, banner de actualizacion. El detalle de las fases de busqueda, ordenacion, footer hints y score debug esta
  en `docs/ui-main-window-search.md`.
- `docs/ui-main-window-search.md` — Comportamiento de busqueda dentro de la ventana: fases instant/deferred, ordenacion,
  auto-seleccion, web search en la lista, footer hints, score debug y hint de busqueda.

## Gotchas (Avalonia / transversales)

- **No animar `RenderTransform` con keyframes CSS** — No hay animator registrado para `ITransform`; lanza
  `InvalidOperationException`. Animar solo propiedades de tipo simple (`double`, `Color`, `Thickness`...). Para
  indicadores de carga, usar `Opacity` con `PlaybackDirection="Alternate"`. `AutoReverse` no existe en Avalonia — el
  equivalente es `PlaybackDirection="Alternate"`.
- **No `BoxShadow` en el root Border** — Avalonia lo renderiza como rectangulo independientemente del `CornerRadius`.
  macOS provee sombra redondeada nativa via la ventana frameless transparente.
- **Compiled bindings** habilitados globalmente (`AvaloniaUseCompiledBindingsByDefault=true`) — los bindings deben ser
  type-resolvable en compile time.
- **`DataAnnotationsValidationPlugin`** deshabilitado en `App.axaml.cs` para evitar conflictos con
  CommunityToolkit.Mvvm.
- **`ControlTheme` interno gana en `:focus-within`** — Los estilos externos con `/template/` aplicados a template
  children de un control (p.ej. `NumericUpDown:focus-within /template/ ButtonSpinner /template/ Border`) son
  sobreescritos por los estilos del `ControlTheme` interno del control hijo (`ButtonSpinner`). No hay `!important` en
  Avalonia. Solución: envolver el control en un `Border` externo que gestione el borde visual y hacer que el control
  interior tenga `BorderThickness="0"`. El `Border` exterior no tiene `ControlTheme` que interfiera, así que su
  `BorderBrush` siempre se respeta.
- **`CornerRadius` no recorta hijos automáticamente** — Ni `CornerRadius` ni `ClipToBounds="True"` en un `Border`
  recortan los hijos a la forma redondeada: `ClipToBounds` recorta al rectángulo de layout, no a la curva. Si el fondo
  de un control interior cambia de color en `:focus-within` (p.ej. FluentTheme cambia el background del `ButtonSpinner`
  al enfocar), ese color distinto aparece en las esquinas tapando el borde redondeado. La solución correcta es hacer
  todos los fondos interiores `Transparent` — el `Border` exterior aporta el color de fondo y sus esquinas nunca quedan
  tapadas porque los hijos no pintan nada opaco.
- **`NumericUpDown` y validación** — Si el campo queda vacío, Avalonia muestra un error de validación ("System.Object[]"
  si `ErrorTemplate` es `{x:Null}`). Suprimir con un template vacío: `<DataTemplate><Panel/></DataTemplate>` como valor
  de `ErrorTemplate` en un estilo sobre `NumericUpDown /template/ DataValidationErrors`. Para bloquear letras, usar
  `AddHandler(InputElement.TextInputEvent, handler, RoutingStrategies.Tunnel)` en code-behind (el evento
  `TextInputting` no se puede declarar en AXAML sobre `NumericUpDown`).
- **`.axaml` standalone (`ResourceDictionary`/`Styles`) sin `x:Class` no se auto-incluye** — El csproj de
  `Yottacast` no tiene glob `**/*.axaml`. Avalonia solo auto-incluye en el build los `.axaml` con `x:Class` (via
  compilacion XAML: ventanas, UserControls). Un fichero de recursos o estilos compartido (sin `x:Class`, como
  `Views/Settings/SettingsResources.axaml` o `SettingsStyles.axaml`) hay que declararlo a mano con
  `<AvaloniaResource Include="ruta.axaml" />` en el `.csproj`, o el `ResourceInclude`/`StyleInclude` que lo referencie
  fallara en runtime (recurso no encontrado).