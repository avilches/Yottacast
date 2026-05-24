# File Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir un panel editor AvaloniaEdit que aparece a la derecha del buscador al pulsar Cmd+E sobre un fichero de texto/código, con soporte de syntax highlighting, save/autosave y diálogo de cambios sin guardar.

**Architecture:** Panel inline en MainWindow (Grid horizontal 2 columnas). Estado en `MainWindowViewModel` + `EditorPanelViewModel`. Lógica de I/O en `FileEditorService` (Core). Hotkey Cmd+E interceptada como caso especial en `MainWindow.axaml.cs` antes del loop genérico de acciones.

**Tech Stack:** Avalonia 11.3.12, AvaloniaEdit 11.x, CommunityToolkit.Mvvm 8.2.1, xUnit (tests)

---

## Mapa de ficheros

**Nuevos:**
- `Yottacast.Core/Services/FileEditorService.cs` — lectura/escritura, validación extensión + binario
- `Yottacast.Core/ViewModels/EditorPanelViewModel.cs` — estado del editor (contenido, dirty, autosave, overlay)
- `Yottacast/Views/EditorPanelView.axaml` + `.cs` — vista AvaloniaEdit con overlay de confirmación
- `Yottacast.Core.Tests/FileEditorServiceTests.cs` — tests TDD del servicio

**Modificados:**
- `Yottacast.Core/AppDefaults.cs` — 4 constantes nuevas
- `Yottacast.Core/ViewModels/ActionHotkey.cs` — constante `MetaE`
- `Yottacast.Core/Services/UserSettings.cs` — 3 campos + UserSettingsData + Load + Save
- `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs` — acción MetaE en ficheros editables
- `Yottacast/Yottacast.csproj` — paquete AvaloniaEdit
- `Yottacast/ViewModels/MainWindowViewModel.cs` — propiedades editor + OpenEditor/CloseEditor
- `Yottacast/Views/MainWindow.axaml` — Grid 2 columnas + EditorPanelView
- `Yottacast/Views/MainWindow.axaml.cs` — inyectar FileEditorService + Cmd+E + Esc overlay
- `Yottacast/ViewModels/SettingsWindowViewModel.cs` — sección FileEditor
- `Yottacast/Views/SettingsWindow.axaml` — nav + sección Editor
- `Yottacast/App.axaml.cs` — registrar FileEditorService, actualizar constructores

---

## Task 1: Constantes base (AppDefaults + ActionHotkey)

**Files:**
- Modify: `Yottacast.Core/AppDefaults.cs`
- Modify: `Yottacast.Core/ViewModels/ActionHotkey.cs`

- [ ] **Añadir constantes a `AppDefaults.cs`** — al final del fichero, antes del `}`

```csharp
    // ── File Editor ────────────────────────────────────────────────────────────
    /// Width of the inline editor panel in pixels.
    public const double EditorWidth = 680;
    /// Height of the inline editor panel in pixels (≈ max launcher height with full results).
    public const double EditorHeight = 640;
    /// Maximum file size in MB the editor will open.
    public const int EditorMaxFileSizeMb = 5;
    /// Number of bytes read to detect binary content (null-byte heuristic).
    public const int EditorBinaryDetectionBytes = 8_192;
```

- [ ] **Añadir `MetaE` a `ActionHotkey.cs`**

Añadir al final del bloque de static readonly:
```csharp
    public static readonly ActionHotkey MetaE      = new("E", ActionModifiers.Meta);
    public static readonly ActionHotkey MetaS      = new("S", ActionModifiers.Meta);
```

- [ ] **Commit**

```bash
git add Yottacast.Core/AppDefaults.cs Yottacast.Core/ViewModels/ActionHotkey.cs
git commit -m "feat(editor): add AppDefaults constants and ActionHotkey.MetaE"
```

---

## Task 2: FileEditorService — TDD

**Files:**
- Create: `Yottacast.Core/Services/FileEditorService.cs`
- Create: `Yottacast.Core.Tests/FileEditorServiceTests.cs`

- [ ] **Escribir los tests primero** — crear `Yottacast.Core.Tests/FileEditorServiceTests.cs`

