using Xunit;
using Yottacast.Core.Platform;

namespace Yottacast.Core.Tests.Platform;

public class HotkeyConfigTests {
    // --- Parse: canonical modifier names ---

    [Theory]
    [InlineData("Alt+Space", true, false, false, false, "Space")]
    [InlineData("Ctrl+Space", false, true, false, false, "Space")]
    [InlineData("Shift+Space", false, false, true, false, "Space")]
    [InlineData("Meta+Space", false, false, false, true, "Space")]
    [InlineData("Ctrl+Shift+A", false, true, true, false, "A")]
    [InlineData("Alt+Ctrl+Shift+Meta+F1", true, true, true, true, "F1")]
    public void Parse_CanonicalModifiers(string input, bool alt, bool ctrl, bool shift, bool meta, string key) {
        var h = HotkeyConfig.Parse(input);
        Assert.NotNull(h);
        Assert.Equal(alt, h.Alt);
        Assert.Equal(ctrl, h.Ctrl);
        Assert.Equal(shift, h.Shift);
        Assert.Equal(meta, h.Meta);
        Assert.Equal(key, h.KeyName);
    }

    // --- Parse: Alt synonyms ---

    [Theory]
    [InlineData("Option+Space")]
    [InlineData("Options+Space")]
    public void Parse_AltSynonyms(string input) {
        var h = HotkeyConfig.Parse(input);
        Assert.NotNull(h);
        Assert.True(h.Alt);
        Assert.Equal("Space", h.KeyName);
    }

    // --- Parse: Ctrl synonyms ---

    [Fact]
    public void Parse_ControlIsSynonymForCtrl() {
        var h = HotkeyConfig.Parse("Control+C");
        Assert.NotNull(h);
        Assert.True(h.Ctrl);
        Assert.Equal("C", h.KeyName);
    }

    // --- Parse: Meta synonyms ---

    [Theory]
    [InlineData("Cmd+Space")]
    [InlineData("Command+Space")]
    [InlineData("Win+Space")]
    [InlineData("Windows+Space")]
    public void Parse_MetaSynonyms(string input) {
        var h = HotkeyConfig.Parse(input);
        Assert.NotNull(h);
        Assert.True(h.Meta);
        Assert.Equal("Space", h.KeyName);
    }

    // --- Parse: case-insensitive ---

    [Theory]
    [InlineData("alt+space")]
    [InlineData("ALT+SPACE")]
    [InlineData("Alt+Space")]
    [InlineData("option+space")]
    [InlineData("OPTION+SPACE")]
    public void Parse_CaseInsensitive(string input) {
        var h = HotkeyConfig.Parse(input);
        Assert.NotNull(h);
        Assert.True(h.Alt);
    }

    // --- Parse: null / empty / no key ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Alt")] // modifier only, no key
    [InlineData("Ctrl+Shift")] // modifiers only, no key
    public void Parse_ReturnsNullForInvalidInput(string? input) {
        Assert.Null(HotkeyConfig.Parse(input));
    }

    // --- Default ---

    [Fact]
    public void Default_IsAltSpace() {
        var d = HotkeyConfig.Default;
        Assert.True(d.Alt);
        Assert.False(d.Ctrl);
        Assert.False(d.Shift);
        Assert.False(d.Meta);
        Assert.Equal("Space", d.KeyName);
    }

    // --- ToString ---

    [Theory]
    [InlineData(true, false, false, false, "Space", "Alt+Space")]
    [InlineData(false, true, false, false, "Space", "Ctrl+Space")]
    [InlineData(false, false, true, false, "A", "Shift+A")]
    [InlineData(false, false, false, true, "Space", "Meta+Space")]
    [InlineData(false, true, true, false, "A", "Ctrl+Shift+A")]
    [InlineData(true, true, true, true, "F1", "Ctrl+Alt+Shift+Meta+F1")]
    public void ToString_CanonicalOrder(bool alt, bool ctrl, bool shift, bool meta, string key, string expected) {
        var h = new HotkeyConfig(alt, ctrl, shift, meta, key);
        Assert.Equal(expected, h.ToString());
    }

    // --- Round-trip ---

    [Theory]
    [InlineData("Alt+Space")]
    [InlineData("Ctrl+Shift+A")]
    [InlineData("Ctrl+Alt+Shift+Meta+F1")]
    public void RoundTrip_ParseThenToString(string canonical) {
        var h = HotkeyConfig.Parse(canonical);
        Assert.NotNull(h);
        Assert.Equal(canonical, h.ToString());
    }

    // --- Synonyms normalise to canonical form ---

    [Theory]
    [InlineData("Option+Space", "Alt+Space")]
    [InlineData("Options+Space", "Alt+Space")]
    [InlineData("Control+C", "Ctrl+C")]
    [InlineData("Cmd+Space", "Meta+Space")]
    [InlineData("Command+Space", "Meta+Space")]
    [InlineData("Win+Space", "Meta+Space")]
    [InlineData("Windows+Space", "Meta+Space")]
    public void Synonyms_NormaliseToCanonicalToString(string input, string expected) {
        var h = HotkeyConfig.Parse(input);
        Assert.NotNull(h);
        Assert.Equal(expected, h.ToString());
    }
}