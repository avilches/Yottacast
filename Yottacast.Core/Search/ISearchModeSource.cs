namespace Yottacast.Core.Search;

/// <summary>
/// Optional interface for sources that support dedicated search modes.
/// Sources not implementing this are only active in SearchMode.All.
/// </summary>
public interface ISearchModeSource
{
    bool IsActiveIn(SearchMode mode);
}
