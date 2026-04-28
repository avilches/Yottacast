# Keep Value When Hide — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a setting that controls whether the search text is preserved or cleared when the launcher hides, with optional timed auto-clear.

**Architecture:** Two new `UserSettings` fields drive the behavior. `MainWindowViewModel` owns a `CancellationTokenSource` timer that fires `CleanAndSaveHistory(null)` after the configured duration. `MainWindow` starts/cancels the timer from `OnPropertyChanged(IsVisible)` and `Deactivated`/`Activated` handlers.

**Tech Stack:** C#/.NET 9, Avalonia 11, CommunityToolkit.Mvvm, xUnit.

---

## File Map

| File | Change |
|---|---|
| `Yottacast.Core/AppDefaults.cs` | Add `KeepValueWhenHideDuration = 60` |
| `Yottacast.Core/Services/UserSettings.cs` | Add 2 fields to class, DTO record, `Load()`, `Save()` |
| `Yottacast.Core.Tests/Services/UserSettingsTests.cs` | Add 6 tests |
| `Yottacast/ViewModels/MainWindowViewModel.cs` | Add `_decayCts`, `StartDecayTimer()`, `CancelDecayTimer()` |
| `Yottacast/Views/MainWindow.axaml.cs` | Hook decay into `OnPropertyChanged`, `Activated`, `Deactivated` |
| `Yottacast/ViewModels/SettingsWindowViewModel.cs` | Add observable properties + `KeepValuePreset` record |
| `Yottacast/Views/SettingsWindow.axaml` | Add controls after StickyWindow block in General section |
| `docs/user-settings.md` | Document two new settings |
| `docs/ui-main-window.md` | Document decay timer behavior |

---

## Task 1 — AppDefaults + UserSettings fields (TDD)

**Files:**
- Modify: `Yottacast.Core/AppDefaults.cs`
- Modify: `Yottacast.Core/Services/UserSettings.cs`
- Modify: `Yottacast.Core.Tests/Services/UserSettingsTests.cs`

- [ ] **Step 1: Write failing tests**

Append to `Yottacast.Core.Tests/Services/UserSettingsTests.cs`, after the last test block and before the closing `}` of the class:

```csharp
    // ══════════════════════════════════════════════════════════════════════════
    // KeepValueWhenHide persistence
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void KeepValueWhenHide_DefaultIsTrue() {
        var settings = Load();
        Assert.True(settings.KeepValueWhenHide);
    }

    [Fact]
    public void KeepValueWhenHide_SaveAndLoad_RoundTrips() {
        var settings = Load();
        settings.KeepValueWhenHide = false;
        settings.Save();

        WaitForSettingsFile("keepValueWhenHide");
        var reloaded = Load();

        Assert.False(reloaded.KeepValueWhenHide);
    }

    [Fact]
    public void KeepValueWhenHide_MissingFromJson_DefaultsToTrue() {
        WriteSettingsJson("""
            {
                "browser": "",
                "terminal": ""
            }
            """);

        var settings = Load();

        Assert.True(settings.KeepValueWhenHide);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // KeepValueWhenHideDuration persistence
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void KeepValueWhenHideDuration_DefaultIs60() {
        var settings = Load();
        Assert.Equal(60, settings.KeepValueWhenHideDuration);
    }

    [Fact]
    public void KeepValueWhenHideDuration_SaveAndLoad_RoundTrips() {
        var settings = Load();
        settings.KeepValueWhenHideDuration = 300;
        settings.Save();

        WaitForSettingsFile("keepValueWhenHideDuration");
        var reloaded = Load();

        Assert.Equal(300, reloaded.KeepValueWhenHideDuration);
    }

    [Fact]
    public void KeepValueWhenHideDuration_MissingFromJson_DefaultsTo60() {
        WriteSettingsJson("""
            {
                "browser": "",
                "terminal": ""
            }
            """);

        var settings = Load();

        Assert.Equal(60, settings.KeepValueWhenHideDuration);
    }
```

- [ ] **Step 2: Run tests — expect failures**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "KeepValue" -v n 2>&1 | tail -20
```

Expected: 6 test failures (member not found / compile errors).

- [ ] **Step 3: Add constant to AppDefaults**

In `Yottacast.Core/AppDefaults.cs`, append after the `// ── History` block:

```csharp
    // ── Window behavior ────────────────────────────────────────────────────────
    /// Default duration in seconds before auto-clearing the search text after hide.
    /// 0 means "always keep" (never auto-clear).
    public const int KeepValueWhenHideDuration = 60;
```

