# Clipboard + Emoji Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Corregir tres bugs del area clipboard/emoji: clipboard visible en modo emoji, Ctrl+Down que no navega el grid, y texto truncado en lista con preview lateral siempre visible.

**Architecture:** Tres cambios independientes en Core (guard emoji en ClipboardHistorySearch, nueva clase ClipboardResultItemViewModel, metodo LoadTextContent en EditorPanelViewModel) y dos en la GUI (guard e.Handled en MainWindow, caso clipboard en OnSelectedResultChanged). Tests en Yottacast.Core.Tests.

**Tech Stack:** .NET 9, Avalonia 11, CommunityToolkit.Mvvm, xUnit.

---

## Mapa de ficheros

| Fichero | Operacion | Motivo |
|---|---|---|
| `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs` | Modificar | Guard `:`, truncacion 60, devolver `ClipboardResultItemViewModel` |
| `Yottacast.Core/ViewModels/ClipboardResultItemViewModel.cs` | Crear | Nueva clase con `FullText` |
| `Yottacast.Core/ViewModels/EditorPanelViewModel.cs` | Modificar | Metodo `LoadTextContent()` |
| `Yottacast/ViewModels/MainWindowViewModel.cs` | Modificar | Caso clipboard en `OnSelectedResultChanged` |
| `Yottacast/Views/MainWindow.axaml.cs` | Modificar | Guard `!e.Handled` en Ctrl+Down |
| `Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs` | Modificar | Tests C1 + actualizar test truncacion + tests C4 |

---

## Task 1: C1 - Guard emoji en ClipboardHistorySearch

**Files:**
- Modify: `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs:34`
- Test: `Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs`

- [ ] **Step 1: Escribir los tests que deben fallar**

Abrir `Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs` y anadir al final de la clase (antes del cierre `}`):

```csharp
[Fact]
public void Search_EmojiQuery_ReturnsEmpty()
{
    var (search, store, _) = Build();
    store.Add("hello");
    var results = search.Search(":smile", 10);
    Assert.Empty(results);
}

[Fact]
public void Search_EmojiQueryJustColon_ReturnsEmpty()
{
    var (search, store, _) = Build();
    store.Add("hello");
    var results = search.Search(":", 10);
    Assert.Empty(results);
}
```

