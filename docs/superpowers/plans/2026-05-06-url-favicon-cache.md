# URL sin validación + FaviconCache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mostrar resultados de URL aunque la validación esté desactivada, y cachear favicons en disco para no descargarlos en cada sesión.

**Architecture:** Se extrae la lógica de favicons de `UrlSearch` a un nuevo servicio `FaviconCache` (caché memoria + disco, análogo a `AppIconCache`). `UrlSearch` añade un branch para `EnableUrlValidation = false` que muestra el resultado directamente y delega el favicon a `FaviconCache`.

**Tech Stack:** .NET 9, xUnit, `ConcurrentDictionary`, `HttpClient`, `ILogger`

---

## File Map

| Acción | Fichero |
|--------|---------|
| Crear | `Yottacast.Core/Services/FaviconCache.cs` |
| Crear | `Yottacast.Core.Tests/Search/FaviconCacheTests.cs` |
| Modificar | `Yottacast.Core/AppPaths.cs` |
| Modificar | `Yottacast.Core/AppDefaults.cs` |
| Modificar | `Yottacast.Core/Search/Url/UrlSearch.cs` |
| Modificar | `Yottacast.Core.Tests/Search/UrlSearchTests.cs` |
| Modificar | `Yottacast/App.axaml.cs` |

---

### Task 1: AppPaths + AppDefaults

**Files:**
- Modify: `Yottacast.Core/AppPaths.cs`
- Modify: `Yottacast.Core/AppDefaults.cs`

- [ ] **Step 1: Añadir `FaviconCacheDir` a `AppPaths`**

En `Yottacast.Core/AppPaths.cs`, añadir después de la línea de `PluginIconCacheDir`:

```csharp
/// <summary>Favicon disk cache directory.</summary>
public static readonly string FaviconCacheDir = Path.Combine(CacheDir, "favicons");
```

- [ ] **Step 2: Añadir `FaviconTimeoutSeconds` a `AppDefaults`**

En `Yottacast.Core/AppDefaults.cs`, añadir en la sección `// ── Search — URL ─` (créala después de la sección de exchange rates si no existe):

```csharp
// ── Search — URL ─────────────────────────────────────────────────────────
/// HTTP timeout for favicon requests.
public const int FaviconTimeoutSeconds = 5;
```

- [ ] **Step 3: Compilar para verificar que no hay errores**

```bash
cd Yottacast.Core && dotnet build -q
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add Yottacast.Core/AppPaths.cs Yottacast.Core/AppDefaults.cs
git commit -m "feat: add FaviconCacheDir and FaviconTimeoutSeconds constants"
```

---

### Task 2: FaviconCache — tests primero

**Files:**
- Create: `Yottacast.Core.Tests/Search/FaviconCacheTests.cs`
- Create: `Yottacast.Core/Services/FaviconCache.cs` (stub mínimo para compilar)

- [ ] **Step 1: Crear stub mínimo de `FaviconCache` para que compilen los tests**

Crear `Yottacast.Core/Services/FaviconCache.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

public sealed class FaviconCache(HttpClient httpClient, ILogger<FaviconCache> logger) {
    public event Action? FaviconLoaded;
    public byte[]? GetOrLoad(string host) => throw new NotImplementedException();
    public Task Stop() => throw new NotImplementedException();
}
```

- [ ] **Step 2: Crear los tests**

Crear `Yottacast.Core.Tests/Search/FaviconCacheTests.cs`:

```csharp
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;

namespace Yottacast.Core.Tests.Search;

public class FaviconCacheTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public void Dispose() {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private FaviconCache Build(HttpStatusCode code = HttpStatusCode.OK, byte[]? responseBytes = null) {
        var handler = new FakeHttpMessageHandler(code, responseBytes ?? [0x89, 0x50, 0x4E, 0x47]); // PNG header
        return new FaviconCache(new HttpClient(handler), NullLogger<FaviconCache>.Instance, _tempDir);
    }

    [Fact]
    public async Task GetOrLoad_DiskHit_DoesNotFetch() {
        // Arrange: pre-populate disk cache
        Directory.CreateDirectory(_tempDir);
        var host = "example.com";
        File.WriteAllBytes(Path.Combine(_tempDir, $"{host}.png"), [0x89, 0x50, 0x4E, 0x47]);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var cache = new FaviconCache(new HttpClient(handler), NullLogger<FaviconCache>.Instance, _tempDir);

        // Act
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.FaviconLoaded += () => tcs.TrySetResult();
        cache.GetOrLoad(host);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // Assert: served from disk, no HTTP call
        Assert.Equal(0, handler.CallCount);
        Assert.NotNull(cache.GetOrLoad(host));
    }

    [Fact]
    public async Task GetOrLoad_DiskMiss_FetchesAndWritesToDisk() {
        var host = "example.com";
        var cache = Build();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.FaviconLoaded += () => tcs.TrySetResult();

        cache.GetOrLoad(host);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // Favicon written to disk
        var file = Path.Combine(_tempDir, $"{host}.png");
        Assert.True(File.Exists(file));
        Assert.NotEmpty(File.ReadAllBytes(file));
    }

    [Fact]
    public async Task GetOrLoad_SameHostTwice_OnlyOneFetch() {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, [0x89, 0x50]);
        var cache = new FaviconCache(new HttpClient(handler), NullLogger<FaviconCache>.Instance, _tempDir);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.FaviconLoaded += () => tcs.TrySetResult();

        cache.GetOrLoad("example.com");
        cache.GetOrLoad("example.com");
        cache.GetOrLoad("example.com");
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FaviconLoaded_FiredAfterLoad() {
        var cache = Build();
        var fired = false;
        cache.FaviconLoaded += () => fired = true;

        cache.GetOrLoad("example.com");
        await Task.Delay(500);

        Assert.True(fired);
    }

    [Fact]
    public async Task GetOrLoad_HttpFailure_MarksNull_NoRetry() {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.ServiceUnavailable);
        var cache = new FaviconCache(new HttpClient(handler), NullLogger<FaviconCache>.Instance, _tempDir);

        cache.GetOrLoad("example.com");
        await Task.Delay(500);

        // Second call must NOT start another HTTP request
        cache.GetOrLoad("example.com");
        await Task.Delay(200);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Stop_ClearsMemory() {
        var cache = Build();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.FaviconLoaded += () => tcs.TrySetResult();
        cache.GetOrLoad("example.com");
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await cache.Stop();

        // After Stop, memory is cleared (disk still exists)
        Assert.Null(cache.GetOrLoad("example.com")); // returns null until async reload
    }
}
```

