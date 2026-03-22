using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Services;

public class ProcessRunnerTests {
    private static readonly string Cwd = "/tmp";
    private static readonly ProcessRunner Runner = new(new NullLogger<ProcessRunner>());

    [Fact]
    public async Task SingleLine_Echo_ReturnsLine() {
        var lines = new List<string>();
        await Runner.RunAsync("/bin/echo", ["hello"], Cwd, line => { lines.Add(line); return true; }, CancellationToken.None);
        Assert.Single(lines);
        Assert.Equal("hello", lines[0]);
    }

    [Fact]
    public async Task MultipleLines_InOrder() {
        var lines = new List<string>();
        await Runner.RunAsync("/bin/sh", ["-c", "printf 'a\\nb\\nc\\n'"], Cwd,
            line => { lines.Add(line); return true; }, CancellationToken.None);
        Assert.Equal(["a", "b", "c"], lines);
    }

    [Fact]
    public async Task ExitCode_Zero_OnSuccess() {
        var result = await Runner.RunAsync("/bin/echo", ["ok"], Cwd, _ => true, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ExitCode_NonZero_OnFailure() {
        var result = await Runner.RunAsync("/usr/bin/false", [], Cwd, _ => true, CancellationToken.None);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task IsSuccess_True_OnEcho() {
        var result = await Runner.RunAsync("/bin/echo", ["ok"], Cwd, _ => true, CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task IsSuccess_False_OnFalse() {
        var result = await Runner.RunAsync("/usr/bin/false", [], Cwd, _ => true, CancellationToken.None);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task EarlyTermination_OnLineReturnsFalse_StopsAfterFirstLine() {
        var lines = new List<string>();
        await Runner.RunAsync("/bin/sh", ["-c", "printf 'a\\nb\\nc\\n'"], Cwd,
            line => { lines.Add(line); return false; }, CancellationToken.None);
        Assert.Single(lines);
    }

    [Fact]
    public async Task Cancellation_ViaTokenInsideOnLine() {
        using var cts = new CancellationTokenSource();
        var lines = new List<string>();
        await Runner.RunAsync("/bin/sh", ["-c", "echo start; sleep 30"], Cwd,
            line => {
                lines.Add(line);
                cts.Cancel();
                return true;
            }, cts.Token);
        Assert.Contains("start", lines);
    }

    [Fact]
    public async Task Cancellation_ViaToken_AgainstLongRunning() {
        using var cts = new CancellationTokenSource(50);
        var result = await Runner.RunAsync("/bin/sh", ["-c", "sleep 30"], Cwd, _ => true, cts.Token);
        Assert.True(result.Cancelled);
    }

    [Fact]
    public async Task Elapsed_IsGreaterThanZero() {
        var result = await Runner.RunAsync("/bin/echo", ["ok"], Cwd, _ => true, CancellationToken.None);
        Assert.True(result.Elapsed > TimeSpan.Zero);
    }

    [Fact]
    public async Task Stderr_LinesDeliveredToCallback() {
        var errors = new List<string>();
        await Runner.RunAsync("/bin/sh", ["-c", "echo err >&2"], Cwd, _ => true, CancellationToken.None,
            line => { errors.Add(line); return true; });
        Assert.Single(errors);
        Assert.Equal("err", errors[0]);
    }

    [Fact]
    public async Task Stderr_EarlyTermination_OnCallbackReturnsFalse() {
        var errors = new List<string>();
        await Runner.RunAsync("/bin/sh", ["-c", "printf 'a\\nb\\nc\\n' >&2; sleep 30"], Cwd, _ => true,
            CancellationToken.None,
            line => { errors.Add(line); return false; });
        Assert.Single(errors);
    }

    [Fact]
    public async Task Stderr_NoDeadlock_WhenNoCallback() {
        // proceso que escribe bastante en stderr sin callback — no debe bloquearse
        var result = await Runner.RunAsync("/bin/sh", ["-c", "for i in $(seq 1 500); do echo \"err $i\" >&2; done"],
            Cwd, _ => true, CancellationToken.None);
        Assert.True(result.IsSuccess);
    }
}
