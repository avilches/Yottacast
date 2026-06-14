# Ventana principal -- comportamiento y contratos

Este documento describe el comportamiento esperado de la ventana principal de Yottacast (el launcher): ciclo de vida, layout, posicionamiento, arrastre, temporizadores y ocultacion del cursor. Se organiza por capacidades y contratos verificables, no por archivos de codigo fuente.

La fase de busqueda dentro de la ventana (fases instant/diferida, ordenacion y auto-seleccion, busqueda web en la lista, footer hints, score debug y hint de busqueda) se documenta aparte en `docs/ui-main-window-search.md`. Los atajos de teclado y la navegacion estan en `docs/ui-hotkeys.md`.

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

## 2. Atajos de teclado y navegacion

Los atajos de teclado (Escape, Enter y activacion, copy con Cmd+C, cierre de ventana, Cmd+Q, Cmd+,) y la navegacion de la lista de resultados (flechas, delegacion a items con navegacion interna, grid de emojis) se documentan en detalle en `docs/ui-hotkeys.md`. Esta seccion solo resume lo especifico de la ventana principal y delega el detalle.

Resumen:

- **Escape** aplica una cascada: cierra editor/menu, cancela busqueda diferida, limpia texto, u oculta la ventana, en ese orden de prioridad.
- **Enter** activa la accion por defecto del resultado seleccionado, limpia el texto y oculta la ventana (con paste automatico si el item lo pide). `Cmd/Ctrl+Enter` ejecuta sin ocultar.
- **Flechas arriba/abajo** navegan la lista de forma circular; **izquierda/derecha** se delegan al item seleccionado si tiene navegacion interna.
- **Page Up / Page Down** saltan una pagina visible: `SelectDelta(±GetVisiblePageSize())`, donde `GetVisiblePageSize()` se calcula a partir del alto del viewport de resultados y `AppDefaults.ResultItemMinHeight`. No salta un numero fijo de items.
- **Cmd+C / Ctrl+C** copia el valor del resultado seleccionado sin cerrar la ventana.

> **Verificar en:** `docs/ui-hotkeys.md` (detalle completo). `MainWindow.axaml.cs` -- `OnKeyDown`, `OnTunnelKeyDown`, `SelectNext`, `SelectDelta`, `GetVisiblePageSize`. `AppDefaults.cs` -- `ResultItemMinHeight`.

---

## 3. Ocultacion automatica del cursor del raton

Mientras el usuario escribe, el cursor del raton se oculta para no distraer. Se vuelve a mostrar cuando el raton se mueve de su posicion original. El sistema rastrea la posicion en pantalla para distinguir movimientos reales del usuario de movimientos sinteticos causados por el redimensionamiento de la ventana (cuando aparecen resultados).

> **Verificar en:** `MainWindow.axaml.cs` -- `HideCursor`, `ShowCursor`, `TrackOrShowCursor`, `OnTunnelPointerMoved`.

---

## 4. Seleccion con raton

| Gesto | Comportamiento |
|---|---|
| Click izquierdo | Selecciona el elemento. Si habia un menu de opciones abierto, se cierra. |
| Doble click izquierdo | Ejecuta la accion por defecto del elemento (equivalente a Enter). Cmd+doble click (macOS) / Ctrl+doble click (Windows/Linux) ejecuta sin cerrar la ventana (`AsKeepOpen()`). |
| Click derecho | Selecciona el elemento y abre el menu de opciones en la posicion del cursor. Las opciones son clicables con el raton. |

El movimiento del raton sobre resultados ya no selecciona el elemento bajo el cursor. La seleccion solo cambia por teclado o por click.

`OnResultsDoubleTapped` decide el modo "sin cerrar" comprobando `_lastClickModifiers.HasFlag(AppHandler.Instance.MetaKeyModifier)`, que resuelve a Cmd en macOS y a Ctrl en Windows/Linux. Igual que el `Cmd/Ctrl+Enter` de teclado.

> **Verificar en:** `MainWindow.axaml.cs` -- `OnResultsPointerPressed`, `OnResultsDoubleTapped`.

---

## 5. Estado vacio: IEmptyStateSource

