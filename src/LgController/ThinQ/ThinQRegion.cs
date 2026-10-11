namespace LgController.ThinQ;

public static class ThinQRegion
{
    private static readonly HashSet<string> Kic =
    [
        "AU", "BD", "CN", "HK", "ID", "IN", "JP", "KH", "KR", "LA", "LK", "MM", "MY", "NP", "NZ", "PH", "SG", "TH",
        "TW", "VN",
    ];

    private static readonly HashSet<string> Aic =
    [
        "AG", "AR", "AW", "BB", "BO", "BR", "BS", "BZ", "CA", "CL", "CO", "CR", "CU", "DM", "DO", "EC", "GD", "GT",
        "GY", "HN", "HT", "JM", "KN", "LC", "MX", "NI", "PA", "PE", "PR", "PY", "SR", "SV", "TT", "US", "UY", "VC",
        "VE",
    ];

    private static readonly HashSet<string> Eic =
    [
        "AE", "AF", "AL", "AM", "AO", "AT", "AZ", "BA", "BE", "BF", "BG", "BH", "BJ", "BY", "CD", "CF", "CG", "CH",
        "CI", "CM", "CV", "CY", "CZ", "DE", "DJ", "DK", "DZ", "EE", "EG", "ES", "ET", "FI", "FR", "GA", "GB", "GE",
        "GH", "GM", "GN", "GQ", "GR", "HR", "HU", "IE", "IL", "IQ", "IR", "IS", "IT", "JO", "KE", "KG", "KW", "KZ",
        "LB", "LR", "LT", "LU", "LV", "LY", "MA", "MD", "ME", "MK", "ML", "MR", "MT", "MU", "MW", "NE", "NG", "NL",
        "NO", "OM", "PK", "PL", "PS", "PT", "QA", "RO", "RS", "RU", "RW", "SA", "SD", "SE", "SI", "SK", "SL", "SN",
        "SO", "ST", "SY", "TD", "TG", "TN", "TR", "TZ", "UA", "UG", "UZ", "XK", "YE", "ZA", "ZM",
    ];

    public static string FromCountry(string countryCode)
    {
        var code = countryCode.Trim().ToUpperInvariant();
        if (Kic.Contains(code)) return "kic";
        if (Aic.Contains(code)) return "aic";
        if (Eic.Contains(code)) return "eic";
        throw new ArgumentException($"Unsupported country: {countryCode}", nameof(countryCode));
    }
}
