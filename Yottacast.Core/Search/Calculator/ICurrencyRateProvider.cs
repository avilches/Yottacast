namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Provides exchange rates for currency conversion in math expressions.
/// All rates are relative to USD (e.g. EUR=0.92 means 1 USD = 0.92 EUR).
/// </summary>
public interface ICurrencyRateProvider {
    /// <summary>
    /// Currently cached rates (synchronous, safe to read during expression evaluation).
    /// </summary>
    IReadOnlyDictionary<string, double> CachedRates { get; }

    /// <summary>
    /// Downloads rates for the given currency codes and updates the internal cache.
    /// </summary>
    Task RefreshAsync(IReadOnlyList<string> currencyCodes);
}