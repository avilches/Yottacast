using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yottacast.Core.Search.WebSearch;
using Yottacast.Core.Services;

namespace Yottacast.ViewModels;

public partial class WebSearchEngineRowViewModel : ViewModelBase {
    private readonly UserSettings _settings;
    private readonly string _defaultQueryUrl;

    public string Id   { get; }
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrefixEnabled))]
    [NotifyPropertyChangedFor(nameof(ModeLabel))]
    [NotifyPropertyChangedFor(nameof(PrefixOpacity))]
    private WebSearchMode _mode;

    [ObservableProperty] private string _prefix   = "";
    [ObservableProperty] private string _queryUrl = "";
    [ObservableProperty] private bool   _enabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrefixNotEditing))]
    private bool _isPrefixEditing;

    public bool   IsPrefixEnabled  => Mode == WebSearchMode.PrefixOnly;
    public bool   IsPrefixNotEditing => !IsPrefixEditing;
    public string ModeLabel        => Mode == WebSearchMode.PrefixOnly ? "Prefix" : "Always";
    public string QueryUrlWatermark => _defaultQueryUrl;
    public double PrefixOpacity    => IsPrefixEnabled ? 1.0 : 0.35;

    public WebSearchEngineRowViewModel(
        string id, string name, string defaultQueryUrl,
        WebSearchEngineSettings cfg, UserSettings settings) {
        Id               = id;
        Name             = name;
        _defaultQueryUrl = defaultQueryUrl;
        _settings        = settings;
        _mode            = cfg.Mode;
        _prefix          = cfg.Prefix;
        _queryUrl        = cfg.QueryUrl ?? "";
        _enabled         = cfg.Enabled;
    }

    [RelayCommand]
    private void ToggleMode() =>
        Mode = Mode == WebSearchMode.PrefixOnly ? WebSearchMode.ShowAlways : WebSearchMode.PrefixOnly;

    partial void OnModeChanged(WebSearchMode value) {
        if (value != WebSearchMode.PrefixOnly) IsPrefixEditing = false;
        SaveToSettings();
    }

    partial void OnPrefixChanged(string value)   => SaveToSettings();
    partial void OnQueryUrlChanged(string value) => SaveToSettings();
    partial void OnEnabledChanged(bool value)    => SaveToSettings();

    private void SaveToSettings() {
        var idx = _settings.WebSearchEngines.FindIndex(s => s.Id == Id);
        if (idx < 0) return;
        _settings.WebSearchEngines[idx] = new WebSearchEngineSettings {
            Id       = Id,
            Enabled  = Enabled,
            Mode     = Mode,
            Prefix   = Prefix,
            QueryUrl = string.IsNullOrEmpty(QueryUrl) ? null : QueryUrl,
        };
        _settings.Save();
    }
}
