# Ventana principal -- comportamiento y contratos

Este documento describe el comportamiento esperado de la ventana principal de Yottacast (el launcher). Se organiza por capacidades y contratos verificables, no por archivos de codigo fuente.

---

## 1. Ciclo de vida de la ventana

La ventana principal es persistente: nunca se destruye, solo se oculta y se vuelve a mostrar. Esto garantiza tiempos de apertura instantaneos.

| Invariante | Detalle |
|---|---|
| La ventana nunca se cierra salvo Cmd+Q | Todo intento de cierre se cancela y se traduce en `Hide()`, excepto cuando macOS envia `applicationShouldTerminate:` (Cmd+Q), que termina el proceso. |
| Al mostrarse, el foco va al campo de busqueda | Tanto al abrir por primera vez como al volver a ser visible. |
| Al ocultarse, el campo de busqueda se deshabilita | Evita que reciba input mientras la ventana no es visible. Se rehabilita al mostrarse. |
| El estado de Alt se limpia al ocultar | `IsAltPressed` se pone a `false` cuando la ventana deja de ser visible. |

> **Verificar en:** `MainWindow.axaml.cs` -- `OnClosing`, `OnPropertyChanged` (handler de `IsVisibleProperty`), constructor (handler de `Opened`).

---

## 2. Busqueda: fases y tiempos

La busqueda se divide en dos fases con distinto coste y latencia.

### Fase instant (sin retardo)

Cuando el usuario escribe, las fuentes en memoria (apps cacheadas, emojis, calculadora, fechas, web search) se consultan de forma sincrona. Los resultados aparecen inmediatamente.

### Fase diferida (con debounce de 250 ms)

Tras la fase instant, se espera 250 ms sin nuevas pulsaciones antes de consultar las fuentes de disco (busqueda de archivos via Spotlight/Windows Search). Mientras estas fuentes trabajan, se muestra un spinner de actividad en lugar del badge ESC.

### Modo emoji (prefijo `:`)

Cuando la query empieza por `:`, solo se ejecuta la fase instant. No hay debounce ni fase diferida.

| Invariante | Detalle |
|---|---|
| El usuario ve resultados en memoria sin retardo perceptible | La fase instant se ejecuta de forma sincrona antes de cualquier espera. |
| Cada nueva pulsacion cancela la busqueda anterior | Se crea un nuevo `CancellationTokenSource` por query. |
| El spinner solo es visible durante la fase diferida | `IsSearching` se activa justo antes de iterar las fuentes diferidas y se desactiva en el `finally`. |
| En modo emoji no se accede a disco | `SearchAsync` retorna inmediatamente tras la fase instant si la query empieza por `:`. |
| Limite por fuente: 10 resultados | Definido en `AppDefaults.SearchSourceLimit`. |

> **Verificar en:** `MainWindowViewModel.cs` -- `SearchAsync`, `OnSearchTextChanged`. `AppDefaults.cs` -- `SearchDebouncedMs`, `SearchSourceLimit`.

---

## 3. Resultados: ordenacion y auto-seleccion

Los resultados de ambas fases se combinan (merge) ordenados por score descendente.

### Auto-seleccion de calculadora/conversor

Si existe un resultado de tipo `CalculatorResultItemViewModel` o `ConversionResultItemViewModel`, y el usuario no ha navegado con las flechas, ese resultado se selecciona automaticamente. Esto permite ver el resultado de la calculadora sin necesidad de navegar.

### Preservacion de seleccion tras navegacion manual

Si el usuario ha navegado manualmente (flechas arriba/abajo), el sistema intenta preservar el item que tenia seleccionado. Si ese item ya no esta en los resultados, se selecciona el primero.

### Reset de navegacion por query

Cada nueva query reinicia el flag de navegacion manual (`_userNavigated = false`), restaurando el comportamiento de auto-seleccion.

