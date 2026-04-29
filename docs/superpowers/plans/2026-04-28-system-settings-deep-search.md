# System Settings Deep Search — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ampliar la búsqueda de System Settings con ~100 sub-secciones curadas con deep links y items dinámicos (Wi-Fi actual, VPN activa) con caché de 10 s.

**Architecture:** Se añade `ParentName` al modelo `SystemSettingsPanel` para derivar subtítulos anidados. El catálogo `BuiltinPanels` se expande con sub-items usando anchors en el URL scheme `x-apple.systempreferences:bundle?anchor`. Los items dinámicos se generan en cada `Search()` via dos nuevos métodos virtuales en `PlatformProvider`, cacheados 10 s.

**Tech Stack:** .NET 9, C# 13, xUnit — sin dependencias nuevas.

---

## Ficheros afectados

| Fichero | Cambio |
|---------|--------|
| `Yottacast.Core/Search/SystemSettings/SystemSettingsPanel.cs` | Añadir `ParentName: string?` |
| `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs` | Subtítulo dinámico, merge con dinámicos, caché |
| `Yottacast.Core/Search/SystemSettings/BuiltinPanels.cs` | Expandir a ~110 items |
| `Yottacast.Core/Platform/PlatformProvider.cs` | Añadir `GetCurrentWifiNetworkName`, `GetActiveVpnNames` |
| `Yottacast.Core/Platform/MacOsPlatformProvider.cs` | Implementar ambos métodos |
| `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs` | Nuevos tests |
| `docs/search-sources.md` | Actualizar sección 7 |
| `tools/verify-settings-anchors.sh` | Nuevo — script de verificación manual |

---

## Task 1: Modelo `ParentName` + subtítulo en `BuildResult`

**Files:**
- Modify: `Yottacast.Core/Search/SystemSettings/SystemSettingsPanel.cs`
- Modify: `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs`
- Modify: `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs`

- [ ] **Step 1: Escribir test que falla — subtítulo con ParentName**

En `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs`, añadir tras el último test:

```csharp
[Fact]
public async Task Search_SubItem_ShowsParentInSubtitle() {
    var (search, _, _) = Build();
    search.Start();
    await search.WhenReady();

    var results = search.Search("Camera", 10).Cast<ResultItemViewModel>().ToList();

    var camera = results.FirstOrDefault(r => r.Title == "Camera");
    Assert.NotNull(camera);
    Assert.Equal("System Settings › Privacy & Security", camera.Subtitle);
}

[Fact]
public async Task Search_TopLevelPanel_KeepsPlainSubtitle() {
    var (search, _, _) = Build();
    search.Start();
    await search.WhenReady();

    var results = search.Search("Bluetooth", 10).Cast<ResultItemViewModel>().ToList();

    var bt = results.First(r => r.Title == "Bluetooth" && r.Subtitle == "System Settings");
    Assert.Equal("System Settings", bt.Subtitle);
}
```

- [ ] **Step 2: Ejecutar tests para verificar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "Search_SubItem_ShowsParentInSubtitle|Search_TopLevelPanel_KeepsPlainSubtitle" -v
```

Esperado: FAIL — `camera` es null porque "Camera" no existe en el catálogo aún / no tiene `ParentName`.

- [ ] **Step 3: Añadir `ParentName` al record**

Reemplazar el contenido de `Yottacast.Core/Search/SystemSettings/SystemSettingsPanel.cs`:

```csharp
namespace Yottacast.Core.Search.SystemSettings;

public sealed record SystemSettingsPanel(
    string Name,
    string UrlIdentifier,
    bool IsBuiltin = true,
    string? ParentName = null);
```

- [ ] **Step 4: Actualizar `BuildResult` en `SystemSettingsSearch.cs`**

Reemplazar el método `BuildResult` (líneas ~53-66):

```csharp
private ResultItemViewModel BuildResult(SystemSettingsPanel panel, double score) {
    var identifier = panel.UrlIdentifier;
    var subtitle = panel.ParentName is { } parent
        ? $"System Settings › {parent}"
        : panel.IsBuiltin
            ? "System Settings"
            : "System Settings · Preference Pane";
    return new ResultItemViewModel {
        Icon      = "⚙️",
        IconBytes = iconCache.Get(AppPaths.SystemSettingsAppPath),
        Title     = panel.Name,
        Subtitle  = subtitle,
        Category  = "System Settings",
        Score     = score,
        OnActivate = () => {
            logger.LogInformation("SystemSettings: open panel={Panel}", panel.Name);
            platform.LaunchApp($"x-apple.systempreferences:{identifier}");
        },
    };
}
```

- [ ] **Step 5: Ejecutar todos los tests — deben pasar (menos `Search_SubItem_ShowsParentInSubtitle` que falla porque "Camera" no está en el catálogo todavía)**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "SystemSettingsSearch" -v
```

Esperado: `Search_SubItem_ShowsParentInSubtitle` sigue fallando (Camera no existe). Todos los demás pasan.

- [ ] **Step 6: Commit parcial**

```bash
git add Yottacast.Core/Search/SystemSettings/SystemSettingsPanel.cs \
        Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs \
        Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs
git commit -m "feat: añadir ParentName a SystemSettingsPanel y subtítulo anidado"
```

---

## Task 2: Métodos de plataforma para datos dinámicos

**Files:**
- Modify: `Yottacast.Core/Platform/PlatformProvider.cs`
- Modify: `Yottacast.Core/Platform/MacOsPlatformProvider.cs`

