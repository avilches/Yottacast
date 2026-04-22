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
  usuario (Google, DuckDuckGo, etc.). En modo normal, siempre esta presente usando la query completa. En modo emoji (
  query empieza por `:`), usa el texto tras `:` como termino de busqueda; si la query es solo `:`, el item no se
  muestra.

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
directorios de apps, motor de busqueda web, y toggles para features individuales (calculadora, clipboard, emoji) — los
toggles se persisten en `UserSettings` y se muestran en Settings, pero aun no tienen efecto funcional sobre los
resultados de busqueda (pendiente de implementacion).

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
+-- Yottacast.Cli/             <- CLI interactivo para probar servicios (browsers, terminals, apps, search, run)
+-- Yottacast.Core.Tests/      <- Tests xUnit
```

## Build & Run

```bash
# GUI
cd Yottacast && dotnet run
dotnet publish -c Release -r osx-arm64 --self-contained

# CLI (para probar servicios)
cd Yottacast.Cli && dotnet run

# Tests
cd Yottacast.Core.Tests && dotnet test
```

## Reglas

`CLAUDE.md` es la **fuente de intencion** del proyecto: describe que debe hacer y como debe estar estructurado. Solo el
desarrollador lo modifica. Si el codigo contradice algo descrito aqui, se considera un gap a resolver — no al reves.

`docs/` es la **especificacion de comportamiento**: contratos, invariantes y comportamientos esperados (ver seccion
Documentacion).

El **codigo** es la fuente de verdad de la implementacion. `docs/` valida que el codigo cumple los contratos; el codigo
se ajusta a `CLAUDE.md`.

**Mantenimiento general**: describe siempre el estado actual del codigo. No documentes cambios respecto a versiones
anteriores ni migraciones. Si al editar escribes algo como "ahora X en vez de Y", "ya no se usa Z", o "antes se hacia
asi", reformulalo para describir solo el comportamiento actual. Los gotchas y precauciones si se documentan, pero sin
referenciar versiones pasadas.

**Codigo multiplataforma (UI)**: todo codigo OS-especifico que dependa de Avalonia o de la capa de UI debe vivir en
`Yottacast/Services/AppHandler` y sus subclases (`MacAppHandler`, `WindowsAppHandler`, `LinuxAppHandler`).

- El codigo de las Views y ViewModels no debe contener `OperatingSystem.IsMacOS()` ni similares; en su lugar, delega en
  `AppHandler.Instance`.
- La logica OS-especifica que no depende de UI (busqueda de archivos, lanzar procesos, etc.) va en
  `Yottacast.Core/Platform/PlatformProvider` y sus subclases, para que sea reutilizable desde el CLI y los tests.

**Inyeccion de dependencias**: no usar clases `static` para logica de negocio o servicios.
Las clases estaticas no permiten inyectar `ILogger`, `IConfiguration` ni otros servicios, lo que imposibilita el logging
y el testing. En su lugar, usar clases instanciables registradas en el contenedor DI. Los metodos `static` solo son
aceptables para utilidades puras sin dependencias (helpers de conversion, parsers sin estado, etc.).

**Tests**: al modificar funcionalidad cubierta por tests, actualizar los tests correspondientes en
`Yottacast.Core.Tests/`.
Cada `CLAUDE.md` de paquete lista los ficheros de test relevantes para su area. Ejecutar
`cd Yottacast.Core.Tests && dotnet test` para verificar que todo pasa antes de dar la tarea por terminada.

**Centralizacion de constantes y rutas**: toda ruta de fichero o directorio que la app lee o escribe en runtime debe
definirse en `AppPaths.cs`. Todo valor numerico o parametro por defecto debe definirse en `AppDefaults.cs`. Nunca
hardcodear rutas ni constantes en las clases que las consumen.

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
  unidades sueltas y expresiones invalidas.
- `docs/search-emoji.md` — Modo emoji (prefijo `:`), grid navegable, carga de datos, cache compacta, paste automatico.
- `docs/search-files.md` — Busqueda de documentos del usuario, indexacion nativa (Spotlight/Windows Search), resultados
  progresivos.
- `docs/search-file-icons.md` — Cache de iconos de ficheros: niveles de cache (memoria+disco), clave por extension,
  carga sincrona/asincrona, actualizacion reactiva de UI via IconLoaded.
- `docs/search-scoring.md` — Algoritmo de puntuacion y ordenacion de resultados entre fuentes.
- `docs/search-dictionary.md` — Definiciones de diccionario online, modos prefix/showAlways, API, settings.

**Internals:**

- `docs/app-paths.md` — Rutas centralizadas (AppPaths) y constantes numericas (AppDefaults). Convencion para anadir
  nuevas.
- `docs/release-workflow.md` — Versionado, migraciones, comprobacion de actualizaciones, flujo de publicacion.
- `docs/multi-platform.md` — Diferencias por OS: PlatformProvider (Core) y AppHandler (UI), P/Invoke, escaneo de apps,
  paste simulado.
- `docs/logging.md` — Politica de logging, niveles por componente, rotacion de ficheros.

**Settings y UI:**

- `docs/user-settings.md` — Persistencia JSON, auto-reparacion, migraciones de settings, propiedades del modelo.
- `docs/user-settings-browser.md` — Descubrimiento de navegadores, auto-reparacion, lanzamiento de URLs por plataforma.
- `docs/user-settings-terminal.md` — Descubrimiento de terminales, ejecucion de comandos, escaping por plataforma.
- `docs/ui-themes.md` — Temas JSON, deteccion dark/light, hot-swap, estructura de un tema, IMPORTANTE: themes solo
  aplican al buscador, los colores nativos de Settings estan hardcodeados en `Yottacast/Views/SettingsWindow.axaml`. 
  Si se pide algun cambio de fuente o color o theme en los settings, hay que buscarlo hardcodeado ahi. 
  dentro de `Window.Resources > ResourceDictionary.ThemeDictionaries` (dos diccionarios: Light y Dark).
- `docs/ui-hotkeys.md` — Hotkey global configurable, supresion a nivel de OS, mapa de teclas soportadas.
- `docs/ui-main-window.md` — Layout de la ventana, bindings, indicadores de busqueda, banner de actualizacion.
- `docs/unit-catalog.md` — Catalogo de unidades soportadas por la calculadora.

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