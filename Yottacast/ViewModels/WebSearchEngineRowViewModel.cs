using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.WebSearch;
using Yottacast.Core.Services;

namespace Yottacast.ViewModels;

public partial class WebSearchEngineRowViewModel : ViewModelBase {
    private readonly UserSettings _settings;
    private readonly PlatformProvider _platform;
    private readonly string _defaultQueryUrl;

    public string   Id    { get; }
    public string   Name  { get; }
    public string   Group { get; }
    public Bitmap?  Icon  { get; }
    public bool     IsPlugin { get; }
    public string?  PluginFilePath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrefixEnabled))]
    [NotifyPropertyChangedFor(nameof(ModeLabel))]
    [NotifyPropertyChangedFor(nameof(PrefixOpacity))]
    private WebSearchMode _mode;

    [ObservableProperty] private string _prefix   = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomUrl))]
    private string _queryUrl = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _enabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _showDisabled = true;

    public bool   IsPrefixEnabled    => Mode == WebSearchMode.PrefixOnly;
    public bool   IsVisible          => Enabled || ShowDisabled;
    public string ModeLabel          => Mode == WebSearchMode.PrefixOnly ? "Prefix only" : "Show always";
    public string QueryUrlWatermark  => _defaultQueryUrl;
    public double PrefixOpacity      => IsPrefixEnabled ? 1.0 : 0.35;
    public bool   HasCustomUrl       => !string.IsNullOrEmpty(QueryUrl) && QueryUrl != _defaultQueryUrl;

    /// <summary>Constructor for built-in engines (icon from embedded resource).</summary>
    public WebSearchEngineRowViewModel(
        string id, string name, string group, string defaultQueryUrl, string? iconResource,
        WebSearchEngineSettings cfg, UserSettings settings, PlatformProvider platform) {
        Id               = id;
        Name             = name;
        Group            = group;
        _defaultQueryUrl = defaultQueryUrl;
        _settings        = settings;
        _platform        = platform;
        _mode            = cfg.Mode;
        _prefix          = cfg.Prefix;
        _queryUrl        = cfg.QueryUrl ?? defaultQueryUrl;
        _enabled         = cfg.Enabled;

        if (iconResource != null) {
            var asm = typeof(WebSearchEngine).Assembly;
            using var stream = asm.GetManifestResourceStream(iconResource);
            if (stream != null) Icon = new Bitmap(stream);
        }
    }

    /// <summary>Constructor for plugin engines (icon from raw bytes).</summary>
    public WebSearchEngineRowViewModel(
        string id, string name, string group, string defaultQueryUrl, byte[]? iconBytes,
        WebSearchEngineSettings cfg, UserSettings settings, PlatformProvider platform,
        string pluginFilePath) {
        Id               = id;
        Name             = name;
        Group            = group;
        _defaultQueryUrl = defaultQueryUrl;
        _settings        = settings;
        _platform        = platform;
        _mode            = cfg.Mode;
        _prefix          = cfg.Prefix;
        _queryUrl        = cfg.QueryUrl ?? defaultQueryUrl;
        _enabled         = cfg.Enabled;
        IsPlugin         = true;
        PluginFilePath   = pluginFilePath;

        if (iconBytes is { Length: > 0 }) {
            using var ms = new MemoryStream(iconBytes);
            try { Icon = new Bitmap(ms); } catch { /* corrupt icon data */ }
        }
    }

    [RelayCommand]
    private void ToggleMode() =>
        Mode = Mode == WebSearchMode.PrefixOnly ? WebSearchMode.ShowAlways : WebSearchMode.PrefixOnly;

    [RelayCommand]
    private void ResetUrl() => QueryUrl = _defaultQueryUrl;

    [RelayCommand]
    private void TestUrl() {
        var url = string.IsNullOrEmpty(QueryUrl) ? _defaultQueryUrl : QueryUrl;
        var testUrl = url.Replace("{0}", "test");
        _platform.OpenUrl(testUrl, _settings.Browser);
    }

    [RelayCommand]
    private void ShowPluginFolder() {
        if (PluginFilePath == null) return;
        var dir = Path.GetDirectoryName(PluginFilePath);
        if (dir != null) _platform.RevealInFileManager(dir);
    }

    [RelayCommand]
    private void EditPluginSource() {
        if (PluginFilePath == null) return;
        _platform.OpenFile(PluginFilePath);
    }

    // Llamado desde code-behind al perder el foco el TextBox de URL.
    // Si el usuario dejó el campo vacío, lo restaura al valor por defecto para que
    // la próxima vez que se abra el popup aparezca algo editable.
    public void NormalizeQueryUrl() {
        if (string.IsNullOrEmpty(QueryUrl))
            QueryUrl = _defaultQueryUrl;
    }

    partial void OnModeChanged(WebSearchMode value) => SaveToSettings();

    partial void OnPrefixChanged(string value)   => SaveToSettings();
    partial void OnQueryUrlChanged(string value) => SaveToSettings();
    partial void OnEnabledChanged(bool value)    => SaveToSettings();

    private void SaveToSettings() {
        var idx = _settings.WebSearchEngines.FindIndex(s => s.Id == Id);
        if (idx < 0) return;
        var effectiveUrl = (string.IsNullOrEmpty(QueryUrl) || QueryUrl == _defaultQueryUrl) ? null : QueryUrl;
        var existing = _settings.WebSearchEngines[idx];
        if (existing.Enabled == Enabled && existing.Mode == Mode &&
            existing.Prefix == Prefix && existing.QueryUrl == effectiveUrl) return;
        _settings.WebSearchEngines[idx] = new WebSearchEngineSettings {
            Id       = Id,
            Enabled  = Enabled,
            Mode     = Mode,
            Prefix   = Prefix,
            QueryUrl = effectiveUrl,
        };
        _settings.Save();
    }
}
