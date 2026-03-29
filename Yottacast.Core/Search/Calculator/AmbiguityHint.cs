namespace Yottacast.Core.Search.Calculator;

public readonly record struct UnitVariant(string Symbol, string LongName);
public readonly record struct AmbiguityHint(string Input, IReadOnlyList<UnitVariant> Candidates);
