# System Settings Search — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir una instant search source que permite al usuario buscar y abrir paneles de macOS System Settings desde Yottacast (solo macOS 13+).

**Architecture:** `SystemSettingsSearch` implementa `IInstantSearchSource` en `Yottacast.Core/Search/SystemSettings/`. En startup carga ~45 paneles estáticos de Apple y escanea `/Library/PreferencePanes/` + `~/Library/PreferencePanes/` leyendo plists XML. Busca con `NameMatcher`. La acción abre el panel via URL scheme `x-apple.systempreferences:` a través de `PlatformProvider.LaunchUrl`. Se registra en DI solo en macOS.

**Tech Stack:** .NET 9, `System.Xml.Linq` (XDocument para leer plists), `NameMatcher` (existente), `AppIconCache` (existente), CommunityToolkit.Mvvm, Avalonia 11.

---

## Mapa de ficheros

| Acción | Fichero |
|--------|---------|
| Crear  | `Yottacast.Core/Search/SystemSettings/SystemSettingsPanel.cs` |
| Crear  | `Yottacast.Core/Search/SystemSettings/BuiltinPanels.cs` |
| Crear  | `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs` |
| Crear  | `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs` |
| Modificar | `Yottacast.Core/Platform/PlatformProvider.cs` — añadir `virtual LaunchUrl(string url)` |
| Modificar | `Yottacast.Core/Platform/MacOsPlatformProvider.cs` — override `LaunchUrl` |
| Modificar | `Yottacast.Core/Services/UserSettings.cs` — añadir `EnableSystemSettings` |
| Modificar | `Yottacast/App.axaml.cs` — registro DI condicional macOS |
| Modificar | `Yottacast/Services/AppHandler.cs` — añadir `SupportsSystemSettingsSearch` |
| Modificar | `Yottacast/Services/MacAppHandler.cs` — override `SupportsSystemSettingsSearch` |
| Modificar | `Yottacast/ViewModels/SettingsWindowViewModel.cs` — sección + toggle |
| Modificar | `Yottacast/Views/SettingsWindow.axaml` — nav item + panel de contenido |
| Modificar | `docs/search-sources.md` — nueva sección |

---

## Task 1: Modelo de datos y lista de paneles built-in

**Files:**
- Create: `Yottacast.Core/Search/SystemSettings/SystemSettingsPanel.cs`
- Create: `Yottacast.Core/Search/SystemSettings/BuiltinPanels.cs`

- [ ] **Step 1: Crear `SystemSettingsPanel.cs`**

```csharp
namespace Yottacast.Core.Search.SystemSettings;

public sealed record SystemSettingsPanel(
    string Name,
    string UrlIdentifier,
    bool IsBuiltin = true);
```

- [ ] **Step 2: Crear `BuiltinPanels.cs`**

```csharp
namespace Yottacast.Core.Search.SystemSettings;

internal static class BuiltinPanels {
    public static readonly IReadOnlyList<SystemSettingsPanel> All = [
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
    ];
}
```

- [ ] **Step 3: Commit**

```bash
git add Yottacast.Core/Search/SystemSettings/
git commit -m "feat: SystemSettingsPanel record y BuiltinPanels"
```

---

## Task 2: `LaunchUrl` en la capa de plataforma

**Files:**
- Modify: `Yottacast.Core/Platform/PlatformProvider.cs`
- Modify: `Yottacast.Core/Platform/MacOsPlatformProvider.cs`

- [ ] **Step 1: Añadir `LaunchUrl` a `PlatformProvider.cs`**

Después de `public virtual void OpenFile(string filePath) { }` (aprox. línea 48), añadir:

```csharp
/// <summary>Opens a URL using the OS default handler (e.g. x-apple.systempreferences: on macOS).</summary>
public virtual void LaunchUrl(string url) { }
```

- [ ] **Step 2: Override `LaunchUrl` en `MacOsPlatformProvider.cs`**

En la clase `MacOsPlatformProvider`, añadir después del método `OpenFile`:

```csharp
public override void LaunchUrl(string url) {
    try {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    } catch (Exception ex) {
        logger.LogWarning(ex, "LaunchUrl failed: {Url}", url);
    }
}
```

- [ ] **Step 3: Build**

