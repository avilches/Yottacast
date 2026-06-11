# Search Modes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introducir un sistema de modos de búsqueda (`All`, `Files`, `Clipboard`) que permite a cada fuente configurarse como siempre activa, solo en su modo dedicado, o deshabilitada; con una pill visual y Cmd+F para ciclar entre modos.

**Architecture:** Se añade `SearchSourceVisibility` (Disabled/Always/ModeOnly) y `SearchMode` (All/Files/Clipboard). `GlobalSearch` filtra fuentes por modo usando la nueva interfaz `ISearchModeSource`. `MainWindowViewModel` mantiene el modo activo y expone helpers para la UI. La pill de modo solo es visible cuando el modo activo no es `All`.

**Tech Stack:** .NET 9, Avalonia 11, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-06-11-search-modes-design.md`

---

## Mapa de ficheros

| Acción | Fichero |
|---|---|
| Crear | `Yottacast.Core/Search/SearchSourceVisibility.cs` |
| Crear | `Yottacast.Core/Search/SearchMode.cs` |
| Crear | `Yottacast.Core/Search/ISearchModeSource.cs` |
| Modificar | `Yottacast.Core/ViewModels/ActionHotkey.cs` |
| Modificar | `Yottacast.Core/Services/UserSettings.cs` |
| Modificar | `Yottacast.Core/Search/GlobalSearch.cs` |
| Modificar | `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs` |
| Modificar | `Yottacast/ViewModels/MainWindowViewModel.cs` |
| Modificar | `Yottacast/Views/MainWindow.axaml` |
| Modificar | `Yottacast/Views/MainWindow.axaml.cs` |
| Modificar | `Yottacast/ViewModels/SettingsWindowViewModel.cs` |
| Modificar | `Yottacast/Views/SettingsWindow.axaml` |
| Modificar | `Yottacast/App.axaml.cs` |
| Modificar | `Yottacast.Core.Tests/Search/GlobalSearchTests.cs` |
| Modificar | `Yottacast.Core.Tests/Services/UserSettingsTests.cs` |
| Modificar | `docs/search-files.md` |
| Modificar | `docs/user-settings.md` |

---

## Task 1: Tipos base — SearchSourceVisibility, SearchMode, ISearchModeSource, MetaF

**Files:**
- Create: `Yottacast.Core/Search/SearchSourceVisibility.cs`
- Create: `Yottacast.Core/Search/SearchMode.cs`
- Create: `Yottacast.Core/Search/ISearchModeSource.cs`
- Modify: `Yottacast.Core/ViewModels/ActionHotkey.cs`

- [ ] **Step 1: Crear SearchSourceVisibility.cs**

```csharp
// Yottacast.Core/Search/SearchSourceVisibility.cs
namespace Yottacast.Core.Search;

public enum SearchSourceVisibility
{
    Disabled,
    Always,
    ModeOnly,
}
```

- [ ] **Step 2: Crear SearchMode.cs**

```csharp
// Yottacast.Core/Search/SearchMode.cs
namespace Yottacast.Core.Search;

public enum SearchMode
{
    All,
    Files,
    Clipboard,
}
```

- [ ] **Step 3: Crear ISearchModeSource.cs**

```csharp
// Yottacast.Core/Search/ISearchModeSource.cs
namespace Yottacast.Core.Search;

/// <summary>
/// Optional interface for sources that support dedicated search modes.
/// Sources not implementing this are only active in SearchMode.All.
/// </summary>
public interface ISearchModeSource
{
    SearchMode Mode { get; }
    bool IsActiveIn(SearchMode mode);
}
```

- [ ] **Step 4: Añadir MetaF a ActionHotkey.cs**

En `Yottacast.Core/ViewModels/ActionHotkey.cs`, añadir después de la línea con `MetaShiftF`:

```csharp
    public static readonly ActionHotkey MetaF       = new("F", ActionModifiers.Meta);
```

El fichero completo queda:

```csharp
// Yottacast.Core/ViewModels/ActionHotkey.cs
namespace Yottacast.Core.ViewModels;

public enum ActionModifiers { None = 0, Meta = 1, Shift = 2, MetaShift = 3 }

public sealed record ActionHotkey(string Key, ActionModifiers Modifiers = ActionModifiers.None) {
    public static readonly ActionHotkey Enter      = new("Return");
    public static readonly ActionHotkey MetaEnter  = new("Return", ActionModifiers.Meta);
    public static readonly ActionHotkey MetaC      = new("C", ActionModifiers.Meta);
    public static readonly ActionHotkey MetaShiftF = new("F", ActionModifiers.MetaShift);
    public static readonly ActionHotkey MetaF      = new("F", ActionModifiers.Meta);
    public static readonly ActionHotkey MetaE      = new("E", ActionModifiers.Meta);
    public static readonly ActionHotkey MetaP      = new("P", ActionModifiers.Meta);
    public static readonly ActionHotkey MetaS      = new("S", ActionModifiers.Meta);
}
```

- [ ] **Step 5: Compilar para verificar**

```bash
cd /path/to/project/Yottacast.Core && dotnet build -c Debug --no-restore 2>&1 | tail -5
```

Resultado esperado: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/Search/SearchSourceVisibility.cs \
        Yottacast.Core/Search/SearchMode.cs \
        Yottacast.Core/Search/ISearchModeSource.cs \
        Yottacast.Core/ViewModels/ActionHotkey.cs
git commit -m "feat: tipos base SearchSourceVisibility, SearchMode, ISearchModeSource, MetaF"
```

