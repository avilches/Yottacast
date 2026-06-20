using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;

namespace Yottacast.Controls;

/// <summary>
/// A TextBlock that highlights character ranges in its text using the active theme's
/// match-highlight colors: a text color (foreground) and a background fill.
/// When Ranges is null or empty, renders as a plain TextBlock.
/// </summary>
public class HighlightTextBlock : TextBlock {

    public static readonly StyledProperty<IReadOnlyList<(int Start, int Length)>?> RangesProperty =
        AvaloniaProperty.Register<HighlightTextBlock, IReadOnlyList<(int Start, int Length)>?>(
            nameof(Ranges));

    public IReadOnlyList<(int Start, int Length)>? Ranges {
        get => GetValue(RangesProperty);
        set => SetValue(RangesProperty, value);
    }

    private bool _pendingThemeRebuild;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        ResourcesChanged += OnThemeResourcesChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnDetachedFromVisualTree(e);
        ResourcesChanged -= OnThemeResourcesChanged;
    }

    private void OnThemeResourcesChanged(object? sender, ResourcesChangedEventArgs e) {
        if (_pendingThemeRebuild || string.IsNullOrEmpty(Text) || Ranges == null || Ranges.Count == 0) return;
        _pendingThemeRebuild = true;
        Dispatcher.UIThread.Post(() => {
            _pendingThemeRebuild = false;
            RebuildInlines();
        }, DispatcherPriority.Background);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == RangesProperty)
            RebuildInlines();
    }

    private void RebuildInlines() {
        var text   = Text ?? "";
        var ranges = Ranges;

        if (string.IsNullOrEmpty(text) || ranges == null || ranges.Count == 0) {
            Inlines?.Clear();
            return;
        }

        var res    = Application.Current?.Resources;
        var fgBrush = res?["Theme.Results.MatchHighlight.Color"]      as IBrush;
        var bgBrush = res?["Theme.Results.MatchHighlight.Background"] as IBrush;

        // Build a boolean coverage map
        var covered = new bool[text.Length];
        foreach (var (start, len) in ranges) {
            var end = Math.Min(start + len, text.Length);
            for (var i = Math.Max(0, start); i < end; i++)
                covered[i] = true;
        }

        var inlines = new InlineCollection();
        var pos = 0;
        while (pos < text.Length) {
            var isHl = covered[pos];
            var end  = pos;
            while (end < text.Length && covered[end] == isHl) end++;

            var run = new Run { Text = text[pos..end] };

            if (isHl) {
                if (fgBrush != null) run.Foreground = fgBrush;
                if (bgBrush != null) run.Background = bgBrush;
            }

            inlines.Add(run);
            pos = end;
        }

        Inlines = inlines;
    }
}
