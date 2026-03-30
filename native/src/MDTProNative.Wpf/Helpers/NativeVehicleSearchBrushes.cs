using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Helpers;

/// <summary>
/// CDF vehicle summary colors; matches <c>vehicleSearch.js</c> <c>getColorForValue</c>, VIN <c>Scratched</c>,
/// and expiration + paired status handling (StopPed-style checks simplified for native).
/// </summary>
static class NativeVehicleSearchBrushes
{
    static readonly Regex BadDocStatus = new(
        "expired|revoked|suspended|invalid|unlicensed",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Same cases as <c>vehicleSearch.js</c> <c>getColorForValue</c> (includes <c>None</c> → error).</summary>
    public static Brush ForVehicleField(JToken? token, Brush primary, Brush success, Brush warning, Brush danger)
    {
        if (token == null || token.Type == JTokenType.Null) return primary;
        if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? danger : success;
        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) return primary;
        var s = token.ToString().Trim();
        if (string.IsNullOrEmpty(s)) return primary;
        return ForVehicleStatusString(s, primary, success, warning, danger);
    }

    public static Brush ForVehicleStatusString(string s, Brush primary, Brush success, Brush warning, Brush danger)
    {
        s = s.Trim();
        if (s.Equals("Revoked", StringComparison.OrdinalIgnoreCase)
            || s.Equals("None", StringComparison.OrdinalIgnoreCase))
            return danger;
        if (s.Equals("Valid", StringComparison.OrdinalIgnoreCase)) return success;
        if (s.Equals("Expired", StringComparison.OrdinalIgnoreCase)) return warning;
        if (s.Equals("Unlicensed", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Suspended", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Invalid", StringComparison.OrdinalIgnoreCase))
            return danger;
        return primary;
    }

    /// <summary><c>VinStatus === 'Scratched'</c> → warning; otherwise same as <see cref="ForVehicleField"/>.</summary>
    public static Brush ForVinStatus(JToken? token, Brush primary, Brush success, Brush warning, Brush danger)
    {
        if (token == null || token.Type == JTokenType.Null) return primary;
        var s = token.ToString().Trim();
        if (s.Equals("Scratched", StringComparison.OrdinalIgnoreCase)) return warning;
        return ForVehicleField(token, primary, success, warning, danger);
    }

    /// <summary>
    /// Registration/insurance expiration line: paired status can force warning; past date → warning, or danger if over a year past.
    /// </summary>
    public static Brush ForExpirationWithPairedStatus(
        JToken? expirationToken,
        JToken? pairedStatusToken,
        Brush primary,
        Brush warning,
        Brush danger)
    {
        var sev = PairedDocStatusSeverity(pairedStatusToken);
        if (sev == DocSeverity.Danger) return danger;
        if (sev == DocSeverity.Warning) return warning;

        if (expirationToken == null || expirationToken.Type == JTokenType.Null) return primary;
        if (!DateTimeOffset.TryParse(expirationToken.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return primary;
        var now = DateTimeOffset.UtcNow;
        if (dto >= now) return primary;
        if (dto < now.AddYears(-1)) return danger;
        return warning;
    }

    enum DocSeverity { None, Warning, Danger }

    static DocSeverity PairedDocStatusSeverity(JToken? statusToken)
    {
        var s = statusToken?.ToString().Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(s)) return DocSeverity.None;
        if (s is "none" or "revoked") return DocSeverity.Danger;
        if (BadDocStatus.IsMatch(s)) return DocSeverity.Warning;
        return DocSeverity.None;
    }
}
