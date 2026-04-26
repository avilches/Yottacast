using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Yottacast.Core.Search.Dictionary;

public record LocalDictionaryEntry(string Pos, IReadOnlyList<string> Definitions, string? Example);

/// <summary>Read-only wrapper around a local SQLite dictionary database.</summary>
public sealed class LocalDictionaryDb : IDisposable {
    private readonly SqliteConnection _connection;

    public LocalDictionaryDb(string dbPath) {
        _connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        _connection.Open();
    }

    /// <summary>Returns true if a local DB exists at the given path.</summary>
    public static bool Exists(string dbPath) => File.Exists(dbPath);

    /// <summary>Returns all entries for the given word (case-insensitive), one per part-of-speech.</summary>
    public List<LocalDictionaryEntry> Lookup(string word) {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT pos, definitions, example FROM entries WHERE word = @w COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@w", word);
        using var reader = cmd.ExecuteReader();
        var results = new List<LocalDictionaryEntry>();
        while (reader.Read()) {
            var pos = reader.GetString(0);
            var defs = JsonSerializer.Deserialize<List<string>>(reader.GetString(1)) ?? [];
            var example = reader.IsDBNull(2) ? null : reader.GetString(2);
            results.Add(new LocalDictionaryEntry(pos, defs, example));
        }
        return results;
    }

    public void Dispose() => _connection.Dispose();
}
