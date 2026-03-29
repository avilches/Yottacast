namespace Yottacast.Core.Search.Calculator;

public enum CalcErrorKind {
    None,
    Syntax,           // Parse/syntax error
    UnknownSymbol,    // Unknown identifier with no case-variant suggestions
    WrongUnitCasing,  // Known unit but written with wrong casing; ErrorSuggestions contains variants
    IncompatibleUnits,// Units are valid but incompatible for the operation (e.g. kg to meter)
    Other
}

/// <summary>
/// Result of evaluating a math expression. Either Value or Error is set, not both.
/// IsConversion is true when the expression contained an explicit 'to' unit-conversion operator.
/// On failure, ErrorKind classifies the error and ErrorToken / ErrorSuggestions provide detail.
/// </summary>
public readonly record struct EvaluationResult(
    string NormalizedQuery,
    string? Value, string? Error, bool IsConversion = false,
    IReadOnlyList<AmbiguityHint>? AmbiguityHints = null,
    CalcErrorKind ErrorKind = CalcErrorKind.None,
    string? ErrorToken = null,
    IReadOnlyList<UnitVariant>? ErrorSuggestions = null) {
    public bool IsSuccess => Value is not null;
}
