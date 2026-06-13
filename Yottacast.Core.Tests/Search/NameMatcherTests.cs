using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Application;

namespace Yottacast.Core.Tests.Search;

public class NameMatcherTests
{
    // Scoring tiers (source of truth — see NameMatcher.cs):
    //   1.0  CamelHump prefix starting at token 0
    //   0.8  CamelHump prefix starting at token > 0
    //   0.6  Initials match (query chars = first char of consecutive tokens, but not CamelHump)
    //   0.4  Multi-word abbreviation: query spans multiple tokens, consuming ≥1 char per token
    //         e.g. "smifa" → "smi" prefix of "smiling" + "fa" prefix of "face"
    //   0.2  Internal substring (query.Length >= 3)
    //   0.0  No match
    //
    // All-lowercase queries are also retried as uppercase, so "am" → max(ScoreWith("am"), ScoreWith("AM")).

    [Theory]
    // Score 1.0 — CamelHump prefix at token 0
    [InlineData("Saf",   "Safari",           1.0)]   // prefix of sole token
    [InlineData("AcMon", "Activity Monitor", 1.0)]   // two-hump prefix from start
    [InlineData("FoBa",  "FooBar",           1.0)]   // humps inside single CamelCase word
    // Score 1.0 — all-lowercase query retried as uppercase, yielding CamelHump match
    [InlineData("am",    "Activity Monitor", 1.0)]   // "am" → "AM" → prefix A+M from token 0
    [InlineData("mon",   "Microsoft OneNote",1.0)]   // "mon" is prefix of "Microsoft" directly
    // Score 1.0 — uppercase initials acting as CamelHump (each char = one queryHump)
    [InlineData("AM",    "Activity Monitor", 1.0)]
    [InlineData("MON",   "Microsoft OneNote",1.0)]
    // Score 0.8 — CamelHump prefix but NOT at token 0
    [InlineData("Mon",   "Activity Monitor", 0.8)]   // "Mon" matches "Monitor" (token 1)
    // Score 0.6 — initials match via mixed-case query (no lowercase retry)
    [InlineData("Cs",    "Chrome Settings",  0.6)]   // "Cs" fails CamelHump; initials "CS" match
    // Score 0.4 — multi-word abbreviation (≥1 query char consumed per token)
    [InlineData("smifa", "smiling face with open mouth", 0.4)]  // smi→smiling, fa→face
    [InlineData("grfa",  "grinning face",               0.4)]  // gr→grinning, fa→face
    [InlineData("fwtea", "face with tears of joy",      0.4)]  // f→face, w→with, tea→tears
    // Score 0.2 — internal substring, query.Length >= 3
    [InlineData("ari",   "Safari",                       0.2)]
    // Score 0.0 — substring below 3-char threshold
    [InlineData("af",    "Safari",                       0.0)]
    // Score 0.0 — no match at all
    [InlineData("xyz",   "Safari",                       0.0)]
    // Score 0.0 — multi-word abbrev does NOT fire on single-token names
    [InlineData("smif",  "smiling",                      0.0)]
    // Score 0.0 — separator-only queries yield no humps and must not match everything
    [InlineData("-",     "Safari",                       0.0)]
    [InlineData("_",     "Safari",                       0.0)]
    [InlineData("--",    "Activity Monitor",             0.0)]
    [InlineData(" ",     "Safari",                       0.0)]
    public void Score_ReturnsExpected(string query, string name, double expected)
    {
        Assert.Equal(expected, NameMatcher.Score(name, query));
    }

