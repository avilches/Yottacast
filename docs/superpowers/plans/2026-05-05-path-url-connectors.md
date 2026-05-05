# Path & URL Connectors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir dos `IInstantSearchSource` nuevas: `LocalPathSearch` (detecta rutas de fichero/directorio) y `UrlSearch` (detecta URLs, verifica con HEAD, muestra con icono del browser y favicon).

**Architecture:** Ambas fuentes son `IInstantSearchSource` síncronas. `UrlSearch` usa un estado interno `(Pending|Valid|Invalid)` por URL: devuelve el resultado inmediatamente si Pending/Valid, y actualiza la UI via `ResultChanged` event cuando el HEAD async completa. `LocalPathSearch` es puramente síncrona usando `File.Exists`/`Directory.Exists`. El evento de iconos de fichero (`FileIconCache.IconLoaded`) se extiende para actualizar también snapshots instant.

**Tech Stack:** C# 13 / .NET 9, xUnit, `System.Net.Http.HttpClient`, `ConcurrentDictionary` para thread-safety, sin nuevas dependencias externas.

---

## Mapa de ficheros

| Fichero | Acción | Responsabilidad |
|---------|--------|-----------------|
| `Yottacast.Core/Search/LocalPath/LocalPathSearch.cs` | Crear | Detección de rutas locales + devolver `ResultItemViewModel` |
| `Yottacast.Core/Search/Url/UrlSearch.cs` | Crear | Detección de URLs, estado HEAD async, favicon |
| `Yottacast.Core.Tests/Search/LocalPathSearchTests.cs` | Crear | Tests de detección y resultado |
| `Yottacast.Core.Tests/Search/UrlSearchTests.cs` | Crear | Tests de normalización y estados |
| `Yottacast.Core.Tests/Fakes/FakeHttpMessageHandler.cs` | Crear | Handler HTTP fake para tests |
| `Yottacast/App.axaml.cs` | Modificar | Registrar ambas fuentes en DI |
| `Yottacast/ViewModels/MainWindowViewModel.cs` | Modificar | Añadir `UrlSearch`, suscribir `ResultChanged`, fix `OnFileIconLoaded` |

---

## Task 1: LocalPathSearch — detección de ruta (TDD)

**Files:**
- Create: `Yottacast.Core.Tests/Search/LocalPathSearchTests.cs`
- Create: `Yottacast.Core/Search/LocalPath/LocalPathSearch.cs`

- [ ] **Step 1: Escribir los tests de detección**

Crear `Yottacast.Core.Tests/Search/LocalPathSearchTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.LocalPath;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

public class LocalPathSearchTests {

    private static LocalPathSearch BuildSearch() {
        var platform = new FakePlatformProvider([]);
        var fileIconCache = new FileIconCache(platform, NullLogger<FileIconCache>.Instance);
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        return new LocalPathSearch(fileIconCache, platform, clipboard, NullLogger<LocalPathSearch>.Instance);
    }

    // ── IsLocalPath ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/Users/foo/bar.txt", true)]
    [InlineData("/tmp", true)]
    [InlineData("/", true)]
    [InlineData("~/Desktop/test.pdf", true)]
    [InlineData("~/", true)]
    [InlineData("./relative/path", true)]
    [InlineData("../parent/path", true)]
    [InlineData("C:\\Windows\\System32", true)]
    [InlineData("D:\\some\\path.exe", true)]
    [InlineData("hello world", false)]
    [InlineData("google.com", false)]
    [InlineData("https://example.com", false)]
    [InlineData("report.pdf", false)]
    [InlineData("", false)]
    [InlineData("a", false)]
    public void IsLocalPath_DetectsCorrectly(string query, bool expected) {
        Assert.Equal(expected, LocalPathSearch.IsLocalPath(query));
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [Fact]
    public void Search_NonPath_ReturnsEmpty() {
        var search = BuildSearch();
        Assert.Empty(search.Search("hello", 10));
        Assert.Empty(search.Search("google.com", 10));
        Assert.Empty(search.Search("report.pdf", 10));
    }

    [Fact]
    public void Search_NonExistentPath_ReturnsEmpty() {
        var search = BuildSearch();
        Assert.Empty(search.Search("/this/path/absolutely/does/not/exist_xyz.txt", 10));
    }

    [Fact]
    public void Search_ExistingFile_ReturnsOneResult() {
        var tempFile = Path.GetTempFileName();
        try {
            var search = BuildSearch();
            var results = search.Search(tempFile, 10);
            Assert.Single(results);
            var r = Assert.IsType<ResultItemViewModel>(results[0]);
            Assert.Equal(Path.GetFileName(tempFile), r.Title);
            Assert.Equal(tempFile, r.Subtitle);
            Assert.Equal("Files", r.Category);
            Assert.Equal(1.0, r.Score);
        } finally {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Search_ExistingDirectory_ReturnsOneResult() {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try {
            var search = BuildSearch();
            var results = search.Search(tempDir, 10);
            Assert.Single(results);
            var r = Assert.IsType<ResultItemViewModel>(results[0]);
            Assert.Equal(Path.GetFileName(tempDir), r.Title);
            Assert.Equal(tempDir, r.Subtitle);
        } finally {
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void Search_TildeExpansion_ResolvesHomePath() {
        var search = BuildSearch();
        // El directorio home siempre existe
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var results = search.Search("~/", 10);
        if (!Directory.Exists(home)) return; // skip si home no existe (CI edge case)
        Assert.Single(results);
        var r = Assert.IsType<ResultItemViewModel>(results[0]);
        Assert.Equal(home, r.Subtitle);
    }

    [Fact]
    public void Search_ExistingPath_HasActivateAndCopyCallbacks() {
        var tempFile = Path.GetTempFileName();
        try {
            var search = BuildSearch();
            var results = search.Search(tempFile, 10);
            var r = Assert.IsType<ResultItemViewModel>(results[0]);
            Assert.NotNull(r.OnActivate);
            Assert.NotNull(r.OnCopy);
            Assert.Equal("Path copied!", r.CopiedMessage);
        } finally {
            File.Delete(tempFile);
        }
    }
}
```

