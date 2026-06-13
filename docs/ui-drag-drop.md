# Drag-and-drop de resultados

## Que debe hacer

Cualquier resultado de la lista principal puede arrastrarse fuera de Yottacast hacia otras aplicaciones:

- Apps y documentos se arrastran como ficheros (DataFormats.Files). Soltarlos en Finder copia el archivo.
- Resultados de calculadora, conversor, álgebra, fechas, emoji y diccionario se arrastran como texto plano (DataFormats.Text). Soltarlos en un editor pega el contenido.

## Comportamiento esperado

- El drag se inicia cuando se cumple una de dos condiciones: (a) el cursor se mueve más de `AppDefaults.DragStartThresholdPx` píxeles con el botón presionado durante al menos `AppDefaults.DragMinPressDurationMs` ms, o (b) el botón se mantiene presionado durante `AppDefaults.DragLongPressMs` ms sin soltar (long-press).
- Click corto (sin movimiento relevante o sin tiempo de presión suficiente) selecciona el item normalmente; nunca inicia drag.
- La ventana de Yottacast permanece visible durante todo el drag - no se oculta al iniciar ni al soltar.
- En resultados con celdas navegables (conversion, álgebra, fechas, emoji) el drag usa el contenido de la celda **actualmente seleccionada**, no la celda bajo el cursor.
- Si el payload no puede resolverse (fichero borrado, URI inválida) el drag se cancela silenciosamente - no hay excepción visible al usuario.
- Sólo se admite `DragDropEffects.Copy`; mover archivos no es una operación soportada.

> **Bug conocido** - `DragDataFactory.FileAsync` construye la URI del fichero con `new Uri(absolutePath)` tratando la ruta absoluta como si ya fuera una URI. Los caracteres `#` y `%` en el nombre del fichero rompen esa interpretacion: `#` arranca el fragmento de la URI (la ruta resultante pierde todo lo que va despues), y `%` se interpreta como inicio de un escape porcentual (`%20` se decodifica a espacio; una secuencia invalida como `%xx` lanza `UriFormatException`). En todos esos casos el lookup posterior (`TryGetFileFromPathAsync`/`TryGetFolderFromPathAsync`) no encuentra el item o resuelve uno distinto, y el `catch` cancela el drag sin avisar. Ficheros y apps con `#` o `%` en el nombre no se pueden arrastrar correctamente. La forma robusta seria construir la URI con `new Uri(absolutePath, UriKind.Absolute)` solo tras escapar la ruta, o usar el constructor de fichero apropiado.

## Plataformas

v1 sólo se valida en macOS. El código debería funcionar en Windows/Linux con los formatos estándar de Avalonia, pero no se garantiza hasta que se pruebe.

## Contrato

Cada `BaseResultItemViewModel` declara su intención de drag con `GetDragPayload: Func<DragPayload?>?`. Si es null, el item no es arrastrable.

`DragPayload` (en `Yottacast.Core/ViewModels/DragPayload.cs`) tiene dos variantes:
- `DragPayload.File(string AbsolutePath)` - para apps y documentos.
- `DragPayload.Text(string Value)` - para todo lo demás.

La vista (`Yottacast/Views/MainWindow.axaml.cs`) traduce el payload a un `IDataObject` usando `Yottacast/Services/DragDataFactory.cs`.

## Verificar en

- Contrato: `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs` (`GetDragPayload`), `Yottacast.Core/ViewModels/DragPayload.cs`.
- Disparo del drag: `Yottacast/Views/MainWindow.axaml.cs` - `OnResultsPointerPressed` (registra candidato y arranca `StartDragLongPressTimer`), `OnResultsPointerMovedForDrag` (detecta umbral de distancia/tiempo), ambos confluyen en `InitiateDragAsync`. Cancelacion: `OnResultsPointerReleasedForDrag`, `OnResultsPointerCaptureLostForDrag`, `CancelDragTimer`.
- Traducción payload→IDataObject: `Yottacast/Services/DragDataFactory.cs` (`BuildAsync`, `FileAsync`, `Text`).
- Tests por VM: `Yottacast.Core.Tests/ViewModels/`.
