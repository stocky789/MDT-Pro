using System.Globalization;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Helpers;

/// <summary>
/// Traffic-light styling for CDF-backed ped fields; matches <c>pedSearch.js</c> <c>getColorForValue</c> + expiration checks.
/// </summary>
static class NativePedSearchBrushes
{
    public static Brush ForCdfValue(JToken? token, Brush primary, Brush success, Brush warning, Brush danger)
    {
        if (token == null || token.Type == JTokenType.Null) return primary;
        if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? danger : success;
        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            return primary;
        var s = token.ToString();
        if (string.IsNullOrWhiteSpace(s)) return primary;
        return ForCdfStatusString(s, primary, success, warning, danger);
    }

    public static Brush ForCdfStatusString(string? s, Brush primary, Brush success, Brush warning, Brush danger)
    {
        if (string.IsNullOrWhiteSpace(s)) return primary;
        s = s.Trim();
        if (s.Equals("Revoked", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Unlicensed", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            return danger;
        if (s.Equals("Valid", StringComparison.OrdinalIgnoreCase)) return success;
        if (s.Equals("Expired", StringComparison.OrdinalIgnoreCase)) return warning;
        return primary;
    }

    /// <summary>Warrant line clear → success; any text → danger (matches emphasis on active warrant).</summary>
    public static Brush ForWarrantDisplay(string display, Brush success, Brush danger) =>
        string.IsNullOrWhiteSpace(display) || display == "—" ? success : danger;

    /// <summary>Browser MDT paints advisory as error whenever present.</summary>
    public static Brush ForAdvisoryDisplay(string display, Brush primary, Brush danger) =>
        string.IsNullOrWhiteSpace(display) || display == "—" ? primary : danger;

    /// <summary>Yellow when expiration is in the past; otherwise primary (browser pedSearch.js).</summary>
    public static Brush ForExpirationToken(JToken? token, Brush primary, Brush warning)
    {
        if (token == null || token.Type == JTokenType.Null) return primary;
        if (!DateTimeOffset.TryParse(token.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return primary;
        return dto < DateTimeOffset.UtcNow ? warning : primary;
    }
}
