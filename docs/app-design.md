# Diseño general de la aplicación

Yottacast es un launcher de escritorio multiplataforma (macOS, Windows, Linux) construido con Avalonia. El usuario lo
invoca con una hotkey global, escribe una búsqueda, y actúa sobre el resultado (abrir app, copiar emoji, calcular,
buscar ficheros, etc.). La ventana se oculta tras cada acción y el proceso permanece residente en background.

## Principios de diseño

- **Invisible hasta que se necesita**: no hay icono en el Dock/taskbar. La ventana aparece con la hotkey y desaparece al
  activar un resultado o pulsar Escape.
- **Instantánea al aparecer**: la ventana nunca se muestra vacía. El arranque bloquea la UI hasta que las fuentes de
  datos rápidas (apps, emojis, calculadora) están cargadas en memoria.
- **Búsqueda en dos fases**: las fuentes rápidas (en memoria) responden sin delay; las lentas (disco, red) se lanzan
  tras un debounce y sus resultados se mezclan progresivamente.
- **Multiplataforma con contrato común**: las diferencias de OS se encapsulan en un provider de plataforma y un handler
  de UI; el resto del código no sabe en qué OS corre.
- **Core sin dependencia de UI**: el proyecto `Yottacast.Core` no referencia Avalonia. Los servicios que necesitan
  interactuar con la UI (portapapeles, foco) reciben callbacks inyectados.

## Ciclo de vida de la aplicación

### Arranque

El arranque es secuencial e intencionalmente bloqueante. La ventana no aparece hasta que todo está listo.

**Orden requerido:**

1. Configuración OS-específica antes de cualquier UI (ej: macOS debe declararse como app accessory para no aparecer en
   el Dock).
2. Construcción del contenedor de dependencias (DI), incluyendo logging.
3. Aplicación del tema visual antes de que la ventana exista.
4. Migraciones de settings: si la versión de la app cambió desde la última ejecución, se ejecutan migraciones síncronas
   antes de que nadie consuma los settings.
5. Inicialización del ViewModel principal, que lanza la comprobación de actualizaciones en background (no bloquea).
6. Creación de la ventana principal.
7. Registro del bridge de portapapeles (permite que Core copie sin depender de Avalonia).
8. Registro de la hotkey global y del handler de cierre.
9. Inicio de las fuentes de búsqueda (cada fuente carga sus datos de forma independiente).
10. Espera a que todas las fuentes instantáneas estén listas, y solo entonces se muestra la ventana.

**Invariantes del arranque:**

- El usuario nunca ve la ventana sin apps en los resultados.
- Las migraciones se completan antes de que cualquier componente lea los settings.
- La comprobación de actualizaciones nunca bloquea la aparición de la ventana.

> **Verificar en:** `App.axaml.cs` → `OnFrameworkInitializationCompleted` (orden de los pasos),
`GlobalSearch.Start()`, `ShowWhenInstantReadyAsync()`.

### Mostrar / Ocultar

La ventana se controla exclusivamente via hotkey global + Escape/Enter. No hay forma de cerrarla desde el OS (el cierre
nativo se intercepta y convierte en hide).

- **Hotkey global** (configurable por el usuario): si la ventana está visible y activa → se oculta; si no → se muestra y
  activa.
- **Al mostrar**: se captura cuál era la app en primer plano (para poder restaurarla después), se da foco al campo de
  búsqueda.
- **Al ocultar**: se restaura el foco a la app que tenía el usuario antes de invocar Yottacast.
- **El campo de búsqueda se desactiva** mientras la ventana está oculta para evitar que reciba input de fondo.

**Invariantes:**

- La ventana nunca se destruye en runtime; solo se oculta y muestra.
- El cierre nativo (Cmd+W, botón X, `performClose:` de macOS) siempre se convierte en hide.
- La hotkey se suprime a nivel de OS para evitar que produzca pitidos o efectos en la app anterior.

> **Verificar en:** `App.axaml.cs` → `RegisterGlobalHotKey` (supresión síncrona con `SimpleGlobalHook`), `MainWindow` →
`OnClosing`, `AppHandler` → `OnShow`/`OnHide`.

