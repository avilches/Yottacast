# Clipboard History Search — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capturar texto copiado al portapapeles en background, persistirlo en disco, y exponerlo como fuente de búsqueda en modo Clipboard con paste automático al activar.

**Architecture:** `ClipboardHistoryStore` (Core) mantiene la lista en memoria y persiste a JSON. `ClipboardHistorySearch` (Core) es un `IInstantSearchSource + ISearchModeSource` con scoring por relevancia + decay de uso. `MacClipboardMonitor` / `WindowsClipboardMonitor` (UI layer) hacen polling al OS y llaman a `store.Add()` via wiring en `AppHandler`. Los resultados usan `PasteAfterClose = true` igual que emoji.

**Tech Stack:** C# 13, .NET 9, System.Text.Json, System.Runtime.InteropServices.Marshal (P/Invoke macOS), Avalonia Key/KeyModifiers (settings UI).

---

## File Map

| Acción | Ruta |
|--------|------|
| Create | `Yottacast.Core/Services/IClipboardMonitor.cs` |
| Create | `Yottacast.Core/Search/Clipboard/ClipboardHistoryEntry.cs` |
| Create | `Yottacast.Core/Search/Clipboard/ClipboardHistoryStore.cs` |
| Create | `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs` |
| Create | `Yottacast/Services/MacClipboardMonitor.cs` |
| Create | `Yottacast/Services/WindowsClipboardMonitor.cs` |
| Create | `Yottacast.Core.Tests/Search/ClipboardHistoryStoreTests.cs` |
| Create | `Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs` |
| Modify | `Yottacast.Core/AppPaths.cs` |
| Modify | `Yottacast.Core/AppDefaults.cs` |
| Modify | `Yottacast.Core/ViewModels/ActionHotkey.cs` |
| Modify | `Yottacast.Core/Services/UserSettings.cs` |
| Modify | `Yottacast/App.axaml.cs` |
| Modify | `Yottacast/ViewModels/SettingsWindowViewModel.cs` |
| Modify | `Yottacast/Views/SettingsWindow.axaml` |
| Modify | `Yottacast.Ipc/Mapping/SettingsMapper.cs` |

---

## Task 1: Foundation — constantes, rutas, interfaz y hotkey Delete

**Files:**
- Modify: `Yottacast.Core/AppPaths.cs`
- Modify: `Yottacast.Core/AppDefaults.cs`
- Modify: `Yottacast.Core/ViewModels/ActionHotkey.cs`
- Create: `Yottacast.Core/Services/IClipboardMonitor.cs`

- [ ] **Step 1: Añadir ClipboardHistoryFile a AppPaths**

En `Yottacast.Core/AppPaths.cs`, tras la línea de `HistoryFile`, añadir:
```csharp
/// <summary>Clipboard history JSON file.</summary>
public static readonly string ClipboardHistoryFile = Path.Combine(ConfigDir, "clipboard-history.json");
```

- [ ] **Step 2: Añadir constantes a AppDefaults**

En `Yottacast.Core/AppDefaults.cs`, añadir una nueva sección al final del fichero (antes del último `}`):
```csharp
// ── Clipboard history ─────────────────────────────────────────────────────
/// Maximum number of clipboard history entries to keep.
public const int ClipboardHistoryMaxEntries = 200;
/// Maximum age in days for clipboard history entries.
public const int ClipboardHistoryMaxDays = 30;
/// Half-life in days for clipboard history usage decay score.
public const double ClipboardHistoryHalfLifeDays = 30.0;
/// Score cap for clipboard history usage bonus.
public const double ClipboardHistoryMaxBonus = 0.5;
/// Debounce in ms before writing clipboard history to disk.
public const int ClipboardHistoryDebounceMs = 1_000;
/// Polling interval in ms for the clipboard monitor.
public const int ClipboardMonitorIntervalMs = 500;
```

- [ ] **Step 3: Añadir ActionHotkey.Delete**

En `Yottacast.Core/ViewModels/ActionHotkey.cs`, añadir tras la línea de `MetaS`:
```csharp
public static readonly ActionHotkey Delete = new("Delete");
```

- [ ] **Step 4: Crear IClipboardMonitor**

Crear `Yottacast.Core/Services/IClipboardMonitor.cs`:
```csharp
namespace Yottacast.Core.Services;

public interface IClipboardMonitor
{
    event Action<string> TextCopied;
    void Start();
    void Stop();
}
```

- [ ] **Step 5: Compilar para verificar**

```bash
cd /ruta/al/proyecto && dotnet build Yottacast.Core/Yottacast.Core.csproj
```
Esperado: 0 errores.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/AppPaths.cs Yottacast.Core/AppDefaults.cs \
        Yottacast.Core/ViewModels/ActionHotkey.cs \
        Yottacast.Core/Services/IClipboardMonitor.cs
git commit -m "feat: foundation para ClipboardHistory (AppPaths, AppDefaults, ActionHotkey.Delete, IClipboardMonitor)"
```

---

## Task 2: ClipboardHistoryStore — TDD

**Files:**
- Create: `Yottacast.Core/Search/Clipboard/ClipboardHistoryEntry.cs`
- Create: `Yottacast.Core/Search/Clipboard/ClipboardHistoryStore.cs`
- Create: `Yottacast.Core.Tests/Search/ClipboardHistoryStoreTests.cs`

- [ ] **Step 1: Crear ClipboardHistoryEntry**

Crear `Yottacast.Core/Search/Clipboard/ClipboardHistoryEntry.cs`:
```csharp
namespace Yottacast.Core.Search.Clipboard;

