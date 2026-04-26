using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;

namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Downloads exchange rates from the fawazahmed0 free API, caches them to disk,
/// and refreshes them periodically in background. Fires <see cref="RatesUpdated"/>
/// whenever the active rate set changes (new download or toggle change).
/// </summary>
public sealed class ExchangeRateService : IAsyncDisposable {
    // Primary URL: USD base → all currencies in one request
    private const string PrimaryUrl = "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies/usd.json";
    private const string FallbackUrl = "https://latest.currency-api.pages.dev/v1/currencies/usd.json";

    private readonly HttpClient _http;
    private readonly UserSettings _settings;
    private readonly ILogger<ExchangeRateService> _logger;

    private IReadOnlyDictionary<string, double> _allRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _lastUpdated;
    private Timer? _timer;
    private readonly Lock _lock = new();

    /// <summary>Fired whenever the active (filtered) rate set is ready or changes.</summary>
    public event Action<IReadOnlyDictionary<string, double>>? RatesUpdated;

    /// <summary>When rates were last successfully downloaded. Null if never.</summary>
    public DateTimeOffset? LastUpdated {
        get { lock (_lock) { return _lastUpdated; } }
    }

    /// <summary>True if rates have never been downloaded or are older than 2× the refresh interval.</summary>
    public bool IsStale {
        get {
            lock (_lock) {
                if (_lastUpdated == null) return true;
                return (DateTimeOffset.UtcNow - _lastUpdated.Value).TotalHours
                    > _settings.ExchangeRateRefreshIntervalHours * 2;
            }
        }
    }

    /// <summary>All rates downloaded from the API, keyed by uppercase currency code (USD=1.0).</summary>
    public IReadOnlyDictionary<string, double> AllRates {
        get { lock (_lock) { return _allRates; } }
    }

    public ExchangeRateService(HttpClient http, UserSettings settings, ILogger<ExchangeRateService> logger) {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Starts the service: loads cached rates from disk, downloads if stale or missing,
    /// and starts the background refresh timer.
    /// </summary>
    public async Task StartAsync() {
        await LoadCacheFromDiskAsync();

        // If cache is missing or stale, download immediately
        if (IsStale) {
            await DownloadAndUpdateAsync();
        } else {
            // Rates from cache are good — fire event immediately so engine can be created
            FireRatesUpdated();
        }

        // Start periodic timer
        var interval = TimeSpan.FromHours(_settings.ExchangeRateRefreshIntervalHours);
        _timer = new Timer(_ => _ = DownloadAndUpdateAsync(), null, interval, interval);
    }

    /// <summary>
    /// Call when the user changes toggle settings (CalculatorIncludeMetals / CalculatorIncludeCrypto).
    /// Recalculates active rates and fires RatesUpdated.
    /// </summary>
    public void NotifySettingsChanged() {
        FireRatesUpdated();
    }

    /// <summary>Builds active rates by filtering AllRates according to current settings toggles.</summary>
    public IReadOnlyDictionary<string, double> BuildActiveRates() {
        IReadOnlyDictionary<string, double> snapshot;
        lock (_lock) { snapshot = _allRates; }

        return snapshot
            .Where(kvp => {
                var type = CurrencyClassifier.Classify(kvp.Key);
                return type == CurrencyType.Forex
                    || (type == CurrencyType.Metal && _settings.CalculatorIncludeMetals)
                    || (type == CurrencyType.Crypto && _settings.CalculatorIncludeCrypto);
            })
            .ToDictionary(kvp => kvp.Key.ToUpperInvariant(), kvp => kvp.Value,
                          StringComparer.OrdinalIgnoreCase);
    }

    private async Task DownloadAndUpdateAsync() {
        try {
            var rates = await FetchRatesAsync();
            if (rates == null || rates.Count == 0) return;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (_lock) {
                _allRates = rates;
                _lastUpdated = now;
            }

            await SaveCacheToDiskAsync(rates, now);
            FireRatesUpdated();
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Exchange rate download failed — using cached rates");
        }
    }

    private async Task<IReadOnlyDictionary<string, double>?> FetchRatesAsync() {
        string? json = null;
        foreach (var url in new[] { PrimaryUrl, FallbackUrl }) {
            try {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppDefaults.ExchangeRateTimeoutSeconds));
                json = await _http.GetStringAsync(url, cts.Token);
                break;
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Exchange rate fetch failed for {Url}", url);
            }
        }

        if (json == null) return null;

        try {
            // API response: { "date": "2026-04-26", "usd": { "eur": 0.921, "gbp": 0.789, ... } }
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("usd", out var usdObj)) return null;

            var rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) {
                ["USD"] = 1.0
            };
            foreach (var prop in usdObj.EnumerateObject()) {
                if (prop.Value.TryGetDouble(out var rate) && rate > 0) {
                    rates[prop.Name.ToUpperInvariant()] = rate;
                }
            }
            return rates;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to parse exchange rate response");
            return null;
        }
    }

    private async Task LoadCacheFromDiskAsync() {
        try {
            if (!File.Exists(AppPaths.ExchangeRatesCache)) return;
            var json = await File.ReadAllTextAsync(AppPaths.ExchangeRatesCache);
            var cache = JsonSerializer.Deserialize<RateCache>(json);
            if (cache?.Rates == null || cache.Rates.Count == 0) return;

            var rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in cache.Rates) rates[k.ToUpperInvariant()] = v;
            rates["USD"] = 1.0; // always ensure USD baseline

            lock (_lock) {
                _allRates = rates;
                _lastUpdated = cache.LastUpdated;
            }
            _logger.LogInformation("Loaded exchange rates from disk cache ({Count} currencies, updated {Updated:u})",
                rates.Count, cache.LastUpdated);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to load exchange rate cache from disk");
        }
    }

    private async Task SaveCacheToDiskAsync(IReadOnlyDictionary<string, double> rates, DateTimeOffset updated) {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.ExchangeRatesCache)!);
            var cache = new RateCache { LastUpdated = updated, Rates = rates.ToDictionary(k => k.Key, k => k.Value) };
            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(AppPaths.ExchangeRatesCache, json);
            _logger.LogDebug("Saved exchange rates to disk ({Count} currencies)", rates.Count);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to save exchange rate cache to disk");
        }
    }

    private void FireRatesUpdated() {
        var active = BuildActiveRates();
        _logger.LogInformation("Exchange rates updated: {Count} active currencies", active.Count);
        RatesUpdated?.Invoke(active);
    }

    public async ValueTask DisposeAsync() {
        if (_timer != null) await _timer.DisposeAsync();
    }

    private sealed class RateCache {
        [JsonPropertyName("lastUpdated")] public DateTimeOffset LastUpdated { get; set; }
        [JsonPropertyName("rates")] public Dictionary<string, double>? Rates { get; set; }
    }
}
