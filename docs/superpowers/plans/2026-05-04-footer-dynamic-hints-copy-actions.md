# Footer dinámico, acciones de copia y shortcut Settings — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rediseñar el footer de la ventana principal con hints dinámicos por tipo de resultado, añadir Cmd+C a todos los tipos de resultado (sin cerrar la ventana), mostrar un hint de feedback al copiar, y cambiar el shortcut de Settings a Cmd+;.

**Architecture:** Se añade `CopiedMessage: string?` a `BaseResultItemViewModel` para que cada fuente declare su propio mensaje. `MainWindowViewModel` expone `FooterHints: IReadOnlyList<string>` como propiedad computed derivada del tipo de `SelectedResult`, y `ShowCopiedMessage` con timer 1.5 s. El handler de Cmd+C en `MainWindow.axaml.cs` deja de cerrar la ventana. El footer pasa a ser siempre visible con el botón de Settings a la izquierda.

**Tech Stack:** Avalonia 11, .NET 9, CommunityToolkit.Mvvm, xUnit

**Spec:** `docs/superpowers/specs/2026-05-02-footer-dynamic-hints-copy-actions-design.md`

---

## Mapa de archivos

| Archivo | Cambio |
|---|---|
| `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs` | + `CopiedMessage: string?` |
| `Yottacast.Core/Search/Application/ApplicationSearch.cs` | + `ClipboardService`, `OnCopy`, `CopiedMessage` |
| `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs` | + `ClipboardService`, `OnCopy`, `CopiedMessage` |
| `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` | + `OnCopy`, `CopiedMessage` en calc y conversión |
| `Yottacast.Core/Search/Dictionary/DictionarySource.cs` | + `ClipboardService`, `OnCopy`, `CopiedMessage` |
| `Yottacast.Core/Search/Emoji/EmojiSearch.cs` | + `CopiedMessage` |
| `Yottacast/ViewModels/MainWindowViewModel.cs` | + `FooterHints`, `ShowCopiedMessage`; elimina props viejas |
| `Yottacast/Views/MainWindow.axaml` | Rediseño footer |
| `Yottacast/Views/MainWindow.axaml.cs` | Handler Cmd+C sin close, shortcut Cmd+; |
| `Yottacast.Core.Tests/Search/ApplicationSearchTests.cs` | + test OnCopy |
| `Yottacast.Core.Tests/Search/UserDocumentSearchTests.cs` | + test OnCopy |
| `Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs` | + test OnCopy calc |
| `Yottacast.Core.Tests/Search/Calculator/UnitConverterSearchTests.cs` | + test OnCopy conversion |
| `Yottacast.Core.Tests/Search/EmojiSearchTests.cs` | + test CopiedMessage |
| `docs/ui-hotkeys.md` | Cmd+, → Cmd+;, tabla copy |
| `docs/ui-main-window.md` | Footer actualizado |
| `docs/result-viewmodels.md` | + `CopiedMessage` |

---

## Task 1: Añadir `CopiedMessage` a `BaseResultItemViewModel`

**Files:**
- Modify: `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs`

- [ ] **Añadir la propiedad**

Abrir `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs`. Añadir después de `OnCopy`:

```csharp
public string? CopiedMessage { get; init; }
```

- [ ] **Compilar para verificar**

```bash
cd "Yottacast.Core" && dotnet build -q
```
Expected: Build succeeded, 0 errors.

- [ ] **Commit**

```bash
git add Yottacast.Core/ViewModels/BaseResultItemViewModel.cs
git commit -m "feat: add CopiedMessage to BaseResultItemViewModel"
```

---

## Task 2: `ApplicationSearch` — inject ClipboardService y OnCopy

**Files:**
- Modify: `Yottacast.Core/Search/Application/ApplicationSearch.cs`
- Test: `Yottacast.Core.Tests/Search/ApplicationSearchTests.cs`

- [ ] **Escribir test que falla**

