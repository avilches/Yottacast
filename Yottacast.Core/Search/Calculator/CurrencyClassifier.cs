namespace Yottacast.Core.Search.Calculator;

public enum CurrencyType { Forex, Metal, Crypto }

/// <summary>
/// Classifies currency codes as Forex (country currencies), Metal (precious metals),
/// or Crypto (everything else). Used to filter which currencies are loaded into the math engine.
/// </summary>
public static class CurrencyClassifier {
    /// <summary>ISO 4217 fiat currency codes. Always included in the engine.</summary>
    public static readonly HashSet<string> Forex = new(StringComparer.OrdinalIgnoreCase) {
        // Major
        "USD","EUR","GBP","JPY","CHF","CAD","AUD","NZD","CNY","HKD","SGD",
        "SEK","NOK","DKK","MXN","BRL","INR","KRW","ZAR","TRY","RUB","PLN",
        "CZK","HUF","RON","HRK","BGN","ILS","AED","SAR","QAR","KWD","BHD",
        "OMR","JOD","THB","MYR","IDR","PHP","VND","EGP","NGN","PKR","BDT",
        "UAH","GEL","AZN","AMD","KZT","UZS","GHS","KES","TZS","ETB","DZD",
        "MAD","TND","LYD","AOA","MZN","ZMW","UGX","RWF","CDF","XOF","XAF",
        "PEN","COP","ARS","CLP","UYU","BOB","PYG","VES","DOP","GTQ","HNL",
        "NIO","CRC","PAB","CUP","JMD","TTD","BBD","XCD","BWP","MUR","SCR",
        "MGA","ZWL","NAD","SZL","LSL","MWK","GMD","SLL","LRD","GNF",
        "MRU","CVE","STN","KMF","DJF","ERN","SOS","SDG","SSP",
        "ISK","LBP","SYP","IQD","IRR","AFN","LKR","NPR","MMK",
        "KHR","LAK","MNT","KPW","TWD","MVR","BND","PGK","FJD","SBD","TOP",
        "WST","VUV","XPF","KYD","BSD","HTG","AWG","ANG","SRD","GYD",
        "BMD","BZD","FKP","GIP","SHP","MOP","KGS","TJS","TMT","MDL",
        "ALL","MKD","BAM","RSD","BYN","XDR",
    };

    /// <summary>Precious metal currency codes (XAU=gold, XAG=silver, XPT=platinum, XPD=palladium).</summary>
    public static readonly HashSet<string> Metals = new(StringComparer.OrdinalIgnoreCase) {
        "XAU", "XAG", "XPT", "XPD"
    };

    /// <summary>Returns the type of currency: Forex, Metal, or Crypto (anything else).</summary>
    public static CurrencyType Classify(string code) {
        if (Forex.Contains(code)) return CurrencyType.Forex;
        if (Metals.Contains(code)) return CurrencyType.Metal;
        return CurrencyType.Crypto;
    }
}
