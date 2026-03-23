namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Result of evaluating a math expression. Either Value or Error is set, not both.
/// </summary>
public readonly record struct EvaluationResult(string? Value, string? Error) {
    public bool IsSuccess => Value is not null;
}