```csharp
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests;

public class FileEditorServiceTests {
    private readonly FileEditorService _svc = new();

    [Fact]
    public void HasEditableExtension_KnownExtension_ReturnsTrue() {
        Assert.True(_svc.HasEditableExtension("/foo/bar.txt", ["txt", "cs"]));
    }

    [Fact]
    public void HasEditableExtension_UnknownExtension_ReturnsFalse() {
        Assert.False(_svc.HasEditableExtension("/foo/bar.exe", ["txt", "cs"]));
    }

    [Fact]
    public void HasEditableExtension_CaseInsensitive() {
        Assert.True(_svc.HasEditableExtension("/foo/bar.TXT", ["txt"]));
    }

    [Fact]
    public void HasEditableExtension_NoExtension_ReturnsFalse() {
        Assert.False(_svc.HasEditableExtension("/foo/Makefile", ["txt", "cs"]));
    }

    [Fact]
    public void IsTextContent_TextFile_ReturnsTrue() {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "Hello, world!\nLine 2");
        try { Assert.True(_svc.IsTextContent(tmp)); }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void IsTextContent_BinaryFile_ReturnsFalse() {
        var tmp = Path.GetTempFileName();
        File.WriteAllBytes(tmp, [0x00, 0x01, 0x02, 0x00, 0xFF]);
        try { Assert.False(_svc.IsTextContent(tmp)); }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void IsTextContent_EmptyFile_ReturnsTrue() {
        var tmp = Path.GetTempFileName();
        File.WriteAllBytes(tmp, []);
        try { Assert.True(_svc.IsTextContent(tmp)); }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CanOpen_ValidTextFile_ReturnsCanOpen() {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "hello");
        var ext = Path.GetExtension(tmp).TrimStart('.');
        try {
            var result = _svc.CanOpen(tmp, [ext]);
            Assert.True(result.CanOpen);
            Assert.Null(result.Error);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CanOpen_WrongExtension_ReturnsFalse() {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "hello");
        try {
            var result = _svc.CanOpen(tmp, ["neverexists"]);
            Assert.False(result.CanOpen);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CanOpen_BinaryContent_ReturnsError() {
        var tmp = Path.GetTempFileName();
        File.WriteAllBytes(tmp, [0x00, 0x01, 0x02]);
        var ext = Path.GetExtension(tmp).TrimStart('.');
        try {
            var result = _svc.CanOpen(tmp, [ext]);
            Assert.False(result.CanOpen);
            Assert.NotNull(result.Error);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ReadFile_ReturnsContent() {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "hello content");
        try { Assert.Equal("hello content", _svc.ReadFile(tmp)); }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void WriteFile_WritesContent() {
        var tmp = Path.GetTempFileName();
        try {
            _svc.WriteFile(tmp, "new content");
            Assert.Equal("new content", File.ReadAllText(tmp));
        }
        finally { File.Delete(tmp); }
    }
}
```

- [ ] **Ejecutar tests para verificar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "FileEditorServiceTests" 2>&1 | tail -5
```
Expected: error de compilación o FAIL porque `FileEditorService` no existe.

- [ ] **Crear `Yottacast.Core/Services/FileEditorService.cs`**

```csharp
namespace Yottacast.Core.Services;

public class FileEditorService {
    public record OpenResult(bool CanOpen, string? Error = null);

    public bool HasEditableExtension(string filePath, IReadOnlyList<string> extensions) {
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) return false;
        return extensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsTextContent(string filePath) {
        try {
            var length = new FileInfo(filePath).Length;
            if (length == 0) return true;
            var bytesToRead = (int)Math.Min(length, AppDefaults.EditorBinaryDetectionBytes);
            var buffer = new byte[bytesToRead];
            using var fs = File.OpenRead(filePath);
            var read = fs.Read(buffer, 0, bytesToRead);
            for (var i = 0; i < read; i++)
                if (buffer[i] == 0) return false;
            return true;
        } catch {
            return false;
        }
    }

    public OpenResult CanOpen(string filePath, IReadOnlyList<string> extensions) {
        if (!HasEditableExtension(filePath, extensions))
            return new(false);
        var info = new FileInfo(filePath);
        if (!info.Exists)
            return new(false, "File not found");
        if (info.Length > (long)AppDefaults.EditorMaxFileSizeMb * 1024 * 1024)
            return new(false, "File is too large to open in the editor");
        if (!IsTextContent(filePath))
            return new(false, "File appears to be binary");
        return new(true);
    }

    public string ReadFile(string filePath) => File.ReadAllText(filePath);

    public void WriteFile(string filePath, string content) => File.WriteAllText(filePath, content);
}
```

- [ ] **Ejecutar tests para verificar que pasan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "FileEditorServiceTests" 2>&1 | tail -10
```
Expected: todos los tests PASS.

- [ ] **Commit**

```bash
git add Yottacast.Core/Services/FileEditorService.cs Yottacast.Core.Tests/FileEditorServiceTests.cs
git commit -m "feat(editor): add FileEditorService with tests"
```

---

## Task 3: UserSettings — tres campos nuevos

**Files:**
- Modify: `Yottacast.Core/Services/UserSettings.cs`

Los cambios son en cuatro sitios del mismo fichero: la clase pública, el record `UserSettingsData`, el método `Load` y el método `Save`.

- [ ] **Añadir propiedades a la clase `UserSettings`** — después de `public bool EnableHistory`:

```csharp
    public bool EnableFileEditor { get; set; } = true;
    public bool FileEditorAutoSave { get; set; } = false;
    public List<string> FileEditorExtensions { get; set; } = [
        "txt", "md", "markdown", "log", "csv",
        "cs", "fs", "vb",
        "py", "rb", "go", "rs", "java", "kt", "swift", "c", "cpp", "h",
        "js", "ts", "jsx", "tsx", "vue",
        "json", "yaml", "yml", "toml", "ini", "cfg", "conf", "config", "env",
        "xml", "html", "htm", "css", "scss", "less",
        "sh", "bash", "zsh", "fish", "ps1",
        "gitignore", "gitattributes", "editorconfig", "dockerfile",
    ];
```