### Cierre de la aplicación

Al cerrar la app (no la ventana), se detienen las fuentes de búsqueda (`globalSearch.Stop()`). No hay persistencia de
estado en cierre; los settings se guardan en el momento en que cambian.

## Búsqueda

### Dos fases, dos tipos de fuente

| Tipo                                   | Comportamiento                                                 | Ejemplos                                           |
|----------------------------------------|----------------------------------------------------------------|----------------------------------------------------|
| **Instant** (`IInstantSearchSource`)   | En memoria, síncrono, responde sin delay al escribir           | Apps, calculadora, emojis, búsqueda web, historial de portapapeles |
| **Deferred** (`IDeferredSearchSource`) | Acceso a disco/red, asíncrono, se lanza tras 250ms de debounce | Ficheros del usuario, diccionario                 |

Cada fuente produce un **snapshot completo** (los mejores N resultados ordenados), no ítems individuales. `GlobalSearch`
mantiene un slot por fuente deferred y mezcla los snapshots en cada actualización.

### Flujo de búsqueda

1. El usuario escribe texto → se cancela cualquier búsqueda anterior.
2. **Fase instant**: se ejecuta inmediatamente (sin delay). Todas las fuentes instant reciben la query y devuelven
   resultados síncronamente.
3. **Fase deferred**: tras 250ms de debounce, se lanzan las fuentes deferred en paralelo. A medida que cada fuente
   responde, los resultados se mezclan con los de la fase instant.
4. **Query vacía**: limpia los resultados sin buscar.
5. **Modo emoji** (query empieza por `:`): solo se ejecuta la fase instant; la fase deferred se omite.

**Invariantes:**

- Un cambio de texto siempre cancela la búsqueda deferred anterior.
- Los resultados instant aparecen sin latencia perceptible.
- Las fuentes deferred nunca bloquean la respuesta instant.

> **Verificar en:** `MainWindowViewModel` → `OnSearchTextChanged`, `GlobalSearch` → `SearchInstant`/
`SearchDeferredAsync`. Detalle completo en `docs/search-sources.md`.

### Readiness de fuentes

Cada fuente expone un mecanismo de "estoy lista" (`WhenReady()`). Las fuentes sin arranque asíncrono se declaran listas
inmediatamente. Las que necesitan cargar datos (ej: escaneo de apps) completan su readiness al terminar la carga
inicial.

- `GlobalSearch.WhenInstantReady()` espera solo a las fuentes instant → controla cuándo se muestra la ventana.
- `GlobalSearch.WhenReady()` espera a todas → lo usa Settings para garantizar que los caches de descubrimiento de
  browsers/terminales están poblados.

> **Verificar en:** `GlobalSearch` → `WhenReady`/`WhenInstantReady`, `ApplicationSearch` → `ScanAndWatchAsync` (completa
> readiness al terminar el scan y luego instala watchers).

## Teclado

### Hotkey global

La hotkey es configurable en settings. La supresión de la tecla a nivel de OS requiere procesamiento síncrono en el hilo
del hook (no se puede usar un pool de hilos asíncrono).

### Atajos en la ventana principal

| Tecla                         | Comportamiento                                                                                                                                                                      |
|-------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Escape** (3 niveles)        | 1) Si hay búsqueda deferred en curso → cancela la búsqueda y limpia el texto. 2) Si hay texto → limpia el texto. 3) Si el texto está vacío → oculta la ventana.                     |
| **Enter**                     | Activa el resultado seleccionado, limpia el texto y oculta la ventana. Si el resultado tiene `PasteAfterActivate`, además restaura el foco a la app anterior y simula Cmd+V/Ctrl+V. |
| **Flechas arriba/abajo**      | Navegación circular por la lista de resultados (del último salta al primero y viceversa).                                                                                           |
| **Flechas izquierda/derecha** | Capturadas por el resultado seleccionado si lo soporta (ej: grid de emojis). Si no, el TextBox las consume normalmente.                                                             |
| **Cmd+,** (macOS)             | Abre la ventana de Settings.                                                                                                                                                        |
| **Cmd+W / Ctrl+W / Ctrl+F4**  | Atajo de cierre de ventana (OS-específico). Se intercepta y convierte en hide.                                                                                                      |
| **Alt+Space**                 | Se consume explícitamente para evitar pitidos de macOS.                                                                                                                             |