---

## Task 2: UserSettings — FileSearchVisibility + migración

**Files:**
- Modify: `Yottacast.Core/Services/UserSettings.cs`
- Test: `Yottacast.Core.Tests/Services/UserSettingsTests.cs`

- [ ] **Step 1: Escribir tests que fallan**

En `Yottacast.Core.Tests/Services/UserSettingsTests.cs`, localizar la sección `// EnableFileSearch / FileSearchOnlySpecificFolders` (aprox. línea 797) y reemplazar los tests de `EnableFileSearch` por los siguientes:

```csharp
// FileSearchVisibility
[Fact]
public void FileSearchVisibility_DefaultsToAlways() {
    var settings = UserSettings.Load(new FakePlatformProvider());
    Assert.Equal(SearchSourceVisibility.Always, settings.FileSearchVisibility);
}

[Fact]
public void FileSearchVisibility_SaveAndLoad_RoundTrips() {
    using var tmp = new TempSettingsFile();
    var settings = UserSettings.Load(new FakePlatformProvider(), settingsPath: tmp.Path);
    settings.FileSearchVisibility = SearchSourceVisibility.ModeOnly;
    settings.Save();
    var reloaded = UserSettings.Load(new FakePlatformProvider(), settingsPath: tmp.Path);
    Assert.Equal(SearchSourceVisibility.ModeOnly, reloaded.FileSearchVisibility);
}

[Fact]
public void FileSearchVisibility_Migration_TrueBecomesAlways() {
    // JSON antiguo con enableFileSearch=true (sin fileSearchVisibility)
    using var tmp = new TempSettingsFile("""{"enableFileSearch":true}""");
    var settings = UserSettings.Load(new FakePlatformProvider(), settingsPath: tmp.Path);
    Assert.Equal(SearchSourceVisibility.Always, settings.FileSearchVisibility);
}

[Fact]
public void FileSearchVisibility_Migration_FalseBecomesDisabled() {
    using var tmp = new TempSettingsFile("""{"enableFileSearch":false}""");
    var settings = UserSettings.Load(new FakePlatformProvider(), settingsPath: tmp.Path);
    Assert.Equal(SearchSourceVisibility.Disabled, settings.FileSearchVisibility);
}

[Fact]
public void ClipboardSearchVisibility_DefaultsToDisabled() {
    var settings = UserSettings.Load(new FakePlatformProvider());
    Assert.Equal(SearchSourceVisibility.Disabled, settings.ClipboardSearchVisibility);
}
```

- [ ] **Step 2: Ejecutar para verificar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "FileSearchVisibility|ClipboardSearchVisibility" -v n 2>&1 | tail -20
```

Resultado esperado: `FAILED` (los miembros no existen aún).

- [ ] **Step 3: Implementar los cambios en UserSettings.cs**

**3a. Añadir `using` al principio del fichero si no está:**

```csharp
using Yottacast.Core.Search;
```

**3b. Reemplazar la propiedad pública `EnableFileSearch` (línea ~38) con:**

```csharp
    public SearchSourceVisibility FileSearchVisibility { get; set; } = SearchSourceVisibility.Always;
    public SearchSourceVisibility ClipboardSearchVisibility { get; set; } = SearchSourceVisibility.Disabled;
    public string? ClipboardHotkey { get; set; }
