using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Search.Emoji;

internal record EmojiEntry(string Char, string Name, string[] Keywords, string Category, int SortOrder) {
    public IReadOnlyList<string> NameTokens { get; } =
        Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// Loads emoji data from the embedded resource (Search/Emoji/emoji-data.json, bundled at build time).
/// On first use, parses the raw JSON and writes a compact cache to disk for fast subsequent startups.
/// No network access at runtime. To update emoji data, delete Search/Emoji/emoji-data.json and rebuild.
/// On any failure with no cache available, returns an empty list.
/// </summary>
public class EmojiDataLoader(ILogger<EmojiDataLoader> logger) {

    private const string CacheFileName = "emoji-cache.json";

    internal async Task<IReadOnlyList<EmojiEntry>> LoadAsync(
        string cacheDir,
        CancellationToken ct = default) {

        var cachePath = Path.Combine(cacheDir, CacheFileName);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (File.Exists(cachePath)) {
            try {
                var cached = await File.ReadAllTextAsync(cachePath, ct);
                var entries = ParseCompactCache(cached);
                logger.LogInformation("Emoji disk cache loaded in {Ms}ms ({Count} entries)", sw.ElapsedMilliseconds, entries.Count);
                return entries;
            } catch (Exception ex) {
                logger.LogWarning("Failed to read emoji cache: {Message}", ex.Message);
                sw.Restart();
            }
        }

        var embeddedCache = TryReadEmbeddedString("Yottacast.Core.Search.Emoji.emoji-cache.json");
        if (embeddedCache != null) {
            try {
                var entries = ParseCompactCache(embeddedCache);
                logger.LogInformation("Emoji embedded cache loaded in {Ms}ms ({Count} entries)", sw.ElapsedMilliseconds, entries.Count);
                return entries;
            } catch (Exception ex) {
                logger.LogWarning("Failed to parse embedded emoji cache: {Message}", ex.Message);
            }
        }

        try {
            var t0 = sw.ElapsedMilliseconds;
            var rawJson = ReadEmbeddedString("Yottacast.Core.Search.Emoji.emoji-data.json");
            var t1 = sw.ElapsedMilliseconds;
            var entries = ParseRawJson(rawJson);
            var t2 = sw.ElapsedMilliseconds;
            WriteCompactCache(cachePath, entries);
            logger.LogInformation(
                "Emoji loaded from embedded resource in {TotalMs}ms (read={ReadMs}ms parse={ParseMs}ms write={WriteMs}ms, {Count} entries)",
                sw.ElapsedMilliseconds, t1 - t0, t2 - t1, sw.ElapsedMilliseconds - t2, entries.Count);
            return entries;
        } catch (Exception ex) {
            logger.LogWarning("Failed to parse embedded emoji data: {Message}", ex.Message);
            return [];
        }
    }

    private static string? TryReadEmbeddedString(string resourceName) {
        var stream = typeof(EmojiDataLoader).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var _ = stream;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string ReadEmbeddedString(string resourceName) =>
        TryReadEmbeddedString(resourceName)
        ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found");

    /// <summary>Parses the raw iamcal/emoji-data JSON, keeping only needed fields.</summary>
    internal static IReadOnlyList<EmojiEntry> ParseRawJson(string json) {
        using var doc = JsonDocument.Parse(json);
        var entries = new List<EmojiEntry>();

        foreach (var item in doc.RootElement.EnumerateArray()) {
            // Skip emojis superseded by gendered/newer versions
            if (item.TryGetProperty("obsoleted_by", out var ob) &&
                ob.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(ob.GetString())) continue;

            if (!item.TryGetProperty("unified", out var unifiedProp)) continue;
            var unified = unifiedProp.GetString();
            if (string.IsNullOrEmpty(unified)) continue;

            string emojiChar;
            try { emojiChar = UnifiedToChar(unified); }
            catch { continue; }

            var name = item.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString()?.ToLowerInvariant() ?? ""
                : "";

            var shortNames = item.TryGetProperty("short_names", out var snProp) &&
                             snProp.ValueKind == JsonValueKind.Array
                ? snProp.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToArray()
                : [];

            var texts = item.TryGetProperty("texts", out var textsProp) &&
                        textsProp.ValueKind == JsonValueKind.Array
                ? textsProp.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToArray()
                : [];

            var keywords = shortNames.Concat(texts).Distinct().ToArray();

            var category = item.TryGetProperty("category", out var catProp)
                ? catProp.GetString() ?? ""
                : "";

            var sortOrder = item.TryGetProperty("sort_order", out var soProp) &&
                            soProp.TryGetInt32(out var so)
                ? so : 0;

            entries.Add(new EmojiEntry(emojiChar, name, keywords, category, sortOrder));
        }

        return entries;
    }

    /// <summary>Parses the compact on-disk cache format.</summary>
    internal static IReadOnlyList<EmojiEntry> ParseCompactCache(string json) {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(item => new EmojiEntry(
                item[0].GetString()!,
                item[1].GetString()!,
                item[2].EnumerateArray().Select(e => e.GetString()!).ToArray(),
                item[3].GetString()!,
                item[4].GetInt32()))
            .ToList();
    }

    private void WriteCompactCache(string cachePath, IReadOnlyList<EmojiEntry> entries) {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var tmpPath = cachePath + ".tmp";

            using (var stream = File.Create(tmpPath)) {
                using var writer = new Utf8JsonWriter(stream);
                writer.WriteStartArray();
                foreach (var e in entries) {
                    writer.WriteStartArray();
                    writer.WriteStringValue(e.Char);
                    writer.WriteStringValue(e.Name);
                    writer.WriteStartArray();
                    foreach (var k in e.Keywords) writer.WriteStringValue(k);
                    writer.WriteEndArray();
                    writer.WriteStringValue(e.Category);
                    writer.WriteNumberValue(e.SortOrder);
                    writer.WriteEndArray();
                }
                writer.WriteEndArray();
            }

            File.Move(tmpPath, cachePath, overwrite: true);
            logger.LogInformation("Emoji cache written to {Path} ({Count} entries)", cachePath, entries.Count);
        } catch (Exception ex) {
            logger.LogWarning("Failed to write emoji cache: {Message}", ex.Message);
        }
    }

    // Converts "1F44D-1F3FB" → "👍🏻" by parsing each hex codepoint segment.
    private static string UnifiedToChar(string unified) =>
        string.Concat(unified.Split('-').Select(h => char.ConvertFromUtf32(Convert.ToInt32(h, 16))));
}