- [ ] **Step 2: Verificar que el test falla por clase inexistente**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "LocalPathSearchTests" 2>&1 | tail -10
```
Esperado: error de compilación ("The type or namespace name 'LocalPath' does not exist")

- [ ] **Step 3: Crear la implementación**

Crear `Yottacast.Core/Search/LocalPath/LocalPathSearch.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.LocalPath;

public class LocalPathSearch(
    FileIconCache fileIconCache,
    PlatformProvider platform,
    ClipboardService clipboard,
    ILogger<LocalPathSearch> logger) : IInstantSearchSource {

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _limit) {
        if (!IsLocalPath(query)) return [];
        var expanded = ExpandPath(query);
        if (!File.Exists(expanded) && !Directory.Exists(expanded)) return [];

        var title = Path.GetFileName(expanded);
        if (string.IsNullOrEmpty(title)) title = expanded; // raíces como "/" o "C:\"

        logger.LogDebug("LocalPathSearch: found \"{Path}\"", expanded);

        var capturedPath = expanded;
        return [new ResultItemViewModel {
            IconBytes      = fileIconCache.GetOrPreload(expanded),
            Title          = title,
            Subtitle       = expanded,
            Category       = "Files",
            Score          = 1.0,
            OnActivate     = () => {
                logger.LogInformation("LocalPath: open \"{Path}\"", capturedPath);
                platform.LaunchApp(capturedPath);
            },
            OnCopy         = () => clipboard.CopyText(capturedPath),
            CopiedMessage  = "Path copied!",
        }];
    }

    /// <summary>Returns true if the query looks like a local filesystem path.</summary>
    public static bool IsLocalPath(string query) {
        if (string.IsNullOrEmpty(query)) return false;
        if (query[0] == '/' || query.StartsWith("~/") ||
            query.StartsWith("./") || query.StartsWith("../"))
            return true;
        // Windows: C:\ or D:/
        return query.Length >= 3
               && query[1] == ':'
               && (query[2] == '\\' || query[2] == '/')
               && char.IsLetter(query[0]);
    }

    private static string ExpandPath(string path) {
        if (path == "~" || path.StartsWith("~/"))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path.Length > 2 ? path[2..] : "");
        return path;
    }
}
```

- [ ] **Step 4: Verificar que los tests pasan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "LocalPathSearchTests" 2>&1 | tail -10
```
Esperado: `Passed! - Failed: 0, Passed: 14 (o similar)`

- [ ] **Step 5: Commit**

```bash
cd .. && git add Yottacast.Core/Search/LocalPath/LocalPathSearch.cs Yottacast.Core.Tests/Search/LocalPathSearchTests.cs
git commit -m "feat: LocalPathSearch instant source con TDD"
```

---

## Task 2: LocalPathSearch — DI + fix OnFileIconLoaded