- [ ] **Step 4: Add public properties to UserSettings class**

In `Yottacast.Core/Services/UserSettings.cs`, after the `public int HistoryMaxItems` line (around line 52), add:

```csharp
    public bool KeepValueWhenHide { get; set; } = true;
    public int KeepValueWhenHideDuration { get; set; } = AppDefaults.KeepValueWhenHideDuration;
```

- [ ] **Step 5: Add DTO fields to UserSettingsData**

In `UserSettingsData` record (around line 163), after the `historyMaxItems` line, add:

```csharp
        [JsonPropertyName("keepValueWhenHide")] public bool KeepValueWhenHide { get; init; } = true;
        [JsonPropertyName("keepValueWhenHideDuration")] public int KeepValueWhenHideDuration { get; init; } = AppDefaults.KeepValueWhenHideDuration;
```

- [ ] **Step 6: Wire into Load()**

In the `Load()` method, inside the `settings = new UserSettings(...) { ... }` initializer (after the `HistoryMaxItems = ...` line), add:

```csharp
                    KeepValueWhenHide = data.KeepValueWhenHide,
                    KeepValueWhenHideDuration = data.KeepValueWhenHideDuration >= 0
                        ? data.KeepValueWhenHideDuration
                        : AppDefaults.KeepValueWhenHideDuration,
```

- [ ] **Step 7: Wire into Save()**

In the `Save()` method, inside the `new UserSettingsData { ... }` initializer (after the `HistoryMaxItems = ...` line), add:

```csharp
                KeepValueWhenHide = KeepValueWhenHide,
                KeepValueWhenHideDuration = KeepValueWhenHideDuration,
```

- [ ] **Step 8: Run tests — expect all 6 pass**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "KeepValue" -v n 2>&1 | tail -20
```

Expected: 6 tests pass, 0 failures.

- [ ] **Step 9: Run full test suite**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
```

Expected: all existing tests still pass.

- [ ] **Step 10: Commit**

```bash
cd .. && git add Yottacast.Core/AppDefaults.cs Yottacast.Core/Services/UserSettings.cs Yottacast.Core.Tests/Services/UserSettingsTests.cs
git commit -m "feat: add KeepValueWhenHide and KeepValueWhenHideDuration settings"
```

---

## Task 2 — Timer logic in MainWindowViewModel

**Files:**
- Modify: `Yottacast/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add _decayCts field**

In `MainWindowViewModel.cs`, after the `private CancellationTokenSource? _deferredCts;` line (around line 59), add:

```csharp
    private CancellationTokenSource? _decayCts;
```

- [ ] **Step 2: Add StartDecayTimer and CancelDecayTimer methods**

Append these two methods anywhere after the `CancelDeferredSearch()` method (before the closing `}` of the class):

```csharp
    /// <summary>
    /// Starts (or resets) the decay timer. When it fires, clears the search text as if Escape was pressed.
    /// No-op if KeepValueWhenHide is false or duration is 0 (Always).
    /// </summary>
    public void StartDecayTimer() {
        _decayCts?.Cancel();
        _decayCts = null;

        if (!settings.KeepValueWhenHide || settings.KeepValueWhenHideDuration <= 0) return;

        var cts = new CancellationTokenSource();
        _decayCts = cts;
        var delay = TimeSpan.FromSeconds(settings.KeepValueWhenHideDuration);

        _ = Task.Run(async () => {
            try {
                await Task.Delay(delay, cts.Token);
                Dispatcher.UIThread.Post(() => CleanAndSaveHistory(null));
            } catch (OperationCanceledException) {
                // Timer cancelled — keep the value
            }
        });
    }

    /// <summary>
    /// Cancels any pending decay timer, preserving the current search text.
    /// </summary>
    public void CancelDecayTimer() {
        _decayCts?.Cancel();
        _decayCts = null;
    }
```

- [ ] **Step 3: Verify it compiles**

```bash
cd Yottacast && dotnet build 2>&1 | tail -10
```

Expected: build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Yottacast/ViewModels/MainWindowViewModel.cs
git commit -m "feat: add decay timer to MainWindowViewModel"
```

---