En `ApplicationSearchTests.cs`, el helper `BuildSearch` y `BuildSearchWithSettings` necesitan `ClipboardService`. Añadir al final del archivo:

```csharp
[Fact]
public async Task CreateResultItem_HasOnCopyAndCopiedMessage() {
    var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
    var platform = new FakePlatformProviderWithApps(["/Applications/Safari.app"]);
    var settings = UserSettings.Load(platform);
    var iconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
    var search = new ApplicationSearch(settings, platform, iconCache, clipboard, NullLogger<ApplicationSearch>.Instance);
    await StartAndWaitAsync(search);
    var results = SearchAll(search, "safari");
    var item = Assert.Single(results);
    Assert.NotNull(item.OnCopy);
    Assert.Equal("Path copied!", item.CopiedMessage);
}
```

- [ ] **Ejecutar test para verificar que falla**

```bash
cd "Yottacast.Core.Tests" && dotnet test --filter "CreateResultItem_HasOnCopyAndCopiedMessage" -v
```
Expected: FAIL — `ApplicationSearch` constructor no acepta `ClipboardService`.

- [ ] **Añadir `ClipboardService` al constructor de `ApplicationSearch`**

En `ApplicationSearch.cs`, cambiar el constructor de:
```csharp
public sealed class ApplicationSearch(
    UserSettings settings,
    PlatformProvider platform,
    AppIconCache iconCache,
    ILogger<ApplicationSearch> logger)
```
a:
```csharp
public sealed class ApplicationSearch(
    UserSettings settings,
    PlatformProvider platform,
    AppIconCache iconCache,
    ClipboardService clipboard,
    ILogger<ApplicationSearch> logger)
```

- [ ] **Añadir `OnCopy` y `CopiedMessage` en `CreateResultItem`**

En `CreateResultItem()`, cambiar de:
```csharp
public ResultItemViewModel CreateResultItem(AppInfo app, double score = 1.0) => new() {
    Icon = "📱",
    IconBytes = iconCache.Get(app.Path),
    Title = app.Name,
    Subtitle = app.Path,
    Category = "Application",
    Score = score,
    OnActivate = () => platform.LaunchApp(app.Path),
};
```
a:
```csharp
public ResultItemViewModel CreateResultItem(AppInfo app, double score = 1.0) {
    var path = app.Path;
    return new() {
        Icon = "📱",
        IconBytes = iconCache.Get(app.Path),
        Title = app.Name,
        Subtitle = app.Path,
        Category = "Application",
        Score = score,
        OnActivate = () => platform.LaunchApp(path),
        OnCopy = () => clipboard.CopyText(path),
        CopiedMessage = "Path copied!",
    };
}
```

- [ ] **Ejecutar test para verificar que pasa**

```bash
cd "Yottacast.Core.Tests" && dotnet test --filter "CreateResultItem_HasOnCopyAndCopiedMessage" -v
```
Expected: PASS.

- [ ] **Ejecutar suite completa para verificar no hay regresiones**

```bash
cd "Yottacast.Core.Tests" && dotnet test -q
```
Expected: todos los tests pasan.

- [ ] **Commit**

```bash
git add Yottacast.Core/Search/Application/ApplicationSearch.cs \
        Yottacast.Core.Tests/Search/ApplicationSearchTests.cs
git commit -m "feat: ApplicationSearch — Cmd+C copies app path"
```

---

## Task 3: `UserDocumentSearch` — inject ClipboardService y OnCopy

**Files:**
- Modify: `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs`
- Test: `Yottacast.Core.Tests/Search/UserDocumentSearchTests.cs`

- [ ] **Escribir test que falla**

En `UserDocumentSearchTests.cs`, añadir un segundo overload de `BuildSearch` que acepta `ClipboardService` (el existente sin clipboard queda intacto para no romper los tests actuales):

