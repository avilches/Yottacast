using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Search.Dictionary;

/// <summary>Converts a kaikki basic JSONL file into a local SQLite dictionary database.</summary>
internal static class LocalDictionaryConverter {
    private record JsonlEntry(
        [property: JsonPropertyName("w")] string? Word,
        [property: JsonPropertyName("p")] string? Pos,
        [property: JsonPropertyName("d")] List<string>? Definitions,
        [property: JsonPropertyName("e")] string? Example
    );

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = false };

    /// <summary>
    /// Converts <paramref name="jsonlPath"/> to a SQLite database at <paramref name="dbPath"/>.
    /// Uses an atomic write (temp file + rename) so a partial conversion is never visible.
    /// </summary>
    public static async Task ConvertAsync(string jsonlPath, string dbPath, ILogger logger,
        CancellationToken ct = default) {
        var tempPath = dbPath + ".tmp";
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using var connection = new SqliteConnection($"Data Source={tempPath}");
            connection.Open();

            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = """
                CREATE TABLE entries (
                    word TEXT NOT NULL COLLATE NOCASE,
                    pos TEXT NOT NULL,
                    definitions TEXT NOT NULL,
                    example TEXT
                );
                CREATE INDEX idx_word ON entries(word COLLATE NOCASE);
                """;
            createCmd.ExecuteNonQuery();

            using var tx = connection.BeginTransaction();
            using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText =
                "INSERT INTO entries (word, pos, definitions, example) VALUES (@w, @p, @d, @e)";
            var pWord = insertCmd.Parameters.Add("@w", SqliteType.Text);
            var pPos  = insertCmd.Parameters.Add("@p", SqliteType.Text);
            var pDefs = insertCmd.Parameters.Add("@d", SqliteType.Text);
            var pEx   = insertCmd.Parameters.Add("@e", SqliteType.Text);

            int count = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await foreach (var line in File.ReadLinesAsync(jsonlPath, ct)) {
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonlEntry? entry;
                try {
                    entry = JsonSerializer.Deserialize<JsonlEntry>(line, JsonOpts);
                } catch (JsonException) {
                    continue;
                }

                if (entry is null
                    || string.IsNullOrWhiteSpace(entry.Word)
                    || string.IsNullOrWhiteSpace(entry.Pos)
                    || entry.Definitions is not { Count: > 0 }) continue;

                pWord.Value = entry.Word;
                pPos.Value  = entry.Pos;
                pDefs.Value = JsonSerializer.Serialize(entry.Definitions);
                pEx.Value   = (object?)entry.Example ?? DBNull.Value;
                insertCmd.ExecuteNonQuery();
                count++;
            }

            tx.Commit();
            sw.Stop();
            logger.LogInformation(
                "Dictionary: imported {Count} entries from {File} in {Elapsed:F1}s",
                count, Path.GetFileName(jsonlPath), sw.Elapsed.TotalSeconds);
        } catch {
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }

        File.Move(tempPath, dbPath, overwrite: true);
    }
}
