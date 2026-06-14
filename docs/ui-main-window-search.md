# Ventana principal -- busqueda y resultados

Este documento describe el comportamiento de la fase de busqueda dentro de la ventana principal de Yottacast: como se ejecutan las fases, como se ordenan y auto-seleccionan los resultados, como se integra la busqueda web en la lista, y los elementos de UI asociados (footer hints, score debug, hint de busqueda). Es la continuacion de `docs/ui-main-window.md`, que cubre el ciclo de vida, layout, posicionamiento y temporizadores de la ventana.

Se organiza por comportamientos y contratos verificables, no por archivos de codigo fuente.

---

## 1. Busqueda: fases y tiempos

La busqueda se divide en dos fases con distinto coste y latencia.

### Fase instant (sin retardo)

Cuando el usuario escribe, las fuentes en memoria (apps cacheadas, emojis, calculadora, fechas, web search) se consultan de forma sincrona. Los resultados aparecen inmediatamente.

### Fase diferida (con debounce)

Tras la fase instant, se espera un intervalo de debounce sin nuevas pulsaciones antes de consultar las fuentes de disco (busqueda de archivos via Spotlight/Windows Search). Mientras estas fuentes trabajan, se muestra un spinner de actividad (`IsVisible="{Binding IsSearching}"`) a la derecha del campo de busqueda. No existe ningun badge ESC en la UI.

### Modo emoji (prefijo `:`)

Cuando la query empieza por `:`, solo se ejecuta la fase instant. No hay debounce ni fase diferida.

| Invariante | Detalle |
|---|---|
| El usuario ve resultados en memoria sin retardo perceptible | La fase instant se ejecuta de forma sincrona antes de cualquier espera. |
| Cada nueva pulsacion cancela la busqueda anterior | Se crea un nuevo `CancellationTokenSource` por query. |
| El spinner solo es visible durante la fase diferida | `IsSearching` se activa justo antes de iterar las fuentes diferidas y se desactiva en el `finally`. |
| En modo emoji no se accede a disco | `SearchAsync` retorna inmediatamente tras la fase instant si la query empieza por `:`. |
| Limite general por fuente | Cada fuente recorta sus resultados a `AppDefaults.SearchSourceLimit` salvo que tenga su propio limite (p. ej. apps usan `AppDefaults.AppSearchLimit`, web y URL no recortan con `-1`). Los items con `BypassLimit = true` (WebSearch, Dictionary) no se descartan por este limite. |

> **Verificar en:** `MainWindowViewModel.cs` -- `SearchAsync`, `OnSearchTextChanged`. `AppDefaults.cs` -- `SearchDebouncedMs`, `SearchSourceLimit`, `AppSearchLimit`.

---

## 2. Resultados: ordenacion y auto-seleccion

Los resultados de ambas fases se combinan (merge) ordenados por score descendente.

### Auto-seleccion de calculadora/conversor

Si entre los resultados hay un `ConversionResultItemViewModel`, o un `ResultItemViewModel` cuya `Category` es `"Calculator"`, y el usuario no ha navegado con las flechas, ese resultado se selecciona automaticamente. Esto permite ver el resultado de la calculadora/conversor sin navegar. La deteccion es por tipo del ViewModel de conversion y por categoria del item, no por un tipo `CalculatorResultItemViewModel` concreto.

### Preservacion de seleccion tras navegacion manual

Si el usuario ha navegado manualmente (flechas arriba/abajo), el sistema intenta preservar el item que tenia seleccionado. Si ese item ya no esta en los resultados, se selecciona el primero.

### Reset de navegacion por query

Cada nueva query reinicia el flag de navegacion manual (`_userNavigated = false`), restaurando el comportamiento de auto-seleccion.

| Invariante | Detalle |
|---|---|
| `ShowNoResults` solo aparece si la fase diferida completo sin cancelacion y hay 0 resultados | Si se cancelo (ESC o nueva query), los resultados parciales permanecen visibles. |
| `ShowNoResults` se limpia en cada `RefreshResults` | Solo `SearchAsync` puede activarlo a `true`. |

> **Verificar en:** `Yottacast/ViewModels/MainWindowViewModel.cs` -- `RefreshResults`, `SearchAsync`, `NotifyUserNavigated`.

---

## 3. Busqueda web en la lista

Yottacast soporta multiples motores de busqueda web configurables. Cada motor puede funcionar en dos modos, con distinto score:

| Modo | Comportamiento | Score |
|---|---|---|
| `ShowAlways` | El motor aparece siempre que haya texto de busqueda (salvo si hay un motor con prefijo activo). | `AppDefaults.WebSearchShowAlwaysScore` |
| `PrefixOnly` | El motor solo aparece cuando la query comienza con su prefijo (ej. `yt video`). | `AppDefaults.WebSearchPrefixScore` |

Cuando un motor de tipo `PrefixOnly` coincide, los motores `ShowAlways` se ocultan para no ensuciar los resultados.

Los motores web no aparecen en modo emoji (queries que empiezan con `:`). Los resultados web llevan `BypassLimit = true`, por lo que no se descartan por el limite por fuente.

> **Verificar en:** `Yottacast.Core/Search/WebSearch/WebSearchSource.cs` -- `Search`. `AppDefaults.cs` -- `WebSearchShowAlwaysScore`, `WebSearchPrefixScore`.

---

## 4. Footer: Settings y hints dinamicos

El footer de la ventana principal es siempre visible, independientemente de si hay resultados o no.

