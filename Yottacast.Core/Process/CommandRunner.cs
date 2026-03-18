namespace Yottacast.Core.Process;

public static class CommandRunner {
    public static Task<ProcessResult> RunAsync(
        RunnerBackend backend,
        string binary, string[] args, string cwd,
        Func<string, bool> onLine, CancellationToken ct) {
        
        ICommandRunner runner = backend == RunnerBackend.Pty
            ? PtyRunner.Instance
            : StandardCommandRunner.Instance;
        
        return runner.RunAsync(binary, args, cwd, onLine, ct);
    }
}