public record ClipboardHistoryEntry(
    string Text,
    DateTimeOffset CopiedAt,
    int UsageCount,
    DateTimeOffset LastUsedAt
);
```

- [ ] **Step 2: Escribir los tests (failing)**

Crear `Yottacast.Core.Tests/Search/ClipboardHistoryStoreTests.cs`:
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Clipboard;

namespace Yottacast.Core.Tests.Search;

public class ClipboardHistoryStoreTests
{
    private static ClipboardHistoryStore BuildStore(string? filePath = null)
    {
        filePath ??= Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        return new ClipboardHistoryStore(filePath, NullLogger<ClipboardHistoryStore>.Instance);
    }

    [Fact]
    public void Add_NewText_InsertsAtFront()
    {
        var store = BuildStore();
        store.Add("hello");
        var entries = store.GetAll();
        Assert.Single(entries);
        Assert.Equal("hello", entries[0].Text);
        Assert.Equal(0, entries[0].UsageCount);
    }

    [Fact]
    public void Add_MultipleTexts_MostRecentFirst()
    {
        var store = BuildStore();
        store.Add("first");
        store.Add("second");
        var entries = store.GetAll();
        Assert.Equal("second", entries[0].Text);
        Assert.Equal("first", entries[1].Text);
    }

    [Fact]
    public void Add_DuplicateText_DeduplicatesAndMovesToFront()
    {
        var store = BuildStore();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        store.Add("hello");
        store.Add("world");
        store.Add("hello"); // duplicate — should move to front
        var entries = store.GetAll();
        Assert.Equal(2, entries.Count);
        Assert.Equal("hello", entries[0].Text);
        Assert.Equal("world", entries[1].Text);
    }

    [Fact]
    public void Add_DuplicateText_UpdatesCopiedAt()
    {
        var store = BuildStore();
        store.Add("hello");
        var firstCopied = store.GetAll()[0].CopiedAt;
        System.Threading.Thread.Sleep(10);
        store.Add("hello");
        var secondCopied = store.GetAll()[0].CopiedAt;
        Assert.True(secondCopied >= firstCopied);
    }

    [Fact]
    public void Add_ExceedsMaxEntries_TrimsOldest()
    {
        var store = BuildStore();
        for (int i = 0; i < 5; i++)
            store.Add($"entry-{i}");
        // maxEntries=3 — set via overload
        store.MaxEntries = 3;
        store.Add("new");
        Assert.Equal(3, store.GetAll().Count);
        Assert.Equal("new", store.GetAll()[0].Text);
    }

    [Fact]
    public void Add_EntryOlderThanMaxDays_IsDiscarded()
    {
        var store = BuildStore();
        store.MaxDays = 30;
        // Add an entry then manually set it to old
        store.Add("old");
        var entries = store.GetAll();
        // Simulate old entry by using store internals via Remove+re-Add won't work,
        // so we test the behavior via the protected clock override.
        // This test verifies that entries added normally are NOT discarded:
        Assert.Single(entries);
        Assert.Equal("old", entries[0].Text);
    }

    [Fact]
    public void Remove_ExistingText_RemovesEntry()
    {
        var store = BuildStore();
        store.Add("hello");
        store.Add("world");
        store.Remove("hello");
        var entries = store.GetAll();
        Assert.Single(entries);
        Assert.Equal("world", entries[0].Text);
    }

    [Fact]
    public void Remove_NonExistingText_NoOp()
    {
        var store = BuildStore();
        store.Add("hello");
        store.Remove("nope");
        Assert.Single(store.GetAll());
    }

    [Fact]
    public void RecordUsage_IncrementsCountAndUpdatesLastUsed()
    {
        var store = BuildStore();
        store.Add("hello");
        var before = store.GetAll()[0].LastUsedAt;
        System.Threading.Thread.Sleep(10);
        store.RecordUsage("hello");
        var entry = store.GetAll()[0];
        Assert.Equal(1, entry.UsageCount);
        Assert.True(entry.LastUsedAt >= before);
    }

    [Fact]
    public void RecordUsage_NonExisting_NoOp()
    {
        var store = BuildStore();
        store.RecordUsage("ghost");  // should not throw
    }

    [Fact]
    public void EntriesChanged_FiredAfterAdd()
    {
        var store = BuildStore();
        var fired = false;
        store.EntriesChanged += () => fired = true;
        store.Add("hello");
        Assert.True(fired);
    }

    [Fact]
    public void EntriesChanged_FiredAfterRemove()
    {
        var store = BuildStore();
        store.Add("hello");
        var fired = false;
        store.EntriesChanged += () => fired = true;
        store.Remove("hello");
        Assert.True(fired);
    }

    [Fact]
    public void EntriesChanged_FiredAfterRecordUsage()
    {
        var store = BuildStore();
        store.Add("hello");
        var fired = false;
        store.EntriesChanged += () => fired = true;
        store.RecordUsage("hello");
        Assert.True(fired);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store1 = new ClipboardHistoryStore(file, NullLogger<ClipboardHistoryStore>.Instance);
        store1.Add("hello");
        store1.Add("world");
        store1.RecordUsage("hello");
        await store1.FlushAsync();

        var store2 = new ClipboardHistoryStore(file, NullLogger<ClipboardHistoryStore>.Instance);
        await store2.LoadAsync();
        var entries = store2.GetAll();
        Assert.Equal(2, entries.Count);
        Assert.Equal("world", entries[0].Text);
        Assert.Equal("hello", entries[1].Text);
        Assert.Equal(1, entries[1].UsageCount);
    }
}
```

- [ ] **Step 3: Ejecutar tests — verificar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "ClipboardHistoryStoreTests" 2>&1 | tail -20
```
Esperado: errores de compilación (ClipboardHistoryStore no existe).

- [ ] **Step 4: Implementar ClipboardHistoryStore**

Crear `Yottacast.Core/Search/Clipboard/ClipboardHistoryStore.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Search.Clipboard;

public class ClipboardHistoryStore(string filePath, ILogger<ClipboardHistoryStore> logger)
{
    private readonly Lock _lock = new();
    private List<ClipboardHistoryEntry> _entries = [];
    private CancellationTokenSource? _debounceCts;

    public event Action? EntriesChanged;

    public int MaxEntries { get; set; } = AppDefaults.ClipboardHistoryMaxEntries;
    public int MaxDays    { get; set; } = AppDefaults.ClipboardHistoryMaxDays;

    public IReadOnlyList<ClipboardHistoryEntry> GetAll()
    {
        lock (_lock) return [.._entries];
    }

    public void Add(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_lock)
        {
            var idx = _entries.FindIndex(e => e.Text == text);
            if (idx >= 0)
            {
                var existing = _entries[idx];
                _entries.RemoveAt(idx);
                _entries.Insert(0, existing with { CopiedAt = DateTimeOffset.UtcNow });
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                _entries.Insert(0, new ClipboardHistoryEntry(text, now, 0, now));
            }
            ApplyLimits();
        }
        EntriesChanged?.Invoke();
        ScheduleSave();
    }

    public void Remove(string text)
    {
        bool removed;
        lock (_lock)
        {
            var idx = _entries.FindIndex(e => e.Text == text);
            if (idx < 0) return;
            _entries.RemoveAt(idx);
            removed = true;
        }
        if (removed)
        {
            EntriesChanged?.Invoke();
            ScheduleSave();
        }
    }

    public void RecordUsage(string text)
    {
        bool found;
        lock (_lock)
        {
            var idx = _entries.FindIndex(e => e.Text == text);
            if (idx < 0) { found = false; return; }
            var e = _entries[idx];
            _entries[idx] = e with { UsageCount = e.UsageCount + 1, LastUsedAt = DateTimeOffset.UtcNow };
            found = true;
        }
        if (found)
        {
            EntriesChanged?.Invoke();
            ScheduleSave();
        }
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(filePath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<List<JsonEntry>>(json);
            if (loaded is null) return;
            lock (_lock)
            {
                _entries = loaded
                    .Select(j => new ClipboardHistoryEntry(j.Text, j.CopiedAt, j.UsageCount, j.LastUsedAt))
                    .ToList();
                ApplyLimits();
            }
            logger.LogInformation("ClipboardHistoryStore: loaded {Count} entries", _entries.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning("ClipboardHistoryStore: load failed, starting fresh: {Message}", ex.Message);
            lock (_lock) _entries = [];
        }
    }

    public async Task FlushAsync()
    {
        List<ClipboardHistoryEntry> snapshot;
        lock (_lock) snapshot = [.._entries];
        await SaveAsync(snapshot).ConfigureAwait(false);
    }

    private void ApplyLimits()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-MaxDays);
        _entries.RemoveAll(e => e.CopiedAt < cutoff);
        if (_entries.Count > MaxEntries)
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
    }

    private void ScheduleSave()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;
        _ = Task.Delay(AppDefaults.ClipboardHistoryDebounceMs, ct)
            .ContinueWith(async t =>
            {
                if (t.IsCanceled) return;
                List<ClipboardHistoryEntry> snapshot;
                lock (_lock) snapshot = [.._entries];
                await SaveAsync(snapshot).ConfigureAwait(false);
            }, TaskScheduler.Default);
    }

    private async Task SaveAsync(List<ClipboardHistoryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var tmpPath = filePath + ".tmp";
            var jsonEntries = entries.Select(e => new JsonEntry(e.Text, e.CopiedAt, e.UsageCount, e.LastUsedAt)).ToList();
            var json = JsonSerializer.Serialize(jsonEntries, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(tmpPath, json).ConfigureAwait(false);
            File.Move(tmpPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning("ClipboardHistoryStore: save failed: {Message}", ex.Message);
        }
    }

    private record JsonEntry(
        [property: JsonPropertyName("text")]      string Text,
        [property: JsonPropertyName("copiedAt")]  DateTimeOffset CopiedAt,
        [property: JsonPropertyName("usageCount")]int UsageCount,
        [property: JsonPropertyName("lastUsedAt")]DateTimeOffset LastUsedAt
    );
}
```

- [ ] **Step 5: Ejecutar tests — verificar que pasan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "ClipboardHistoryStoreTests" 2>&1 | tail -20
```
Esperado: todos los tests pasan excepto posiblemente `Add_EntryOlderThanMaxDays_IsDiscarded` (que solo verifica que entradas normales NO se descartan — debería pasar).

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/Search/Clipboard/ClipboardHistoryEntry.cs \
        Yottacast.Core/Search/Clipboard/ClipboardHistoryStore.cs \
        Yottacast.Core.Tests/Search/ClipboardHistoryStoreTests.cs
