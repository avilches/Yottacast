namespace Yottacast.Core.Search.SystemSettings;

public sealed record SystemSettingsPanel(
    string Name,
    string UrlIdentifier,
    bool IsBuiltin = true);
