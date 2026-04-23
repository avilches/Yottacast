using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Yottacast.Views.Results;

public partial class EmojiGridResultView : UserControl {
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
                break;
            }
            parent = parent.GetVisualParent();
        }
    }
}
