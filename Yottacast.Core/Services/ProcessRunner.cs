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
        Func<string, bool> onLine, CancellationToken ct) {
        var sw = Stopwatch.StartNew();
        var cancelled = false;
        var exitCode = -1;
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

        try {
            while (await proc.StandardOutput.ReadLineAsync(ct) is { } line)
                if (!onLine(line))
                    break;

            await proc.WaitForExitAsync(ct);
            exitCode = proc.ExitCode;
        } catch (OperationCanceledException) {
            cancelled = true;
        } catch (Exception ex) {
            error = ex;
        } finally {
            try {
                proc.Kill(entireProcessTree: true);
            } catch {
            }
            sw.Stop();
        }

        return new ProcessResult(sw.Elapsed, exitCode, cancelled, error);
    }

    private static string QuoteArg(string arg) =>
        arg.Contains(' ') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;
}
