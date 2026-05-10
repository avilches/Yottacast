# Empty State Sources Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce `IEmptyStateSource` — una abstracción DI para lo que se muestra cuando el texto de búsqueda está vacío — con dos implementaciones: `NewlyInstalledAppsSource` (extracción de `MainWindowViewModel`) y `ClipboardSearch` (nueva, detecta URL/path en portapapeles al abrir la ventana).

**Architecture:** Nueva interfaz `IEmptyStateSource` con ciclo de vida idéntico a las search sources. `MainWindowViewModel` inyecta `IEnumerable<IEmptyStateSource>` y delega completamente el estado vacío a estas fuentes. `MainWindow.axaml.cs` lee el portapapeles al abrir y llama `vm.OnWindowShown(text)`. Las fuentes reactivas (`NewlyInstalledAppsSource`) notifican al ViewModel vía `ResultsChanged`.

**Tech Stack:** .NET 9, Avalonia 11.3.12, CommunityToolkit.Mvvm 8.2.1, xUnit

---

## File Map

| Acción | Fichero |
|--------|---------|
| **Crear** | `Yottacast.Core/Search/IEmptyStateSource.cs` |
| **Crear** | `Yottacast.Core/Search/Application/NewlyInstalledAppsSource.cs` |
| **Crear** | `Yottacast.Core/Search/Clipboard/ClipboardSearch.cs` |
| **Modificar** | `Yottacast.Core/Services/ClipboardService.cs` |
| **Modificar** | `Yottacast.Core/ViewModels/MainWindowViewModel.cs` |
| **Modificar** | `Yottacast/Views/MainWindow.axaml.cs` |
| **Modificar** | `Yottacast/App.axaml.cs` |
| **Crear** | `Yottacast.Core.Tests/Search/NewlyInstalledAppsSourceTests.cs` |
| **Crear** | `Yottacast.Core.Tests/Search/ClipboardSearchTests.cs` |

---

## Task 1: Interfaz `IEmptyStateSource`

**Files:**
- Create: `Yottacast.Core/Search/IEmptyStateSource.cs`

- [ ] **Step 1: Crear la interfaz**

```csharp
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public interface IEmptyStateSource
{
    /// <summary>Starts any background activity. Fire-and-forget; never await the return value.</summary>
    void Start();

    /// <summary>Returns a Task that completes once the source is ready to serve results.</summary>
    Task WhenReady();

    Task Stop();

    /// <summary>
    /// Called once each time the window becomes visible with empty search text.
    /// clipboardText is the raw clipboard string read by the View layer; may be null.
    /// </summary>
    void OnWindowShown(string? clipboardText);

    /// <summary>Called when SearchText transitions from empty to non-empty.</summary>
    void OnSearchStarted();

    IReadOnlyList<BaseResultItemViewModel> GetResults();

    /// <summary>
    /// Fired (on any thread) when the result set changes while the window is open.
    /// The ViewModel re-calls GetResults() when this fires, provided SearchText is still empty.
    /// </summary>
    event Action? ResultsChanged;
}
```

- [ ] **Step 2: Commit**

```bash
git add Yottacast.Core/Search/IEmptyStateSource.cs
git commit -m "feat: add IEmptyStateSource interface for empty-state results"
```

---

## Task 2: `NewlyInstalledAppsSource`

Extrae la lógica de `_pendingAppInfos` / `ShowPendingApps()` / `StartTrackingNewAppsAsync()` de `MainWindowViewModel` a una fuente propia.

**Files:**
- Create: `Yottacast.Core/Search/Application/NewlyInstalledAppsSource.cs`
- Create: `Yottacast.Core.Tests/Search/NewlyInstalledAppsSourceTests.cs`

**Nota**: `ApplicationSearch` es `sealed`, por lo que no puede mockearse con Moq/NSubstitute ni sustituirse por un fake. Los tests cubren el comportamiento directamente testeable (`OnWindowShown`, `OnSearchStarted`, `GetResults` inicial). El comportamiento reactivo (AppAdded → ResultsChanged) se verifica en la verificación manual (Task 7).

- [ ] **Step 1: Escribir los tests**