```bash
cd Yottacast.Core.Tests && dotnet build 2>&1 | grep -E "error"
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Yottacast.Core/Platform/PlatformProvider.cs Yottacast.Core/Platform/MacOsPlatformProvider.cs
git commit -m "feat: LaunchUrl en PlatformProvider para URL schemes del sistema"
```

---

## Task 3: `EnableSystemSettings` en `UserSettings`

**Files:**
- Modify: `Yottacast.Core/Services/UserSettings.cs`

- [ ] **Step 1: Añadir propiedad**

Después de `public bool EnableDictionary { get; set; } = true;` (aprox. línea 47), añadir:

```csharp
public bool EnableSystemSettings { get; set; } = true;
```

- [ ] **Step 2: Commit**

```bash
git add Yottacast.Core/Services/UserSettings.cs
git commit -m "feat: EnableSystemSettings en UserSettings"
```

---

## Task 4: `SystemSettingsSearch` — TDD

**Files:**
- Create: `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs`
- Create: `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs`

- [ ] **Step 1: Escribir el primer test (toggle desactivado)**

Crear `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.SystemSettings;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

internal sealed class TrackingPlatformProvider : FakePlatformProvider {
    public List<string> LaunchedUrls { get; } = new();
    public TrackingPlatformProvider() : base([]) { }
    public override void LaunchUrl(string url) => LaunchedUrls.Add(url);
}

public class SystemSettingsSearchTests {
    private const string SystemSettingsAppPath = "/System/Applications/System Settings.app";

    private static (SystemSettingsSearch search, UserSettings settings, TrackingPlatformProvider platform)
        Build(IReadOnlyList<string>? thirdPartyDirs = null) {
        var platform = new TrackingPlatformProvider();
        var settings = UserSettings.Load(platform);
        var iconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var search = new SystemSettingsSearch(
            settings, platform, iconCache,
            NullLogger<SystemSettingsSearch>.Instance,
            thirdPartyDirs);
        return (search, settings, platform);
    }

    [Fact]
    public async Task Search_WhenDisabled_ReturnsEmpty() {
        var (search, settings, _) = Build();
        settings.EnableSystemSettings = false;
        search.Start();
        await search.WhenReady();
        var results = search.Search("wifi", 10);
        Assert.Empty(results);
    }
}
```

- [ ] **Step 2: Ejecutar test — debe fallar (compile error)**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "SystemSettingsSearchTests" -v minimal 2>&1 | head -20
```

Expected: error de compilación — `SystemSettingsSearch` no existe.

- [ ] **Step 3: Crear esqueleto mínimo de `SystemSettingsSearch.cs`**

Crear `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs`:

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
    IReadOnlyList<string>? thirdPartyDirs = null)
    : IInstantSearchSource {

    private static readonly string SystemSettingsAppPath =
        "/System/Applications/System Settings.app";

    private static readonly IReadOnlyList<string> DefaultThirdPartyDirs = [
        "/Library/PreferencePanes",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library/PreferencePanes"),
    ];

    private readonly IReadOnlyList<string> _thirdPartyDirs =
        thirdPartyDirs ?? DefaultThirdPartyDirs;
    private readonly List<SystemSettingsPanel> _panels = [];
    private readonly TaskCompletionSource _readyTcs = new();

    public void Start() {
        if (!settings.EnableSystemSettings) {
            _readyTcs.TrySetResult();
            return;
        }
        _ = LoadAsync();
    }

    public Task WhenReady() => _readyTcs.Task;

    public Task Stop() {
        _panels.Clear();
        return Task.CompletedTask;
    }

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit) {
        if (!settings.EnableSystemSettings) return [];
        return [];   // implementar en el próximo paso
    }

    private async Task LoadAsync() {
        _readyTcs.TrySetResult();   // implementar en el próximo paso
        await Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Ejecutar test — debe pasar**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "SystemSettingsSearchTests.Search_WhenDisabled_ReturnsEmpty" -v minimal
```

Expected: PASS.

- [ ] **Step 5: Añadir test de búsqueda básica (panel builtin)**

Añadir en `SystemSettingsSearchTests.cs` (dentro de la clase, con el using `using Yottacast.Core.ViewModels;` ya presente):

