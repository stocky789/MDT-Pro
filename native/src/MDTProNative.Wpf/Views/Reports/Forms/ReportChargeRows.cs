using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

/// <summary>Editable citation charge row (serializes to plugin <c>CitationReport.Charge</c> field names).</summary>
public sealed class CitationChargeRow : INotifyPropertyChanged
{
    string _chargeName = "";
    int _minFine;
    int _maxFine;
    bool _canRevokeLicense;
    bool _isArrestable;
    bool _addedByReportInEdit;

    public string ChargeName
    {
        get => _chargeName;
        set { if (_chargeName == value) return; _chargeName = value; OnPropertyChanged(); }
    }

    public int MinFine
    {
        get => _minFine;
        set { if (_minFine == value) return; _minFine = value; OnPropertyChanged(); }
    }

    public int MaxFine
    {
        get => _maxFine;
        set { if (_maxFine == value) return; _maxFine = value; OnPropertyChanged(); }
    }

    public bool CanRevokeLicense
    {
        get => _canRevokeLicense;
        set { if (_canRevokeLicense == value) return; _canRevokeLicense = value; OnPropertyChanged(); }
    }

    public bool IsArrestable
    {
        get => _isArrestable;
        set { if (_isArrestable == value) return; _isArrestable = value; OnPropertyChanged(); }
    }

