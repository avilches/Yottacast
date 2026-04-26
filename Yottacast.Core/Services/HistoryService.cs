using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

public record HistoryEntry {
    public required string Query { get; init; }
    public string? ActionName { get; init; }
    public DateTime Timestamp { get; init; }
}

public class HistoryService {
    private readonly UserSettings _settings;
    private readonly ILogger<HistoryService> _logger;
    private readonly string _historyPath;
    private List<HistoryEntry> _entries = [];

    private record HistoryEntryData {
        [JsonPropertyName("query")]
        public string Query { get; init; } = "";

        [JsonPropertyName("actionName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ActionName { get; init; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public event Action? Changed;

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public HistoryService(UserSettings settings, ILogger<HistoryService> logger, string? historyPath = null) {
        _settings = settings;
        _logger = logger;
        _historyPath = historyPath ?? AppPaths.HistoryFile;
        Load();
    }

    public void Add(string query, string? actionName) {
        if (!_settings.EnableHistory) return;
        if (string.IsNullOrWhiteSpace(query)) return;
        _entries.Add(new HistoryEntry { Query = query, ActionName = actionName, Timestamp = DateTime.Now });
        while (_entries.Count > _settings.HistoryMaxItems)
            _entries.RemoveAt(0);
        Save();
        Changed?.Invoke();
    }

    public void Clear() {
        _entries.Clear();
        Save();
        Changed?.Invoke();
    }

    private void Load() {
        try {
            if (!File.Exists(_historyPath)) return;
            var data = JsonSerializer.Deserialize<List<HistoryEntryData>>(
                File.ReadAllText(_historyPath), JsonOptions);
            _entries = data?.Select(d => new HistoryEntry {
                Query = d.Query,
                ActionName = d.ActionName,
                Timestamp = d.Timestamp
            }).ToList() ?? [];
            _logger.LogInformation("History: loaded {Count} entries from {Path}", _entries.Count, _historyPath);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "History: failed to load from {Path}", _historyPath);
            _entries = [];
        }
    }

    private void Save() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
            var data = _entries.Select(e => new HistoryEntryData {
                Query = e.Query,
                ActionName = e.ActionName,
                Timestamp = e.Timestamp
            }).ToList();
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(data, JsonOptions));
        } catch (Exception ex) {
            _logger.LogWarning(ex, "History: failed to save to {Path}", _historyPath);
        }
    }
}