```csharp
private static UserDocumentSearch BuildSearch(ClipboardService clipboard, params FileResult[] files) {
    var platform = new FakePlatformProvider(files);
    var settings = UserSettings.Load(platform);
    var fileSearch = new FileSearch(platform);
    var fileIconCache = new FileIconCache(platform, NullLogger<FileIconCache>.Instance);
    return new UserDocumentSearch(settings, fileSearch, fileIconCache, platform, NullLogger<UserDocumentSearch>.Instance, clipboard);
}
```

Añadir al final del archivo:

```csharp
[Fact]
public async Task Results_HaveOnCopyAndCopiedMessage() {
    string? copied = null;
    var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
    clipboard.Initialize(t => copied = t);
    var search = BuildSearch(clipboard, new FileResult("report.pdf", "/docs/report.pdf"));
    var results = await SearchAllAsync(search, "report");
    var item = Assert.Single(results);
    Assert.NotNull(item.OnCopy);
    Assert.Equal("Path copied!", item.CopiedMessage);
    item.OnCopy!();
    Assert.Equal("/docs/report.pdf", copied);
}
```

- [ ] **Ejecutar test para verificar que falla**

```bash
cd "Yottacast.Core.Tests" && dotnet test --filter "Results_HaveOnCopyAndCopiedMessage" --filter "UserDocument" -v
```
Expected: FAIL — constructor no acepta `ClipboardService`.

- [ ] **Añadir `ClipboardService` al constructor de `UserDocumentSearch`**

Cambiar:
```csharp
public class UserDocumentSearch(
    UserSettings settings,
    FileSearch fileSearch,
    FileIconCache fileIconCache,
    PlatformProvider platform,
    ILogger<UserDocumentSearch> logger,
    int timeoutMs = AppDefaults.FileSearchTimeoutMs) : IDeferredSearchSource {
```
a:
```csharp
public class UserDocumentSearch(
    UserSettings settings,
    FileSearch fileSearch,
    FileIconCache fileIconCache,
    PlatformProvider platform,
    ILogger<UserDocumentSearch> logger,
    ClipboardService? clipboard = null,
    int timeoutMs = AppDefaults.FileSearchTimeoutMs) : IDeferredSearchSource {
```

> El parámetro `clipboard` es nullable con default `null` para que la inyección de DI siga funcionando sin cambios en `App.axaml.cs`. Nota: DI inyecta por tipo, no por posición, así que el orden con el `int` al final no da problema en DI — pero en los tests del helper sí importa el orden que definamos.

> **Alternativa más limpia:** añadir `ClipboardService` como parámetro requerido (sin default) y actualizar `App.axaml.cs`. Dado que `App.axaml.cs` usa `services.AddSingleton<UserDocumentSearch>()` con DI automático, el contenedor inyecta `ClipboardService` automáticamente si está registrado. Usar esta alternativa — eliminar `= null` y poner el parámetro antes de `int timeoutMs`.

Constructor final:
```csharp
public class UserDocumentSearch(
    UserSettings settings,
    FileSearch fileSearch,
    FileIconCache fileIconCache,
    PlatformProvider platform,
    ILogger<UserDocumentSearch> logger,
    ClipboardService clipboard,
    int timeoutMs = AppDefaults.FileSearchTimeoutMs) : IDeferredSearchSource {
```

- [ ] **Añadir `OnCopy` y `CopiedMessage` al resultado en `SearchAsync`**

En el bloque donde se crea `ResultItemViewModel` (alrededor de línea 123), añadir:
```csharp
buffer.Add(new ResultItemViewModel {
    IconBytes = fileIconCache.Get(r.Path),
    BadgeIconBytes = _badgeByExtension.GetValueOrDefault(ext),
    Title = r.Name,
    Subtitle = r.Path,
    Category = "Files",
    Score = score,
    OnActivate = () => {
        logger.LogInformation("DocSearch: open \"{Path}\"", path);
        platform.LaunchApp(path);
    },
    OnCopy = () => clipboard.CopyText(path),
    CopiedMessage = "Path copied!",
});
```

- [ ] **Ejecutar tests**

