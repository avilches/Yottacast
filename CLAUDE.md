# Definición del proyecto

Yottacast es un lanzador de aplicaciones para macOS/Windows — similar a Spotlight, Alfred o Raycast.

**Stack**: Avalonia 11.3.12, .NET 9, CommunityToolkit.Mvvm 8.2.1, SharpHook 7.1.1, Jint 3.1.0 (JS engine).

## Arquitectura clave

- Es una ventana sin marco, transparente, con una única entrada de texto donde el usuario escribe para buscar en múltiples fuentes.
- El flujo principal de búsqueda: el usuario escribe → debounce → `GlobalSearch` lanza en paralelo todas las sources registradas → las instant devuelven resultados síncronamente desde memoria; las deferred emiten resultados vía `IAsyncEnumerable` en streaming → `GlobalSearch` combina y ordena los resultados → el ViewModel actualiza la UI.
- Hay dos tipos de search sources: **instant** (responden en memoria: apps, calculadora, emoji) y **deferred** (requieren I/O: búsqueda de ficheros). Cada tipo tiene su propia interfaz con ciclo de vida `Start/WhenReady/Stop`.
- Cada fuente una devuelve uno o más elementos, cada uno con un score, que luego son mezclados, opcionalmente filtrados, ordenados por score y mostrados
  al usuario, y donde éste podrá usar las teclas de flecha + Enter para hacer acciones en los elementos (abrir, copiar, lanzar un comando).
- Hay algunos elementos de una sola línea y otros en forma de grid. En los de grid se podrá navegar con los cursores arriba abajo izquierda derecha dentro del elemento (por ejemplo un selector de Emojis).
- El usuario podrá pulsar enter y otras teclas para ejecutar acciones sobre cada elemento.

Los temas visuales son ficheros JSON que se aplican en runtime vía `ThemeService`. La hotkey global (mostrar/ocultar la ventana) se captura con SharpHook.

## Comportamiento de la ventana

Yottacast es una **app accesoria**: no muestra icono en el Dock (macOS) ni en la taskbar (Windows), y no tiene barra de menú. En macOS esto se logra con `NSApplicationActivationPolicyAccessory` vía P/Invoke a Objective-C.

La ventana nunca se cierra realmente — se oculta y se muestra con una hotkey global (toggle).
Al mostrarse, captura la referencia a la app que tenía el foco en ese momento.
Al ocultarse, restaura el foco a esa app. La ventana se posiciona centrada en pantalla.

## Search sources

Yottacast busca en varias fuentes simultáneamente. Cada source tiene un propósito concreto:

- **Apps**: escanea las aplicaciones instaladas del sistema, las cachea en memoria y vigila cambios en el sistema de archivos para mantener la caché actualizada.
- **Calculadora**: evalúa expresiones matemáticas y conversiones de unidades usando math.js (ejecutado en Jint). Responde en línea mientras el usuario escribe.
- **Emoji**: se activa con el prefijo `:`. Muestra los resultados en un grid navegable con cursores. Tras seleccionar un emoji, lo copia y lo pega automáticamente en la app anterior.
- **Búsqueda de documentos**: busca archivos en las carpetas configuradas del usuario usando indexación nativa del sistema operativo. Los resultados llegan progresivamente (deferred source).
- **Google suggestion**: permite abrir una búsqueda web en el navegador configurado. En modo normal, siempre está presente usando la query completa. En modo emoji (query empieza por `:`), usa el texto tras `:` como término de búsqueda; si la query es solo `:`, el ítem no se muestra.

## Acciones

Cada tipo de resultado tiene una acción por defecto al activarlo (Enter):

- **Apps** → lanzar la aplicación.
- **Calculadora** → copiar el resultado al portapapeles.
- **Emoji** → copiar al portapapeles + ocultar ventana + restaurar app anterior + simular paste (Cmd+V / Ctrl+V con delay).
- **Google** → abrir la URL de búsqueda en el navegador configurado.
- **Documentos** → abrir el archivo con la aplicación por defecto del sistema.

## Settings

La configuración del usuario incluye: hotkey global, navegador preferido, terminal, tema visual, carpetas de búsqueda, directorios de apps, y toggles para features individuales (calculadora, clipboard, emoji) — los toggles se persisten en `UserSettings` y se muestran en Settings, pero aún no tienen efecto funcional sobre los resultados de búsqueda (pendiente de implementación).

La ventana de settings es una ventana modal separada, accesible con Cmd+, (macOS) o Ctrl+, (Windows).

**Auto-reparación**: si el navegador o terminal configurado desaparece del sistema, Yottacast selecciona automáticamente el primero disponible.

## Temas

Los temas son ficheros JSON que definen colores, fuentes y parámetros de layout. Se aplican en runtime inyectando recursos en el árbol de Avalonia y son hot-swappable (cambiar de tema no requiere reiniciar).
Yottacast detecta automáticamente el modo dark/light del sistema operativo y selecciona el tema acorde.

## Startup no bloqueante

La ventana es interactiva inmediatamente al arrancar.
Las search sources se inicializan en background mediante su ciclo de vida `Start/WhenReady`.
La UI no se arranca hasta que las instant sources están todas Ready.
Se abre la UI y se acepta input del usuario, la búsqueda nunca espera: las instant sources ya estarán listas, mientras que las deferred siempre responden al momento de la búsqueda, solo que quizá tarden más en devolver resultados.

