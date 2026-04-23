using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Yottacast.Views.Results;

public partial class EmojiGridResultView : UserControl {
    private ListBoxItem? _taggedItem;

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
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
        // Remove the tag so reused ListBoxItems don't keep the emoji-specific style.
        if (_taggedItem != null) {
            _taggedItem.Classes.Remove("emoji-item");
            _taggedItem = null;
        }
        base.OnDetachedFromVisualTree(e);
    }
}