**Nota:** `FakeHttpMessageHandler` necesita soportar `responseBytes` opcional. Revisa su firma actual en `Yottacast.Core.Tests/Fakes/FakeHttpMessageHandler.cs` — si solo acepta `HttpStatusCode`, añade un overload o un parámetro opcional `byte[]? responseBody = null`.

- [ ] **Step 3: Verificar que los tests fallan por NotImplementedException**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "FaviconCacheTests" -q
```
Expected: FAIL (NotImplementedException).

- [ ] **Step 4: Commit del stub + tests**

```bash
git add Yottacast.Core/Services/FaviconCache.cs Yottacast.Core.Tests/Search/FaviconCacheTests.cs
git commit -m "test: FaviconCacheTests con stub inicial"
```

---

### Task 3: Implementar FaviconCache

**Files:**
- Modify: `Yottacast.Core/Services/FaviconCache.cs`
- Modify: `Yottacast.Core.Tests/Fakes/FakeHttpMessageHandler.cs` (si hace falta añadir soporte de responseBytes)

- [ ] **Step 1: Comprobar `FakeHttpMessageHandler`**

Leer `Yottacast.Core.Tests/Fakes/FakeHttpMessageHandler.cs`. Si no acepta `byte[]? responseBody`, añadir:

```csharp
// Añadir campo y modificar constructor:
private readonly byte[]? _responseBody;

public FakeHttpMessageHandler(HttpStatusCode statusCode, byte[]? responseBody = null) {
    _statusCode = statusCode;
    _responseBody = responseBody;
}

protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
    Interlocked.Increment(ref _callCount);
    var response = new HttpResponseMessage(_statusCode);
    if (_responseBody is not null)
        response.Content = new ByteArrayContent(_responseBody);
    return Task.FromResult(response);
}
```

- [ ] **Step 2: Implementar `FaviconCache` completo**

Reemplazar el contenido de `Yottacast.Core/Services/FaviconCache.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

/// <summary>
/// Two-level cache for domain favicons: memory (instant) and disk (persists across launches).
/// Disk layout: {FaviconCacheDir}/{host}.png
/// </summary>
public sealed class FaviconCache {
    private readonly HttpClient _httpClient;
    private readonly ILogger<FaviconCache> _logger;
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, byte[]?> _memory = new();

    // Tracks which hosts have had a load initiated (dedup guard)
    private readonly ConcurrentDictionary<string, bool> _started = new();

    public FaviconCache(HttpClient httpClient, ILogger<FaviconCache> logger)
        : this(httpClient, logger, AppPaths.FaviconCacheDir) { }

    // Internal constructor for tests (injects tempDir)
    internal FaviconCache(HttpClient httpClient, ILogger<FaviconCache> logger, string cacheDir) {
        _httpClient = httpClient;
        _logger = logger;
        _cacheDir = cacheDir;
    }

    /// <summary>Fires on a thread-pool thread when a favicon finishes loading with non-null bytes.</summary>
    public event Action? FaviconLoaded;

    /// <summary>
    /// Returns cached bytes if available, triggers async load on first call per host.
    /// Returns null while loading or if favicon could not be obtained.
    /// </summary>
    public byte[]? GetOrLoad(string host) {
        if (_memory.TryGetValue(host, out var cached)) return cached;

        // Start load only once per host
        if (_started.TryAdd(host, true))
            _ = Task.Run(() => LoadAsync(host));

        return null;
    }

    /// <summary>Clears in-memory cache. Disk cache persists across sessions.</summary>
    public Task Stop() {
        _memory.Clear();
        _started.Clear();
        return Task.CompletedTask;
    }

