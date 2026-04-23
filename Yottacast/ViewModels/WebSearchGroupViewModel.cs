using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Yottacast.ViewModels;

public partial class WebSearchGroupViewModel : ViewModelBase {
    public string GroupId { get; }
    public string Title { get; }
    public IReadOnlyList<WebSearchEngineRowViewModel> Engines { get; }

    [ObservableProperty] private bool _isVisible = true;

    public WebSearchGroupViewModel(string groupId, IReadOnlyList<WebSearchEngineRowViewModel> engines) {
        GroupId = groupId;
        Title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(groupId);
        Engines = engines;

        foreach (var engine in engines)
            engine.PropertyChanged += OnEnginePropertyChanged;

        UpdateVisibility();
    }

    private void OnEnginePropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(WebSearchEngineRowViewModel.IsVisible))
            UpdateVisibility();
    }

    private void UpdateVisibility() =>
        IsVisible = Engines.Any(e => e.IsVisible);
}
