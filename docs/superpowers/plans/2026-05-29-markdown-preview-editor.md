# Markdown Preview en el Editor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Renderizar ficheros `.md`/`.markdown` con `Markdown.Avalonia` en modo Preview, y desactivar el syntax highlighting de AvaloniaEdit para esos mismos ficheros.

**Architecture:** Se añade el control `MarkdownScrollViewer` en `EditorPanelView` que se muestra condicionalmente cuando el fichero activo es Markdown y el modo es Preview. Dos propiedades calculadas nuevas en `EditorPanelViewModel` (`IsMarkdownFile`, `IsMarkdownPreview`) controlan la visibilidad. El syntax highlighting se suprime en `ApplySyntaxHighlighting` para extensiones `.md`/`.markdown`.

**Tech Stack:** Avalonia 11.3.12, Markdown.Avalonia 11.3.x, CommunityToolkit.Mvvm 8.2.1

**Spec:** `docs/superpowers/specs/2026-05-28-markdown-preview-editor-design.md`

---

## Mapa de ficheros

**Modificados:**
- `Yottacast/Yottacast.csproj` — añadir `Markdown.Avalonia`
- `Yottacast.Core/ViewModels/EditorPanelViewModel.cs` — propiedades `IsMarkdownFile`, `IsMarkdownPreview`
- `Yottacast/Views/EditorPanelView.axaml` — namespace `md:`, Grid Row 1 con Panel + visibilidad condicional
- `Yottacast/Views/EditorPanelView.axaml.cs` — `ApplySyntaxHighlighting` salta `.md`/`.markdown`

**Sin cambios:** `MainWindow`, `FileEditorService`, `UserSettings`, tests, temas JSON.

---

## Task 1: Añadir paquete Markdown.Avalonia

**Files:**
- Modify: `Yottacast/Yottacast.csproj`

- [ ] **Añadir la referencia al paquete** en el bloque `<ItemGroup>` de paquetes existente (junto a `Avalonia.AvaloniaEdit`):

```xml
<PackageReference Include="Markdown.Avalonia" Version="11.3.*" />
```

El fichero queda así en ese ItemGroup:
```xml
<PackageReference Include="Avalonia" Version="11.3.12" />
<PackageReference Include="Avalonia.AvaloniaEdit" Version="11.3.0" />
<PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.12" />
<PackageReference Include="Avalonia.Fonts.Inter" Version="11.3.12" />
<PackageReference Include="Markdown.Avalonia" Version="11.3.*" />
```

- [ ] **Restaurar paquetes y verificar que compila:**

```bash
cd Yottacast && dotnet restore && dotnet build
```

Resultado esperado: sin errores.

- [ ] **Commit:**

```bash
git add Yottacast/Yottacast.csproj
git commit -m "feat: añadir Markdown.Avalonia"
```

---

## Task 2: Propiedades ViewModel (IsMarkdownFile, IsMarkdownPreview)

**Files:**
- Modify: `Yottacast.Core/ViewModels/EditorPanelViewModel.cs`

El fichero actual tiene estas propiedades calculadas al final de los ObservableProperty:
```csharp
public bool IsDirty => Content != _originalContent;
public string TitleText => IsDirty ? $"* {FilePath}" : FilePath;
public bool IsAutoSave { get; private set; }
public bool ShowSaveButton => !IsAutoSave && Mode == EditorMode.Edit;
public bool IsPreviewMode => Mode == EditorMode.Preview;
public bool IsEditMode => Mode == EditorMode.Edit;
```

Y estos parciales:
```csharp
partial void OnFilePathChanged(string value) => OnPropertyChanged(nameof(TitleText));

partial void OnModeChanged(EditorMode value) {
    OnPropertyChanged(nameof(ShowSaveButton));
    OnPropertyChanged(nameof(IsPreviewMode));
    OnPropertyChanged(nameof(IsEditMode));
}
```

- [ ] **Añadir las dos propiedades nuevas** justo después de `IsEditMode`:

```csharp
private static readonly HashSet<string> MarkdownExtensions = [".md", ".markdown"];
public bool IsMarkdownFile =>
    MarkdownExtensions.Contains(Path.GetExtension(FilePath).ToLowerInvariant());
public bool IsMarkdownPreview => IsPreviewMode && IsMarkdownFile;
```

- [ ] **Actualizar `OnFilePathChanged`** para notificar las nuevas propiedades:

```csharp
partial void OnFilePathChanged(string value) {
    OnPropertyChanged(nameof(TitleText));
    OnPropertyChanged(nameof(IsMarkdownFile));
    OnPropertyChanged(nameof(IsMarkdownPreview));
}
```

- [ ] **Actualizar `OnModeChanged`** para notificar `IsMarkdownPreview`:

```csharp
partial void OnModeChanged(EditorMode value) {
    OnPropertyChanged(nameof(ShowSaveButton));
    OnPropertyChanged(nameof(IsPreviewMode));
    OnPropertyChanged(nameof(IsEditMode));
    OnPropertyChanged(nameof(IsMarkdownPreview));
}
```

- [ ] **Verificar que compila:**

```bash
cd Yottacast.Core && dotnet build
```

Resultado esperado: sin errores.

- [ ] **Commit:**

