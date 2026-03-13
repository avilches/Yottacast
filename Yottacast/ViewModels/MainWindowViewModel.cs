using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Yottacast.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ResultItemViewModel? _selectedResult;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _showNoResults;

    public ObservableCollection<ResultItemViewModel> Results { get; } = [];

    private readonly List<ResultItemViewModel> _allItems =
    [
        new() { Icon = "⚡", Title = "Open Terminal",       Subtitle = "Launch Terminal application",         Category = "Apps",    Shortcut = "⌘T" },
        new() { Icon = "📁", Title = "Downloads Folder",   Subtitle = "~/Downloads",                         Category = "Folders", Shortcut = "⌘D" },
        new() { Icon = "⚙️", Title = "System Settings",    Subtitle = "Manage your system preferences",      Category = "System",  Shortcut = "⌘," },
        new() { Icon = "🌐", Title = "Open Browser",       Subtitle = "Launch default web browser",          Category = "Apps",    Shortcut = "⌘B" },
        new() { Icon = "📝", Title = "New Text File",      Subtitle = "Create a blank text document",        Category = "Actions", Shortcut = "⌘N" },
        new() { Icon = "🔒", Title = "Lock Screen",        Subtitle = "Lock your computer immediately",      Category = "System",  Shortcut = "⌘L" },
        new() { Icon = "📸", Title = "Screenshot",         Subtitle = "Capture the entire screen",           Category = "Actions", Shortcut = "⇧⌘3" },
        new() { Icon = "🔊", Title = "Toggle Volume",      Subtitle = "Mute or unmute system audio",         Category = "System",  Shortcut = "" },
        new() { Icon = "📦", Title = "App Store",          Subtitle = "Browse and install applications",     Category = "Apps",    Shortcut = "" },
        new() { Icon = "🗑️", Title = "Empty Trash",        Subtitle = "Permanently delete trashed items",    Category = "Actions", Shortcut = "" },
    ];

    public MainWindowViewModel()
    {
        FilterResults("");
    }

    partial void OnSearchTextChanged(string value) => FilterResults(value);

    private void FilterResults(string query)
    {
        Results.Clear();

        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(i =>
                i.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.Category.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var item in filtered)
            Results.Add(item);

        HasResults = Results.Count > 0;
        ShowNoResults = Results.Count == 0 && !string.IsNullOrWhiteSpace(query);
        SelectedResult = Results.FirstOrDefault();
    }
}