**Files:**
- Modify: `Yottacast/App.axaml.cs`
- Modify: `Yottacast/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Registrar LocalPathSearch en DI**

En `Yottacast/App.axaml.cs`, añadir en `BuildServices()` justo después de la línea de `WebSearchSource`:

```csharp
// Línea existente (referencia):
services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<WebSearchSource>());

// Añadir las dos líneas siguientes:
services.AddSingleton<LocalPathSearch>();
services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<LocalPathSearch>());
```

También añadir el using al principio del fichero si no existe:
```csharp
using Yottacast.Core.Search.LocalPath;
```

- [ ] **Step 2: Extender OnFileIconLoaded para cubrir snapshots instant**

En `Yottacast/ViewModels/MainWindowViewModel.cs`, modificar `OnFileIconLoaded`:

```csharp
// Antes:
private void OnFileIconLoaded() {
    Dispatcher.UIThread.Post(() => {
        foreach (var item in _deferredSnapshot)
            if (item is ResultItemViewModel r && r.IconBytes is null)
                r.IconBytes = fileIconCache.Get(r.Subtitle);
        RefreshResults();
    });
}

// Después:
private void OnFileIconLoaded() {
    Dispatcher.UIThread.Post(() => {
        foreach (var item in _instantSnapshot.Concat(_deferredSnapshot))
            if (item is ResultItemViewModel r && r.IconBytes is null)
                r.IconBytes = fileIconCache.Get(r.Subtitle);
        RefreshResults();
    });
}
```

- [ ] **Step 3: Verificar compilación y tests**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -5
```
Esperado: todos los tests existentes siguen pasando.

- [ ] **Step 4: Smoke test manual (opcional)**

```bash
cd ../Yottacast && dotnet run
```
Escribir `/tmp` o `~/Desktop` → debe aparecer el directorio como resultado de tipo "Files".

- [ ] **Step 5: Commit**

```bash
cd .. && git add Yottacast/App.axaml.cs Yottacast/ViewModels/MainWindowViewModel.cs
git commit -m "feat: registrar LocalPathSearch en DI + fix OnFileIconLoaded para instant snapshot"
```

---

## Task 3: FakeHttpMessageHandler + UrlSearch URL detection (TDD)

**Files:**
- Create: `Yottacast.Core.Tests/Fakes/FakeHttpMessageHandler.cs`
- Create: `Yottacast.Core.Tests/Search/UrlSearchTests.cs` (parcial: solo detección)
- Create: `Yottacast.Core/Search/Url/UrlSearch.cs` (solo `TryNormalizeUrl`)

- [ ] **Step 1: Crear FakeHttpMessageHandler**

Crear `Yottacast.Core.Tests/Fakes/FakeHttpMessageHandler.cs`:

```csharp
using System.Net;
using System.Net.Http;

namespace Yottacast.Core.Tests.Fakes;

/// <summary>
/// HttpMessageHandler configurable para tests. Responde con el status code indicado
/// sin realizar ninguna petición de red real.
/// </summary>
internal class FakeHttpMessageHandler(HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler {
    public int CallCount { get; private set; }
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) {
        CallCount++;
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
```

- [ ] **Step 2: Escribir tests de TryNormalizeUrl**

Crear `Yottacast.Core.Tests/Search/UrlSearchTests.cs` (solo la sección de normalización):

```csharp
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Url;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;

namespace Yottacast.Core.Tests.Search;

public class UrlSearchTests {

    // ── TryNormalizeUrl ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.com",       "https://example.com",       true)]
    [InlineData("http://example.com",        "http://example.com",        true)]
    [InlineData("https://example.com/path",  "https://example.com/path",  true)]
    [InlineData("www.example.com",           "https://www.example.com",   true)]
    [InlineData("www.example.com/path",      "https://www.example.com/path", true)]
    [InlineData("github.com/user/repo",      "https://github.com/user/repo", true)]
    [InlineData("example.io",               "https://example.io",         true)]
    [InlineData("example.dev",              "https://example.dev",        true)]
    [InlineData("myapp.ai",                 "https://myapp.ai",           true)]
    [InlineData("hello world",              "",                           false)]  // tiene espacios
    [InlineData("hello",                    "",                           false)]  // sin punto
    [InlineData("report.pdf",              "",                            false)]  // TLD desconocido
    [InlineData("example.xyz",             "",                            false)]  // TLD desconocido
    [InlineData("",                         "",                           false)]
    [InlineData("abc",                      "",                           false)]
    [InlineData("/usr/local/bin",           "",                           false)]  // ruta local
    public void TryNormalizeUrl_CorrectlyClassifies(
        string query, string expectedUrl, bool expectedResult) {
        var result = UrlSearch.TryNormalizeUrl(query, out var url);
        Assert.Equal(expectedResult, result);
        if (expectedResult) Assert.Equal(expectedUrl, url);
    }
}
```

