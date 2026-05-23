# Drag-and-drop de resultados

## Que debe hacer

Cualquier resultado de la lista principal puede arrastrarse fuera de Yottacast hacia otras aplicaciones:

- Apps y documentos se arrastran como ficheros (DataFormats.Files). Soltarlos en Finder copia el archivo.
- Resultados de calculadora, conversor, álgebra, fechas, emoji y diccionario se arrastran como texto plano (DataFormats.Text). Soltarlos en un editor pega el contenido.

## Comportamiento esperado

- El drag se inicia cuando el cursor se mueve más de `AppDefaults.DragStartThresholdPx` píxeles con el botón izquierdo presionado sobre un item arrastrable.
- Click corto (sin movimiento) selecciona el item normalmente; nunca inicia drag.
- La ventana de Yottacast permanece visible durante todo el drag — no se oculta al iniciar ni al soltar.
- En resultados con celdas navegables (conversion, álgebra, fechas, emoji) el drag usa el contenido de la celda **actualmente seleccionada**, no la celda bajo el cursor.
- Si el payload no puede resolverse (fichero borrado, URI inválida) el drag se cancela silenciosamente — no hay excepción visible al usuario.
- Sólo se admite `DragDropEffects.Copy`; mover archivos no es una operación soportada.

## Plataformas

v1 sólo se valida en macOS. El código debería funcionar en Windows/Linux con los formatos estándar de Avalonia, pero no se garantiza hasta que se pruebe.

## Contrato

Cada `BaseResultItemViewModel` declara su intención de drag con `GetDragPayload: Func<DragPayload?>?`. Si es null, el item no es arrastrable.

`DragPayload` (en `Yottacast.Core/ViewModels/DragPayload.cs`) tiene dos variantes:
- `DragPayload.File(string AbsolutePath)` — para apps y documentos.
- `DragPayload.Text(string Value)` — para todo lo demás.

La vista (`Yottacast/Views/MainWindow.axaml.cs`) traduce el payload a un `IDataObject` usando `Yottacast/Services/DragDataFactory.cs`.

## Verificar en

- Contrato: `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs`, `Yottacast.Core/ViewModels/DragPayload.cs`.
- Disparo del drag: `Yottacast/Views/MainWindow.axaml.cs` métodos `OnResultsPointerPressedForDrag` / `OnResultsPointerMovedForDrag`.
- Traducción payload→IDataObject: `Yottacast/Services/DragDataFactory.cs`.
- Tests por VM: `Yottacast.Core.Tests/ViewModels/`.
