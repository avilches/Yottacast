using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Platform;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Platform;

/// <summary>
/// Filesystem-backed tests for WindowsPlatformProvider. They exercise OS-agnostic logic
/// (glob resolution, recursive app scan, helper-exe filtering) using real temp files,
/// so they run on any platform even though the provider targets Windows.
/// </summary>
public class WindowsPlatformProviderTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"YottacastWin_{Guid.NewGuid():N}");

    public WindowsPlatformProviderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static WindowsPlatformProvider CreateProvider() =>
        new(new ProcessRunner(NullLogger<ProcessRunner>.Instance), NullLogger<WindowsPlatformProvider>.Instance);

    private string Touch(string relative) {
        var path = Path.Combine(_tempDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    // ─── ResolveKnownPath — literal paths ─────────────────────────────────────

    [Fact]
    public void ResolveKnownPath_LiteralExisting_ReturnsPath() {
        var provider = CreateProvider();
        var exe = Touch("Git/bin/bash.exe");

        Assert.Equal(exe, provider.ResolveKnownPath(exe));
    }

    [Fact]
    public void ResolveKnownPath_LiteralMissing_ReturnsNull() {
        var provider = CreateProvider();

        Assert.Null(provider.ResolveKnownPath(Path.Combine(_tempDir, "nope", "bash.exe")));
    }

    // ─── ResolveKnownPath — glob in a directory segment (BUG 2) ───────────────

    [Fact]
    public void ResolveKnownPath_GlobInDirectory_ResolvesToInstalledExe() {
        var provider = CreateProvider();
        // Mirrors C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_<ver>\wt.exe
        var wt = Touch(Path.Combine("WindowsApps", "Microsoft.WindowsTerminal_1.0", "wt.exe"));
        var glob = Path.Combine(_tempDir, "WindowsApps", "Microsoft.WindowsTerminal*", "wt.exe");

        Assert.Equal(wt, provider.ResolveKnownPath(glob));
    }

    [Fact]
    public void ResolveKnownPath_GlobMatchesDirButExeMissing_ReturnsNull() {
        var provider = CreateProvider();
        Directory.CreateDirectory(Path.Combine(_tempDir, "WindowsApps", "Microsoft.WindowsTerminal_1.0"));
        var glob = Path.Combine(_tempDir, "WindowsApps", "Microsoft.WindowsTerminal*", "wt.exe");

        Assert.Null(provider.ResolveKnownPath(glob));
    }

    [Fact]
    public void ResolveKnownPath_GlobNoMatchingDir_ReturnsNull() {
        var provider = CreateProvider();
        Directory.CreateDirectory(Path.Combine(_tempDir, "WindowsApps"));
        var glob = Path.Combine(_tempDir, "WindowsApps", "Microsoft.WindowsTerminal*", "wt.exe");

        Assert.Null(provider.ResolveKnownPath(glob));
    }

    // ─── App scan — nested executables are found (BUG 3) ──────────────────────

    [Fact]
    public async Task ScanAppsAsync_FindsDeeplyNestedExe() {
        var provider = CreateProvider();
        // Google\Chrome\Application\chrome.exe — depth 3, missed by the old depth-1 scan.
        var chrome = Touch(Path.Combine("Google", "Chrome", "Application", "chrome.exe"));
        var found = new List<string>();

        await provider.ScanAppsAsync(found.Add, [_tempDir], CancellationToken.None);

        Assert.Contains(chrome, found);
    }

    [Fact]
    public async Task ScanAppsAsync_FindsTopLevelExe() {
        var provider = CreateProvider();
        var notepad = Touch(Path.Combine("Notepad", "notepad.exe"));
        var found = new List<string>();

        await provider.ScanAppsAsync(found.Add, [_tempDir], CancellationToken.None);

        Assert.Contains(notepad, found);
    }

    [Fact]
    public async Task ScanAppsAsync_SkipsHelperAndUninstallerExes() {
        var provider = CreateProvider();
        var app = Touch(Path.Combine("App", "app.exe"));
        Touch(Path.Combine("App", "unins000.exe"));
        Touch(Path.Combine("App", "crashpad_handler.exe"));
        Touch(Path.Combine("App", "app_update.exe"));
        var found = new List<string>();

        await provider.ScanAppsAsync(found.Add, [_tempDir], CancellationToken.None);

        Assert.Contains(app, found);
        Assert.DoesNotContain(found, p => Path.GetFileName(p) == "unins000.exe");
        Assert.DoesNotContain(found, p => Path.GetFileName(p) == "crashpad_handler.exe");
        Assert.DoesNotContain(found, p => Path.GetFileName(p) == "app_update.exe");
    }

    [Fact]
    public async Task ScanAppsAsync_RespectsMaxDepth() {
        var provider = CreateProvider();
        // One level deeper than the max depth: must not be picked up.
        var tooDeep = Touch(Path.Combine("a", "b", "c", "d", "deep.exe"));
        var found = new List<string>();

        await provider.ScanAppsAsync(found.Add, [_tempDir], CancellationToken.None);

        Assert.DoesNotContain(tooDeep, found);
    }

    // ─── BuildTerminalArgs — Git Bash needs -c (BUG 1) ────────────────────────

    [Fact]
    public void BuildTerminalArgs_GitBash_WrapsInDashC() {
        Assert.Equal("-c \"ls -la\"", WindowsPlatformProvider.BuildTerminalArgs("ls -la", "Git Bash"));
    }

    [Fact]
    public void BuildTerminalArgs_GitBash_EscapesQuotesAndBackslashes() {
        var args = WindowsPlatformProvider.BuildTerminalArgs(@"echo ""a\b""", "Git Bash");
        Assert.Equal(@"-c ""echo \""a\\b\""""", args);
    }

    [Fact]
    public void BuildTerminalArgs_PowerShell_UsesNoExitCommand() {
        Assert.Equal("-NoExit -Command \"git status\"",
            WindowsPlatformProvider.BuildTerminalArgs("git status", "PowerShell"));
    }

    [Fact]
    public void BuildTerminalArgs_CommandPrompt_UsesSlashK() {
        Assert.Equal("/K \"dir\"", WindowsPlatformProvider.BuildTerminalArgs("dir", "Command Prompt"));
    }

    // ─── IsLaunchableAppExe — shared predicate ────────────────────────────────

    [Theory]
    [InlineData("chrome.exe", true)]
    [InlineData("notepad.exe", true)]
    [InlineData("unins000.exe", false)]
    [InlineData("Setup.exe", false)]
    [InlineData("crashpad_handler.exe", false)]
    [InlineData("notification_helper.exe", false)]
    public void IsLaunchableAppExe_FiltersHelpers(string fileName, bool expected) {
        Assert.Equal(expected, WindowsPlatformProvider.IsLaunchableAppExe(Path.Combine("dir", fileName)));
    }
}