- [ ] **Step 3: Verificar que el test falla por clase inexistente**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "UrlSearchTests" 2>&1 | tail -10
```
Esperado: error de compilación

- [ ] **Step 4: Crear UrlSearch con solo TryNormalizeUrl**

Crear `Yottacast.Core/Search/Url/UrlSearch.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Url;

public class UrlSearch(
    HttpClient httpClient,
    UserSettings settings,
    BrowserDiscovery browserDiscovery,
    AppIconCache appIconCache,
    ILogger<UrlSearch> logger) : IInstantSearchSource {

    private enum UrlReachability { Pending, Valid, Invalid }

    private readonly ConcurrentDictionary<string, UrlReachability> _reachability = new();
    private readonly ConcurrentDictionary<string, byte[]?> _favicons = new();

    /// <summary>Fires (on a thread-pool thread) when reachability or favicon state changes.</summary>
    public event Action? ResultChanged;

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() {
        _reachability.Clear();
        _favicons.Clear();
        return Task.CompletedTask;
    }

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _limit) {
        if (!TryNormalizeUrl(query, out var url)) return [];

        var reachability = _reachability.GetOrAdd(url, _ => {
            _ = CheckReachabilityAsync(url);
            return UrlReachability.Pending;
        });

        if (reachability == UrlReachability.Invalid) return [];

        var browser = settings.ActiveBrowser;
        var subtitle = browser is null ? "Open in browser" : $"Open in {browser.Name}";
        var capturedUrl = url;

        byte[]? iconBytes = _favicons.GetValueOrDefault(url)
                            ?? (browser is null ? null : appIconCache.Get(browser.ExecutablePath));

        return [new ResultItemViewModel {
            IconBytes   = iconBytes,
            Title       = url.Length > 80 ? url[..77] + "…" : url,
            Subtitle    = subtitle,
            Category    = "Web",
            Score       = 3.0,
            BypassLimit = true,
            OnActivate  = () => {
                if (browser is null) return;
                logger.LogInformation("UrlSearch: open \"{Url}\" in {Browser}", capturedUrl, browser.Name);
                browserDiscovery.OpenUrl(capturedUrl, browser);
            },
        }];
    }

    private async Task CheckReachabilityAsync(string url) {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status >= 200 && status < 400) {
                _reachability[url] = UrlReachability.Valid;
                logger.LogDebug("UrlSearch: HEAD {Url} → {Status}", url, status);
                ResultChanged?.Invoke();
                _ = LoadFaviconAsync(url);
            } else {
                _reachability[url] = UrlReachability.Invalid;
                logger.LogDebug("UrlSearch: HEAD {Url} → {Status} (invalid)", url, status);
                ResultChanged?.Invoke();
            }
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException) {
            _reachability[url] = UrlReachability.Invalid;
            logger.LogDebug("UrlSearch: HEAD {Url} failed: {Message}", url, ex.Message);
            ResultChanged?.Invoke();
        }
    }

    private async Task LoadFaviconAsync(string url) {
        try {
            var host = new Uri(url).Host;
            var faviconUrl = $"https://www.google.com/s2/favicons?sz=64&domain={host}";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var bytes = await httpClient.GetByteArrayAsync(faviconUrl, cts.Token).ConfigureAwait(false);
            _favicons[url] = bytes.Length > 0 ? bytes : null;
            if (bytes.Length > 0) {
                logger.LogDebug("UrlSearch: favicon loaded for {Url} ({N} bytes)", url, bytes.Length);
                ResultChanged?.Invoke();
            }
        } catch (Exception ex) {
            _favicons[url] = null;
            logger.LogDebug("UrlSearch: favicon failed for {Url}: {Message}", url, ex.Message);
        }
    }

    private static readonly HashSet<string> KnownTlds = new(StringComparer.OrdinalIgnoreCase) {
        "com", "net", "org", "io", "co", "uk", "de", "es", "fr", "dev", "app", "ai", "edu", "gov"
    };

    /// <summary>
    /// Returns true and sets <paramref name="url"/> to the normalized https:// URL
    /// if <paramref name="query"/> looks like a URL.
    /// </summary>
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