```csharp
[Fact]
public async Task Search_BuiltinPanel_ExactMatch_ReturnsResult() {
    var (search, _, _) = Build();
    search.Start();
    await search.WhenReady();

    var results = search.Search("Wi-Fi", 10)
        .Cast<ResultItemViewModel>().ToList();

    Assert.Single(results);
    Assert.Equal("Wi-Fi", results[0].Title);
    Assert.Equal("System Settings", results[0].Subtitle);
    Assert.Equal("System Settings", results[0].Category);
    Assert.Equal(1.0, results[0].Score);
}
```

- [ ] **Step 6: Ejecutar test — debe fallar**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "SystemSettingsSearchTests.Search_BuiltinPanel_ExactMatch_ReturnsResult" -v minimal
```

Expected: FAIL (Search devuelve `[]`).

- [ ] **Step 7: Implementar `LoadAsync` y `Search` con paneles builtin**

Reemplazar los métodos `Search`, `LoadAsync` y añadir los helpers en `SystemSettingsSearch.cs`:

```csharp
public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit) {
    if (!settings.EnableSystemSettings) return [];
    return _panels
        .Select(p => (panel: p, score: NameMatcher.Score(p.Name, query)))
        .Where(x => x.score > 0)
        .OrderByDescending(x => x.score)
        .Take(limit)
        .Select(x => BuildResult(x.panel, x.score))
        .ToList();
}

private ResultItemViewModel BuildResult(SystemSettingsPanel panel, double score) {
    var identifier = panel.UrlIdentifier;
    return new ResultItemViewModel {
        Icon      = "⚙️",
        IconBytes = iconCache.Get(SystemSettingsAppPath),
        Title     = panel.Name,
        Subtitle  = panel.IsBuiltin ? "System Settings" : "System Settings · Preference Pane",
        Category  = "System Settings",
        Score     = score,
        OnActivate = () => {
            logger.LogInformation("SystemSettings: open panel={Panel}", panel.Name);
            platform.LaunchUrl($"x-apple.systempreferences:{identifier}");
        },
    };
}

private async Task LoadAsync() {
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

    iconCache.PreloadAsync(SystemSettingsAppPath);
    logger.LogInformation("SystemSettings: loaded {Count} panels", _panels.Count);
    _readyTcs.TrySetResult();
    await Task.CompletedTask;
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
        // plistPath = {bundle}.prefPane/Contents/Info.plist — dos niveles arriba para el nombre
        var bundleDirName = Path.GetDirectoryName(Path.GetDirectoryName(plistPath)!);
        var name = displayName ?? bundleName
            ?? Path.GetFileNameWithoutExtension(bundleDirName ?? plistPath);
        return (name!, bundleId);
    } catch {
        return null;
    }
}
```

- [ ] **Step 8: Ejecutar todos los tests hasta ahora**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "SystemSettingsSearchTests" -v minimal
```

Expected: 2 tests PASS.

- [ ] **Step 9: Añadir tests de CamelHump, sin match y OnActivate**

Añadir en `SystemSettingsSearchTests.cs`:

```csharp
[Fact]
public async Task Search_CamelHump_MatchesBluetooth() {
    var (search, _, _) = Build();
    search.Start();
    await search.WhenReady();

    var results = search.Search("bt", 10).Cast<ResultItemViewModel>().ToList();

    Assert.Contains(results, r => r.Title == "Bluetooth");
}

[Fact]
public async Task Search_NoMatch_ReturnsEmpty() {
    var (search, _, _) = Build();
    search.Start();
    await search.WhenReady();

    var results = search.Search("zzzzzzxxx", 10);

    Assert.Empty(results);
}

[Fact]
public async Task Search_OnActivate_CallsLaunchUrlWithCorrectScheme() {
    var (search, _, platform) = Build();
    search.Start();
    await search.WhenReady();

    var results = search.Search("Bluetooth", 10).Cast<ResultItemViewModel>().ToList();
    var bluetooth = results.First(r => r.Title == "Bluetooth");
    bluetooth.OnActivate?.Invoke();

    Assert.Single(platform.LaunchedUrls);
    Assert.Equal("x-apple.systempreferences:com.apple.preferences.Bluetooth",
        platform.LaunchedUrls[0]);
}
```

