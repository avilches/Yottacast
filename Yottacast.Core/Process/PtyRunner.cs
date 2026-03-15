using System.Diagnostics;
using System.Text;
using Pty.Net;

namespace Yottacast.Core.Process;

public sealed class PtyRunner : ICommandRunner {
    public static readonly PtyRunner Instance = new();

    public async Task<ProcessResult> RunAsync(string binary, string[] args, string cwd, Func<string, bool> onLine, CancellationToken ct) {
        var sw = Stopwatch.StartNew();
        var cancelled = false;

        var options = CreateOptions(binary, args, cwd);

        IPtyConnection pty;
        try {
            pty = await PtyProvider.SpawnAsync(options, ct);
        } catch (OperationCanceledException) {
            return new ProcessResult(sw.Elapsed, -1, true, null);
        } catch (Exception ex) {
            return new ProcessResult(sw.Elapsed, -1, false, ex);
        }

        try {
            using var reader = new StreamReader(pty.ReaderStream, Encoding.UTF8, leaveOpen: true);
            try {
                while (true) {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;
                    line = line.TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!onLine(line)) break;
                }
            } catch (OperationCanceledException) {
                cancelled = true;
            } catch {
                // process exited or stream closed — normal end
            }
        } finally {
            sw.Stop();
        }

        int exitCode = -1;
        try {
            pty.WaitForExit(1000);
            exitCode = pty.ExitCode;
        } catch { }
        KillPty(pty);

        return new ProcessResult(sw.Elapsed, exitCode, cancelled, null);
    }

    private static PtyOptions CreateOptions(string binary, string[] args, string cwd) {
        var options = new PtyOptions {
            Name = "xterm-256color",
            App = binary,
            CommandLine = args,
            Cwd = cwd,
            Rows = 24,
            Cols = 220,
            Environment = new Dictionary<string, string>(),
        };
        return options;
    }

    private static void KillPty(IPtyConnection pty) {
        if (!TryKillPty(pty)) TryKillByPid(pty.Pid);
    }

    private static bool TryKillPty(IPtyConnection pty) {
        try { pty.Kill(); return true; } catch { return false; }
    }

    private static bool TryKillByPid(int pid) {
        try { System.Diagnostics.Process.GetProcessById(pid).Kill(); return true; } catch { return false; }
    }
}
