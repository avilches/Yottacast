using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yottacast.Services;

namespace Yottacast.ViewModels;

public partial class PermissionRowViewModel : ViewModelBase {
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Description))]
    [NotifyPropertyChangedFor(nameof(IsGranted))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private PermissionInfo _info;

    public PermissionRowViewModel(PermissionInfo info) {
        _info = info;
    }

    public string Title       => Info.Title;
    public string Description => Info.Description;
    public bool   IsGranted   => Info.Status == PermissionStatus.Granted;

    // Apple system semantic colors. Hardcoded on purpose: these communicate state
    // (ok / not ok), not theme styling, so they should look the same in light and dark.
    public IBrush StatusBrush =>
        Info.Status switch {
            PermissionStatus.Granted => new SolidColorBrush(Color.Parse("#34C759")),
            PermissionStatus.Denied  => new SolidColorBrush(Color.Parse("#FF3B30")),
            _                        => new SolidColorBrush(Color.Parse("#8E8E93")),
        };

    [RelayCommand]
    private void Request() {
        AppHandler.Instance.Permissions.Request(Info.Id);
    }
}