```bash
cd "Yottacast.Core.Tests" && dotnet test -q
```
Expected: todos los tests pasan.

- [ ] **Commit**

```bash
git add Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs \
        Yottacast.Core.Tests/Search/UserDocumentSearchTests.cs
git commit -m "feat: UserDocumentSearch — Cmd+C copies file path"
```

---

## Task 4: `CalculatorSearch` — OnCopy para resultado y conversión

**Files:**
- Modify: `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`
- Test: `Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs`
- Test: `Yottacast.Core.Tests/Search/Calculator/UnitConverterSearchTests.cs`

- [ ] **Escribir test de calculadora que falla**

En `CalculatorSearchTests.cs`, añadir:
```csharp
[Fact]
public void CalculatorResult_HasOnCopyAndCopiedMessage() {
    var search = BuildSearch(out var clipboard);
    var result = StandardResult(search, "2+2");
    Assert.NotNull(result.OnCopy);
    Assert.Equal("Result copied!", result.CopiedMessage);
}
```

- [ ] **Escribir test de conversión que falla**

En `UnitConverterSearchTests.cs`, añadir:
```csharp
[Fact]
public void ConversionResult_HasOnCopyAndCopiedMessage() {
    var search = BuildSearch(out _);
    var results = search.Search("5 km to miles", 5);
    var item = Assert.Single(results.OfType<ConversionResultItemViewModel>());
    Assert.NotNull(item.OnCopy);
    Assert.Equal("Result copied!", item.CopiedMessage);
}
```

> Para ver cómo se construye `BuildSearch` en `UnitConverterSearchTests.cs`, verificar que el método existe — si no, copiar el patrón de `CalculatorSearchTests.cs`.

- [ ] **Ejecutar tests para verificar que fallan**

```bash
cd "Yottacast.Core.Tests" && dotnet test --filter "HasOnCopyAndCopiedMessage" -v
```
Expected: 2 FAILs.

- [ ] **Añadir `OnCopy` y `CopiedMessage` al `CalculatorResultItemViewModel`**

En `CalculatorSearch.cs`, en el bloque `case CalcResult r when r.RawValue != q:` (alrededor de línea 100):
```csharp
return [new CalculatorResultItemViewModel {
    Icon = "🧮",
    Title = r.RawValue,
    TitleLong = titleLong,
    Subtitle = subtitle,
    Category = "Calculator",
    Score = 4,
    OnActivate = () => {
        logger.LogInformation("Calculator: copied result \"{Value}\"", captured);
        clipboard.CopyText(captured);
    },
    OnCopy = () => {
        logger.LogInformation("Calculator: copied result via Cmd+C \"{Value}\"", captured);
        clipboard.CopyText(captured);
    },
    CopiedMessage = "Result copied!",
}];
```

- [ ] **Añadir `OnCopy` y `CopiedMessage` al `ConversionResultItemViewModel`**

En el bloque que crea `ConversionResultItemViewModel` (alrededor de línea 67), añadir después de las propiedades existentes:
```csharp
OnCopy = () => {
    var copied = vm.SelectedCell switch {
        ConversionCell.NormFrom => capturedNorm ?? capturedTo,
        _                       => capturedTo,
    };
    logger.LogInformation("Calculator: copied conversion via Cmd+C \"{Value}\"", copied);
    clipboard.CopyText(copied);
},
CopiedMessage = "Result copied!",
```

- [ ] **Ejecutar tests**

```bash
cd "Yottacast.Core.Tests" && dotnet test -q
```
Expected: todos los tests pasan.

- [ ] **Commit**

```bash
git add Yottacast.Core/Search/Calculator/CalculatorSearch.cs \
        Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs \
        Yottacast.Core.Tests/Search/Calculator/UnitConverterSearchTests.cs
git commit -m "feat: CalculatorSearch — Cmd+C copies result"
```

---

## Task 5: `EmojiSearch` — añadir `CopiedMessage`

