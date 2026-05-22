# Drag-and-drop de resultados — Diseño

**Fecha**: 2026-05-22
**Alcance**: macOS (v1)

## Objetivo

Permitir que cualquier resultado de la lista principal pueda arrastrarse fuera de Yottacast hacia otras aplicaciones. El caso de uso pivote es arrastrar un archivo de la búsqueda de documentos al Finder, pero la solución abarca todos los tipos de resultado: archivos, apps, texto del calculadora/conversor, álgebra, fechas, emojis y diccionario.

## Contrato del ViewModel

Para no acoplar `Yottacast.Core` a Avalonia, el ViewModel declara una **intención** (qué quiere arrastrar), no un `IDataObject` final. La vista traduce esa intención al objeto que entiende el OS.

Nuevo record sellado en `Yottacast.Core/ViewModels/DragPayload.cs`:

```csharp
public abstract record DragPayload {
    public sealed record File(string AbsolutePath) : DragPayload;
    public sealed record Text(string Value) : DragPayload;
}
```

Y un único punto de extensión opcional en `BaseResultItemViewModel`:

```csharp
/// <summary>
/// Si no es null, el item es arrastrable. La vista lo invoca al detectar inicio de drag
/// y traduce el DragPayload al IDataObject que recibe el sistema operativo. Devolver null
/// o lanzar excepción cancela el drag silenciosamente (loguear y no propagar).
/// </summary>
public Func<DragPayload?>? GetDragPayload { get; init; }
```

**Por qué un delegate y no un método abstracto**: encaja con el resto de extensiones de `BaseResultItemViewModel` (acciones, callbacks de navegación) y permite que cada source decida en construcción si el item es arrastrable o no, sin tocar la jerarquía de clases.

**Por qué síncrono**: las sources sólo conocen rutas y strings; resolver `IStorageItem` (asíncrono) es tarea de la vista. Los grids con celdas (emoji, conversion, álgebra, fechas) leen su estado actual en el delegate, que es síncrono.

Si `GetDragPayload` es `null`, el item no es arrastrable y no cambia nada en el comportamiento previo.

## Mapping por tipo de resultado

| Source / ViewModel                    | DataFormat        | Contenido                                                |
|---------------------------------------|-------------------|----------------------------------------------------------|
| `AppsSearch` → `ResultItemViewModel`  | `Files`           | Bundle `.app` (macOS lo trata como un fichero arrastrable) |
| `UserDocumentSearch` → `ResultItemViewModel` | `Files`    | Ruta del documento                                       |
| `CalculatorResultItemViewModel`       | `Text`            | Valor formateado (mismo string que copia Enter)          |
| `ConversionResultItemViewModel`       | `Text`            | Texto de la **celda seleccionada**                       |
| `AlgebraResultItemViewModel`          | `Text`            | Celda seleccionada                                       |
| `DateSearchResultViewModel`           | `Text`            | Celda seleccionada                                       |
| `EmojiGridResultViewModel`            | `Text`            | Emoji actualmente seleccionado en el grid                |
| `DictionaryResultViewModel`           | `Text`            | La palabra (no la definición)                            |

### Resultados con celdas navegables

Conversion, álgebra, fechas y emoji ya tienen celdas internas que se navegan con flechas. Para v1, **el drag usa siempre la celda actualmente seleccionada**, no la celda bajo el cursor del ratón. Esto:

- Mantiene la simetría con Enter (que también opera sobre la celda seleccionada).
- Evita hit-testing por celda en la vista.
- Es predecible: el usuario navega con flechas o click, ve la selección destacada, y arrastra.

Si más adelante se quiere que el ratón pueda seleccionar una celda concreta y arrastrarla en un mismo gesto, se añade después.

## Disparo del drag en la vista

Toda la lógica vive en `Yottacast/Views/MainWindow.axaml.cs`, sobre el `ResultsList` (`ListBox`). No hay código OS-específico — Avalonia traduce `DragDrop.DoDragDrop` al backend nativo de macOS automáticamente.

### Estado por gesto

Una variable de instancia simple en el code-behind del `MainWindow`:

```csharp
private (Point Origin, BaseResultItemViewModel Vm)? _dragCandidate;
```

### Eventos

