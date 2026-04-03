namespace Yottacast.Core.Search.Calculator;

public enum CalcErrorKind {
    None,
    Syntax,           // Parse/syntax error
    UnknownSymbol,    // Unknown identifier not found in the unit registry
    IncompatibleUnits,// Units are valid but incompatible for the operation (e.g. kg to meter)
    Other
}

public abstract record EvalResult {
    public string NormalizedQuery { get; init; } = "";
    public IReadOnlyList<AmbiguityHint>? AmbiguityHints { get; init; }
    public bool IsSuccess => this is CalcResult or ConversionResult;
    public string? Value => this switch {
        CalcResult r       => r.RawValue,
        ConversionResult r => $"{r.ToValue} {r.ToUnit}".Trim(),
        _                  => null
    };
    public string? Error => (this as ErrorResult)?.ErrorMessage;
}

public sealed record CalcResult(string RawValue) : EvalResult;

public sealed record ConversionResult(
    string FromValue, string FromUnit,
    string ToValue,   string ToUnit,
    string? FromUnitLong = null,
    string? ToUnitLong   = null) : EvalResult;

public sealed record ErrorResult(
    string? ErrorMessage = null,
    CalcErrorKind ErrorKind = CalcErrorKind.None,
    string? ErrorToken = null) : EvalResult;
