using System.Diagnostics;

namespace Yottacast.Core.Process;

public sealed class StandardCommandRunner : ICommandRunner {
    public static readonly StandardCommandRunner Instance = new();

    public async Task<ProcessResult> RunAsync(
        string binary, string[] args, string cwd,
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
            WorkingDirectory = cwd,
        };

        using var proc = System.Diagnostics.Process.Start(psi)
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