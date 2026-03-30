using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

public partial class IncidentReportForm : UserControl, IReportFormPane
{
    const string TitleKey = "incidentTitle";
    const string DefaultDocTitle = "General Incident Report (IR)";

    JObject? _source;

    public IncidentReportForm()
    {
        InitializeComponent();
        StatusCombo.ItemsSource = new[]
        {
            "Closed (0)",
            "Open (1)",
            "Canceled (2)",
            "Pending (3)"
        };
        StatusCombo.SelectedIndex = 1;
        ClearFormBtn.Click += (_, _) => Clear();
        ExportPdfBtn.Click += (_, _) =>
            ReportDocumentBrandingHelper.PrintToPdf(DocumentBodyScroll, DocumentPrintRoot,
                "MDT Incident " + (IdBox.Text.Trim().Length > 0 ? IdBox.Text.Trim() : "report"));
        ReportDocumentBrandingHelper.ApplyChrome(null, TitleKey, DefaultDocTitle, DocHeader, BrandingTemplateHint, "offline", BrandingFooter);
        NearbyVehicleBar.VehicleDetailReady += (_, vehicle) => ApplyVehicleSnapshot(vehicle);
        ReportFormCopyButtons.Wire(CopyReportIdBtn, IdBox);
        ReportFormCopyButtons.Wire(CopyOffenderNamesBtn, OffenderNamesBox);
        ReportFormCopyButtons.Wire(CopyWitnessNamesBtn, WitnessNamesBox);
    }

    void ApplyVehicleSnapshot(JObject vehicle)
    {
        var s = ReportMdtVehicleSnapshot.FromVehicleJson(vehicle);
        if (s == null) return;
        if (!string.IsNullOrEmpty(s.Owner) && !string.IsNullOrEmpty(s.Plate))
            ReportFormMultilineMerge.AppendLineIfMissing(OffenderNamesBox, $"{s.Owner} (vehicle {s.Plate})");
        else if (!string.IsNullOrEmpty(s.Owner))
            ReportFormMultilineMerge.AppendLineIfMissing(OffenderNamesBox, s.Owner);
        else if (!string.IsNullOrEmpty(s.Plate))
            ReportFormMultilineMerge.AppendLineIfMissing(OffenderNamesBox, $"Vehicle — plate {s.Plate}");
    }

    public void Bind(MdtConnectionManager? connection)
    {
        NearbyVehicleBar.Bind(connection);
        if (connection?.Http == null)
        {
            ReportDocumentBrandingHelper.ApplyChrome(null, TitleKey, DefaultDocTitle, DocHeader, BrandingTemplateHint, "offline", BrandingFooter);
            return;
        }

        _ = ReportDocumentBrandingHelper.LoadBrandingAsync(connection, "incident", TitleKey, DefaultDocTitle, DocHeader, BrandingTemplateHint, Dispatcher, BrandingFooter);
    }

    public void LoadFromReport(JObject report)
    {
        _source = (JObject)report.DeepClone();
        IdBox.Text = report["Id"]?.ToString() ?? "";
        var sy = report["ShortYear"];
        ShortYearBox.Text = sy == null || sy.Type == JTokenType.Null
            ? ""
            : sy.ToString();

        var ts = ReportFormJson.ParseTimestampToken(report["TimeStamp"]);
        TsDatePicker.SelectedDate = ts.Date;
        TsTimeBox.Text = ts.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        var st = ReportFormJson.ParseStatusToken(report["Status"]);
        StatusCombo.SelectedIndex = Math.Clamp(st, 0, 3);

        NotesBox.Text = report["Notes"]?.ToString() ?? "";

        ReportFormJson.ReadOfficer(report["OfficerInformation"], out var f, out var l, out var r, out var c, out var a, out var b);
        OfFirstBox.Text = f;
        OfLastBox.Text = l;
        OfRankBox.Text = r;
        OfCallBox.Text = c;
        OfAgencyBox.Text = a;
        OfBadgeBox.Text = b;

        ReportFormJson.ReadLocation(report["Location"], out var ar, out var stt, out var co, out var po);
        LocAreaBox.Text = ar;
        LocStreetBox.Text = stt;
        LocCountyBox.Text = co;
        LocPostalBox.Text = po;

        OffenderNamesBox.Text = ReportFormJson.JArrayStringsToLines(report["OffenderPedsNames"]);
        WitnessNamesBox.Text = ReportFormJson.JArrayStringsToLines(report["WitnessPedsNames"]);
    }

    public JObject BuildReport()
    {
        var statusIdx = StatusCombo.SelectedIndex is >= 0 and <= 3 ? StatusCombo.SelectedIndex : 1;
        var combined = ReportFormJson.CombineDateAndTime(TsDatePicker.SelectedDate, TsTimeBox.Text);

        return ReportFormJson.MergeOverlay(_source, o =>
        {
            o["Id"] = IdBox.Text.Trim();
            if (int.TryParse(ShortYearBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sy))
                o["ShortYear"] = sy;
            else
                o["ShortYear"] = combined.Year % 100;
            o["TimeStamp"] = combined;
            o["Status"] = statusIdx;
            o["Notes"] = NotesBox.Text ?? "";
            o["OfficerInformation"] = ReportFormJson.BuildOfficerInformation(
                OfFirstBox.Text, OfLastBox.Text, OfRankBox.Text, OfCallBox.Text, OfAgencyBox.Text, OfBadgeBox.Text);
            o["Location"] = ReportFormJson.BuildLocation(LocAreaBox.Text, LocStreetBox.Text, LocCountyBox.Text, LocPostalBox.Text);
            o["OffenderPedsNames"] = ReportFormJson.LinesToStringJArray(OffenderNamesBox.Text);
            o["WitnessPedsNames"] = ReportFormJson.LinesToStringJArray(WitnessNamesBox.Text);
        });
    }

    public void Clear()
    {
        _source = null;
        IdBox.Text = "";
        ShortYearBox.Text = "";
        TsDatePicker.SelectedDate = DateTime.Today;
        TsTimeBox.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        StatusCombo.SelectedIndex = 1;
        NotesBox.Text = "";
        OfFirstBox.Text = OfLastBox.Text = OfRankBox.Text = OfCallBox.Text = OfAgencyBox.Text = OfBadgeBox.Text = "";
        LocAreaBox.Text = LocStreetBox.Text = LocCountyBox.Text = LocPostalBox.Text = "";
        OffenderNamesBox.Text = WitnessNamesBox.Text = "";
    }
}