**Files:**
- Modify: `Yottacast.Core/Search/Emoji/EmojiSearch.cs`
- Test: `Yottacast.Core.Tests/Search/EmojiSearchTests.cs`

- [ ] **Escribir test que falla**

En `EmojiSearchTests.cs`, añadir:
```csharp
[Fact]
public async Task OnCopy_HasCopiedMessage() {
    var json = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";
    var search = await BuildSearchWithCache(json);
    var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();
    Assert.Equal("Emoji copied!", grid.CopiedMessage);
}
```

- [ ] **Ejecutar para verificar que falla**

```bash
cd "Yottacast.Core.Tests" && dotnet test --filter "OnCopy_HasCopiedMessage" -v
```
Expected: FAIL.

- [ ] **Añadir `CopiedMessage` al `EmojiGridResultViewModel` en `EmojiSearch.cs`**

Buscar donde se crea `EmojiGridResultViewModel` (en `MakeGrid` o similar). Añadir:
```csharp
CopiedMessage = "Emoji copied!",
```

- [ ] **Ejecutar tests**

```bash
cd "Yottacast.Core.Tests" && dotnet test -q
```
Expected: todos pasan.

- [ ] **Commit**

```bash
git add Yottacast.Core/Search/Emoji/EmojiSearch.cs \
        Yottacast.Core.Tests/Search/EmojiSearchTests.cs
git commit -m "feat: EmojiSearch — add CopiedMessage"
```

---

## Task 6: `DictionarySource` — inject ClipboardService y OnCopy

**Files:**
- Modify: `Yottacast.Core/Search/Dictionary/DictionarySource.cs`

No hay test de integración de `DictionarySource` — la verificación es end-to-end.

- [ ] **Añadir `ClipboardService` al constructor**

Cambiar:
```csharp
public class DictionarySource(
    UserSettings settings,
    BrowserDiscovery browserDiscovery,
    ILogger<DictionarySource> logger) : IDeferredSearchSource {
```
a:
```csharp
public class DictionarySource(
    UserSettings settings,
    BrowserDiscovery browserDiscovery,
    ClipboardService clipboard,
    ILogger<DictionarySource> logger) : IDeferredSearchSource {
```

- [ ] **Añadir `OnCopy` y `CopiedMessage` al resultado local**

En el bloque que crea `DictionaryResultViewModel` para resultados locales (alrededor de línea 103):
```csharp
var capturedDef = defs[0].Definition;
results.Add(new DictionaryResultViewModel {
    IconBytes = IconBytes,
    Word = searchWord,
    Language = multiLang ? langName : null,
    Definitions = defs,
    Score = score,
    BypassLimit = true,
    OnActivate = () => {
        var browser = settings.ActiveBrowser;
        if (browser is not null)
            browserDiscovery.OpenUrl(capturedUrl, browser);
    },
    OnCopy = () => clipboard.CopyText(capturedDef),
    CopiedMessage = "Definition copied!",
});
```

- [ ] **Añadir `OnCopy` y `CopiedMessage` al resultado API (bloque similar más abajo)**

Buscar el segundo lugar donde se añade `DictionaryResultViewModel` (fallback API, alrededor de línea 164). Aplicar el mismo patrón — capturar la primera definición y añadir `OnCopy` y `CopiedMessage = "Definition copied!"`.

- [ ] **Compilar para verificar**

```bash
cd "Yottacast.Core" && dotnet build -q
```
Expected: 0 errores.

- [ ] **Ejecutar suite completa**

```bash
cd "Yottacast.Core.Tests" && dotnet test -q
```
Expected: todos pasan.

- [ ] **Commit**

```bash
git add Yottacast.Core/Search/Dictionary/DictionarySource.cs
git commit -m "feat: DictionarySource — Cmd+C copies first definition"
```

---

## Task 7: `MainWindowViewModel` — FooterHints y ShowCopiedMessage

**Files:**
- Modify: `Yottacast/ViewModels/MainWindowViewModel.cs`

- [ ] **Eliminar propiedades obsoletas**

