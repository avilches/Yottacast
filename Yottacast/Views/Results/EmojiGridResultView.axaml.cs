using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Yottacast.Core.ViewModels;

namespace Yottacast.Views.Results;

public partial class EmojiGridResultView : UserControl {
    private ListBoxItem? _taggedItem;
    private TopLevel? _topLevel;

    public EmojiGridResultView() {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        // Tag the parent ListBoxItem so its selection background can be suppressed via style.
        var parent = this.GetVisualParent();
        while (parent != null) {
            if (parent is ListBoxItem lbi) {
                lbi.Classes.Add("emoji-item");
                _taggedItem = lbi;
                break;
            }
            parent = parent.GetVisualParent();
        }

        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel != null) {
            _topLevel.KeyDown += OnTopLevelKeyDown;
            _topLevel.KeyUp   += OnTopLevelKeyUp;
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
        if (_topLevel != null) {
            _topLevel.KeyDown -= OnTopLevelKeyDown;
            _topLevel.KeyUp   -= OnTopLevelKeyUp;
            _topLevel = null;
        }
        // Reset usage count visibility when leaving emoji mode.
        (DataContext as EmojiGridResultViewModel)?.SetShowUsageCount(false);

        // Remove the tag so reused ListBoxItems don't keep the emoji-specific style.
        if (_taggedItem != null) {
            _taggedItem.Classes.Remove("emoji-item");
            _taggedItem = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e) {
        if (e.Key is Key.LeftAlt or Key.RightAlt)
            (DataContext as EmojiGridResultViewModel)?.SetShowUsageCount(true);
    }

    private void OnTopLevelKeyUp(object? sender, KeyEventArgs e) {
        if (e.Key is Key.LeftAlt or Key.RightAlt)
            (DataContext as EmojiGridResultViewModel)?.SetShowUsageCount(false);
    }
}
