using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Platform;

public class LinuxPlatformProviderTests {
    private static LinuxPlatformProvider CreateProvider() =>
        new(new ProcessRunner(NullLogger<ProcessRunner>.Instance), NullLogger<LinuxPlatformProvider>.Instance);

    // Regression: a query that becomes empty after sanitizing (only quotes or only
    // whitespace) must not throw IndexOutOfRangeException when splitting into tokens.
    // The guard short-circuits with a completed task before plocate/locate is spawned.
    [Theory]
    [InlineData("\"\"")]
    [InlineData("  ")]
    [InlineData("\" \"")]
    public async Task SearchFilesAsync_EmptyAfterSanitize_ReturnsWithoutThrowing(string query) {
        var provider = CreateProvider();
        var results = new List<FileResult>();

        var task = provider.SearchFilesAsync(query, results.Add, 100, null, CancellationToken.None);

        Assert.True(task.IsCompletedSuccessfully);
        await task;
        Assert.Empty(results);
    }
}
