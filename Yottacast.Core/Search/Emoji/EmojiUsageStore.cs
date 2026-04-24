using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Search.Emoji;

/// <summary>
/// Persists emoji favorites and usage counts to a JSON file.
/// Favorites are user-toggled; usage counts are incremented on every emoji activation or copy.
/// The file is written atomically (temp file + File.Move) to avoid corruption.
/// </summary>
public class EmojiUsageStore(string filePath, ILogger<EmojiUsageStore> logger) {

    private List<string> _favorites = [];
    private Dictionary<string, int> _usage = new();
    private readonly HashSet<string> _favoriteSet = new();

    public IReadOnlyList<string> Favorites => _favorites;

    public bool IsFavorite(string ch) => _favoriteSet.Contains(ch);

    public void ToggleFavorite(string ch) {
        if (_favoriteSet.Contains(ch)) {
            _favoriteSet.Remove(ch);
            _favorites.Remove(ch);
        } else {
            _favoriteSet.Add(ch);
            _favorites.Add(ch);
        }
        Save();
    }

    public void RecordUsage(string ch) {
        _usage.TryGetValue(ch, out var count);
        _usage[ch] = count + 1;
        Save();
    }

    public int GetUsageCount(string ch) => _usage.TryGetValue(ch, out var count) ? count : 0;

    public IReadOnlyList<string> GetMostUsed(int max) =>
        _usage
            .Where(kv => !_favoriteSet.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .Take(max)
            .Select(kv => kv.Key)
            .ToList();

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
                _usage = new Dictionary<string, int>();
                foreach (var prop in usageProp.EnumerateObject()) {
                    if (prop.Value.TryGetInt32(out var count))
                        _usage[prop.Name] = count;
                }
            }

            logger.LogInformation("Emoji usage loaded: {FavCount} favorites, {UsageCount} usage entries",
                _favorites.Count, _usage.Count);
        } catch (Exception ex) {
            logger.LogWarning("Failed to load emoji usage file, starting fresh: {Message}", ex.Message);
            _favorites = [];
            _usage = new Dictionary<string, int>();
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
                foreach (var (ch, count) in _usage)
                    writer.WriteNumber(ch, count);
                writer.WriteEndObject();

                writer.WriteEndObject();
            }

            File.Move(tmpPath, filePath, overwrite: true);
        } catch (Exception ex) {
            logger.LogWarning("Failed to save emoji usage file: {Message}", ex.Message);
        }
    }
}
