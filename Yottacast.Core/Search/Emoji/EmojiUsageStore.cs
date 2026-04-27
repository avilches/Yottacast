using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Search.Emoji;

/// <summary>
/// Persists emoji favorites and usage records to a JSON file.
/// Each record stores a use count and the timestamp of last use.
/// Most-used ranking uses a decay score: count × 0.5^(daysSinceLastUse / halfLifeDays),
/// so recently-used emojis rank above equally-counted but stale ones.
/// Favorites are user-toggled and stored separately.
/// The file is written atomically (temp file + File.Move) to avoid corruption.
/// </summary>
public class EmojiUsageStore(string filePath, ILogger<EmojiUsageStore> logger) {

    private record UsageRecord(int Count, DateTime LastUsedAt);

    private List<string> _favorites = [];
    private Dictionary<string, UsageRecord> _usage = new();
    private readonly HashSet<string> _favoriteSet = new();

    public IReadOnlyList<string> Favorites => _favorites;

    public bool IsFavorite(string ch) => _favoriteSet.Contains(ch);

    public void ToggleFavorite(string ch) {
        if (_favoriteSet.Contains(ch)) {
            _favoriteSet.Remove(ch);
            _favorites.Remove(ch);
        } else {
            if (_favorites.Count >= AppDefaults.EmojiMaxFavorites) {
                // Evict the least-used favorite; ties broken by position (first = oldest added)
                var evict = _favorites.MinBy(f => GetUsageCount(f))!;
                _favoriteSet.Remove(evict);
                _favorites.Remove(evict);
            }
            _favoriteSet.Add(ch);
            _favorites.Add(ch);
        }
        Save();
    }

    public void RecordUsage(string ch) {
        _usage.TryGetValue(ch, out var existing);
        _usage[ch] = new UsageRecord(
            Count: (existing?.Count ?? 0) + 1,
            LastUsedAt: DateTime.UtcNow
        );
        Save();
    }

    public int GetUsageCount(string ch) =>
        _usage.TryGetValue(ch, out var rec) ? rec.Count : 0;

    public IReadOnlyList<string> GetMostUsed(int max) =>
        _usage
            .Where(kv => !_favoriteSet.Contains(kv.Key))
            .OrderByDescending(kv => DecayScore(kv.Value))
            .Take(max)
            .Select(kv => kv.Key)
            .ToList();

    private static double DecayScore(UsageRecord rec) {
        var daysSince = (DateTime.UtcNow - rec.LastUsedAt).TotalDays;
        // Clamp negative days (future timestamps used in tests) to 0
        if (daysSince < 0) daysSince = 0;
        return rec.Count * Math.Pow(0.5, daysSince / AppDefaults.EmojiHalfLifeDays);
    }

    public async Task LoadAsync() {
        if (!File.Exists(filePath)) return;
        try {
            var json = await File.ReadAllTextAsync(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("favorites", out var favProp) && favProp.ValueKind == JsonValueKind.Array) {
                _favorites = favProp.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => s != null)
                    .Cast<string>()
                    .ToList();
                _favoriteSet.Clear();
                foreach (var f in _favorites) _favoriteSet.Add(f);
            }

            if (root.TryGetProperty("usage", out var usageProp) && usageProp.ValueKind == JsonValueKind.Object) {
                _usage = new Dictionary<string, UsageRecord>();
                foreach (var prop in usageProp.EnumerateObject()) {
                    // Migrate old format: integer value → UsageRecord with LastUsedAt = UtcNow
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var legacyCount)) {
                        _usage[prop.Name] = new UsageRecord(legacyCount, DateTime.UtcNow);
                    } else if (prop.Value.ValueKind == JsonValueKind.Object) {
                        var count = prop.Value.TryGetProperty("count", out var cp) && cp.TryGetInt32(out var c) ? c : 0;
                        var lastUsedAt = prop.Value.TryGetProperty("lastUsedAt", out var lp) && lp.TryGetDateTimeOffset(out var dt)
                            ? dt.UtcDateTime
                            : DateTime.UtcNow;
                        _usage[prop.Name] = new UsageRecord(count, lastUsedAt);
                    }
                }
            }

            logger.LogInformation("Emoji usage loaded: {FavCount} favorites, {UsageCount} usage entries",
                _favorites.Count, _usage.Count);
        } catch (Exception ex) {
            logger.LogWarning("Failed to load emoji usage file, starting fresh: {Message}", ex.Message);
            _favorites = [];
            _usage = new Dictionary<string, UsageRecord>();
            _favoriteSet.Clear();
        }
    }

    public void Save() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var tmpPath = filePath + ".tmp";

            using (var stream = File.Create(tmpPath)) {
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();

                writer.WritePropertyName("favorites");
                writer.WriteStartArray();
                foreach (var f in _favorites) writer.WriteStringValue(f);
                writer.WriteEndArray();

                writer.WritePropertyName("usage");
                writer.WriteStartObject();
                foreach (var (ch, rec) in _usage) {
                    writer.WritePropertyName(ch);
                    writer.WriteStartObject();
                    writer.WriteNumber("count", rec.Count);
                    writer.WriteString("lastUsedAt", rec.LastUsedAt.ToString("O"));
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();

                writer.WriteEndObject();
            }

            File.Move(tmpPath, filePath, overwrite: true);
        } catch (Exception ex) {
            logger.LogWarning("Failed to save emoji usage file: {Message}", ex.Message);
        }
    }
}
