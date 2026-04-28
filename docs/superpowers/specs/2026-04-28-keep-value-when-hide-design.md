# Keep Value When Hide — Design Spec

**Date:** 2026-04-28

## Summary

Add a user setting that controls whether the search text is preserved or cleared when the launcher window is hidden. When enabled, a configurable timer allows the value to persist for a set duration before being auto-cleared. When disabled, the text is cleared immediately on hide (saved to history, same as pressing Escape).

---

## 1. Motivation

Currently, the search text is preserved indefinitely when the window hides (via hotkey, Cmd+W, or deactivation in non-sticky mode). This is useful when the user hides the launcher temporarily and wants to resume the same query. However, some users may prefer a clean slate every time, or may want the value to expire after a short period of inactivity.

---

## 2. New Settings

Two new fields in `UserSettings` / `UserSettingsData`:

| Field | Type | Default | Description |
|---|---|---|---|
| `KeepValueWhenHide` | `bool` | `true` | Whether text is preserved on hide |
| `KeepValueWhenHideDuration` | `int` | `60` | Seconds before auto-clear; `0` = never (Always) |

`AppDefaults.KeepValueWhenHideDuration = 60` (1 minute).

JSON keys: `keepValueWhenHide`, `keepValueWhenHideDuration`.

These settings do NOT trigger `SearchSettingsChanged` (they don't affect which results appear).

---

## 3. Behavior

### 3.1 KeepValueWhenHide = false

When the window is hidden (any trigger: hotkey, Cmd+W, deactivation in non-sticky mode):
- If `SearchText` is non-empty: call `CleanAndSaveHistory(null)` immediately (same as pressing Escape with text).
- In sticky mode: deactivation does NOT trigger clearing (the window stays visible and the user may reactivate it).

### 3.2 KeepValueWhenHide = true, Duration > 0

When the window is hidden **or** (in sticky mode) the window loses focus:
1. Cancel any pending decay timer.
2. Start a new async timer for `KeepValueWhenHideDuration` seconds.
3. If the window becomes visible again **or** (in sticky mode) regains focus before the timer fires → cancel the timer. Text is preserved.
4. If the timer fires → call `CleanAndSaveHistory(null)` (same as Escape).

### 3.3 KeepValueWhenHide = true, Duration = 0 (Always)

No timer is started. Text is preserved indefinitely (equivalent to current behavior).

---

## 4. Timer Logic — MainWindowViewModel

The timer lives in `MainWindowViewModel` (already owns `SearchText` and `CleanAndSaveHistory`).

New members:
- `CancellationTokenSource? _decayCts` — current pending timer; `null` if no timer running.
- `StartDecayTimer()` — cancels any existing `_decayCts`, creates a new one, launches a `Task.Delay` that calls `CleanAndSaveHistory(null)` on the UI thread when it fires. Only starts if `KeepValueWhenHide = true` and `KeepValueWhenHideDuration > 0`.
- `CancelDecayTimer()` — cancels `_decayCts` and sets it to `null`.

`MainWindow` calls these methods at:

| Event | When | Action |
|---|---|---|
| `IsVisible = false` | Window hides (any method) | `StartDecayTimer()` if ON; `CleanAndSaveHistory(null)` if OFF |
| `IsVisible = true` | Window shows | `CancelDecayTimer()` |
| `Activated` | Window gains focus (sticky mode) | `CancelDecayTimer()` |
| `Deactivated` | Window loses focus (sticky mode only) | `StartDecayTimer()` |

The `Deactivated` path for sticky mode: in non-sticky mode, `Deactivated` triggers `Hide()` in `App.axaml.cs`, which then triggers `IsVisible = false`. So the timer is only started once (via `IsVisible = false`), not twice.

In sticky mode, `Deactivated` does NOT hide the window, so the timer must be started explicitly from the `Deactivated` handler — but only when `StickyWindow = true`.

---

## 5. Settings UI (General Section)

Placed after the StickyWindow toggle in the General section of Settings.

**Layout (checkbox ON):**
```
[✓] Keep value when hide
    Keep value for:  [1 minute ▾]
```

**Layout (Customize... selected):**
```
[✓] Keep value when hide
    Keep value for:  [Customize... ▾]  [_90s_______]
```

The duration dropdown is disabled when the checkbox is OFF.

### Dropdown Presets

| Label | Seconds |
|---|---|
| 15 seconds | 15 |
| 30 seconds | 30 |
| 1 minute | 60 |
| 5 minutes | 300 |
| 30 minutes | 1800 |
| 1 hour | 3600 |
| Always | 0 |
| Customize... | — |

### Custom Duration Field

Shown only when "Customize..." is selected. Accepts `{number}{unit}` where unit ∈ {`s`, `m`, `h`, `d`}, case-insensitive. Examples: `90s`, `2m`, `1h`, `1d`.

- Invalid input: field shown in red, value not saved.
- Valid input: saved immediately.
- On load: if `KeepValueWhenHideDuration` matches a preset → select that preset. Otherwise → select "Customize..." and show value formatted with the most readable unit (prefer `m` > `s` > `h` > `d`, choosing the unit that produces a whole number).

### ViewModel

`SettingsWindowViewModel` gets:
- `KeepValueWhenHide: bool` — bound to checkbox.
- `KeepValuePresets: List<KeepValuePreset>` — static list of presets (record with `Label` and `int? Seconds`; `null` = Customize).
- `SelectedKeepValuePreset: KeepValuePreset` — bound to ComboBox.
- `IsCustomDuration: bool` — computed: `SelectedKeepValuePreset.Seconds == null`. Controls text field visibility.
- `CustomDurationText: string` — bound to text field. Validated on change; saves when valid.

---

## 6. Tests

New tests in `Yottacast.Core.Tests/Services/UserSettingsTests.cs`:
- `KeepValueWhenHide_DefaultIsTrue`
- `KeepValueWhenHideDuration_DefaultIs60`
- `KeepValueWhenHide_SaveAndLoad_RoundTrips`
- `KeepValueWhenHideDuration_SaveAndLoad_RoundTrips`
- `KeepValueWhenHide_MissingFromJson_DefaultsToTrue`
- `KeepValueWhenHideDuration_MissingFromJson_DefaultsTo60`

Timer behavior tests are integration-level (require UI thread); document them as manual verification points rather than automated tests.

---

## 7. Files Affected

| File | Change |
|---|---|
| `Yottacast.Core/AppDefaults.cs` | Add `KeepValueWhenHideDuration = 60` |
| `Yottacast.Core/Services/UserSettings.cs` | Add two fields to `UserSettings` and `UserSettingsData`, wire in `Load()` and `Save()` |
| `Yottacast/ViewModels/MainWindowViewModel.cs` | Add `_decayCts`, `StartDecayTimer()`, `CancelDecayTimer()` |
| `Yottacast/Views/MainWindow.axaml.cs` | Call decay methods from `OnPropertyChanged(IsVisible)`, `Activated`, `Deactivated` |
| `Yottacast/ViewModels/SettingsWindowViewModel.cs` | Add `KeepValueWhenHide`, `KeepValuePresets`, `SelectedKeepValuePreset`, `IsCustomDuration`, `CustomDurationText` |
| `Yottacast/Views/SettingsWindow.axaml` | Add controls in General section |
| `Yottacast.Core.Tests/Services/UserSettingsTests.cs` | Add 6 new tests |
| `docs/user-settings.md` | Document new settings |
| `docs/ui-main-window.md` | Document decay timer behavior |

> **Verify in:** `UserSettings.cs` (fields), `MainWindowViewModel.cs` (timer), `MainWindow.axaml.cs` (event hooks), `SettingsWindowViewModel.cs` (UI binding), `SettingsWindow.axaml` (General section).
