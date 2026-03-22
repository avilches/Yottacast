using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

public record ProcessResult(
    TimeSpan Elapsed,
    int ExitCode,
    bool Cancelled,
    Exception? Error) {
    public bool IsSuccess => Error is null && !Cancelled && ExitCode == 0;
}

public sealed class ProcessRunner(ILogger<ProcessRunner> logger) {
    public async Task<ProcessResult> RunAsync(
        string binary, string[] args, string? cwd,
        Func<string, bool> onLine, CancellationToken ct,
        Func<string, bool>? onErrorLine = null) {
        var sw = Stopwatch.StartNew();
        var cancelled = false;
        Exception? error = null;

        var psi = new ProcessStartInfo(binary, string.Join(' ', args.Select(QuoteArg))) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = cwd ?? Environment.CurrentDirectory,
        };

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException($"Failed to start process: {binary}");

        // breakCts se cancela cuando cualquier callback devuelve false, desbloqueando ambas lecturas.
        using var breakCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try {
            await Task.WhenAll(
                DrainStdout(proc, onLine, breakCts),
                DrainStderr(proc, onErrorLine, breakCts)
            );
        } catch (OperationCanceledException) {
            cancelled = ct.IsCancellationRequested;
        } catch (Exception ex) {
            error = ex;
        }

        var exitCode = await ExitProcess(proc);
        
        sw.Stop();
        return new ProcessResult(sw.Elapsed, exitCode, cancelled, error);
    }

    private static async Task<int> ExitProcess(Process proc) {
        try {
            proc.Kill(entireProcessTree: true);
        } catch {
        }

        try {
            await proc.WaitForExitAsync(CancellationToken.None);
            return proc.ExitCode;
        } catch {
            return -1;
        }
    }

    private static async Task DrainStderr(Process proc, Func<string, bool>? onErrorLine, CancellationTokenSource breakCts) {
        while (await proc.StandardError.ReadLineAsync(breakCts.Token) is { } line)
            if (onErrorLine?.Invoke(line) == false) {
                await breakCts.CancelAsync();
                break;
            }
    }

    private static async Task DrainStdout(Process proc, Func<string, bool>? onLine, CancellationTokenSource breakCts) {
        while (await proc.StandardOutput.ReadLineAsync(breakCts.Token) is { } line)
            if (onLine?.Invoke(line) == false) {
                await breakCts.CancelAsync();
                break;
            }
    }

    private static string QuoteArg(string arg) =>
        arg.Contains(' ') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;
}