| Invariante | Detalle |
|---|---|
| `ShowNoResults` solo aparece si la fase diferida completo sin cancelacion y hay 0 resultados | Si se cancelo (ESC o nueva query), los resultados parciales permanecen visibles. |
| `ShowNoResults` se limpia en cada `RefreshResults` | Solo `SearchAsync` puede activarlo a `true`. |

> **Verificar en:** `MainWindowViewModel.cs` -- `RefreshResults`, `SearchAsync`, `NotifyUserNavigated`.

---

## 4. Busqueda web (antes "Google item")

Yottacast soporta multiples motores de busqueda web configurables. Cada motor puede funcionar en dos modos:

| Modo | Comportamiento | Score |
|---|---|---|
| `ShowAlways` | El motor aparece siempre que haya texto de busqueda (salvo si hay un motor con prefijo activo). | 3.0 |
| `PrefixOnly` | El motor solo aparece cuando la query comienza con su prefijo (ej. `yt video`). | 3.5 |

Cuando un motor de tipo `PrefixOnly` coincide, los motores `ShowAlways` se ocultan para no ensuciar los resultados.

Los motores web no aparecen en modo emoji (queries que empiezan con `:`).

> **Verificar en:** `WebSearchSource.cs` -- `Search`.

---

## 5. Atajos de teclado

### Tecla Escape -- jerarquia de tres niveles

El handler de ESC aplica esta logica en cascada:

1. Si hay una busqueda diferida en curso (`IsSearching == true`): cancela la fase diferida **y limpia el texto de busqueda**.
2. Si el texto no esta vacio (y no habia busqueda diferida): limpia el texto.
3. Si el texto ya esta vacio: oculta la ventana.

### Tecla Enter -- activacion y paste

Al pulsar Enter sobre un resultado seleccionado:

1. Ejecuta la accion del resultado (`OnActivate`).
2. Limpia el texto de busqueda.
3. Oculta la ventana.
4. Si el resultado tiene `PasteAfterActivate = true` (usado por emojis): devuelve el foco a la app anterior y simula un pegado (Cmd+V / Ctrl+V).

### Cierre nativo de ventana

El atajo nativo de "cerrar ventana" se intercepta y se redirige a ocultar la ventana.

| Plataforma | Atajo |
|---|---|
| macOS | Cmd+W |
| Windows | Ctrl+F4 |
| Linux | Ctrl+W |

### Salir de la aplicacion (solo macOS)

`Cmd+Q` cierra el proceso completamente. macOS intercepta este atajo a nivel de `NSApplication` antes de que llegue al key handler; Avalonia lo expone como `ShutdownRequested` en el lifetime.

### Otros atajos

| Atajo | Accion |
|---|---|
| Cmd+, (macOS) | Abre la ventana de Settings |
| Cmd+C / Ctrl+C | Copia el valor del resultado seleccionado sin cerrar la ventana |
| Alt+Space | Se consume sin accion para evitar el beep nativo de macOS |
| Cmd+P | Abre el panel de preview del archivo seleccionado (solo archivos de texto) |
| Cmd+E | Abre directamente el editor del archivo seleccionado; si el panel está en preview, cambia a modo edición |

> **Verificar en:** `MainWindow.axaml.cs` -- `OnKeyDown`. `App.axaml.cs` -- `ShutdownRequested`. `MacAppHandler.cs`, `WindowsAppHandler.cs`, `LinuxAppHandler.cs` -- `CloseWindowShortcut`.

---

## 6. Navegacion de lista

### Navegacion circular y por salto

| Tecla | Comportamiento |
|-------|----------------|
| Arriba / Abajo | Circular entre todos los resultados: al llegar al extremo vuelve al opuesto. Formula: `(current + delta + Count) % Count`. |
| Page Down / Page Up | Salta `AppDefaults.SearchSourceLimit` items hacia adelante/atras, detenienndose en el extremo sin circular. |

