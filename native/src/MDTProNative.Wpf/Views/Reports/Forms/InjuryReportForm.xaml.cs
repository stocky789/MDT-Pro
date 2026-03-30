using System.Windows.Controls;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Reports;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

public partial class InjuryReportForm : UserControl, IReportFormPane
{
    public InjuryReportForm()
    {
        InitializeComponent();
        ExportPdfBtn.Click += (_, _) =>
            ReportDocumentBrandingHelper.PrintToPdf(DocumentBodyScroll, DocumentPrintRoot,
                "MDT Injury " + (GeneralIdBox.Text.Trim().Length > 0 ? GeneralIdBox.Text.Trim() : "report"));
        ReportDocumentBrandingHelper.ApplyChrome(null, "injuryTitle", "Injury / Medical Incident Report", DocHeader, BrandingTemplateHint, "offline", BrandingFooter);
        ReportFormCopyButtons.Wire(CopyReportIdBtn, GeneralIdBox);
        ReportFormCopyButtons.Wire(CopyInjuredPartyBtn, InjuredPartyNameBox);
        ReportFormCopyButtons.Wire(CopyLinkedReportIdBtn, LinkedReportIdBox);
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

    public void Bind(MdtConnectionManager? connection)
    {
        if (connection?.Http == null)
        {
            ReportDocumentBrandingHelper.ApplyChrome(null, "injuryTitle", "Injury / Medical Incident Report", DocHeader, BrandingTemplateHint, "offline", BrandingFooter);
            return;
        }

        _ = ReportDocumentBrandingHelper.LoadBrandingAsync(connection, "injury", "injuryTitle", "Injury / Medical Incident Report", DocHeader, BrandingTemplateHint, Dispatcher, BrandingFooter);
    }

    public void LoadFromReport(JObject report)
    {
        ReportFormPaneFields.LoadBase(report, BaseControls);
        InjuredPartyNameBox.Text = report["InjuredPartyName"]?.ToString() ?? "";
        InjuryTypeBox.Text = report["InjuryType"]?.ToString() ?? "";
        SeverityBox.Text = report["Severity"]?.ToString() ?? "";
        TreatmentBox.Text = report["Treatment"]?.ToString() ?? "";
        IncidentContextBox.Text = report["IncidentContext"]?.ToString() ?? "";
        LinkedReportIdBox.Text = report["LinkedReportId"]?.ToString() ?? "";

        var snap = report["GameInjurySnapshot"];
        if (snap == null || snap.Type == JTokenType.Null)
            GameInjurySnapshotBox.Text = "";
        else if (snap.Type == JTokenType.String)
            GameInjurySnapshotBox.Text = snap.Value<string>() ?? "";
        else
            GameInjurySnapshotBox.Text = snap.ToString(Formatting.Indented);
    }

    public JObject BuildReport()
    {
        var root = new JObject();
        ReportFormPaneFields.WriteBase(root, BaseControls);
        root["InjuredPartyName"] = InjuredPartyNameBox.Text.Trim();
        root["InjuryType"] = NullIfEmpty(InjuryTypeBox.Text);
        root["Severity"] = NullIfEmpty(SeverityBox.Text);
        root["Treatment"] = NullIfEmpty(TreatmentBox.Text);
        root["IncidentContext"] = NullIfEmpty(IncidentContextBox.Text);
        root["LinkedReportId"] = NullIfEmpty(LinkedReportIdBox.Text);

        var snapRaw = GameInjurySnapshotBox.Text.Trim();
        if (string.IsNullOrEmpty(snapRaw))
            root["GameInjurySnapshot"] = JValue.CreateNull();
        else
        {
            try
            {
                var tok = JToken.Parse(snapRaw);
                root["GameInjurySnapshot"] = snapRaw.StartsWith('{') || snapRaw.StartsWith('[')
                    ? tok.ToString(Formatting.None)
                    : snapRaw;
            }
            catch
            {
                root["GameInjurySnapshot"] = snapRaw;
            }
        }

        return root;
    }

    public void ApplyPersonSearchPrefill(string pedName, string? vehicleLicensePlate)
    {
        if (!string.IsNullOrWhiteSpace(pedName))
            InjuredPartyNameBox.Text = pedName.Trim();
    }

    static JToken NullIfEmpty(string s)
    {
        var t = s.Trim();
        return string.IsNullOrEmpty(t) ? JValue.CreateNull() : t;
    }

    public void Clear()
    {
        ReportFormPaneFields.ClearBase(BaseControls, 1);
        InjuredPartyNameBox.Text = InjuryTypeBox.Text = SeverityBox.Text = TreatmentBox.Text = "";
        IncidentContextBox.Text = LinkedReportIdBox.Text = GameInjurySnapshotBox.Text = "";
    }
}