    // Overload with pre-computed tokens — must return the same score as the string overload
    [Theory]
    [InlineData("smiling face with open mouth", "smifa", 0.4)]
    [InlineData("smiling face with open mouth", "smi",   1.0)]
    [InlineData("smiling face with open mouth", "face",  0.8)]
    [InlineData("smiling face with open mouth", "xyz",   0.0)]
    public void Score_WithPrecomputedTokens_MatchesStringOverload(string name, string query, double expected)
    {
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected, NameMatcher.Score(tokens, name, query));
        Assert.Equal(expected, NameMatcher.Score(name, query));
    }

    [Theory]
    [InlineData("Activity Monitor", new[] { "Activity", "Monitor" })]
    [InlineData("FooBar",           new[] { "Foo", "Bar" })]
    [InlineData("AM",               new[] { "A", "M" })]          // all-uppercase → each char its own hump
    [InlineData("iCloud",           new[] { "i", "Cloud" })]      // lowercase prefix + uppercase continuation
    [InlineData("my_file",          new[] { "my", "file" })]      // underscore separator
    [InlineData("some-thing",       new[] { "some", "thing" })]   // dash separator
    public void SplitTokens_ReturnsExpected(string input, string[] expected)
    {
        Assert.Equal(expected, NameMatcher.SplitTokens(input));
    }

    // ── Match() — exact match ─────────────────────────────────────────────────

    [Fact]
    public void Match_ExactName_ReturnsFullRange()
    {
        var result = NameMatcher.Match("PyCharm", "PyCharm");

        Assert.Equal(1.1, result.Score);
        Assert.Equal("Nombre exacto", result.Reason);
        Assert.NotNull(result.Ranges);
        Assert.Single(result.Ranges);
        Assert.Equal((0, 7), result.Ranges[0]);
    }

    // ── Match() — CamelHump from start ───────────────────────────────────────

    [Fact]
    public void Match_CamelHumpStart_ReturnsTokenPrefixRanges()
    {
        // "PyCharm" tokens: [("Py",0), ("Charm",2)]
        // Query "PC" → queryHumps ["P","C"] → match Py+Charm from start 0
        var result = NameMatcher.Match("PyCharm", "PC");

        Assert.Equal(1.0, result.Score);
        Assert.Equal("CamelHump inicio", result.Reason);
        Assert.NotNull(result.Ranges);
        Assert.Equal(2, result.Ranges.Count);
        Assert.Equal((0, 1), result.Ranges[0]);  // "P" at position 0, length 1
        Assert.Equal((2, 1), result.Ranges[1]);  // "C" at position 2, length 1
    }

    [Fact]
    public void Match_CamelHumpStart_MultiHump()
    {
        // "ActivityMonitor" tokens: [("Activity",0), ("Monitor",8)]
        // Query "AcMon" → queryHumps ["Ac","Mon"] → ranges [(0,2),(8,3)]
        var result = NameMatcher.Match("ActivityMonitor", "AcMon");

        Assert.Equal(1.0, result.Score);
        Assert.Equal("CamelHump inicio", result.Reason);
        Assert.NotNull(result.Ranges);
        Assert.Equal(2, result.Ranges.Count);
        Assert.Equal((0, 2), result.Ranges[0]);  // "Ac" at position 0, length 2
        Assert.Equal((8, 3), result.Ranges[1]);  // "Mon" at position 8, length 3
    }

    // ── Match() — CamelHump interior ─────────────────────────────────────────

    [Fact]
    public void Match_CamelHumpInterior_ReturnsScore08()
    {
        // "PyCharm" tokens: [("Py",0), ("Charm",2)]
        // Query "Char" → starts at token 1 ("Charm") → score 0.8
        var result = NameMatcher.Match("PyCharm", "Char");

        Assert.Equal(0.8, result.Score);
        Assert.Equal("CamelHump interior", result.Reason);
        Assert.NotNull(result.Ranges);
        Assert.Single(result.Ranges);
        Assert.Equal((2, 4), result.Ranges[0]);  // "Char" at position 2, length 4
    }

    // ── Match() — Initials ────────────────────────────────────────────────────

    [Fact]
    public void Match_Initials_ReturnsFirstCharOfEachToken()
    {
        // "Chrome Settings" tokens: [("Chrome",0), ("Settings",7)]
        // Query "Cs" → fails CamelHump (C+s don't match prefix pairs) → initials C+S → score 0.6
        // Note: "AM" would score 1.0 (CamelHump), so we use a mixed-case query that avoids CamelHump.
        var result = NameMatcher.Match("Chrome Settings", "Cs");

        Assert.Equal(0.6, result.Score);
        Assert.Equal("Iniciales", result.Reason);
        Assert.NotNull(result.Ranges);
        Assert.Equal(2, result.Ranges.Count);
        Assert.Equal((0, 1), result.Ranges[0]);   // 'C' at position 0
        Assert.Equal((7, 1), result.Ranges[1]);   // 'S' at position 7
    }

    // ── Match() — Abbreviation ────────────────────────────────────────────────

    [Fact]
    public void Match_Abbreviation_ReturnsScore04()
    {
        // "smiling face" → "smifa": "smi" from "smiling", "fa" from "face"
        var result = NameMatcher.Match("smiling face", "smifa");

        Assert.Equal(0.4, result.Score);
        Assert.Equal("Abreviatura", result.Reason);
        Assert.NotNull(result.Ranges);
        // Should have ranges covering "smi" (0,3) and "fa" (8,2)
        Assert.Contains(result.Ranges, r => r.Start == 0 && r.Length == 3);
        Assert.Contains(result.Ranges, r => r.Start == 8 && r.Length == 2);
    }

    // ── Match() — Substring ───────────────────────────────────────────────────

    [Fact]
    public void Match_Substring_ReturnsScore02WithCorrectRange()
    {
        // "typescript" query "scr" → idx=4, length=3
        var result = NameMatcher.Match("typescript", "scr");

        Assert.Equal(0.2, result.Score);
        Assert.Equal("Substring", result.Reason);
        Assert.NotNull(result.Ranges);
        Assert.Single(result.Ranges);
        Assert.Equal((4, 3), result.Ranges[0]);
    }

    // ── Match() — No match ────────────────────────────────────────────────────

    [Fact]
    public void Match_NoMatch_ReturnsNullRanges()
    {
        var result = NameMatcher.Match("PyCharm", "xyz");

        Assert.Equal(0, result.Score);
        Assert.Null(result.Reason);
        Assert.Null(result.Ranges);
    }

    // ── Match() — separator-only query ───────────────────────────────────────

    [Fact]
    public void Match_SeparatorOnlyQuery_ReturnsNoMatch()
    {
        // "-", "_", "--" split to zero humps; without the guard the CamelHump loop
        // would match any name with score 1.0 (regression).
        foreach (var query in new[] { "-", "_", "--", "-_-" }) {
            var result = NameMatcher.Match("Activity Monitor", query);
            Assert.Equal(0, result.Score);
            Assert.Null(result.Reason);
            Assert.Null(result.Ranges);
        }
    }

    // ── Score() backwards compatibility ──────────────────────────────────────

    [Fact]
    public void Score_BackwardsCompat_MatchDelegatesToScore()
    {
        // Verify Score() == Match().Score for a variety of inputs
        string[] names   = ["Safari", "Activity Monitor", "PyCharm", "smiling face"];
        string[] queries = ["Saf", "AM", "Char", "smifa", "xyz"];

        foreach (var name in names)
        foreach (var query in queries) {
            var scoreResult = NameMatcher.Score(name, query);
            var matchResult = NameMatcher.Match(name, query);
            Assert.Equal(scoreResult, matchResult.Score);
        }
    }
}