- [ ] **Step 2: Ejecutar los tests para verificar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "Search_EmojiQuery" -v
```

Esperado: FAIL — los tests devuelven 1 resultado en lugar de 0.

- [ ] **Step 3: Implementar el guard**

En `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs`, en el metodo `Search` (linea 34), anadir el guard como primera linea del cuerpo:

```csharp
public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit)
{
    if (query.StartsWith(':')) return [];

    var entries = store.GetAll();
    // ...resto sin cambios
```

- [ ] **Step 4: Verificar que los tests pasan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "Search_EmojiQuery" -v
```

Esperado: PASS ambos tests.

- [ ] **Step 5: Ejecutar la suite completa de ClipboardHistorySearch**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "ClipboardHistorySearchTests" -v
```

Esperado: todos los tests existentes siguen en verde.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs \
        Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs
git commit -m "fix(clipboard): excluir historial de clipboard en modo emoji"
```

---

## Task 2: C2+C4 parte 1 - ClipboardResultItemViewModel

**Files:**
- Create: `Yottacast.Core/ViewModels/ClipboardResultItemViewModel.cs`

No hay tests unitarios directos para esta clase (se testa via ClipboardHistorySearch en Task 3).

- [ ] **Step 1: Crear el fichero**

Crear `Yottacast.Core/ViewModels/ClipboardResultItemViewModel.cs` con este contenido exacto:

```csharp
namespace Yottacast.Core.ViewModels;

public class ClipboardResultItemViewModel : ResultItemViewModel
{
    public required string FullText { get; init; }
}
```

- [ ] **Step 2: Verificar que compila**

```bash
cd Yottacast.Core && dotnet build
```

Esperado: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Yottacast.Core/ViewModels/ClipboardResultItemViewModel.cs
git commit -m "feat(clipboard): ClipboardResultItemViewModel con FullText"
```

---

## Task 3: C2+C4 parte 2 - BuildResult devuelve ClipboardResultItemViewModel con truncacion 60

**Files:**
- Modify: `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs:69-119`
- Test: `Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs`

- [ ] **Step 1: Escribir los tests que deben fallar**

En `Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs`, localizar el test `Result_LongText_TruncatedTo120Chars` (linea ~187) y sustituirlo por:

```csharp
[Fact]
public void Result_LongText_TruncatedTo60Chars()
{
    var (search, store, _) = Build();
    var longText = new string('a', 200);
    store.Add(longText);
    var result = search.Search(new string('a', 5), 10).First();
    Assert.True(result.Title.Length <= 62); // 60 chars + "…"
}

[Fact]
public void Result_IsClipboardResultItemViewModel()
{
    var (search, store, _) = Build();
    store.Add("hello");
    var result = search.Search("hello", 10).First();
    Assert.IsType<ClipboardResultItemViewModel>(result);
}

[Fact]
public void Result_FullTextIsUntruncated()
{
    var (search, store, _) = Build();
    var longText = new string('a', 200);
    store.Add(longText);
    var result = search.Search(new string('a', 5), 10).First();
    var clipResult = Assert.IsType<ClipboardResultItemViewModel>(result);
    Assert.Equal(longText, clipResult.FullText);
}
```

- [ ] **Step 2: Ejecutar para verificar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "Result_LongText_TruncatedTo60Chars|Result_IsClipboard|Result_FullText" -v
```

Esperado: FAIL — titulo demasiado largo, tipo incorrecto, FullText no existe.

- [ ] **Step 3: Implementar los cambios en BuildResult**

En `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs`, reemplazar el metodo `BuildResult` completo (lineas 69-119):

```csharp
private ClipboardResultItemViewModel BuildResult(ClipboardHistoryEntry entry, double score)
{
    var displayText = entry.Text.Replace('\n', '·').Replace('\r', '·');
    if (displayText.Length > 60) displayText = displayText[..60] + "…";

    var subtitle = FormatRelativeTime(entry.CopiedAt);
    var capturedText = entry.Text;

    return new ClipboardResultItemViewModel
    {
        FullText = capturedText,
        Title    = displayText,
        Subtitle = subtitle,
        Category = "Clipboard",
        Score    = score,
        Actions  =
        [
            new()
            {
                Label           = "Paste",
                Hotkey          = ActionHotkey.Enter,
                ShowInFooter    = true,
                ShowInMenu      = true,
                ClosesMenu      = true,
                ClosesWindow    = true,
                PasteAfterClose = true,
                Execute = () =>
                {
                    logger.LogInformation("ClipboardHistory: paste \"{Text}\"",
                        capturedText.Length > 40 ? capturedText[..40] + "…" : capturedText);
                    clipboard.CopyText(capturedText);
                    store.RecordUsage(capturedText);
                },
            },
            new()
            {
                Label        = "Delete",
                Hotkey       = ActionHotkey.Delete,
                ShowInFooter = true,
                ShowInMenu   = true,
                ClosesMenu   = true,
                ClosesWindow = false,
                Execute      = () =>
                {
                    logger.LogInformation("ClipboardHistory: delete \"{Text}\"",
                        capturedText.Length > 40 ? capturedText[..40] + "…" : capturedText);
                    store.Remove(capturedText);
                },
            },
        ],
    };
}
```

Nota: el tipo de retorno del metodo pasa de `ResultItemViewModel` a `ClipboardResultItemViewModel`. El tipo de retorno de `Search` sigue siendo `IReadOnlyList<BaseResultItemViewModel>` (sin cambios).

- [ ] **Step 4: Verificar que los tests pasan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "ClipboardHistorySearchTests" -v
```

Esperado: todos en verde, incluidos los tres nuevos.

- [ ] **Step 5: Commit**

```bash
git add Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs \
        Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs
git commit -m "feat(clipboard): truncar titulo a 60 chars y devolver ClipboardResultItemViewModel"
```

---

## Task 4: C2+C4 parte 3 - LoadTextContent en EditorPanelViewModel

**Files:**
- Modify: `Yottacast.Core/ViewModels/EditorPanelViewModel.cs`

No hay tests unitarios de UI para este metodo (se verifica manualmente).

- [ ] **Step 1: Anadir el metodo LoadTextContent**

En `Yottacast.Core/ViewModels/EditorPanelViewModel.cs`, despues del metodo `LoadPreview` (linea ~62), anadir:

```csharp
public void LoadTextContent(string text) {
    FilePath = "";
    FileName = "";
    Mode = EditorMode.Preview;
    ShowUnsavedDialog = false;
    _originalContent = text;
    Content = text;
}
```

- [ ] **Step 2: Verificar que compila**

```bash
cd Yottacast.Core && dotnet build
```

Esperado: Build succeeded, 0 errors.

- [ ] **Step 3: Ejecutar suite completa de Core**

```bash
cd Yottacast.Core.Tests && dotnet test
```

Esperado: todos en verde (este cambio no afecta tests existentes).

- [ ] **Step 4: Commit**

```bash
git add Yottacast.Core/ViewModels/EditorPanelViewModel.cs
git commit -m "feat(editor): LoadTextContent para mostrar texto de clipboard sin ruta de fichero"
```

---

## Task 5: C2+C4 parte 4 - Caso clipboard en OnSelectedResultChanged

**Files:**
- Modify: `Yottacast/ViewModels/MainWindowViewModel.cs:505-526`

- [ ] **Step 1: Modificar OnSelectedResultChanged**

En `Yottacast/ViewModels/MainWindowViewModel.cs`, reemplazar el bloque `OnSelectedResultChanged` (lineas 505-526) por:

```csharp
partial void OnSelectedResultChanged(BaseResultItemViewModel? value) {
    OnPropertyChanged(nameof(IsEmojiMode));
    OnPropertyChanged(nameof(FooterHints));
    OnPropertyChanged(nameof(OptionsMenuActions));
    OnPropertyChanged(nameof(OptionsMenuItems));
    OnPropertyChanged(nameof(HasOptionsMenu));
    if (!HasOptionsMenu) CloseOptionsMenu();

    if (EditorPanel.IsEditMode) return; // buscador pausado: no cambiar fichero mientras se edita

    if (value is ClipboardResultItemViewModel clip) {
        EditorPanel.LoadTextContent(clip.FullText);
        IsEditorOpen = true;
        return;
    }

    if (!IsEditorOpen && !_isPreviewEnabled) return;

    if (value is FileResultItemViewModel { ItemPath: { } path }) {
        if (fileEditorService.IsTextContent(path)) {
            EditorPanel.LoadPreview(path);
            IsEditorOpen = true;
        } else {
            IsEditorOpen = false; // sin preview para este elemento; _isPreviewEnabled permanece activo
        }
    } else {
        IsEditorOpen = false;
    }
}
```

Diferencias respecto al original:
- El guard `if (!IsEditorOpen && !_isPreviewEnabled) return;` se mueve DESPUES del caso clipboard.
- El guard `if (EditorPanel.IsEditMode) return;` se mueve ANTES del caso clipboard.
- Caso nuevo: `if (value is ClipboardResultItemViewModel clip)` que carga el texto y sale.

- [ ] **Step 2: Verificar que compila la solucion completa**

```bash
dotnet build Yottacast.sln
```

Esperado: Build succeeded, 0 errors, 0 warnings relevantes.

- [ ] **Step 3: Ejecutar suite completa**

```bash
cd Yottacast.Core.Tests && dotnet test
```

Esperado: todos en verde.

- [ ] **Step 4: Commit**

```bash
git add Yottacast/ViewModels/MainWindowViewModel.cs
git commit -m "feat(clipboard): preview lateral siempre visible al seleccionar item de clipboard"
```

---

## Task 6: E1 - Guard e.Handled en Ctrl+Down

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml.cs:661-669`

No hay tests de UI para este comportamiento.

- [ ] **Step 1: Anadir el guard**

En `Yottacast/Views/MainWindow.axaml.cs`, reemplazar el bloque `case Key.Down` (lineas 661-669):

**Antes:**
```csharp
case Key.Down:
    if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
        vm.NavigateHistoryForward();
        SearchBox.CaretIndex = int.MaxValue;
    } else {
        SelectNext(vm, +1);
    }
    e.Handled = true;
    break;
```

**Despues:**
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

- [ ] **Step 2: Verificar que compila la solucion completa**

```bash
dotnet build Yottacast.sln
```

Esperado: Build succeeded, 0 errors.

- [ ] **Step 3: Ejecutar suite completa**

```bash
cd Yottacast.Core.Tests && dotnet test
```

Esperado: todos en verde.

- [ ] **Step 4: Commit**

```bash
git add Yottacast/Views/MainWindow.axaml.cs
git commit -m "fix(emoji): Ctrl+Down navega el grid en lugar de historial cuando el tunnel ya lo proceso"
```

---

## Task 7: Verificacion manual

- [ ] **Step 1: Lanzar la app**

```bash
cd Yottacast && dotnet run
```

- [ ] **Step 2: Verificar C1**

Escribir `:smile`. Comprobar que NO aparece ningun item de clipboard en la lista, solo el grid de emojis.

- [ ] **Step 3: Verificar E1**

Con el grid de emojis visible (`:` en el campo), pulsar Ctrl+Down. Debe mover la seleccion dentro del grid, no navegar el historial de busquedas.

- [ ] **Step 4: Verificar C2+C4 (lista)**

Abrir modo Clipboard (Cmd+Alt+Right o pill de Clipboard). Si hay items con texto largo, verificar que el titulo se trunca a ~60 chars con `…` al final y no solapa con la etiqueta "Clipboard".

- [ ] **Step 5: Verificar C2+C4 (preview)**

Seleccionar un item de clipboard. El panel lateral debe abrirse automaticamente con el texto completo. Pulsar Cmd+P debe cerrarlo. Navegar a otro item de clipboard debe reabrirlo.

- [ ] **Step 6: Verificar que el preview de ficheros no se rompe**

Reactivar el file editor en settings (poner `enableFileEditor: true` en `user-data/config/settings.json`), reiniciar. Buscar un fichero de texto, pulsar Cmd+P: el preview de fichero debe funcionar igual que antes.