### Lado izquierdo: boton Settings y hint de ciclo

El lado izquierdo del footer tiene dos elementos:

- **Siempre visible**: El atajo de teclado para abrir la ventana de preferencias.
- **Condicional**: El hint de ciclo de modos aparece solo cuando hay modos disponibles (`ShowModePill == true`, es decir, cuando `AvailableModes.Count > 0`). Permite cambiar rapidamente entre modos de busqueda (All / Files / Clipboard).

### Lado derecho: hints dinamicos por tipo de resultado

Los hints del lado derecho son contextuales: solo se muestran cuando hay resultados y reflejan las acciones disponibles para el tipo de resultado seleccionado. Cuando no hay resultados, el area de hints esta vacia.

**Contrato de los hints**: la lista `FooterHints` se deriva de las `Actions` del resultado seleccionado, no de una tabla fija. Para cada accion con `ShowInFooter == true` y `Hotkey != null` se genera un hint con el atajo formateado por plataforma y la etiqueta de la accion (`LabelProvider?.Invoke() ?? Label`). Si alguna accion tiene `ShowInMenu == true`, se anade siempre al final el hint `Tab  Options` que abre el overlay de opciones.

Por tanto, los textos exactos los aportan las propias fuentes en las `Actions` de cada item. Ejemplos de etiquetas reales por fuente:

- **Apps**: accion por defecto `"Open"` o `"Bring to Front"` (segun si la app esta en ejecucion), mas `"Copy path"`.
- **Archivos / documentos**: `"Open"`, `"Copy path"`, `"Preview"`, `"Edit"`.
- **Calculadora / Conversor / Fecha**: el Enter es `"Close and paste"`, mas `"Copy result"` / `"Copy value"` / `"Copy date"`.
- **Emoji**: Enter `"Close and paste"`, mas `"Copy"` y `"Favorite"`.
- **Diccionario**: `"Open in Wiktionary"` y `"Copy definition"`.
- **Busqueda web**: `"Open search in {browser}"`.

`FooterHints` es una propiedad observable del ViewModel que se actualiza en cada cambio de resultado seleccionado.

### Sin contador de resultados

El footer no muestra el numero de resultados. La cantidad de items es visible directamente por la longitud de la lista.

> **Verificar en:** `MainWindow.axaml` (seccion footer), `Yottacast/ViewModels/MainWindowViewModel.cs` (`FooterHints`). Las etiquetas de cada accion viven en las fuentes: `Yottacast.Core/Search/Application/ApplicationSearch.cs`, `UserDocuments/UserDocumentSearch.cs`, `Calculator/CalculatorSearch.cs`, `Date/DateSearch.cs`, `Emoji/EmojiSearch.cs`, `Dictionary/DictionarySource.cs`, `WebSearch/WebSearchSource.cs`. Contrato de `ResultAction` en `docs/result-viewmodels.md`.

---

## 5. Score visible como modo debug

En modo normal, cada item estandar muestra su etiqueta de categoria (ej. "App", "File", "Web"). Si el usuario mantiene pulsada la tecla Alt, la categoria se reemplaza por el score numerico (formato dos decimales). Esto permite depurar el ranking sin herramientas externas.

> **Verificar en:** `MainWindow.axaml` -- DataTemplate de `ResultItemViewModel`, condicion `IsAltPressed`. `Yottacast/ViewModels/MainWindowViewModel.cs` -- `IsAltPressed`. `MainWindow.axaml.cs` -- `OnKeyDown` (Alt), `OnKeyUp` (Alt).

---

## 6. Hint de busqueda

El area de hint siempre reserva espacio fijo debajo del campo de busqueda (no hay salto de layout al aparecer o desaparecer). El texto aparece con fade-in.

Hay dos estilos visuales:

| Estilo | Color | Cuando se usa |
|---|---|---|
| Error | Rojo (`Theme.Search.Hint.Error`) | Unidades incompatibles en la calculadora (`IncompatibleUnitsConvert`, `IncompatibleUnitsOp`) |
| Info | Gris (`Theme.Search.Hint.Info`) | Hints de ambiguedad de unidades ("Maybe you meant...") y mensajes de copia ("Copied ...") |

El hint se limpia automaticamente en cada nueva busqueda o cuando el texto se vacia.

> **Verificar en:** `Yottacast/ViewModels/MainWindowViewModel.cs` -- `SetSearchHint`, `SearchHintIsError`, `SearchHintIsInfo`. `MainWindow.axaml` -- `Grid` con `MinHeight` y dos TextBlocks. `GlobalSearch.cs` -- `SearchInstant` devuelve `SearchHintKind`. `CalculatorSearch.cs` -- `LastHintKind`. `ThemeService.cs` -- `Theme.Search.Hint.Error`, `Theme.Search.Hint.Info`.

---

## Documentos relacionados

- `docs/ui-main-window.md` -- ciclo de vida de la ventana, layout, posicionamiento/arrastre, decay timer, ocultacion del cursor.
- `docs/ui-hotkeys.md` -- atajos de teclado y navegacion (Escape, Enter, flechas, copy, Tab).
- `docs/search-sources.md` -- interfaces de fuentes, ciclo de vida y mecanismo de merge.
- `docs/search-scoring.md` -- algoritmo de puntuacion y ordenacion.
- `docs/result-viewmodels.md` -- contrato de `ResultAction` y jerarquia de ViewModels de resultado.
