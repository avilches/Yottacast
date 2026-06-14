# TODO

Features e ideas a desarrollar mas adelante. Organizado por area. Cada entrada indica si es **bug** o **feature** y su **tamano** estimado (pequeno / mediano / grande).

Cuando se pregunte "que queda por hacer", esto se revisa **despues** de `docs/PENDING.md`.

---

## Clipboard

### C1. ~~El clipboard aparece en modo emoji~~ — HECHO (2026-06-14)
Guard `if (query.StartsWith(':')) return [];` en `ClipboardHistorySearch.Search`.

### C2. ~~Texto largo se pisa con la categoria "Clipboard"~~ — HECHO (2026-06-14)
Truncacion reducida a 60 chars + preview lateral siempre visible al seleccionar (C2+C4 combinados).

### C3. Iconos del clipboard ausentes o incorrectos (salen carpetas) — Bug + Feature, grande
Los items de clipboard no asignan icono, asi que cae al icono por defecto (a veces una carpeta). Lo deseable: mostrar el icono de la **app de origen** desde la que se copio el contenido.
- Hoy `ClipboardHistoryEntry` solo guarda `Text`, `CopiedAt`, `UsageCount`, `LastUsedAt`: no captura la app de origen, y los monitores (`MacClipboardMonitor`, `WindowsClipboardMonitor`) solo leen texto plano.
- Parte pequena: asignar un icono fijo de "portapapeles" mientras no haya app de origen (elimina el bug de la carpeta).
- Parte grande: capturar la app frontmost en el momento de la copia (en macOS no viene en el pasteboard; hay que consultar la app activa al detectar el cambio), persistir su identificador en el entry, y resolver su icono via la cache de iconos de apps.
- Verificar en: `ClipboardHistoryEntry`, `MacClipboardMonitor`, `WindowsClipboardMonitor`, `ClipboardHistorySearch.BuildResult`, cache de iconos de apps.


### C5. Borrar descoloca la vista — Bug, mediano
Al borrar un item con Delete, la vista se reordena y el cursor "salta". Lo correcto: el elemento desaparece y el cursor se mantiene en su posicion (pasa a seleccionar el siguiente item en el mismo indice).
- `Remove` dispara `EntriesChanged` -> `RefreshSearch` -> `RefreshResults`, que reconstruye toda la lista y reasigna la seleccion buscando el item previo por referencia/titulo; como el item ya no existe, la heuristica de reseleccion no preserva el indice.
- Fix: tras borrar, seleccionar el item que queda en el mismo indice (o el ultimo si era el final), en vez de la heuristica actual por titulo/subtitulo.
- Verificar en: `ClipboardHistoryStore.Remove`, `MainWindowViewModel.OnClipboardHistoryResultChanged`, `MainWindowViewModel.RefreshResults`.

### C6. Revisar el decay del scoring vs orden por fecha — Feature (decision), pequeno
Con query vacia el historial se ordena por recencia pura (`score = 1000 - indice`); el decay (`usageBonus` con half-life de 30 dias) solo influye cuando hay query. Hay que decidir si el decay aporta algo o si conviene ordenar siempre por fecha de insercion/uso.
- Analizar si el `usageBonus` cambia el orden de forma util en busquedas reales o si domina siempre el `matchScore`. Si no aporta, simplificar a orden por fecha.
- Verificar en: `ClipboardHistorySearch.ComputeScore`, `AppDefaults` (`ClipboardHistoryHalfLifeDays`, `ClipboardHistoryMaxBonus`, scores de match).

### C7. Etiqueta "Text from clipboard, Nh ago" al mezclar con resultados normales — Feature, pequeno
Cuando un item de clipboard aparece mezclado con resultados de otras fuentes, deberia identificarse claramente, p.ej. `Text from clipboard, 19h ago`.
- Hoy el item solo lleva `Subtitle` con tiempo relativo ("3h ago") y `Category = "Clipboard"`; no hay un texto unificado tipo "Text from clipboard, Nh ago". (El `InfoTag = "from clipboard"` solo lo pone la otra fuente `ClipboardSearch` del empty state, no el historial).
- Definir el formato y donde mostrarlo (subtitle o InfoTag) cuando el modo es mixto vs modo clipboard puro.
- Verificar en: `ClipboardHistorySearch.BuildResult`, `MainWindow.axaml` (subtitle / InfoTag).

### C8. Guardar otros tipos de contenido (ficheros, imagenes/graficos) — Feature, grande
Permitir que el historial de clipboard capture y reproduzca no solo texto sino tambien ficheros e imagenes.
- Hoy todo es texto plano: se persiste en un unico JSON (`clipboard-history.json`, limite 200 entradas / 30 dias). Los monitores solo leen `NSStringPboardType` / `CF_UNICODETEXT`.
- Decisiones de diseno a resolver: el texto seguiria en el JSON; las imagenes/ficheros no caben inline en el JSON (tamano), habria que guardarlos como blobs en disco (p.ej. carpeta de cache con un id) y referenciarlos desde el entry. Definir formato del entry polimorfico (texto / fichero / imagen), limites de tamano, y como se hace el "paste" de cada tipo.
- Verificar en: `ClipboardHistoryEntry`, `ClipboardHistoryStore` (persistencia JSON), `MacClipboardMonitor`/`WindowsClipboardMonitor`, `AppPaths` (nueva ruta de blobs).