Eliminar las tres líneas:
```csharp
public string EmojiCopyShortcut  => $"{MetaSymbol}C  copy";
public string EmojiFavShortcut   => $"{MetaSymbol}{ShiftSymbol}F  fav";
```
Y eliminar:
```csharp
public int DisplayResultCount =>
    Results.OfType<EmojiGridResultViewModel>().FirstOrDefault()?.Cells.Count
    ?? Results.Count;
```
Y eliminar las llamadas a `OnPropertyChanged(nameof(DisplayResultCount))` (buscar y eliminar las dos ocurrencias).

- [ ] **Añadir `SettingsShortcutText`**

Añadir junto a `MetaSymbol` y `ShiftSymbol`:
```csharp
public string SettingsShortcutText => $"{MetaSymbol};  settings";
```

- [ ] **Añadir `FooterHints`**

```csharp
public IReadOnlyList<string> FooterHints => SelectedResult switch {
    EmojiGridResultViewModel =>
        [$"{MetaSymbol}C  copy", "↵  paste", $"{MetaSymbol}{ShiftSymbol}F  fav", "Esc  clear"],
    CalculatorResultItemViewModel or ConversionResultItemViewModel =>
        ["↵  copy", $"{MetaSymbol}C  copy", "Esc  clear"],
    DictionaryResultViewModel =>
        ["↵  open", $"{MetaSymbol}C  definition", "Esc  clear"],
    ResultItemViewModel { OnCopy: not null } =>
        ["↵  open", $"{MetaSymbol}C  path", "Esc  clear"],
    ResultItemViewModel =>
        ["↵  open", "Esc  clear"],
    _ =>
        ["Esc  clear"],
};
```

- [ ] **Actualizar `OnSelectedResultChanged` para notificar `FooterHints`**

```csharp
partial void OnSelectedResultChanged(BaseResultItemViewModel? value) {
    OnPropertyChanged(nameof(IsEmojiMode));
    OnPropertyChanged(nameof(FooterHints));
}
```

- [ ] **Añadir `ShowCopiedMessage`**

Añadir campo y método:
```csharp
private CancellationTokenSource? _copiedMsgCts;

public void ShowCopiedMessage(string msg) {
    _copiedMsgCts?.Cancel();
    _copiedMsgCts = new CancellationTokenSource();
    SearchHint = msg;
    _ = ClearCopiedMessageAsync(msg, _copiedMsgCts.Token);
}

private async Task ClearCopiedMessageAsync(string msg, CancellationToken ct) {
    try {
        await Task.Delay(1500, ct);
        if (SearchHint == msg) SearchHint = null;
    } catch (OperationCanceledException) { }
}
```

- [ ] **Compilar**

```bash
cd "Yottacast" && dotnet build -q
```
Expected: 0 errores. Si el compilador se queja de referencias a `EmojiCopyShortcut`, `EmojiFavShortcut` o `DisplayResultCount` en algún AXAML, es correcto — se arreglará en la siguiente tarea.

- [ ] **Commit**

```bash
git add Yottacast/ViewModels/MainWindowViewModel.cs
git commit -m "feat: MainWindowViewModel — FooterHints + ShowCopiedMessage"
```

---