### Captura de flechas en fase tunnel

La ventana registra un handler en la fase tunnel (`RoutingStrategies.Tunnel`) que se ejecuta antes de que el `TextBox` procese las teclas. Esto permite que los items con navegacion interna (como el grid de emojis) capturen las flechas antes de que muevan el cursor del campo de texto.

Cada item puede definir handlers opcionales (`OnLeft`, `OnRight`, `OnUp`, `OnDown`) que devuelven `bool`:
- `true`: la tecla fue consumida por el item (el `TextBox` no mueve el cursor).
- `false`: la tecla pasa al siguiente handler (movimiento de cursor del TextBox o navegacion de lista).

### Navegacion del grid de emojis

| Tecla | Comportamiento |
|---|---|
| Izquierda/Derecha | Navegacion circular dentro de las celdas del grid (al llegar al final vuelve al principio). |
| Arriba/Abajo | Si el movimiento saldria del grid (primera o ultima fila), devuelve `false` y delega la navegacion al nivel de lista. |

> **Verificar en:** `MainWindow.axaml.cs` -- `OnTunnelKeyDown`, `SelectNext`, `SelectDelta`. `AppDefaults.cs` -- `SearchSourceLimit`. `EmojiGridResultViewModel.cs` -- `SelectDown`, `SelectUp`, `SelectNext`, `SelectPrevious`. `BaseResultItemViewModel.cs` -- `OnLeft`, `OnRight`, `OnUp`, `OnDown`.

---

## 7. Ocultacion automatica del cursor del raton

Mientras el usuario escribe, el cursor del raton se oculta para no distraer. Se vuelve a mostrar cuando el raton se mueve de su posicion original. El sistema rastrea la posicion en pantalla para distinguir movimientos reales del usuario de movimientos sinteticos causados por el redimensionamiento de la ventana (cuando aparecen resultados).

> **Verificar en:** `MainWindow.axaml.cs` -- `HideCursor`, `ShowCursor`, `TrackOrShowCursor`, `OnTunnelPointerMoved`.

---

## 8. Seleccion con raton

| Gesto | Comportamiento |
|---|---|
| Click izquierdo | Selecciona el elemento. Si habia un menu de opciones abierto, se cierra. |
| Doble click izquierdo | Ejecuta la accion por defecto del elemento (equivalente a Enter). Cmd+doble click ejecuta sin cerrar la ventana. |
| Click derecho | Selecciona el elemento y abre el menu de opciones en la posicion del cursor. Las opciones son clicables con el raton. |

El movimiento del raton sobre resultados ya no selecciona el elemento bajo el cursor. La seleccion solo cambia por teclado o por click.

> **Verificar en:** `MainWindow.axaml.cs` -- `OnResultsPointerPressed`, `OnResultsDoubleTapped`.

---

## 9. Estado vacio: IEmptyStateSource

Cuando el buscador esta vacio (sin texto), la ventana muestra resultados procedentes de fuentes de estado vacio (`IEmptyStateSource`). Cada fuente es independiente, registrada en DI, y puede actualizar sus resultados reactivamente.

### Fuentes activas

| Fuente | Comportamiento |
|---|---|
| `NewlyInstalledAppsSource` | Muestra apps detectadas por `FileSystemWatcher` despues del scan inicial. Se acumulan mientras el buscador esta vacio; se descartan cuando el usuario empieza a escribir. Reacciona a `AppAdded` e `IconLoaded` disparando `ResultsChanged`. |
| `ClipboardSearch` | Al abrir la ventana, lee el portapapeles. Si contiene una URL valida o ruta local existente, muestra un resultado con `· from clipboard` en el titulo. No reacciona a cambios del portapapeles; solo se actualiza al abrir la ventana. |

### Ciclo de vida

