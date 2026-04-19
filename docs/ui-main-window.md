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

Cuando el usuario escribe, las fuentes en memoria (apps cacheadas, emojis, calculadora, web search) se consultan de forma sincrona. Los resultados aparecen inmediatamente.

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
| Alt+Space | Se consume sin accion para evitar el beep nativo de macOS |

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

Mover el raton sobre un resultado lo selecciona (hover-to-select), pero solo si el cursor no esta oculto. Hacer clic (tap) sobre un resultado ejecuta la misma logica que Enter: activacion, limpieza de texto, ocultacion de ventana y paste si corresponde.

> **Verificar en:** `MainWindow.axaml.cs` -- `OnResultsPointerMoved`, `OnResultsTapped`.

---

## 9. Apps recien instaladas (pending apps)

Cuando el sistema detecta una app nueva (via `FileSystemWatcher`, despues del scan inicial), la ventana principal reacciona en funcion del estado del buscador:

| Estado del buscador | Comportamiento |
|---|---|
| Vacio | La app se anade a la lista de pendientes y se muestra inmediatamente. |
| Con texto | Se refresca la busqueda instant; si la app coincide con la query, aparece. No se anade a pendientes. |
| El usuario empieza a escribir | Las apps pendientes se descartan permanentemente (`_pendingAppInfos.Clear()`). |
| El usuario borra todo el texto | Se muestran las apps pendientes que quedaban (si no se habia escrito nada antes). |
| La ventana se oculta y se reabre | Las apps pendientes persisten en memoria. |
| Se carga un icono | Si hay pendientes y el buscador esta vacio, se reconstruye la lista para reflejar el icono recien disponible. |

El tracking de apps nuevas solo se activa despues de que `ApplicationSearch` complete su scan inicial (`WhenReady()`), evitando que las apps del scan inicial se traten como "recien instaladas".

> **Verificar en:** `MainWindowViewModel.cs` -- `StartTrackingNewAppsAsync`, `OnNewAppInstalled`, `ShowPendingApps`, `OnAppCacheChanged`, `OnSearchTextChanged`.

---

## 10. Banner de actualizacion

Cuando hay una version nueva disponible, se muestra una franja clicable al pie de la ventana con el texto `"Yottacast {version} available -- click to download"`. El comando de clic es actualmente un placeholder sin implementacion.

> **Verificar en:** `MainWindowViewModel.cs` -- `CheckForUpdateAsync`, `UpdateBannerClick`. `MainWindow.axaml` -- seccion "Update banner".

---

## 11. Score visible como modo debug

En modo normal, cada item estandar muestra su etiqueta de categoria (ej. "App", "File", "Web"). Si el usuario mantiene pulsada la tecla Alt, la categoria se reemplaza por el score numerico (formato dos decimales). Esto permite depurar el ranking sin herramientas externas.

> **Verificar en:** `MainWindow.axaml` -- DataTemplate de `ResultItemViewModel`, condicion `IsAltPressed`. `MainWindowViewModel.cs` -- `IsAltPressed`. `MainWindow.axaml.cs` -- `OnKeyDown` (Alt), `OnKeyUp` (Alt).

---

## 12. Hint de busqueda

Cuando una fuente instant proporciona un hint (ej. la calculadora detecta un error corregible), se muestra como texto rojo debajo del campo de busqueda. Se limpia automaticamente en cada nueva busqueda o cuando el texto se vacia.

> **Verificar en:** `MainWindowViewModel.cs` -- `SearchHint`. `MainWindow.axaml` -- TextBlock con binding a `SearchHint`. `GlobalSearch.cs` -- `SearchInstant` (extraccion de hint via `ISearchHintProvider`).

---

## 13. Layout de la ventana

| Propiedad | Valor |
|---|---|
| Decoraciones del sistema | Ninguna (`SystemDecorations="None"`) |
| Fondo | Transparente con borde redondeado |
| Ancho | Definido por tema (`Theme.WindowWidth`) |
| Alto | Ajustado al contenido (`SizeToContent="Height"`) |
| Barra de tareas | No visible (`ShowInTaskbar="False"`) |
| Redimensionable | No |
| Altura maxima de la lista de resultados | 416 px con scroll vertical automatico, sin scroll horizontal |

> **Verificar en:** `MainWindow.axaml` -- atributos del `Window` y propiedades del `ListBox`.
