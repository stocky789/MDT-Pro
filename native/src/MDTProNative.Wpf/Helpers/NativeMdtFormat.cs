using System.Globalization;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Helpers;

static class NativeMdtFormat
{
    /// <summary>British-style calendar order (dd/MM) for MDT read-only text and editable timestamp fields.</summary>
    internal static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("en-GB");

    static readonly string[] CourtStatuses = ["Pending", "Convicted", "Acquitted", "Dismissed"];

    public static string CourtStatus(int status) =>
        status >= 0 && status < CourtStatuses.Length ? CourtStatuses[status] : $"Status {status}";

    public static string YesNo(JToken? t)
    {
        if (t == null || t.Type == JTokenType.Null) return "—";
        if (t.Type == JTokenType.Boolean) return t.Value<bool>() ? "Yes" : "No";
        var s = t.ToString();
        if (bool.TryParse(s, out var b)) return b ? "Yes" : "No";
        return string.IsNullOrWhiteSpace(s) ? "—" : s;
    }

    public static string Text(JToken? t)
    {
        if (t == null || t.Type == JTokenType.Null) return "—";
        if (t.Type == JTokenType.Boolean) return t.Value<bool>() ? "Yes" : "No";
        var s = t.ToString();
        return string.IsNullOrWhiteSpace(s) ? "—" : s;
    }

    /// <summary>Format a single instant for UI: date-only at midnight shows as dd/MM/yyyy; otherwise dd/MM/yyyy HH:mm (local).</summary>
    public static string FormatDateTimeDisplay(DateTime dt)
    {
        var local = dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
        if (local.TimeOfDay.TotalSeconds < 1)
            return local.ToString("dd/MM/yyyy", DisplayCulture);
        return local.ToString("dd/MM/yyyy HH:mm", DisplayCulture);
    }

    /// <summary>Parse timestamps from API (ISO) or from MDT display text (dd/MM/yyyy, optional time).</summary>
    public static bool TryParseMdtDateTime(string? text, out DateTime result)
    {
        text = text?.Trim() ?? "";
        if (text.Length == 0)
        {
            result = default;
            return false;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var iso))
        {
            result = iso;
            return true;
        }

        string[] exact =
        [
            "dd/MM/yyyy HH:mm", "dd/MM/yyyy H:mm", "d/M/yyyy HH:mm", "d/M/yyyy H:mm",
            "dd/MM/yyyy", "d/M/yyyy",
        ];
        if (DateTime.TryParseExact(text, exact, DisplayCulture, DateTimeStyles.None, out var gbEx))
        {
            result = DateTime.SpecifyKind(gbEx, DateTimeKind.Local);
            return true;
        }

        if (DateTime.TryParse(text, DisplayCulture, DateTimeStyles.None, out var gb))
        {
            result = DateTime.SpecifyKind(gb, DateTimeKind.Local);
            return true;
        }

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var cur))
        {
            result = DateTime.SpecifyKind(cur, DateTimeKind.Local);
            return true;
        }

        result = default;
        return false;
    }

    public static string IsoDate(JToken? t)
    {
        if (t == null || t.Type == JTokenType.Null) return "—";
        if (t.Type == JTokenType.Date)
            return FormatDateTimeDisplay(t.Value<DateTime>());
        var s = t.ToString();
        if (TryParseMdtDateTime(s, out var dt))
            return FormatDateTimeDisplay(dt);
        return string.IsNullOrWhiteSpace(s) ? "—" : s;
    }

    public static IEnumerable<string> StringList(JToken? t)
    {
        if (t is not JArray arr) yield break;
        foreach (var item in arr)
        {
            if (item.Type == JTokenType.String)
            {
                var s = item.Value<string>();
                if (!string.IsNullOrWhiteSpace(s)) yield return s!;
            }
            else if (item is JObject o)
            {
                var name = o["name"]?.ToString() ?? o["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) yield return name!;
                else
                {
                    var line = JTokenDisplay.ForDataCell(o);
                    if (!string.IsNullOrWhiteSpace(line)) yield return line;
                }
            }
        }
    }
}
