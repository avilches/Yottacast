using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Yottacast.Services;

public record ProcessResult(
    TimeSpan Elapsed,
    int ExitCode,
    bool Cancelled,
    Exception? Error) {

    public bool IsSuccess => Error is null && !Cancelled && ExitCode == 0;
}

public sealed class ProcessHandle : IDisposable {
    private readonly CancellationTokenSource _cts;

    public Task<ProcessResult> Completion { get; }

    internal ProcessHandle(CancellationTokenSource cts, Task<ProcessResult> completion) {
        _cts = cts;
        Completion = completion;
    }

    public void Cancel() => _cts.Cancel();

    public void Dispose() => _cts.Dispose();
}

public static class ProcessRunner {
    /// <summary>
    /// Starts a process and returns a handle immediately.
    /// Await <see cref="ProcessHandle.Completion"/> to get the result.
    /// Call <see cref="ProcessHandle.Cancel"/> to abort at any time.
    /// </summary>
    /// <param name="binary">Executable name or full path.</param>
    /// <param name="arguments">Command-line arguments.</param>
    /// <param name="timeout">Optional max duration; cancels automatically when exceeded.</param>
    /// <param name="onLine">Called for each stdout line as it arrives.</param>
    /// <param name="onErrorLine">Called for each stderr line as it arrives.</param>
    /// <param name="workingDirectory">Working directory for the process. Defaults to current directory.</param>
    /// <param name="ct">External cancellation token; linked with the internal one.</param>
    public static ProcessHandle Execute(
        string binary,
        string arguments,
        TimeSpan? timeout = null,
        Action<string>? onLine = null,
        Action<string>? onErrorLine = null,
        string? workingDirectory = null,
        CancellationToken ct = default) {

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout.HasValue)
            cts.CancelAfter(timeout.Value);

        var task = RunCoreAsync(binary, arguments, onLine, onErrorLine, workingDirectory, cts.Token);
        return new ProcessHandle(cts, task);
    }

    private static async Task<ProcessResult> RunCoreAsync(
        string binary, string arguments,
        Action<string>? onLine, Action<string>? onErrorLine,
        string? workingDirectory,
        CancellationToken ct) {

        var sw = Stopwatch.StartNew();
        var cancelled = false;
        var exitCode = -1;
        Exception? error = null;

        Process? proc = null;
        try {
            var psi = new ProcessStartInfo(binary, arguments) {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (workingDirectory is not null)
                psi.WorkingDirectory = workingDirectory;

            proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start process: {binary}");

            // Read stdout and stderr concurrently to avoid deadlock when either buffer fills up
            await Task.WhenAll(
                ReadStreamAsync(proc.StandardOutput, onLine, ct),
                ReadStreamAsync(proc.StandardError, onErrorLine, ct));

            await proc.WaitForExitAsync(ct);
            exitCode = proc.ExitCode;
        } catch (OperationCanceledException) {
            cancelled = true;
            try { proc?.Kill(entireProcessTree: true); } catch { }
        } catch (Exception ex) {
            error = ex;
        } finally {
            proc?.Dispose();
            sw.Stop();
        }

        return new ProcessResult(sw.Elapsed, exitCode, cancelled, error);
    }

    private static async Task ReadStreamAsync(
        StreamReader reader, Action<string>? onLine, CancellationToken ct) {

        while (await reader.ReadLineAsync(ct) is { } line)
            onLine?.Invoke(line);
    }
}