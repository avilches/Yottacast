using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yottacast.Core.Services;

namespace Yottacast.Core.ViewModels;

public partial class EditorPanelViewModel(FileEditorService fileEditorService) : ObservableObject {
    private string _originalContent = "";

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private bool _showUnsavedDialog;
    [ObservableProperty] private string _statusText = "Ln 1, Col 1";

    public bool IsDirty => Content != _originalContent;
    public bool IsAutoSave { get; private set; }
    public bool ShowSaveButton => !IsAutoSave;

    public Action? CloseRequested { get; init; }

    // Notify IsDirty when Content changes (Content uses [ObservableProperty] generated setter)
    partial void OnContentChanged(string value) => OnPropertyChanged(nameof(IsDirty));

    public void Load(string path, bool autoSave) {
        FilePath = path;
        FileName = Path.GetFileName(path);
        IsAutoSave = autoSave;
        var content = fileEditorService.ReadFile(path);
        _originalContent = content;
        Content = content;
        ShowUnsavedDialog = false;
        OnPropertyChanged(nameof(ShowSaveButton));
    }

    [RelayCommand]
    public void SaveFile() {
        fileEditorService.WriteFile(FilePath, Content);
        _originalContent = Content;
        OnPropertyChanged(nameof(IsDirty));
    }

    [RelayCommand]
    public void SaveAndClose() {
        SaveFile();
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void DiscardAndClose() {
        Content = _originalContent;  // Restore to disk content so IsDirty becomes false
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void CancelUnsavedDialog() => ShowUnsavedDialog = false;

    public void RequestClose() {
        if (IsAutoSave) {
            if (IsDirty) SaveFile();
            CloseRequested?.Invoke();
        } else if (!IsDirty) {
            CloseRequested?.Invoke();
        } else {
            ShowUnsavedDialog = true;
        }
    }

    public void UpdateStatus(int line, int col) => StatusText = $"Ln {line}, Col {col}";
}