- [ ] **Añadir campos al record `UserSettingsData`** — después de `keepValueWhenHideDuration`:

```csharp
        [JsonPropertyName("enableFileEditor")] public bool EnableFileEditor { get; init; } = true;
        [JsonPropertyName("fileEditorAutoSave")] public bool FileEditorAutoSave { get; init; } = false;
        [JsonPropertyName("fileEditorExtensions")] public List<string>? FileEditorExtensions { get; init; }
```

- [ ] **Añadir asignación en `Load`** — dentro del bloque `settings = new UserSettings(...) { ... }`, después de `KeepValueWhenHideDuration = ...`:

```csharp
                    EnableFileEditor = data.EnableFileEditor,
                    FileEditorAutoSave = data.FileEditorAutoSave,
                    FileEditorExtensions = data.FileEditorExtensions is { Count: > 0 }
                        ? data.FileEditorExtensions
                        : new UserSettings(platform, logger, path).FileEditorExtensions,
```

- [ ] **Añadir serialización en `Save`** — dentro de `new UserSettingsData { ... }`, después de `KeepValueWhenHideDuration = ...`:

```csharp
                EnableFileEditor = EnableFileEditor,
                FileEditorAutoSave = FileEditorAutoSave,
                FileEditorExtensions = FileEditorExtensions,
```

- [ ] **Verificar que la solución compila**

```bash
cd Yottacast.Core && dotnet build 2>&1 | tail -5
```
Expected: Build succeeded.

- [ ] **Commit**

```bash
git add Yottacast.Core/Services/UserSettings.cs
git commit -m "feat(editor): add EnableFileEditor, FileEditorAutoSave, FileEditorExtensions settings"
```

---

## Task 4: EditorPanelViewModel

**Files:**
- Create: `Yottacast.Core/ViewModels/EditorPanelViewModel.cs`

- [ ] **Crear `EditorPanelViewModel.cs`**

```csharp
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yottacast.Core.ViewModels;

public partial class EditorPanelViewModel(FileEditorService fileEditorService) : ViewModelBase {
    private string _originalContent = "";

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private bool _showUnsavedDialog;
    [ObservableProperty] private string _statusText = "Ln 1, Col 1";

    public bool IsDirty => Content != _originalContent;
    public bool IsAutoSave { get; private set; }
    public bool ShowSaveButton => !IsAutoSave;

    /// <summary>Invoked when the editor decides to close (after save or discard).</summary>
    public Action? CloseRequested { get; init; }

    // Notify IsDirty when Content changes (Content uses [ObservableProperty] generated setter)
    partial void OnContentChanged(string value) => OnPropertyChanged(nameof(IsDirty));

    public void Load(string path, bool autoSave) {
        FilePath = path;
        FileName = Path.GetFileName(path);
        IsAutoSave = autoSave;
        var content = fileEditorService.ReadFile(path);
        _originalContent = content;
        Content = content;
        ShowUnsavedDialog = false;
    }

    [RelayCommand]
    public void SaveFile() {
        fileEditorService.WriteFile(FilePath, Content);
        _originalContent = Content;
    }

    [RelayCommand]
    public void SaveAndClose() {
        SaveFile();
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void DiscardAndClose() {
        _originalContent = Content;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void CancelUnsavedDialog() => ShowUnsavedDialog = false;

    /// <summary>Called when Cmd+E is pressed to close the editor.</summary>
    public void RequestClose() {
        if (IsAutoSave) {
            if (IsDirty) SaveFile();
            CloseRequested?.Invoke();
        } else if (!IsDirty) {
            CloseRequested?.Invoke();
        } else {
            ShowUnsavedDialog = true;
        }
    }

    public void UpdateStatus(int line, int col) => StatusText = $"Ln {line}, Col {col}";
}
```