- [ ] **Step 5: Verificar que los tests de normalización pasan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "UrlSearchTests" 2>&1 | tail -10
```
Esperado: todos los tests de `TryNormalizeUrl` pasan

- [ ] **Step 6: Commit**

```bash
cd .. && git add \
  Yottacast.Core.Tests/Fakes/FakeHttpMessageHandler.cs \
  Yottacast.Core.Tests/Search/UrlSearchTests.cs \
  Yottacast.Core/Search/Url/UrlSearch.cs
git commit -m "feat: UrlSearch con TryNormalizeUrl + FakeHttpMessageHandler"
```

---

## Task 4: UrlSearch — tests de estado y comportamiento async

**Files:**
- Modify: `Yottacast.Core.Tests/Search/UrlSearchTests.cs`

- [ ] **Step 1: Añadir helper BuildSearch y tests de comportamiento**

Añadir al final de la clase `UrlSearchTests` en `Yottacast.Core.Tests/Search/UrlSearchTests.cs`:

```csharp
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static UrlSearch BuildSearch(HttpStatusCode headStatusCode = HttpStatusCode.OK) {
        var handler = new FakeHttpMessageHandler(headStatusCode);
        var httpClient = new HttpClient(handler);
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        var appIconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var browserDiscovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);
        return new UrlSearch(httpClient, settings, browserDiscovery, appIconCache, NullLogger<UrlSearch>.Instance);
    }

    /// <summary>Espera a que ResultChanged se dispare (máx <paramref name="timeoutMs"/> ms).</summary>
    private static Task WaitForResultChangedAsync(UrlSearch search, int timeoutMs = 3000) {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        search.ResultChanged += () => tcs.TrySetResult();
        return tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
    }

    // ── Search behavior ───────────────────────────────────────────────────────

    [Fact]
    public void Search_NonUrl_ReturnsEmpty() {
        var search = BuildSearch();
        Assert.Empty(search.Search("hello world", 10));
        Assert.Empty(search.Search("just text", 10));
        Assert.Empty(search.Search("/local/path", 10));
    }

    [Fact]
    public void Search_UrlLooking_ReturnsPendingResultImmediately() {
        var search = BuildSearch();
        var results = search.Search("https://example.com", 10);
        Assert.Single(results);
        var r = Assert.IsType<ResultItemViewModel>(results[0]);
        Assert.Equal("https://example.com", r.Title);
        Assert.Equal("Web", r.Category);
        Assert.Equal(3.0, r.Score);
        Assert.True(r.BypassLimit);
        Assert.NotNull(r.OnActivate);
    }

    [Fact]
    public async Task Search_AfterHead200_StillReturnsResult() {
        var search = BuildSearch(HttpStatusCode.OK);
        var waitTask = WaitForResultChangedAsync(search);
        _ = search.Search("https://example.com", 10);
        await waitTask;
        var results = search.Search("https://example.com", 10);
        Assert.Single(results);
    }

    [Fact]
    public async Task Search_AfterHead404_ReturnsEmpty() {
        var search = BuildSearch(HttpStatusCode.NotFound);
        var waitTask = WaitForResultChangedAsync(search);
        _ = search.Search("https://example.com", 10);
        await waitTask;
        Assert.Empty(search.Search("https://example.com", 10));
    }

    [Fact]
    public async Task Search_AfterHead500_ReturnsEmpty() {
        var search = BuildSearch(HttpStatusCode.InternalServerError);
        var waitTask = WaitForResultChangedAsync(search);
        _ = search.Search("https://example.com", 10);
        await waitTask;
        Assert.Empty(search.Search("https://example.com", 10));
    }

    [Fact]
    public void Search_SameUrlTwice_DoesNotStartTwoBackgroundChecks() {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        var appIconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var browserDiscovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);
        var search = new UrlSearch(httpClient, settings, browserDiscovery, appIconCache, NullLogger<UrlSearch>.Instance);

        _ = search.Search("https://example.com", 10);
        _ = search.Search("https://example.com", 10);
        _ = search.Search("https://example.com", 10);

        // Solo 1 background check por URL (HEAD + favicon = 2 llamadas máx, no 6)
        // Esperamos un momento para que el background check inicie
        Thread.Sleep(50);
        Assert.True(handler.CallCount <= 2, $"Expected ≤2 calls, got {handler.CallCount}");
    }

    [Fact]
    public void Search_WwwPrefix_NormalizesUrl() {
        var search = BuildSearch();
        var results = search.Search("www.example.com", 10);
        Assert.Single(results);
        var r = Assert.IsType<ResultItemViewModel>(results[0]);
        Assert.Equal("https://www.example.com", r.Title);
    }

    [Fact]
    public void Search_Subtitle_ContainsBrowserName() {
        var search = BuildSearch();
        var results = search.Search("https://example.com", 10);
        var r = Assert.IsType<ResultItemViewModel>(results[0]);
        // ActiveBrowser es null en test (FakePlatformProvider sin browsers)
        Assert.Equal("Open in browser", r.Subtitle);
    }

    [Fact]
    public async Task Stop_ClearsState() {
        var search = BuildSearch(HttpStatusCode.OK);
        var waitTask = WaitForResultChangedAsync(search);
        _ = search.Search("https://example.com", 10);
        await waitTask;

        await search.Stop();

        // Después de Stop, una nueva búsqueda inicia ciclo fresco (Pending de nuevo)
        var results = search.Search("https://example.com", 10);
        Assert.Single(results); // Pending de nuevo
    }