Cuando el buscador esta vacio (sin texto), la ventana muestra resultados procedentes de fuentes de estado vacio (`IEmptyStateSource`). Cada fuente es independiente, registrada en DI, y puede actualizar sus resultados reactivamente.

### Fuentes activas

| Fuente | Comportamiento |
|---|---|
| `NewlyInstalledAppsSource` | Muestra apps detectadas por `FileSystemWatcher` despues del scan inicial. Se acumulan mientras el buscador esta vacio; se descartan cuando el usuario empieza a escribir. Reacciona a `AppAdded` e `IconLoaded` disparando `ResultsChanged`. |
| `ClipboardSearch` | Al abrir la ventana, lee el portapapeles. Si contiene una URL valida o ruta local existente, muestra un resultado con `· from clipboard` en el titulo. No reacciona a cambios del portapapeles; solo se actualiza al abrir la ventana. |

### Ciclo de vida

Al abrir la ventana con buscador vacio:
1. `MainWindow` llama `vm.OnWindowShown(null)` inmediatamente - las fuentes ya activas muestran sus resultados al instante.
2. `MainWindow` lee el portapapeles en background y llama `vm.OnWindowShown(text)` si hay contenido - `ClipboardSearch` puede añadir un resultado.

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

## 6. Banner de actualizacion

Cuando hay una version nueva disponible, se muestra una franja clicable al pie de la ventana con el texto `"Yottacast {version} available -- click to download"`. El comando de clic es actualmente un placeholder sin implementacion.

> **Verificar en:** `MainWindowViewModel.cs` -- `CheckForUpdateAsync`, `UpdateBannerClick`. `MainWindow.axaml` -- seccion "Update banner".

---

## 7. Posicionamiento y arrastre

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

## 8. Layout de la ventana

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

El panel de preview/editor (`EditorPanelView`) no tiene cabecera en modo preview. Su parte inferior tiene siempre el mismo aspecto que el footer de la lista de resultados: separador superior (`BorderThickness="0,1,0,0"`), mismo fondo (`Theme.Footer.Background`) y sin esquinas redondeadas propias (`CornerRadius="0"` - la ventana exterior ya gestiona el redondeo). En modo edición, el footer muestra además los atajos de teclado (`⌘S`, `⌘E`, `Esc`).

> **Verificar en:** `MainWindow.axaml` -- atributos del `Window`, `Grid x:Name="ResultsPanel"` y `Panel x:Name="EditorContainer"`.

---

## 9. Preservación del texto al ocultar (decay timer)

El comportamiento del campo de búsqueda al ocultar la ventana depende del setting `KeepValueWhenHide`:

| Setting | Comportamiento al ocultar |
|---|---|
| `KeepValueWhenHide = false` | El texto se limpia inmediatamente (`CleanAndSaveHistory(null)`), igual que pulsar Escape |
| `KeepValueWhenHide = true`, duración > 0 | Se inicia un timer; si la ventana reaparece antes de que expire, el texto se conserva; si expira, se limpia |
| `KeepValueWhenHide = true`, duración = 0 (Siempre) | No se inicia timer; el texto se conserva indefinidamente |

En modo sticky, al perder el foco la ventana se oculta si el campo está vacío. Si hay texto, el timer se inicia al perder el foco (aunque la ventana siga visible), y se cancela al recuperarlo.

El timer vive en `MainWindowViewModel` como un `CancellationTokenSource` (`_decayCts`). `MainWindow` lo arranca y cancela desde los eventos `IsVisible`, `Deactivated` y `Activated`.

> **Verificar en:** `MainWindowViewModel.StartDecayTimer()`, `MainWindowViewModel.CancelDecayTimer()` - `Yottacast/ViewModels/MainWindowViewModel.cs`. Hooks en `MainWindow.OnPropertyChanged`, `Activated`, `Deactivated` - `Yottacast/Views/MainWindow.axaml.cs`.

---

## Documentos relacionados

- `docs/ui-main-window-search.md` -- fases de busqueda, ordenacion y auto-seleccion, busqueda web en la lista, footer hints, score debug, hint de busqueda.
- `docs/ui-hotkeys.md` -- atajos de teclado y navegacion de resultados.
- `docs/app-design.md` -- ciclo de vida y arquitectura general de la app.