```

**3c. En `UserSettingsData` (record interno, línea ~147), reemplazar la línea de `enableFileSearch`:**

```csharp
        [JsonPropertyName("enableFileSearch")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EnableFileSearch { get; init; }  // solo para migración; null en ficheros nuevos
        [JsonPropertyName("fileSearchVisibility")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FileSearchVisibility { get; init; }
        [JsonPropertyName("clipboardSearchVisibility")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ClipboardSearchVisibility { get; init; }
        [JsonPropertyName("clipboardHotkey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ClipboardHotkey { get; init; }
```

**3d. En el bloque `Load()` (donde asigna propiedades desde `data`), reemplazar la asignación `EnableFileSearch = data.EnableFileSearch,` por:**

```csharp
                    FileSearchVisibility = data.FileSearchVisibility != null
                        ? Enum.TryParse<SearchSourceVisibility>(data.FileSearchVisibility, ignoreCase: true, out var fsv)
                            ? fsv : SearchSourceVisibility.Always
                        : data.EnableFileSearch == false
                            ? SearchSourceVisibility.Disabled
                            : SearchSourceVisibility.Always,
                    ClipboardSearchVisibility = data.ClipboardSearchVisibility != null
                        ? Enum.TryParse<SearchSourceVisibility>(data.ClipboardSearchVisibility, ignoreCase: true, out var csv)
                            ? csv : SearchSourceVisibility.Disabled
                        : SearchSourceVisibility.Disabled,
                    ClipboardHotkey = data.ClipboardHotkey,
```

**3e. En el método `Save()`, reemplazar la línea `EnableFileSearch = EnableFileSearch,` por:**

```csharp
                FileSearchVisibility = FileSearchVisibility.ToString(),
                ClipboardSearchVisibility = ClipboardSearchVisibility.ToString(),
                ClipboardHotkey = ClipboardHotkey,
```

Y eliminar `EnableFileSearch = EnableFileSearch,` de Save (no escribimos el campo legacy en ficheros nuevos).

- [ ] **Step 4: Ejecutar tests**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "FileSearchVisibility|ClipboardSearchVisibility" -v n 2>&1 | tail -20
```

Resultado esperado: todos PASS.

- [ ] **Step 5: Ejecutar suite completa para verificar no hay regresiones**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
```

Resultado esperado: `Passed` (0 failed). Si hay tests de `EnableFileSearch` que fallan, actualizarlos: `EnableFileSearch_DefaultsToTrue` → `FileSearchVisibility_DefaultsToAlways` (ya añadido). Eliminar los tests legacy de `EnableFileSearch`.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/Services/UserSettings.cs \
        Yottacast.Core.Tests/Services/UserSettingsTests.cs
git commit -m "feat: UserSettings con FileSearchVisibility, ClipboardSearchVisibility y migración desde enableFileSearch"
```

---

## Task 3: UserDocumentSearch implementa ISearchModeSource

**Files:**
- Modify: `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs`
- Test: `Yottacast.Core.Tests/Search/UserDocumentSearchTests.cs`

- [ ] **Step 1: Escribir tests que fallan**

Al final de la clase `UserDocumentSearchTests`, añadir:

```csharp
// ── ISearchModeSource ────────────────────────────────────────────────────────

[Fact]
public void IsActiveIn_All_WhenAlways() {
    var platform = new FakePlatformProvider();
    var settings = UserSettings.Load(platform);
    settings.FileSearchVisibility = SearchSourceVisibility.Always;
    var search = new UserDocumentSearch(settings, new FileSearch(platform),
        new FileIconCache(platform, NullLogger<FileIconCache>.Instance),
        platform, NullLogger<UserDocumentSearch>.Instance,
        new ClipboardService(NullLogger<ClipboardService>.Instance));

    Assert.True(((ISearchModeSource)search).IsActiveIn(SearchMode.All));
    Assert.False(((ISearchModeSource)search).IsActiveIn(SearchMode.Files));
}

[Fact]
public void IsActiveIn_Files_WhenModeOnly() {
    var platform = new FakePlatformProvider();
    var settings = UserSettings.Load(platform);
    settings.FileSearchVisibility = SearchSourceVisibility.ModeOnly;
    var search = new UserDocumentSearch(settings, new FileSearch(platform),
        new FileIconCache(platform, NullLogger<FileIconCache>.Instance),
        platform, NullLogger<UserDocumentSearch>.Instance,
        new ClipboardService(NullLogger<ClipboardService>.Instance));

    Assert.False(((ISearchModeSource)search).IsActiveIn(SearchMode.All));
    Assert.True(((ISearchModeSource)search).IsActiveIn(SearchMode.Files));
    Assert.False(((ISearchModeSource)search).IsActiveIn(SearchMode.Clipboard));
}

[Fact]
public void IsActiveIn_NeverActive_WhenDisabled() {
    var platform = new FakePlatformProvider();
    var settings = UserSettings.Load(platform);
    settings.FileSearchVisibility = SearchSourceVisibility.Disabled;
    var search = new UserDocumentSearch(settings, new FileSearch(platform),
        new FileIconCache(platform, NullLogger<FileIconCache>.Instance),
        platform, NullLogger<UserDocumentSearch>.Instance,
        new ClipboardService(NullLogger<ClipboardService>.Instance));

    Assert.False(((ISearchModeSource)search).IsActiveIn(SearchMode.All));
    Assert.False(((ISearchModeSource)search).IsActiveIn(SearchMode.Files));
}
```

- [ ] **Step 2: Ejecutar para verificar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "IsActiveIn" -v n 2>&1 | tail -15
```

Resultado esperado: `FAILED` (UserDocumentSearch no implementa ISearchModeSource).

- [ ] **Step 3: Implementar ISearchModeSource en UserDocumentSearch**

En la declaración de la clase, añadir la interfaz:

```csharp
public class UserDocumentSearch(...) : IDeferredSearchSource, ISearchModeSource {
```

Añadir las propiedades/métodos de la interfaz (después de `Stop()`):

```csharp
    public SearchMode Mode => SearchMode.Files;

    public bool IsActiveIn(SearchMode mode) => mode switch {
        SearchMode.All   => settings.FileSearchVisibility == SearchSourceVisibility.Always,
        SearchMode.Files => settings.FileSearchVisibility == SearchSourceVisibility.ModeOnly,
        _                => false,
    };
```

Eliminar la línea en `SearchAsync` (línea ~83):

```csharp
        if (!settings.EnableFileSearch) yield break;  // ← eliminar esta línea
```

Actualizar el log de debug (línea ~102) para no referenciar `EnableFileSearch`:

```csharp
            logger.LogDebug("DocSearch start query=\"{Query}\" timeout={TimeoutMs}ms visibility={Visibility} onlySpecificFolders={OnlySpecific} folders=[{Folders}]",
                query, timeoutMs, settings.FileSearchVisibility, settings.FileSearchOnlySpecificFolders,
                folders is null ? "(all)" : string.Join(", ", folders));
```

- [ ] **Step 4: Ejecutar tests**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "IsActiveIn" -v n 2>&1 | tail -15
```

Resultado esperado: todos PASS.

- [ ] **Step 5: Suite completa**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
```

Resultado esperado: 0 failed.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs \
        Yottacast.Core.Tests/Search/UserDocumentSearchTests.cs
git commit -m "feat: UserDocumentSearch implementa ISearchModeSource"
```

---

## Task 4: GlobalSearch — filtrado por modo

**Files:**
- Modify: `Yottacast.Core/Search/GlobalSearch.cs`
- Test: `Yottacast.Core.Tests/Search/GlobalSearchTests.cs`

- [ ] **Step 1: Escribir tests que fallan**

Al final del fichero `GlobalSearchTests.cs`, antes del cierre de namespace, añadir:

```csharp
// ── Filtrado por modo ────────────────────────────────────────────────────────

file sealed class ModeInstantSource(SearchMode mode, SearchSourceVisibility visibility)
    : IInstantSearchSource, ISearchModeSource {
    public bool WasSearched { get; private set; }
    public int Limit => 100;
    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;
    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _) {
        WasSearched = true;
        return [new ResultItemViewModel { Title = $"result-{mode}", Score = 1.0 }];
    }
    public SearchMode Mode => mode;
    public bool IsActiveIn(SearchMode m) => m switch {
        SearchMode.All   => visibility == SearchSourceVisibility.Always,
        var x when x == mode => visibility == SearchSourceVisibility.ModeOnly,
        _ => false,
    };
}

public class GlobalSearchModeTests {

    [Fact]
    public void SearchInstant_AllMode_ExcludesModeOnlySources() {
        var always = new StubInstantSource([new ResultItemViewModel { Title = "always", Score = 1.0 }]);
        var modeOnly = new ModeInstantSource(SearchMode.Files, SearchSourceVisibility.ModeOnly);
        var gs = new GlobalSearch([always, modeOnly], []);

        var (items, _, _) = gs.SearchInstant("q", 10, SearchMode.All);

        Assert.Single(items);
        Assert.Equal("always", ((ResultItemViewModel)items[0]).Title);
        Assert.True(always.WasSearched);
        Assert.False(modeOnly.WasSearched);
    }

    [Fact]
    public void SearchInstant_FilesMode_OnlyIncludesModeOnlyFilesSource() {
        var always = new StubInstantSource([new ResultItemViewModel { Title = "always", Score = 1.0 }]);
        var modeOnly = new ModeInstantSource(SearchMode.Files, SearchSourceVisibility.ModeOnly);
        var gs = new GlobalSearch([always, modeOnly], []);

        var (items, _, _) = gs.SearchInstant("q", 10, SearchMode.Files);

        Assert.Single(items);
        Assert.Equal("result-Files", ((ResultItemViewModel)items[0]).Title);
        Assert.False(always.WasSearched);
        Assert.True(modeOnly.WasSearched);
    }

    [Fact]
    public void SearchInstant_AllMode_AlwaysSourceActive() {
        var always = new ModeInstantSource(SearchMode.Files, SearchSourceVisibility.Always);
        var gs = new GlobalSearch([always], []);

        var (items, _, _) = gs.SearchInstant("q", 10, SearchMode.All);

        Assert.Single(items);
        Assert.True(always.WasSearched);
    }
}
```

- [ ] **Step 2: Ejecutar para verificar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "GlobalSearchModeTests" -v n 2>&1 | tail -15
```

Resultado esperado: `FAILED` (SearchInstant no acepta parámetro mode).

- [ ] **Step 3: Implementar filtrado por modo en GlobalSearch.cs**

Reemplazar el contenido de `GlobalSearch.cs` con:

```csharp
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public class GlobalSearch(IEnumerable<IInstantSearchSource> instantSources, IEnumerable<IDeferredSearchSource> deferredSources) {

    private readonly IReadOnlyList<IInstantSearchSource> _instantSources = instantSources.ToList();
    private readonly IReadOnlyList<IDeferredSearchSource> _deferredSources = deferredSources.ToList();

    public void Start() {
        foreach (var s in _instantSources) s.Start();
        foreach (var s in _deferredSources) s.Start();
    }

    public Task WhenInstantReady() => Task.WhenAll(_instantSources.Select(s => s.WhenReady()));

    public Task WhenReady() => Task.WhenAll(
        _instantSources.Select(s => s.WhenReady())
        .Concat(_deferredSources.Select(s => s.WhenReady())));

    public Task Stop() => Task.WhenAll(
        _instantSources.Select(s => s.Stop())
        .Concat(_deferredSources.Select(s => s.Stop())));

    public (IReadOnlyList<BaseResultItemViewModel> Items, string? Hint, SearchHintKind HintKind)
        SearchInstant(string query, int limit, SearchMode mode = SearchMode.All) {

        var activeSources = GetActiveInstantSources(mode);
        var items = activeSources
            .SelectMany(s => {
                var sourceLimit = s.Limit;
                return s.Search(query, sourceLimit < 0 ? limit : sourceLimit);
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        var hintProvider = activeSources.OfType<ISearchHintProvider>().FirstOrDefault(s => s.LastHint != null);
        var hint = hintProvider?.LastHint;
        var hintKind = hintProvider?.LastHintKind ?? SearchHintKind.Info;
        return (items, hint, hintKind);
    }

    public IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchDeferredAsync(
        string query, int limit, SearchMode mode = SearchMode.All, CancellationToken ct = default)
        => SearchSourcesAsync(GetActiveDeferredSources(mode), query, limit, ct);

    private IReadOnlyList<IInstantSearchSource> GetActiveInstantSources(SearchMode mode) {
        if (mode == SearchMode.All)
            return _instantSources
                .Where(s => s is not ISearchModeSource ms || ms.IsActiveIn(SearchMode.All))
                .ToList();
        return _instantSources
            .OfType<ISearchModeSource>()
            .Where(s => s.IsActiveIn(mode))
            .Cast<IInstantSearchSource>()
            .ToList();
    }

    private IReadOnlyList<IDeferredSearchSource> GetActiveDeferredSources(SearchMode mode) {
        if (mode == SearchMode.All)
            return _deferredSources
                .Where(s => s is not ISearchModeSource ms || ms.IsActiveIn(SearchMode.All))
                .ToList();
        return _deferredSources
            .OfType<ISearchModeSource>()
            .Where(s => s.IsActiveIn(mode))
            .Cast<IDeferredSearchSource>()
            .ToList();
    }

    private static async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchSourcesAsync(
        IReadOnlyList<IDeferredSearchSource> subset, string query, int limit,
        [EnumeratorCancellation] CancellationToken ct = default) {

        var snapshots = new List<BaseResultItemViewModel>[subset.Count];
        for (var i = 0; i < subset.Count; i++) snapshots[i] = [];

        var channel = Channel.CreateUnbounded<(int, IReadOnlyList<BaseResultItemViewModel>)>();

        var tasks = subset.Select((s, i) => Task.Run(async () => {
            try {
                await foreach (var snap in s.SearchAsync(query, limit, ct).ConfigureAwait(false))
                    await channel.Writer.WriteAsync((i, snap), ct).ConfigureAwait(false);
            } catch (OperationCanceledException) { }
        }, CancellationToken.None)).ToList();

        _ = Task.WhenAll(tasks).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        await foreach (var (idx, snap) in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            snapshots[idx] = snap.ToList();
            yield return snapshots.SelectMany(s => s)
                .OrderByDescending(x => x.Score)
                .Take(limit)
                .ToList();
        }
    }
}
```

- [ ] **Step 4: Ejecutar tests de modo**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "GlobalSearchModeTests" -v n 2>&1 | tail -15
```

Resultado esperado: todos PASS.

- [ ] **Step 5: Suite completa**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
```

Resultado esperado: 0 failed.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/Search/GlobalSearch.cs \
        Yottacast.Core.Tests/Search/GlobalSearchTests.cs
git commit -m "feat: GlobalSearch filtra fuentes por SearchMode"
```

---

## Task 5: MainWindowViewModel — modo activo

**Files:**
- Modify: `Yottacast/ViewModels/MainWindowViewModel.cs`

No hay tests unitarios para este ViewModel (depende de Avalonia UI). Los tests se hacen en la tarea de UI.

- [ ] **Step 1: Añadir usings necesarios en MainWindowViewModel.cs**

Asegurarse de que están presentes (añadir si faltan):

```csharp
using Yottacast.Core.Search;
```

- [ ] **Step 2: Añadir propiedades y métodos de modo**

Después del campo `private bool _textIsFromHistory;` (aprox. línea 160), añadir:

```csharp
    private SearchMode _activeMode = SearchMode.All;

    public SearchMode ActiveMode {
        get => _activeMode;
        private set {
            if (_activeMode == value) return;
            _activeMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowModePill));
            OnPropertyChanged(nameof(ActiveModeName));
            if (!string.IsNullOrWhiteSpace(SearchText)) {
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                _userNavigated = false;
                _ = SearchAsync(SearchText.Trim(), _cts.Token);
            }
        }
    }

    public bool ShowModePill => _activeMode != SearchMode.All;

    public string ActiveModeName => _activeMode switch {
        SearchMode.Files     => "Files",
        SearchMode.Clipboard => "Clipboard",
        _                    => "",
    };

    public IReadOnlyList<SearchMode> AvailableModes {
        get {
            var modes = new List<SearchMode>();
            if (settings.FileSearchVisibility == SearchSourceVisibility.ModeOnly)
                modes.Add(SearchMode.Files);
            if (settings.ClipboardSearchVisibility == SearchSourceVisibility.ModeOnly)
                modes.Add(SearchMode.Clipboard);
            return modes;
        }
    }

    public void CycleMode() {
        var modes = AvailableModes;
        if (modes.Count == 0) return;
        if (_activeMode == SearchMode.All) {
            ActiveMode = modes[0];
        } else {
            var idx = modes.IndexOf(_activeMode);
            ActiveMode = idx >= modes.Count - 1 ? SearchMode.All : modes[idx + 1];
        }
    }

    public void ResetMode() => ActiveMode = SearchMode.All;

    public void ActivateMode(SearchMode mode) {
        if (AvailableModes.Contains(mode))
            ActiveMode = mode;
    }
```

- [ ] **Step 3: Actualizar SearchAsync para pasar el modo activo**

En `SearchAsync` (aprox. línea 315), reemplazar la llamada a `SearchInstant`:

```csharp
        var (instantItems, hint, hintKind) = globalSearch.SearchInstant(query, limit: SearchSourceLimit, _activeMode);
```

Y la llamada a `SearchDeferredAsync` (aprox. línea 341):

```csharp
            await foreach (var snapshot in globalSearch.SearchDeferredAsync(query, limit: SearchSourceLimit, _activeMode, _deferredCts.Token)) {
```

- [ ] **Step 4: Actualizar RefreshSearch y OnAppCacheChanged**

En `RefreshSearch` (aprox. línea 171):

```csharp
        var (items, hint, hintKind) = globalSearch.SearchInstant(SearchText.Trim(), limit: SearchSourceLimit, _activeMode);
```

En `OnAppCacheChanged` (aprox. línea 234):

```csharp
            var (items, hint, hintKind) = globalSearch.SearchInstant(SearchText.Trim(), limit: SearchSourceLimit, _activeMode);
```

- [ ] **Step 5: Actualizar OnSearchSettingsChanged para recalcular modos disponibles**

En `OnSearchSettingsChanged` (aprox. línea 215), añadir después del `_userNavigated = false;`:

```csharp
            // Si el modo activo ya no está disponible (el usuario cambió su configuración), volver a All
            OnPropertyChanged(nameof(AvailableModes));
            if (_activeMode != SearchMode.All && !AvailableModes.Contains(_activeMode))
                ResetMode();
```

- [ ] **Step 6: Compilar**

```bash
cd Yottacast && dotnet build -c Debug --no-restore 2>&1 | tail -10
```

Resultado esperado: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add Yottacast/ViewModels/MainWindowViewModel.cs
git commit -m "feat: MainWindowViewModel con ActiveMode, CycleMode, ShowModePill"
```

---

## Task 6: MainWindow UI — pill de modo + Cmd+F + Escape ampliado

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml`
- Modify: `Yottacast/Views/MainWindow.axaml.cs`

- [ ] **Step 1: Añadir pill de modo en MainWindow.axaml**

Localizar el bloque del divisor (aprox. línea 285):

```xml
            <!-- ── Divider ── -->
            <Rectangle DockPanel.Dock="Top"
```

Justo ANTES de esa línea, insertar la pill de modo:

```xml
            <!-- ── Mode pill (visible solo cuando ActiveMode != All) ── -->
            <Border DockPanel.Dock="Top"
                    IsVisible="{Binding ShowModePill}"
                    Padding="12,0,12,6">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Border Background="{DynamicResource Theme.Results.SelectionBar.Color}"
                            CornerRadius="12"
                            Padding="10,4">
                        <TextBlock Text="{Binding ActiveModeName}"
                                   Foreground="{DynamicResource Theme.Window.Background}"
                                   FontSize="12"
                                   FontWeight="Medium"/>
                    </Border>
                    <TextBlock Text="⌘F to cycle · Esc to exit"
                               Foreground="{DynamicResource Theme.Search.Color}"
                               Opacity="0.5"
                               FontSize="11"
                               VerticalAlignment="Center"/>
                </StackPanel>
            </Border>
```

- [ ] **Step 2: Añadir handler Cmd+F en OnTunnelKeyDown (MainWindow.axaml.cs)**

Localizar el bloque `// ── Cmd+E: open edit directly...` (aprox. línea 524). Justo ANTES de ese bloque, añadir:

```csharp
        // ── Cmd+F: ciclar modo de búsqueda ──────────────────────────────────────
        if (AppHandler.Instance.MatchesHotkey(e, ActionHotkey.MetaF)) {
            vm.CycleMode();
            e.Handled = true;
            return;
        }
```

- [ ] **Step 3: Añadir nivel de Escape para modo activo en OnKeyDown (MainWindow.axaml.cs)**

Localizar el bloque `case Key.Escape:` en `OnKeyDown` (aprox. línea 602). La cadena actual es:

```csharp
            case Key.Escape:
                if (vm.IsEditorOpen) { ... }
                if (vm.IsOptionsMenuOpen) {
                    vm.CloseOptionsMenu();
                } else if (vm.IsSearching) {
                    vm.CancelDeferredSearch();
                    vm.CleanAndSaveHistory(null);
                } else if (!string.IsNullOrEmpty(vm.SearchText)) {
                    vm.CleanAndSaveHistory(null);
                } else {
                    Hide();
                }
```

Añadir el nivel de modo entre `IsSearching` y `SearchText no vacío`:

```csharp
            case Key.Escape:
                if (vm.IsEditorOpen) {
                    if (vm.EditorPanel.ShowUnsavedDialog)
                        vm.EditorPanel.CancelUnsavedDialog();
                    else
                        vm.EditorPanel.RequestClose();
                    e.Handled = true;
                    break;
                }
                if (vm.IsOptionsMenuOpen) {
                    vm.CloseOptionsMenu();
                } else if (vm.IsSearching) {
                    vm.CancelDeferredSearch();
                    vm.CleanAndSaveHistory(null);
                } else if (vm.ShowModePill) {
                    vm.ResetMode();
                } else if (!string.IsNullOrEmpty(vm.SearchText)) {
                    vm.CleanAndSaveHistory(null);
                } else {
                    Hide();
                }
                e.Handled = true;
                break;
```

- [ ] **Step 4: Compilar y ejecutar la app**

```bash
cd Yottacast && dotnet run 2>&1 &
sleep 3
```

Verificar manualmente:
1. Configurar File Search como "⌘F only" en Settings (aún no disponible, hacerlo en JSON directamente o esperar Task 7)
2. Abrir el launcher → no se ven pills
3. Pulsar Cmd+F → aparece pill "Files"
4. Pulsar Cmd+F otra vez → desaparece pill (vuelve a All)
5. Con pill activa y texto escrito, pulsar Escape → desaparece pill (modo vuelve a All)
6. Pulsar Escape de nuevo → limpia el texto

- [ ] **Step 5: Commit**

```bash
git add Yottacast/Views/MainWindow.axaml \
        Yottacast/Views/MainWindow.axaml.cs
git commit -m "feat: pill de modo activo, Cmd+F cicla modos, Escape vuelve a All"
```

---

## Task 7: Settings — segmented control para FileSearchVisibility

**Files:**
- Modify: `Yottacast/ViewModels/SettingsWindowViewModel.cs`
- Modify: `Yottacast/Views/SettingsWindow.axaml`

- [ ] **Step 1: Actualizar SettingsWindowViewModel.cs**

**1a.** Localizar el campo `[ObservableProperty] private bool _enableFileSearch;` (línea ~186 área de campos) y reemplazarlo:

```csharp
    private SearchSourceVisibility _fileSearchVisibility;

    public bool FileSearchDisabled  { get => _fileSearchVisibility == SearchSourceVisibility.Disabled;  set { if (value) UpdateFileSearchVisibility(SearchSourceVisibility.Disabled);  } }
    public bool FileSearchAlways    { get => _fileSearchVisibility == SearchSourceVisibility.Always;     set { if (value) UpdateFileSearchVisibility(SearchSourceVisibility.Always);     } }
    public bool FileSearchModeOnly  { get => _fileSearchVisibility == SearchSourceVisibility.ModeOnly;   set { if (value) UpdateFileSearchVisibility(SearchSourceVisibility.ModeOnly);   } }
    public bool FileSearchNotDisabled => _fileSearchVisibility != SearchSourceVisibility.Disabled;

    private void UpdateFileSearchVisibility(SearchSourceVisibility v) {
        _fileSearchVisibility = v;
        _settings.FileSearchVisibility = v;
        _settings.Save();
        _settings.NotifySearchSettingsChanged();
        _logger.LogInformation("Settings: FileSearchVisibility = {Value}", v);
        OnPropertyChanged(nameof(FileSearchDisabled));
        OnPropertyChanged(nameof(FileSearchAlways));
        OnPropertyChanged(nameof(FileSearchModeOnly));
        OnPropertyChanged(nameof(FileSearchNotDisabled));
    }
```

**1b.** Eliminar el `partial void OnEnableFileSearchChanged(bool value)` (línea ~186 área de partial methods).

**1c.** En el constructor (bloque de asignaciones iniciales, aprox. línea 445), reemplazar `_enableFileSearch = settings.EnableFileSearch;` por:

```csharp
        _fileSearchVisibility = settings.FileSearchVisibility;
```

**1d.** Añadir el `using` necesario si no está:

```csharp
using Yottacast.Core.Search;
```

- [ ] **Step 2: Actualizar SettingsWindow.axaml**

Localizar el bloque de File Search (aprox. línea 1008-1020):

```xml
                <StackPanel Spacing="16" IsVisible="{Binding IsFileSearchSelected}">
                    <TextBlock Classes="section-heading" Text="File Search"/>

                    <ToggleSwitch IsChecked="{Binding EnableFileSearch}"
                                  OnContent="Enabled"
                                  OffContent="Disabled"/>

                    <TextBlock Classes="description"
                               Text="Search for documents and files on your system."/>

                    <StackPanel Spacing="12" IsVisible="{Binding EnableFileSearch}">
```

Reemplazar por:

```xml
                <StackPanel Spacing="16" IsVisible="{Binding IsFileSearchSelected}">
                    <TextBlock Classes="section-heading" Text="File Search"/>

                    <StackPanel Orientation="Horizontal" Spacing="4">
                        <RadioButton Content="Off"
                                     GroupName="FileSearchVisibility"
                                     IsChecked="{Binding FileSearchDisabled}"/>
                        <RadioButton Content="Always"
                                     GroupName="FileSearchVisibility"
                                     IsChecked="{Binding FileSearchAlways}"/>
                        <RadioButton Content="⌘F only"
                                     GroupName="FileSearchVisibility"
                                     IsChecked="{Binding FileSearchModeOnly}"/>
                    </StackPanel>

                    <TextBlock Classes="description"
                               Text="Search for documents and files on your system."/>

                    <StackPanel Spacing="12" IsVisible="{Binding FileSearchNotDisabled}">
```

- [ ] **Step 3: Compilar**

```bash
cd Yottacast && dotnet build -c Debug --no-restore 2>&1 | tail -10
```

Resultado esperado: `Build succeeded.`

- [ ] **Step 4: Probar manualmente**

1. Ejecutar la app (`dotnet run` en `Yottacast/`)
2. Abrir Settings (Cmd+,) → sección File Search
3. Verificar que aparecen 3 radio buttons: "Off", "Always", "⌘F only"
4. Seleccionar "⌘F only" → cerrar Settings
5. En el launcher, pulsar Cmd+F → debe aparecer pill "Files"
6. Pulsar Cmd+F de nuevo → vuelve a All (sin pill)

- [ ] **Step 5: Commit**

```bash
git add Yottacast/ViewModels/SettingsWindowViewModel.cs \
        Yottacast/Views/SettingsWindow.axaml
git commit -m "feat: Settings reemplaza toggle FileSearch por selector Off/Always/⌘F only"
```

---

## Task 8: App.axaml.cs — hotkey global de Clipboard + migración

**Files:**
- Modify: `Yottacast/App.axaml.cs`

- [ ] **Step 1: Registrar hotkey global de Clipboard**

En `App.axaml.cs`, localizar `private void RegisterGlobalHotKey(...)` (aprox. línea 364). Justo antes de `_ = _globalHook.RunAsync();` (al final del método), añadir el registro del hotkey de Clipboard:

```csharp
        // Hotkey global para abrir directamente en modo Clipboard
        var clipboardHotkey = HotkeyConfig.Parse(settings.ClipboardHotkey);

        if (clipboardHotkey != null && settings.ClipboardSearchVisibility == SearchSourceVisibility.ModeOnly) {
            _globalHook.KeyPressed += (_, e) => {
                var mask = e.RawEvent.Mask;
                var hasAlt  = mask.HasFlag(EventMask.LeftAlt)  || mask.HasFlag(EventMask.RightAlt);
                var hasCtrl = mask.HasFlag(EventMask.LeftCtrl) || mask.HasFlag(EventMask.RightCtrl);
                var hasShift= mask.HasFlag(EventMask.LeftShift)|| mask.HasFlag(EventMask.RightShift);
                var hasMeta = mask.HasFlag(EventMask.LeftMeta) || mask.HasFlag(EventMask.RightMeta);

                if (e.Data.KeyCode == KeyNameToKeyCode(clipboardHotkey.KeyName)
                    && hasAlt == clipboardHotkey.Alt && hasCtrl == clipboardHotkey.Ctrl
                    && hasShift == clipboardHotkey.Shift && hasMeta == clipboardHotkey.Meta) {

                    e.SuppressEvent = true;

                    Dispatcher.UIThread.InvokeAsync(() => {
                        var window = desktop.MainWindow;
                        if (window is null) return;
                        if (!window.IsVisible) AppHandler.Instance.ShowWindow(window);
                        if (window.DataContext is MainWindowViewModel vm)
                            vm.ActivateMode(SearchMode.Clipboard);
                    });
                }
            };
        }
```

- [ ] **Step 2: Añadir usings en App.axaml.cs si faltan**

```csharp
using Yottacast.Core.Search;
using Yottacast.Core.Platform;
using Yottacast.ViewModels;
```

- [ ] **Step 3: Compilar**

```bash
cd Yottacast && dotnet build -c Debug --no-restore 2>&1 | tail -10
```

Resultado esperado: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add Yottacast/App.axaml.cs \
        Yottacast/ViewModels/MainWindowViewModel.cs
git commit -m "feat: hotkey global para abrir directamente en modo Clipboard"
```

---

## Task 9: Docs + tests finales

**Files:**
- Modify: `docs/search-files.md`
- Modify: `docs/user-settings.md`

- [ ] **Step 1: Actualizar docs/search-files.md**

En la sección de invariantes de Búsqueda de documentos del usuario, reemplazar la referencia a `EnableFileSearch` por `FileSearchVisibility`:

```markdown
- Nunca se lanza una búsqueda de archivos si `FileSearchVisibility == Disabled`.
- En modo `Always`: la fuente es activa en el modo All (búsqueda normal mezclada).
- En modo `ModeOnly`: la fuente solo es activa cuando el usuario activa el modo Files (Cmd+F).
```

En la sección de verificación (`> **Verificar en:**`), actualizar `UserSettings.EnableFileSearch` → `UserSettings.FileSearchVisibility`.

- [ ] **Step 2: Actualizar docs/user-settings.md**

Localizar la tabla de propiedades o sección de búsqueda de ficheros y actualizar:
- `EnableFileSearch: bool` → `FileSearchVisibility: SearchSourceVisibility` (Disabled/Always/ModeOnly, default Always)
- Añadir: `ClipboardSearchVisibility: SearchSourceVisibility` (default Disabled)
- Añadir: `ClipboardHotkey: string?` (null por defecto)

- [ ] **Step 3: Suite completa de tests**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -15
cd Yottacast.Ipc.Tests && dotnet test 2>&1 | tail -10
```

Resultado esperado: 0 failed en ambos.

- [ ] **Step 4: Commit final**

```bash
git add docs/search-files.md docs/user-settings.md
git commit -m "docs: actualizar search-files y user-settings para reflejar FileSearchVisibility y modos"
```

---

## Verificación manual completa

Antes de dar por terminada la feature, verificar manualmente:

1. **Comportamiento por defecto (Always)**: Abrir el launcher, escribir texto, los ficheros aparecen mezclados. Sin pills. Cmd+F no hace nada (no hay modos disponibles).
2. **Modo ⌘F only**: En Settings, cambiar File Search a "⌘F only". Abrir el launcher, escribir texto, no aparecen ficheros. Pulsar Cmd+F → pill "Files" aparece, solo aparecen ficheros. Pulsar Cmd+F → vuelve a All. Pulsar Escape con pill activa → vuelve a All.
3. **Escape preserva el texto**: Escribir "informe", pulsar Cmd+F (modo Files), el texto sigue siendo "informe" y se busca en ficheros. Pulsar Escape → pill desaparece, sigue buscando "informe" en All.
4. **Migración**: Editar `settings.json` manualmente para tener `"enableFileSearch": false`, reiniciar la app, verificar que File Search aparece como "Off" en Settings.
5. **Disabled**: Cambiar a "Off" en Settings, verificar que no aparecen ficheros nunca.
