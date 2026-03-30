using System.Globalization;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

internal static class ReportFormJson
{
    public static int ParseStatusToken(JToken? t)
    {
        if (t == null || t.Type == JTokenType.Null) return 1;
        if (t.Type == JTokenType.Integer)
        {
            var i = t.Value<int>();
            return i is >= 0 and <= 3 ? i : 1;
        }

        if (t.Type == JTokenType.Float)
        {
            var i = (int)Math.Round(t.Value<double>(), MidpointRounding.AwayFromZero);
            return i is >= 0 and <= 3 ? i : 1;
        }

        var s = t.ToString().Trim();
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= 3)
            return n;
        return s.ToLowerInvariant() switch
        {
            "closed" => 0,
            "open" => 1,
            "canceled" or "cancelled" => 2,
            "pending" => 3,
            _ => 1
        };
    }

    public static DateTime ParseTimestampToken(JToken? t)
    {
        if (t == null || t.Type == JTokenType.Null) return DateTime.Now;
        var s = t.ToString();
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
        return DateTime.Now;
    }

    public static JObject BuildOfficerInformation(string first, string last, string rank, string call, string agency, string badgeText)
    {
        var o = new JObject
        {
            ["firstName"] = first ?? "",
            ["lastName"] = last ?? "",
            ["rank"] = rank ?? "",
            ["callSign"] = call ?? "",
            ["agency"] = agency ?? ""
        };
        if (string.IsNullOrWhiteSpace(badgeText))
            o["badgeNumber"] = JValue.CreateNull();
        else if (int.TryParse(badgeText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bn))
            o["badgeNumber"] = bn;
        else
            o["badgeNumber"] = badgeText.Trim();
        return o;
    }

    public static void ReadOfficer(JToken? tok, out string first, out string last, out string rank, out string call, out string agency, out string badge)
    {
        var o = tok as JObject;
        first = o?["firstName"]?.ToString() ?? "";
        last = o?["lastName"]?.ToString() ?? "";
        rank = o?["rank"]?.ToString() ?? "";
        call = o?["callSign"]?.ToString() ?? "";
        agency = o?["agency"]?.ToString() ?? "";
        var b = o?["badgeNumber"];
        badge = b == null || b.Type == JTokenType.Null ? "" : b.ToString();
    }

    public static JObject BuildLocation(string area, string street, string county, string postal) =>
        new()
        {
            ["Area"] = area ?? "",
            ["Street"] = street ?? "",
            ["County"] = county ?? "",
            ["Postal"] = postal ?? ""
        };

    public static void ReadLocation(JToken? tok, out string area, out string street, out string county, out string postal)
    {
        var o = tok as JObject;
        area = o?["Area"]?.ToString() ?? "";
        street = o?["Street"]?.ToString() ?? "";
        county = o?["County"]?.ToString() ?? "";
        postal = o?["Postal"]?.ToString() ?? "";
    }

    public static string JArrayStringsToLines(JToken? t)
    {
        if (t is not JArray arr) return "";
        return string.Join(Environment.NewLine,
            arr.Select(x => x.Type == JTokenType.String ? x.Value<string>() ?? "" : x.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    public static JArray LinesToStringJArray(string? text)
    {
        var ja = new JArray();
        if (string.IsNullOrWhiteSpace(text)) return ja;
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) ja.Add(trimmed);
        }
        return ja;
    }

    public static JArray ParseChargesMultiline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new JArray();
        try
        {
            var tok = JToken.Parse(text.Trim());
            if (tok is JArray a) return (JArray)a.DeepClone();
            if (tok is JObject one) return new JArray(one);
            return new JArray();
        }
        catch
        {
            return new JArray();
        }
    }

    public static string ChargesToEditorText(JToken? t)
    {
        if (t is not JArray arr || arr.Count == 0) return "";
        return arr.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    public static DateTime CombineDateAndTime(DateTime? datePart, string? timeText)
    {
        var d = (datePart ?? DateTime.Today).Date;
        if (string.IsNullOrWhiteSpace(timeText))
            return DateTime.SpecifyKind(d, DateTimeKind.Local);
        var t = timeText.Trim();
        if (TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out var ts))
            return DateTime.SpecifyKind(d + ts, DateTimeKind.Local);
        if (TimeSpan.TryParseExact(t, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out ts))
            return DateTime.SpecifyKind(d + ts, DateTimeKind.Local);
        if (TimeSpan.TryParseExact(t, @"hh\:mm", CultureInfo.InvariantCulture, out ts))
            return DateTime.SpecifyKind(d + ts, DateTimeKind.Local);
        if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out var asTime))
            return DateTime.SpecifyKind(d.Date + asTime.TimeOfDay, DateTimeKind.Local);
        return DateTime.SpecifyKind(d, DateTimeKind.Local);
    }

    public static JObject MergeOverlay(JObject? source, Action<JObject> write)
    {
        var o = source == null ? new JObject() : (JObject)source.DeepClone();
        write(o);
        return o;
    }
}
