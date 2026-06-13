using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

public record ProcessResult(
    TimeSpan Elapsed,
    int ExitCode,
    bool Cancelled,
    Exception? Error,
    bool StoppedByCallback = false) {
    // StoppedByCallback indica terminacion voluntaria: un callback devolvio false (p.ej. limite
    // de resultados alcanzado). Es un caso de exito funcional aunque el ExitCode sea distinto de 0
    // porque el proceso se mata con Kill antes de terminar por su cuenta.
    public bool IsSuccess => Error is null && !Cancelled && (StoppedByCallback || ExitCode == 0);
}

public sealed class ProcessRunner(ILogger<ProcessRunner> logger) {
    public async Task<ProcessResult> RunAsync(
        string binary, string[] args, string? cwd,
        Func<string, bool> onLine, CancellationToken ct,
        Func<string, bool>? onErrorLine = null) {
        var sw = Stopwatch.StartNew();
        var cancelled = false;
        var stoppedByCallback = false;
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
        var stdout = DrainStdout(proc, onLine, breakCts);
        var stderr = DrainStderr(proc, onErrorLine, breakCts);
        try {
            await Task.WhenAll(stdout, stderr);
        } catch (OperationCanceledException) {
            // breakCts puede haberse cancelado por un callback (parada voluntaria) o por ct (cancelacion real).
            cancelled = ct.IsCancellationRequested;
        } catch (Exception ex) {
            error = ex;
        }

        // Un callback que devolvio false detiene la lectura voluntariamente. La otra lectura puede
        // haber quedado cancelada por breakCts (devuelve OperationCanceledException, no resultado),
        // por eso se inspecciona cada Task de forma individual.
        if (!cancelled && error is null)
            stoppedByCallback = TaskStoppedByCallback(stdout) || TaskStoppedByCallback(stderr);

        var exitCode = await ExitProcess(proc);

        sw.Stop();
        return new ProcessResult(sw.Elapsed, exitCode, cancelled, error, stoppedByCallback);
    }

    // True solo si la lectura termino completa y el callback devolvio false. Si quedo cancelada
    // por breakCts (estado Canceled/Faulted) no se considera parada voluntaria de este drain.
    private static bool TaskStoppedByCallback(Task<bool> drain) =>
        drain.IsCompletedSuccessfully && drain.Result;

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

    // Devuelve true si el callback detuvo la lectura voluntariamente (devolvio false).
    private static async Task<bool> DrainStderr(Process proc, Func<string, bool>? onErrorLine, CancellationTokenSource breakCts) {
        while (await proc.StandardError.ReadLineAsync(breakCts.Token) is { } line)
            if (onErrorLine?.Invoke(line) == false) {
                await breakCts.CancelAsync();
                return true;
            }
        return false;
    }

    // Devuelve true si el callback detuvo la lectura voluntariamente (devolvio false).
    private static async Task<bool> DrainStdout(Process proc, Func<string, bool>? onLine, CancellationTokenSource breakCts) {
        while (await proc.StandardOutput.ReadLineAsync(breakCts.Token) is { } line)
            if (onLine?.Invoke(line) == false) {
                await breakCts.CancelAsync();
                return true;
            }
        return false;
    }

    // Escapa siempre las comillas dobles internas (con \") y entrecomilla el argumento si tiene
    // espacios o comillas, para que no se pierdan ni rompan el parseo del comando.
    private static string QuoteArg(string arg) =>
        arg.Contains(' ') || arg.Contains('"')
            ? $"\"{arg.Replace("\"", "\\\"")}\""
            : arg;
}