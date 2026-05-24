using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using AvaloniaEdit.Highlighting;
using Yottacast.Core.ViewModels;

namespace Yottacast.Views;

public partial class EditorPanelView : UserControl {
    private bool _settingContent;

    public EditorPanelView() {
        InitializeComponent();
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Editor.TextChanged += OnEditorTextChanged;
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is EditorPanelViewModel vm) {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _settingContent = true;
            Editor.Text = vm.Content;
            _settingContent = false;
            ApplySyntaxHighlighting(vm.FilePath);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is not EditorPanelViewModel vm) return;

        if (e.PropertyName == nameof(EditorPanelViewModel.Content) && !_settingContent) {
            _settingContent = true;
            Editor.Text = vm.Content;
            _settingContent = false;
        }

        if (e.PropertyName == nameof(EditorPanelViewModel.FilePath))
            ApplySyntaxHighlighting(vm.FilePath);
    }

    private void OnEditorTextChanged(object? sender, EventArgs e) {
        if (_settingContent) return;
        if (DataContext is EditorPanelViewModel vm) {
            _settingContent = true;
            vm.Content = Editor.Text;
            _settingContent = false;
        }
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e) {
        if (DataContext is EditorPanelViewModel vm)
            vm.UpdateStatus(Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column);
    }

    private void ApplySyntaxHighlighting(string filePath) {
        if (string.IsNullOrEmpty(filePath)) return;
        var ext = Path.GetExtension(filePath);
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
    }
}