1. **`PointerPressedEvent` (Tunnel)** sobre `ResultsList`:
   - Si el botón es izquierdo y el `DataContext` del control pulsado (resuelto subiendo el árbol visual hasta encontrar un `ListBoxItem`) es un `BaseResultItemViewModel` con `GetDragDataAsync != null`, guardar `(posición, vm)` en `_dragCandidate`.
   - No marcar el evento como handled — el `ListBox` sigue procesando la selección normal.

2. **`PointerMovedEvent` (Tunnel)** sobre `ResultsList`:
   - Si `_dragCandidate` es null o el botón izquierdo ya no está pulsado, ignorar.
   - Si la distancia desde `Origin` supera el umbral de drag (5 px en cada eje, valor a definir en `AppDefaults.DragStartThresholdPx`), iniciar el drag:
     - Limpiar `_dragCandidate` antes de await (un drag concurrente no debe re-disparar).
     - `var data = await vm.GetDragDataAsync();` — si null, abortar.
     - `await DragDrop.DoDragDrop(args, data, DragDropEffects.Copy);`
     - Excepciones loguean en warning y no propagan (el OS puede cancelar el drag por motivos varios).

3. **`PointerReleasedEvent` / `PointerCaptureLostEvent`**: limpiar `_dragCandidate`.

### Por qué Tunnel y no Bubble

Los handlers de los `ListBoxItem` y de los controles internos pueden marcar `Handled=true` antes de que el evento burbujee. Tunnel garantiza que el `MainWindow` ve el `PointerPressed` aunque el item lo consuma para gestionar selección.

### Comportamiento de la ventana

La ventana de Yottacast permanece visible durante el drag (decisión de UX confirmada). No se oculta al iniciar ni al soltar; el usuario controla cuándo cerrar con Esc o con la hotkey global. Esto es coherente con el resto de acciones que mantienen la ventana abierta.

### Coexistencia con click y Enter

- **Click corto** (sin movimiento más allá del umbral): el `ListBox` selecciona el item normalmente. No se inicia drag.
- **Click + arrastre**: a partir del umbral, se inicia el drag. La selección ya se aplicó al `PointerPressed` inicial.
- **Enter**: ejecuta la acción por defecto del item (lanzar app, copiar, abrir). No interactúa con drag.

## Construcción del `IDataObject` en la vista

Helper en `Yottacast/Services/DragDataFactory.cs` (vive en `Yottacast/`, no en `Core`, porque depende de Avalonia):

```csharp
public static class DragDataFactory {
    public static async Task<IDataObject?> BuildAsync(Visual visual, DragPayload payload) {
        return payload switch {
            DragPayload.Text t => Text(t.Value),
            DragPayload.File f => await FileAsync(visual, f.AbsolutePath),
            _                  => null
        };
    }

    private static IDataObject Text(string text) {
        var data = new DataObject();
        data.Set(DataFormats.Text, text);
        return data;
    }

    private static async Task<IDataObject?> FileAsync(Visual visual, string absolutePath) {
        var topLevel = TopLevel.GetTopLevel(visual);
        var storage = topLevel?.StorageProvider;
        if (storage is null) return null;
        var file = await storage.TryGetFileFromPathAsync(new Uri(absolutePath));
        if (file is null) return null;
        var data = new DataObject();
        data.Set(DataFormats.Files, new[] { (IStorageItem)file });
        return data;
    }
}
```

Excepción razonada a la regla de DI: son helpers puros sin estado ni dependencias inyectables, equivalentes a parsers o conversores. La vista los usa directamente desde el handler de drag.

## Ficheros tocados