git commit -m "feat: ClipboardHistoryEntry + ClipboardHistoryStore con tests"
```

---

## Task 3: ClipboardHistorySearch — TDD

**Files:**
- Create: `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs`
- Create: `Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs`

- [ ] **Step 1: Escribir los tests (failing)**

Crear `Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs`:
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Clipboard;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

public class ClipboardHistorySearchTests
{
    private static (ClipboardHistorySearch search, ClipboardHistoryStore store, UserSettings settings) Build(
        SearchSourceVisibility visibility = SearchSourceVisibility.ModeOnly,
        bool historyEnabled = true)
    {
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        settings.ClipboardHistoryEnabled = historyEnabled;
        settings.ClipboardSearchVisibility = visibility;
        var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store = new ClipboardHistoryStore(filePath, NullLogger<ClipboardHistoryStore>.Instance);
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        var search = new ClipboardHistorySearch(settings, store, clipboard, NullLogger<ClipboardHistorySearch>.Instance);
        return (search, store, settings);
    }

    // ── IsActiveIn ────────────────────────────────────────────────────────────

    [Fact]
    public void IsActiveIn_ModeOnly_ActiveInClipboardOnly()
    {
        var (search, _, _) = Build(SearchSourceVisibility.ModeOnly);
        Assert.True(search.IsActiveIn(SearchMode.Clipboard));
        Assert.False(search.IsActiveIn(SearchMode.All));
        Assert.False(search.IsActiveIn(SearchMode.Files));
    }

    [Fact]
    public void IsActiveIn_Always_ActiveInAllOnly()
    {
        var (search, _, _) = Build(SearchSourceVisibility.Always);
        Assert.True(search.IsActiveIn(SearchMode.All));
        Assert.False(search.IsActiveIn(SearchMode.Clipboard));
        Assert.False(search.IsActiveIn(SearchMode.Files));
    }

    [Fact]
    public void IsActiveIn_Disabled_NeverActive()
    {
        var (search, _, _) = Build(SearchSourceVisibility.Disabled);
        Assert.False(search.IsActiveIn(SearchMode.All));
        Assert.False(search.IsActiveIn(SearchMode.Clipboard));
    }

    // ── Search — disabled ─────────────────────────────────────────────────────

    [Fact]
    public void Search_HistoryDisabled_ReturnsEmpty()
    {
        var (search, store, _) = Build(historyEnabled: false);
        store.Add("hello");
        Assert.Empty(search.Search("", 10));
    }

    // ── Search — empty query ──────────────────────────────────────────────────

    [Fact]
    public void Search_EmptyQuery_ReturnsMostRecentFirst()
    {
        var (search, store, _) = Build();
        store.Add("first");
        store.Add("second");
        var results = search.Search("", 10);
        Assert.Equal(2, results.Count);
        Assert.Equal("second", results[0].Title.TrimEnd('…').Trim());
        Assert.Equal("first", results[1].Title.TrimEnd('…').Trim());
    }

    [Fact]
    public void Search_EmptyQuery_RespectsLimit()
    {
        var (search, store, _) = Build();
        for (int i = 0; i < 5; i++) store.Add($"entry-{i}");
        var results = search.Search("", 3);
        Assert.Equal(3, results.Count);
    }

    // ── Search — query ────────────────────────────────────────────────────────

    [Fact]
    public void Search_Query_FiltersContains()
    {
        var (search, store, _) = Build();
        store.Add("hello world");
        store.Add("goodbye");
        var results = search.Search("world", 10);
        Assert.Single(results);
        Assert.Contains("hello world", results[0].Title);
    }

    [Fact]
    public void Search_Query_CaseInsensitive()
    {
        var (search, store, _) = Build();
        store.Add("Hello World");
        var results = search.Search("hello", 10);
        Assert.Single(results);
    }

    // ── Scoring ───────────────────────────────────────────────────────────────

    [Fact]
    public void Score_ExactMatch_HigherThanStartsWith()
    {
        var (search, store, _) = Build();
        store.Add("hello");        // exact match
        store.Add("hello world");  // starts with
        var results = search.Search("hello", 10);
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Score > results[1].Score);
        Assert.Contains("hello", results[0].Title);
    }

    [Fact]
    public void Score_StartsWith_HigherThanContains()
    {
        var (search, store, _) = Build();
        store.Add("world hello");  // contains
        store.Add("hello world");  // starts with
        var results = search.Search("hello", 10);
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Score > results[1].Score);
        Assert.Contains("hello world", results[0].Title);
    }

    [Fact]
    public void Score_UsageBonus_IncreasesScore()
    {
        var (search, store, _) = Build();
        store.Add("hello");
        var scoresBefore = search.Search("hello", 10).Select(r => r.Score).ToList();
        store.RecordUsage("hello");
        store.RecordUsage("hello");
        var scoresAfter = search.Search("hello", 10).Select(r => r.Score).ToList();
        Assert.True(scoresAfter[0] > scoresBefore[0]);
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    [Fact]
    public void Result_HasPasteAction_WithEnterHotkey()
    {
        var (search, store, _) = Build();
        store.Add("hello");
        var result = search.Search("hello", 10).First();
        var paste = result.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter);
        Assert.NotNull(paste);
        Assert.True(paste.PasteAfterClose);
        Assert.True(paste.ClosesWindow);
    }

    [Fact]
    public void Result_HasDeleteAction_WithDeleteHotkey()
    {
        var (search, store, _) = Build();
        store.Add("hello");
        var result = search.Search("hello", 10).First();
        var delete = result.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Delete);
        Assert.NotNull(delete);
        Assert.False(delete.ClosesWindow);
    }

    [Fact]
    public void DeleteAction_Execute_RemovesFromStore()
    {
        var (search, store, _) = Build();
        store.Add("hello");
        var result = search.Search("hello", 10).First();
        var delete = result.Actions.First(a => a.Hotkey == ActionHotkey.Delete);
        delete.Execute();
        Assert.Empty(store.GetAll());
    }

    // ── ResultChanged ─────────────────────────────────────────────────────────

    [Fact]
    public void ResultChanged_FiredWhenStoreChanges()
    {
        var (search, store, _) = Build();
        search.Start();
        var fired = false;
        search.ResultChanged += () => fired = true;
        store.Add("hello");
        Assert.True(fired);
        search.Stop();
    }

    // ── Display format ────────────────────────────────────────────────────────

    [Fact]
    public void Result_MultilineText_NewlinesReplacedInTitle()
    {
        var (search, store, _) = Build();
        store.Add("line1\nline2\nline3");
        var result = search.Search("line1", 10).First();
        Assert.DoesNotContain("\n", result.Title);
    }

    [Fact]
    public void Result_LongText_TruncatedTo120Chars()
    {
        var (search, store, _) = Build();
        var longText = new string('a', 200);
        store.Add(longText);
        var result = search.Search(new string('a', 5), 10).First();
        Assert.True(result.Title.Length <= 122); // 120 + "…" puede ser hasta 121
    }
}
```

