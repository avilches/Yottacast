using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

public class FileEditorService(ILogger<FileEditorService> logger) {
    public record OpenResult(bool CanOpen, string? Error = null);

    public bool HasEditableExtension(string filePath, IReadOnlyList<string> extensions) {
        var ext = Path.GetExtension(filePath).TrimStart('.');
        if (string.IsNullOrEmpty(ext)) return false;
        return extensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsTextContent(string filePath) {
        try {
            var length = new FileInfo(filePath).Length;
            if (length == 0) return true;
            var bytesToRead = (int)Math.Min(length, AppDefaults.EditorBinaryDetectionBytes);
            var buffer = new byte[bytesToRead];
            using var fs = File.OpenRead(filePath);
            var read = fs.Read(buffer, 0, bytesToRead);
            for (var i = 0; i < read; i++)
                if (buffer[i] == 0) return false;
            return true;
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to read file for binary detection: {Path}", filePath);
            return false;
        }
    }

    public OpenResult CanOpen(string filePath, IReadOnlyList<string> extensions) {
        if (!HasEditableExtension(filePath, extensions))
            return new(false);
        var info = new FileInfo(filePath);
        if (!info.Exists)
            return new(false, "File not found");
        if (info.Length > (long)AppDefaults.EditorMaxFileSizeMb * 1024 * 1024)
            return new(false, "File is too large to open in the editor");
        if (!IsTextContent(filePath))
            return new(false, "File appears to be binary");
        return new(true);
    }

    public string ReadFile(string filePath) => File.ReadAllText(filePath);

    public void WriteFile(string filePath, string content) => File.WriteAllText(filePath, content);
}
