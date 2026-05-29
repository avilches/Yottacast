using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yottacast.Core.Services;

namespace Yottacast.Core.ViewModels;

public enum EditorMode { Preview, Edit }

public partial class EditorPanelViewModel(FileEditorService fileEditorService) : ObservableObject {
    private string _originalContent = "";

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private bool _showUnsavedDialog;
    [ObservableProperty] private string _statusText = "Ln 1, Col 1";
    [ObservableProperty] private EditorMode _mode = EditorMode.Preview;

    public bool IsDirty => Content != _originalContent;
    public string TitleText => IsDirty ? $"* {FilePath}" : FilePath;
    public bool IsAutoSave { get; private set; }
    public bool ShowSaveButton => !IsAutoSave && Mode == EditorMode.Edit;
    public bool IsPreviewMode => Mode == EditorMode.Preview;
    public bool IsEditMode => Mode == EditorMode.Edit;

    private static readonly HashSet<string> MarkdownExtensions = [".md", ".markdown"];
    public bool IsMarkdownFile =>
        MarkdownExtensions.Contains(Path.GetExtension(FilePath).ToLowerInvariant());
    public bool IsMarkdownPreview => IsPreviewMode && IsMarkdownFile;

    public Action? CloseRequested { get; set; }

    partial void OnContentChanged(string value) {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(TitleText));
    }

    partial void OnFilePathChanged(string value) {
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(IsMarkdownFile));
        OnPropertyChanged(nameof(IsMarkdownPreview));
    }

    partial void OnModeChanged(EditorMode value) {
        OnPropertyChanged(nameof(ShowSaveButton));
        OnPropertyChanged(nameof(IsPreviewMode));
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsMarkdownPreview));
    }

    public void LoadPreview(string path) {
        FilePath = path;
        FileName = Path.GetFileName(path);
        Mode = EditorMode.Preview;
        ShowUnsavedDialog = false;
        var text = fileEditorService.ReadFile(path);
        _originalContent = text;
        Content = text;
    }

    public void LoadEdit(string path, bool autoSave) {
        FilePath = path;
        FileName = Path.GetFileName(path);
        IsAutoSave = autoSave;
        Mode = EditorMode.Edit;
        var content = fileEditorService.ReadFile(path);
        _originalContent = content;
        Content = content;
        ShowUnsavedDialog = false;
        OnPropertyChanged(nameof(ShowSaveButton));
    }

    public void SwitchToEdit(bool autoSave) {
        IsAutoSave = autoSave;
        Mode = EditorMode.Edit;
        // _originalContent remains as set during LoadPreview — dirty tracking from disk content
    }

    [RelayCommand]
    public void SaveFile() {
        fileEditorService.WriteFile(FilePath, Content);
        _originalContent = Content;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(TitleText));
    }

    [RelayCommand]
    public void SaveAndClose() {
        SaveFile();
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void DiscardAndClose() {
        Content = _originalContent;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void CancelUnsavedDialog() => ShowUnsavedDialog = false;

    public void RequestClose() {
        if (Mode == EditorMode.Preview) {
            CloseRequested?.Invoke();
            return;
        }
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