## Task 3 — MainWindow event hooks

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml.cs`

- [ ] **Step 1: Update OnPropertyChanged to handle decay**

In `MainWindow.axaml.cs`, find the `OnPropertyChanged` override (around line 48). Replace the entire `if/else` block inside the `if (change.Property == IsVisibleProperty)` check:

```csharp
        if (change.Property == IsVisibleProperty) {
            Log($"[Property] IsVisible → {change.NewValue}");
            var isVisible = change.GetNewValue<bool>();
            SearchBox.IsEnabled = isVisible;
            if (isVisible) {
                ApplyPositionOnShow();
                _positionDirty = false;
                _screenPosKnown = false;
                SearchBox.Focus();
                if (DataContext is MainWindowViewModel vm)
                    vm.CancelDecayTimer();
            } else {
                SavePosition();
                if (DataContext is MainWindowViewModel vm) {
                    vm.IsAltPressed = false;
                    if (!_settings.KeepValueWhenHide)
                        vm.CleanAndSaveHistory(null);
                    else
                        vm.StartDecayTimer();
                }
            }
        }
```

- [ ] **Step 2: Extend the Activated handler to also cancel the decay timer**

In the constructor, find `Activated += (_, _) => SearchBox.Focus();` and replace it with:

```csharp
        Activated += (_, _) => {
            SearchBox.Focus();
            if (DataContext is MainWindowViewModel vm)
                vm.CancelDecayTimer();
        };
```

- [ ] **Step 3: Add Deactivated handler for sticky mode**

In the constructor, after the `Activated` handler, add:

```csharp
        Deactivated += (_, _) => {
            // In sticky mode the window stays visible on deactivation — start the decay timer.
            // In non-sticky mode, deactivation triggers Hide() in App.axaml.cs, so
            // OnPropertyChanged(IsVisible=false) already handles it.
            if (_settings.StickyWindow
                && DataContext is MainWindowViewModel vm) {
                vm.StartDecayTimer();
            }
        };
```

- [ ] **Step 4: Build and verify**

```bash
cd Yottacast && dotnet build 2>&1 | tail -10
```

Expected: build succeeds, 0 errors.

- [ ] **Step 5: Manual smoke test**

Run `cd Yottacast && dotnet run`. Type something in the search box. Press the hotkey to hide. Wait 70 seconds (slightly more than the 60s default). Press hotkey to show. Verify the search box is empty.

Then repeat but show the window before 60 seconds pass — verify the text is still there.

- [ ] **Step 6: Commit**

```bash
git add Yottacast/Views/MainWindow.axaml.cs
git commit -m "feat: hook decay timer into MainWindow show/hide/activate events"
```

---

## Task 4 — SettingsWindowViewModel

**Files:**
- Modify: `Yottacast/ViewModels/SettingsWindowViewModel.cs`

- [ ] **Step 1: Add KeepValuePreset record**

At the bottom of `SettingsWindowViewModel.cs`, after the last class/record definition (e.g. after `DictionaryLanguageItem`), add:

```csharp
public sealed record KeepValuePreset(string Label, int? Seconds);
```

- [ ] **Step 2: Add observable properties**

In the `// ── General section` block (around line 66), after the `[ObservableProperty] private bool _stickyWindow;` line, add:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomDuration))]
    private KeepValuePreset? _selectedKeepValuePreset;

    [ObservableProperty] private bool _keepValueWhenHide;

    [ObservableProperty] private string _customDurationText = "";
    [ObservableProperty] private bool _isCustomDurationTextValid = true;

    public bool IsCustomDuration => SelectedKeepValuePreset?.Seconds == null;

    public IReadOnlyList<KeepValuePreset> KeepValuePresets { get; } = [
        new("15 seconds", 15),
        new("30 seconds", 30),
        new("1 minute", 60),
        new("5 minutes", 300),
        new("30 minutes", 1800),
        new("1 hour", 3600),
        new("Always", 0),
        new("Customize...", null),
    ];
```

- [ ] **Step 3: Add partial void handlers**

After `partial void OnStickyWindowChanged(bool value)` (around line 115), add:

```csharp
    partial void OnKeepValueWhenHideChanged(bool value) {
        _settings.KeepValueWhenHide = value;
        _settings.Save();
        _logger.LogInformation("Settings: KeepValueWhenHide = {Value}", value);
    }

    partial void OnSelectedKeepValuePresetChanged(KeepValuePreset? value) {
        if (value?.Seconds != null) {
            _settings.KeepValueWhenHideDuration = value.Seconds.Value;
            _settings.Save();
        }
        // When "Customize..." (Seconds == null) is selected, we wait for text input
    }

    partial void OnCustomDurationTextChanged(string value) {
        var parsed = ParseDuration(value);
        IsCustomDurationTextValid = parsed != null;
        if (parsed != null) {
            _settings.KeepValueWhenHideDuration = parsed.Value;
            _settings.Save();
        }
    }
