using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core;
using Yottacast.Core.Platform;

namespace Yottacast.Core.Tests.Platform;

public class MacOsPlatformProviderTests {
    private static MacOsPlatformProvider CreateProvider() =>
        new(NullLogger<MacOsPlatformProvider>.Instance);

    // Regression: the macOS "Otros" terminal path used to create an orphan .tmp file via
    // Path.GetTempFileName() and never clean up the .command scripts it wrote. The script must now
    // be written to AppPaths.TerminalScriptsDir, with a unique name, and previous scripts must be
    // swept on each call so /tmp (and the cache dir) never accumulate orphans.

    [Fact]
    public void CreateCommandScript_WritesUniqueExecutableScript_InTerminalScriptsDir() {
        var provider = CreateProvider();

        var script = provider.CreateCommandScript("echo hello");

        try {
            Assert.True(File.Exists(script));
            Assert.Equal(AppPaths.TerminalScriptsDir, Path.GetDirectoryName(script));
            Assert.Equal(".command", Path.GetExtension(script));
            Assert.Contains("echo hello", File.ReadAllText(script));
        } finally {
            File.Delete(script);
        }
    }

    [Fact]
    public void CreateCommandScript_LeavesNoOrphanTmpFile() {
        var provider = CreateProvider();
        var before = Directory.GetFiles(Path.GetTempPath(), "*.tmp");

        var script = provider.CreateCommandScript("echo hi");

        try {
            var after = Directory.GetFiles(Path.GetTempPath(), "*.tmp");
            // No new .tmp file should appear in the system temp dir.
            Assert.Empty(after.Except(before));
        } finally {
            File.Delete(script);
        }
    }

    [Fact]
    public void CreateCommandScript_SweepsPreviousScripts() {
        var provider = CreateProvider();

        var first = provider.CreateCommandScript("first");
        Assert.True(File.Exists(first));

        var second = provider.CreateCommandScript("second");

        try {
            // The previous script must be gone after the next launch sweeps the directory.
            Assert.False(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.NotEqual(first, second);
        } finally {
            File.Delete(second);
        }
    }
}
