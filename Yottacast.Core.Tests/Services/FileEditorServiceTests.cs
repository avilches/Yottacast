using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Services;

public class FileEditorServiceTests {
    private readonly FileEditorService _svc = new(NullLogger<FileEditorService>.Instance);

    [Fact]
    public void HasEditableExtension_KnownExtension_ReturnsTrue() {
        Assert.True(_svc.HasEditableExtension("/foo/bar.txt", ["txt", "cs"]));
    }

    [Fact]
    public void HasEditableExtension_UnknownExtension_ReturnsFalse() {
        Assert.False(_svc.HasEditableExtension("/foo/bar.exe", ["txt", "cs"]));
    }

    [Fact]
    public void HasEditableExtension_CaseInsensitive() {
        Assert.True(_svc.HasEditableExtension("/foo/bar.TXT", ["txt"]));
    }

    [Fact]
    public void HasEditableExtension_NoExtension_ReturnsFalse() {
        Assert.False(_svc.HasEditableExtension("/foo/Makefile", ["txt", "cs"]));
    }

    [Fact]
    public void IsTextContent_TextFile_ReturnsTrue() {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "Hello, world!\nLine 2");
        try { Assert.True(_svc.IsTextContent(tmp)); }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void IsTextContent_BinaryFile_ReturnsFalse() {
        var tmp = Path.GetTempFileName();
        File.WriteAllBytes(tmp, [0x00, 0x01, 0x02, 0x00, 0xFF]);
        try { Assert.False(_svc.IsTextContent(tmp)); }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void IsTextContent_EmptyFile_ReturnsTrue() {
        var tmp = Path.GetTempFileName();
        File.WriteAllBytes(tmp, []);
        try { Assert.True(_svc.IsTextContent(tmp)); }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CanOpen_ValidTextFile_ReturnsCanOpen() {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "hello");
        var ext = Path.GetExtension(tmp).TrimStart('.');
        try {
            var result = _svc.CanOpen(tmp, [ext]);
            Assert.True(result.CanOpen);
            Assert.Null(result.Error);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CanOpen_WrongExtension_ReturnsFalse() {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "hello");
        try {
            var result = _svc.CanOpen(tmp, ["neverexists"]);
            Assert.False(result.CanOpen);
            Assert.Null(result.Error);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CanOpen_NonExistentFile_ReturnsError() {
        var result = _svc.CanOpen("/nonexistent/file.txt", ["txt"]);
        Assert.False(result.CanOpen);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void CanOpen_BinaryContent_ReturnsError() {
        var tmp = Path.GetTempFileName();
        File.WriteAllBytes(tmp, [0x00, 0x01, 0x02]);
        var ext = Path.GetExtension(tmp).TrimStart('.');
        try {
            var result = _svc.CanOpen(tmp, [ext]);
            Assert.False(result.CanOpen);
            Assert.NotNull(result.Error);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ReadFile_ReturnsContent() {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "hello content");
        try { Assert.Equal("hello content", _svc.ReadFile(tmp)); }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void WriteFile_WritesContent() {
        var tmp = Path.GetTempFileName();
        try {
            _svc.WriteFile(tmp, "new content");
            Assert.Equal("new content", File.ReadAllText(tmp));
        }
        finally { File.Delete(tmp); }
    }
}