Note: `ViewModelBase` is the base class used by all ViewModels in Yottacast.Core — check the existing class in `Yottacast.Core/ViewModels/` (same base as `BaseResultItemViewModel`). Add the `using` for `FileEditorService` if needed (it's in `Yottacast.Core.Services`).

- [ ] **Verificar que compila**

```bash
cd Yottacast.Core && dotnet build 2>&1 | tail -5
```

- [ ] **Commit**

```bash
git add Yottacast.Core/ViewModels/EditorPanelViewModel.cs
git commit -m "feat(editor): add EditorPanelViewModel"
```

---

## Task 5: MainWindowViewModel — propiedades del editor

**Files:**
- Modify: `Yottacast/ViewModels/MainWindowViewModel.cs`

El constructor de `MainWindowViewModel` es un primary constructor. Hay que añadir `FileEditorService fileEditorService` y declarar `EditorPanel`.

- [ ] **Añadir `FileEditorService` al constructor y campo `EditorPanel`**

Localizar la declaración del constructor (línea ~23):
```csharp
public partial class MainWindowViewModel(
    UserSettings settings,
    GlobalSearch globalSearch,
    ApplicationSearch appSearch,
    FileIconCache fileIconCache,
    UserDocumentSearch userDocumentSearch,
    UpdateChecker updateChecker,
    HistoryService historyService,
    UrlSearch urlSearch,
    DateSearch dateSearch,
    LaunchHistory launchHistory,
    IEnumerable<IEmptyStateSource> emptySources)
    : ViewModelBase {
```

Cambiar a (añadir `FileEditorService fileEditorService` antes de `IEnumerable<IEmptyStateSource>`):
```csharp
public partial class MainWindowViewModel(
    UserSettings settings,
    GlobalSearch globalSearch,
    ApplicationSearch appSearch,
    FileIconCache fileIconCache,
    UserDocumentSearch userDocumentSearch,
    UpdateChecker updateChecker,
    HistoryService historyService,
    UrlSearch urlSearch,
    DateSearch dateSearch,
    LaunchHistory launchHistory,
    FileEditorService fileEditorService,
    IEnumerable<IEmptyStateSource> emptySources)
    : ViewModelBase {
```

- [ ] **Añadir `[ObservableProperty] private bool _isEditorOpen;`** — junto a las demás `[ObservableProperty]` al inicio de la clase:

```csharp
    [ObservableProperty] private bool _isEditorOpen;
```

- [ ] **Añadir propiedad `EditorPanel` y métodos `OpenEditor`/`CloseEditor`** — al final de la clase, antes del último `}`:

```csharp
    public EditorPanelViewModel EditorPanel { get; } = new EditorPanelViewModel(fileEditorService) {
        CloseRequested = () => IsEditorOpen = false,
    };

    public void OpenEditor(string path) {
        EditorPanel.Load(path, settings.FileEditorAutoSave);
        IsEditorOpen = true;
    }
```

Note: `CloseRequested = () => IsEditorOpen = false` se puede escribir en el initializer del primary constructor porque `fileEditorService` y `IsEditorOpen` son accesibles en el cuerpo de la clase.

- [ ] **Añadir `using Yottacast.Core.Services;`** al bloque de usings si no está ya.

- [ ] **Verificar que compila**

```bash
cd Yottacast && dotnet build 2>&1 | tail -5
```
Expected: Build succeeded (habrá error porque App.axaml.cs no actualiza el constructor de MainWindowViewModel todavía — lo arreglamos en Task 11).

- [ ] **Commit**

```bash
git add Yottacast/ViewModels/MainWindowViewModel.cs
git commit -m "feat(editor): add IsEditorOpen, EditorPanel, OpenEditor to MainWindowViewModel"
```

---

## Task 6: AvaloniaEdit + EditorPanelView

**Files:**
- Modify: `Yottacast/Yottacast.csproj`
- Create: `Yottacast/Views/EditorPanelView.axaml`
- Create: `Yottacast/Views/EditorPanelView.axaml.cs`

- [ ] **Añadir AvaloniaEdit al `Yottacast.csproj`**

Dentro del `<ItemGroup>` de PackageReferences existente:
```xml
<PackageReference Include="AvaloniaEdit" Version="11.3.0" />
```

Verificar la versión exacta disponible:
```bash
cd Yottacast && dotnet add package AvaloniaEdit 2>&1 | tail -5
```
(Esto buscará la última versión compatible con Avalonia 11.3.x. Si instala una versión diferente, anotar la versión instalada.)

- [ ] **Crear `EditorPanelView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:Yottacast.Core.ViewModels;assembly=Yottacast.Core"
             xmlns:aedit="clr-namespace:AvaloniaEdit;assembly=AvaloniaEdit"
             x:Class="Yottacast.Views.EditorPanelView"
             x:DataType="vm:EditorPanelViewModel">

    <Grid RowDefinitions="40,*,24">

        <!-- Separator vertical izquierdo -->
        <Border Grid.RowSpan="3"
                Width="1"
                HorizontalAlignment="Left"
                Background="{DynamicResource Theme.Divider.Color}"/>

        <!-- Header bar -->
        <Grid Grid.Row="0"
              ColumnDefinitions="*,Auto"
              Margin="12,0,8,0">
            <TextBlock Grid.Column="0"
                       Text="{Binding FileName}"
                       VerticalAlignment="Center"
                       TextTrimming="CharacterEllipsis"
                       Foreground="{DynamicResource Theme.Results.Color}"
                       FontSize="13"/>
            <Button Grid.Column="1"
                    Content="Guardar"
                    IsVisible="{Binding ShowSaveButton}"
                    Command="{Binding SaveFileCommand}"
                    VerticalAlignment="Center"
                    Padding="10,4"
                    FontSize="12"/>
        </Grid>

        <!-- AvaloniaEdit TextEditor -->
        <aedit:TextEditor Grid.Row="1"
                          x:Name="Editor"
                          ShowLineNumbers="True"
                          FontFamily="Cascadia Code, Fira Code, Menlo, Consolas, monospace"
                          FontSize="13"
                          WordWrap="False"/>

        <!-- Status bar -->
        <Border Grid.Row="2"
                Background="{DynamicResource Theme.Window.Background}"
                BorderThickness="0,1,0,0"
                BorderBrush="{DynamicResource Theme.Divider.Color}"
                Padding="12,0">
            <TextBlock Text="{Binding StatusText}"
                       FontSize="11"
                       VerticalAlignment="Center"
                       Foreground="{DynamicResource Theme.Results.Subtitle.Color}"/>
        </Border>

        <!-- Unsaved changes overlay -->
        <Grid Grid.RowSpan="3"
              IsVisible="{Binding ShowUnsavedDialog}"
              Background="#B0000000">
            <Border CornerRadius="10"
                    Background="{DynamicResource Theme.Window.Background}"
                    MaxWidth="340"
                    HorizontalAlignment="Center"
                    VerticalAlignment="Center"
                    Padding="28,24">
                <StackPanel Spacing="16">
                    <TextBlock Text="Cambios sin guardar"
                               FontWeight="SemiBold"
                               FontSize="14"
                               Foreground="{DynamicResource Theme.Results.Color}"/>
                    <TextBlock Text="{Binding FileName}"
                               TextTrimming="CharacterEllipsis"
                               Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
                               FontSize="13"/>
                    <StackPanel Spacing="8" Margin="0,4,0,0">
                        <Button Content="Esc  —  Seguir editando"
                                Command="{Binding CancelUnsavedDialogCommand}"
                                HorizontalAlignment="Stretch"
                                HorizontalContentAlignment="Center"
                                Padding="0,8"/>
                        <Button Content="No guardar"
                                Command="{Binding DiscardAndCloseCommand}"
                                HorizontalAlignment="Stretch"
                                HorizontalContentAlignment="Center"
                                Padding="0,8"/>
                        <Button Content="⌘E  —  Guardar y cerrar"
                                Command="{Binding SaveAndCloseCommand}"
                                HorizontalAlignment="Stretch"
                                HorizontalContentAlignment="Center"
                                Padding="0,8"/>
                    </StackPanel>
                </StackPanel>
            </Border>
        </Grid>

    </Grid>
</UserControl>
```

- [ ] **Crear `EditorPanelView.axaml.cs`**

```csharp
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using AvaloniaEdit.Highlighting;
using Yottacast.Core.ViewModels;

namespace Yottacast.Views;

public partial class EditorPanelView : UserControl {
    private bool _settingContent;

    public EditorPanelView() {
        InitializeComponent();
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Editor.TextChanged += OnEditorTextChanged;
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is EditorPanelViewModel vm) {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _settingContent = true;
            Editor.Text = vm.Content;
            _settingContent = false;
            ApplySyntaxHighlighting(vm.FilePath);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is not EditorPanelViewModel vm) return;

        if (e.PropertyName == nameof(EditorPanelViewModel.Content) && !_settingContent) {
            _settingContent = true;
            Editor.Text = vm.Content;
            _settingContent = false;
        }

        if (e.PropertyName == nameof(EditorPanelViewModel.FilePath))
            ApplySyntaxHighlighting(vm.FilePath);
    }

    private void OnEditorTextChanged(object? sender, EventArgs e) {
        if (_settingContent) return;
        if (DataContext is EditorPanelViewModel vm) {
            _settingContent = true;
            vm.Content = Editor.Text;
            _settingContent = false;
        }
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e) {
        if (DataContext is EditorPanelViewModel vm)
            vm.UpdateStatus(Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column);
    }

    private void ApplySyntaxHighlighting(string filePath) {
        if (string.IsNullOrEmpty(filePath)) return;
        var ext = Path.GetExtension(filePath);
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
    }
}
```

- [ ] **Verificar que compila**

```bash
cd Yottacast && dotnet build 2>&1 | tail -10
```

- [ ] **Commit**

```bash
git add Yottacast/Yottacast.csproj Yottacast/Views/EditorPanelView.axaml Yottacast/Views/EditorPanelView.axaml.cs
git commit -m "feat(editor): add AvaloniaEdit package and EditorPanelView"
```

---

## Task 7: MainWindow.axaml — layout Grid de 2 columnas

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml`

El cambio consiste en: (a) quitar `Width=...` del `<Window>`, cambiar `SizeToContent` a `WidthAndHeight`; (b) envolver el `<Border Margin="28" ...>` existente en un `<Grid>` de 2 columnas y mover el `Width` al Border; (c) añadir `<views:EditorPanelView>` en la columna 1.

- [ ] **Cambiar el elemento `<Window>`** — quitar el atributo `Width="..."` y cambiar `SizeToContent`:

```xml
<!-- Antes -->
Width="{DynamicResource Theme.Window.Width}"
SizeToContent="Height"

<!-- Después -->
SizeToContent="WidthAndHeight"
```

- [ ] **Añadir namespace `views` al `<Window>`** si no existe ya:

```xml
xmlns:views="using:Yottacast.Views"
```

- [ ] **Envolver el `<Border Margin="28">` en un Grid** — el contenido actual es:

```xml
<Border Margin="28"
        CornerRadius="{DynamicResource Theme.Window.CornerRadius}"
        Background="{DynamicResource Theme.Window.Background}"
        ...>
    ...
</Border>
```

Cambiar a:

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>

    <Border Grid.Column="0"
            Width="{DynamicResource Theme.Window.Width}"
            Margin="28"
            CornerRadius="{DynamicResource Theme.Window.CornerRadius}"
            Background="{DynamicResource Theme.Window.Background}"
            TextElement.FontFamily="{DynamicResource Theme.Window.FontFamily}"
            PointerPressed="OnRootPointerPressed">
        <!-- contenido existente sin cambios -->
    </Border>

    <views:EditorPanelView Grid.Column="1"
                           IsVisible="{Binding IsEditorOpen}"
                           DataContext="{Binding EditorPanel}"
                           Width="680"
                           Height="640"/>
</Grid>
```

- [ ] **Verificar que compila y que el layout no tiene errores de XAML**

```bash
cd Yottacast && dotnet build 2>&1 | tail -10
```

- [ ] **Commit**

```bash
git add Yottacast/Views/MainWindow.axaml
git commit -m "feat(editor): restructure MainWindow as 2-column grid for editor panel"
```

---

## Task 8: MainWindow.axaml.cs — hotkey Cmd+E + Esc para overlay

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml.cs`

- [ ] **Inyectar `FileEditorService` en el constructor de `MainWindow`**

Localizar:
```csharp
private readonly UserSettings _settings;
private readonly ILogger<MainWindow> _logger;
```

Añadir:
```csharp
private readonly FileEditorService _fileEditorService;
```

Localizar el constructor:
```csharp
public MainWindow(UserSettings settings, ILogger<MainWindow> logger) {
    _settings = settings;
    _logger = logger;
```

Cambiar a:
```csharp
public MainWindow(UserSettings settings, ILogger<MainWindow> logger, FileEditorService fileEditorService) {
    _settings = settings;
    _logger = logger;
    _fileEditorService = fileEditorService;
```

- [ ] **Añadir el using necesario** si no está:

```csharp
using Yottacast.Core.Services;
```

- [ ] **Añadir manejo de Cmd+E en `OnTunnelKeyDown`** — insertar ANTES del comentario `// ── Generic action hotkeys`:

```csharp
        // ── Cmd+E: open/close inline file editor ────────────────────────────────
        if (AppHandler.Instance.MatchesHotkey(e, ActionHotkey.MetaE)) {
            if (vm.IsEditorOpen) {
                vm.EditorPanel.RequestClose();
                e.Handled = true;
                return;
            }
            if (_settings.EnableFileEditor
                && vm.SelectedResult is ResultItemViewModel { ItemPath: { } path }) {
                var check = _fileEditorService.CanOpen(path, _settings.FileEditorExtensions);
                if (check.CanOpen) {
                    vm.OpenEditor(path);
                } else if (check.Error != null) {
                    vm.ShowCopiedMessage(check.Error);
                }
                e.Handled = true;
                return;
            }
        }
```

- [ ] **Añadir manejo de Esc para el overlay de cambios sin guardar** — en `OnKeyDown`, al inicio del `case Key.Escape:`, ANTES de `if (vm.IsOptionsMenuOpen)`:

```csharp
            case Key.Escape:
                if (vm.IsEditorOpen && vm.EditorPanel.ShowUnsavedDialog) {
                    vm.EditorPanel.CancelUnsavedDialog();
                    e.Handled = true;
                    break;
                }
                // resto del case sin cambios
                if (vm.IsOptionsMenuOpen) {
```

- [ ] **Verificar que compila**

```bash
cd Yottacast && dotnet build 2>&1 | tail -10
```

- [ ] **Commit**

```bash
git add Yottacast/Views/MainWindow.axaml.cs
git commit -m "feat(editor): handle Cmd+E and Esc overlay in MainWindow"
```

---

## Task 9: UserDocumentSearch — acción MetaE en ficheros editables

**Files:**
- Modify: `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs`

- [ ] **Cambiar la construcción de `Actions`** para añadir condicionalmente la acción MetaE

Localizar el bloque donde se construye el `ResultItemViewModel` (línea ~165):
```csharp
                        buffer.Add(new ResultItemViewModel {
                            ...
                            Actions = [
                                new() { /* Open */ ... },
                                new() { /* Copy path */ ... },
                            ],
                        });
```

Reemplazar el bloque completo de construcción del `ResultItemViewModel` y su `Actions` por este patrón (reemplazar solo la parte de `Actions`, manteniendo el resto sin cambios):

```csharp
                        var actions = new List<ResultAction> {
                            new() {
                                Label         = "Open",
                                LabelProvider = () => {
                                    var appName = _appNameByExtension.GetValueOrDefault(ext);
                                    return appName != null ? $"Open in {appName}" : "Open";
                                },
                                Hotkey       = ActionHotkey.Enter,
                                ShowInFooter = true,
                                ShowInMenu   = true,
                                ClosesMenu   = true,
                                ClosesWindow = true,
                                Execute      = () => {
                                    logger.LogInformation("DocSearch: open \"{Path}\"", path);
                                    platform.LaunchApp(path);
                                },
                            },
                            new() {
                                Label        = "Copy path",
                                Hotkey       = ActionHotkey.MetaC,
                                ShowInFooter = true,
                                ShowInMenu   = true,
                                ClosesMenu   = true,
                                HintProvider = () => "Path copied!",
                                Execute      = () => clipboard.CopyText(path),
                            },
                        };
                        if (IsEditableExtension(path))
                            actions.Add(new ResultAction {
                                Label        = "Edit",
                                Hotkey       = ActionHotkey.MetaE,
                                ShowInFooter = true,
                                ShowInMenu   = false,
                                Execute      = () => { },
                            });
                        buffer.Add(new ResultItemViewModel {
                            IconBytes        = fileIconCache.Get(r.Path),
                            BadgeIconBytes   = _badgeByExtension.GetValueOrDefault(ext),
                            Title            = r.Name,
                            Subtitle         = r.Path,
                            ItemPath         = r.Path,
                            Category         = "Files",
                            Score            = score * 3.5,
                            ScoreReason      = scoreReason,
                            TitleRanges      = titleRanges,
                            SubtitleRanges   = subtitleRanges,
                            GetDragPayload   = () => new DragPayload.File(path),
                            Actions          = actions,
                        });
```

Nota: este cambio extrae la construcción del objeto `ResultItemViewModel` del bloque anidado. El `buffer.Add(...)` existente debe ser reemplazado completamente por este bloque.

- [ ] **Añadir el método privado `IsEditableExtension`** al final de `UserDocumentSearch`, antes del `}` de cierre de la clase:

```csharp
    private bool IsEditableExtension(string filePath) {
        if (!settings.EnableFileEditor) return false;
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        return !string.IsNullOrEmpty(ext)
            && settings.FileEditorExtensions.Any(e =>
                e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }
```

- [ ] **Verificar que compila**

```bash
cd Yottacast.Core && dotnet build 2>&1 | tail -5
```

- [ ] **Commit**

```bash
git add Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs
git commit -m "feat(editor): add Cmd+E hint action to editable file results"
```

---

## Task 10: Settings — sección FileEditor

**Files:**
- Modify: `Yottacast/ViewModels/SettingsWindowViewModel.cs`
- Modify: `Yottacast/Views/SettingsWindow.axaml`

### SettingsWindowViewModel

- [ ] **Añadir `FileEditor` al enum `SettingsSection`**

```csharp
public enum SettingsSection {
    General, AppSearch, WebSearch, FileSearch, FileEditor, Calculator, Clipboard, Emoji, Dictionary, DateSearch, History, Permissions
}
```

- [ ] **Añadir `[NotifyPropertyChangedFor]` para `IsFileEditorSelected`** — en el `[ObservableProperty]` de `_selectedSection` (añadir junto a los demás):

```csharp
    [NotifyPropertyChangedFor(nameof(IsFileEditorSelected))]
```

- [ ] **Añadir propiedad `IsFileEditorSelected`** — junto a las demás `Is*Selected`:

```csharp
    public bool IsFileEditorSelected => SelectedSection == SettingsSection.FileEditor;
```

- [ ] **Añadir command `SelectFileEditor`** — junto a los demás `[RelayCommand]`:

```csharp
    [RelayCommand] private void SelectFileEditor() => SelectedSection = SettingsSection.FileEditor;
```

- [ ] **Añadir propiedades binding** — junto a las demás propiedades que delegan en `_settings`:

```csharp
    public bool EnableFileEditor {
        get => _settings.EnableFileEditor;
        set { _settings.EnableFileEditor = value; _settings.Save(); OnPropertyChanged(); }
    }

    public bool FileEditorAutoSave {
        get => _settings.FileEditorAutoSave;
        set { _settings.FileEditorAutoSave = value; _settings.Save(); OnPropertyChanged(); }
    }
```

(Nota: verificar cómo se accede a `_settings` en este ViewModel — puede ser un campo o una propiedad inyectada por constructor. Buscar `_settings` en el fichero y seguir el mismo patrón que usa para `EnableFileSearch`.)

### SettingsWindow.axaml

- [ ] **Añadir botón de navegación** — en la barra lateral, después del botón de FileSearch (buscar `SelectFileSearchCommand`) y antes del siguiente botón:

```xml
<Button Classes="nav-btn"
        Classes.nav-selected="{Binding IsFileEditorSelected}"
        Command="{Binding SelectFileEditorCommand}">
    <StackPanel Orientation="Horizontal" Spacing="6">
        <PathIcon Data="M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zm17.71-10.21a1 1 0 0 0 0-1.41l-2.34-2.34a1 1 0 0 0-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z"
                  Width="14" Height="14"
                  VerticalAlignment="Center"/>
        <TextBlock Text="Editor"/>
    </StackPanel>
</Button>
```

- [ ] **Añadir sección del panel de contenido** — después del `</StackPanel>` de cierre de la sección "File Search" (buscar `<!-- Calculator -->`) e INMEDIATAMENTE antes de él:

```xml
<!-- File Editor -->
<StackPanel Spacing="16" IsVisible="{Binding IsFileEditorSelected}">
    <TextBlock Classes="section-heading" Text="File Editor"/>

    <ToggleSwitch IsChecked="{Binding EnableFileEditor}"
                  OnContent="Enabled"
                  OffContent="Disabled"/>

    <TextBlock Classes="description"
               Text="Open text and code files directly from search results with ⌘E."/>

    <StackPanel Spacing="12" IsVisible="{Binding EnableFileEditor}">
        <CheckBox Content="Auto-save on close"
                  IsChecked="{Binding FileEditorAutoSave}"/>
        <TextBlock Classes="description"
                   Text="If disabled, a save button (⌘S) appears in the editor header."/>
    </StackPanel>
</StackPanel>
```

- [ ] **Verificar que compila**

```bash
cd Yottacast && dotnet build 2>&1 | tail -10
```

- [ ] **Commit**

```bash
git add Yottacast/ViewModels/SettingsWindowViewModel.cs Yottacast/Views/SettingsWindow.axaml
git commit -m "feat(editor): add FileEditor section in Settings"
```

---

## Task 11: DI — registro y wiring

**Files:**
- Modify: `Yottacast/App.axaml.cs`

- [ ] **Registrar `FileEditorService` en el contenedor DI** — después de `services.AddSingleton<FileSearch>()` (línea ~248):

```csharp
services.AddSingleton<FileEditorService>();
```

- [ ] **Verificar que `MainWindow` y `MainWindowViewModel` se resuelven correctamente**

Los constructores actualizados (`MainWindow` con `FileEditorService`, `MainWindowViewModel` con `FileEditorService`) deben resolverse automáticamente por DI siempre que `FileEditorService` esté registrado.

Buscar en `App.axaml.cs` cómo se construyen `MainWindow` y `MainWindowViewModel`:
```bash
grep -n "MainWindow\|MainWindowViewModel" Yottacast/App.axaml.cs | head -15
```
Si se construyen via `services.AddSingleton<MainWindow>()` o `services.GetRequiredService<MainWindow>()`, no hay nada más que hacer — DI inyecta `FileEditorService` automáticamente.

- [ ] **Verificar que toda la solución compila sin errores**

```bash
cd /Users/avilches/Work/Proy/Other/Yottacast && dotnet build 2>&1 | tail -15
```
Expected: Build succeeded, 0 Error(s).

- [ ] **Ejecutar todos los tests**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -15
```
Expected: todos los tests PASS.

```bash
cd ../Yottacast.Ipc.Tests && dotnet test 2>&1 | tail -10
```
Expected: todos los tests PASS.

- [ ] **Commit**

```bash
git add Yottacast/App.axaml.cs
git commit -m "feat(editor): register FileEditorService in DI"
```

---

## Task 12: Cmd+S en editor (guardar sin cerrar)

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml.cs`

Cmd+S debe guardar sin cerrar cuando el editor está abierto y autosave está desactivado.

- [ ] **Añadir handling de Cmd+S** — en `OnTunnelKeyDown`, dentro del bloque Cmd+E (o justo después), añadir:

```csharp
        // ── Cmd+S: guardar desde el editor ──────────────────────────────────────
        if (vm.IsEditorOpen
            && !vm.EditorPanel.IsAutoSave
            && AppHandler.Instance.MatchesHotkey(e, ActionHotkey.MetaS)) {
            vm.EditorPanel.SaveFile();
            vm.ShowCopiedMessage("Guardado");
            e.Handled = true;
            return;
        }
```

- [ ] **Verificar que compila**

```bash
cd Yottacast && dotnet build 2>&1 | tail -5
```

- [ ] **Commit final**

```bash
git add Yottacast/Views/MainWindow.axaml.cs
git commit -m "feat(editor): add Cmd+S to save without closing"
```

---

## Verificación final

- [ ] **Run todos los tests**

```bash
cd /Users/avilches/Work/Proy/Other/Yottacast
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
cd ../Yottacast.Ipc.Tests && dotnet test 2>&1 | tail -10
```

- [ ] **Commit del spec y plan**

```bash
git add docs/superpowers/specs/2026-05-24-file-editor-design.md docs/superpowers/plans/2026-05-24-file-editor.md
git commit -m "docs: add file editor spec and implementation plan"
```

---

## Notas de implementación

**ViewModelBase**: en `Yottacast.Core/ViewModels/EditorPanelViewModel.cs`, verificar cuál es la clase base correcta. Buscar `class.*ViewModelBase` en el proyecto para confirmar el nombre y namespace exactos.

**Acceso a `_settings` en `SettingsWindowViewModel`**: buscar `EnableFileSearch` en el fichero para ver cómo accede a `_settings` y replicar el mismo patrón.

**AvaloniaEdit versión**: si `dotnet add package AvaloniaEdit` instala una versión != 11.3.x, ajustar los namespaces del AXAML si es necesario (el namespace `AvaloniaEdit` es estándar y no debería cambiar).

**`IsDirty` en overlay**: ya está cubierto en Task 4 con `partial void OnContentChanged`.