    private async Task LoadAsync(string host) {
        // Phase 1: disk cache
        var diskPath = Path.Combine(_cacheDir, $"{host}.png");
        if (File.Exists(diskPath)) {
            var diskBytes = File.ReadAllBytes(diskPath);
            if (diskBytes.Length > 0) {
                _memory[host] = diskBytes;
                _logger.LogDebug("FaviconCache: disk hit for {Host} ({N} bytes)", host, diskBytes.Length);
                FaviconLoaded?.Invoke();
                return;
            }
        }

        // Phase 2: HTTP
        try {
            var url = $"https://www.google.com/s2/favicons?sz=64&domain={host}";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppDefaults.FaviconTimeoutSeconds));
            var bytes = await _httpClient.GetByteArrayAsync(url, cts.Token).ConfigureAwait(false);
            if (bytes.Length > 0) {
                _memory[host] = bytes;
                Directory.CreateDirectory(_cacheDir);
                File.WriteAllBytes(diskPath, bytes);
                _logger.LogDebug("FaviconCache: fetched and cached {Host} ({N} bytes)", host, bytes.Length);
                FaviconLoaded?.Invoke();
            } else {
                _memory[host] = null;
            }
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException) {
            _memory[host] = null;
            _logger.LogDebug("FaviconCache: fetch failed for {Host}: {Message}", host, ex.Message);
        }
    }
}
```

- [ ] **Step 3: Ejecutar tests de FaviconCache**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "FaviconCacheTests" -q
```
Expected: todos en verde.

- [ ] **Step 4: Ejecutar todos los tests para verificar que no rompimos nada**

```bash
cd Yottacast.Core.Tests && dotnet test -q
```
Expected: todos en verde.

- [ ] **Step 5: Commit**

```bash
git add Yottacast.Core/Services/FaviconCache.cs Yottacast.Core.Tests/Fakes/FakeHttpMessageHandler.cs
git commit -m "feat: FaviconCache con caché en disco y dedup"
```

---

### Task 4: Actualizar UrlSearch

**Files:**
- Modify: `Yottacast.Core/Search/Url/UrlSearch.cs`
- Modify: `Yottacast.Core.Tests/Search/UrlSearchTests.cs`

- [ ] **Step 1: Actualizar los tests de UrlSearch primero**

En `Yottacast.Core.Tests/Search/UrlSearchTests.cs`:

**Modificar `BuildSearch`** para pasar `FaviconCache`:

```csharp
private static (UrlSearch search, FakeHttpMessageHandler handler) BuildSearch(
    HttpStatusCode headStatusCode = HttpStatusCode.OK) {
    var handler = new FakeHttpMessageHandler(headStatusCode);
    var platform = new FakePlatformProvider([]);
    var settings = UserSettings.Load(platform);
    settings.EnableWebSearch = true;
    settings.EnableUrlValidation = true;
    var appIconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
    var browserDiscovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);
    var faviconHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, [0x89, 0x50]);
    var faviconCache = new FaviconCache(new HttpClient(faviconHandler), NullLogger<FaviconCache>.Instance,
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
    var search = new UrlSearch(new HttpClient(handler), settings, browserDiscovery, appIconCache,
        faviconCache, NullLogger<UrlSearch>.Instance);
    return (search, handler);
}
```

**Actualizar todos los usos de `BuildSearch()`** en los tests (ahora devuelve una tupla):

```csharp
// Antes:
var search = BuildSearch();
// Después:
var (search, _) = BuildSearch();
```

**Reemplazar el test `Search_ValidationOff_ReturnsEmpty`** por:

```csharp
[Fact]
public void Search_ValidationOff_ReturnsPendingResult() {
    var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
    var platform = new FakePlatformProvider([]);
    var settings = UserSettings.Load(platform);
    settings.EnableWebSearch = true;
    settings.EnableUrlValidation = false;
    var appIconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
    var browserDiscovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);
    var faviconHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, [0x89, 0x50]);
    var faviconCache = new FaviconCache(new HttpClient(faviconHandler), NullLogger<FaviconCache>.Instance,
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
    var search = new UrlSearch(new HttpClient(handler), settings, browserDiscovery, appIconCache,
        faviconCache, NullLogger<UrlSearch>.Instance);

    var results = search.Search("https://example.com", 10);
    Assert.Single(results);
    var r = Assert.IsType<ResultItemViewModel>(results[0]);
    Assert.Equal("https://example.com", r.Title);
    Assert.Equal("Web", r.Category);

    Thread.Sleep(50);
    // Sin validación: no debe llamar al handler de DNS/HEAD, solo favicon
    Assert.Equal(0, handler.CallCount);
}
```

**Actualizar `WaitForResultChangedAsync`** ya no hace falta cambiarlo — `UrlSearch` sigue exponiendo `ResultChanged`.

**Verificar que los tests existentes que usaban `BuildSearch()` compilan** ajustando la desestructuración.

- [ ] **Step 2: Ejecutar tests (deben fallar porque UrlSearch no acepta FaviconCache aún)**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "UrlSearchTests" -q
```
Expected: error de compilación o fallo por constructor incorrecto.

- [ ] **Step 3: Reescribir `UrlSearch`**

Reemplazar el contenido de `Yottacast.Core/Search/Url/UrlSearch.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Url;

