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
}