**Invariantes:**

- Escape siempre tiene una salida: nunca deja al usuario atrapado.
- Las teclas de flecha en ítems tipo grid (emojis) se interceptan en fase tunnel, antes de que el TextBox las consuma.
- La navegación por la lista de resultados desactiva la auto-selección de resultados de calculadora.

> **Verificar en:** `MainWindow` → `OnKeyDown` (atajos, Escape, Enter), handler tunnel (flechas para grids).

## Resultados

Todos los resultados de búsqueda se representan como `ResultItemViewModel`, un tipo inmutable (`init`-only) que todas
las fuentes producen y la UI consume.

**Capacidades opcionales de un resultado:**

- **Acción al activar** (`OnActivate`): qué hacer cuando el usuario pulsa Enter. Puede ser null si el ítem es solo
  informativo.
- **Pegar automáticamente** (`PasteAfterActivate`): tras activar, restaurar foco a la app anterior y simular paste.
  Usado por emojis.
- **Navegación interna** (`OnLeft/OnRight/OnUp/OnDown`): el resultado puede capturar teclas de flecha para navegación
  interna (ej: grid de emojis). Si el resultado no consume la tecla, la ventana aplica la navegación estándar de lista.
- **Atajo decorativo** (`Shortcut`): texto para mostrar un atajo en la UI. No genera lógica de teclado.

**Grid de emojis** - caso especial: un resultado que contiene una cuadrícula de 8 columnas. La navegación horizontal
wrappea (del último emoji salta al primero). La navegación vertical devuelve el control a la ventana cuando llega al
borde, permitiendo al usuario salir del grid con las flechas.

> **Verificar en:** `ResultItemViewModel`, `BaseResultItemViewModel`, `EmojiGridResultViewModel`, `EmojiCellViewModel` (
> todos en `Yottacast.Core/ViewModels/`).

## Arquitectura multiplataforma

### Separación de responsabilidades

| Capa               | Responsabilidad                                                                                                              |
|--------------------|------------------------------------------------------------------------------------------------------------------------------|
| `Yottacast.Core`   | Lógica de búsqueda, ViewModels, settings, scoring. Sin dependencia de Avalonia.                                              |
| `Yottacast`        | UI (Avalonia), arranque, hotkey, integraciones OS.                                                                           |
| `PlatformProvider` | Abstracción de operaciones OS: escaneo de apps, watchers, rutas de sistema.                                                  |
| `AppHandler`       | Abstracción de interacciones OS con la UI: mostrar/ocultar ventana, capturar/restaurar foco, simular paste, atajo de cierre. |

### Contrato de AppHandler por plataforma

| Operación       | macOS                                                    | Windows                            | Linux                              |
|-----------------|----------------------------------------------------------|------------------------------------|------------------------------------|
| Arranque        | `NSApplicationActivationPolicyAccessory` (sin Dock icon) | -                                  | -                                  |
| Captura de foco | Captura la app en primer plano                           | Captura la ventana en primer plano | Captura la ventana en primer plano |
| Paste simulado  | `CGEvent` (Cmd+V)                                        | `keybd_event` (Ctrl+V)             | No implementado                    |
| Atajo de cierre | Cmd+W                                                    | Ctrl+F4                            | Ctrl+W                             |

> **Verificar en:** `AppHandler.cs`, `MacAppHandler.cs`, `WindowsAppHandler.cs`, `LinuxAppHandler.cs` (en
`Yottacast/Services/`). Detalle completo en `docs/multi-platform.md`.

### Portapapeles: bridge Core → UI

`Yottacast.Core` no puede depender de Avalonia, pero necesita leer y copiar el portapapeles. Se resuelve con callbacks: al arrancar, la capa UI inyecta dos funciones - una para escritura y otra para lectura - que encapsulan el acceso a Avalonia con el marshal al UI thread. Core llama `clipboardService.CopyText(text)` para copiar y `clipboardService.ReadTextAsync()` para leer.

