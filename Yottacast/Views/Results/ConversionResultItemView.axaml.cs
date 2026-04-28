using Avalonia.Controls;
using Avalonia.VisualTree;
using Yottacast.Core.ViewModels;

namespace Yottacast.Views.Results;

public partial class ConversionResultItemView : UserControl {
    private ListBoxItem? _taggedItem;

    public ConversionResultItemView() {
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
