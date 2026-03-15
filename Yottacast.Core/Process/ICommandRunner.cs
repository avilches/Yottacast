namespace Yottacast.Core.Process;

public enum RunnerBackend { Standard, Pty }

public interface ICommandRunner {
    Task<ProcessResult> RunAsync(string binary, string[] args, string cwd, Func<string, bool> onLine, CancellationToken ct);
}