public class UrlSearch(
    HttpClient httpClient,
    UserSettings settings,
    BrowserDiscovery browserDiscovery,
    AppIconCache appIconCache,
    FaviconCache faviconCache,
    ILogger<UrlSearch> logger) : IInstantSearchSource {

    private enum UrlReachability { Pending, Valid, Invalid }

    private readonly ConcurrentDictionary<string, UrlReachability> _reachability = new();
    private readonly ConcurrentDictionary<string, string> _reachabilityError = new();

    /// <summary>Fires (on a thread-pool thread) when reachability or favicon state changes.</summary>
    public event Action? ResultChanged;

    public int Limit => AppDefaults.UrlSearchSourceLimit;

    public void Start() {
        faviconCache.FaviconLoaded += () => ResultChanged?.Invoke();
    }

    public Task WhenReady() => Task.CompletedTask;

    public Task Stop() {
        _reachability.Clear();
        _reachabilityError.Clear();
        return Task.CompletedTask;
    }

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _limit) {
        if (!settings.EnableWebSearch) return [];
        if (!TryNormalizeUrl(query, out var url)) return [];

        var host = new Uri(url).Host;
        var browser = settings.ActiveBrowser;
        var browserFallbackIcon = browser is null ? null : appIconCache.Get(browser.ExecutablePath);

        if (!settings.EnableUrlValidation) {
            var iconBytes = faviconCache.GetOrLoad(host) ?? browserFallbackIcon;
            return [BuildResult(url, iconBytes, errorHint: null)];
        }

        var reachability = _reachability.GetOrAdd(url, key => {
            _ = CheckReachabilityAsync(key);
            return UrlReachability.Pending;
        });

        var favicon = faviconCache.GetOrLoad(host) ?? browserFallbackIcon;
        var errorHint = reachability == UrlReachability.Invalid
            ? _reachabilityError.GetValueOrDefault(url)
            : null;

        return [BuildResult(url, favicon, errorHint)];
    }

    private ResultItemViewModel BuildResult(string url, byte[]? iconBytes, string? errorHint) {
        var browser = settings.ActiveBrowser;
        var browserLabel = browser?.Name ?? "browser";
        var subtitle = errorHint is null ? $"Open in {browserLabel}" : $"Open in {browserLabel} ({errorHint})";
        var capturedUrl = url;
        return new ResultItemViewModel {
            IconBytes   = iconBytes,
            Title       = url.Length > 80 ? url[..77] + "…" : url,
            Subtitle    = subtitle,
            Category    = "Web",
            Score      = 4.0,
            OnActivate = () => {
                if (browser is null) {
                    logger.LogWarning("UrlSearch: cannot open \"{Url}\" — no browser configured", capturedUrl);
                    return;
                }
                logger.LogInformation("UrlSearch: open \"{Url}\" in {Browser}", capturedUrl, browser.Name);
                browserDiscovery.OpenUrl(capturedUrl, browser);
            },
        };
    }

    private async Task CheckReachabilityAsync(string url) {
        var host = new Uri(url).Host;

        // Phase 1: DNS — confirm the domain exists
        try {
            using var dnsCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Dns.GetHostAddressesAsync(host, dnsCts.Token).ConfigureAwait(false);
        } catch (Exception ex) when (ex is SocketException or TaskCanceledException or OperationCanceledException) {
            _reachabilityError[url] = ex is TaskCanceledException or OperationCanceledException
                ? "connection timed out"
                : "domain doesn't exist";
            _reachability[url] = UrlReachability.Invalid;
            logger.LogDebug("UrlSearch: DNS {Host} failed: {Message}", host, ex.Message);
            ResultChanged?.Invoke();
            return;
        }

        // DNS resolved — domain exists, show result
        _reachability[url] = UrlReachability.Valid;
        logger.LogDebug("UrlSearch: DNS {Host} → resolved", host);
        ResultChanged?.Invoke();

        // Phase 2: favicon (via FaviconCache)
        faviconCache.GetOrLoad(host);
    }

    // 1437 TLDs from IANA (https://data.iana.org/TLD/tlds-alpha-by-domain.txt)
    private static readonly HashSet<string> KnownTlds = new(StringComparer.OrdinalIgnoreCase) {
        "aaa", "aarp", "abb", "abbott", "abbvie", "abc", "able", "abogado", "abudhabi", "ac",
        "academy", "accenture", "accountant", "accountants", "aco", "actor", "ad", "ads", "adult", "ae",
        "aeg", "aero", "aetna", "af", "afl", "africa", "ag", "agakhan", "agency", "ai",
        "aig", "airbus", "airforce", "airtel", "akdn", "al", "alibaba", "alipay", "allfinanz", "allstate",
        "ally", "alsace", "alstom", "am", "amazon", "americanexpress", "americanfamily", "amex", "amfam", "amica",
        "amsterdam", "analytics", "android", "anquan", "anz", "ao", "aol", "apartments", "app", "apple",
        "aq", "aquarelle", "ar", "arab", "aramco", "archi", "army", "arpa", "art", "arte",
        "as", "asda", "asia", "associates", "at", "athleta", "attorney", "au", "auction", "audi",
        "audible", "audio", "auspost", "author", "auto", "autos", "aw", "aws", "ax", "axa",
        "az", "azure", "ba", "baby", "baidu", "banamex", "band", "bank", "bar", "barcelona",
        "barclaycard", "barclays", "barefoot", "bargains", "baseball", "basketball", "bauhaus", "bayern", "bb", "bbc",
        "bbt", "bbva", "bcg", "bcn", "bd", "be", "beats", "beauty", "beer", "berlin",
        "best", "bestbuy", "bet", "bf", "bg", "bh", "bharti", "bi", "bible", "bid",
        "bike", "bing", "bingo", "bio", "biz", "bj", "black", "blackfriday", "blockbuster", "blog",
        "bloomberg", "blue", "bm", "bms", "bmw", "bn", "bnpparibas", "bo", "boats", "boehringer",
        "bofa", "bom", "bond", "boo", "book", "booking", "bosch", "bostik", "boston", "bot",
        "boutique", "box", "br", "bradesco", "bridgestone", "broadway", "broker", "brother", "brussels", "bs",
        "bt", "build", "builders", "business", "buy", "buzz", "bv", "bw", "by", "bz",
        "bzh", "ca", "cab", "cafe", "cal", "call", "calvinklein", "cam", "camera", "camp",
        "canon", "capetown", "capital", "capitalone", "car", "caravan", "cards", "care", "career", "careers",
        "cars", "casa", "case", "cash", "casino", "cat", "catering", "catholic", "cba", "cbn",
        "cbre", "cc", "cd", "center", "ceo", "cern", "cf", "cfa", "cfd", "cg",
        "ch", "chanel", "channel", "charity", "chase", "chat", "cheap", "chintai", "christmas", "chrome",
        "church", "ci", "cipriani", "circle", "cisco", "citadel", "citi", "citic", "city", "ck",
        "cl", "claims", "cleaning", "click", "clinic", "clinique", "clothing", "cloud", "club", "clubmed",
        "cm", "cn", "co", "coach", "codes", "coffee", "college", "cologne", "com", "commbank",
        "community", "company", "compare", "computer", "comsec", "condos", "construction", "consulting", "contact", "contractors",
        "cooking", "cool", "coop", "corsica", "country", "coupon", "coupons", "courses", "cpa", "cr",
        "credit", "creditcard", "creditunion", "cricket", "crown", "crs", "cruise", "cruises", "cu", "cuisinella",
        "cv", "cw", "cx", "cy", "cymru", "cyou", "cz", "dad", "dance", "data",
        "date", "dating", "datsun", "day", "dclk", "dds", "de", "deal", "dealer", "deals",
        "degree", "delivery", "dell", "deloitte", "delta", "democrat", "dental", "dentist", "desi", "design",
        "dev", "dhl", "diamonds", "diet", "digital", "direct", "directory", "discount", "discover", "dish",
        "diy", "dj", "dk", "dm", "dnp", "do", "docs", "doctor", "dog", "domains",
        "dot", "download", "drive", "dtv", "dubai", "dupont", "durban", "dvag", "dvr", "dz",
        "earth", "eat", "ec", "eco", "edeka", "edu", "education", "ee", "eg", "email",
        "emerck", "energy", "engineer", "engineering", "enterprises", "epson", "equipment", "er", "ericsson", "erni",
        "es", "esq", "estate", "et", "eu", "eurovision", "eus", "events", "exchange", "expert",
        "exposed", "express", "extraspace", "fage", "fail", "fairwinds", "faith", "family", "fan", "fans",
        "farm", "farmers", "fashion", "fast", "fedex", "feedback", "ferrari", "ferrero", "fi", "fidelity",
        "fido", "film", "final", "finance", "financial", "fire", "firestone", "firmdale", "fish", "fishing",
        "fit", "fitness", "fj", "fk", "flickr", "flights", "flir", "florist", "flowers", "fly",
        "fm", "fo", "foo", "food", "football", "ford", "forex", "forsale", "forum", "foundation",
        "fox", "fr", "free", "fresenius", "frl", "frogans", "frontier", "ftr", "fujitsu", "fun",
        "fund", "furniture", "futbol", "fyi", "ga", "gal", "gallery", "gallo", "gallup", "game",
        "games", "gap", "garden", "gay", "gb", "gbiz", "gd", "gdn", "ge", "gea",
        "gent", "genting", "george", "gf", "gg", "ggee", "gh", "gi", "gift", "gifts",
        "gives", "giving", "gl", "glass", "gle", "global", "globo", "gm", "gmail", "gmbh",
        "gmo", "gmx", "gn", "godaddy", "gold", "goldpoint", "golf", "goodyear", "goog", "google",
        "gop", "got", "gov", "gp", "gq", "gr", "grainger", "graphics", "gratis", "green",
        "gripe", "grocery", "group", "gs", "gt", "gu", "gucci", "guge", "guide", "guitars",
        "guru", "gw", "gy", "hair", "hamburg", "hangout", "haus", "hbo", "hdfc", "hdfcbank",
        "health", "healthcare", "help", "helsinki", "here", "hermes", "hiphop", "hisamitsu", "hitachi", "hiv",
        "hk", "hkt", "hm", "hn", "hockey", "holdings", "holiday", "homedepot", "homegoods", "homes",
        "homesense", "honda", "horse", "hospital", "host", "hosting", "hot", "hotels", "hotmail", "house",
        "how", "hr", "hsbc", "ht", "hu", "hughes", "hyatt", "hyundai", "ibm", "icbc",
        "ice", "icu", "id", "ie", "ieee", "ifm", "ikano", "il", "im", "imamat",
        "imdb", "immo", "immobilien", "in", "inc", "industries", "infiniti", "info", "ing", "ink",
        "institute", "insurance", "insure", "int", "international", "intuit", "investments", "io", "ipiranga", "iq",
        "ir", "irish", "is", "ismaili", "ist", "istanbul", "it", "itau", "itv", "jaguar",
        "java", "jcb", "je", "jeep", "jetzt", "jewelry", "jio", "jll", "jm", "jmp",
        "jnj", "jo", "jobs", "joburg", "jot", "joy", "jp", "jpmorgan", "jprs", "juegos",
        "juniper", "kaufen", "kddi", "ke", "kerryhotels", "kerryproperties", "kfh", "kg", "kh", "ki",
        "kia", "kids", "kim", "kindle", "kitchen", "kiwi", "km", "kn", "koeln", "komatsu",
        "kosher", "kp", "kpmg", "kpn", "kr", "krd", "kred", "kuokgroup", "kw", "ky",
        "kyoto", "kz", "la", "lacaixa", "lamborghini", "lamer", "land", "landrover", "lanxess", "lasalle",
        "lat", "latino", "latrobe", "law", "lawyer", "lb", "lc", "lds", "lease", "leclerc",
        "lefrak", "legal", "lego", "lexus", "lgbt", "li", "lidl", "life", "lifeinsurance", "lifestyle",
        "lighting", "like", "lilly", "limited", "limo", "lincoln", "link", "live", "living", "lk",
        "llc", "llp", "loan", "loans", "locker", "locus", "lol", "london", "lotte", "lotto",
        "love", "lpl", "lplfinancial", "lr", "ls", "lt", "ltd", "ltda", "lu", "lundbeck",
        "luxe", "luxury", "lv", "ly", "ma", "madrid", "maif", "maison", "makeup", "man",
        "management", "mango", "map", "market", "marketing", "markets", "marriott", "marshalls", "mattel", "mba",
        "mc", "mckinsey", "md", "me", "med", "media", "meet", "melbourne", "meme", "memorial",
        "men", "menu", "merck", "merckmsd", "mg", "mh", "miami", "microsoft", "mil", "mini",
        "mint", "mit", "mitsubishi", "mk", "ml", "mlb", "mls", "mm", "mma", "mn",
        "mo", "mobi", "mobile", "moda", "moe", "moi", "mom", "monash", "money", "monster",
        "mormon", "mortgage", "moscow", "moto", "motorcycles", "mov", "movie", "mp", "mq", "mr",
        "ms", "msd", "mt", "mtn", "mtr", "mu", "museum", "music", "mv", "mw",
        "mx", "my", "mz", "na", "nab", "nagoya", "name", "navy", "nba", "nc",
        "ne", "nec", "net", "netbank", "netflix", "network", "neustar", "new", "news", "next",
        "nextdirect", "nexus", "nf", "nfl", "ng", "ngo", "nhk", "ni", "nico", "nike",
        "nikon", "ninja", "nissan", "nissay", "nl", "no", "nokia", "norton", "now", "nowruz",
        "nowtv", "np", "nr", "nra", "nrw", "ntt", "nu", "nyc", "nz", "obi",
        "observer", "office", "okinawa", "olayan", "olayangroup", "ollo", "om", "omega", "one", "ong",
        "onl", "online", "ooo", "open", "oracle", "orange", "org", "organic", "origins", "osaka",
        "otsuka", "ott", "ovh", "pa", "page", "panasonic", "paris", "pars", "partners", "parts",
        "party", "pay", "pccw", "pe", "pet", "pf", "pfizer", "pg", "ph", "pharmacy",
        "phd", "philips", "phone", "photo", "photography", "photos", "physio", "pics", "pictet", "pictures",
        "pid", "pin", "ping", "pink", "pioneer", "pizza", "pk", "pl", "place", "play",
        "playstation", "plumbing", "plus", "pm", "pn", "pnc", "pohl", "poker", "politie", "porn",
        "post", "pr", "praxi", "press", "prime", "pro", "prod", "productions", "prof", "progressive",
        "promo", "properties", "property", "protection", "pru", "prudential", "ps", "pt", "pub", "pw",
        "pwc", "py", "qa", "qpon", "quebec", "quest", "racing", "radio", "re", "read",
        "realestate", "realtor", "realty", "recipes", "red", "redumbrella", "rehab", "reise", "reisen", "reit",
        "reliance", "ren", "rent", "rentals", "repair", "report", "republican", "rest", "restaurant", "review",
        "reviews", "rexroth", "rich", "richardli", "ricoh", "ril", "rio", "rip", "ro", "rocks",
        "rodeo", "rogers", "room", "rs", "rsvp", "ru", "rugby", "ruhr", "run", "rw",
        "rwe", "ryukyu", "sa", "saarland", "safe", "safety", "sakura", "sale", "salon", "samsclub",
        "samsung", "sandvik", "sandvikcoromant", "sanofi", "sap", "sarl", "sas", "save", "saxo", "sb",
        "sbi", "sbs", "sc", "scb", "schaeffler", "schmidt", "scholarships", "school", "schule", "schwarz",
        "science", "scot", "sd", "se", "search", "seat", "secure", "security", "seek", "select",
        "sener", "services", "seven", "sew", "sex", "sexy", "sfr", "sg", "sh", "shangrila",
        "sharp", "shell", "shia", "shiksha", "shoes", "shop", "shopping", "shouji", "show", "si",
        "silk", "sina", "singles", "site", "sj", "sk", "ski", "skin", "sky", "skype",
        "sl", "sling", "sm", "smart", "smile", "sn", "sncf", "so", "soccer", "social",
        "softbank", "software", "sohu", "solar", "solutions", "song", "sony", "soy", "spa", "space",
        "sport", "spot", "sr", "srl", "ss", "st", "stada", "staples", "star", "statebank",
        "statefarm", "stc", "stcgroup", "stockholm", "storage", "store", "stream", "studio", "study", "style",
        "su", "sucks", "supplies", "supply", "support", "surf", "surgery", "suzuki", "sv", "swatch",
        "swiss", "sx", "sy", "sydney", "systems", "sz", "tab", "taipei", "talk", "taobao",
        "target", "tatamotors", "tatar", "tattoo", "tax", "taxi", "tc", "tci", "td", "tdk",
        "team", "tech", "technology", "tel", "temasek", "tennis", "teva", "tf", "tg", "th",
        "thd", "theater", "theatre", "tiaa", "tickets", "tienda", "tips", "tires", "tirol", "tj",
        "tjmaxx", "tjx", "tk", "tkmaxx", "tl", "tm", "tmall", "tn", "to", "today",
        "tokyo", "tools", "top", "toray", "toshiba", "total", "tours", "town", "toyota", "toys",
        "tr", "trade", "trading", "training", "travel", "travelers", "travelersinsurance", "trust", "trv", "tt",
        "tube", "tui", "tunes", "tushu", "tv", "tvs", "tw", "tz", "ua", "ubank",
        "ubs", "ug", "uk", "unicom", "university", "uno", "uol", "ups", "us", "uy",
        "uz", "va", "vacations", "vana", "vanguard", "vc", "ve", "vegas", "ventures", "verisign",
        "versicherung", "vet", "vg", "vi", "viajes", "video", "vig", "viking", "villas", "vin",
        "vip", "virgin", "visa", "vision", "viva", "vivo", "vlaanderen", "vn", "vodka", "volvo",
        "vote", "voting", "voto", "voyage", "vu", "wales", "walmart", "walter", "wang", "wanggou",
        "watch", "watches", "weather", "weatherchannel", "webcam", "weber", "website", "wed", "wedding", "weibo",
        "weir", "wf", "whoswho", "wien", "wiki", "williamhill", "win", "windows", "wine", "winners",
        "wme", "woodside", "work", "works", "world", "wow", "ws", "wtc", "wtf", "xbox",
        "xerox", "xihuan", "xin", "xn--11b4c3d", "xn--1ck2e1b", "xn--1qqw23a", "xn--2scrj9c", "xn--30rr7y", "xn--3bst00m", "xn--3ds443g",
        "xn--3e0b707e", "xn--3hcrj9c", "xn--3pxu8k", "xn--42c2d9a", "xn--45br5cyl", "xn--45brj9c", "xn--45q11c", "xn--4dbrk0ce", "xn--4gbrim", "xn--54b7fta0cc",
        "xn--55qw42g", "xn--55qx5d", "xn--5su34j936bgsg", "xn--5tzm5g", "xn--6frz82g", "xn--6qq986b3xl", "xn--80adxhks", "xn--80ao21a", "xn--80aqecdr1a", "xn--80asehdb",
        "xn--80aswg", "xn--8y0a063a", "xn--90a3ac", "xn--90ae", "xn--90ais", "xn--9dbq2a", "xn--9et52u", "xn--9krt00a", "xn--b4w605ferd", "xn--bck1b9a5dre4c",
        "xn--c1avg", "xn--c2br7g", "xn--cck2b3b", "xn--cckwcxetd", "xn--cg4bki", "xn--clchc0ea0b2g2a9gcd", "xn--czr694b", "xn--czrs0t", "xn--czru2d", "xn--d1acj3b",
        "xn--d1alf", "xn--e1a4c", "xn--eckvdtc9d", "xn--efvy88h", "xn--fct429k", "xn--fhbei", "xn--fiq228c5hs", "xn--fiq64b", "xn--fiqs8s", "xn--fiqz9s",
        "xn--fjq720a", "xn--flw351e", "xn--fpcrj9c3d", "xn--fzc2c9e2c", "xn--fzys8d69uvgm", "xn--g2xx48c", "xn--gckr3f0f", "xn--gecrj9c", "xn--gk3at1e", "xn--h2breg3eve",
        "xn--h2brj9c", "xn--h2brj9c8c", "xn--hxt814e", "xn--i1b6b1a6a2e", "xn--imr513n", "xn--io0a7i", "xn--j1aef", "xn--j1amh", "xn--j6w193g", "xn--jlq480n2rg",
        "xn--jvr189m", "xn--kcrx77d1x4a", "xn--kprw13d", "xn--kpry57d", "xn--kput3i", "xn--l1acc", "xn--lgbbat1ad8j", "xn--mgb9awbf", "xn--mgba3a3ejt", "xn--mgba3a4f16a",
        "xn--mgba7c0bbn0a", "xn--mgbaam7a8h", "xn--mgbab2bd", "xn--mgbah1a3hjkrd", "xn--mgbai9azgqp6j", "xn--mgbayh7gpa", "xn--mgbbh1a", "xn--mgbbh1a71e", "xn--mgbc0a9azcg", "xn--mgbca7dzdo",
        "xn--mgbcpq6gpa1a", "xn--mgberp4a5d4ar", "xn--mgbgu82a", "xn--mgbi4ecexp", "xn--mgbpl2fh", "xn--mgbt3dhd", "xn--mgbtx2b", "xn--mgbx4cd0ab", "xn--mix891f", "xn--mk1bu44c",
        "xn--mxtq1m", "xn--ngbc5azd", "xn--ngbe9e0a", "xn--ngbrx", "xn--node", "xn--nqv7f", "xn--nqv7fs00ema", "xn--nyqy26a", "xn--o3cw4h", "xn--ogbpf8fl",
        "xn--otu796d", "xn--p1acf", "xn--p1ai", "xn--pgbs0dh", "xn--pssy2u", "xn--q7ce6a", "xn--q9jyb4c", "xn--qcka1pmc", "xn--qxa6a", "xn--qxam",
        "xn--rhqv96g", "xn--rovu88b", "xn--rvc1e0am3e", "xn--s9brj9c", "xn--ses554g", "xn--t60b56a", "xn--tckwe", "xn--tiq49xqyj", "xn--unup4y", "xn--vermgensberater-ctb",
        "xn--vermgensberatung-pwb", "xn--vhquv", "xn--vuq861b", "xn--w4r85el8fhu5dnra", "xn--w4rs40l", "xn--wgbh1c", "xn--wgbl6a", "xn--xhq521b", "xn--xkc2al3hye2a", "xn--xkc2dl3a5ee0h",
        "xn--y9a3aq", "xn--yfro4i67o", "xn--ygbi2ammx", "xn--zfr164b", "xxx", "xyz", "yachts", "yahoo", "yamaxun", "yandex",
        "ye", "yodobashi", "yoga", "yokohama", "you", "youtube", "yt", "yun", "za", "zappos",
        "zara", "zero", "zip", "zm", "zone", "zuerich", "zw",
    };

    public static bool TryNormalizeUrl(string query, out string url) {
        url = "";
        if (string.IsNullOrWhiteSpace(query) || query.Contains(' ')) return false;

        if (query.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            query.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            url = query;
            return true;
        }

        if (query.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) {
            url = "https://" + query;
            return true;
        }

        if (LooksLikeDomain(query)) {
            url = "https://" + query;
            return true;
        }

        return false;
    }

    private static bool LooksLikeDomain(string query) {
        if (query.Length < 4) return false;
        var slashIdx = query.IndexOf('/');
        var host = slashIdx >= 0 ? query[..slashIdx] : query;
        var dotIdx = host.LastIndexOf('.');
        if (dotIdx <= 0 || dotIdx >= host.Length - 1) return false;
        var tld = host[(dotIdx + 1)..];
        return KnownTlds.Contains(tld);
    }
}
```

- [ ] **Step 4: Ejecutar tests de UrlSearch**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "UrlSearchTests" -q
```
Expected: todos en verde.

