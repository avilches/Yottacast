using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Highlighting;
using Yottacast.Core.ViewModels;

namespace Yottacast.Views;

public partial class EditorPanelView : UserControl {
    private bool _settingContent;
    private EditorPanelViewModel? _currentVm;
    private int _dialogFocusIndex = 0; // 0=SaveDialogButton, 1=DiscardDialogButton, 2=CancelDialogButton

    public EditorPanelView() {
        InitializeComponent();
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Editor.TextChanged += OnEditorTextChanged;
        DialogOverlay.AddHandler(KeyDownEvent, OnDialogTunnelKeyDown, RoutingStrategies.Tunnel);
    }

    public void FocusEditor() => Editor.TextArea.Focus();

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);

        if (_currentVm != null)
            _currentVm.PropertyChanged -= OnViewModelPropertyChanged;

        _currentVm = DataContext as EditorPanelViewModel;

        if (_currentVm != null) {
            _currentVm.PropertyChanged += OnViewModelPropertyChanged;
            _settingContent = true;
            Editor.Text = _currentVm.Content;
            _settingContent = false;
            ApplySyntaxHighlighting(_currentVm.FilePath);
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

        if (e.PropertyName == nameof(EditorPanelViewModel.ShowUnsavedDialog)) {
            if (vm.ShowUnsavedDialog) {
                _dialogFocusIndex = 0; // Save tiene el foco inicial
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => SaveDialogButton.Focus(),
                    Avalonia.Threading.DispatcherPriority.Background);
            } else if (vm.IsEditMode) {
                // Al cerrar el dialog por cancelación, devolver foco al editor
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => Editor.TextArea.Focus(),
                    Avalonia.Threading.DispatcherPriority.Background);
            }
        }
    }

    // Trampa de foco: Tab y cursores rotan entre los botones del dialog sin salirse
    private void OnDialogTunnelKeyDown(object? sender, KeyEventArgs e) {
        if (_currentVm?.ShowUnsavedDialog != true) return;

        bool isForward = (e.Key == Key.Tab && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                      || e.Key is Key.Right or Key.Down;
        bool isBack = (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                   || e.Key is Key.Left or Key.Up;

        if (!isForward && !isBack) return;

        Control[] buttons = [SaveDialogButton, DiscardDialogButton, CancelDialogButton];
        _dialogFocusIndex = isForward
            ? (_dialogFocusIndex + 1) % buttons.Length
            : (_dialogFocusIndex - 1 + buttons.Length) % buttons.Length;

        buttons[_dialogFocusIndex].Focus();
        e.Handled = true;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e) {
        if (_settingContent) return;
        if (DataContext is EditorPanelViewModel vm) {
            if (vm.IsPreviewMode) return;
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
