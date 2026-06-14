# Spec: Clipboard + Emoji fixes (C1, E1, C2+C4)

Fecha: 2026-06-14

## Alcance

Tres bugs/features del historial de clipboard y del modo emoji:

- **C1**: Clipboard aparece en modo emoji (`:`)
- **E1**: Ctrl+Down no navega el grid de emojis
- **C2+C4**: Texto largo de clipboard se pisa con la columna Category + preview lateral siempre visible

---

## C1 - Clipboard en modo emoji

**Comportamiento esperado**: cuando la query empieza por `:`, solo el emoji grid debe aparecer. El historial de clipboard no debe mostrarse.

**Causa**: `ClipboardHistorySearch.Search()` no filtra queries de emoji. `WebSearchSource` ya tiene esta guarda (`if (query.StartsWith(':')) return [];`).

**Fix**: añadir la misma guarda al inicio de `ClipboardHistorySearch.Search()`:

```csharp
if (query.StartsWith(':')) return [];
```

**Test**: `Search_EmojiQuery_ReturnsEmpty` en `ClipboardHistorySearchTests.cs`.

**Ficheros**: `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs`, `Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs`.

---

## E1 - Ctrl+Down no navega el grid de emojis

**Comportamiento esperado**: Ctrl+Down en modo emoji debe mover la seleccion dentro del grid de emojis, igual que Down sin Ctrl.

**Causa**: en `MainWindow.axaml.cs`, `OnKeyDown` (bubble), el `case Key.Down` llama `NavigateHistoryForward()` sin comprobar si el tunnel handler ya proceso la tecla. El tunnel handler (`OnTunnelKeyDown`) llama `onDown()` y marca `e.Handled = true` cuando el emoji grid esta activo. El bubble handler ignora ese flag y navega el historial encima.

**Fix**: en `case Key.Down`, añadir `if (!e.Handled)` antes de `NavigateHistoryForward()`:

```csharp
case Key.Down:
    if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
        if (!e.Handled) {
            vm.NavigateHistoryForward();
            SearchBox.CaretIndex = int.MaxValue;
        }
    } else {
        SelectNext(vm, +1);
    }
    e.Handled = true;
    break;
```

**Invariante**: el guard `!e.Handled` es general: protege cualquier fuente que maneje Down en el tunnel, no solo emojis.

**Test**: no hay tests de UI para este comportamiento.

**Ficheros**: `Yottacast/Views/MainWindow.axaml.cs`.

---

## C2+C4 - Texto largo + preview lateral de clipboard

**Comportamiento esperado**:

- En la lista: el titulo de un item de clipboard se trunca a 60 caracteres (en lugar de 120) para no solaparse con la columna Category. El texto truncado usa `·` para newlines y `…` al final si se corta.
- En el panel lateral: al seleccionar cualquier item de clipboard, se abre automaticamente el panel de preview (derecha) mostrando el texto completo sin cortar. El panel se abre sin necesitar Cmd+P. Cmd+P lo cierra. Al navegar a un item que no es clipboard, el panel se cierra.

### Nuevos componentes

**`ClipboardResultItemViewModel`** (nuevo, `Yottacast.Core/ViewModels/`):

```csharp
public class ClipboardResultItemViewModel : ResultItemViewModel {
    public required string FullText { get; init; }
}
```

Hereda todo de `ResultItemViewModel` (Title, Subtitle, Category, Actions, Score, etc.). Anade `FullText` con el texto completo sin truncar ni procesar. `ClipboardHistorySearch.BuildResult()` devuelve este tipo.

**`EditorPanelViewModel.LoadTextContent(string text)`** (metodo nuevo):

Carga texto plano directamente, sin leer un fichero. `FilePath = ""`, `Mode = EditorMode.Preview`, `Content = text`. `FileName` no se usa en modo Preview (el header del editor esta oculto en ese modo). `CanOpen("")` ya devuelve false por extension vacia, asi que Cmd+E es un no-op natural.

### Cambios en `ClipboardHistorySearch.BuildResult()`

- Truncar titulo a 60 chars (en lugar de 120).
- Devolver `ClipboardResultItemViewModel { FullText = capturedText, ... }` en lugar de `ResultItemViewModel`.

### Cambios en `MainWindowViewModel.OnSelectedResultChanged`

Añadir el caso clipboard ANTES del guard `_isPreviewEnabled`:

```
if (EditorPanel.IsEditMode) return;

if (value is ClipboardResultItemViewModel clip) {
    EditorPanel.LoadTextContent(clip.FullText);
    IsEditorOpen = true;
    return;
}

// ...resto sin cambios (guard _isPreviewEnabled, file preview, else IsEditorOpen=false)
```

**Invariantes**:

- El preview de clipboard no modifica `_isPreviewEnabled`. El file preview no interfiere.
- Si `_isPreviewEnabled` es true y el usuario navega a un clipboard item: se muestra el preview de clipboard. Al volver a un fichero de texto, se muestra el file preview. Al ir a otro tipo de resultado, se cierra.
- Cmd+P cierra el panel de clipboard (via el handler existente `RequestClose`). El panel no se re-abre automaticamente hasta que el usuario navegue a otro item de clipboard.
- `EnableFileEditor = false` no bloquea el preview de clipboard: el panel se controla solo con `IsEditorOpen`.

### Cambio en el template AXAML

No se necesita. El `DataTemplate` de `ResultItemViewModel` se reutiliza para `ClipboardResultItemViewModel` (herencia), y el `EditorPanelView` ya muestra AvaloniaEdit en modo read-only para modo Preview.

### Tests

- `ClipboardHistorySearchTests.cs`: actualizar `Result_LongText_TruncatedTo120Chars` a 62 chars (60 + `…` = 61, o con margen). Añadir `Result_BuildResult_ReturnsClipboardResultItemViewModel` verificando el tipo y que `FullText` tiene el texto sin truncar.

**Ficheros afectados**:

| Fichero | Cambio |
|---|---|
| `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs` | Guard `:`, truncacion 60, devuelve `ClipboardResultItemViewModel` |
| `Yottacast.Core/ViewModels/ClipboardResultItemViewModel.cs` | Nuevo |
| `Yottacast.Core/ViewModels/EditorPanelViewModel.cs` | `LoadTextContent()` |
| `Yottacast/ViewModels/MainWindowViewModel.cs` | `OnSelectedResultChanged` |
| `Yottacast/Views/MainWindow.axaml.cs` | Guard `!e.Handled` en Ctrl+Down |
| `Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs` | Test C1 + actualizar test truncacion |
