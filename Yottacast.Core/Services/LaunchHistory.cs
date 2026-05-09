using System.Text.Json;
using Microsoft.Extensions.Logging;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Services;

/// <summary>
/// Tracks how often and how recently each item (app or file) has been launched.
/// Each record stores a launch count and the timestamp of last use.
/// Bonus scoring uses exponential decay: count × e^(-ageDays / halfLifeDays),
/// so recently-launched items rank above equally-counted but stale ones.
/// The bonus is capped at <see cref="AppDefaults.LaunchHistoryMaxBonus"/> so that
/// frequently-used apps cannot escape their score band (stay below Calculator/Emoji).
/// The file is written atomically (temp file + File.Move) to avoid corruption.
/// </summary>
public class LaunchHistory(string filePath, ILogger<LaunchHistory> logger, Func<DateTime>? clock = null) {

    private record LaunchRecord(int Count, DateTime LastUsedAt);

    private Dictionary<string, LaunchRecord> _data = new();

    private DateTime Now => clock?.Invoke() ?? DateTime.UtcNow;

    public void Record(string itemPath) {
        _data.TryGetValue(itemPath, out var existing);
        _data[itemPath] = new LaunchRecord(
            Count: (existing?.Count ?? 0) + 1,
            LastUsedAt: Now
        );
        Save();
    }

    /// <summary>
    /// Returns the score bonus for the given result item.
    /// Returns 0 if the item is not a <see cref="ResultItemViewModel"/> with a non-empty <see cref="ResultItemViewModel.ItemPath"/>.
    /// </summary>
    public double BonusFor(BaseResultItemViewModel item) {
        if (item is not ResultItemViewModel r || string.IsNullOrEmpty(r.ItemPath)) return 0;
        return Bonus(r.ItemPath);
    }

    private double Bonus(string itemPath) {
        if (!_data.TryGetValue(itemPath, out var rec)) return 0;
        var ageDays = Math.Max(0, (Now - rec.LastUsedAt).TotalDays);
        var decay = Math.Exp(-ageDays / AppDefaults.LaunchHistoryHalfLifeDays);
        return Math.Min(Math.Log(rec.Count + 1) * decay, AppDefaults.LaunchHistoryMaxBonus);
    }

    public async Task LoadAsync() {
        if (!File.Exists(filePath)) return;
        try {
            var json = await File.ReadAllTextAsync(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _data = new Dictionary<string, LaunchRecord>();
            foreach (var prop in root.EnumerateObject()) {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var count = prop.Value.TryGetProperty("count", out var cp) && cp.TryGetInt32(out var c) ? c : 0;
                var lastUsedAt = prop.Value.TryGetProperty("lastUsedAt", out var lp) && lp.TryGetDateTimeOffset(out var dt)
                    ? dt.UtcDateTime
                    : DateTime.UtcNow;
                _data[prop.Name] = new LaunchRecord(count, lastUsedAt);
            }

            logger.LogInformation("LaunchHistory loaded: {Count} entries", _data.Count);
        } catch (Exception ex) {
            logger.LogWarning("Failed to load launch history, starting fresh: {Message}", ex.Message);
            _data = new Dictionary<string, LaunchRecord>();
        }
    }

    private void Save() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var tmpPath = filePath + ".tmp";

            using (var stream = File.Create(tmpPath)) {
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();
                foreach (var (path, rec) in _data) {
                    writer.WritePropertyName(path);
                    writer.WriteStartObject();
                    writer.WriteNumber("count", rec.Count);
                    writer.WriteString("lastUsedAt", rec.LastUsedAt.ToString("O"));
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }

            File.Move(tmpPath, filePath, overwrite: true);
        } catch (Exception ex) {
            logger.LogWarning("Failed to save launch history: {Message}", ex.Message);
        }
    }
}
