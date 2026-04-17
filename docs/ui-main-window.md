# MainWindow — UI y comportamiento visual

## Indicador de búsqueda en curso (IsSearching)

`MainWindowViewModel.IsSearching` es `true` mientras la fase diferida (`SearchDeferredAsync`) está activa. Se activa justo antes de iterar la fase diferida y se desactiva en el `finally` al completar, cancelar o fallar.

**Spinner en la UI**: cuando `IsSearching` es `true`, la search row muestra un `Ellipse` giratorio (`Classes="spinner"`, animación CSS en `Window.Styles`) en lugar del badge "ESC". La animación pulsa la opacidad (duración definida en `MainWindow.axaml`) con `PlaybackDirection="Alternate"`. Cuando `IsSearching` baja a `false`, la animación se detiene y el badge ESC reaparece (si el texto está vacío).

**`CancelDeferredSearch()`**: cancela solo la fase diferida sin tocar el texto ni la búsqueda instant. Llamado por el handler de ESC cuando `IsSearching == true`. Internamente cancela `_deferredCts`, que es un `CancellationTokenSource` enlazado al `ct` principal — si se teclea texto nuevo, el `ct` padre cancela ambas fases.

**`ShowNoResults`**: solo se activa si la búsqueda diferida completó sin cancelación (`completed = true`). Si se paró con ESC o por nueva búsqueda, los resultados parciales permanecen visibles sin mostrar "No results".

**Gotcha — `ALT+Space` consumido por MainWindow**: MainWindow intercepta `ALT+Space` explícitamente para evitar el beep nativo de macOS cuando la app está en background pero la ventana recibe el evento.

**Gotcha — `ResultItemViewModel.OnUp()`/`OnDown()`**: devuelven `bool`. `true` significa que el ítem ha consumido la tecla (p.ej. navegación interna del grid emoji); `false` delega la navegación de lista a la ventana. Ver `MainWindow.axaml.cs` para el handler.

## Navegación de lista y foco

**Navegación circular**: `SelectNext` avanza/retrocede con `(current + delta + Count) % Count`, haciendo la navegación circular (al llegar al final vuelve al principio y viceversa).

**SearchBox y visibilidad**: al abrir (`Opened`) y al volverse visible (`IsVisibleProperty` changed), la ventana focaliza `SearchBox`. Cuando la ventana se oculta, `SearchBox.IsEnabled` se pone a `false` para evitar que reciba input mientras está escondida; se reactiva con foco cuando vuelve a mostrarse.

## Auto-selección de calculadora y Google item

**Auto-selección de calculadora**: si hay un resultado de categoría `"Calculator"` o `"Converter"` y el usuario no ha navegado con las flechas (`_userNavigated == false`), `RefreshResults()` lo fuerza como `SelectedResult`. Si el usuario había navegado manualmente, intenta preservar el ítem seleccionado previamente; si ya no está en los resultados, selecciona el primero.

**Google item score**: el ítem de Google tiene score fijo 3, garantizando que aparezca por encima de resultados de fuentes de búsqueda (scores ≤ 1) pero es desplazado por resultados de calculadora cuando están presentes (la calculadora también usa score > 1).

**Google item en modo emoji**: el ítem de Google se incluye si `query.Length > 1` (usando `query[1..].Trim()` como término), o es `null` si la query es solo `:`.

## Banner de actualización

Cuando `UpdateChecker.UpdateAvailable` es `true`, `MainWindowViewModel` activa `UpdateAvailable` y rellena `UpdateBannerText` con `"Yottacast {LatestVersion} available — click to download"`, lo que muestra una franja clicable al pie de la ventana principal. El comando `UpdateBannerClickCommand` es un placeholder — conectarlo a la URL de descarga en el siguiente plan.

## Tecla Escape — jerarquía de tres niveles

El handler de ESC en `MainWindow.OnKeyDown` aplica esta lógica en cascada:
1. Si `IsSearching == true` → llama `CancelDeferredSearch()`.
2. Si el texto no está vacío → vacía `SearchText`.
3. En caso contrario → llama `Hide()`.

## Tecla Enter — activación y paste post-acción

Al pulsar Enter, el handler en `MainWindow.OnKeyDown` ejecuta `OnActivate()`, vacía `SearchText` y llama `Hide()`. Si `result.PasteAfterActivate` es `true`, además llama `AppHandler.Instance.OnHide()` seguido de `AppHandler.Instance.SimulatePasteAsync()`, de modo que el contenido copiado por la acción queda pegado inmediatamente en la app anterior.

## Atajo de cierre de ventana por plataforma

`MainWindow.OnKeyDown` intercepta el atajo nativo de "cerrar ventana" obtenido de `AppHandler.Instance.CloseWindowShortcut` (Cmd+W en macOS, Ctrl+F4 en Windows, Ctrl+W en Linux) y lo redirige a `Hide()` en vez de dejar que la ventana se cierre.

## Cmd+, para abrir Settings