| Fichero                                                       | Cambio                                                |
|---------------------------------------------------------------|-------------------------------------------------------|
| `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs`        | Añadir `GetDragPayload`                               |
| `Yottacast.Core/ViewModels/DragPayload.cs`                    | Nuevo: record sellado `File` / `Text`                 |
| `Yottacast.Core/AppDefaults.cs`                               | `DragStartThresholdPx = 5`                            |
| `Yottacast/Views/MainWindow.axaml.cs`                         | Handlers Pointer + invocación de `DragDrop.DoDragDrop` |
| `Yottacast/Services/DragDataFactory.cs`                       | Helper para construir `IDataObject` desde `DragPayload` |
| `Yottacast.Core/Search/Apps/AppsSearch.cs`                    | Setear `GetDragPayload = () => new DragPayload.File(appPath)` |
| `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs`   | Setear `GetDragPayload` para cada doc                 |
| `Yottacast.Core/ViewModels/CalculatorResultItemViewModel.cs`  | `GetDragPayload = () => new DragPayload.Text(value)`  |
| `Yottacast.Core/ViewModels/ConversionResultItemViewModel.cs`  | Texto de la celda seleccionada                        |
| `Yottacast.Core/ViewModels/AlgebraResultItemViewModel.cs`     | Texto de la celda seleccionada                        |
| `Yottacast.Core/ViewModels/DateSearchResultViewModel.cs`      | Texto de la celda seleccionada                        |
| `Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs`       | Emoji seleccionado                                    |
| `Yottacast.Core/ViewModels/DictionaryResultViewModel.cs`      | La palabra                                            |
| `docs/ui-drag-drop.md` (nuevo)                                | Contrato y comportamiento esperado                    |
| `CLAUDE.md`                                                   | Apuntar a `docs/ui-drag-drop.md`                      |

`MacAppHandler` y `WindowsAppHandler` **no se tocan**: el drag funciona con APIs de Avalonia puras.

## Invariantes verificables

1. **Item con `GetDragPayload == null` no es arrastrable** — un click + movimiento sobre él se comporta exactamente como antes (selección o sin efecto).
2. **Umbral respetado** — un click corto (< 5 px de movimiento) nunca inicia drag.
3. **Ventana visible durante el drag** — `MainWindow.IsVisible == true` mientras el drag está activo.
4. **Drag de archivo en Finder** — soltar un resultado de `UserDocuments` o `Apps` en una carpeta de Finder produce una copia del archivo, no un alias ni un movimiento (DragDropEffects.Copy).
5. **Drag de texto en editor** — soltar un resultado del calculadora en TextEdit/Notes pega el valor formateado.
6. **Cancelación silenciosa** — si `TryGetFileFromPathAsync` devuelve null (fichero borrado entre búsqueda y drag), no se inicia drag y no hay excepción visible al usuario.

> **Verificar en**: `MainWindow.axaml.cs` (handlers), `BaseResultItemViewModel` (contrato), tests manuales con Finder/TextEdit.

## Tests

Tests unitarios mínimos en `Yottacast.Core.Tests`:

- `DragPayloadTests`: las sources con archivos generan `DragPayload.File` con la ruta correcta.
- `EmojiGridResultViewModelTests`: el `GetDragPayload` devuelve el emoji correspondiente a la celda seleccionada.
- `ConversionResultItemViewModelTests` (similar para Algebra/Date): el payload sigue el cambio de celda seleccionada.

Tests de la vista (drag en sí) no son automatizables sin entorno gráfico. Se documentan los pasos manuales de verificación en `docs/ui-drag-drop.md`.

## YAGNI explícito

Cosas conscientemente fuera del alcance de v1:

- **Windows/Linux**: el código debería funcionar tal cual, pero no se valida hasta que se necesite.
- **Drag de celda específica bajo el cursor**: por ahora siempre la seleccionada.
- **Drag de múltiples items a la vez**: ListBox no está en SelectionMode.Multiple; se añade si surge la necesidad.
- **Iconos custom durante el drag**: macOS pinta el icono del fichero o un placeholder de texto automáticamente.
- **Hit-test por celda en grids**: ver punto anterior sobre celda seleccionada.
- **Modo move**: sólo `DragDropEffects.Copy`. Mover archivos sería arriesgado y poco útil aquí.

## Riesgos conocidos

1. **Eventos pointer de los grids internos** (emoji, conversion) pueden marcar handled antes del Tunnel. Mitigado usando `RoutingStrategies.Tunnel` en el `AddHandler`, que se dispara antes de que los hijos puedan consumir el evento.
2. **Race entre indexación y drag**: un fichero indexado pero borrado luego. Resuelto por la cancelación silenciosa (punto 6 de invariantes).
3. **Visual transparente y frameless**: el cursor de drag puede pintarse extraño si el OS no detecta bien la ventana origen. Se valida manualmente en macOS al implementar.
