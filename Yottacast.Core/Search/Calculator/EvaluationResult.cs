namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Result of evaluating a math expression. Either Value or Error is set, not both.
/// IsConversion is true when the expression contained an explicit 'to' unit-conversion operator.
/// </summary>
public readonly record struct EvaluationResult(string? Value, string? Error, bool IsConversion = false) {
    public bool IsSuccess => Value is not null;
}