---

## Emoji / navegacion

### ~~E1. Ctrl+Abajo no funciona en modo emoji~~ — HECHO (2026-06-14)
Guard `!e.Handled` en `case Key.Down` y `case Key.Up` para respetar lo que el tunnel handler ya procesó.

---

## Footer / hotkeys

### F1. Los hotkeys de abajo no caben — Bug, mediano
Cuando hay muchas acciones, los hints del footer no caben y se desbordan (no hay truncado ni wrap).
- El footer es un `ItemsControl` + `StackPanel` horizontal (`FooterHints`), sin limite de ancho ni ellipsis. (Esto se relaciona con F2).
- Opciones: priorizar/limitar el numero de hints visibles, permitir wrap, o reducir tamano. Mejor resolver junto con F2.
- Verificar en: `MainWindow.axaml` (footer, `ItemsControl FooterHints`), `MainWindowViewModel.FooterHints`.

### F2. Hotkeys del footer mas pequenos y clicables — Feature, mediano
Los hints de abajo deberian tener un estilo mas compacto (fuente mas pequena) y ser clicables (ejecutar la accion al pulsarlos), no solo decorativos.
- Hoy son `TextBlock` puros dentro de un `ItemsControl`; cada hint se construye como `"{hotkey}  {label}"` a partir de las acciones con `ShowInFooter`. No hay interaccion.
- Convertir cada hint en un control clicable (Button con estilo plano) que dispare la accion asociada, y ajustar `Theme.Footer.Size` o un token nuevo para el tamano. OJO: cambio de estilo/tamano de fuente -> requiere confirmar token de tema con el usuario antes de tocarlo (regla de CLAUDE.md).
- Verificar en: `MainWindow.axaml` (template del footer), `MainWindowViewModel.FooterHints` (necesita exponer la accion, no solo el string), tokens `Theme.Footer.*`.

---

## Drag & drop

### D1. Revisar el arrastre (que hace y si funciona bien) — Bug/revision, mediano
Hace falta auditar el comportamiento del drag-and-drop: no esta claro que entrega ni si funciona correctamente en todos los casos.
- Estado actual: arrastran ficheros/apps (`DragPayload.File` con ruta absoluta) y varios tipos de texto (calc, conversion, algebra, fecha, emoji, diccionario via `DragPayload.Text`). Disparo por umbral de distancia + tiempo o long-press en `MainWindow.axaml.cs`; traduccion a `IDataObject` en `DragDataFactory`.
- Puntos dudosos a revisar: el drag de ficheros solo se ha validado en macOS (`UriBuilder` con `Host=""` puede comportarse distinto en Windows con drive letters / UNC); `DragDataFactory.FileAsync` traga excepciones sin loguear (catch vacio -> usar el logger); no hay feedback visual durante el arrastre. Verificar caso por caso y documentar el contrato real.
- Verificar en: `MainWindow.axaml.cs` (`OnResultsPointerPressed`, `OnResultsPointerMovedForDrag`, `InitiateDragAsync`), `DragDataFactory`, `docs/ui-drag-drop.md`.

---

## Calculadora / conversores

### K1. Conversores nuevos: bases numericas, colores, hashes — Feature, grande
Ampliar la calculadora con mas conversiones que no encajan en math.js (que solo hace aritmetica y unidades fisicas/divisas):
- **Bases numericas**: hex / decimal / binario / octal (`0xFF`, `0b1010`, `255 to hex`...).
- **Colores**: hex <-> rgb <-> hsl.
- **Hashes**: md5, sha1, sha256 de un texto.
- **Otras ideas**: codificacion base64 / url-encode, timestamp unix <-> fecha, conversion de tamanos de datos (ya hay unidades de datos via math.js, revisar solape).
- Arquitectura: math.js (Jint) NO soporta nada de esto; lo limpio es una o varias **instant sources independientes** que detecten el patron de la query por regex y devuelvan `ConversionResultItemViewModel` (celdas navegables, p.ej. mostrar dec/hex/oct/bin a la vez) o `CalculatorResultItemViewModel`. Registrar en `GlobalSearch` con score que no compita con un match exacto de math.js.
- Decidir si se hace una source generica "conversores de programador" o una por dominio. Empezar por bases numericas (la mas pedida) y dimensionar el resto.
- Verificar en: `GlobalSearch` (registro de sources), patron de `CalculatorSearch` / `ConversionResultItemViewModel` como referencia, `docs/search-calculator.md`.