```bash
git add Yottacast.Core/ViewModels/EditorPanelViewModel.cs
git commit -m "feat: IsMarkdownFile e IsMarkdownPreview en EditorPanelViewModel"
```

---

## Task 3: Actualizar EditorPanelView.axaml

**Files:**
- Modify: `Yottacast/Views/EditorPanelView.axaml`

- [ ] **Añadir el namespace de Markdown.Avalonia** en el elemento raíz `<UserControl>`, junto a los existentes:

```xml
xmlns:md="clr-namespace:Markdown.Avalonia;assembly=Markdown.Avalonia"
```

El bloque de atributos del UserControl quedará así (los existentes no cambian):
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:Yottacast.Core.ViewModels;assembly=Yottacast.Core"
             xmlns:aedit="clr-namespace:AvaloniaEdit;assembly=AvaloniaEdit"
             xmlns:md="clr-namespace:Markdown.Avalonia;assembly=Markdown.Avalonia"
             x:Class="Yottacast.Views.EditorPanelView"
             x:DataType="vm:EditorPanelViewModel">
```

- [ ] **Reemplazar el bloque Grid Row 1** (el `Border` que envuelve el `TextEditor`) por un `Panel` que contenga ambos controles con visibilidad condicional:

Bloque **actual**:
```xml
<!-- ── AvaloniaEdit TextEditor ── -->
<Border Grid.Row="1"
        Background="{DynamicResource Theme.Results.Background}"
        CornerRadius="0">
    <aedit:TextEditor x:Name="Editor"
                      IsReadOnly="{Binding IsPreviewMode}"
                      ShowLineNumbers="False"
                      FontFamily="Cascadia Code, Fira Code, Menlo, Consolas, monospace"
                      FontSize="13"
                      WordWrap="False"
                      Background="Transparent"/>
</Border>
```

Bloque **nuevo**:
```xml
<!-- ── Editor / Markdown Preview ── -->
<Panel Grid.Row="1">
    <!-- AvaloniaEdit: ficheros de código/texto y .md en modo Edit -->
    <Border IsVisible="{Binding !IsMarkdownPreview}"
            Background="{DynamicResource Theme.Results.Background}"
            CornerRadius="0">
        <aedit:TextEditor x:Name="Editor"
                          IsReadOnly="{Binding IsPreviewMode}"
                          ShowLineNumbers="False"
                          FontFamily="Cascadia Code, Fira Code, Menlo, Consolas, monospace"
                          FontSize="13"
                          WordWrap="False"
                          Background="Transparent"/>
    </Border>
    <!-- Markdown renderizado: solo para .md en modo Preview -->
    <md:MarkdownScrollViewer IsVisible="{Binding IsMarkdownPreview}"
                              Markdown="{Binding Content}"
                              Background="{DynamicResource Theme.Results.Background}"
                              Padding="12,8"/>
</Panel>
```

- [ ] **Verificar que compila:**

```bash
cd Yottacast && dotnet build
```

Resultado esperado: sin errores. Si hay error de tipo en el binding `!IsMarkdownPreview`, verificar que `IsMarkdownPreview` es `bool` (no `bool?`) en el ViewModel.

- [ ] **Commit:**

```bash
git add Yottacast/Views/EditorPanelView.axaml
git commit -m "feat: MarkdownScrollViewer en EditorPanelView para preview de .md"
```

---

## Task 4: Desactivar syntax highlighting para Markdown

**Files:**
- Modify: `Yottacast/Views/EditorPanelView.axaml.cs`

El método actual:
```csharp
private void ApplySyntaxHighlighting(string filePath) {
    if (string.IsNullOrEmpty(filePath)) return;
    var ext = Path.GetExtension(filePath);
    Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
}
```

- [ ] **Reemplazar el método** por la versión que suprime highlighting para `.md`/`.markdown`:

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

- [ ] **Verificar que compila:**

```bash
cd Yottacast && dotnet build
```

Resultado esperado: sin errores.

- [ ] **Commit:**

```bash
git add Yottacast/Views/EditorPanelView.axaml.cs
git commit -m "feat: desactivar syntax highlighting de AvaloniaEdit para .md"
```

---

## Task 5: Verificación manual

- [ ] **Arrancar la app:**

```bash
cd Yottacast && dotnet run
```

- [ ] **Verificar Preview de .md**: buscar cualquier fichero `.md` (por ejemplo `README.md`), seleccionarlo y pulsar Cmd+P.
  - Resultado esperado: el panel muestra el Markdown renderizado (títulos grandes, listas con bullets, código con fondo, etc.), no el texto crudo.

- [ ] **Verificar Edit de .md**: con el mismo fichero pulsar Cmd+E (o Cmd+E desde la búsqueda directamente).
  - Resultado esperado: AvaloniaEdit muestra el texto fuente `.md` sin ningún coloreado de sintaxis (texto plano en blanco sobre fondo oscuro).

- [ ] **Verificar que otros ficheros no cambian**: buscar un `.cs` o `.json`, abrir en Preview (Cmd+P) y en Edit (Cmd+E).
  - Resultado esperado: comportamiento idéntico al anterior (syntax highlighting activo).

- [ ] **Commit final si todo está bien:**

```bash
git add -A
git commit -m "chore: verificación manual ok"
```