## Recursos embebidos

math.js y emoji-data se descargan automáticamente durante el build si no existen localmente.
El caché compacto de emojis se genera en runtime y se copia al source tree en build.
El build es autosuficiente: no requiere pasos manuales de descarga ni preparación.

## Actualizaciones y versiones

La versión de la aplicación se define en ambos `.csproj` (Yottacast y Yottacast.Core). 
Un `UpdateChecker` consulta un endpoint remoto para detectar nuevas versiones.
Existe un sistema de migraciones basado en `LastLaunchedVersion` que ejecuta transformaciones al detectar que el usuario ha actualizado desde una versión anterior.

## Estructura de la solución

```
Yottacast.sln
├── Yottacast/                 ← GUI app (Avalonia, WinExe). Views, ViewModels, Themes, AppHandler (código OS-específico de UI)
├── Yottacast.Core/            ← Shared library (sin UI). Search sources, PlatformProvider, Services, ViewModels base
├── Yottacast.Cli/             ← CLI interactivo para probar servicios (browsers, terminals, apps, search, run)
└── Yottacast.Core.Tests/      ← Tests xUnit
```

## Fuentes de verdad

`CLAUDE.md` es la **fuente de intención** del proyecto: describe qué debe hacer y cómo debe estar estructurado. Solo el desarrollador lo modifica. Si el código contradice algo descrito aquí, se considera un gap a resolver — no al revés.

`docs/` es la **descripción del código actual**: explica cómo funciona lo que ya está implementado. Claude lo mantiene sincronizado con el código vía `/audit`.

El **código** es la fuente de verdad de la implementación. `docs/` se ajusta al código; el código se ajusta a `CLAUDE.md`.

## Reglas

**Mantenimiento**: describe siempre el estado actual del código. No documentes cambios respecto a versiones anteriores ni migraciones. Si al editar escribes algo como "ahora X en vez de Y", "ya no se usa Z", o "antes se hacía así", reformúlalo para describir solo el comportamiento actual. Los gotchas y precauciones sí se documentan, pero sin referenciar versiones pasadas.

**Código multiplataforma (UI)**: todo código OS-específico que dependa de Avalonia o de la capa de UI debe vivir en `Yottacast/Services/AppHandler` y sus subclases (`MacAppHandler`, `WindowsAppHandler`, `LinuxAppHandler`).
- El código de las Views y ViewModels no debe contener `OperatingSystem.IsMacOS()` ni similares; en su lugar, delega en `AppHandler.Instance`.
- La lógica OS-específica que no depende de UI (búsqueda de archivos, lanzar procesos, etc.) va en `Yottacast.Core/Platform/PlatformProvider` y sus subclases, para que sea reutilizable desde el CLI y los tests.

**Inyección de dependencias**: no usar clases `static` para lógica de negocio o servicios.
Las clases estáticas no permiten inyectar `ILogger`, `IConfiguration` ni otros servicios, lo que imposibilita el logging y el testing. En su lugar, usar clases instanciables registradas en el contenedor DI. Los métodos `static` solo son aceptables para utilidades puras sin dependencias (helpers de conversión, parsers sin estado, etc.).

**Documentación**: los ficheros en `docs/` explican diseño, arquitectura y relaciones entre componentes.
No duplican constantes concretas, listas completas de rutas, puntuaciones numéricas, patrones regex ni otros
detalles de implementación que ya son legibles en el código; en su lugar, señalan dónde viven esos detalles (p. ej. "ver `ClassName.Method`" o "definido en `File.cs`").
Esto evita que la documentación quede desactualizada cuando cambian los valores.
Los docs responden "¿cómo funciona esto?" y "¿dónde lo busco?", no "¿cuáles son los valores exactos?".

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

## Documentación

Las docs están en `docs/`. Léelas antes de trabajar en cualquier área:

Diseño general y fuentes de busqueda:
- `docs/app-design.md`
- `docs/search-sources.md`
- `docs/search-calculator.md`
- `docs/search-emoji.md`
- `docs/search-scoring.md`
- `docs/search-files.md`

Internals:
- `docs/release-workflow.md`
- `docs/multi-platform.md`
- `docs/logging.md`

Settings
- `docs/user-settings.md`
- `docs/user-settings-browser.md`
- `docs/user-settings-terminal.md`
- `docs/ui-themes.md`
- `docs/ui-hotkeys.md`
- `docs/ui-main-window.md`

## Gotchas (Avalonia / transversales)

- **No animar `RenderTransform` con keyframes CSS** — No hay animator registrado para `ITransform`; lanza `InvalidOperationException`. Animar solo propiedades de tipo simple (`double`, `Color`, `Thickness`…). Para indicadores de carga, usar `Opacity` con `PlaybackDirection="Alternate"`. `AutoReverse` no existe en Avalonia — el equivalente es `PlaybackDirection="Alternate"`.
- **No `BoxShadow` en el root Border** — Avalonia lo renderiza como rectángulo independientemente del `CornerRadius`. macOS provee sombra redondeada nativa vía la ventana frameless transparente.
- **Compiled bindings** habilitados globalmente (`AvaloniaUseCompiledBindingsByDefault=true`) — los bindings deben ser type-resolvable en compile time.
- **`DataAnnotationsValidationPlugin`** deshabilitado en `App.axaml.cs` para evitar conflictos con CommunityToolkit.Mvvm.
