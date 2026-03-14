using System;

namespace Yottacast.ViewModels;

public class ResultItemViewModel
{
    public string Icon { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Category { get; init; } = "";
    public string Shortcut { get; init; } = "";
    public Action? OnActivate { get; init; }
}
