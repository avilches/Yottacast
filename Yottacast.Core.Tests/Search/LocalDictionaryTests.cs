using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Dictionary;

namespace Yottacast.Core.Tests.Search;

public class LocalDictionaryDbTests : IDisposable {
    private readonly string _tempDir;
    private readonly string _dbPath;

    public LocalDictionaryDbTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"yc_dict_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "es.db");
        CreateTestDb(_dbPath);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static void CreateTestDb(string path) {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE entries (
                word TEXT NOT NULL COLLATE NOCASE,
                pos TEXT NOT NULL,
                definitions TEXT NOT NULL,
                example TEXT
            );
            CREATE INDEX idx_word ON entries(word COLLATE NOCASE);
            INSERT INTO entries VALUES ('casa', 'Noun', '["edificación destinada a vivienda","domicilio"]', 'Vivo en una casa grande.');
            INSERT INTO entries VALUES ('casa', 'Verb', '["tercera persona singular de casar"]', NULL);
            INSERT INTO entries VALUES ('hola', 'Interjection', '["saludo"]', NULL);
            """;
        cmd.ExecuteNonQuery();
    }

    // ── LocalDictionaryDb tests ──────────────────────────────────────────────

    [Fact]
    public void Lookup_ReturnsAllPosEntries_ForKnownWord() {
        using var db = new LocalDictionaryDb(_dbPath);
        var results = db.Lookup("casa");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Lookup_ReturnsCorrectDefinitionsAndExample() {
        using var db = new LocalDictionaryDb(_dbPath);
        var noun = db.Lookup("casa").Single(e => e.Pos == "Noun");
        Assert.Equal(2, noun.Definitions.Count);
        Assert.Equal("edificación destinada a vivienda", noun.Definitions[0]);
        Assert.Equal("domicilio", noun.Definitions[1]);
        Assert.Equal("Vivo en una casa grande.", noun.Example);
    }

    [Fact]
    public void Lookup_ReturnsNullExample_WhenNotStored() {
        using var db = new LocalDictionaryDb(_dbPath);
        var verb = db.Lookup("casa").Single(e => e.Pos == "Verb");
        Assert.Null(verb.Example);
    }

    [Fact]
    public void Lookup_IsCaseInsensitive() {
        using var db = new LocalDictionaryDb(_dbPath);
        Assert.Equal(db.Lookup("casa").Count, db.Lookup("CASA").Count);
        Assert.Equal(db.Lookup("hola").Count, db.Lookup("Hola").Count);
    }

    [Fact]
    public void Lookup_ReturnsEmpty_ForUnknownWord() {
        using var db = new LocalDictionaryDb(_dbPath);
        Assert.Empty(db.Lookup("xyz_nonexistent_word"));
    }

    [Fact]
    public void Exists_ReturnsFalse_WhenDbNotPresent() {
        Assert.False(LocalDictionaryDb.Exists(Path.Combine(_tempDir, "nonexistent.db")));
    }

    [Fact]
    public void Exists_ReturnsTrue_WhenDbPresent() {
        Assert.True(LocalDictionaryDb.Exists(_dbPath));
    }
}

public class LocalDictionaryConverterTests : IDisposable {
    private readonly string _tempDir;

    public LocalDictionaryConverterTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"yc_conv_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task ConvertAsync_CreatesDb_WithCorrectEntries() {
        var jsonlPath = Path.Combine(_tempDir, "es.jsonl");
        var dbPath = Path.Combine(_tempDir, "es.db");
        await File.WriteAllLinesAsync(jsonlPath, [
            """{"w":"casa","p":"Noun","d":["edificación destinada a vivienda","domicilio"],"e":"Vivo en una casa."}""",
            """{"w":"hola","p":"Interjection","d":["saludo"]}""",
        ]);

        await LocalDictionaryConverter.ConvertAsync(jsonlPath, dbPath, NullLogger.Instance);

        Assert.True(File.Exists(dbPath));
        using var db = new LocalDictionaryDb(dbPath);
        var casa = db.Lookup("casa");
        Assert.Single(casa);
        Assert.Equal("Noun", casa[0].Pos);
        Assert.Equal(2, casa[0].Definitions.Count);
        Assert.Equal("edificación destinada a vivienda", casa[0].Definitions[0]);
        Assert.Equal("Vivo en una casa.", casa[0].Example);

        var hola = db.Lookup("hola");
        Assert.Single(hola);
        Assert.Equal("Interjection", hola[0].Pos);
        Assert.Null(hola[0].Example);
    }

    [Fact]
    public async Task ConvertAsync_HandlesEmptyFile() {
        var jsonlPath = Path.Combine(_tempDir, "empty.jsonl");
        var dbPath = Path.Combine(_tempDir, "empty.db");
        await File.WriteAllTextAsync(jsonlPath, "");

        await LocalDictionaryConverter.ConvertAsync(jsonlPath, dbPath, NullLogger.Instance);

        Assert.True(File.Exists(dbPath));
        using var db = new LocalDictionaryDb(dbPath);
        Assert.Empty(db.Lookup("anyword"));
    }

    [Fact]
    public async Task ConvertAsync_SkipsMalformedLines() {
        var jsonlPath = Path.Combine(_tempDir, "partial.jsonl");
        var dbPath = Path.Combine(_tempDir, "partial.db");
        await File.WriteAllLinesAsync(jsonlPath, [
            "not-json-at-all",
            """{"w":"hola","p":"Interjection","d":["saludo"]}""",
            "",
        ]);

        await LocalDictionaryConverter.ConvertAsync(jsonlPath, dbPath, NullLogger.Instance);

        using var db = new LocalDictionaryDb(dbPath);
        Assert.Single(db.Lookup("hola"));
    }
}
