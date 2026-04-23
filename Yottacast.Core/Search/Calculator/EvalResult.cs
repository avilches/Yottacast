namespace Yottacast.Core.Search.Calculator;

public enum CalcErrorKind {
    None,
    Syntax,           // Parse/syntax error
    UnknownSymbol,    // Unknown identifier not found in the unit registry
    IncompatibleUnitsConvert, // Units are valid but incompatible for explicit conversion (e.g. "10km to litres")
    IncompatibleUnitsOp,      // Units are valid but incompatible for arithmetic (e.g. "1km + 2l")
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

public sealed record CalcResult(string RawValue, string? Unit = null, string? UnitLong = null) : EvalResult;

/// <summary>
/// Result of a unit conversion or currency conversion.
/// <para><b>From (original)</b>: <see cref="FromValue"/>/<see cref="FromUnit"/> — the value in the unit the user
/// typed, well-formatted but magnitude-preserving (e.g. "0.001 V", not "1 mV").</para>
/// <para><b>From (normalized)</b>: <see cref="NormFromValue"/>/<see cref="NormFromUnit"/> — the value after
/// math.js auto-simplification (e.g. "1 mV"). Null when the from was not changed.</para>
/// <para><b>To</b>: <see cref="ToValue"/>/<see cref="ToUnit"/> — the conversion result.</para>
/// </summary>
public sealed record ConversionResult(
    string FromValue, string FromUnit,
    string ToValue,   string ToUnit,
    string? FromUnitLong  = null,
    string? ToUnitLong    = null,
    /// <summary>True when the user explicitly wrote "X to Y"; false for implicit default conversions.</summary>
    bool IsExplicitConversion = false,
    /// <summary>True when math.js auto-simplified the from unit (NormFrom* fields are set).</summary>
    bool FromWasNormalized = false,
    /// <summary>Math.js auto-simplified from value (e.g. "1"); null when not normalized.</summary>
    string? NormFromValue    = null,
    /// <summary>Math.js auto-simplified from unit (e.g. "mV"); null when not normalized.</summary>
    string? NormFromUnit     = null,
    /// <summary>Long name for the normalized from unit; null when not normalized.</summary>
    string? NormFromUnitLong = null) : EvalResult;

public sealed record ErrorResult(
    string? ErrorMessage = null,
    CalcErrorKind ErrorKind = CalcErrorKind.None,
    string? ErrorToken = null) : EvalResult;
