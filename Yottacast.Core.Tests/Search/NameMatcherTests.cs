using Xunit;
using Yottacast.Core.Search;

namespace Yottacast.Core.Tests.Search;

public class NameMatcherTests
{
    [Theory]
    [InlineData("Saf", "Safari", 1.0)]
    [InlineData("AcMon", "Activity Monitor", 1.0)]
    [InlineData("AM", "Activity Monitor", 1.0)]
    [InlineData("am", "Activity Monitor", 1.0)]
    [InlineData("FoBa", "FooBar", 1.0)]
    [InlineData("Mon", "Activity Monitor", 0.8)]
    [InlineData("mon", "Microsoft OneNote", 1.0)]
    [InlineData("MON", "Microsoft OneNote", 1.0)]
    [InlineData("ari", "Safari", 0.2)]
    [InlineData("af", "Safari", 0.0)]
    [InlineData("xyz", "Safari", 0.0)]
    public void Score_ReturnsExpected(string query, string name, double expected)
    {
        Assert.Equal(expected, NameMatcher.Score(name, query));
    }
}