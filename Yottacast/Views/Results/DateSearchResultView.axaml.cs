using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Yottacast.Views.Results;

public partial class DateSearchResultView : UserControl {
    private ListBoxItem? _taggedItem;

    public DateSearchResultView() {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        var parent = this.GetVisualParent();
        while (parent != null) {
            if (parent is ListBoxItem lbi) {
                lbi.Classes.Add("conv-navigable");
                _taggedItem = lbi;
                break;
            }
            parent = parent.GetVisualParent();
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
        if (_taggedItem != null) {
            _taggedItem.Classes.Remove("conv-navigable");
            _taggedItem = null;
        }
        base.OnDetachedFromVisualTree(e);
    }
}