- [ ] **Step 1: Añadir métodos virtuales a `PlatformProvider.cs`**

Añadir antes del último método del fichero (antes de `CollapseHomePath`):

```csharp
/// <summary>Returns the name of the currently connected Wi-Fi network, or null if not connected.</summary>
public virtual string? GetCurrentWifiNetworkName() => null;

/// <summary>Returns the names of currently active (Connected) VPN connections.</summary>
public virtual IReadOnlyList<string> GetActiveVpnNames() => [];
```

- [ ] **Step 2: Implementar en `MacOsPlatformProvider.cs`**

Añadir al final del fichero, antes del último `}` de la clase:

```csharp
// ── Dynamic settings ──────────────────────────────────────────────────────

public override string? GetCurrentWifiNetworkName() {
    try {
        foreach (var iface in new[] { "en0", "en1" }) {
            using var p = Process.Start(new ProcessStartInfo {
                FileName               = "networksetup",
                Arguments              = $"-getairportnetwork {iface}",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
            });
            if (p is null) continue;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            const string prefix = "Current Wi-Fi Network: ";
            if (output.StartsWith(prefix, StringComparison.Ordinal))
                return output[prefix.Length..];
        }
        return null;
    } catch {
        return null;
    }
}

public override IReadOnlyList<string> GetActiveVpnNames() {
    try {
        using var p = Process.Start(new ProcessStartInfo {
            FileName               = "scutil",
            Arguments              = "--nc list",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
        });
        if (p is null) return [];
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        var names = new List<string>();
        // Format: "* (Connected)   UUID   Name   <Type>"
        foreach (var line in output.Split('\n')) {
            if (!line.Contains("(Connected)", StringComparison.Ordinal)) continue;
            var match = System.Text.RegularExpressions.Regex.Match(
                line, @"\(Connected\)\s+[\dA-Fa-f-]{36}\s+(.+?)\s+<");
            if (match.Success)
                names.Add(match.Groups[1].Value.Trim().Trim('"'));
        }
        return names;
    } catch {
        return [];
    }
}
```

- [ ] **Step 3: Compilar para verificar que no hay errores**

```bash
cd Yottacast.Core && dotnet build
```

Esperado: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Yottacast.Core/Platform/PlatformProvider.cs \
        Yottacast.Core/Platform/MacOsPlatformProvider.cs
git commit -m "feat: GetCurrentWifiNetworkName y GetActiveVpnNames en PlatformProvider"
```

---

## Task 3: Items dinámicos en `SystemSettingsSearch` + caché

**Files:**
- Modify: `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs`
- Modify: `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs`

- [ ] **Step 1: Escribir tests que fallan**

Añadir al final de la clase `SystemSettingsSearchTests` en `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs`:

```csharp
// ── Tests de items dinámicos ─────────────────────────────────────────────────

private static (SystemSettingsSearch search, UserSettings settings)
    BuildWithPlatform(FakePlatformProvider platform, IReadOnlyList<string>? thirdPartyDirs = null) {
    var settings = UserSettings.Load(platform);
    var iconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
    var search = new SystemSettingsSearch(
        settings, platform, iconCache,
        NullLogger<SystemSettingsSearch>.Instance,
        thirdPartyDirs,
        dynamicCacheTtl: TimeSpan.FromHours(1)); // no expira durante el test
    return (search, settings);
}

[Fact]
public async Task Search_WifiConnected_ShowsDynamicItem() {
    var platform = new DynamicFakePlatformProvider { WifiNetwork = "MyHomeWifi" };
    var (search, _) = BuildWithPlatform(platform);
    search.Start();
    await search.WhenReady();

    var results = search.Search("MyHomeWifi", 10).Cast<ResultItemViewModel>().ToList();

    Assert.Single(results);
    Assert.Equal("Wi-Fi · MyHomeWifi", results[0].Title);
    Assert.Equal("System Settings › Network", results[0].Subtitle);
}

[Fact]
public async Task Search_WifiDisconnected_NoDynamicWifiItem() {
    var platform = new DynamicFakePlatformProvider { WifiNetwork = null };
    var (search, _) = BuildWithPlatform(platform);
    search.Start();
    await search.WhenReady();

    var results = search.Search("Wi-Fi", 10).Cast<ResultItemViewModel>().ToList();

    Assert.DoesNotContain(results, r => r.Title.StartsWith("Wi-Fi ·"));
}

[Fact]
public async Task Search_VpnActive_ShowsDynamicVpnItem() {
    var platform = new DynamicFakePlatformProvider { VpnNames = ["Work VPN"] };
    var (search, _) = BuildWithPlatform(platform);
    search.Start();
    await search.WhenReady();

    var results = search.Search("Work VPN", 10).Cast<ResultItemViewModel>().ToList();

    Assert.Single(results);
    Assert.Equal("VPN · Work VPN", results[0].Title);
}

[Fact]
public async Task Search_DynamicItems_CachesWithinTtl() {
    var platform = new CountingDynamicProvider { WifiNetwork = "TestNet" };
    var settings = UserSettings.Load(platform);
    var iconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
    var search = new SystemSettingsSearch(
        settings, platform, iconCache,
        NullLogger<SystemSettingsSearch>.Instance,
        thirdPartyDirs: [],
        dynamicCacheTtl: TimeSpan.FromHours(1));
    search.Start();
    await search.WhenReady();

    _ = search.Search("TestNet", 10);
    _ = search.Search("TestNet", 10);

    Assert.Equal(1, platform.WifiCallCount);
}

