namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Returns hardcoded exchange rates relative to USD.
/// Replace with a real implementation that calls an exchange rate API.
/// </summary>
public class StaticCurrencyRateProvider : ICurrencyRateProvider {
    private static readonly Dictionary<string, double> Rates = new(StringComparer.OrdinalIgnoreCase) {
        { "USD", 1.0  },
        { "EUR", 0.92 },
        { "JPY", 150.5 },
        { "MXN", 17.1 },
        { "GBP", 0.79 },
    };

    public IReadOnlyDictionary<string, double> CachedRates => Rates;

    public Task RefreshAsync(IReadOnlyList<string> currencyCodes) => Task.CompletedTask;
}