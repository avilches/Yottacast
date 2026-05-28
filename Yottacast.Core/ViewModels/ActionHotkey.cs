// Yottacast.Core/ViewModels/ActionHotkey.cs
namespace Yottacast.Core.ViewModels;

public enum ActionModifiers { None = 0, Meta = 1, Shift = 2, MetaShift = 3 }

/// <summary>
/// Platform-agnostic hotkey descriptor. "Meta" resolves to Cmd (macOS) or Ctrl (Windows/Linux)
/// at the UI layer. Key names follow Avalonia's Key enum (e.g. "C", "F", "Return", "Tab").
/// </summary>
public sealed record ActionHotkey(string Key, ActionModifiers Modifiers = ActionModifiers.None) {
    public static readonly ActionHotkey Enter      = new("Return");
    public static readonly ActionHotkey MetaEnter  = new("Return", ActionModifiers.Meta);
    public static readonly ActionHotkey MetaC      = new("C", ActionModifiers.Meta);
    public static readonly ActionHotkey MetaShiftF = new("F", ActionModifiers.MetaShift);
    public static readonly ActionHotkey MetaE      = new("E", ActionModifiers.Meta);
    public static readonly ActionHotkey MetaP      = new("P", ActionModifiers.Meta);
    public static readonly ActionHotkey MetaS      = new("S", ActionModifiers.Meta);
}