```

- [ ] **Step 2: Verificar que los tests pasan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "UrlSearchTests" 2>&1 | tail -10
```
Esperado: todos los tests de `UrlSearchTests` pasan

- [ ] **Step 3: Correr todos los tests para detectar regresiones**

```bash
dotnet test 2>&1 | tail -5
```
Esperado: `Passed! - Failed: 0`

- [ ] **Step 4: Commit**

```bash
cd .. && git add Yottacast.Core.Tests/Search/UrlSearchTests.cs
git commit -m "test: añadir tests de comportamiento async para UrlSearch"
```

---

## Task 5: UrlSearch — DI + wiring en MainWindowViewModel

**Files:**
- Modify: `Yottacast/App.axaml.cs`
- Modify: `Yottacast/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Registrar UrlSearch en DI**

En `Yottacast/App.axaml.cs`, en `BuildServices()`:

Añadir using al principio si no existe:
```csharp
using Yottacast.Core.Search.Url;
```

Añadir registro justo después de `LocalPathSearch`:
```csharp
services.AddSingleton<UrlSearch>();
services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<UrlSearch>());
```

- [ ] **Step 2: Añadir UrlSearch al constructor de MainWindowViewModel**

En `Yottacast/ViewModels/MainWindowViewModel.cs`:

```csharp
// Antes:
public partial class MainWindowViewModel(
    UserSettings settings,
    GlobalSearch globalSearch,
    ApplicationSearch appSearch,
    FileIconCache fileIconCache,
    UserDocumentSearch userDocumentSearch,
    UpdateChecker updateChecker,
    HistoryService historyService)
    : ViewModelBase {

// Después:
public partial class MainWindowViewModel(
    UserSettings settings,
    GlobalSearch globalSearch,
    ApplicationSearch appSearch,
    FileIconCache fileIconCache,
    UserDocumentSearch userDocumentSearch,
    UpdateChecker updateChecker,
    HistoryService historyService,
    UrlSearch urlSearch)
    : ViewModelBase {
```

Añadir el using al principio del fichero:
```csharp
using Yottacast.Core.Search.Url;
```

- [ ] **Step 3: Suscribir ResultChanged en Initialize()**

En `MainWindowViewModel.Initialize()`, añadir al final del método:
```csharp
urlSearch.ResultChanged += OnUrlResultChanged;
```

Añadir el handler privado (al final de la región de handlers privados, junto a `OnFileIconLoaded` y `OnBadgeIconLoaded`):
```csharp
private void OnUrlResultChanged() {
    Dispatcher.UIThread.Post(RefreshSearch);
}
```

- [ ] **Step 4: Verificar compilación y tests completos**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -5
```
Esperado: `Passed! - Failed: 0`

- [ ] **Step 5: Smoke test manual**

```bash
cd ../Yottacast && dotnet run
```
Pruebas manuales:
- Escribir `https://google.com` → aparece inmediatamente, desaparece si HEAD falla, icono del browser
- Escribir `github.com` → mismo comportamiento
- Escribir `www.apple.com` → normaliza a `https://www.apple.com`
- Escribir `/tmp` → resultado de tipo "Files"
- Escribir `~/Desktop` → resultado de tipo "Files" (si Desktop existe)

- [ ] **Step 6: Commit final**

```bash
cd .. && git add Yottacast/App.axaml.cs Yottacast/ViewModels/MainWindowViewModel.cs
git commit -m "feat: registrar UrlSearch en DI + wiring en MainWindowViewModel"
```
