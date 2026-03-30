using System.Windows.Controls;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Reports;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

public partial class PropertyEvidenceReportForm : UserControl, IReportFormPane
{
    public PropertyEvidenceReportForm()
    {
        InitializeComponent();
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

        var drugsTok = report["SeizedDrugs"];
        if (drugsTok is JArray drugArr && drugArr.Count > 0)
            SeizedDrugsJsonBox.Text = drugArr.ToString(Formatting.Indented);
        else
            SeizedDrugsJsonBox.Text = "[]";

        SeizedFirearmTypesBox.Text = ReportFormPaneFields.StringArrayToLines(report["SeizedFirearmTypes"]);
        OtherContrabandNotesBox.Text = report["OtherContrabandNotes"]?.ToString() ?? "";
    }

    public JObject BuildReport()
    {
        var root = new JObject();
        ReportFormPaneFields.WriteBase(root, BaseControls);

        var subjectNames = ReportFormPaneFields.LinesToStringArray(SubjectPedNamesBox.Text);
        root["SubjectPedNames"] = subjectNames;
        root["SubjectPedName"] = subjectNames.Count > 0 ? subjectNames[0] : JValue.CreateNull();

        var drugs = ParseSeizedDrugsJson(SeizedDrugsJsonBox.Text);
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

    static JArray ParseSeizedDrugsJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new JArray();
        try
        {
            var tok = JToken.Parse(text.Trim());
            if (tok is not JArray arr) return new JArray();
            var outArr = new JArray();
            foreach (var item in arr)
            {
                if (item is not JObject src) continue;
                var drugType = src["DrugType"]?.ToString() ?? src["drugType"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(drugType)) continue;
                var qty = src["Quantity"]?.ToString() ?? src["quantity"]?.ToString() ?? "";
                outArr.Add(new JObject
                {
                    ["DrugType"] = drugType.Trim(),
                    ["Quantity"] = qty.Trim()
                });
            }
            return outArr;
        }
        catch
        {
            return new JArray();
        }
    }

    public void Clear()
    {
        ReportFormPaneFields.ClearBase(BaseControls, 1);
        SubjectPedNamesBox.Text = "";
        SeizedDrugsJsonBox.Text = "[]";
        SeizedFirearmTypesBox.Text = "";
        OtherContrabandNotesBox.Text = "";
    }
}