[Fact]
public async Task Search_DynamicItems_RefreshesAfterTtlExpiry() {
    var platform = new CountingDynamicProvider { WifiNetwork = "TestNet" };
    var settings = UserSettings.Load(platform);
    var iconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
    var search = new SystemSettingsSearch(
        settings, platform, iconCache,
        NullLogger<SystemSettingsSearch>.Instance,
        thirdPartyDirs: [],
        dynamicCacheTtl: TimeSpan.Zero); // expira inmediatamente
    search.Start();
    await search.WhenReady();

    _ = search.Search("TestNet", 10);
    _ = search.Search("TestNet", 10);

    Assert.Equal(2, platform.WifiCallCount);
}

[Fact(Skip = "manual — abre System Settings para verificar visualmente cada anchor")]
public async Task Manual_AllAnchorsOpen() {
    var (search, _, _) = Build();
    search.Start();
    await search.WhenReady();

    // Busca términos representativos de sub-secciones del catálogo para abrirlas una a una
    var probes = new[] {
        "Camera", "Microphone", "Location Services", "Full Disk Access", "FileVault", "Firewall",
        "Keyboard Shortcuts", "Night Shift", "Hot Corners", "Login Items", "AirDrop",
        "Time Zone", "Screen Sharing", "VoiceOver", "Battery Options",
    };
    foreach (var query in probes) {
        var results = search.Search(query, 1).Cast<ResultItemViewModel>().ToList();
        foreach (var r in results) r.OnActivate?.Invoke();
        await Task.Delay(1200);
    }
}

// ── Fakes para tests dinámicos ───────────────────────────────────────────────

private sealed class DynamicFakePlatformProvider(
    string? wifiNetwork = null,
    IReadOnlyList<string>? vpnNames = null)
    : FakePlatformProvider([]) {
    public string? WifiNetwork { get; init; } = wifiNetwork;
    public IReadOnlyList<string> VpnNames { get; init; } = vpnNames ?? [];
    public override string? GetCurrentWifiNetworkName() => WifiNetwork;
    public override IReadOnlyList<string> GetActiveVpnNames() => VpnNames;
}

private sealed class CountingDynamicProvider : FakePlatformProvider {
    public string? WifiNetwork { get; set; }
    public int WifiCallCount { get; private set; }
    public CountingDynamicProvider() : base([]) { }
    public override string? GetCurrentWifiNetworkName() { WifiCallCount++; return WifiNetwork; }
    public override IReadOnlyList<string> GetActiveVpnNames() => [];
}
```

- [ ] **Step 2: Ejecutar tests para verificar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "WifiConnected|WifiDisconnected|VpnActive|CachesWithinTtl|RefreshesAfterTtl" -v
```

Esperado: FAIL — `dynamicCacheTtl` no existe en el constructor, `GetDynamicPanels` no existe.

- [ ] **Step 3: Implementar items dinámicos y caché en `SystemSettingsSearch.cs`**