```csharp
// Yottacast.Core.Tests/Search/NewlyInstalledAppsSourceTests.cs
using Yottacast.Core.Search.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Yottacast.Core.Tests.Search;

public class NewlyInstalledAppsSourceTests
{
    // Para construir NewlyInstalledAppsSource necesitamos ApplicationSearch,
    // pero ApplicationSearch es sealed y requiere muchas dependencias.
    // Estos tests validan el contrato observable sin tocar los eventos internos.

    [Fact]
    public void GetResults_Initially_ReturnsEmpty()
    {
        var source = BuildSource(out _);
        Assert.Empty(source.GetResults());
    }

    [Fact]
    public void OnWindowShown_WithAnyInput_IsNoOp()
    {
        var source = BuildSource(out _);
        // Should not throw, should not affect GetResults
        source.OnWindowShown("https://example.com");
        source.OnWindowShown(null);
        Assert.Empty(source.GetResults());
    }

    [Fact]
    public async Task WhenReady_CompletesImmediately()
    {
        var source = BuildSource(out _);
        await source.WhenReady(); // must not hang
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static NewlyInstalledAppsSource BuildSource(out ApplicationSearch appSearch)
    {
        // Buscar si existe un builder de ApplicationSearch en los helpers de test del proyecto.
        // Si no existe, crear uno mínimo aquí o marcar este test como [Skip].
        // ApplicationSearch tiene muchas dependencias — si esto resulta muy pesado,
        // extraer una interfaz IAppNotifications en un refactor posterior.
        throw new NotImplementedException(
            "Implement by wiring a minimal ApplicationSearch or extracting an interface. " +
            "See Task 2 notes in the plan.");
    }
}
```

> **Si crear un `ApplicationSearch` mínimo resulta demasiado pesado** (requiere `UserSettings`, `PlatformProvider`, `AppIconCache`, `ClipboardService`, `ILogger`): verificar si existen builders de test en el proyecto (e.g. `TestBuilders.cs`, `Fixtures/`). Si no existen, simplemente eliminar este fichero de test y verificar `NewlyInstalledAppsSource` exclusivamente mediante la verificación manual del Task 7.

- [ ] **Step 2: Ejecutar los tests para confirmar que fallan o que se puede compilar**

```bash
cd Yottacast.Core.Tests && dotnet build 2>&1 | tail -20
```

Expected: error de compilación (`NewlyInstalledAppsSource` no existe aún).

- [ ] **Step 3: Implementar `NewlyInstalledAppsSource`**

```csharp
// Yottacast.Core/Search/Application/NewlyInstalledAppsSource.cs
using Microsoft.Extensions.Logging;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Application;

/// <summary>
/// IEmptyStateSource that shows apps that were installed while Yottacast was running.
/// Extracted from MainWindowViewModel._pendingAppInfos / ShowPendingApps().
/// </summary>
public class NewlyInstalledAppsSource(
    ApplicationSearch appSearch,
    ILogger<NewlyInstalledAppsSource> logger) : IEmptyStateSource
{
    private readonly List<AppInfo> _pending = [];

    public event Action? ResultsChanged;

    public void Start() => _ = StartAsync();

    private async Task StartAsync()
    {
        await appSearch.WhenReady().ConfigureAwait(false);
        appSearch.AppAdded += OnAppAdded;
        appSearch.IconLoaded += OnIconLoaded;
        logger.LogDebug("NewlyInstalledAppsSource: subscribed to AppAdded");
    }

    public Task WhenReady() => Task.CompletedTask;

    public Task Stop()
    {
        appSearch.AppAdded -= OnAppAdded;
        appSearch.IconLoaded -= OnIconLoaded;
        _pending.Clear();
        return Task.CompletedTask;
    }

    public void OnWindowShown(string? clipboardText) { } // no-op

    public void OnSearchStarted()
    {
        _pending.Clear();
        logger.LogDebug("NewlyInstalledAppsSource: cleared (search started)");
    }

    public IReadOnlyList<BaseResultItemViewModel> GetResults() =>
        _pending.Select(app => appSearch.CreateResultItem(app)).ToList();

    private void OnAppAdded(AppInfo app)
    {
        _pending.Add(app);
        logger.LogDebug("NewlyInstalledAppsSource: app added \"{Name}\"", app.Name);
        ResultsChanged?.Invoke();
    }

    private void OnIconLoaded() => ResultsChanged?.Invoke();
}
```

