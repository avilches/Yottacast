using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.Tests.Search;

/// <summary>
/// Dedicated fixture with an empty currency provider so no currencies are registered
/// into the engine, keeping the snapshot limited to math.js built-in units only.
/// </summary>
public sealed class MathJsSnapshotFixture : IAsyncLifetime {
    public MathJsEngine Engine { get; } = new(new EmptyCurrencyRateProvider());
    public Task InitializeAsync() => Engine.WhenReady();
    public Task DisposeAsync() { Engine.Dispose(); return Task.CompletedTask; }
}

[CollectionDefinition("MathJsSnapshot")]
public class MathJsSnapshotCollection : ICollectionFixture<MathJsSnapshotFixture>;

file class EmptyCurrencyRateProvider : ICurrencyRateProvider {
    public IReadOnlyDictionary<string, double> CachedRates =>
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    public Task RefreshAsync(IReadOnlyList<string> currencyCodes) => Task.CompletedTask;
}

[Collection("MathJsSnapshot")]
public class MathJsUnitSnapshotTests(MathJsSnapshotFixture fixture) {

    private static string SnapshotPath {
        get {
            var projectDir = typeof(MathJsUnitSnapshotTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .First(a => a.Key == "ProjectDir").Value!;
            return Path.Combine(projectDir, "Search", "mathjs-unit-snapshot.json");
        }
    }

    [Fact]
    public void UnitSnapshot_MatchesCommittedBaseline() {
        var currentJson = fixture.Engine.ExtractUnitSnapshot();
        bool updateMode = Environment.GetEnvironmentVariable("MATHJS_UPDATE_SNAPSHOT") == "1";

        if (!File.Exists(SnapshotPath) || updateMode) {
            File.WriteAllText(SnapshotPath, currentJson);
            return; // Snapshot creado/actualizado: test pasa
        }

        var committedJson = File.ReadAllText(SnapshotPath);
        if (committedJson == currentJson) return; // Sin cambios: test pasa

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var current   = JsonSerializer.Deserialize<MathJsSnapshot>(currentJson, options)!;
        var committed = JsonSerializer.Deserialize<MathJsSnapshot>(committedJson, options)!;

        var newUnits      = current.Units.Except(committed.Units).ToList();
        var removedUnits  = committed.Units.Except(current.Units).ToList();
        var newAmbig      = current.Ambiguous.Except(committed.Ambiguous).ToList();
        var resolvedAmbig = committed.Ambiguous.Except(current.Ambiguous).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("math.js unit data changed. Review and update snapshot:");
        sb.AppendLine("  MATHJS_UPDATE_SNAPSHOT=1 dotnet test --filter UnitSnapshot");
        if (current.Version != committed.Version)
            sb.AppendLine($"  Version: {committed.Version} → {current.Version}");
        if (newUnits.Any())
            sb.AppendLine($"  New units ({newUnits.Count}): {string.Join(", ", newUnits)}");
        if (removedUnits.Any())
            sb.AppendLine($"  Removed units ({removedUnits.Count}): {string.Join(", ", removedUnits)}");
        if (newAmbig.Any())
            sb.AppendLine($"  New ambiguous tokens (regression): {string.Join(", ", newAmbig)}");
        if (resolvedAmbig.Any())
            sb.AppendLine($"  Resolved ambiguous tokens (improvement): {string.Join(", ", resolvedAmbig)}");

        Assert.Fail(sb.ToString());
    }
}

file record MathJsSnapshot(
    string Version,
    int UnitCount,
    [property: JsonPropertyName("units")]     string[] Units,
    [property: JsonPropertyName("prefixGroups")] Dictionary<string, string[]> PrefixGroups,
    [property: JsonPropertyName("tokenMap")]  Dictionary<string, string[]> TokenMap,
    [property: JsonPropertyName("ambiguous")] string[] Ambiguous
);