- [ ] **Step 5: Ejecutar todos los tests**

```bash
cd Yottacast.Core.Tests && dotnet test -q
```
Expected: todos en verde.

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Core/Search/Url/UrlSearch.cs Yottacast.Core.Tests/Search/UrlSearchTests.cs
git commit -m "feat: UrlSearch muestra URLs sin validación, delega favicon a FaviconCache"
```

---

### Task 5: Registro en DI

**Files:**
- Modify: `Yottacast/App.axaml.cs`

- [ ] **Step 1: Registrar `FaviconCache` como singleton**

En `Yottacast/App.axaml.cs`, añadir `FaviconCache` justo después de `AppIconCache` (línea ~221):

```csharp
services.AddSingleton<AppIconCache>();
services.AddSingleton<FaviconCache>();  // ← añadir esta línea
services.AddSingleton<FileIconCache>();
```

- [ ] **Step 2: Compilar la app completa**

```bash
cd Yottacast && dotnet build -q
```
Expected: Build succeeded (sin warnings nuevos).

- [ ] **Step 3: Ejecutar todos los tests**

```bash
cd Yottacast.Core.Tests && dotnet test -q
```
Expected: todos en verde.

- [ ] **Step 4: Commit final**

```bash
git add Yottacast/App.axaml.cs
git commit -m "feat: registrar FaviconCache en DI"
```