Reemplazar el fichero completo `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.SystemSettings;

public sealed class SystemSettingsSearch(
    UserSettings settings,
    PlatformProvider platform,
    AppIconCache iconCache,
    ILogger<SystemSettingsSearch> logger,
    IReadOnlyList<string>? thirdPartyDirs = null,
    TimeSpan? dynamicCacheTtl = null)
    : IInstantSearchSource {

    private static readonly IReadOnlyList<string> DefaultThirdPartyDirs = [
        AppPaths.SystemPreferencePanesDir,
        AppPaths.UserPreferencePanesDir,
    ];

    private readonly IReadOnlyList<string> _thirdPartyDirs =
        thirdPartyDirs ?? DefaultThirdPartyDirs;
    private readonly TimeSpan _cacheTtl = dynamicCacheTtl ?? TimeSpan.FromSeconds(10);
    private readonly List<SystemSettingsPanel> _panels = [];
    private readonly TaskCompletionSource _readyTcs = new();

    private IReadOnlyList<SystemSettingsPanel> _dynamicCache = [];
    private DateTime _dynamicCacheTime = DateTime.MinValue;

    public void Start() {
        if (!settings.EnableSystemSettings) {
            _readyTcs.TrySetResult();
            return;
        }
        Task.Run(Load);
    }

    public Task WhenReady() => _readyTcs.Task;

    public Task Stop() {
        _panels.Clear();
        return Task.CompletedTask;
    }

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit) {
        if (!settings.EnableSystemSettings) return [];
        return _panels.Concat(GetDynamicPanels())
            .Select(p => (panel: p, score: NameMatcher.Score(p.Name, query)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(limit)
            .Select(x => BuildResult(x.panel, x.score))
            .ToList();
    }

    private IReadOnlyList<SystemSettingsPanel> GetDynamicPanels() {
        if (DateTime.UtcNow - _dynamicCacheTime < _cacheTtl)
            return _dynamicCache;

        var items = new List<SystemSettingsPanel>();

        var wifi = platform.GetCurrentWifiNetworkName();
        if (wifi is not null)
            items.Add(new SystemSettingsPanel(
                $"Wi-Fi · {wifi}",
                "com.apple.preference.network",
                IsBuiltin: true,
                ParentName: "Network"));

        foreach (var vpn in platform.GetActiveVpnNames())
            items.Add(new SystemSettingsPanel(
                $"VPN · {vpn}",
                "com.apple.preference.network",
                IsBuiltin: true,
                ParentName: "Network"));

        _dynamicCache = items;
        _dynamicCacheTime = DateTime.UtcNow;
        return _dynamicCache;
    }

    private ResultItemViewModel BuildResult(SystemSettingsPanel panel, double score) {
        var identifier = panel.UrlIdentifier;
        var subtitle = panel.ParentName is { } parent
            ? $"System Settings › {parent}"
            : panel.IsBuiltin
                ? "System Settings"
                : "System Settings · Preference Pane";
        return new ResultItemViewModel {
            Icon      = "⚙️",
            IconBytes = iconCache.Get(AppPaths.SystemSettingsAppPath),
            Title     = panel.Name,
            Subtitle  = subtitle,
            Category  = "System Settings",
            Score     = score,
            OnActivate = () => {
                logger.LogInformation("SystemSettings: open panel={Panel}", panel.Name);
                platform.LaunchApp($"x-apple.systempreferences:{identifier}");
            },
        };
    }

    private void Load() {
        try {
            foreach (var panel in BuiltinPanels.All)
                _panels.Add(panel);

            foreach (var dir in _thirdPartyDirs) {
                if (!Directory.Exists(dir)) continue;
                foreach (var bundlePath in Directory.EnumerateDirectories(dir, "*.prefPane")) {
                    var plistPath = Path.Combine(bundlePath, "Contents", "Info.plist");
                    var parsed = TryReadPlist(plistPath);
                    if (parsed is null) continue;
                    var (name, bundleId) = parsed.Value;
                    if (_panels.Any(p => p.UrlIdentifier == bundleId)) continue;
                    _panels.Add(new SystemSettingsPanel(name, bundleId, IsBuiltin: false));
                }
            }

            iconCache.PreloadAsync(AppPaths.SystemSettingsAppPath);
            logger.LogInformation("SystemSettings: loaded {Count} panels", _panels.Count);
        } catch (Exception ex) {
            logger.LogWarning(ex, "SystemSettings: error loading panels, using partial results");
        } finally {
            _readyTcs.TrySetResult();
        }
    }

    private static (string Name, string BundleId)? TryReadPlist(string plistPath) {
        try {
            var xmlSettings = new System.Xml.XmlReaderSettings {
                DtdProcessing = System.Xml.DtdProcessing.Ignore,
                XmlResolver   = null,
            };
            using var reader = System.Xml.XmlReader.Create(plistPath, xmlSettings);
            var doc      = System.Xml.Linq.XDocument.Load(reader);
            var dict     = doc.Root?.Element("dict");
            if (dict is null) return null;

            var children = dict.Elements().ToList();
            string? displayName = null, bundleName = null, bundleId = null;

            for (var i = 0; i + 1 < children.Count; i += 2) {
                if (children[i].Name != "key") continue;
                var value = children[i + 1].Value;
                switch (children[i].Value) {
                    case "CFBundleDisplayName": displayName = value; break;
                    case "CFBundleName":        bundleName  = value; break;
                    case "CFBundleIdentifier":  bundleId    = value; break;
                }
            }

            if (string.IsNullOrWhiteSpace(bundleId)) return null;
            var bundleDirName = Path.GetDirectoryName(Path.GetDirectoryName(plistPath)!);
            var name = displayName ?? bundleName
                ?? Path.GetFileNameWithoutExtension(bundleDirName ?? plistPath);
            return (name!, bundleId);
        } catch {
            return null;
        }
    }
}
```

- [ ] **Step 4: Ejecutar todos los tests de SystemSettings**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "SystemSettingsSearch" -v
```

Esperado: `Search_SubItem_ShowsParentInSubtitle` sigue fallando (Camera no en catálogo aún). Todos los tests dinámicos pasan.

- [ ] **Step 5: Commit**

```bash
git add Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs \
        Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs
git commit -m "feat: items dinámicos Wi-Fi/VPN con caché en SystemSettingsSearch"
```

---

## Task 4: Expandir catálogo `BuiltinPanels.cs`

**Files:**
- Modify: `Yottacast.Core/Search/SystemSettings/BuiltinPanels.cs`

- [ ] **Step 1: Reemplazar el contenido completo del fichero**

```csharp
namespace Yottacast.Core.Search.SystemSettings;