- [ ] **Step 10: Ejecutar tests**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "SystemSettingsSearchTests" -v minimal
```

Expected: 5 tests PASS (CamelHump y NoMatch ya funcionan con NameMatcher).

- [ ] **Step 11: Añadir test de pane de terceros**

Añadir en `SystemSettingsSearchTests.cs`:

```csharp
[Fact]
public async Task Search_ThirdPartyPane_AppearsInResults() {
    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    try {
        var bundleContents = Path.Combine(tempDir, "TestPane.prefPane", "Contents");
        Directory.CreateDirectory(bundleContents);
        await File.WriteAllTextAsync(Path.Combine(bundleContents, "Info.plist"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>CFBundleIdentifier</key>
                <string>com.test.testpane</string>
                <key>CFBundleDisplayName</key>
                <string>Test Preference Pane</string>
            </dict>
            </plist>
            """);

        var (search, _, _) = Build(thirdPartyDirs: [tempDir]);
        search.Start();
        await search.WhenReady();

        var results = search.Search("Test", 10).Cast<ResultItemViewModel>().ToList();
        var pane = results.FirstOrDefault(r => r.Title == "Test Preference Pane");

        Assert.NotNull(pane);
        Assert.Equal("System Settings · Preference Pane", pane.Subtitle);
    } finally {
        Directory.Delete(tempDir, recursive: true);
    }
}
```

- [ ] **Step 12: Ejecutar todos los tests**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "SystemSettingsSearchTests" -v minimal
```

Expected: 6 tests PASS.

- [ ] **Step 13: Ejecutar suite completa de tests para verificar no hay regresiones**

```bash
cd Yottacast.Core.Tests && dotnet test
```

Expected: todos los tests PASS.

- [ ] **Step 14: Commit**

```bash
git add Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs \
        Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs
git commit -m "feat: SystemSettingsSearch instant source (TDD)"
```

---

## Task 5: Registro DI en `App.axaml.cs`

**Files:**
- Modify: `Yottacast/App.axaml.cs`

- [ ] **Step 1: Añadir using**

En los `using` del fichero `Yottacast/App.axaml.cs`, añadir:

```csharp
using Yottacast.Core.Search.SystemSettings;
```

- [ ] **Step 2: Añadir registro condicional**

Después de la línea `services.AddSingleton<DictionarySource>();` (aprox. línea 216), añadir:

```csharp
if (OperatingSystem.IsMacOS()) {
    services.AddSingleton<SystemSettingsSearch>(sp => new SystemSettingsSearch(
        sp.GetRequiredService<UserSettings>(),
        sp.GetRequiredService<PlatformProvider>(),
        sp.GetRequiredService<AppIconCache>(),
        sp.GetRequiredService<ILogger<SystemSettingsSearch>>()));
    services.AddSingleton<IInstantSearchSource>(
        sp => sp.GetRequiredService<SystemSettingsSearch>());
}
```

- [ ] **Step 3: Build**

```bash
cd Yottacast && dotnet build -c Debug 2>&1 | grep -E " error "
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Yottacast/App.axaml.cs
git commit -m "feat: registro DI de SystemSettingsSearch (solo macOS)"
```

---

## Task 6: Settings UI — toggle y navegación

**Files:**
- Modify: `Yottacast/Services/AppHandler.cs`
- Modify: `Yottacast/Services/MacAppHandler.cs`
- Modify: `Yottacast/ViewModels/SettingsWindowViewModel.cs`
- Modify: `Yottacast/Views/SettingsWindow.axaml`

- [ ] **Step 1: Añadir `SupportsSystemSettingsSearch` a `AppHandler.cs`**

En `Yottacast/Services/AppHandler.cs`, después de `public virtual void HideCursor() { }`, añadir:

```csharp
/// <summary>True on macOS 13+: System Settings panels can be opened via URL scheme.</summary>
public virtual bool SupportsSystemSettingsSearch => false;
```

- [ ] **Step 2: Override en `MacAppHandler.cs`**

En `Yottacast/Services/MacAppHandler.cs`, dentro de la clase `MacAppHandler`, añadir:

```csharp
public override bool SupportsSystemSettingsSearch => true;
```

- [ ] **Step 3: Añadir `SystemSettings` al enum `SettingsSection`**

En `Yottacast/ViewModels/SettingsWindowViewModel.cs`, cambiar:

```csharp
// Antes:
public enum SettingsSection {
    General, AppSearch, WebSearch, FileSearch, Calculator, Clipboard, Emoji, Dictionary
}

// Después:
public enum SettingsSection {
    General, AppSearch, WebSearch, FileSearch, Calculator, Clipboard, Emoji, Dictionary, SystemSettings
}
```

- [ ] **Step 4: Añadir `[NotifyPropertyChangedFor]` para la nueva sección**

En el bloque de atributos sobre `_selectedSection` (aprox. línea 28-36), añadir:

```csharp
[NotifyPropertyChangedFor(nameof(IsSystemSettingsSelected))]
```

(después de la línea `[NotifyPropertyChangedFor(nameof(IsDictionarySelected))]`)

- [ ] **Step 5: Añadir propiedades y comando de navegación**

Después de `public bool IsDictionarySelected => SelectedSection == SettingsSection.Dictionary;`, añadir:

```csharp
public bool IsSystemSettingsSelected     => SelectedSection == SettingsSection.SystemSettings;
public bool IsSystemSettingsSectionVisible => AppHandler.Instance.SupportsSystemSettingsSearch;
```

Después de `[RelayCommand] private void SelectDictionary() => SelectedSection = SettingsSection.Dictionary;`, añadir:

```csharp
[RelayCommand] private void SelectSystemSettings() => SelectedSection = SettingsSection.SystemSettings;
```

- [ ] **Step 6: Añadir toggle de feature**

En la sección `// ── Feature toggles ─────────────────────────────────────────────────────`, añadir:

```csharp
[ObservableProperty] private bool _enableSystemSettings;
partial void OnEnableSystemSettingsChanged(bool value) {
    _settings.EnableSystemSettings = value;
    _settings.Save();
    _logger.LogInformation("Settings: EnableSystemSettings = {Value}", value);
    _settings.NotifySearchSettingsChanged();
}
```

- [ ] **Step 7: Inicializar en el constructor**

En el constructor de `SettingsWindowViewModel`, después de `_enableDictionary = settings.EnableDictionary;`, añadir:

```csharp
_enableSystemSettings = settings.EnableSystemSettings;
```

- [ ] **Step 8: Añadir icono SVG en `SettingsWindow.axaml`**

En el bloque `<Window.Resources>` donde están los `StreamGeometry` (primeras ~35 líneas), añadir:

```xml
<StreamGeometry x:Key="Icon.SystemSettings">M9.405 1.05c-.413-1.4-2.397-1.4-2.81 0l-.1.34a1.464 1.464 0 0 1-2.105.872l-.31-.17c-1.283-.698-2.686.705-1.987 1.987l.169.311c.446.82.023 1.841-.872 2.105l-.34.1c-1.4.413-1.4 2.397 0 2.81l.34.1a1.464 1.464 0 0 1 .872 2.105l-.17.31c-.698 1.283.705 2.686 1.987 1.987l.311-.169a1.464 1.464 0 0 1 2.105.872l.1.34c.413 1.4 2.397 1.4 2.81 0l.1-.34a1.464 1.464 0 0 1 2.105-.872l.31.17c1.283.698 2.686-.705 1.987-1.987l-.169-.311a1.464 1.464 0 0 1 .872-2.105l.34-.1c1.4-.413 1.4-2.397 0-2.81l-.34-.1a1.464 1.464 0 0 1-.872-2.105l.17-.31c.698-1.283-.705-2.686-1.987-1.987l-.311.169a1.464 1.464 0 0 1-2.105-.872l-.1-.34zM8 10.93a2.929 2.929 0 1 1 0-5.86 2.929 2.929 0 0 1 0 5.858z</StreamGeometry>
```

- [ ] **Step 9: Añadir item de navegación en `SettingsWindow.axaml`**

Después del `</Button>` del Dictionary nav item (aprox. línea 496), añadir:

```xml
<Button Classes="nav-item"
        Classes.nav-selected="{Binding IsSystemSettingsSelected}"
        Command="{Binding SelectSystemSettingsCommand}"
        IsVisible="{Binding IsSystemSettingsSectionVisible}">
    <StackPanel Orientation="Horizontal" Spacing="8">
        <PathIcon Data="{StaticResource Icon.SystemSettings}" Width="14" Height="14" VerticalAlignment="Center"/>
        <TextBlock Text="System Settings" VerticalAlignment="Center"/>
    </StackPanel>
</Button>
```

- [ ] **Step 10: Añadir panel de contenido en `SettingsWindow.axaml`**

Después del `</StackPanel>` de cierre de la sección Dictionary, añadir:

```xml
<!-- System Settings -->
<StackPanel Spacing="16" IsVisible="{Binding IsSystemSettingsSelected}">
    <TextBlock Classes="section-heading" Text="System Settings"/>
    <ToggleSwitch IsChecked="{Binding EnableSystemSettings}"
                  OnContent="Enabled"
                  OffContent="Disabled"/>
    <TextBlock Classes="description"
               Text="Search macOS System Settings panels from the launcher. Type a panel name like 'Wi-Fi', 'Bluetooth', or 'Displays'."/>
</StackPanel>
```

- [ ] **Step 11: Build para verificar compilación**

```bash
cd Yottacast && dotnet build -c Debug 2>&1 | grep -E " error "
```

Expected: 0 errors.

- [ ] **Step 12: Commit**

```bash
git add Yottacast/Services/AppHandler.cs \
        Yottacast/Services/MacAppHandler.cs \
        Yottacast/ViewModels/SettingsWindowViewModel.cs \
        Yottacast/Views/SettingsWindow.axaml
git commit -m "feat: sección System Settings en Settings UI con toggle"
```

---

## Task 7: Documentación

**Files:**
- Modify: `docs/search-sources.md`

- [ ] **Step 1: Añadir sección en `docs/search-sources.md`**

Antes de la sección `## 7. RandomSearch (solo testing)`, insertar:

```markdown
## 7. Búsqueda de paneles de System Settings (macOS 13+)

Permite al usuario buscar y abrir paneles de System Settings directamente desde el launcher. Solo disponible en macOS 13+ (Ventura).

### Invariantes

- Solo se activa en macOS. En otras plataformas la fuente no se registra y no genera resultados.
- Si `EnableSystemSettings = false`, `Search()` devuelve `[]` siempre.
- Los paneles compiten por score con el resto de resultados usando `NameMatcher` (mismo algoritmo que apps, rango 0.0–1.0).
- Las queries que empiezan por `:` (modo emoji) no activan esta fuente.
- Al activar un resultado, abre System Settings en el panel correspondiente via URL scheme `x-apple.systempreferences:{identifier}`.
- Paneles de terceros con el mismo `CFBundleIdentifier` que uno builtin se omiten para evitar duplicados.

### Datos de paneles

- **Builtin**: ~45 entradas estáticas definidas en `BuiltinPanels.cs` para los paneles de Apple de macOS 13+.
- **Terceros**: se escanean `/Library/PreferencePanes/` y `~/Library/PreferencePanes/` en startup. El nombre se extrae del `Info.plist` del bundle (`CFBundleDisplayName` → `CFBundleName` → nombre de fichero). La lectura del plist usa `XDocument` con `DtdProcessing.Ignore` para no realizar peticiones de red al DTD de Apple.

### Resultado visible

| Campo | Builtin | Tercero |
|-------|---------|---------|
| Título | nombre del panel (ej: `"Wi-Fi"`) | nombre del bundle |
| Subtítulo | `"System Settings"` | `"System Settings · Preference Pane"` |
| Categoría | `"System Settings"` | `"System Settings"` |
| Icono | icono de System Settings.app | icono de System Settings.app |

> **Verificar en:** `Search/SystemSettings/SystemSettingsSearch.cs` (Start, Search, LoadAsync, TryReadPlist, BuildResult), `Search/SystemSettings/BuiltinPanels.cs`, `Platform/PlatformProvider.cs` (LaunchUrl), `Platform/MacOsPlatformProvider.cs` (LaunchUrl), `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs`.
```

Renumerar la sección actual "7. RandomSearch" a "8. RandomSearch".

- [ ] **Step 2: Ejecutar suite completa de tests**

```bash
cd Yottacast.Core.Tests && dotnet test
```

Expected: todos los tests PASS.

- [ ] **Step 3: Commit**

```bash
git add docs/search-sources.md
git commit -m "docs: sección System Settings Search en search-sources.md"
```
