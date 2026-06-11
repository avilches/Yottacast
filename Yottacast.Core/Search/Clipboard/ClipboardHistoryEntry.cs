namespace Yottacast.Core.Search.Clipboard;

public record ClipboardHistoryEntry(
    string Text,
    DateTimeOffset CopiedAt,
    int UsageCount,
    DateTimeOffset LastUsedAt
);