    public bool AddedByReportInEdit
    {
        get => _addedByReportInEdit;
        set { if (_addedByReportInEdit == value) return; _addedByReportInEdit = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static ObservableCollection<CitationChargeRow> CollectionFromCharges(JToken? tok)
    {
        var col = new ObservableCollection<CitationChargeRow>();
        if (tok is not JArray arr) return col;
        foreach (var item in arr.OfType<JObject>())
            col.Add(FromJObject(item));
        return col;
    }

    public static JArray ToJArray(IEnumerable<CitationChargeRow> rows)
    {
        var a = new JArray();
        foreach (var r in rows)
        {
            var name = r.ChargeName?.Trim() ?? "";
            if (name.Length == 0 && r.MinFine == 0 && r.MaxFine == 0) continue;
            a.Add(ToJObject(r));
        }
        return a;
    }

    internal static CitationChargeRow FromJObject(JObject o) => new()
    {
        ChargeName = PickStr(o, "name", "Name"),
        MinFine = PickInt(o, "minFine", "MinFine"),
        MaxFine = PickInt(o, "maxFine", "MaxFine"),
        CanRevokeLicense = PickBool(o, "canRevokeLicense", "CanRevokeLicense"),
        IsArrestable = PickBool(o, "isArrestable", "IsArrestable", defaultValue: true),
        AddedByReportInEdit = PickBool(o, "addedByReportInEdit", "AddedByReportInEdit"),
    };

    static JObject ToJObject(CitationChargeRow r) => new()
    {
        ["name"] = r.ChargeName?.Trim() ?? "",
        ["minFine"] = r.MinFine,
        ["maxFine"] = r.MaxFine,
        ["canRevokeLicense"] = r.CanRevokeLicense,
        ["isArrestable"] = r.IsArrestable,
        ["addedByReportInEdit"] = r.AddedByReportInEdit,
    };

    static string PickStr(JObject o, params string[] keys)
    {
        foreach (var k in keys)
        {
            var t = o[k];
            if (t != null && t.Type != JTokenType.Null)
                return t.ToString();
        }
        return "";
    }

    static int PickInt(JObject o, params string[] keys)
    {
        foreach (var k in keys)
        {
            var t = o[k];
            if (t == null || t.Type == JTokenType.Null) continue;
            if (t.Type == JTokenType.Integer) return t.Value<int>();
            if (int.TryParse(t.ToString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return n;
        }
        return 0;
    }

    static bool PickBool(JObject o, string key1, string key2, bool defaultValue = false)
    {
        foreach (var k in new[] { key1, key2 })
        {
            var t = o[k];
            if (t == null || t.Type == JTokenType.Null) continue;
            if (t.Type == JTokenType.Boolean) return t.Value<bool>();
            if (bool.TryParse(t.ToString(), out var b)) return b;
            if (int.TryParse(t.ToString(), out var i)) return i != 0;
        }
        return defaultValue;
    }
}

/// <summary>Editable arrest charge row (plugin <c>ArrestReport.Charge</c>).</summary>
public sealed class ArrestChargeRow : INotifyPropertyChanged
{
    string _chargeName = "";
    int _minFine;
    int _maxFine;
    bool _canRevokeLicense = true;
    bool _isArrestable = true;
    bool _addedByReportInEdit;
    int _minDays;
    string _maxDaysText = "";
    double _probation;
    bool _canBeWarrant;

    public string ChargeName
    {
        get => _chargeName;
        set { if (_chargeName == value) return; _chargeName = value; OnPropertyChanged(); }
    }

    public int MinFine
    {
        get => _minFine;
        set { if (_minFine == value) return; _minFine = value; OnPropertyChanged(); }
    }

    public int MaxFine
    {
        get => _maxFine;
        set { if (_maxFine == value) return; _maxFine = value; OnPropertyChanged(); }
    }

    public bool CanRevokeLicense
    {
        get => _canRevokeLicense;
        set { if (_canRevokeLicense == value) return; _canRevokeLicense = value; OnPropertyChanged(); }
    }

    public bool IsArrestable
    {
        get => _isArrestable;
        set { if (_isArrestable == value) return; _isArrestable = value; OnPropertyChanged(); }
    }

    public bool AddedByReportInEdit
    {
        get => _addedByReportInEdit;
        set { if (_addedByReportInEdit == value) return; _addedByReportInEdit = value; OnPropertyChanged(); }
    }

    public int MinDays
    {
        get => _minDays;
        set { if (_minDays == value) return; _minDays = value; OnPropertyChanged(); }
    }

    public string MaxDaysText
    {
        get => _maxDaysText;
        set { if (_maxDaysText == value) return; _maxDaysText = value; OnPropertyChanged(); }
    }

    public double Probation
    {
        get => _probation;
        set { if (Math.Abs(_probation - value) < 1e-9) return; _probation = value; OnPropertyChanged(); }
    }

    public bool CanBeWarrant
    {
        get => _canBeWarrant;
        set { if (_canBeWarrant == value) return; _canBeWarrant = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static ObservableCollection<ArrestChargeRow> CollectionFromCharges(JToken? tok)
    {
        var col = new ObservableCollection<ArrestChargeRow>();
        if (tok is not JArray arr) return col;
        foreach (var item in arr.OfType<JObject>())
            col.Add(FromJObject(item));
        return col;
    }

    public static JArray ToJArray(IEnumerable<ArrestChargeRow> rows)
    {
        var a = new JArray();
        foreach (var r in rows)
        {
            var name = r.ChargeName?.Trim() ?? "";
            if (name.Length == 0 && r.MinFine == 0 && r.MaxFine == 0 && r.MinDays == 0 && string.IsNullOrWhiteSpace(r.MaxDaysText))
                continue;
            a.Add(ToJObject(r));
        }
        return a;
    }

    static ArrestChargeRow FromJObject(JObject o)
    {
        var c = CitationChargeRow.FromJObject(o);
        var r = new ArrestChargeRow
        {
            ChargeName = c.ChargeName,
            MinFine = c.MinFine,
            MaxFine = c.MaxFine,
            CanRevokeLicense = c.CanRevokeLicense,
            IsArrestable = c.IsArrestable,
            AddedByReportInEdit = c.AddedByReportInEdit,
            MinDays = PickInt(o, "minDays", "MinDays"),
            Probation = PickDouble(o, "probation", "Probation"),
            CanBeWarrant = PickBoolExtra(o, "canBeWarrant", "CanBeWarrant"),
        };
        var md = o["maxDays"] ?? o["MaxDays"];
        if (md == null || md.Type == JTokenType.Null)
            r.MaxDaysText = "";
        else if (md.Type == JTokenType.Integer)
            r.MaxDaysText = md.Value<int>().ToString(CultureInfo.InvariantCulture);
        else
            r.MaxDaysText = md.ToString();
        return r;
    }

    static JObject ToJObject(ArrestChargeRow r)
    {
        var o = new JObject
        {
            ["name"] = r.ChargeName?.Trim() ?? "",
            ["minFine"] = r.MinFine,
            ["maxFine"] = r.MaxFine,
            ["canRevokeLicense"] = r.CanRevokeLicense,
            ["isArrestable"] = r.IsArrestable,
            ["addedByReportInEdit"] = r.AddedByReportInEdit,
            ["minDays"] = r.MinDays,
            ["probation"] = r.Probation,
            ["canBeWarrant"] = r.CanBeWarrant,
        };
        var maxTxt = r.MaxDaysText?.Trim() ?? "";
        o["maxDays"] = string.IsNullOrEmpty(maxTxt)
            ? JValue.CreateNull()
            : int.TryParse(maxTxt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mx)
                ? mx
                : JValue.CreateNull();
        return o;
    }

    static int PickInt(JObject o, string k1, string k2)
    {
        foreach (var k in new[] { k1, k2 })
        {
            var t = o[k];
            if (t == null || t.Type == JTokenType.Null) continue;
            if (t.Type == JTokenType.Integer) return t.Value<int>();
            if (int.TryParse(t.ToString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return n;
        }
        return 0;
    }

    static double PickDouble(JObject o, string k1, string k2)
    {
        foreach (var k in new[] { k1, k2 })
        {
            var t = o[k];
            if (t == null || t.Type == JTokenType.Null) continue;
            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer)
                return t.Value<double>();
            if (double.TryParse(t.ToString().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;
        }
        return 0;
    }

    static bool PickBoolExtra(JObject o, string k1, string k2)
    {
        foreach (var k in new[] { k1, k2 })
        {
            var t = o[k];
            if (t == null || t.Type == JTokenType.Null) continue;
            if (t.Type == JTokenType.Boolean) return t.Value<bool>();
            if (bool.TryParse(t.ToString(), out var b)) return b;
        }
        return false;
    }
}
