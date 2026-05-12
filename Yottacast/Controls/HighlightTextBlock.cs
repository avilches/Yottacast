using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Yottacast.Controls;

/// <summary>
/// A TextBlock that highlights character ranges in its text using the active theme's
/// match-highlight style (foreground color, background chip, or bold+underline).
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

        var res     = Application.Current?.Resources;
        var style   = res?["Theme.Results.MatchHighlight.Style"]             as string ?? "foreground";
        var hlBrush = res?["Theme.Results.MatchHighlight.Color"]             as IBrush;
        var bgOp    = res?["Theme.Results.MatchHighlight.BackgroundOpacity"] is double d ? d : 0.22;

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

            if (isHl && hlBrush != null) {
                switch (style) {
                    case "background":
                        if (hlBrush is SolidColorBrush scb)
                            run.Background = new SolidColorBrush(scb.Color, bgOp);
                        run.Foreground = hlBrush;
                        break;
                    case "underline":
                        run.FontWeight = FontWeight.Bold;
                        run.TextDecorations = new TextDecorationCollection {
                            new() {
                                Location        = TextDecorationLocation.Underline,
                                Stroke          = hlBrush,
                                StrokeThickness = 1.5
                            }
                        };
                        break;
                    default: // "foreground"
                        run.Foreground = hlBrush;
                        run.FontWeight = FontWeight.SemiBold;
                        break;
                }
            }

            inlines.Add(run);
            pos = end;
        }

        Inlines = inlines;
    }
}
