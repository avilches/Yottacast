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
        bool removed = false;
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
        bool found = false;
        lock (_lock)
        {
            var idx = _entries.FindIndex(e => e.Text == text);
            if (idx >= 0)
            {
                var e = _entries[idx];
                _entries[idx] = e with { UsageCount = e.UsageCount + 1, LastUsedAt = DateTimeOffset.UtcNow };
                found = true;
            }
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
