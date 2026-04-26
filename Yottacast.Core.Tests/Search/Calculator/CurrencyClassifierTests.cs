using Xunit;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.Tests.Search.Calculator;

public class CurrencyClassifierTests {

    // ── Forex ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    public void Classify_UsdEurGbp_ReturnsForex(string code) {
        Assert.Equal(CurrencyType.Forex, CurrencyClassifier.Classify(code));
    }

    // ── Metals ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("XAU")]
    [InlineData("XAG")]
    [InlineData("XPT")]
    [InlineData("XPD")]
    public void Classify_XauXagXptXpd_ReturnsMetal(string code) {
        Assert.Equal(CurrencyType.Metal, CurrencyClassifier.Classify(code));
    }

    // ── Crypto ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("BTC")]
    [InlineData("ETH")]
    [InlineData("DOGE")]
    public void Classify_BtcEthDoge_ReturnsCrypto(string code) {
        Assert.Equal(CurrencyType.Crypto, CurrencyClassifier.Classify(code));
    }

    // ── Case insensitivity ────────────────────────────────────────────────────

    [Theory]
    [InlineData("usd")]
    [InlineData("USD")]
    [InlineData("Usd")]
    public void Classify_IsCaseInsensitive(string code) {
        Assert.Equal(CurrencyType.Forex, CurrencyClassifier.Classify(code));
    }
}