Al abrir la ventana con buscador vacio:
1. `MainWindow` llama `vm.OnWindowShown(null)` inmediatamente — las fuentes ya activas muestran sus resultados al instante.
2. `MainWindow` lee el portapapeles en background y llama `vm.OnWindowShown(text)` si hay contenido — `ClipboardSearch` puede añadir un resultado.

Cuando el usuario empieza a escribir, `OnSearchStarted()` se llama en todas las fuentes: `NewlyInstalledAppsSource` descarta sus pendientes, `ClipboardSearch` limpia su cache.

Cuando una fuente dispara `ResultsChanged` (p.ej. nueva app instalada con buscador vacio), el ViewModel refresca el estado vacio sin volver a leer el portapapeles.

### Apps recien instaladas: detalle

| Estado del buscador | Comportamiento |
|---|---|
| Vacio | La app aparece inmediatamente. |
| Con texto | Se refresca la busqueda instant; si la app coincide, aparece. |
| El usuario empieza a escribir | Las apps pendientes se descartan. |
| Se carga un icono | Si el buscador esta vacio, se reconstruye la lista para reflejar el icono. |

El tracking solo se activa despues de que `ApplicationSearch` complete su scan inicial (`WhenReady()`), evitando que las apps del scan inicial se traten como recien instaladas.

> **Verificar en:** `IEmptyStateSource.cs`, `NewlyInstalledAppsSource.cs`, `ClipboardSearch.cs`, `MainWindowViewModel.cs` -- `OnWindowShown`, `RefreshEmptyState`, `OnSearchTextChanged`, `OnAppCacheChanged`. `MainWindow.axaml.cs` -- `HandleWindowShownAsync`.

---

## 10. Banner de actualizacion

Cuando hay una version nueva disponible, se muestra una franja clicable al pie de la ventana con el texto `"Yottacast {version} available -- click to download"`. El comando de clic es actualmente un placeholder sin implementacion.

> **Verificar en:** `MainWindowViewModel.cs` -- `CheckForUpdateAsync`, `UpdateBannerClick`. `MainWindow.axaml` -- seccion "Update banner".

---

## 11. Footer: Settings y hints dinámicos

El footer de la ventana principal es siempre visible, independientemente de si hay resultados o no.

### Lado izquierdo: botón Settings

El botón de Settings ocupa la parte izquierda del footer. Muestra un icono de engranaje y el atajo de teclado `⌘,` (macOS). Al pulsarlo, abre la ventana de preferencias (equivalente a `Cmd+,`).

### Lado derecho: hints dinámicos por tipo de resultado

Los hints del lado derecho son contextuales: solo se muestran cuando hay resultados y reflejan las acciones disponibles para el tipo de resultado seleccionado. Cuando no hay resultados, el área de hints está vacía.

Cada tipo de resultado tiene sus propios hints:

| Tipo | Hints mostrados |
|---|---|
| Apps | `↵ Launch` · `⌘C Path` |
| Archivos | `↵ Open` · `⌘C Path` · `⌘P Preview` · `⌘E Edit` (si extensión editable) |
| Calculadora | `↵ Copy result` · `⌘C Copy` |
| Conversor | `↵ Copy result` · `⌘C Copy` · `←→ Switch cell` |
| Diccionario | `↵ Open Wiktionary` · `⌘C Definition` |
| Fecha / Rango de fechas | `↵ Copy` · `⌘C Copy` · `←→ Switch cell` |
| Emoji | `↵ Copy & paste` · `⌘C Copy` · `⌘⇧F Favorite` |
| Búsqueda web | `↵ Search` |

`FooterHints` es una propiedad observable del ViewModel que se actualiza en cada cambio de resultado seleccionado.

### Sin contador de resultados

El footer no muestra el número de resultados. La cantidad de items es visible directamente por la longitud de la lista.

> **Verificar en:** `MainWindow.axaml` (sección footer), `MainWindowViewModel.cs` (`FooterHints`).

---

## 12. Score visible como modo debug

