# Empty State Sources — Design Spec

## Context

When Yottacast opens with an empty search box, the only content shown today is
`_pendingAppInfos` — apps detected by FileSystemWatcher while the window was
hidden. This logic is embedded directly in `MainWindowViewModel`, making it hard
to extend or test.

Two goals:

1. **Clipboard-aware open**: if the clipboard holds a valid URL or local path
   when the window appears, show that result (UrlSearch or LocalPathSearch style)
   with `· from clipboard` in the subtitle — without touching the search box.
2. **Clean architecture**: extract all "what to show when text is empty" logic
   into discrete, DI-registered sources (`IEmptyStateSource`), removing ad-hoc
   state from the ViewModel.

---

## Interface: `IEmptyStateSource`

```csharp
public interface IEmptyStateSource
{
    void Start();
    Task WhenReady();
    Task Stop();

    /// Called once each time the window becomes visible (text is empty).
    /// clipboardText is the raw clipboard string read by the ViewModel.
    void OnWindowShown(string? clipboardText);

    /// Called when SearchText transitions from empty to non-empty.
    void OnSearchStarted();

    IReadOnlyList<BaseResultItemViewModel> GetResults();

    /// Fired by reactive sources when their result set changes.
    /// ViewModel re-calls GetResults() when this fires (only if text still empty).
    event Action? ResultsChanged;
}
```

Lifecycle mirrors existing search sources. `ResultsChanged` is optional —
sources that don't need reactivity leave it unsubscribed.

---

## `NewlyInstalledAppsSource`

Extracts the current `_pendingAppInfos` / `ShowPendingApps()` pattern out of
`MainWindowViewModel`.

**Behaviour:**
- `Start()`: subscribes to `ApplicationSearch.AppAdded`.
- On `AppAdded`: appends to internal list, fires `ResultsChanged`.
- `GetResults()`: returns result items for all pending apps (same items that
  `ShowPendingApps()` builds today).
- `OnSearchStarted()`: clears the internal list (equivalent to current
  `_pendingAppInfos.Clear()` in `OnSearchTextChanged`).
- `OnWindowShown(...)`: no-op.
- `WhenReady()`: completes immediately (no async init needed).

**Files:**
- New: `Yottacast.Core/Search/Application/NewlyInstalledAppsSource.cs`
- Depends on: `ApplicationSearch`, `ILogger`

---

## `ClipboardSearch`

New source that inspects the clipboard each time the window opens.

**Behaviour:**
- `OnWindowShown(string? clipboardText)`:
  - If null/empty → cache nothing.
  - Else try `UrlSearch.TryNormalizeUrl(clipboardText, out var url)`:
    - If valid → build a URL result item (same shape as `UrlSearch` produces)
      with subtitle `"Open in {browser} · from clipboard"`.
  - Else try `LocalPathSearch.IsLocalPath(clipboardText)` + existence check:
    - If valid → build a local-path result item with subtitle
      `"{expandedPath} · from clipboard"`.
  - Store single result (or null) in private field.
- `GetResults()`: returns the cached result list (0 or 1 item).
- `OnSearchStarted()`: clears cache (so stale clipboard doesn't re-appear next
  open after the user typed something in between).
- `ResultsChanged`: never fired (stateless per open).
- `WhenReady()`: completes immediately.

**Notes:**
- Does not trigger DNS validation — the result is shown instantly.
- For favicon: `ClipboardSearch` subscribes to `FaviconCache.FaviconLoaded`
  in `Start()` (same pattern as `UrlSearch`) and fires `ResultChanged` on the
  cached result item so the UI updates when the favicon arrives.
- `UrlSearch.TryNormalizeUrl` and `LocalPathSearch.IsLocalPath` must be
  `internal static` (or extracted to a shared utility) to be accessible from
  `ClipboardSearch`. If they are currently private, they need to be promoted
  as part of this work.

**Files:**
- New: `Yottacast.Core/Search/Clipboard/ClipboardSearch.cs`
- Depends on: `UrlSearch`, `LocalPathSearch`, `BrowserDiscovery`,
  `PlatformProvider`, `FileIconCache`, `ILogger`

---

## `ClipboardService` — read support

Add a read path symmetric to the existing write path:

```csharp
public void Initialize(Action<string> write, Func<Task<string?>> read) { ... }
public Task<string?> ReadTextAsync() => _read?.Invoke() ?? Task.FromResult<string?>(null);
```

`App.axaml.cs` initialises the reader alongside the writer:

```csharp
clipboardService.Initialize(
    write: text => Dispatcher.UIThread.InvokeAsync(() => clipboard.SetTextAsync(text)),
    read:  ()   => Dispatcher.UIThread.InvokeAsync(() => clipboard.GetTextAsync())
);
```

**Files:**
- Modified: `Yottacast.Core/Services/ClipboardService.cs`

---

## `MainWindowViewModel` — refactored empty state

**Remove:**
- `_pendingAppInfos`, `OnNewAppInstalled()`, `ShowPendingApps()`
- Direct dependency on `ApplicationSearch` for the new-app notification

**Add:**
- Inject `IEnumerable<IEmptyStateSource>` and `ClipboardService`
- In `Initialize()`: subscribe to each `source.ResultsChanged`
- Two methods for showing empty state:
  - `ShowEmptyStateAsync()` — called when window opens:
    1. `var text = await _clipboardService.ReadTextAsync()`
    2. `foreach source: source.OnWindowShown(text)`
    3. Collect `source.GetResults()` from all sources, flatten, show
  - `RefreshEmptyState()` — called when `source.ResultsChanged` fires (text still empty):
    1. Collect `source.GetResults()` from all sources, flatten, show
    2. Does NOT re-read clipboard or call `OnWindowShown` again
- In `OnSearchTextChanged` when value is non-empty:
  - `foreach source: source.OnSearchStarted()`

**Files:**
- Modified: `Yottacast.Core/ViewModels/MainWindowViewModel.cs`

---

## DI Registration (`App.axaml.cs`)

```csharp
services.AddSingleton<NewlyInstalledAppsSource>();
services.AddSingleton<ClipboardSearch>();
services.AddSingleton<IEmptyStateSource>(sp => sp.GetRequiredService<NewlyInstalledAppsSource>());
services.AddSingleton<IEmptyStateSource>(sp => sp.GetRequiredService<ClipboardSearch>());
```

Remove the existing `services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<ApplicationSearch>())` registration for the new-app notification (that responsibility moves to `NewlyInstalledAppsSource`). `ApplicationSearch` itself stays registered as before for normal search.

---

## Verification

1. **Clipboard URL**: copy `https://github.com` to clipboard, open Yottacast with
   empty search → result "github.com · from clipboard" appears. Search box empty.
   Press Enter → opens in browser.
2. **Clipboard path**: copy `/Users/you/Documents` → result shows folder name +
   `· from clipboard`. Press Enter → opens in Finder.
3. **Clipboard garbage**: copy `hello world` → no empty-state result shown.
4. **Newly installed app**: install an app while Yottacast is open with empty
   text → app appears in results (same as today, now via `NewlyInstalledAppsSource`).
5. **Type then clear**: type a query, clear it → clipboard result does NOT
   reappear (cache was cleared by `OnSearchStarted`).
6. **Reopen**: close and reopen Yottacast with same clipboard → result reappears.
7. Run `cd Yottacast.Core.Tests && dotnet test` — all tests pass.