```

- [ ] **Step 4: Add private helpers**

Anywhere in the private methods area (e.g. before the closing `}` of the class):

```csharp
    private static string FormatDuration(int seconds) {
        if (seconds > 0 && seconds % 86400 == 0) return $"{seconds / 86400}d";
        if (seconds > 0 && seconds % 3600 == 0) return $"{seconds / 3600}h";
        if (seconds > 0 && seconds % 60 == 0) return $"{seconds / 60}m";
        return $"{seconds}s";
    }

    private static int? ParseDuration(string? text) {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().ToLowerInvariant();
        if (text.Length < 2) return null;
        var unit = text[^1];
        if (!int.TryParse(text[..^1], out var n) || n <= 0) return null;
        return unit switch {
            's' => n,
            'm' => n * 60,
            'h' => n * 3600,
            'd' => n * 86400,
            _ => null
        };
    }
```

- [ ] **Step 5: Initialize from settings in constructor**

In the constructor (around line 228–235), after `_stickyWindow = settings.StickyWindow;`, add:

```csharp
        _keepValueWhenHide = settings.KeepValueWhenHide;
        var dur = settings.KeepValueWhenHideDuration;
        var matchedPreset = KeepValuePresets.FirstOrDefault(p => p.Seconds == dur);
        if (matchedPreset != null) {
            _selectedKeepValuePreset = matchedPreset;
        } else {
            _selectedKeepValuePreset = KeepValuePresets.Last(); // "Customize..."
            _customDurationText = FormatDuration(dur);
        }
```

- [ ] **Step 6: Build and verify**

```bash
cd Yottacast && dotnet build 2>&1 | tail -10
```

Expected: build succeeds, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add Yottacast/ViewModels/SettingsWindowViewModel.cs
git commit -m "feat: add KeepValueWhenHide settings bindings to SettingsWindowViewModel"
```

---

## Task 5 — SettingsWindow UI

**Files:**
- Modify: `Yottacast/Views/SettingsWindow.axaml`

- [ ] **Step 1: Add the UI block after StickyWindow**

Find the StickyWindow block in `SettingsWindow.axaml` (around line 578–584):

```xml
                    <!-- Sticky window -->
                    <StackPanel Spacing="4">
                        <CheckBox Content="Sticky window"
                                  IsChecked="{Binding StickyWindow}"/>
                        <TextBlock Classes="description"
                                   Text="When enabled, the window stays visible when it loses focus."/>
                    </StackPanel>
                </StackPanel>
```

Replace it with (adds the new block before the closing `</StackPanel>`):

```xml
                    <!-- Sticky window -->
                    <StackPanel Spacing="4">
                        <CheckBox Content="Sticky window"
                                  IsChecked="{Binding StickyWindow}"/>
                        <TextBlock Classes="description"
                                   Text="When enabled, the window stays visible when it loses focus."/>
                    </StackPanel>

                    <!-- Keep value when hide -->
                    <StackPanel Spacing="4">
                        <CheckBox Content="Keep value when hide"
                                  IsChecked="{Binding KeepValueWhenHide}"/>
                        <TextBlock Classes="description"
                                   Text="Preserve the search text when the window hides. Auto-clears after the chosen duration."/>
                        <StackPanel Orientation="Horizontal" Spacing="8"
                                    IsVisible="{Binding KeepValueWhenHide}"
                                    Margin="0,4,0,0">
                            <TextBlock VerticalAlignment="Center" Text="Keep value for"/>
                            <ComboBox ItemsSource="{Binding KeepValuePresets}"
                                      SelectedItem="{Binding SelectedKeepValuePreset}">
                                <ComboBox.ItemTemplate>
                                    <DataTemplate x:DataType="vm:KeepValuePreset">
                                        <TextBlock Text="{Binding Label}"/>
                                    </DataTemplate>
                                </ComboBox.ItemTemplate>
                            </ComboBox>
                            <Border BorderThickness="1"
                                    CornerRadius="4"
                                    IsVisible="{Binding IsCustomDuration}"
                                    Classes.invalid="{Binding !IsCustomDurationTextValid}">
                                <Border.Styles>
                                    <Style Selector="Border.invalid">
                                        <Setter Property="BorderBrush" Value="Red"/>
                                    </Style>
                                </Border.Styles>
                                <TextBox BorderThickness="0"
                                         Background="Transparent"
                                         Text="{Binding CustomDurationText}"
                                         Width="80"
                                         Watermark="e.g. 2m"/>
                            </Border>
                        </StackPanel>
                    </StackPanel>
                </StackPanel>
```

