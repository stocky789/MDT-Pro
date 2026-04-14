using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

/// <summary>Flattened arrest charge from <c>/arrestOptions</c> (same shape as browser <c>citationArrestSection.js</c> charge buttons).</summary>
public sealed class ArrestChargePickerOption
{
    public string GroupName { get; init; } = "";
    public string ChargeName { get; init; } = "";
    public string DisplayName => string.IsNullOrEmpty(GroupName) ? ChargeName : $"{GroupName} — {ChargeName}";

    public int MinFine { get; init; }
    public int MaxFine { get; init; }
    public int MinDays { get; init; }
    public int? MaxDays { get; init; }
    public double Probation { get; init; }
    public bool CanRevokeLicense { get; init; }
    public bool CanBeWarrant { get; init; }
    public bool IsArrestable { get; init; } = true;

    public void ApplyTo(ArrestChargeRow r)
    {
        r.ChargeName = ChargeName;
        r.MinFine = MinFine;
        r.MaxFine = MaxFine;
        r.MinDays = MinDays;
        r.MaxDaysText = MaxDays.HasValue ? MaxDays.Value.ToString(CultureInfo.InvariantCulture) : "";
        r.Probation = Probation;
        r.CanRevokeLicense = CanRevokeLicense;
        r.CanBeWarrant = CanBeWarrant;
        r.IsArrestable = IsArrestable;
    }

    public static List<ArrestChargePickerOption> ParseGroups(JArray? root)
    {
        var list = new List<ArrestChargePickerOption>();
        if (root == null) return list;
        foreach (var g in root.OfType<JObject>())
        {
            var gn = g["name"]?.ToString() ?? "";
            if (g["charges"] is not JArray charges) continue;
            foreach (var c in charges.OfType<JObject>())
            {
                var name = c["name"]?.ToString()?.Trim() ?? "";
                if (name.Length == 0) continue;
                int? maxDays = null;
                var md = c["maxDays"];
                if (md != null && md.Type != JTokenType.Null && md.Type != JTokenType.Undefined)
                {
                    if (md.Type == JTokenType.Integer) maxDays = md.Value<int>();
                    else if (int.TryParse(md.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mx))
                        maxDays = mx;
                }

                var ia = c["isArrestable"];
                var isArrestable = true;
                if (ia != null && ia.Type != JTokenType.Null)
                {
                    if (ia.Type == JTokenType.Boolean) isArrestable = ia.Value<bool>();
                    else if (bool.TryParse(ia.ToString(), out var b)) isArrestable = b;
                }

                list.Add(new ArrestChargePickerOption
                {
                    GroupName = gn,
                    ChargeName = name,
                    MinFine = PickInt(c, "minFine"),
                    MaxFine = PickInt(c, "maxFine"),
                    MinDays = PickInt(c, "minDays"),
                    MaxDays = maxDays,
                    Probation = PickDouble(c, "probation"),
                    CanRevokeLicense = PickBool(c, "canRevokeLicense"),
                    CanBeWarrant = PickBool(c, "canBeWarrant"),
                    IsArrestable = isArrestable
                });
            }
        }
        return list;
    }

    static int PickInt(JObject o, string key)
    {
        var t = o[key];
        if (t == null || t.Type == JTokenType.Null) return 0;
        if (t.Type == JTokenType.Integer) return t.Value<int>();
        return int.TryParse(t.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    static double PickDouble(JObject o, string key)
    {
        var t = o[key];
        if (t == null || t.Type == JTokenType.Null) return 0;
        if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) return t.Value<double>();
        return double.TryParse(t.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    static bool PickBool(JObject o, string key)
    {
        var t = o[key];
        if (t == null || t.Type == JTokenType.Null) return false;
        if (t.Type == JTokenType.Boolean) return t.Value<bool>();
        return bool.TryParse(t.ToString(), out var b) && b;
    }
}

/// <summary>Flattened citation charge from <c>/citationOptions</c>.</summary>
public sealed class CitationChargePickerOption
{
    public string GroupName { get; init; } = "";
    public string ChargeName { get; init; } = "";
    public string DisplayName => string.IsNullOrEmpty(GroupName) ? ChargeName : $"{GroupName} — {ChargeName}";

    public int MinFine { get; init; }
    public int MaxFine { get; init; }
    public bool CanRevokeLicense { get; init; }
    public bool IsArrestable { get; init; }

    public void ApplyTo(CitationChargeRow r)
    {
        r.ChargeName = ChargeName;
        r.MinFine = MinFine;
        r.MaxFine = MaxFine;
        r.CanRevokeLicense = CanRevokeLicense;
        r.IsArrestable = IsArrestable;
    }

    public static List<CitationChargePickerOption> ParseGroups(JArray? root)
    {
        var list = new List<CitationChargePickerOption>();
        if (root == null) return list;
        foreach (var g in root.OfType<JObject>())
        {
            var gn = g["name"]?.ToString() ?? "";
            if (g["charges"] is not JArray charges) continue;
            foreach (var c in charges.OfType<JObject>())
            {
                var name = c["name"]?.ToString()?.Trim() ?? "";
                if (name.Length == 0) continue;
                var ia = c["isArrestable"];
                var isArrestable = false;
                if (ia != null && ia.Type != JTokenType.Null)
                {
                    if (ia.Type == JTokenType.Boolean) isArrestable = ia.Value<bool>();
                    else if (bool.TryParse(ia.ToString(), out var b)) isArrestable = b;
                }
                list.Add(new CitationChargePickerOption
                {
                    GroupName = gn,
                    ChargeName = name,
                    MinFine = PickInt(c, "minFine"),
                    MaxFine = PickInt(c, "maxFine"),
                    CanRevokeLicense = PickBool(c, "canRevokeLicense"),
                    IsArrestable = isArrestable
                });
            }
        }
        return list;
    }

    static int PickInt(JObject o, string key)
    {
        var t = o[key];
        if (t == null || t.Type == JTokenType.Null) return 0;
        if (t.Type == JTokenType.Integer) return t.Value<int>();
        return int.TryParse(t.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    static bool PickBool(JObject o, string key)
    {
        var t = o[key];
        if (t == null || t.Type == JTokenType.Null) return false;
        if (t.Type == JTokenType.Boolean) return t.Value<bool>();
        return bool.TryParse(t.ToString(), out var b) && b;
    }
}