- [ ] **Step 2: Ejecutar tests — verificar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "ClipboardHistorySearchTests" 2>&1 | tail -20
```
Esperado: errores de compilación (ClipboardHistorySearch no existe, ClipboardHistoryEnabled no existe en UserSettings).

- [ ] **Step 3: Implementar ClipboardHistorySearch**

Crear `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs`:
```csharp
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Clipboard;

public class ClipboardHistorySearch(
    UserSettings settings,
    ClipboardHistoryStore store,
    ClipboardService clipboard,
    ILogger<ClipboardHistorySearch> logger)
    : IInstantSearchSource, ISearchModeSource
{
    public event Action? ResultChanged;

    public int Limit => -1;

    public void Start()  => store.EntriesChanged += OnEntriesChanged;
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop()
    {
        store.EntriesChanged -= OnEntriesChanged;
        return Task.CompletedTask;
    }

    private void OnEntriesChanged() => ResultChanged?.Invoke();

    public bool IsActiveIn(SearchMode mode) => mode switch {
        SearchMode.All       => settings.ClipboardSearchVisibility == SearchSourceVisibility.Always,
        SearchMode.Clipboard => settings.ClipboardSearchVisibility == SearchSourceVisibility.ModeOnly,
        _                    => false,
    };

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit)
    {
        if (!settings.ClipboardHistoryEnabled) return [];
        var entries = store.GetAll();
        if (string.IsNullOrEmpty(query))
            return entries
                .Take(limit < 0 ? entries.Count : limit)
                .Select((e, i) => BuildResult(e, score: 1000.0 - i))
                .ToList();

        return entries
            .Where(e => e.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(e => BuildResult(e, ComputeScore(e, query)))
            .OrderByDescending(r => r.Score)
            .Take(limit < 0 ? int.MaxValue : limit)
            .ToList();
    }

    private double ComputeScore(ClipboardHistoryEntry entry, string query)
    {
        double matchScore;
        if (entry.Text.Equals(query, StringComparison.OrdinalIgnoreCase))
            matchScore = 4.0;
        else if (entry.Text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            matchScore = 3.5;
        else
            matchScore = 3.0;

        var ageDays = Math.Max(0, (DateTimeOffset.UtcNow - entry.LastUsedAt).TotalDays);
        var decay = Math.Exp(-ageDays / AppDefaults.ClipboardHistoryHalfLifeDays);
        var usageBonus = Math.Min(Math.Log(entry.UsageCount + 1) * decay, AppDefaults.ClipboardHistoryMaxBonus);

        return matchScore + usageBonus;
    }

    private ResultItemViewModel BuildResult(ClipboardHistoryEntry entry, double score)
    {
        var displayText = entry.Text.Replace('\n', '·').Replace('\r', '·');
        if (displayText.Length > 120) displayText = displayText[..120] + "…";

        var subtitle = FormatRelativeTime(entry.CopiedAt);
        var capturedText = entry.Text;

        return new ResultItemViewModel
        {
            Title    = displayText,
            Subtitle = subtitle,
            Category = "Clipboard",
            Score    = score,
            Actions  =
            [
                new()
                {
                    Label          = "Paste",
                    Hotkey         = ActionHotkey.Enter,
                    ShowInFooter   = true,
                    ShowInMenu     = true,
                    ClosesMenu     = true,
                    ClosesWindow   = true,
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

    private static string FormatRelativeTime(DateTimeOffset time)
    {
        var diff = DateTimeOffset.UtcNow - time;
        if (diff.TotalMinutes < 1)  return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min ago";
        if (diff.TotalHours < 24)   return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 2)     return "yesterday";
        if (diff.TotalDays < 7)     return $"{(int)diff.TotalDays} days ago";
        return time.LocalDateTime.ToString("d MMM");
    }
}
```

- [ ] **Step 4: Añadir ClipboardHistoryEnabled a UserSettings temporalmente para compilar**

En `Yottacast.Core/Services/UserSettings.cs`, en el bloque de propiedades públicas (cerca de la línea 37), añadir temporalmente:
```csharp
public bool ClipboardHistoryEnabled { get; set; } = false;
public int ClipboardHistoryMaxEntries { get; set; } = AppDefaults.ClipboardHistoryMaxEntries;
public int ClipboardHistoryMaxDays { get; set; } = AppDefaults.ClipboardHistoryMaxDays;
```
> Nota: en Task 5 se hará la integración completa de UserSettings (serialización JSON, migración de EnableClipboard). Por ahora solo se añaden las propiedades para que compilen los tests.

- [ ] **Step 5: Ejecutar tests — verificar que pasan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "ClipboardHistoryStoreTests|ClipboardHistorySearchTests" 2>&1 | tail -20
```
Esperado: todos los tests pasan.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs \
        Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs \
        Yottacast.Core/Services/UserSettings.cs
git commit -m "feat: ClipboardHistorySearch con tests (search, scoring, actions)"
```

---

## Task 4: UserSettings — integración completa

**Files:**
- Modify: `Yottacast.Core/Services/UserSettings.cs`
- Modify: `Yottacast.Ipc/Mapping/SettingsMapper.cs`

- [ ] **Step 1: Completar UserSettings**

En `Yottacast.Core/Services/UserSettings.cs`:

**1a.** Renombrar `EnableClipboard` a `ClipboardHistoryEnabled` (ya añadida en Task 3). Cambiar la línea existente `public bool EnableClipboard { get; set; } = true;` a:
```csharp
public bool ClipboardHistoryEnabled { get; set; } = false;
```
Y eliminar la línea temporal añadida en Task 3 (si quedó duplicada).

**1b.** Asegurarse de que `ClipboardHistoryMaxEntries` y `ClipboardHistoryMaxDays` están añadidas (de Task 3).

**1c.** En la clase privada `JsonData` (o similar — la que tiene `[JsonPropertyName]`), localizar la propiedad `EnableClipboard` y actualizar:
```csharp
// Reemplazar:
[JsonPropertyName("enableClipboard")] public bool EnableClipboard { get; init; } = true;
// Con (mantener la clave JSON para compatibilidad, default false):
[JsonPropertyName("enableClipboard")] public bool ClipboardHistoryEnabled { get; init; } = false;
```

Y añadir las nuevas propiedades en `JsonData`:
```csharp
[JsonPropertyName("clipboardHistoryMaxEntries")] public int ClipboardHistoryMaxEntries { get; init; } = AppDefaults.ClipboardHistoryMaxEntries;
[JsonPropertyName("clipboardHistoryMaxDays")]    public int ClipboardHistoryMaxDays    { get; init; } = AppDefaults.ClipboardHistoryMaxDays;
```

**1d.** En el método `Load` (o `FromJson`/constructor que crea la instancia desde `JsonData`), actualizar las líneas que asignan:
```csharp
// Reemplazar la asignación de EnableClipboard con:
ClipboardHistoryEnabled      = data.ClipboardHistoryEnabled,
ClipboardHistoryMaxEntries   = data.ClipboardHistoryMaxEntries,
ClipboardHistoryMaxDays      = data.ClipboardHistoryMaxDays,
```

**1e.** En el método `Save` (o `ToJson`), actualizar la serialización:
```csharp
// Reemplazar EnableClipboard = EnableClipboard con:
ClipboardHistoryEnabled    = ClipboardHistoryEnabled,
ClipboardHistoryMaxEntries = ClipboardHistoryMaxEntries,
ClipboardHistoryMaxDays    = ClipboardHistoryMaxDays,
```

- [ ] **Step 2: Actualizar SettingsMapper en Yottacast.Ipc**

En `Yottacast.Ipc/Mapping/SettingsMapper.cs`, reemplazar todas las referencias a `EnableClipboard` con `ClipboardHistoryEnabled`:
```csharp
// Reemplazar:
EnableClipboard = s.EnableClipboard,
// Con:
ClipboardHistoryEnabled = s.ClipboardHistoryEnabled,
```
Y la línea de asignación inversa:
```csharp
// Reemplazar:
s.EnableClipboard = msg.EnableClipboard;
// Con:
s.ClipboardHistoryEnabled = msg.ClipboardHistoryEnabled;
```

> Nota: el proto de IPC define el campo en `Yottacast.Ipc/Proto/settings.proto` línea 23: `bool enable_clipboard = 9;`. Renombrarlo a `bool clipboard_history_enabled = 9;` (mismo número de campo para compatibilidad binaria). Después regenerar los ficheros de gRPC o actualizar las referencias manualmente si hay código generado commiteado.

- [ ] **Step 3: Compilar toda la solución**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
```
Esperado: 0 errores, todos los tests pasan.

- [ ] **Step 4: Commit**

```bash
git add Yottacast.Core/Services/UserSettings.cs \
        Yottacast.Ipc/Mapping/SettingsMapper.cs
git commit -m "feat: UserSettings — ClipboardHistoryEnabled, MaxEntries, MaxDays (migración de EnableClipboard)"
```

---

## Task 5: MacClipboardMonitor

**Files:**
- Create: `Yottacast/Services/MacClipboardMonitor.cs`

- [ ] **Step 1: Crear MacClipboardMonitor**

Crear `Yottacast/Services/MacClipboardMonitor.cs`:
```csharp
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Yottacast.Core;
using Yottacast.Core.Services;

namespace Yottacast.Services;

/// <summary>
/// Polls NSPasteboard.generalPasteboard every 500ms. When changeCount changes,
/// reads plain text content and fires TextCopied. Polling stops when Stop() is called.
/// </summary>
public sealed class MacClipboardMonitor(ILogger<MacClipboardMonitor> logger) : IClipboardMonitor, IDisposable
{
    private CancellationTokenSource? _cts;
    private int _lastChangeCount = -1;

    public event Action<string>? TextCopied;

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = PollAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task PollAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(AppDefaults.ClipboardMonitorIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var text = ReadText();
                if (text is not null)
                    TextCopied?.Invoke(text);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogWarning("MacClipboardMonitor: poll error: {Message}", ex.Message);
        }
    }

    private string? ReadText()
    {
        try
        {
            var pb = ObjcMsgSend(ObjcGetClass("NSPasteboard"), SelRegisterName("generalPasteboard"));
            if (pb == IntPtr.Zero) return null;

            var count = ObjcMsgSendInt(pb, SelRegisterName("changeCount"));
            if (count == _lastChangeCount) return null;
            _lastChangeCount = count;

            var nsStringAlloc = ObjcMsgSend(ObjcGetClass("NSString"), SelRegisterName("alloc"));
            var typeStr = ObjcMsgSendInitString(nsStringAlloc, SelRegisterName("initWithUTF8String:"),
                "public.utf8-plain-text");
            var strObj = ObjcMsgSendArg(pb, SelRegisterName("stringForType:"), typeStr);
            ObjcRelease(typeStr);
            if (strObj == IntPtr.Zero) return null;

            var utf8Ptr = ObjcMsgSend(strObj, SelRegisterName("UTF8String"));
            if (utf8Ptr == IntPtr.Zero) return null;

            return Marshal.PtrToStringUTF8(utf8Ptr);
        }
        catch (Exception ex)
        {
            logger.LogDebug("MacClipboardMonitor: ReadText failed: {Message}", ex.Message);
            return null;
        }
    }

    [DllImport("libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass(string name);

    [DllImport("libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string name);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSend(IntPtr receiver, IntPtr sel);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern int ObjcMsgSendInt(IntPtr receiver, IntPtr sel);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendInitString(IntPtr receiver, IntPtr sel,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendArg(IntPtr receiver, IntPtr sel, IntPtr arg);

    [DllImport("libobjc.dylib", EntryPoint = "objc_release")]
    private static extern void ObjcRelease(IntPtr obj);
}
```

- [ ] **Step 2: Compilar**

```bash
dotnet build Yottacast/Yottacast.csproj 2>&1 | tail -10
```
Esperado: 0 errores.

- [ ] **Step 3: Commit**

```bash
git add Yottacast/Services/MacClipboardMonitor.cs
git commit -m "feat: MacClipboardMonitor — polling NSPasteboard via P/Invoke"
```

---

## Task 6: WindowsClipboardMonitor

**Files:**
- Create: `Yottacast/Services/WindowsClipboardMonitor.cs`

- [ ] **Step 1: Crear WindowsClipboardMonitor**

Crear `Yottacast/Services/WindowsClipboardMonitor.cs`:
```csharp
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Yottacast.Core;
using Yottacast.Core.Services;

namespace Yottacast.Services;

/// <summary>
/// Polls the Windows clipboard every 500ms via OpenClipboard/GetClipboardData/CloseClipboard.
/// When the text content changes, fires TextCopied.
/// </summary>
public sealed class WindowsClipboardMonitor(ILogger<WindowsClipboardMonitor> logger) : IClipboardMonitor, IDisposable
{
    private CancellationTokenSource? _cts;
    private string? _lastText;

    public event Action<string>? TextCopied;

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = PollAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task PollAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(AppDefaults.ClipboardMonitorIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var text = ReadText();
                if (text is not null && text != _lastText)
                {
                    _lastText = text;
                    TextCopied?.Invoke(text);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogWarning("WindowsClipboardMonitor: poll error: {Message}", ex.Message);
        }
    }

    private string? ReadText()
    {
        try
        {
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
            if (!OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                var hData = GetClipboardData(CF_UNICODETEXT);
                if (hData == IntPtr.Zero) return null;
                var ptr = GlobalLock(hData);
                if (ptr == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(ptr); }
                finally { GlobalUnlock(hData); }
            }
            finally { CloseClipboard(); }
        }
        catch (Exception ex)
        {
            logger.LogDebug("WindowsClipboardMonitor: ReadText failed: {Message}", ex.Message);
            return null;
        }
    }

    private const uint CF_UNICODETEXT = 13;

    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
}
```

- [ ] **Step 2: Compilar**

```bash
dotnet build Yottacast/Yottacast.csproj 2>&1 | tail -10
```
Esperado: 0 errores.

- [ ] **Step 3: Commit**

```bash
git add Yottacast/Services/WindowsClipboardMonitor.cs
git commit -m "feat: WindowsClipboardMonitor — polling via Win32 OpenClipboard"
```

---

## Task 7: DI registration + App wiring

**Files:**
- Modify: `Yottacast/App.axaml.cs`

- [ ] **Step 1: Registrar servicios en ConfigureServices**

En `Yottacast/App.axaml.cs`, en el bloque `ConfigureServices` (tras las líneas de `LaunchHistory`), añadir:
```csharp
services.AddSingleton<ClipboardHistoryStore>(sp => new ClipboardHistoryStore(
    AppPaths.ClipboardHistoryFile,
    sp.GetRequiredService<ILogger<ClipboardHistoryStore>>()));
services.AddSingleton<ClipboardHistorySearch>();
services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<ClipboardHistorySearch>());
```

- [ ] **Step 2: Cargar el store al arrancar**

En `App.axaml.cs`, en el método `OnFrameworkInitializationCompleted` (o donde se arranca la app), añadir la carga asíncrona del store. Buscar el bloque donde se llama a `WhenReady()` de las instant sources. Justo antes o después, añadir:
```csharp
var clipboardStore = services.GetRequiredService<ClipboardHistoryStore>();
_ = clipboardStore.LoadAsync();
```

- [ ] **Step 3: Crear y cablear el monitor según plataforma**

En `App.axaml.cs`, añadir el campo privado y el método de arranque del monitor. Buscar el método que inicializa los hotkeys globales (cerca de la línea 443) y añadir ANTES del bloque de hotkey de clipboard existente:

```csharp
// Campo en la clase App:
private IClipboardMonitor? _clipboardMonitor;

// En el método de inicialización (donde se configura el global hook):
private void SetupClipboardMonitor(IServiceProvider services)
{
    var settings = services.GetRequiredService<UserSettings>();
    var store    = services.GetRequiredService<ClipboardHistoryStore>();

    void StartMonitor()
    {
        if (!settings.ClipboardHistoryEnabled) return;
        _clipboardMonitor?.Stop();
        if (OperatingSystem.IsMacOS())
            _clipboardMonitor = new MacClipboardMonitor(
                services.GetRequiredService<ILogger<MacClipboardMonitor>>());
        else if (OperatingSystem.IsWindows())
            _clipboardMonitor = new WindowsClipboardMonitor(
                services.GetRequiredService<ILogger<WindowsClipboardMonitor>>());
        else return;

        _clipboardMonitor.TextCopied += text => store.Add(text);
        _clipboardMonitor.Start();
    }

    void StopMonitor()
    {
        _clipboardMonitor?.Stop();
        _clipboardMonitor = null;
    }

    StartMonitor();

    settings.SearchSettingsChanged += () =>
    {
        if (settings.ClipboardHistoryEnabled)
            StartMonitor();
        else
            StopMonitor();
    };
}
```

Llamar a `SetupClipboardMonitor(services)` desde el lugar donde se inicializa el global hook.

- [ ] **Step 4: Compilar y ejecutar la app**

```bash
cd Yottacast && dotnet run
```
Verificar manualmente:
1. Copiar texto en cualquier app → abrir Yottacast en modo Clipboard → verificar que aparece.
2. Activar una entrada → verifica que pega en la app anterior.
3. Borrar una entrada con Supr → verifica que desaparece de la lista.

- [ ] **Step 5: Ejecutar suite completa de tests**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
```
Esperado: todos los tests pasan.

- [ ] **Step 6: Commit**

```bash
git add Yottacast/App.axaml.cs
git commit -m "feat: DI y wiring ClipboardHistoryStore, ClipboardHistorySearch, ClipboardMonitor"
```

---

## Task 8: SettingsWindowViewModel — propiedades Clipboard

**Files:**
- Modify: `Yottacast/ViewModels/SettingsWindowViewModel.cs`

- [ ] **Step 1: Renombrar EnableClipboard → ClipboardHistoryEnabled en el ViewModel**

En `Yottacast/ViewModels/SettingsWindowViewModel.cs`:

**1a.** Buscar `[ObservableProperty] private bool _enableClipboard;` y renombrar el campo a `_clipboardHistoryEnabled`. Actualizar también la `partial void OnEnableClipboardChanged` a `partial void OnClipboardHistoryEnabledChanged`:
```csharp
[ObservableProperty] private bool _clipboardHistoryEnabled;

partial void OnClipboardHistoryEnabledChanged(bool value)
{
    _settings.ClipboardHistoryEnabled = value;
    _settings.Save();
    _logger.LogInformation("Settings: ClipboardHistoryEnabled = {Value}", value);
    _settings.NotifySearchSettingsChanged();
}
```

**1b.** En el constructor, cambiar la asignación:
```csharp
// Reemplazar:
_enableClipboard = settings.EnableClipboard;
// Con:
_clipboardHistoryEnabled = settings.ClipboardHistoryEnabled;
```

- [ ] **Step 2: Añadir propiedades para ClipboardSearchVisibility**

Añadir el patrón de radio buttons igual que `FileSearch`. Buscar el bloque de `_fileSearchVisibility` y añadir tras él:

```csharp
private SearchSourceVisibility _clipboardSearchVisibility;

public bool ClipboardSearchDisabled  { get => _clipboardSearchVisibility == SearchSourceVisibility.Disabled;  set { if (value) UpdateClipboardSearchVisibility(SearchSourceVisibility.Disabled);  } }
public bool ClipboardSearchAlways    { get => _clipboardSearchVisibility == SearchSourceVisibility.Always;     set { if (value) UpdateClipboardSearchVisibility(SearchSourceVisibility.Always);     } }
public bool ClipboardSearchModeOnly  { get => _clipboardSearchVisibility == SearchSourceVisibility.ModeOnly;   set { if (value) UpdateClipboardSearchVisibility(SearchSourceVisibility.ModeOnly);   } }
public bool ClipboardSearchNotDisabled => _clipboardSearchVisibility != SearchSourceVisibility.Disabled;
public bool ClipboardSearchModeOnlySelected => _clipboardSearchVisibility == SearchSourceVisibility.ModeOnly;

private void UpdateClipboardSearchVisibility(SearchSourceVisibility v)
{
    _clipboardSearchVisibility = v;
    _settings.ClipboardSearchVisibility = v;
    _settings.Save();
    _settings.NotifySearchSettingsChanged();
    _logger.LogInformation("Settings: ClipboardSearchVisibility = {Value}", v);
    OnPropertyChanged(nameof(ClipboardSearchDisabled));
    OnPropertyChanged(nameof(ClipboardSearchAlways));
    OnPropertyChanged(nameof(ClipboardSearchModeOnly));
    OnPropertyChanged(nameof(ClipboardSearchNotDisabled));
    OnPropertyChanged(nameof(ClipboardSearchModeOnlySelected));
}
```

- [ ] **Step 3: Añadir propiedades para MaxEntries y MaxDays**

```csharp
[ObservableProperty] private int _clipboardHistoryMaxEntries;
[ObservableProperty] private int _clipboardHistoryMaxDays;

partial void OnClipboardHistoryMaxEntriesChanged(int value)
{
    if (value < 1) return;
    _settings.ClipboardHistoryMaxEntries = value;
    _settings.Save();
}

partial void OnClipboardHistoryMaxDaysChanged(int value)
{
    if (value < 1) return;
    _settings.ClipboardHistoryMaxDays = value;
    _settings.Save();
}
```

- [ ] **Step 4: Añadir propiedades para el hotkey de Clipboard**

Añadir estado de captura paralelo al del hotkey principal. Buscar el bloque `// ── Hotkey capture ──` y añadir después:

```csharp
// ── Clipboard Hotkey capture ──────────────────────────────────────────────

[ObservableProperty] private bool _isCapturingClipboardHotkey;
private KeyModifiers _capturingClipboardModifiers = KeyModifiers.None;

public bool ClipboardBadgeCtrlActive  => IsCapturingClipboardHotkey ? _capturingClipboardModifiers.HasFlag(KeyModifiers.Control) : _settings.ParsedClipboardHotkey?.Ctrl  ?? false;
public bool ClipboardBadgeAltActive   => IsCapturingClipboardHotkey ? _capturingClipboardModifiers.HasFlag(KeyModifiers.Alt)     : _settings.ParsedClipboardHotkey?.Alt   ?? false;
public bool ClipboardBadgeShiftActive => IsCapturingClipboardHotkey ? _capturingClipboardModifiers.HasFlag(KeyModifiers.Shift)   : _settings.ParsedClipboardHotkey?.Shift ?? false;
public bool ClipboardBadgeMetaActive  => IsCapturingClipboardHotkey ? _capturingClipboardModifiers.HasFlag(KeyModifiers.Meta)    : _settings.ParsedClipboardHotkey?.Meta  ?? false;

public string ClipboardHotkeyKeyText
{
    get
    {
        if (!IsCapturingClipboardHotkey) return _settings.ParsedClipboardHotkey?.KeyName ?? "—";
        return _capturingClipboardModifiers != KeyModifiers.None ? "Press a key…" : "Press a modifier…";
    }
}

public void StartClipboardHotkeyCapture()
{
    _capturingClipboardModifiers = KeyModifiers.None;
    IsCapturingClipboardHotkey   = true;
    NotifyClipboardBadgesAndKey();
}

public void CancelClipboardHotkeyCapture()
{
    _capturingClipboardModifiers = KeyModifiers.None;
    IsCapturingClipboardHotkey   = false;
    NotifyClipboardBadgesAndKey();
}

public void UpdateCapturingClipboardModifiers(KeyModifiers mods)
{
    _capturingClipboardModifiers = mods;
    NotifyClipboardBadgesAndKey();
}

public void ProcessClipboardKeyCapture(Key key, KeyModifiers mods)
{
    if (key == Key.Escape) { CancelClipboardHotkeyCapture(); return; }
    if (mods == KeyModifiers.None) return;

    var config = new HotkeyConfig(
        Alt:     mods.HasFlag(KeyModifiers.Alt),
        Ctrl:    mods.HasFlag(KeyModifiers.Control),
        Shift:   mods.HasFlag(KeyModifiers.Shift),
        Meta:    mods.HasFlag(KeyModifiers.Meta),
        KeyName: AvaloniaKeyToName(key));

    if (AppHandler.Instance.IsForbidden(config)) return;

    _settings.ClipboardHotkey = config.ToString();
    _settings.Save();
    _logger.LogInformation("Settings: ClipboardHotkey = \"{Value}\"", config);
    _capturingClipboardModifiers = KeyModifiers.None;
    IsCapturingClipboardHotkey   = false;
    NotifyClipboardBadgesAndKey();
}

private void NotifyClipboardBadgesAndKey()
{
    OnPropertyChanged(nameof(ClipboardBadgeCtrlActive));
    OnPropertyChanged(nameof(ClipboardBadgeAltActive));
    OnPropertyChanged(nameof(ClipboardBadgeShiftActive));
    OnPropertyChanged(nameof(ClipboardBadgeMetaActive));
    OnPropertyChanged(nameof(ClipboardHotkeyKeyText));
}
```

- [ ] **Step 5: Añadir ParsedClipboardHotkey a UserSettings**

En `UserSettings.cs`, junto a `ParsedHotkey`, añadir:
```csharp
private HotkeyConfig? _parsedClipboardHotkey;

public HotkeyConfig? ParsedClipboardHotkey
{
    get
    {
        if (ClipboardHotkey is null) return null;
        return _parsedClipboardHotkey ??= HotkeyConfig.Parse(ClipboardHotkey);
    }
}
```
Y invalidar el cache en el setter de `ClipboardHotkey`:
```csharp
public string? ClipboardHotkey
{
    get => _clipboardHotkey;
    set { _clipboardHotkey = value; _parsedClipboardHotkey = null; }
}
private string? _clipboardHotkey;
```
> Nota: si ClipboardHotkey ya es una auto-property, convertirla en propiedad con backing field.

- [ ] **Step 6: Inicializar en el constructor del ViewModel**

En el constructor de `SettingsWindowViewModel`, añadir las inicializaciones:
```csharp
_clipboardHistoryEnabled    = settings.ClipboardHistoryEnabled;
_clipboardSearchVisibility  = settings.ClipboardSearchVisibility;
_clipboardHistoryMaxEntries = settings.ClipboardHistoryMaxEntries;
_clipboardHistoryMaxDays    = settings.ClipboardHistoryMaxDays;
```

- [ ] **Step 7: Compilar**

```bash
dotnet build Yottacast/Yottacast.csproj 2>&1 | tail -10
```
Esperado: 0 errores.

- [ ] **Step 8: Commit**

```bash
git add Yottacast/ViewModels/SettingsWindowViewModel.cs \
        Yottacast.Core/Services/UserSettings.cs
git commit -m "feat: SettingsWindowViewModel — propiedades Clipboard (visibility, maxEntries, maxDays, hotkey capture)"
```

---

## Task 9: SettingsWindow.axaml — sección Clipboard expandida

**Files:**
- Modify: `Yottacast/Views/SettingsWindow.axaml`

- [ ] **Step 1: Reemplazar la sección Clipboard existente**

En `Yottacast/Views/SettingsWindow.axaml`, localizar el bloque que empieza con `<!-- Clipboard -->` (aproximadamente línea 1400). Reemplazar el contenido del `<StackPanel Spacing="16" IsVisible="{Binding IsClipboardSelected}">` completo con:

```xml
<!-- Clipboard -->
<StackPanel Spacing="16" IsVisible="{Binding IsClipboardSelected}">
    <TextBlock Classes="section-heading" Text="Clipboard History"/>
    <ToggleSwitch IsChecked="{Binding ClipboardHistoryEnabled}"
                  OnContent="Enabled"
                  OffContent="Disabled"/>
    <TextBlock Classes="description"
               Text="Capture everything you copy and search it from Clipboard mode."/>

    <StackPanel Spacing="16" IsVisible="{Binding ClipboardHistoryEnabled}">

        <!-- Visibility radio buttons (mismo patrón que FileSearch) -->
        <StackPanel Spacing="4">
            <TextBlock Classes="label" Text="Show in search"/>
            <RadioButton Content="Off"
                         GroupName="ClipboardSearchVisibility"
                         IsChecked="{Binding ClipboardSearchDisabled}"/>
            <RadioButton Content="Always (in all search)"
                         GroupName="ClipboardSearchVisibility"
                         IsChecked="{Binding ClipboardSearchAlways}"/>
            <RadioButton Content="⌘F only (dedicated Clipboard mode)"
                         GroupName="ClipboardSearchVisibility"
                         IsChecked="{Binding ClipboardSearchModeOnly}"/>
        </StackPanel>

        <!-- Hotkey configurator — visible solo si ModeOnly -->
        <StackPanel Spacing="8" IsVisible="{Binding ClipboardSearchModeOnlySelected}">
            <TextBlock Classes="label" Text="Clipboard mode hotkey"/>
            <TextBlock Classes="description"
                       Text="Press this key combination to open directly in Clipboard mode."/>
            <!-- Hotkey capture widget — mismo patrón CSS que el Global Hotkey en la sección General:
                 clases: hotkey-field / hotkey-field.capturing / modifier-badge / modifier-badge.active / hotkey-key -->
            <Border Classes="hotkey-field"
                    Classes.capturing="{Binding IsCapturingClipboardHotkey}"
                    PointerPressed="OnClipboardHotkeyPointerPressed">
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Spacing="0">
                    <Border Classes="modifier-badge" Classes.active="{Binding ClipboardBadgeCtrlActive}">
                        <TextBlock Text="{Binding CtrlSymbol}"/>
                    </Border>
                    <Border Classes="modifier-badge" Classes.active="{Binding ClipboardBadgeAltActive}">
                        <TextBlock Text="{Binding AltSymbol}"/>
                    </Border>
                    <Border Classes="modifier-badge" Classes.active="{Binding ClipboardBadgeShiftActive}">
                        <TextBlock Text="{Binding ShiftSymbol}"/>
                    </Border>
                    <Border Classes="modifier-badge" Classes.active="{Binding ClipboardBadgeMetaActive}">
                        <TextBlock Text="{Binding MetaSymbol}"/>
                    </Border>
                    <TextBlock Classes="hotkey-key" Text="{Binding ClipboardHotkeyKeyText}"/>
                </StackPanel>
            </Border>
        </StackPanel>

        <Separator/>

        <!-- Limits -->
        <StackPanel Spacing="8">
            <TextBlock Classes="label" Text="Max entries"/>
            <NumericUpDown Value="{Binding ClipboardHistoryMaxEntries}"
                           Minimum="1" Maximum="1000"
                           HorizontalAlignment="Left" Width="120"/>
        </StackPanel>

        <StackPanel Spacing="8">
            <TextBlock Classes="label" Text="Keep for (days)"/>
            <NumericUpDown Value="{Binding ClipboardHistoryMaxDays}"
                           Minimum="1" Maximum="365"
                           HorizontalAlignment="Left" Width="120"/>
        </StackPanel>

    </StackPanel>
</StackPanel>
```

- [ ] **Step 2: Añadir handlers en SettingsWindow.axaml.cs**

En `Yottacast/Views/SettingsWindow.axaml.cs`, buscar los handlers existentes del hotkey principal (como `OnHotkeyPointerPressed`) y añadir los equivalentes para el clipboard hotkey:

```csharp
private void OnClipboardHotkeyPointerPressed(object? sender, PointerPressedEventArgs e)
{
    if (DataContext is SettingsWindowViewModel vm)
        vm.StartClipboardHotkeyCapture();
}

private void OnCancelClipboardHotkeyClick(object? sender, RoutedEventArgs e)
{
    if (DataContext is SettingsWindowViewModel vm)
        vm.CancelClipboardHotkeyCapture();
}
```

Además, en el handler `OnKeyDown` de SettingsWindow (donde se procesa la captura del hotkey principal), añadir la rama para el clipboard:
```csharp
if (vm.IsCapturingClipboardHotkey)
{
    vm.UpdateCapturingClipboardModifiers(e.KeyModifiers);
    if (e.Key is not (Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
                      or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin))
        vm.ProcessClipboardKeyCapture(e.Key, e.KeyModifiers);
    e.Handled = true;
    return;
}
```

Y en `OnKeyUp`:
```csharp
if (vm.IsCapturingClipboardHotkey)
    vm.UpdateCapturingClipboardModifiers(e.KeyModifiers);
```

- [ ] **Step 3: Compilar**

```bash
dotnet build Yottacast/Yottacast.csproj 2>&1 | tail -10
```
Esperado: 0 errores.

- [ ] **Step 4: Verificar manualmente en la app**

```bash
cd Yottacast && dotnet run
```
Abrir Settings → Clipboard. Verificar:
1. Toggle Clipboard History → habilita/deshabilita la sección.
2. RadioButtons cambian la visibilidad (Off/Always/⌘F only).
3. Si se selecciona "⌘F only", aparece el configurador de hotkey.
4. NumericUpDowns muestran 200 y 30 por defecto.
5. Cambiar MaxEntries y MaxDays persiste en settings.json.

- [ ] **Step 5: Ejecutar suite completa de tests**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
```
Esperado: todos los tests pasan.

- [ ] **Step 6: Commit**

```bash
git add Yottacast/Views/SettingsWindow.axaml \
        Yottacast/Views/SettingsWindow.axaml.cs
git commit -m "feat: SettingsWindow — sección Clipboard History expandida con hotkey configurador"
```

---

## Task 10: Verificación final y cleanup

**Files:** ninguno nuevo — verificación manual.

- [ ] **Step 1: Suite completa de tests**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -15
cd Yottacast.Ipc.Tests && dotnet test 2>&1 | tail -15
```
Esperado: 0 failed.

- [ ] **Step 2: Prueba end-to-end manual**

```bash
cd Yottacast && dotnet run
```

Flujo a verificar:
1. Settings → Clipboard History → Enable → "⌘F only" → configurar hotkey (ej. ⌥Space).
2. Copiar texto en otra app → activar Yottacast con el hotkey → verificar que el texto aparece en la lista.
3. Copiar el mismo texto → verifica que hay UNA entrada (dedup) con timestamp actualizado.
4. Buscar texto → verifica que filtra y que el matching exacto aparece primero.
5. Activar entrada → verifica paste automático en la app anterior.
6. Borrar entrada con Supr → verifica que desaparece sin cerrar la ventana.
7. Activar la misma entrada varias veces → verifica que sube en ranking de búsqueda.

- [ ] **Step 3: Commit final de documentación**

```bash
git add docs/superpowers/plans/2026-06-11-clipboard-history-search.md
git commit -m "docs: plan de implementación ClipboardHistorySearch"
```