internal static class BuiltinPanels {
    // Anchors verificados en macOS Ventura 13 / Sonoma 14.
    // Al actualizar macOS, ejecutar tools/verify-settings-anchors.sh para re-verificar.
    // Si un anchor falla, open abre el panel padre sin navegar: degradación silenciosa.
    public static readonly IReadOnlyList<SystemSettingsPanel> All = [

        // ── Paneles de primer nivel ───────────────────────────────────────────
        new("Wi-Fi",               "com.apple.preference.network"),
        new("Bluetooth",           "com.apple.preferences.Bluetooth"),
        new("Network",             "com.apple.preference.network"),
        new("VPN",                 "com.apple.preference.network"),
        new("Notifications",       "com.apple.preference.notifications"),
        new("Focus",               "com.apple.preference.notifications"),
        new("Sound",               "com.apple.preference.sound"),
        new("Screen Time",         "com.apple.preference.screentime"),
        new("General",             "com.apple.preference.general"),
        new("Appearance",          "com.apple.preference.general"),
        new("Accessibility",       "com.apple.preference.universalaccess"),
        new("Control Center",      "com.apple.preference.controllcenter"),
        new("Siri & Spotlight",    "com.apple.preference.speech"),
        new("Privacy & Security",  "com.apple.preference.security"),
        new("Privacy",             "com.apple.preference.security"),
        new("Security",            "com.apple.preference.security"),
        new("Desktop & Dock",      "com.apple.preference.exposeclassic"),
        new("Stage Manager",       "com.apple.preference.exposeclassic"),
        new("Mission Control",     "com.apple.preference.exposeclassic"),
        new("Displays",            "com.apple.preference.displays"),
        new("Wallpaper",           "com.apple.preference.desktopscreeneffect"),
        new("Screen Saver",        "com.apple.preference.desktopscreeneffect"),
        new("Battery",             "com.apple.preference.battery"),
        new("Energy Saver",        "com.apple.preference.battery"),
        new("Lock Screen",         "com.apple.preference.security"),
        new("Touch ID & Password", "com.apple.systempreferences.LocalAuthenticationPrefPane"),
        new("Users & Groups",      "com.apple.preferences.users"),
        new("Passwords",           "com.apple.Passwords"),
        new("Apple ID",            "com.apple.systempreferences.AppleIDPrefPane"),
        new("Family Sharing",      "com.apple.systempreferences.FamilySharingPrefPane"),
        new("Internet Accounts",   "com.apple.preference.internetaccounts"),
        new("Game Center",         "com.apple.systempreferences.GameCenterPrefPane"),
        new("Wallet & Apple Pay",  "com.apple.systempreferences.WalletPrefPane"),
        new("Keyboard",            "com.apple.preference.keyboard"),
        new("Trackpad",            "com.apple.preference.trackpad"),
        new("Mouse",               "com.apple.preference.mouse"),
        new("Printers & Scanners", "com.apple.preference.printfax"),
        new("Date & Time",         "com.apple.preference.datetime"),
        new("Language & Region",   "com.apple.Localization"),
        new("Storage",             "com.apple.preference.storage"),
        new("Sharing",             "com.apple.preferences.sharing"),
        new("Time Machine",        "com.apple.prefs.backup"),
        new("Software Update",     "com.apple.preferences.softwareupdate"),
        new("Startup Disk",        "com.apple.preference.startupdisk"),
        new("Extensions",          "com.apple.preference.extensions"),

        // ── Privacy & Security ───────────────────────────────────────────────
        new("Camera",                 "com.apple.preference.security?Privacy_Camera",            ParentName: "Privacy & Security"),
        new("Microphone",             "com.apple.preference.security?Privacy_Microphone",        ParentName: "Privacy & Security"),
        new("Location Services",      "com.apple.preference.security?Privacy_LocationServices",  ParentName: "Privacy & Security"),
        new("Contacts Permissions",   "com.apple.preference.security?Privacy_ContactsFull",      ParentName: "Privacy & Security"),
        new("Calendars Permissions",  "com.apple.preference.security?Privacy_Calendars",         ParentName: "Privacy & Security"),
        new("Reminders Permissions",  "com.apple.preference.security?Privacy_Reminders",         ParentName: "Privacy & Security"),
        new("Photos Permissions",     "com.apple.preference.security?Privacy_Photos",            ParentName: "Privacy & Security"),
        new("Bluetooth Permissions",  "com.apple.preference.security?Privacy_Bluetooth",         ParentName: "Privacy & Security"),
        new("Screen Recording",       "com.apple.preference.security?Privacy_ScreenCapture",     ParentName: "Privacy & Security"),
        new("Accessibility Apps",     "com.apple.preference.security?Privacy_Accessibility",     ParentName: "Privacy & Security"),
        new("Full Disk Access",       "com.apple.preference.security?Privacy_AllFiles",          ParentName: "Privacy & Security"),
        new("Files and Folders",      "com.apple.preference.security?Privacy_FilesAndFolders",   ParentName: "Privacy & Security"),
        new("Home Permissions",       "com.apple.preference.security?Privacy_HomeKit",           ParentName: "Privacy & Security"),
        new("Media & Apple Music",    "com.apple.preference.security?Privacy_MediaLibrary",      ParentName: "Privacy & Security"),
        new("Motion & Fitness",       "com.apple.preference.security?Privacy_Motion",            ParentName: "Privacy & Security"),
        new("Speech Recognition",     "com.apple.preference.security?Privacy_SpeechRecognition", ParentName: "Privacy & Security"),
        new("Automation Permissions", "com.apple.preference.security?Privacy_Automation",        ParentName: "Privacy & Security"),
        new("Developer Tools",        "com.apple.preference.security?Privacy_DevTools",          ParentName: "Privacy & Security"),
        new("Analytics & Improvements","com.apple.preference.security?Privacy_Analytics",        ParentName: "Privacy & Security"),
        new("Apple Advertising",      "com.apple.preference.security?Privacy_Advertising",       ParentName: "Privacy & Security"),
        new("FileVault",              "com.apple.preference.security?FDE",                       ParentName: "Privacy & Security"),
        new("Firewall",               "com.apple.preference.security?Firewall",                  ParentName: "Privacy & Security"),
        new("Advanced Security",      "com.apple.preference.security?Advanced",                  ParentName: "Privacy & Security"),

        // ── Keyboard ─────────────────────────────────────────────────────────
        new("Keyboard Shortcuts",  "com.apple.preference.keyboard?Shortcuts",    ParentName: "Keyboard"),
        new("Text Replacements",   "com.apple.preference.keyboard?Text",         ParentName: "Keyboard"),
        new("Dictation",           "com.apple.preference.keyboard?Dictation",    ParentName: "Keyboard"),
        new("Input Sources",       "com.apple.preference.keyboard?InputSources", ParentName: "Keyboard"),

        // ── Displays ──────────────────────────────────────────────────────────
        new("Night Shift",         "com.apple.preference.displays?nightShift",   ParentName: "Displays"),
        new("Display Resolution",  "com.apple.preference.displays?scaled",       ParentName: "Displays"),
        new("Color Profile",       "com.apple.preference.displays?ColorProfile", ParentName: "Displays"),

        // ── Desktop & Dock ────────────────────────────────────────────────────
        new("Hot Corners",         "com.apple.preference.exposeclassic?hotcorners", ParentName: "Desktop & Dock"),
        new("Dock Settings",       "com.apple.preference.exposeclassic?dock",       ParentName: "Desktop & Dock"),

        // ── Sound ─────────────────────────────────────────────────────────────
        new("Sound Output",        "com.apple.preference.sound?output", ParentName: "Sound"),
        new("Sound Input",         "com.apple.preference.sound?input",  ParentName: "Sound"),

        // ── General ───────────────────────────────────────────────────────────
        new("Login Items",         "com.apple.preference.general?LoginItems", ParentName: "General"),
        new("AirDrop & Handoff",   "com.apple.preference.general?AirDrop",   ParentName: "General"),

        // ── Language & Region ─────────────────────────────────────────────────
        new("Language",            "com.apple.Localization?language", ParentName: "Language & Region"),
        new("Region",              "com.apple.Localization?region",   ParentName: "Language & Region"),
        new("Calendar Format",     "com.apple.Localization?calendar", ParentName: "Language & Region"),

        // ── Date & Time ───────────────────────────────────────────────────────
        new("Time Zone",           "com.apple.preference.datetime?TimeZone", ParentName: "Date & Time"),

        // ── Sharing ───────────────────────────────────────────────────────────
        new("Screen Sharing",      "com.apple.preferences.sharing?Services_ScreenSharing",  ParentName: "Sharing"),
        new("File Sharing",        "com.apple.preferences.sharing?Services_ARDService",     ParentName: "Sharing"),
        new("Printer Sharing",     "com.apple.preferences.sharing?Services_PrinterSharing", ParentName: "Sharing"),
        new("Remote Login",        "com.apple.preferences.sharing?Services_RemoteLogin",    ParentName: "Sharing"),
        new("Remote Management",   "com.apple.preferences.sharing?Services_ARD",            ParentName: "Sharing"),
        new("Internet Sharing",    "com.apple.preferences.sharing?Services_InternetSharing",ParentName: "Sharing"),
        new("Content Caching",     "com.apple.preferences.sharing?Services_NetworkCache",   ParentName: "Sharing"),

        // ── Accessibility ─────────────────────────────────────────────────────
        new("VoiceOver",           "com.apple.preference.universalaccess?VoiceOver",       ParentName: "Accessibility"),
        new("Zoom",                "com.apple.preference.universalaccess?Seeing_Zoom",     ParentName: "Accessibility"),
        new("Display Accessibility","com.apple.preference.universalaccess?Seeing_Display", ParentName: "Accessibility"),
        new("Spoken Content",      "com.apple.preference.universalaccess?Seeing_Content",  ParentName: "Accessibility"),
        new("Audio Descriptions",  "com.apple.preference.universalaccess?Seeing_Audio",    ParentName: "Accessibility"),
        new("Audio Accessibility", "com.apple.preference.universalaccess?Hearing_Audio",   ParentName: "Accessibility"),
        new("RTT",                 "com.apple.preference.universalaccess?Hearing_RTT",     ParentName: "Accessibility"),
        new("Keyboard Accessibility","com.apple.preference.universalaccess?Keyboard",      ParentName: "Accessibility"),
        new("Pointer Control",     "com.apple.preference.universalaccess?Mouse",           ParentName: "Accessibility"),
        new("Switch Control",      "com.apple.preference.universalaccess?Switch",          ParentName: "Accessibility"),

        // ── Battery ───────────────────────────────────────────────────────────
        new("Battery Options",     "com.apple.preference.battery?options",     ParentName: "Battery"),
        new("Battery Usage History","com.apple.preference.battery?UsageHistory",ParentName: "Battery"),
    ];
}
```

- [ ] **Step 2: Ejecutar todos los tests de SystemSettings**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "SystemSettingsSearch" -v
```

