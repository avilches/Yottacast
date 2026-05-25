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
    [ObservableProperty] private bool _isTextFile;
    [ObservableProperty] private string _fileSize = "";
    [ObservableProperty] private string _fileCreated = "";
    [ObservableProperty] private string _fileModified = "";
    [ObservableProperty] private string _fileKind = "";

    public bool IsDirty => Content != _originalContent;
    public bool IsAutoSave { get; private set; }
    public bool ShowSaveButton => !IsAutoSave && Mode == EditorMode.Edit;
    public bool IsPreviewMode => Mode == EditorMode.Preview;
    public bool IsEditMode => Mode == EditorMode.Edit;
    public bool IsMetadataOnly => !IsTextFile;

    public Action? CloseRequested { get; set; }

    partial void OnContentChanged(string value) => OnPropertyChanged(nameof(IsDirty));

    partial void OnModeChanged(EditorMode value) {
        OnPropertyChanged(nameof(ShowSaveButton));
        OnPropertyChanged(nameof(IsPreviewMode));
        OnPropertyChanged(nameof(IsEditMode));
    }

    partial void OnIsTextFileChanged(bool value) => OnPropertyChanged(nameof(IsMetadataOnly));

    public void LoadPreview(string path) {
        FilePath = path;
        FileName = Path.GetFileName(path);
        Mode = EditorMode.Preview;
        ShowUnsavedDialog = false;

        try {
            var info = new FileInfo(path);
            var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            FileKind = string.IsNullOrEmpty(ext) ? "File" : $"{ext} File";
            FileSize = info.Exists ? FormatSize(info.Length) : "";
            FileCreated = info.Exists ? info.CreationTime.ToString("d MMM yyyy, HH:mm") : "";
            FileModified = info.Exists ? info.LastWriteTime.ToString("d MMM yyyy, HH:mm") : "";

            IsTextFile = fileEditorService.IsTextContent(path);
            if (IsTextFile) {
                var text = fileEditorService.ReadFile(path);
                _originalContent = text;
                Content = text;
            } else {
                _originalContent = "";
                Content = "";
            }
        } catch {
            IsTextFile = false;
            _originalContent = "";
            Content = "";
        }
    }

    public void LoadEdit(string path, bool autoSave) {
        FilePath = path;
        FileName = Path.GetFileName(path);
        IsAutoSave = autoSave;
        Mode = EditorMode.Edit;
        IsTextFile = true;
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

    private static string FormatSize(long bytes) {
        if (bytes < 1_000) return $"{bytes} bytes";
        if (bytes < 1_000_000) return $"{bytes / 1_000.0:F1} KB";
        if (bytes < 1_000_000_000) return $"{bytes / 1_000_000.0:F1} MB";
        return $"{bytes / 1_000_000_000.0:F1} GB";
    }
}