## Task 8: `MainWindow.axaml` — rediseño del footer

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml`

- [ ] **Hacer el footer siempre visible**

Localizar el `Border` con `<!-- ── Footer ── -->` (alrededor de línea 386). Eliminar `IsVisible="{Binding HasResults}"` de ese Border.

- [ ] **Reemplazar el contenido del Grid del footer**

Reemplazar el `<Grid ColumnDefinitions="*,Auto">` completo y todo su contenido (las dos columnas con el count y los StackPanels de hints) con:

```xaml
<Grid ColumnDefinitions="Auto,*">
    <!-- Izquierda: botón de Settings -->
    <Button Grid.Column="0"
            Click="OnSettingsButtonClick"
            Background="Transparent"
            BorderThickness="0"
            Padding="0"
            Cursor="Hand"
            VerticalAlignment="Center">
        <StackPanel Orientation="Horizontal" Spacing="6">
            <TextBlock Text="⚙"
                       Foreground="{DynamicResource Theme.Footer.Color}"
                       FontSize="{DynamicResource Theme.Footer.Size}"/>
            <TextBlock Text="{Binding SettingsShortcutText}"
                       Foreground="{DynamicResource Theme.Footer.Color}"
                       FontSize="{DynamicResource Theme.Footer.Size}"/>
        </StackPanel>
    </Button>

    <!-- Derecha: hints dinámicos -->
    <ItemsControl Grid.Column="1"
                  ItemsSource="{Binding FooterHints}"
                  IsVisible="{Binding HasResults}"
                  HorizontalAlignment="Right"
                  VerticalAlignment="Center">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <StackPanel Orientation="Horizontal" Spacing="16"/>
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate x:CompileBindings="False">
                <TextBlock Text="{Binding}"
                           Foreground="{DynamicResource Theme.Footer.Color}"
                           FontSize="{DynamicResource Theme.Footer.Size}"/>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</Grid>
```

- [ ] **Añadir el handler del botón en el code-behind (`MainWindow.axaml.cs`)**

Añadir al final de la clase (antes del último `}`):
```csharp
private void OnSettingsButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
    (Application.Current as App)?.OpenSettings();
}
```

- [ ] **Compilar**

```bash
cd "Yottacast" && dotnet build -q
```
Expected: 0 errores.

- [ ] **Commit**

```bash
git add Yottacast/Views/MainWindow.axaml Yottacast/Views/MainWindow.axaml.cs
git commit -m "feat: footer rediseñado — hints dinámicos + botón Settings"
```

---

## Task 9: `MainWindow.axaml.cs` — handler Cmd+C sin close y shortcut Cmd+;

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml.cs`

- [ ] **Actualizar el handler de Cmd+C para no cerrar la ventana**

Localizar el bloque del handler Cmd+C en `OnTunnelKeyDown` (alrededor de línea 281):

```csharp
// ANTES:
if (e.Key == copyKey && e.KeyModifiers == copyMods && vm.SelectedResult is { OnCopy: { } copyAction }) {
    copyAction();
    vm.CleanAndSaveHistory("Copy");
    Hide();
    AppHandler.Instance.OnHide();
    e.Handled = true;
    return;
}
```

Reemplazar con:
```csharp
if (e.Key == copyKey && e.KeyModifiers == copyMods && vm.SelectedResult is { OnCopy: { } copyAction } result) {
    copyAction();
    if (result.CopiedMessage is { } msg)
        vm.ShowCopiedMessage(msg);
    e.Handled = true;
    return;
}
```

- [ ] **Cambiar el shortcut de Settings de Cmd+, a Cmd+;**

Localizar (alrededor de línea 398):
```csharp
case Key.OemComma when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
    (Application.Current as App)?.OpenSettings();
    e.Handled = true;
    break;
```

Cambiar `Key.OemComma` por `Key.OemSemicolon`:
```csharp
case Key.OemSemicolon when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
    (Application.Current as App)?.OpenSettings();
    e.Handled = true;
    break;
```

- [ ] **Compilar**

```bash
cd "Yottacast" && dotnet build -q
```
Expected: 0 errores.

- [ ] **Ejecutar todos los tests**

```bash
cd "Yottacast.Core.Tests" && dotnet test -q
```
Expected: todos pasan.

- [ ] **Commit**

```bash
git add Yottacast/Views/MainWindow.axaml.cs
git commit -m "feat: Cmd+C sin close, shortcut Settings → Cmd+;"
```

---

## Task 10: Actualizar documentación

**Files:**
- Modify: `docs/ui-hotkeys.md`
- Modify: `docs/ui-main-window.md`
- Modify: `docs/result-viewmodels.md`

- [ ] **`docs/ui-hotkeys.md`** — sección 7