`OnKeyDown` intercepta `Key.OemComma + KeyModifiers.Meta` y llama `(Application.Current as App)?.OpenSettings()`.

## OnClosing siempre cancela el cierre

`MainWindow.OnClosing` hace siempre `e.Cancel = true; Hide()`, impidiendo cualquier cierre nativo (por ejemplo el `performClose:` de macOS que puede llegar tras cerrar la SettingsWindow).

## Captura de flechas izquierda/derecha en fase tunnel

`MainWindow` registra `OnTunnelKeyDown` con `RoutingStrategies.Tunnel`, lo que lo ejecuta *antes* de que el `TextBox` procese los movimientos del cursor. `OnLeft` y `OnRight` en `BaseResultItemViewModel` son `Func<bool>?` — si el handler devuelve `true`, el evento se marca como `Handled = true` y el TextBox no mueve el cursor; si devuelve `false`, el evento no se consume y el TextBox procesa el movimiento de cursor normalmente (útil para celdas en el extremo de la navegación). Las teclas Up/Down también pasan por aquí; si el ítem devuelve `false`, la ventana las procesa como navegación de lista en la fase de burbuja.

## SearchSourceLimit

`MainWindowViewModel` define `SearchSourceLimit = 10` como número máximo de resultados que se piden a cada fuente en cada búsqueda, tanto instant como deferred.

## Emoji mode: solo fuentes instant, sin debounce ni deferred

Cuando la query empieza por `:`, `SearchAsync` retorna inmediatamente después de la fase instant, sin esperar el debounce de 250 ms ni lanzar las fuentes deferred.

## Debounce solo para la fase deferred

El `Task.Delay(250, ct)` se sitúa *después* de publicar los resultados instant. El usuario ve resultados de memoria de inmediato; solo el acceso a disco se retrasa.

## Reset de `_userNavigated` en cada nueva búsqueda

`OnSearchTextChanged` pone `_userNavigated = false` al inicio de cada nueva query, antes de llamar a `SearchAsync`. Esto garantiza que la auto-selección de calculadora funcione en cada búsqueda nueva, independientemente de si el usuario navegó en la búsqueda anterior.

## `ShowNoResults` siempre se limpia en `RefreshResults`

`RefreshResults` pone `ShowNoResults = false` en cada llamada. Solo `SearchAsync` puede activarlo a `true`, y únicamente cuando la fase deferred completa sin cancelación y `Results.Count == 0`.

## Footer de resultados

La ventana muestra un footer con el recuento de resultados (`Results.Count`) y los atajos de teclado (navegar con ↑↓, abrir con ↵). El footer solo es visible cuando `HasResults` es `true`.

## Score visible en la UI

La plantilla estándar de ítem (`ResultItemViewModel`) muestra el `Score` formateado a dos decimales junto a la etiqueta de categoría, con opacidad reducida (0.6). La lista de resultados tiene una altura máxima de 416 px con scroll vertical automático y sin scroll horizontal.

## Apps recién instaladas (pending apps)

Cuando el sistema detecta una app nueva via `FileSystemWatcher` (después del scan inicial), `MainWindowViewModel` la almacena en `_pendingAppInfos: List<AppInfo>`.

**`StartTrackingNewAppsAsync()`** — se suscribe a `appSearch.AppAdded` solo tras `appSearch.WhenReady()`, de modo que las apps del scan inicial no se tratan como "recién instaladas".

**`ShowPendingApps()`** — reconstruye `Results` llamando `appSearch.CreateResultItem(info)` para cada `AppInfo` pendiente. Se reconstruyen los `ResultItemViewModel` en cada llamada para capturar el icono más reciente del caché (los iconos pueden no estar disponibles cuando llega el evento `AppAdded`).

**Ciclo de vida de `_pendingAppInfos`**:
- **App instalada con buscador vacío** → se añade a `_pendingAppInfos` y se muestra inmediatamente.
- **App instalada con buscador con texto** → se refresca `SearchInstant` con la query actual; si la app coincide, aparece. No va a `_pendingAppInfos`.
- **Usuario empieza a escribir** → `_pendingAppInfos.Clear()` — las apps pendientes se descartan permanentemente.
- **Usuario borra el texto (vuelve a vacío)** → `ShowPendingApps()` muestra las apps que quedaban (si aún no se había escrito nada).
- **Hide/Show de Yottacast** → `_pendingAppInfos` persiste en memoria; las apps siguen visibles al volver a abrir.
- **Icono cargado** (`IconLoaded`) → si hay pendientes y el buscador está vacío, `ShowPendingApps()` reconstruye la lista para reflejar el icono recién disponible.

## Navegación interna del grid emoji — comportamiento en los bordes

`EmojiGridResultViewModel.SelectDown()`/`SelectUp()` devuelven `false` si el movimiento saldría fuera del grid (primera o última fila), delegando la navegación al nivel de lista. `SelectNext()`/`SelectPrevious()` (flechas derecha/izquierda) siempre envuelven circularmente dentro de las celdas del grid.
