using System.Globalization;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

/// <summary>Shared load/save for <see cref="Report"/> base JSON (Id, ShortYear, TimeStamp, Status, Notes, OfficerInformation, Location).</summary>
internal readonly struct ReportFormBaseControls
{
    public TextBox Id { get; init; }
    public TextBox ShortYear { get; init; }
    public DatePicker Date { get; init; }
    public TextBox Time { get; init; }
    public ComboBox Status { get; init; }
    public TextBox Notes { get; init; }
    public TextBox OffFirst { get; init; }
    public TextBox OffLast { get; init; }
    public TextBox OffBadge { get; init; }
    public TextBox OffRank { get; init; }
    public TextBox OffCall { get; init; }
    public TextBox OffAgency { get; init; }
    public TextBox LocArea { get; init; }
    public TextBox LocStreet { get; init; }
    public TextBox LocCounty { get; init; }
    public TextBox LocPostal { get; init; }
}

internal static class ReportFormPaneFields
{
    public static JArray LinesToStringArray(string? text)
    {
        var arr = new JArray();
        if (string.IsNullOrWhiteSpace(text)) return arr;
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var s = line.Trim();
            if (s.Length > 0) arr.Add(s);
        }
        return arr;
    }

    public static string StringArrayToLines(JToken? token)
    {
        if (token is not JArray arr) return "";
        var lines = new List<string>();
        foreach (var x in arr)
        {
            if (x == null || x.Type == JTokenType.Null) continue;
            var s = x.Type == JTokenType.String ? x.Value<string>() : x.ToString();
            if (!string.IsNullOrWhiteSpace(s)) lines.Add(s.Trim());
        }
        return string.Join(Environment.NewLine, lines);
    }

    public static void LoadBase(JObject report, ReportFormBaseControls c)
    {
        c.Id.Text = GetStr(report, "Id");
        var syTok = report["ShortYear"];
        if (syTok == null || syTok.Type == JTokenType.Null)
            c.ShortYear.Text = "";
        else
            c.ShortYear.Text = ReadIntFlexible(syTok, DateTime.Now.Year % 100).ToString(CultureInfo.InvariantCulture);

        var ts = ReadDateTime(report["TimeStamp"], DateTime.Now);
        c.Date.SelectedDate = ts.Date;
        c.Time.Text = ts.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        var st = ReadIntFlexible(report["Status"], 1);
        c.Status.SelectedIndex = st is >= 0 and <= 3 ? st : 1;

        c.Notes.Text = GetStr(report, "Notes");

        var off = report["OfficerInformation"] as JObject;
        c.OffFirst.Text = GetStr(off, "firstName", "FirstName");
        c.OffLast.Text = GetStr(off, "lastName", "LastName");
        c.OffBadge.Text = GetStr(off, "badgeNumber", "BadgeNumber");
        c.OffRank.Text = GetStr(off, "rank", "Rank");
        c.OffCall.Text = GetStr(off, "callSign", "CallSign");
        c.OffAgency.Text = GetStr(off, "agency", "Agency");

        var loc = report["Location"] as JObject;
        c.LocArea.Text = GetStr(loc, "Area", "area");
        c.LocStreet.Text = GetStr(loc, "Street", "street");
        c.LocCounty.Text = GetStr(loc, "County", "county");
        c.LocPostal.Text = GetStr(loc, "Postal", "postal");
    }

    public static void WriteBase(JObject root, ReportFormBaseControls c)
    {
        root["Id"] = c.Id.Text.Trim();
        root["ShortYear"] = int.TryParse(c.ShortYear.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sy)
            ? sy
            : (c.Date.SelectedDate?.Year % 100 ?? DateTime.Now.Year % 100);
        root["TimeStamp"] = CombineDateTime(c.Date.SelectedDate, c.Time.Text);
        root["Status"] = c.Status.SelectedIndex >= 0 ? c.Status.SelectedIndex : 1;
        root["Notes"] = c.Notes.Text.Trim();

        var off = new JObject
        {
            ["firstName"] = c.OffFirst.Text.Trim(),
            ["lastName"] = c.OffLast.Text.Trim(),
            ["rank"] = c.OffRank.Text.Trim(),
            ["callSign"] = c.OffCall.Text.Trim(),
            ["agency"] = c.OffAgency.Text.Trim()
        };
        var badgeTrim = c.OffBadge.Text.Trim();
        off["badgeNumber"] = int.TryParse(badgeTrim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bn)
            ? (JToken)new JValue(bn)
            : JValue.CreateNull();

        root["OfficerInformation"] = off;
        root["Location"] = new JObject
        {
            ["Area"] = c.LocArea.Text.Trim(),
            ["Street"] = c.LocStreet.Text.Trim(),
            ["County"] = c.LocCounty.Text.Trim(),
            ["Postal"] = c.LocPostal.Text.Trim()
        };
    }

    public static void ClearBase(ReportFormBaseControls c, int defaultStatusIndex = 1)
    {
        var now = DateTime.Now;
        c.Id.Text = "";
        c.ShortYear.Text = (now.Year % 100).ToString(CultureInfo.InvariantCulture);
        c.Date.SelectedDate = now.Date;
        c.Time.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        c.Status.SelectedIndex = defaultStatusIndex is >= 0 and <= 3 ? defaultStatusIndex : 1;
        c.Notes.Text = "";
        c.OffFirst.Text = c.OffLast.Text = c.OffBadge.Text = c.OffRank.Text = c.OffCall.Text = c.OffAgency.Text = "";
        c.LocArea.Text = c.LocStreet.Text = c.LocCounty.Text = c.LocPostal.Text = "";
    }

    public static DateTime CombineDateTime(DateTime? datePart, string? timeText)
    {
        var d = datePart?.Date ?? DateTime.Today;
        var tod = ParseTimeOfDay(timeText) ?? DateTime.Now.TimeOfDay;
        return DateTime.SpecifyKind(d + tod, DateTimeKind.Local);
    }

    static TimeSpan? ParseTimeOfDay(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out var ts)) return ts;
        foreach (var fmt in new[] { "HH:mm:ss", "H:mm:ss", "HH:mm", "H:mm" })
        {
            if (DateTime.TryParseExact(t, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.TimeOfDay;
        }
        return null;
    }

    static string GetStr(JObject? root, params string[] keys)
    {
        if (root == null) return "";
        foreach (var key in keys)
        {
            var tok = root[key];
            if (tok != null && tok.Type != JTokenType.Null) return tok.ToString();
        }
        return "";
    }

    static int ReadIntFlexible(JToken? t, int defaultValue)
    {
        if (t == null || t.Type == JTokenType.Null) return defaultValue;
        if (t.Type == JTokenType.Integer) return t.Value<int>();
        if (int.TryParse(t.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        return defaultValue;
    }

    static DateTime ReadDateTime(JToken? t, DateTime fallback)
    {
        if (t == null || t.Type == JTokenType.Null) return fallback;
        var s = t.ToString();
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)) return dt;
        if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt)) return dt;
        return fallback;
    }

    public static bool ReadBool(JToken? t, bool defaultValue = false)
    {
        if (t == null || t.Type == JTokenType.Null) return defaultValue;
        if (t.Type == JTokenType.Boolean) return t.Value<bool>();
        if (bool.TryParse(t.ToString(), out var b)) return b;
        return defaultValue;
    }
}
