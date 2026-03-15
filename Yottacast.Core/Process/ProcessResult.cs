namespace Yottacast.Core.Process;

public record ProcessResult(
    TimeSpan Elapsed,
    int ExitCode,
    bool Cancelled,
    Exception? Error) {
    public bool IsSuccess => Error is null && !Cancelled && ExitCode == 0;
}