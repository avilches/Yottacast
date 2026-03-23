using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.Tests.Search;

/// <summary>
/// Test double for ICurrencyRateProvider that allows updating rates at runtime.
/// </summary>
public class MutableCurrencyRateProvider : ICurrencyRateProvider {
    private readonly Dictionary<string, double> _rates;

    public MutableCurrencyRateProvider(IEnumerable<KeyValuePair<string, double>> initialRates) {
        _rates = new Dictionary<string, double>(initialRates, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, double> CachedRates => _rates;

    public void SetRate(string currency, double rate) => _rates[currency] = rate;

    public Task RefreshAsync(IReadOnlyList<string> currencyCodes) => Task.CompletedTask;
}