- [ ] **Step 2: Verify the vm: namespace is already declared**

Check that the top of `SettingsWindow.axaml` has `xmlns:vm="using:Yottacast.ViewModels"`. It should already be there. If not, add it to the `<Window>` attributes.

- [ ] **Step 3: Build**

```bash
cd Yottacast && dotnet build 2>&1 | tail -10
```

Expected: build succeeds, 0 errors. If you see a compiled bindings error about `KeepValuePreset`, ensure the record is `public` and in the `Yottacast.ViewModels` namespace.

- [ ] **Step 4: Manual UI test**

Run `cd Yottacast && dotnet run`. Open Settings (Cmd+,). Go to General. Verify:
- "Keep value when hide" checkbox appears after "Sticky window".
- Checking it shows the "Keep value for" row with the dropdown.
- Default selection is "1 minute".
- Selecting "Customize..." shows the text input field.
- Typing an invalid value (e.g. `abc`) turns the border red.
- Typing a valid value (e.g. `90s`) turns it back to normal.
- Unchecking the checkbox hides the duration row.
- Settings persist across close/reopen of Settings.

- [ ] **Step 5: Commit**

```bash
git add Yottacast/Views/SettingsWindow.axaml
git commit -m "feat: add Keep value when hide controls to General settings"
```

---

## Task 6 — Update docs

**Files:**
- Modify: `docs/user-settings.md`
- Modify: `docs/ui-main-window.md`

- [ ] **Step 1: Update user-settings.md — settings table**

In section 2 ("Preferencias disponibles"), add two rows to the table after the `StickyWindow` row:

```markdown
| KeepValueWhenHide | `true` | Si el texto se preserva al ocultar la ventana; `false` lo limpia inmediatamente |
| KeepValueWhenHideDuration | `60` | Segundos antes de borrar el texto tras ocultar; `0` = nunca (Siempre) |
```

- [ ] **Step 2: Update user-settings.md — section 12 (no-refresh list)**

In section 12 ("Settings que NO disparan refresco"), add `KeepValueWhenHide`, `KeepValueWhenHideDuration` to the list.

- [ ] **Step 3: Update ui-main-window.md — add decay timer section**

Append a new section after section 14 ("Posicionamiento y arrastre"):

```markdown
## 15. Preservación del texto al ocultar (decay timer)

El comportamiento del campo de búsqueda al ocultar la ventana depende del setting `KeepValueWhenHide`:

| Setting | Comportamiento al ocultar |
|---|---|
| `KeepValueWhenHide = false` | El texto se limpia inmediatamente (`CleanAndSaveHistory(null)`), igual que pulsar Escape |
| `KeepValueWhenHide = true`, duración > 0 | Se inicia un timer; si la ventana reaparece antes de que expire, el texto se conserva; si expira, se limpia |
| `KeepValueWhenHide = true`, duración = 0 (Siempre) | No se inicia timer; el texto se conserva indefinidamente (comportamiento histórico) |

En modo sticky, el timer también se inicia al perder el foco (aunque la ventana siga visible), y se cancela al recuperarlo.

El timer vive en `MainWindowViewModel` como un `CancellationTokenSource` (`_decayCts`). `MainWindow` lo arranca y cancela desde los eventos `IsVisible`, `Deactivated` y `Activated`.

> **Verificar en:** `MainWindowViewModel.StartDecayTimer()`, `MainWindowViewModel.CancelDecayTimer()` — `Yottacast/ViewModels/MainWindowViewModel.cs`. Hooks en `MainWindow.OnPropertyChanged`, `Activated`, `Deactivated` — `Yottacast/Views/MainWindow.axaml.cs`.
```

- [ ] **Step 4: Run full test suite one last time**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
```

Expected: all tests pass.

- [ ] **Step 5: Commit docs**

```bash
cd .. && git add docs/user-settings.md docs/ui-main-window.md
git commit -m "docs: document keep-value-when-hide behavior and settings"
```