**Nota sobre el test double**: el test usa `FakeApplicationSearch` en lugar de la clase real para no arrastrar dependencias pesadas. Para que el test compile correctamente, `NewlyInstalledAppsSource` debe ser genérico sobre el tipo de `appSearch`, o los tests usan reflexión. La forma más sencilla es hacer que `NewlyInstalledAppsSource` use una interfaz interna o que el test use la clase real con una instancia mínima.

> **Alternativa pragmática**: si los tests no compilan fácilmente por el acoplamiento a `ApplicationSearch`, simplificar los tests a validar solo el comportamiento de `OnSearchStarted` y `OnWindowShown` usando la clase real con un `ApplicationSearch` mockeado con Moq/NSubstitute (revisar qué framework de mocking existe en `Yottacast.Core.Tests.csproj`).

- [ ] **Step 4: Ejecutar los tests**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "NewlyInstalledAppsSourceTests" -v minimal
```

Expected: todos PASS.

- [ ] **Step 5: Commit**

```bash
git add Yottacast.Core/Search/Application/NewlyInstalledAppsSource.cs \
        Yottacast.Core.Tests/Search/NewlyInstalledAppsSourceTests.cs
git commit -m "feat: extract NewlyInstalledAppsSource from MainWindowViewModel"
```

---

## Task 3: Extender `ClipboardService` con lectura

**Files:**
- Modify: `Yottacast.Core/Services/ClipboardService.cs`

- [ ] **Step 1: Añadir soporte de lectura**

Reemplazar el contenido completo de `ClipboardService.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

/// <summary>
/// Bridge that lets Core code read/write the clipboard without depending on Avalonia.
/// The GUI project calls Initialize() once at startup with Avalonia-backed implementations.
/// </summary>
public class ClipboardService(ILogger<ClipboardService> logger)
{
    private Action<string>? _copy;
    private Func<Task<string?>>? _read;

    public void Initialize(Action<string> copy, Func<Task<string?>> read)
    {
        _copy = copy;
        _read = read;
    }

    public void CopyText(string text) => _copy?.Invoke(text);

    public Task<string?> ReadTextAsync() =>
        _read?.Invoke() ?? Task.FromResult<string?>(null);
}
```

- [ ] **Step 2: Actualizar la inicialización en `App.axaml.cs`**

Localizar (alrededor de la línea 116) el bloque:

```csharp
clipboardService.Initialize(text =>
    Dispatcher.UIThread.InvokeAsync(() => {
        var clipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;
        if (clipboard != null) _ = clipboard.SetTextAsync(text);
    }));
```

Reemplazarlo por:

```csharp
clipboardService.Initialize(
    copy: text =>
        Dispatcher.UIThread.InvokeAsync(() => {
            var clipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;
            if (clipboard != null) _ = clipboard.SetTextAsync(text);
        }),
    read: () =>
        Dispatcher.UIThread.InvokeAsync(async () => {
            var clipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;
            return clipboard != null ? await clipboard.GetTextAsync() : null;
        }));
```

- [ ] **Step 3: Verificar que compila**

```bash
cd Yottacast && dotnet build -v minimal 2>&1 | tail -20
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Yottacast.Core/Services/ClipboardService.cs Yottacast/App.axaml.cs
git commit -m "feat: add read support to ClipboardService"
```

---

## Task 4: `ClipboardSearch`

**Files:**
- Create: `Yottacast.Core/Search/Clipboard/ClipboardSearch.cs`
- Create: `Yottacast.Core.Tests/Search/ClipboardSearchTests.cs`

Antes de escribir los tests, buscar helpers existentes:

```bash
find Yottacast.Core.Tests -name "*.cs" | xargs grep -l "Fake\|Mock\|Stub\|Builder\|Fixture" 2>/dev/null
```

- [ ] **Step 1: Escribir los tests**

Los tests de `ClipboardSearch` usan las funciones estáticas ya existentes (`UrlSearch.TryNormalizeUrl`, `LocalPathSearch.IsLocalPath`) para validar la detección, y un set mínimo de fakes para construir la clase. El `ClipboardSearch` en los tests recibirá dependencias nulas/stub donde no afecten al resultado bajo prueba (la detección URL/path no necesita browser ni favicon para retornar el item).

```csharp
// Yottacast.Core.Tests/Search/ClipboardSearchTests.cs
using Yottacast.Core.Search.Clipboard;
using Yottacast.Core.Search.Url;
using Yottacast.Core.Search.LocalPath;
using Yottacast.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Yottacast.Core.Tests.Search;

