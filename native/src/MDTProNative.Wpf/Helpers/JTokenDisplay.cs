using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Helpers;

/// <summary>Turns API JSON (nested objects, arrays, locations) into short, readable text for grids and status lines.</summary>
public static class JTokenDisplay
{
    const int MaxGenericObjectLength = 320;

    public static string ForDataCell(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return "";
        return token switch
        {
            JValue v => FormatScalar(v),
            JArray a => FormatArray(a),
            JObject o => FormatObject(o),
            _ => token.ToString()
        };
    }

    public static string FormatLocation(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return "";
        if (token is JObject o) return FormatObject(o);
        return FormatScalar((JValue)token);
    }

    /// <summary>Multi-line key/value block for search results, callout detail, etc.</summary>
    public static string FormatDocument(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return "";
        if (token is not JObject jo) return ForDataCell(token);
        var sb = new StringBuilder();
        foreach (var p in jo.Properties())
        {
            if (p.Value.Type == JTokenType.Null) continue;
            var valueText = FormatPropertyValue(p.Value);
            if (string.IsNullOrWhiteSpace(valueText)) continue;
            sb.AppendLine($"{p.Name}: {valueText}");
        }
        return sb.ToString().TrimEnd();
    }

    static string FormatPropertyValue(JToken value)
    {
        if (value is JArray ja && ja.Count > 0 && ja.All(t => t is JObject))
        {
            var lines = ja.Select(ForDataCell).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (lines.Count == 0) return "";
            if (lines.Count == 1) return lines[0];
            return Environment.NewLine + "  • " + string.Join(Environment.NewLine + "  • ", lines);
        }
        return ForDataCell(value);
    }

    static string FormatScalar(JValue v)
    {
        return v.Type switch
        {
            JTokenType.String => v.Value<string>() ?? "",
            JTokenType.Integer or JTokenType.Float or JTokenType.Boolean =>
                v.ToString(CultureInfo.InvariantCulture),
            JTokenType.Date => v.Value<DateTime>().ToString("g", CultureInfo.CurrentCulture),
            JTokenType.Null => "",
            _ => v.ToString(CultureInfo.InvariantCulture)
        };
    }

    static string FormatArray(JArray arr)
    {
        if (arr.Count == 0) return "—";
        if (arr.All(t => t.Type == JTokenType.String))
            return string.Join(", ", arr.Select(t => t.Value<string>()).Where(s => !string.IsNullOrEmpty(s)));

        if (arr.All(t => t is JObject))
        {
            var parts = arr.Select(t => SummarizeObject((JObject)t)).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (parts.Count == 0) return "—";
            var oneLine = string.Join("; ", parts);
            return oneLine.Length > 220 ? string.Join(Environment.NewLine, parts) : oneLine;
        }

        return string.Join(", ", arr.Select(ForDataCell).Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    static string SummarizeObject(JObject o)
    {
        var name = o["name"]?.Value<string>() ?? o["Name"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(name)) return name!;

        var drug = o["DrugType"]?.Value<string>() ?? o["drugType"]?.Value<string>();
        var qty = o["Quantity"]?.Value<string>() ?? o["quantity"]?.ToString();
        if (!string.IsNullOrWhiteSpace(drug))
            return string.IsNullOrWhiteSpace(qty) ? drug! : $"{drug} ({qty})";

        var plate = o["LicensePlate"]?.Value<string>() ?? o["licensePlate"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(plate)) return plate!;

        return CompactGenericObject(o);
    }

    static string FormatObject(JObject o)
    {
        if (LooksLikeLocation(o))
        {
            var parts = new[]
            {
                o["Area"]?.Value<string>() ?? o["area"]?.Value<string>(),
                o["Street"]?.Value<string>() ?? o["street"]?.Value<string>(),
                o["County"]?.Value<string>() ?? o["county"]?.Value<string>(),
                o["Postal"]?.Value<string>() ?? o["postal"]?.Value<string>()
            }.Where(s => !string.IsNullOrWhiteSpace(s));
            var s = string.Join(", ", parts);
            return string.IsNullOrEmpty(s) ? CompactGenericObject(o) : s;
        }

        if (LooksLikeOfficer(o))
        {
            var rank = o["rank"]?.Value<string>();
            var fn = o["firstName"]?.Value<string>();
            var ln = o["lastName"]?.Value<string>();
            var name = $"{fn} {ln}".Trim();
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(rank)) bits.Add(rank!);
            if (!string.IsNullOrWhiteSpace(name)) bits.Add(name);
            var cs = o["callSign"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(cs)) bits.Add(cs!);
            var ag = o["agency"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(ag)) bits.Add(ag!);
            var badge = o["badgeNumber"]?.ToString();
            if (!string.IsNullOrWhiteSpace(badge)) bits.Add($"#{badge}");
            return string.Join(" · ", bits);
        }

        var dominant = o["name"]?.Value<string>() ?? o["Name"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(dominant) && o.Properties().Count() <= 16)
            return dominant!;

        return CompactGenericObject(o);
    }

    static bool LooksLikeLocation(JObject o)
    {
        var keys = new HashSet<string>(o.Properties().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        return keys.Contains("Area") || keys.Contains("Street") || keys.Contains("County") || keys.Contains("Postal");
    }

    static bool LooksLikeOfficer(JObject o)
    {
        var keys = new HashSet<string>(o.Properties().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        return keys.Contains("firstName") || keys.Contains("lastName") || keys.Contains("callSign") || keys.Contains("badgeNumber");
    }

    static string CompactGenericObject(JObject o)
    {
        var parts = new List<string>();
        foreach (var p in o.Properties())
        {
            if (p.Value.Type == JTokenType.Null) continue;
            var inner = p.Value is JValue jv ? FormatScalar(jv) : ForDataCell(p.Value);
            if (string.IsNullOrWhiteSpace(inner)) continue;
            parts.Add($"{p.Name}: {inner}");
        }
        if (parts.Count == 0) return "—";
        var s = string.Join(" · ", parts);
        return s.Length > MaxGenericObjectLength ? s[..(MaxGenericObjectLength - 1)] + "…" : s;
    }

    public static bool IsJsonNullOrEmptyResponse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var t = text.Trim();
        if (t.Equals("null", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static JToken? ParseJsonOrNull(string? text)
    {
        if (IsJsonNullOrEmptyResponse(text)) return null;
        var t = text!.Trim();
        if (!t.StartsWith('{') && !t.StartsWith('[')) return null;
        try { return JToken.Parse(text!); }
        catch { return null; }
    }
}
