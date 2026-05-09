using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public interface IEmptyStateSource
{
    /// <summary>Starts any background activity. Fire-and-forget; never await the return value.</summary>
    void Start();

    /// <summary>Returns a Task that completes once the source is ready to serve results.</summary>
    Task WhenReady();

    Task Stop();

    /// <summary>
    /// Called once each time the window becomes visible with empty search text.
    /// clipboardText is the raw clipboard string read by the View layer; may be null.
    /// </summary>
    void OnWindowShown(string? clipboardText);

    /// <summary>Called when SearchText transitions from empty to non-empty.</summary>
    void OnSearchStarted();

    IReadOnlyList<BaseResultItemViewModel> GetResults();

    /// <summary>
    /// Fired (on any thread) when the result set changes while the window is open.
    /// The ViewModel re-calls GetResults() when this fires, provided SearchText is still empty.
    /// </summary>
    event Action? ResultsChanged;
}