La lectura del portapapeles se usa en `ClipboardSearch` (via `MainWindow.HandleWindowShownAsync`) para detectar URLs o rutas locales al abrir la ventana.

> **Verificar en:** `ClipboardService` (en `Yottacast.Core/Services/`), inicialización en `App.axaml.cs`, `ClipboardSearch.cs`, `MainWindow.axaml.cs` -- `HandleWindowShownAsync`.

## Settings y persistencia

- Los settings se cargan desde un JSON al arranque y se guardan inmediatamente al cambiar (no al cerrar).
- La validación de browser/terminal activos no ocurre en el arranque; se auto-repara en el momento de uso.
- Las migraciones comparan la versión de la app con la última versión ejecutada y se ejecutan síncronamente antes de que
  nadie consuma los settings.
- La ventana de Settings se abre inmediatamente cuando el usuario pulsa Cmd+, (no espera al caché de apps). El descubrimiento de browsers y terminales se realiza de forma lazy al abrir el dropdown correspondiente.

> **Verificar en:** `UserSettings` → `Load`/`Save`, `App.axaml.cs` → `RunMigrations`, `App.OpenSettings`. Detalle en
`docs/user-settings.md`.

## Logging

Fichero con rotación diaria y retención de 7 días. Ruta OS-específica. El backend es Serilog pero todos los servicios
usan `ILogger<T>` estándar de Microsoft, desacoplando la implementación.

> **Verificar en:** `App.axaml.cs` → `BuildServices` (configuración Serilog), `AppPaths` (rutas de log). Detalle en
`docs/logging.md`.

## Servicios registrados en DI

Todos los servicios se registran en `App.axaml.cs` → `BuildServices()`. La lista canónica está en el código; aquí se
documenta la intención de cada grupo:

| Grupo             | Servicios (no exhaustivo)                                                                                                                     | Lifetime  | Propósito                  |
|-------------------|-----------------------------------------------------------------------------------------------------------------------------------------------|-----------|----------------------------|
| Plataforma        | `PlatformProvider`, `ProcessRunner`                                                                                                           | Singleton | Abstracción de OS          |
| Config            | `UserSettings`                                                                                                                                | Singleton | Configuración persistente  |
| Búsqueda instant  | `ApplicationSearch`, `CalculatorSearch`, `EmojiSearch`, `WebSearchSource`, `LocalPathSearch`, `UrlSearch`, `DateSearch`, `ClipboardHistorySearch`, `SystemSettingsSearch` (macOS) | Singleton | Fuentes rápidas en memoria |
| Búsqueda deferred | `UserDocumentSearch`, `DictionarySource`, `RandomSearch`                                                                                      | Singleton | Fuentes lentas (disco/red) |
| Empty-state       | `NewlyInstalledAppsSource`, `ClipboardSearch` (`IEmptyStateSource`)                                                                           | Singleton | Resultados con query vacía  |
| Orquestación      | `GlobalSearch`                                                                                                                                | Singleton | Agrega y mezcla fuentes    |
| Cache de iconos   | `AppIconCache`, `FileIconCache`, `FaviconCache`                                                                                               | Singleton | Cache dos niveles (mem+disco) |
| Persistencia      | `HistoryService`, `LaunchHistory`, `ClipboardHistoryStore`, `EmojiUsageStore`                                                                 | Singleton | Estado e historiales en disco |
| Soporte           | `UpdateChecker`, `BrowserDiscovery`, `TerminalDiscovery`, `FileSearch`, `FileEditorService`, `ClipboardService`, `MathJsEngineProvider`, `NerdamerEngine`, `ExchangeRateService`, `EmojiDataLoader`, `PluginService`, `ThemeService`, `HttpClient` | Singleton | Servicios auxiliares       |
| ViewModels        | `MainWindowViewModel`, `SettingsWindowViewModel`                                                                                              | Transient | Estado de UI por ventana   |

La lista canónica y completa vive en el código; esta tabla solo documenta los grupos y su intención.

> **Verificar en:** `App.axaml.cs` → `BuildServices()`.