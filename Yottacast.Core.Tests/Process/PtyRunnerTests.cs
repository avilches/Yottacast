using Xunit;
using Yottacast.Core.Process;

namespace Yottacast.Core.Tests.Process;

// PTY tests run sequentially (DisableTestParallelization in AssemblyInfo) because
// Pty.Net's kqueue-based exit-detection can race when PTY connections overlap.
// Tests stop reading early (return false from onLine) rather than waiting for EOF
// to avoid a Pty.Net race where process-exit is not detected, blocking ReadLineAsync.
public class PtyRunnerTests {
    private static readonly string Cwd = "/tmp";

    [Fact(Timeout = 10000)]
    public async Task Output_Echo_ContainsExpectedText() {
        var lines = new List<string>();
        await CommandRunner.RunAsync(RunnerBackend.Pty, "/bin/echo", ["hello"], Cwd,
            line => { lines.Add(line); return false; }, CancellationToken.None);
        Assert.Contains(lines, l => l.Contains("hello"));
    }

    [Fact(Timeout = 10000)]
    public async Task NoLine_HasTrailingCarriageReturn() {
        var lines = new List<string>();
        // Stop after 3 non-empty lines to avoid blocking on EOF detection.
        await CommandRunner.RunAsync(RunnerBackend.Pty, "/bin/sh", ["-c", "printf 'a\\nb\\nc\\n'"], Cwd,
            line => { lines.Add(line); return lines.Count < 3; }, CancellationToken.None);
        Assert.NotEmpty(lines);
        Assert.All(lines, l => Assert.DoesNotContain('\r', l));
    }

    // Verifies PtyRunner's IsNullOrWhiteSpace guard: no whitespace-only line
    // ever reaches the onLine callback.
    [Fact(Timeout = 10000)]
    public async Task EmptyLines_AreSkipped() {
        var lines = new List<string>();
        await CommandRunner.RunAsync(RunnerBackend.Pty, "/bin/echo", ["hello"], Cwd,
            line => { lines.Add(line); return false; }, CancellationToken.None);
        Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l)));
    }

    [Fact(Timeout = 10000)]
    public async Task EarlyTermination_OnLineReturnsFalse_FewerLines() {
        var lines = new List<string>();
        await CommandRunner.RunAsync(RunnerBackend.Pty, "/bin/sh", ["-c", "printf 'a\\nb\\nc\\nd\\ne\\n'"], Cwd,
            line => { lines.Add(line); return lines.Count < 2; }, CancellationToken.None);
        Assert.True(lines.Count < 5);
    }

    // Cancel inside onLine so the token is already cancelled when ReadLineAsync
    // is called next — PTY streams don't interrupt blocking reads externally.
    [Fact(Timeout = 10000)]
    public async Task Cancellation_ViaToken_StopsReading() {
        using var cts = new CancellationTokenSource();
        var lines = new List<string>();
        await CommandRunner.RunAsync(RunnerBackend.Pty, "/bin/sh", ["-c", "echo start; sleep 30"], Cwd,
            line => {
                lines.Add(line);
                cts.Cancel();
                return true;
            }, cts.Token);
        Assert.Contains(lines, l => l.Contains("start"));
    }

    // Return false after first line so we don't block on EOF detection.
    // WaitForExit(1000) in PtyRunner will capture the exit code.
    [Fact(Timeout = 10000)]
    public async Task ExitCode_Zero_OnEcho() {
        var result = await CommandRunner.RunAsync(RunnerBackend.Pty, "/bin/echo", ["ok"], Cwd,
            _ => false, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
    }

    // /usr/bin/false produces no output, so ReadLineAsync gets EOF immediately.
    [Fact(Timeout = 10000)]
    public async Task ExitCode_NonZero_OnFalse() {
        var result = await CommandRunner.RunAsync(RunnerBackend.Pty, "/usr/bin/false", [], Cwd,
            _ => true, CancellationToken.None);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact(Timeout = 10000)]
    public async Task Elapsed_IsGreaterThanZero() {
        var result = await CommandRunner.RunAsync(RunnerBackend.Pty, "/bin/echo", ["ok"], Cwd,
            _ => false, CancellationToken.None);
        Assert.True(result.Elapsed > TimeSpan.Zero);
    }
}