En modo normal, cada item estandar muestra su etiqueta de categoria (ej. "App", "File", "Web"). Si el usuario mantiene pulsada la tecla Alt, la categoria se reemplaza por el score numerico (formato dos decimales). Esto permite depurar el ranking sin herramientas externas.

> **Verificar en:** `MainWindow.axaml` -- DataTemplate de `ResultItemViewModel`, condicion `IsAltPressed`. `MainWindowViewModel.cs` -- `IsAltPressed`. `MainWindow.axaml.cs` -- `OnKeyDown` (Alt), `OnKeyUp` (Alt).

---

## 13. Hint de búsqueda

El área de hint siempre reserva espacio fijo debajo del campo de búsqueda (no hay salto de layout al aparecer o desaparecer). El texto aparece con fade-in de 0.4 s.

Hay dos estilos visuales:

| Estilo | Color | Cuándo se usa |
|---|---|---|
| Error | Rojo (`Theme.Search.Hint.Error`) | Unidades incompatibles en la calculadora (`IncompatibleUnitsConvert`, `IncompatibleUnitsOp`) |
| Info | Gris (`Theme.Search.Hint.Info`) | Hints de ambigüedad de unidades ("Maybe you meant…") y mensajes de copia ("Copied …") |

El hint se limpia automáticamente en cada nueva búsqueda o cuando el texto se vacía.

> **Verificar en:** `MainWindowViewModel.cs` — `SetSearchHint`, `SearchHintIsError`, `SearchHintIsInfo`. `MainWindow.axaml` — `Grid` con `MinHeight` y dos TextBlocks. `GlobalSearch.cs` — `SearchInstant` devuelve `SearchHintKind`. `CalculatorSearch.cs` — `LastHintKind`. `ThemeService.cs` — `Theme.Search.Hint.Error`, `Theme.Search.Hint.Info`.

---

## 14. Posicionamiento y arrastre

### Arrastre con el raton

El usuario puede mover la ventana arrastrando cualquier zona que no sea un control interactivo (SearchBox, ListBox, Button, ScrollViewer). El arrastre se inicia con clic izquierdo y delega en el mecanismo nativo de la plataforma via `BeginMoveDrag`.

### Posicion al mostrar la ventana

Cada vez que la ventana se hace visible, se aplica la logica siguiente:

1. Se obtiene la posicion actual del cursor del raton (via P/Invoke a la plataforma).
2. Se determina en que pantalla esta el cursor (`targetScreen`).
3. Si hay una posicion guardada en `UserSettings.WindowX/Y` Y esa posicion esta dentro del area de trabajo de `targetScreen` → se restaura la posicion guardada.
4. En caso contrario → la ventana se centra en `targetScreen`.

Esto garantiza que la ventana siempre aparece en la pantalla donde esta el cursor. Si el usuario la ha posicionado en una pantalla concreta, esa posicion se respeta mientras siga siendo visible en la pantalla actual.

### Guardado de posicion

La posicion se actualiza en memoria en cada movimiento via `PositionChanged` (sin I/O). Se escribe a disco en tres momentos:

| Momento | Mecanismo |
|---|---|
| Al soltar el raton tras un drag | `OnPointerReleased` detecta el flag `_dragging` |
| Al ocultar la ventana | `Hide()` → `IsVisibleProperty = false` → `SavePosition()` |
| Al cerrar la app | `ShutdownRequested` → `SavePosition()` antes de `Environment.Exit` |

| Invariante | Detalle |
|---|---|
| La ventana siempre aparece en la pantalla con el cursor | Si la posicion guardada no es visible en esa pantalla, se centra |
| Un `kill -9` durante el drag puede perder la posicion | Es el unico caso no cubierto; se acepta |
| `windowX`/`windowY` ausentes en JSON antiguo | Se cargan como `null` y la ventana se centra (retrocompatible) |

