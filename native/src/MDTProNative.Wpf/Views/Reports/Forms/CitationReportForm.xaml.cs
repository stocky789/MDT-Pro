using System.Globalization;
using System.Windows.Controls;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Reports;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

public partial class CitationReportForm : UserControl, IReportFormPane
{
    JObject? _source;
    MdtConnectionManager? _connection;

    public CitationReportForm()
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
    }

    public void Bind(MdtConnectionManager? connection) => _connection = connection;

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

        OffenderPedBox.Text = report["OffenderPedName"]?.ToString() ?? "";
        OffenderPlateBox.Text = report["OffenderVehicleLicensePlate"]?.ToString() ?? "";
        var cc = report["CourtCaseNumber"];
        CourtCaseBox.Text = cc == null || cc.Type == JTokenType.Null ? "" : cc.ToString();
        ChargesBox.Text = ReportFormJson.ChargesToEditorText(report["Charges"]);
    }

    public JObject BuildReport()
    {
        var statusIdx = StatusCombo.SelectedIndex is >= 0 and <= 3 ? StatusCombo.SelectedIndex : 1;
        var combined = ReportFormJson.CombineDateAndTime(TsDatePicker.SelectedDate, TsTimeBox.Text);
        var charges = ReportFormJson.ParseChargesMultiline(ChargesBox.Text);

        return ReportFormJson.MergeOverlay(_source, o =>
        {
            o["Id"] = IdBox.Text.Trim();
            if (int.TryParse(ShortYearBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sy))
                o["ShortYear"] = sy;
            else
                o["ShortYear"] = 0;
            o["TimeStamp"] = combined;
            o["Status"] = statusIdx;
            o["Notes"] = NotesBox.Text ?? "";
            o["OfficerInformation"] = ReportFormJson.BuildOfficerInformation(
                OfFirstBox.Text, OfLastBox.Text, OfRankBox.Text, OfCallBox.Text, OfAgencyBox.Text, OfBadgeBox.Text);
            o["Location"] = ReportFormJson.BuildLocation(LocAreaBox.Text, LocStreetBox.Text, LocCountyBox.Text, LocPostalBox.Text);
            o["OffenderPedName"] = OffenderPedBox.Text ?? "";
            o["OffenderVehicleLicensePlate"] = OffenderPlateBox.Text ?? "";
            var ccTrim = CourtCaseBox.Text?.Trim();
            o["CourtCaseNumber"] = string.IsNullOrEmpty(ccTrim) ? JValue.CreateNull() : ccTrim;
            o["Charges"] = charges;
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
        OffenderPedBox.Text = OffenderPlateBox.Text = CourtCaseBox.Text = "";
        ChargesBox.Text = "";
    }
}
