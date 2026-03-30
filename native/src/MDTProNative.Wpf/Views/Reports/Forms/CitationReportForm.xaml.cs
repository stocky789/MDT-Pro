using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MDTProNative.Client;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Reports;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

public partial class CitationReportForm : UserControl, IReportFormPane
{
    const string TitleKey = "citationTitle";
    const string DefaultDocTitle = "Uniform Traffic Citation — Violation Notice";

    JObject? _source;
    MdtConnectionManager? _connection;
    readonly ObservableCollection<CitationChargeRow> _charges = new();
    readonly List<CitationChargePickerOption> _citationChargePickList = new();
    bool _suppressCitationChargePick;

    public CitationReportForm()
    {
        InitializeComponent();
        ChargesGrid.ItemsSource = _charges;
        AddChargeBtn.Click += (_, _) => _charges.Add(new CitationChargeRow { AddedByReportInEdit = true });
        RemoveChargeBtn.Click += (_, _) =>
        {
            if (ChargesGrid.SelectedItem is CitationChargeRow r)
                _charges.Remove(r);
        };
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
                "MDT Citation " + (IdBox.Text.Trim().Length > 0 ? IdBox.Text.Trim() : "report"));
        ReportDocumentBrandingHelper.ApplyChrome(null, TitleKey, DefaultDocTitle, DocHeader, BrandingTemplateHint, "offline", BrandingFooter);
        NearbyVehicleBar.VehicleDetailReady += (_, vehicle) => ApplyVehicleSnapshot(vehicle);
        ReportFormCopyButtons.Wire(CopyReportIdBtn, IdBox);
        ReportFormCopyButtons.Wire(CopyOffenderPedBtn, OffenderPedBox);
        ReportFormCopyButtons.Wire(CopyOffenderPlateBtn, OffenderPlateBox);
    }

    void ApplyVehicleSnapshot(JObject vehicle)
    {
        var s = ReportMdtVehicleSnapshot.FromVehicleJson(vehicle);
        if (s == null) return;
        if (!string.IsNullOrEmpty(s.Plate)) OffenderPlateBox.Text = s.Plate;
        if (string.IsNullOrWhiteSpace(OffenderPedBox.Text) && !string.IsNullOrEmpty(s.Owner))
            OffenderPedBox.Text = s.Owner;
    }

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        NearbyVehicleBar.Bind(connection);
        if (connection?.Http == null)
        {
            _citationChargePickList.Clear();
            ReportDocumentBrandingHelper.ApplyChrome(null, TitleKey, DefaultDocTitle, DocHeader, BrandingTemplateHint, "offline", BrandingFooter);
            return;
        }

        _ = ReportDocumentBrandingHelper.LoadBrandingAsync(connection, "citation", TitleKey, DefaultDocTitle, DocHeader, BrandingTemplateHint, Dispatcher, BrandingFooter);
        _ = LoadCitationChargePickListAsync(connection.Http);
    }

    async Task LoadCitationChargePickListAsync(MdtHttpClient http)
    {
        try
        {
            var arr = await http.GetCitationOptionsJsonAsync().ConfigureAwait(false);
            var list = CitationChargePickerOption.ParseGroups(arr);
            await Dispatcher.InvokeAsync(() =>
            {
                _citationChargePickList.Clear();
                _citationChargePickList.AddRange(list);
                CollectionViewSource.GetDefaultView(_charges).Refresh();
            });
        }
        catch
        {
            /* keep empty */
        }
    }

    void CitationChargeCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb)
            cb.ItemsSource = _citationChargePickList;
    }

    void CitationChargeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCitationChargePick) return;
        if (sender is not ComboBox cb || cb.DataContext is not CitationChargeRow row) return;
        if (cb.SelectedItem is not CitationChargePickerOption opt) return;
        opt.ApplyTo(row);
        _suppressCitationChargePick = true;
        try
        {
            cb.SelectedItem = null;
        }
        finally
        {
            _suppressCitationChargePick = false;
        }
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

        OffenderPedBox.Text = report["OffenderPedName"]?.ToString() ?? "";
        OffenderPlateBox.Text = report["OffenderVehicleLicensePlate"]?.ToString() ?? "";
        var cc = report["CourtCaseNumber"];
        CourtCaseBox.Text = cc == null || cc.Type == JTokenType.Null ? "" : cc.ToString();
        _charges.Clear();
        foreach (var row in CitationChargeRow.CollectionFromCharges(report["Charges"]))
            _charges.Add(row);
    }

    public JObject BuildReport()
    {
        var statusIdx = StatusCombo.SelectedIndex is >= 0 and <= 3 ? StatusCombo.SelectedIndex : 1;
        var combined = ReportFormJson.CombineDateAndTime(TsDatePicker.SelectedDate, TsTimeBox.Text);
        var charges = CitationChargeRow.ToJArray(_charges);

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
            o["OffenderPedName"] = OffenderPedBox.Text ?? "";
            o["OffenderVehicleLicensePlate"] = OffenderPlateBox.Text ?? "";
            var ccTrim = CourtCaseBox.Text?.Trim();
            o["CourtCaseNumber"] = string.IsNullOrEmpty(ccTrim) ? JValue.CreateNull() : ccTrim;
            o["Charges"] = charges;
        });
    }

    public void ApplyPersonSearchPrefill(string pedName, string? vehicleLicensePlate)
    {
        if (!string.IsNullOrWhiteSpace(pedName))
            OffenderPedBox.Text = pedName.Trim();
        if (!string.IsNullOrWhiteSpace(vehicleLicensePlate))
            OffenderPlateBox.Text = vehicleLicensePlate.Trim();
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
        _charges.Clear();
    }
}
