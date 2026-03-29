using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jint;
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

/// <summary>
/// Generates and verifies the committed JSON files produced by mathjs-precompute.js.
/// Both files live in the source tree and are embedded as assembly resources:
///   - Yottacast.Core.Tests/Search/mathjs-unit-snapshot.json  (regression baseline)
///   - Yottacast.Core/Search/Calculator/mathjs-precomputed.json  (runtime maps)
///
/// To regenerate after upgrading math.js:
///   MATHJS_UPDATE_SNAPSHOT=1 dotnet test --project Yottacast.Core.Tests
/// </summary>
public class MathJsGeneratedFilesTests {

    private static string TestProjectDir =>
        typeof(MathJsGeneratedFilesTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "ProjectDir").Value!;

    private static string SnapshotPath =>
        Path.Combine(TestProjectDir, "Search", "mathjs-unit-snapshot.json");

    private static string PrecomputedPath =>
        Path.GetFullPath(Path.Combine(TestProjectDir, "..", "Yottacast.Core",
            "Search", "Calculator", "mathjs-precomputed.json"));

    [Fact]
    public void GeneratedFiles_MatchCommittedBaseline() {
        var (currentPrecomputed, currentSnapshot) = MathJsDataGenerator.GenerateData();
        var updateMode = Environment.GetEnvironmentVariable("MATHJS_UPDATE_SNAPSHOT") == "1";

        if (!File.Exists(SnapshotPath) || !File.Exists(PrecomputedPath) || updateMode) {
            File.WriteAllText(SnapshotPath,    currentSnapshot);
            File.WriteAllText(PrecomputedPath, currentPrecomputed);
            return; // Archivos creados/actualizados: test pasa
        }

        var committedSnapshot = File.ReadAllText(SnapshotPath);
        if (committedSnapshot != currentSnapshot) {
            var options    = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var current    = JsonSerializer.Deserialize<MathJsSnapshot>(currentSnapshot,   options)!;
            var committed  = JsonSerializer.Deserialize<MathJsSnapshot>(committedSnapshot, options)!;

            var newUnits      = current.Units.Except(committed.Units).ToList();
            var removedUnits  = committed.Units.Except(current.Units).ToList();
            var newAmbig      = current.Ambiguous.Except(committed.Ambiguous).ToList();
            var resolvedAmbig = committed.Ambiguous.Except(current.Ambiguous).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("math.js unit data changed. Review and update snapshot:");
            sb.AppendLine("  MATHJS_UPDATE_SNAPSHOT=1 dotnet test --filter GeneratedFiles");
            if (current.Version != committed.Version)
                sb.AppendLine($"  Version: {committed.Version} → {current.Version}");
            if (newUnits.Count != 0)
                sb.AppendLine($"  New units ({newUnits.Count}): {string.Join(", ", newUnits)}");
            if (removedUnits.Count != 0)
                sb.AppendLine($"  Removed units ({removedUnits.Count}): {string.Join(", ", removedUnits)}");
            if (newAmbig.Count != 0)
                sb.AppendLine($"  New ambiguous tokens (regression): {string.Join(", ", newAmbig)}");
            if (resolvedAmbig.Count != 0)
                sb.AppendLine($"  Resolved ambiguous tokens (improvement): {string.Join(", ", resolvedAmbig)}");

            Assert.Fail(sb.ToString());
        }

        Assert.Equal(File.ReadAllText(PrecomputedPath), currentPrecomputed);
    }
}

file static class MathJsDataGenerator {
    /// <summary>
    /// Builds all pre-computed runtime maps and the unit snapshot by loading math.js and
    /// mathjs-precompute.js in a temporary engine. Called by tests to regenerate the embedded
    /// resources after upgrading math.js.
    /// </summary>
    public static (string PrecomputedJson, string SnapshotJson) GenerateData() {
        var engine = new Engine(opts => opts.LimitRecursion(64));
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.math.min.js"));
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.mathjs-precompute.js"));
        var precomputed = engine.Evaluate("JSON.stringify(extractPrecomputedData())").ToString();
        var snapshot    = engine.Evaluate("JSON.stringify(extractUnitSnapshot(), null, 2)").ToString();
        engine.Dispose();
        return (precomputed, snapshot);
    }

    private static string LoadResource(string name) {
        using var stream = typeof(MathJsEngine).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded resource not found: {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

internal record MathJsSnapshot(
    string Version,
    int UnitCount,
    [property: JsonPropertyName("units")]     string[] Units,
    [property: JsonPropertyName("prefixGroups")] Dictionary<string, string[]> PrefixGroups,
    [property: JsonPropertyName("tokenMap")]  Dictionary<string, string[]> TokenMap,
    [property: JsonPropertyName("ambiguous")] string[] Ambiguous
);

// ── Unit casing invariants derived from the snapshot ─────────────────────────

[Collection("MathJsSnapshot")]
public class MathJsUnitCasingTests(MathJsSnapshotFixture fixture) {

    private static MathJsSnapshot ReadSnapshot() {
        var projectDir = typeof(MathJsUnitCasingTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "ProjectDir").Value!;
        var path = Path.Combine(projectDir, "Search", "mathjs-unit-snapshot.json");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<MathJsSnapshot>(File.ReadAllText(path), options)!;
    }

    /// <summary>
    /// For every non-ambiguous token in the snapshot: evaluating "1 unit" in its canonical
    /// casing and in ALL-CAPS must return the same result (normalization makes them equivalent).
    /// </summary>
    [Fact]
    public void NonAmbiguousUnits_UppercaseGivesSameResultAsCanonical() {
        var snapshot = ReadSnapshot();
        var failures = new List<string>();

        // Tokens with exactly one canonical form and not in the ambiguous list
        var candidates = snapshot.TokenMap
            .Where(kv => kv.Value.Length == 1 && !snapshot.Ambiguous.Contains(kv.Key))
            .Select(kv => kv.Value[0])
            .Take(40)
            .ToList();

        foreach (var unit in candidates) {
            var canonical = fixture.Engine.Evaluate($"1 {unit}");
            var upper     = fixture.Engine.Evaluate($"1 {unit.ToUpperInvariant()}");

            if (!canonical.IsSuccess) continue; // skip units that need special syntax (e.g. temperatures)

            if (!upper.IsSuccess)
                failures.Add($"'{unit.ToUpperInvariant()}' failed but '{unit}' succeeded: {upper.Error}");
            else if (canonical.Value != upper.Value)
                failures.Add($"'{unit}' → '{canonical.Value}'  vs  '{unit.ToUpperInvariant()}' → '{upper.Value}'");
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// For every ambiguous token in the snapshot: the two case-distinct variants stored in
    /// the tokenMap must evaluate to different results (they are genuinely different units,
    /// e.g. "mg" = milligram vs "Mg" = megagram).
    /// </summary>
    [Fact]
    public void AmbiguousUnits_DifferentCasingsGiveDifferentResults() {
        var snapshot = ReadSnapshot();
        var failures = new List<string>();

        var candidates = snapshot.TokenMap
            .Where(kv => kv.Value.Length >= 2)
            .Take(20)
            .ToList();

        foreach (var (token, variants) in candidates) {
            var r1 = fixture.Engine.Evaluate($"1 {variants[0]}");
            var r2 = fixture.Engine.Evaluate($"1 {variants[1]}");

            if (!r1.IsSuccess || !r2.IsSuccess) continue; // skip unsupported standalone units

            if (r1.Value == r2.Value)
                failures.Add($"Token '{token}': '{variants[0]}' and '{variants[1]}' both produced '{r1.Value}'");
        }

        Assert.Empty(failures);
    }
}