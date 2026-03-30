using System.Globalization;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Helpers;

static class NativeMdtFormat
{
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

    public static string IsoDate(JToken? t)
    {
        if (t == null || t.Type == JTokenType.Null) return "—";
        var s = t.ToString();
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        return s;
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