Esperado: TODOS pasan, incluido `Search_SubItem_ShowsParentInSubtitle` (Camera ya está en el catálogo).

- [ ] **Step 3: Ejecutar suite completa de tests**

```bash
cd Yottacast.Core.Tests && dotnet test
```

Esperado: todos los tests pasan.

- [ ] **Step 4: Commit**

```bash
git add Yottacast.Core/Search/SystemSettings/BuiltinPanels.cs
git commit -m "feat: catálogo expandido con ~60 sub-secciones de System Settings"
```

---

## Task 5: Docs + script de verificación

**Files:**
- Modify: `docs/search-sources.md`
- Create: `tools/verify-settings-anchors.sh`

- [ ] **Step 1: Actualizar la sección 7 de `docs/search-sources.md`**

Reemplazar desde la línea `## 7. Búsqueda de paneles de System Settings (macOS 13+)` hasta el bloque `> **Verificar en:**` con:

```markdown
## 7. Búsqueda de paneles de System Settings (macOS 13+)

Permite al usuario buscar y abrir paneles y sub-secciones de System Settings directamente desde el launcher. Solo disponible en macOS 13+ (Ventura).

### Invariantes

- Solo se activa en macOS. En otras plataformas la fuente no se registra y no genera resultados.
- Si `EnableSystemSettings = false`, `Search()` devuelve `[]` siempre.
- Los paneles compiten por score con el resto de resultados usando `NameMatcher` (mismo algoritmo que apps, rango 0.0–1.0).
- Las queries que empiezan por `:` (modo emoji) no activan esta fuente.
- Al activar un resultado, abre System Settings en el panel o sub-sección correspondiente via URL scheme `x-apple.systempreferences:{identifier}` (con anchor opcional: `bundle?anchor`). Si un anchor no está soportado por la versión de macOS actual, `open` abre el panel padre — degradación silenciosa, sin error.
- Paneles de terceros con el mismo `CFBundleIdentifier` que uno builtin se omiten para evitar duplicados.

### Datos de paneles

- **Builtin**: ~110 entradas estáticas definidas en `BuiltinPanels.cs`, organizadas en dos grupos:
  - Paneles de primer nivel (~45): abren el panel raíz.
  - Sub-secciones (~65): tienen `ParentName` y usan anchors en el URL identifier (p.ej. `com.apple.preference.security?Privacy_Camera`). Verificados en macOS Ventura 13 / Sonoma 14.
- **Terceros**: se escanean `/Library/PreferencePanes/` y `~/Library/PreferencePanes/` en startup. El nombre se extrae del `Info.plist` del bundle (`CFBundleDisplayName` → `CFBundleName` → nombre de fichero). La lectura del plist usa `XDocument` con `DtdProcessing.Ignore` para no realizar peticiones de red al DTD de Apple.

### Items dinámicos

En cada llamada a `Search()`, se generan items adicionales basados en el estado actual del sistema:

| Condición | Item | Subtítulo |
|-----------|------|-----------|
| Wi-Fi conectada a "MyNet" | `"Wi-Fi · MyNet"` | `"System Settings › Network"` |
| VPN "Work VPN" activa | `"VPN · Work VPN"` | `"System Settings › Network"` |

Los items dinámicos se cachean 10 s para no añadir latencia al tipado. Si la consulta al sistema falla, no aparecen items dinámicos (solo estáticos).

### Resultado visible

| Campo | Panel primer nivel | Sub-sección builtin | Tercero |
|-------|-------------------|---------------------|---------|
| Título | nombre del panel | nombre de la sub-sección | nombre del bundle |
| Subtítulo | `"System Settings"` | `"System Settings › {ParentName}"` | `"System Settings · Preference Pane"` |
| Categoría | `"System Settings"` | `"System Settings"` | `"System Settings"` |
| Icono | icono de System Settings.app | icono de System Settings.app | icono de System Settings.app |

### Verificación de anchors

Al actualizar macOS, ejecutar `tools/verify-settings-anchors.sh` para verificar visualmente que cada anchor navega a la sección correcta. El script abre cada URL con 1 s de delay entre ellas.

> **Verificar en:** `Search/SystemSettings/SystemSettingsSearch.cs` (Start, Search, GetDynamicPanels, Load, TryReadPlist, BuildResult), `Search/SystemSettings/BuiltinPanels.cs`, `Platform/PlatformProvider.cs` (GetCurrentWifiNetworkName, GetActiveVpnNames), `Platform/MacOsPlatformProvider.cs` (GetCurrentWifiNetworkName, GetActiveVpnNames), `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs`.
```