public class ClipboardSearchTests
{
    // ── TryNormalizeUrl / IsLocalPath statics (already public) ───────────────

    [Theory]
    [InlineData("https://github.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("www.google.com", true)]
    [InlineData("hello world", false)]
    [InlineData("", false)]
    [InlineData("just text", false)]
    public void TryNormalizeUrl_MatchesExpected(string input, bool expected)
    {
        Assert.Equal(expected, UrlSearch.TryNormalizeUrl(input, out _));
    }

    [Theory]
    [InlineData("/usr/bin", true)]
    [InlineData("~/Documents", true)]
    [InlineData("./relative", true)]
    [InlineData("hello world", false)]
    [InlineData("https://example.com", false)]
    public void IsLocalPath_MatchesExpected(string input, bool expected)
    {
        Assert.Equal(expected, LocalPathSearch.IsLocalPath(input));
    }

    // ── ClipboardSearch integration ───────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]
    [InlineData("not a url or path")]
    public void OnWindowShown_InvalidInput_ReturnsEmpty(string? input)
    {
        var search = CreateSearch();
        search.OnWindowShown(input);
        Assert.Empty(search.GetResults());
    }

    [Theory]
    [InlineData("https://github.com")]
    [InlineData("http://example.com")]
    [InlineData("www.google.com")]
    public void OnWindowShown_ValidUrl_ReturnsOneResultWithFromClipboard(string input)
    {
        var search = CreateSearch();
        search.OnWindowShown(input);
        var results = search.GetResults();
        Assert.Single(results);
        Assert.Contains("from clipboard", results[0].Subtitle);
        Assert.Equal("Web", results[0].Category);
    }

    [Fact]
    public void OnSearchStarted_ClearsCachedResult()
    {
        var search = CreateSearch();
        search.OnWindowShown("https://github.com");
        Assert.Single(search.GetResults());
        search.OnSearchStarted();
        Assert.Empty(search.GetResults());
    }

    [Fact]
    public void GetResults_BeforeOnWindowShown_ReturnsEmpty()
    {
        var search = CreateSearch();
        Assert.Empty(search.GetResults());
    }

    [Fact]
    public async Task WhenReady_CompletesImmediately()
    {
        var search = CreateSearch();
        await search.WhenReady();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ClipboardSearch CreateSearch()
    {
        // Buscar si el proyecto de tests ya tiene helpers/builders para UserSettings,
        // BrowserDiscovery, FaviconCache, FileIconCache, PlatformProvider.
        // Si existen, usarlos. Si no, construir instancias mínimas aquí:

        var loggerFactory = Microsoft.Extensions.Logging.Abstractions
            .NullLoggerFactory.Instance;

        // UserSettings.Load necesita PlatformProvider — buscar si existe un
        // UserSettings.CreateDefault() o equivalente en los tests existentes.
        // Si no existe, crear uno mínimo con: var settings = new UserSettings(...)
        // o buscar cómo lo hacen otros tests en el proyecto.

        // IMPORTANTE: completar este helper antes de ejecutar los tests.
        // Ver cómo otros tests del proyecto construyen sus dependencias.
        throw new NotImplementedException(
            "Complete CreateSearch() using the project's existing test helpers or " +
            "creating minimal stubs. Check existing test files for patterns.");
    }
}
```

> **Antes de implementar `CreateSearch()`**: ejecutar `find Yottacast.Core.Tests -name "*.cs" | head -20` y leer 2-3 test files existentes para entender cómo se construyen las dependencias. Seguir ese patrón exacto.

- [ ] **Step 2: Ejecutar para confirmar que falla**

```bash
cd Yottacast.Core.Tests && dotnet build 2>&1 | tail -20
```

Expected: error de compilación (`ClipboardSearch` no existe).

- [ ] **Step 3: Implementar `ClipboardSearch`**

```csharp
// Yottacast.Core/Search/Clipboard/ClipboardSearch.cs
using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.LocalPath;
using Yottacast.Core.Search.Url;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Clipboard;

/// <summary>
/// IEmptyStateSource that inspects the clipboard each time the window opens.
/// If the clipboard contains a valid URL or local filesystem path, shows it
/// as a result with "· from clipboard" in the subtitle.
/// </summary>
public class ClipboardSearch(
    UserSettings settings,
    BrowserDiscovery browserDiscovery,
    FaviconCache faviconCache,
    FileIconCache fileIconCache,
    PlatformProvider platform,
    ClipboardService clipboardService,
    ILogger<ClipboardSearch> logger) : IEmptyStateSource
{
    private BaseResultItemViewModel? _cached;
    private Action? _onFaviconLoaded;

    public event Action? ResultsChanged;

    public void Start()
    {
        _onFaviconLoaded = () => ResultsChanged?.Invoke();
        faviconCache.FaviconLoaded += _onFaviconLoaded;
    }

    public Task WhenReady() => Task.CompletedTask;

    public Task Stop()
    {
        if (_onFaviconLoaded is not null)
        {
            faviconCache.FaviconLoaded -= _onFaviconLoaded;
            _onFaviconLoaded = null;
        }
        _cached = null;
        return Task.CompletedTask;
    }

    public void OnWindowShown(string? clipboardText)
    {
        _cached = Build(clipboardText);
        if (_cached is not null)
            logger.LogDebug("ClipboardSearch: clipboard hit for \"{Text}\"", clipboardText);
    }

    public void OnSearchStarted() => _cached = null;

    public IReadOnlyList<BaseResultItemViewModel> GetResults() =>
        _cached is null ? [] : [_cached];

    // ── builders ─────────────────────────────────────────────────────────────

    private BaseResultItemViewModel? Build(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (UrlSearch.TryNormalizeUrl(text, out var url))
            return BuildUrlResult(url);

        if (LocalPathSearch.IsLocalPath(text))
            return BuildLocalPathResult(text);

        return null;
    }

    private ResultItemViewModel BuildUrlResult(string url)
    {
        var host = new Uri(url).Host;
        var browser = settings.ActiveBrowser;
        var browserLabel = browser?.Name ?? "browser";
        var iconBytes = faviconCache.GetOrLoad(host);
        var capturedUrl = url;

        return new ResultItemViewModel
        {
            IconBytes  = iconBytes,
            Title      = url.Length > 80 ? url[..77] + "…" : url,
            Subtitle   = $"Open in {browserLabel} · from clipboard",
            Category   = "Web",
            Score      = 4.0,
            OnActivate = () =>
            {
                if (browser is null) return;
                logger.LogInformation("ClipboardSearch: open URL \"{Url}\"", capturedUrl);
                browserDiscovery.OpenUrl(capturedUrl, browser);
            },
        };
    }

    private ResultItemViewModel? BuildLocalPathResult(string text)
    {
        var expanded = PlatformProvider.ExpandPath(text);
        if (!File.Exists(expanded) && !Directory.Exists(expanded)) return null;

        var title = Path.GetFileName(expanded);
        if (string.IsNullOrEmpty(title)) title = expanded;

        var capturedPath = expanded;
        return new ResultItemViewModel
        {
            IconBytes     = fileIconCache.GetOrPreload(expanded),
            Title         = title,
            Subtitle      = $"{expanded} · from clipboard",
            Category      = "Files",
            Score         = 4.0,
            OnActivate    = () =>
            {
                logger.LogInformation("ClipboardSearch: open path \"{Path}\"", capturedPath);
                platform.LaunchApp(capturedPath);
            },
            OnCopy        = () => clipboardService.CopyText(capturedPath),
            CopiedMessage = "Path copied!",
        };
    }
}
```

- [ ] **Step 4: Ejecutar los tests**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "ClipboardSearchTests" -v minimal
```

Expected: todos PASS.

- [ ] **Step 5: Ejecutar todos los tests**

```bash
cd Yottacast.Core.Tests && dotnet test -v minimal 2>&1 | tail -20
```

Expected: suite completa pasa.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/Search/Clipboard/ClipboardSearch.cs \
        Yottacast.Core.Tests/Search/ClipboardSearchTests.cs
git commit -m "feat: add ClipboardSearch empty-state source"
```

---

## Task 5: Refactorizar `MainWindowViewModel` y `MainWindow.axaml.cs`

**Files:**
- Modify: `Yottacast.Core/ViewModels/MainWindowViewModel.cs`
- Modify: `Yottacast/Views/MainWindow.axaml.cs`

### 5a. `MainWindowViewModel`

- [ ] **Step 1: Añadir `IEnumerable<IEmptyStateSource>` al constructor**

Localizar el constructor de `MainWindowViewModel` y añadir `IEnumerable<IEmptyStateSource> emptySources` como parámetro. Añadir también el campo:

```csharp
private readonly IReadOnlyList<IEmptyStateSource> _emptySources;
```

En el constructor, inicializar:

```csharp
_emptySources = emptySources.ToList();
```

- [ ] **Step 2: Actualizar `Initialize()` — suscribir a `ResultsChanged` y eliminar `StartTrackingNewAppsAsync`**

En `Initialize()`, eliminar la línea:

```csharp
_ = StartTrackingNewAppsAsync();
```

Añadir al final de `Initialize()`:

```csharp
foreach (var source in _emptySources)
{
    source.Start();
    source.ResultsChanged += () => Dispatcher.UIThread.Post(() => {
        if (string.IsNullOrEmpty(SearchText)) RefreshEmptyState();
    });
}
```

Eliminar los métodos `StartTrackingNewAppsAsync()`, `OnNewAppInstalled()`, `ShowPendingApps()`, y el campo `_pendingAppInfos`.

- [ ] **Step 3: Añadir `OnWindowShown(string?)` y `RefreshEmptyState()`**

```csharp
/// <summary>
/// Called by MainWindow when the window becomes visible with empty search text.
/// clipboardText is the raw clipboard string read by the View layer.
/// </summary>
public void OnWindowShown(string? clipboardText)
{
    foreach (var source in _emptySources)
        source.OnWindowShown(clipboardText);
    RefreshEmptyState();
}

private void RefreshEmptyState()
{
    var results = _emptySources.SelectMany(s => s.GetResults()).ToList();
    Results.Clear();
    foreach (var r in results) Results.Add(r);
    HasResults = Results.Count > 0;
    ShowNoResults = false;
    SelectedResult = Results.FirstOrDefault();
}
```

- [ ] **Step 4: Actualizar `OnSearchTextChanged`**

Localizar el bloque (línea ~210):

```csharp
if (string.IsNullOrWhiteSpace(value)) {
    IsSearching = false;
    _instantSnapshot = [];
    _deferredSnapshot = [];
    SetSearchHint(null);
    ShowPendingApps();
    return;
}

_pendingAppInfos.Clear();
_ = SearchAsync(value.Trim(), _cts.Token);
```

Reemplazar por:

```csharp
if (string.IsNullOrWhiteSpace(value)) {
    IsSearching = false;
    _instantSnapshot = [];
    _deferredSnapshot = [];
    SetSearchHint(null);
    RefreshEmptyState();
    return;
}

foreach (var source in _emptySources) source.OnSearchStarted();
_ = SearchAsync(value.Trim(), _cts.Token);
```

- [ ] **Step 5: Actualizar `OnAppCacheChanged`**

Localizar el bloque en `OnAppCacheChanged()`:

```csharp
if (string.IsNullOrEmpty(SearchText)) {
    if (_pendingAppInfos.Count > 0) ShowPendingApps();
    return;
}
```

Reemplazar por:

```csharp
if (string.IsNullOrEmpty(SearchText)) {
    RefreshEmptyState();
    return;
}
```

- [ ] **Step 6: Verificar que compila**

```bash
cd Yottacast.Core && dotnet build -v minimal 2>&1 | tail -20
```

Expected: Build succeeded. Si hay errores, son referencias a `_pendingAppInfos` o `ShowPendingApps` que quedaron sin eliminar.

### 5b. `MainWindow.axaml.cs`

- [ ] **Step 7: Llamar `OnWindowShown` al mostrar la ventana**

Localizar en `OnPropertyChanged` el bloque `if (isVisible)`:

```csharp
if (isVisible) {
    ApplyPositionOnShow();
    _positionDirty = false;
    _screenPosKnown = false;
    SearchBox.Focus();
    if (DataContext is MainWindowViewModel vm)
        vm.CancelDecayTimer();
}
```

Reemplazar por:

```csharp
if (isVisible) {
    ApplyPositionOnShow();
    _positionDirty = false;
    _screenPosKnown = false;
    SearchBox.Focus();
    if (DataContext is MainWindowViewModel vm) {
        vm.CancelDecayTimer();
        if (string.IsNullOrEmpty(vm.SearchText))
            _ = HandleWindowShownAsync(vm);
    }
}
```

Añadir el método privado:

```csharp
private async Task HandleWindowShownAsync(MainWindowViewModel vm)
{
    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
    var text = clipboard != null ? await clipboard.GetTextAsync() : null;
    vm.OnWindowShown(text);
}
```

- [ ] **Step 8: Verificar que compila**

```bash
cd Yottacast && dotnet build -v minimal 2>&1 | tail -20
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add Yottacast.Core/ViewModels/MainWindowViewModel.cs \
        Yottacast/Views/MainWindow.axaml.cs
git commit -m "refactor: delegate empty state to IEmptyStateSource, add OnWindowShown"
```

---

## Task 6: DI Registration

**Files:**
- Modify: `Yottacast/App.axaml.cs`

- [ ] **Step 1: Registrar las nuevas fuentes en `BuildServices()`**

Después de la línea `services.AddSingleton<ApplicationSearch>();` (línea ~223), añadir:

```csharp
services.AddSingleton<NewlyInstalledAppsSource>();
services.AddSingleton<ClipboardSearch>();
services.AddSingleton<IEmptyStateSource>(sp => sp.GetRequiredService<NewlyInstalledAppsSource>());
services.AddSingleton<IEmptyStateSource>(sp => sp.GetRequiredService<ClipboardSearch>());
```

Añadir también los using necesarios al inicio del fichero si no están ya:

```csharp
using Yottacast.Core.Search.Application;
using Yottacast.Core.Search.Clipboard;
```

- [ ] **Step 2: Build final**

```bash
cd Yottacast && dotnet build -v minimal 2>&1 | tail -20
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Ejecutar todos los tests**

```bash
cd Yottacast.Core.Tests && dotnet test -v minimal 2>&1 | tail -30
```

Expected: todos PASS.

- [ ] **Step 4: Commit**

```bash
git add Yottacast/App.axaml.cs
git commit -m "feat: register NewlyInstalledAppsSource and ClipboardSearch in DI"
```

---

## Task 7: Verificación manual

- [ ] **Step 1: Copiar una URL al portapapeles**

En Terminal: `echo -n "https://github.com" | pbcopy`

- [ ] **Step 2: Lanzar la app y abrir con el hotkey**

```bash
cd Yottacast && dotnet run
```

Abrir Yottacast. Verificar:
- La caja de búsqueda está vacía
- Aparece un resultado "github.com" con subtítulo "Open in [browser] · from clipboard"
- Pulsar Enter abre el navegador con github.com

- [ ] **Step 3: Verificar ruta local**

```bash
echo -n "/tmp" | pbcopy
```

Abrir Yottacast. Verificar:
- Aparece "tmp" con subtítulo "/tmp · from clipboard"
- Pulsar Enter abre Finder en /tmp

- [ ] **Step 4: Verificar que texto normal no produce resultado**

```bash
echo -n "esto no es ni url ni ruta" | pbcopy
```

Abrir Yottacast. Verificar: no aparece ningún resultado de portapapeles.

- [ ] **Step 5: Verificar que apps recién instaladas siguen funcionando**

Instalar/mover cualquier `.app` a `/Applications` mientras Yottacast está abierto con texto vacío. Verificar que aparece en la lista (mismo comportamiento que antes).

- [ ] **Step 6: Verificar que escribir limpia el resultado**

Con el resultado del portapapeles visible, escribir cualquier letra. Verificar que el resultado desaparece y el buscador funciona normalmente.