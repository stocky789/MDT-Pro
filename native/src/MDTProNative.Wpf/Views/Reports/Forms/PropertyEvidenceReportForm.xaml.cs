using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Reports;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

public sealed class SeizedDrugRow
{
    public string DrugType { get; set; } = "";
    public string Quantity { get; set; } = "";
}

public partial class PropertyEvidenceReportForm : UserControl, IReportFormPane
{
    readonly ObservableCollection<SeizedDrugRow> _drugs = new();

    public PropertyEvidenceReportForm()
    {
        InitializeComponent();
        SeizedDrugsGrid.ItemsSource = _drugs;
        AddDrugRowBtn.Click += (_, _) => _drugs.Add(new SeizedDrugRow());
        RemoveDrugRowBtn.Click += (_, _) =>
        {
            if (SeizedDrugsGrid.SelectedItem is SeizedDrugRow r)
                _drugs.Remove(r);
        };
    }

    ReportFormBaseControls BaseControls => new()
    {
        Id = GeneralIdBox,
        ShortYear = GeneralShortYearBox,
        Date = GeneralDatePicker,
        Time = GeneralTimeBox,
        Status = StatusCombo,
        Notes = NotesBox,
        OffFirst = OffFirstBox,
        OffLast = OffLastBox,
        OffBadge = OffBadgeBox,
        OffRank = OffRankBox,
        OffCall = OffCallBox,
        OffAgency = OffAgencyBox,
        LocArea = LocAreaBox,
        LocStreet = LocStreetBox,
        LocCounty = LocCountyBox,
        LocPostal = LocPostalBox
    };

    public void Bind(MdtConnectionManager? connection) { }

    public void LoadFromReport(JObject report)
    {
        ReportFormPaneFields.LoadBase(report, BaseControls);
        SubjectPedNamesBox.Text = ReportFormPaneFields.StringArrayToLines(report["SubjectPedNames"]);
        if (string.IsNullOrWhiteSpace(SubjectPedNamesBox.Text) && report["SubjectPedName"] != null)
        {
            var legacy = report["SubjectPedName"]?.ToString();
            if (!string.IsNullOrWhiteSpace(legacy))
                SubjectPedNamesBox.Text = legacy;
        }

        LoadDrugsIntoCollection(report["SeizedDrugs"], _drugs);

        SeizedFirearmTypesBox.Text = ReportFormPaneFields.StringArrayToLines(report["SeizedFirearmTypes"]);
        OtherContrabandNotesBox.Text = report["OtherContrabandNotes"]?.ToString() ?? "";
    }

    static void LoadDrugsIntoCollection(JToken? tok, ObservableCollection<SeizedDrugRow> col)
    {
        col.Clear();
        if (tok is not JArray arr) return;
        foreach (var item in arr.OfType<JObject>())
        {
            var dt = item["DrugType"]?.ToString() ?? item["drugType"]?.ToString() ?? "";
            var q = item["Quantity"]?.ToString() ?? item["quantity"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(dt) && string.IsNullOrWhiteSpace(q)) continue;
            col.Add(new SeizedDrugRow { DrugType = dt, Quantity = q });
        }
    }

    public JObject BuildReport()
    {
        var root = new JObject();
        ReportFormPaneFields.WriteBase(root, BaseControls);

        var subjectNames = ReportFormPaneFields.LinesToStringArray(SubjectPedNamesBox.Text);
        root["SubjectPedNames"] = subjectNames;
        root["SubjectPedName"] = subjectNames.Count > 0 ? subjectNames[0] : JValue.CreateNull();

        var drugs = DrugsToJArray(_drugs);
        root["SeizedDrugs"] = drugs;
        var types = new JArray();
        foreach (var o in drugs.OfType<JObject>())
        {
            var dt = o["DrugType"]?.ToString();
            if (!string.IsNullOrWhiteSpace(dt)) types.Add(dt.Trim());
        }
        root["SeizedDrugTypes"] = types;

        root["SeizedFirearmTypes"] = ReportFormPaneFields.LinesToStringArray(SeizedFirearmTypesBox.Text);

        var notes = OtherContrabandNotesBox.Text.Trim();
        root["OtherContrabandNotes"] = string.IsNullOrEmpty(notes) ? JValue.CreateNull() : notes;

        return root;
    }

    static JArray DrugsToJArray(IEnumerable<SeizedDrugRow> rows)
    {
        var a = new JArray();
        foreach (var r in rows)
        {
            var dt = r.DrugType?.Trim() ?? "";
            if (string.IsNullOrEmpty(dt)) continue;
            a.Add(new JObject
            {
                ["DrugType"] = dt,
                ["Quantity"] = r.Quantity?.Trim() ?? ""
            });
        }
        return a;
    }

    public void Clear()
    {
        ReportFormPaneFields.ClearBase(BaseControls, 1);
        SubjectPedNamesBox.Text = "";
        _drugs.Clear();
        SeizedFirearmTypesBox.Text = "";
        OtherContrabandNotesBox.Text = "";
    }
}