> **Verificar en:** `MainWindow.axaml.cs` -- `ApplyPositionOnShow`, `CenterOnScreen`, `SavePosition`, `UpdatePositionInMemory`, `OnRootPointerPressed`, `OnPointerReleased`, `IsOverInteractiveElement`. `App.axaml.cs` -- `ShutdownRequested`. `AppHandler.cs` -- `GetMousePosition`. `MacAppHandler.cs` / `WindowsAppHandler.cs` -- implementacion de `GetMousePosition`. `UserSettings.cs` -- `WindowX`, `WindowY`.

---

## 15. Layout de la ventana

| Propiedad | Valor |
|---|---|
| Decoraciones del sistema | Ninguna (`SystemDecorations="None"`) |
| Fondo | Transparente con borde redondeado |
| Ancho | `Theme.Window.Width` en reposo; `Theme.Window.Width + Theme.Preview.Width` cuando el panel de preview está visible |
| Alto | Ajustado al contenido (`SizeToContent="WidthAndHeight"`) |
| Barra de tareas | No visible (`ShowInTaskbar="False"`) |
| Redimensionable | No |
| Altura maxima de la lista de resultados | `Theme.Results.MaxHeight` px con scroll vertical automatico, sin scroll horizontal |

El panel de preview/editor se muestra a la derecha de la lista de resultados, dentro del mismo `Grid x:Name="ResultsPanel"` (columna 1). La columna 0 tiene siempre `Width=Theme.Window.Width`; la columna 1 (`Theme.Preview.Width`) solo es visible cuando `IsEditorOpen=true`. El resto de elementos (SearchBox, Divider, Footer) tienen `Width=Theme.Window.Width` y `HorizontalAlignment="Left"` para no expandirse cuando el panel de preview amplía la ventana.

El panel de preview/editor (`EditorPanelView`) no tiene cabecera en modo preview. Su parte inferior tiene siempre el mismo aspecto que el footer de la lista de resultados: separador superior (`BorderThickness="0,1,0,0"`), mismo fondo (`Theme.Footer.Background`) y sin esquinas redondeadas propias (`CornerRadius="0"` — la ventana exterior ya gestiona el redondeo). En modo edición, el footer muestra además los atajos de teclado (`⌘S`, `⌘E`, `Esc`).

> **Verificar en:** `MainWindow.axaml` -- atributos del `Window`, `Grid x:Name="ResultsPanel"` y `Panel x:Name="EditorContainer"`.

---

## 16. Preservación del texto al ocultar (decay timer)

El comportamiento del campo de búsqueda al ocultar la ventana depende del setting `KeepValueWhenHide`:

| Setting | Comportamiento al ocultar |
|---|---|
| `KeepValueWhenHide = false` | El texto se limpia inmediatamente (`CleanAndSaveHistory(null)`), igual que pulsar Escape |
| `KeepValueWhenHide = true`, duración > 0 | Se inicia un timer; si la ventana reaparece antes de que expire, el texto se conserva; si expira, se limpia |
| `KeepValueWhenHide = true`, duración = 0 (Siempre) | No se inicia timer; el texto se conserva indefinidamente (comportamiento histórico) |

En modo sticky, al perder el foco la ventana se oculta si el campo está vacío. Si hay texto, el timer se inicia al perder el foco (aunque la ventana siga visible), y se cancela al recuperarlo.

El timer vive en `MainWindowViewModel` como un `CancellationTokenSource` (`_decayCts`). `MainWindow` lo arranca y cancela desde los eventos `IsVisible`, `Deactivated` y `Activated`.

> **Verificar en:** `MainWindowViewModel.StartDecayTimer()`, `MainWindowViewModel.CancelDecayTimer()` — `Yottacast/ViewModels/MainWindowViewModel.cs`. Hooks en `MainWindow.OnPropertyChanged`, `Activated`, `Deactivated` — `Yottacast/Views/MainWindow.axaml.cs`.