- [ ] **Step 2: Crear `tools/verify-settings-anchors.sh`**

```bash
#!/usr/bin/env bash
# Verifica visualmente que los anchors de System Settings navegan a la sección correcta.
# Uso: sh tools/verify-settings-anchors.sh
# Ejecutar antes de releases o tras actualizar macOS.
# Cada URL se abre con 1 s de delay para que puedas ver el resultado.

set -euo pipefail

ITEMS=(
    # Primer nivel
    "Wi-Fi|com.apple.preference.network"
    "Bluetooth|com.apple.preferences.Bluetooth"
    "Notifications|com.apple.preference.notifications"
    "Sound|com.apple.preference.sound"
    "General|com.apple.preference.general"
    "Privacy & Security|com.apple.preference.security"
    "Displays|com.apple.preference.displays"
    "Desktop & Dock|com.apple.preference.exposeclassic"
    "Battery|com.apple.preference.battery"
    "Keyboard|com.apple.preference.keyboard"
    "Date & Time|com.apple.preference.datetime"
    "Language & Region|com.apple.Localization"
    "Sharing|com.apple.preferences.sharing"
    "Accessibility|com.apple.preference.universalaccess"
    # Privacy & Security
    "Camera|com.apple.preference.security?Privacy_Camera"
    "Microphone|com.apple.preference.security?Privacy_Microphone"
    "Location Services|com.apple.preference.security?Privacy_LocationServices"
    "Contacts Permissions|com.apple.preference.security?Privacy_ContactsFull"
    "Calendars Permissions|com.apple.preference.security?Privacy_Calendars"
    "Reminders Permissions|com.apple.preference.security?Privacy_Reminders"
    "Photos Permissions|com.apple.preference.security?Privacy_Photos"
    "Bluetooth Permissions|com.apple.preference.security?Privacy_Bluetooth"
    "Screen Recording|com.apple.preference.security?Privacy_ScreenCapture"
    "Accessibility Apps|com.apple.preference.security?Privacy_Accessibility"
    "Full Disk Access|com.apple.preference.security?Privacy_AllFiles"
    "Files and Folders|com.apple.preference.security?Privacy_FilesAndFolders"
    "Home Permissions|com.apple.preference.security?Privacy_HomeKit"
    "Media & Apple Music|com.apple.preference.security?Privacy_MediaLibrary"
    "Motion & Fitness|com.apple.preference.security?Privacy_Motion"
    "Speech Recognition|com.apple.preference.security?Privacy_SpeechRecognition"
    "Automation Permissions|com.apple.preference.security?Privacy_Automation"
    "Developer Tools|com.apple.preference.security?Privacy_DevTools"
    "Analytics & Improvements|com.apple.preference.security?Privacy_Analytics"
    "Apple Advertising|com.apple.preference.security?Privacy_Advertising"
    "FileVault|com.apple.preference.security?FDE"
    "Firewall|com.apple.preference.security?Firewall"
    "Advanced Security|com.apple.preference.security?Advanced"
    # Keyboard
    "Keyboard Shortcuts|com.apple.preference.keyboard?Shortcuts"
    "Text Replacements|com.apple.preference.keyboard?Text"
    "Dictation|com.apple.preference.keyboard?Dictation"
    "Input Sources|com.apple.preference.keyboard?InputSources"
    # Displays
    "Night Shift|com.apple.preference.displays?nightShift"
    "Display Resolution|com.apple.preference.displays?scaled"
    "Color Profile|com.apple.preference.displays?ColorProfile"
    # Desktop & Dock
    "Hot Corners|com.apple.preference.exposeclassic?hotcorners"
    "Dock Settings|com.apple.preference.exposeclassic?dock"
    # Sound
    "Sound Output|com.apple.preference.sound?output"
    "Sound Input|com.apple.preference.sound?input"
    # General
    "Login Items|com.apple.preference.general?LoginItems"
    "AirDrop & Handoff|com.apple.preference.general?AirDrop"
    # Language & Region
    "Language|com.apple.Localization?language"
    "Region|com.apple.Localization?region"
    "Calendar Format|com.apple.Localization?calendar"
    # Date & Time
    "Time Zone|com.apple.preference.datetime?TimeZone"
    # Sharing
    "Screen Sharing|com.apple.preferences.sharing?Services_ScreenSharing"
    "File Sharing|com.apple.preferences.sharing?Services_ARDService"
    "Printer Sharing|com.apple.preferences.sharing?Services_PrinterSharing"
    "Remote Login|com.apple.preferences.sharing?Services_RemoteLogin"
    "Remote Management|com.apple.preferences.sharing?Services_ARD"
    "Internet Sharing|com.apple.preferences.sharing?Services_InternetSharing"
    "Content Caching|com.apple.preferences.sharing?Services_NetworkCache"
    # Accessibility
    "VoiceOver|com.apple.preference.universalaccess?VoiceOver"
    "Zoom|com.apple.preference.universalaccess?Seeing_Zoom"
    "Display Accessibility|com.apple.preference.universalaccess?Seeing_Display"
    "Spoken Content|com.apple.preference.universalaccess?Seeing_Content"
    "Audio Descriptions|com.apple.preference.universalaccess?Seeing_Audio"
    "Audio Accessibility|com.apple.preference.universalaccess?Hearing_Audio"
    "RTT|com.apple.preference.universalaccess?Hearing_RTT"
    "Keyboard Accessibility|com.apple.preference.universalaccess?Keyboard"
    "Pointer Control|com.apple.preference.universalaccess?Mouse"
    "Switch Control|com.apple.preference.universalaccess?Switch"
    # Battery
    "Battery Options|com.apple.preference.battery?options"
    "Battery Usage History|com.apple.preference.battery?UsageHistory"
)

total=${#ITEMS[@]}
echo "Verificando $total anchors de System Settings..."
echo "Cierra System Settings antes de empezar para ver los cambios claramente."
echo ""

for i in "${!ITEMS[@]}"; do
    item="${ITEMS[$i]}"
    name="${item%%|*}"
    identifier="${item##*|}"
    echo "[$((i+1))/$total] $name"
    open "x-apple.systempreferences:$identifier"
    sleep 1
done

echo ""
echo "Hecho. Marca en BuiltinPanels.cs los anchors que no navegaron correctamente."
echo "Actualiza el comentario de versión si cambias el catálogo."
```

- [ ] **Step 3: Dar permisos de ejecución al script**

```bash
chmod +x tools/verify-settings-anchors.sh
```

- [ ] **Step 4: Ejecutar suite completa de tests por última vez**

```bash
cd Yottacast.Core.Tests && dotnet test
```

Esperado: todos los tests pasan.

- [ ] **Step 5: Commit final**

```bash
git add docs/search-sources.md tools/verify-settings-anchors.sh
git commit -m "docs: actualizar search-sources.md y añadir script de verificación de anchors"
```