Buscar la sección "## 7. Abrir preferencias (Cmd+,)". Cambiar título y contenido:
- Título: `## 7. Abrir preferencias (Cmd+;)`
- Texto: sustituir `Cmd+,` por `Cmd+;` y `Key.OemComma` por `Key.OemSemicolon`
- En la tabla al final del archivo, cambiar `Cmd+, (macOS)` por `Cmd+; (macOS)`

Añadir nueva sección sobre acciones de copia:

```markdown
## 8. Copiar resultado (Cmd+C)

`Cmd+C` (macOS) / `Ctrl+C` (Windows/Linux) copia el valor del resultado seleccionado **sin cerrar la ventana**. Aparece un mensaje breve en el área de `SearchHint` durante 1.5 s.

| Tipo | Qué copia | Mensaje |
|---|---|---|
| Apps | Path del bundle | "Path copied!" |
| Archivos | Path del fichero | "Path copied!" |
| Calculadora | Resultado numérico | "Result copied!" |
| Conversor | Celda seleccionada | "Result copied!" |
| Diccionario | Primera definición | "Definition copied!" |
| Emoji | El emoji (sin paste) | "Emoji copied!" |

> **Verificar en:** `MainWindow.axaml.cs` (`OnTunnelKeyDown`, handler `CopyShortcut`), `MainWindowViewModel.cs` (`ShowCopiedMessage`).
```

- [ ] **`docs/ui-main-window.md`** — sección footer

Buscar la sección sobre el footer. Actualizar para describir:
- Footer siempre visible
- Izquierda: botón Settings (⚙ + shortcut)
- Derecha: hints dinámicos por tipo de resultado (solo cuando hay resultados)
- Sin contador de resultados

- [ ] **`docs/result-viewmodels.md`** — tabla Base

Añadir en la tabla de propiedades de `BaseResultItemViewModel`:

| `CopiedMessage` | `string?` | Mensaje mostrado en SearchHint tras Cmd+C. Null = no mostrar mensaje |

- [ ] **Commit**

```bash
git add docs/ui-hotkeys.md docs/ui-main-window.md docs/result-viewmodels.md
git commit -m "docs: footer dinámico, Cmd+C, shortcut Cmd+;"
```

---

## Task 11: Verificación end-to-end

- [ ] **Arrancar la app**

```bash
cd "Yottacast" && dotnet run
```

- [ ] **Verificar footer siempre visible**

Sin teclear nada: el footer debe mostrar `⚙ ⌘;  settings` a la izquierda. Los hints de la derecha no se ven (no hay resultados).

- [ ] **Verificar hints por tipo**

| Acción | Hints esperados (derecha) |
|---|---|
| Teclear nombre de una app | `↵ open · ⌘C path · Esc clear` |
| Teclear `2+2` | `↵ copy · ⌘C copy · Esc clear` |
| Teclear `5 km to miles` | `↵ copy · ⌘C copy · Esc clear` |
| Teclear `hello` (diccionario activo) | `↵ open · ⌘C definition · Esc clear` |
| Teclear `:grin` | `⌘C copy · ↵ paste · ⌘⇧F fav · Esc clear` |

- [ ] **Verificar Cmd+C sin close**

Con app seleccionada: Cmd+C → ventana permanece abierta, aparece "Path copied!" debajo del input, desaparece ~1.5 s.

Con calculadora: Cmd+C → ventana permanece abierta, aparece "Result copied!".

Con emoji: Cmd+C → ventana permanece abierta, aparece "Emoji copied!". Enter → ventana se cierra + pega.

- [ ] **Verificar botón Settings**

Click en `⚙ ⌘;  settings` → abre la ventana de Settings. Pulsar `Cmd+;` → mismo resultado. El antiguo `Cmd+,` ya no funciona.

- [ ] **Ejecutar suite de tests final**

```bash
cd "Yottacast.Core.Tests" && dotnet test
```
Expected: todos los tests pasan.