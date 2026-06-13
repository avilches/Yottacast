using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Yottacast.ViewModels;

namespace Yottacast.Views.Settings;

public partial class SettingsFileSearchView : UserControl {
    public SettingsFileSearchView() {
        InitializeComponent();
    }

    private async Task<string?> PickFolderAsync() {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var results = await top.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select Folder", AllowMultiple = false });
        return results.Count > 0 ? results[0].TryGetLocalPath() : null;
    }

    private async void OnAddSearchFolderClicked(object? sender, RoutedEventArgs e) {
        var path = await PickFolderAsync();
        if (path != null && DataContext is SettingsWindowViewModel vm)
            vm.AddSearchFolder(path);
    }

    private void OnRemoveSearchFolderClicked(object? sender, RoutedEventArgs e) {
        if (sender is Button { Tag: SearchFolderItem item } && DataContext is SettingsWindowViewModel vm)
            vm.RemoveSearchFolder(item);
    }

    private void OnAddCommonFoldersClicked(object? sender, RoutedEventArgs e) {
        if (DataContext is SettingsWindowViewModel vm)
            vm.AddCommonFolders();
    }
}
