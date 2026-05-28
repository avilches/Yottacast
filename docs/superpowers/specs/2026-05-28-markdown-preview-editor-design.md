# Markdown Preview en el Editor — Spec de diseño

**Fecha**: 2026-05-28  
**Feature**: Renderizar ficheros `.md`/`.markdown` con `Markdown.Avalonia` en modo Preview, y desactivar el syntax highlighting de AvaloniaEdit para esos mismos ficheros en modo Edit.

---

## Comportamiento resultante

| Fichero | Modo | Vista |
|---|---|---|
| `.md`, `.markdown` | Preview (Cmd+P) | `MarkdownScrollViewer` renderizado |
| `.md`, `.markdown` | Edit (Cmd+E) | AvaloniaEdit sin syntax highlighting |
| Cualquier otro texto/código | Preview | AvaloniaEdit con syntax highlighting |
| Cualquier otro texto/código | Edit | AvaloniaEdit con syntax highlighting |

El resto del flujo (hotkeys, autosave, overlay de cambios sin guardar, `FileEditorService`) no cambia.

---

## Cambios por fichero

### `Yottacast/Yottacast.csproj`

Añadir los paquetes:

```xml
<PackageReference Include="Markdown.Avalonia" Version="11.3.*" />
<PackageReference Include="Markdown.Avalonia.SyntaxHigh" Version="11.3.*" />
```

`Markdown.Avalonia.SyntaxHigh` activa el syntax highlighting dentro de los bloques de código del preview Markdown.

### `Yottacast.Core/ViewModels/EditorPanelViewModel.cs`

Añadir dos propiedades calculadas:

```csharp
private static readonly HashSet<string> MarkdownExtensions = [".md", ".markdown"];

public bool IsMarkdownFile =>
    MarkdownExtensions.Contains(Path.GetExtension(FilePath).ToLowerInvariant());

public bool IsMarkdownPreview => IsPreviewMode && IsMarkdownFile;
```

Notificar ambas en los parciales existentes:

- `OnFilePathChanged` → añadir `OnPropertyChanged(nameof(IsMarkdownFile))` y `OnPropertyChanged(nameof(IsMarkdownPreview))`
- `OnModeChanged` → añadir `OnPropertyChanged(nameof(IsMarkdownPreview))`

### `Yottacast/Views/EditorPanelView.axaml`

En Grid Row 1 (zona del editor), añadir visibilidad condicional:

- El `Border` que envuelve el `TextEditor` pasa a tener `IsVisible="{Binding !IsMarkdownPreview}"`.
- Junto a él (mismo Grid Row 1, dentro de un `Panel` o `Grid`), añadir:

```xml
<md:MarkdownScrollViewer
    IsVisible="{Binding IsMarkdownPreview}"
    Markdown="{Binding Content}"
    Background="{DynamicResource Theme.Results.Background}"/>
```

donde `md` es el namespace `clr-namespace:Markdown.Avalonia;assembly=Markdown.Avalonia`.

### `Yottacast/Views/EditorPanelView.axaml.cs`

En el método `ApplySyntaxHighlighting`, saltar highlighting para Markdown:

```csharp
private void ApplySyntaxHighlighting(string filePath) {
    if (string.IsNullOrEmpty(filePath)) return;
    var ext = Path.GetExtension(filePath).ToLowerInvariant();
    if (ext is ".md" or ".markdown") {
        Editor.SyntaxHighlighting = null;
        return;
    }
    Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
}
```

---

## Ficheros involucrados

**Modificados:**
- `Yottacast/Yottacast.csproj`
- `Yottacast.Core/ViewModels/EditorPanelViewModel.cs`
- `Yottacast/Views/EditorPanelView.axaml`
- `Yottacast/Views/EditorPanelView.axaml.cs`

**Sin cambios:** `MainWindow`, `FileEditorService`, `UserSettings`, tests, temas JSON.

---

## Invariantes

- En Preview de `.md`, el `TextEditor` no es visible ni recibe input.
- En Edit de `.md`, el `MarkdownScrollViewer` no es visible.
- Para cualquier extensión que no sea `.md`/`.markdown`, el comportamiento del editor no cambia respecto al estado anterior a este cambio.
- El binding de `Content` al `MarkdownScrollViewer` es one-way; el usuario no puede editar desde el preview.
