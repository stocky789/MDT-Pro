using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MDTProNative.Client;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Reports;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

public partial class ArrestReportForm : UserControl, IReportFormPane
{
    sealed record StatusPick(int Value, string Label);

    readonly ObservableCollection<ArrestChargeRow> _charges = new();
    readonly List<ArrestChargePickerOption> _arrestChargePickList = new();
    MdtConnectionManager? _connection;
    bool _suppressArrestChargePick;

    public ArrestReportForm()
    {
        InitializeComponent();
        ArrestChargesGrid.ItemsSource = _charges;
        AddArrestChargeBtn.Click += (_, _) => _charges.Add(new ArrestChargeRow { AddedByReportInEdit = true });
        RemoveArrestChargeBtn.Click += (_, _) =>
        {
            if (ArrestChargesGrid.SelectedItem is ArrestChargeRow r)
                _charges.Remove(r);
        };

        StatusCombo.ItemsSource = new StatusPick[]
        {
            new(0, "Closed"),
            new(1, "Open"),
            new(2, "Canceled"),
            new(3, "Pending"),
        };
        StatusCombo.DisplayMemberPath = nameof(StatusPick.Label);
        StatusCombo.SelectedValuePath = nameof(StatusPick.Value);
        StatusCombo.SelectedValue = 3;
        UofTypeCombo.SelectedIndex = 0;
        ExportPdfBtn.Click += (_, _) =>
            ReportDocumentBrandingHelper.PrintToPdf(DocumentBodyScroll, DocumentPrintRoot,
                "MDT Arrest " + (IdBox.Text.Trim().Length > 0 ? IdBox.Text.Trim() : "report"));
        ReportDocumentBrandingHelper.ApplyChrome(null, "arrestTitle", "Arrest & Booking Report", DocHeader, BrandingTemplateHint, "offline", BrandingFooter);
        NearbyVehicleBar.VehicleDetailReady += (_, vehicle) => ApplyVehicleSnapshot(vehicle);
        ReportFormCopyButtons.Wire(CopyReportIdBtn, IdBox);
        ReportFormCopyButtons.Wire(CopySubjectNameBtn, OffenderPedNameBox);
        ReportFormCopyButtons.Wire(CopyVehiclePlateBtn, OffenderPlateBox);
    }

    void ApplyVehicleSnapshot(JObject vehicle)
    {
        var s = ReportMdtVehicleSnapshot.FromVehicleJson(vehicle);
        if (s == null) return;
        if (!string.IsNullOrEmpty(s.Plate)) OffenderPlateBox.Text = s.Plate;
        if (string.IsNullOrWhiteSpace(OffenderPedNameBox.Text) && !string.IsNullOrEmpty(s.Owner))
            OffenderPedNameBox.Text = s.Owner;
    }

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        NearbyVehicleBar.Bind(connection);
        if (connection?.Http == null)
        {
            _arrestChargePickList.Clear();
            ReportDocumentBrandingHelper.ApplyChrome(null, "arrestTitle", "Arrest & Booking Report", DocHeader, BrandingTemplateHint, "offline", BrandingFooter);
            return;
        }

        _ = ReportDocumentBrandingHelper.LoadBrandingAsync(connection, "arrest", "arrestTitle", "Arrest & Booking Report", DocHeader, BrandingTemplateHint, Dispatcher, BrandingFooter);
        _ = LoadArrestChargePickListAsync(connection.Http);
    }

    async Task LoadArrestChargePickListAsync(MdtHttpClient http)
    {
        try
        {
            var arr = await http.GetArrestOptionsJsonAsync().ConfigureAwait(false);
            var list = ArrestChargePickerOption.ParseGroups(arr);
            await Dispatcher.InvokeAsync(() =>
            {
                _arrestChargePickList.Clear();
                _arrestChargePickList.AddRange(list);
                CollectionViewSource.GetDefaultView(_charges).Refresh();
            });
        }
        catch
        {
            /* keep list empty; combo remains editable for charge name */
        }
    }

    void ArrestChargeCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb)
            cb.ItemsSource = _arrestChargePickList;
    }

    void ArrestChargeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressArrestChargePick) return;
        if (sender is not ComboBox cb || cb.DataContext is not ArrestChargeRow row) return;
        if (cb.SelectedItem is not ArrestChargePickerOption opt) return;
        opt.ApplyTo(row);
        _suppressArrestChargePick = true;
        try
        {
            cb.SelectedItem = null;
        }
        finally
        {
            _suppressArrestChargePick = false;
        }
    }

    public void LoadFromReport(JObject report)
    {
        IdBox.Text = report["Id"]?.ToString() ?? "";
        ShortYearBox.Text = report["ShortYear"]?.ToString() ?? "";

        var ts = report["TimeStamp"];
        if (ts == null || ts.Type == JTokenType.Null)
            TimeStampBox.Text = "";
        else if (ts.Type == JTokenType.Date)
            TimeStampBox.Text = NativeMdtFormat.FormatDateTimeDisplay(ts.Value<DateTime>());
        else
        {
            var s = ts.ToString();
            TimeStampBox.Text = NativeMdtFormat.TryParseMdtDateTime(s, out var parsed)
                ? NativeMdtFormat.FormatDateTimeDisplay(parsed)
                : s;
        }

        var st = report["Status"]?.Value<int?>();
        if (st is >= 0 and <= 3)
            StatusCombo.SelectedValue = st.Value;
        else
            StatusCombo.SelectedValue = 3;

        NotesBox.Text = report["Notes"]?.ToString() ?? "";

        if (report["OfficerInformation"] is JObject off)
        {
            OfficerFirstBox.Text = off["firstName"]?.ToString() ?? "";
            OfficerLastBox.Text = off["lastName"]?.ToString() ?? "";
            OfficerRankBox.Text = off["rank"]?.ToString() ?? "";
            OfficerCallSignBox.Text = off["callSign"]?.ToString() ?? "";
            OfficerAgencyBox.Text = off["agency"]?.ToString() ?? "";
            OfficerBadgeBox.Text = off["badgeNumber"]?.ToString() ?? "";
        }
        else
        {
            OfficerFirstBox.Text = OfficerLastBox.Text = OfficerRankBox.Text = "";
            OfficerCallSignBox.Text = OfficerAgencyBox.Text = OfficerBadgeBox.Text = "";
        }

        if (report["Location"] is JObject loc)
        {
            LocAreaBox.Text = loc["Area"]?.ToString() ?? "";
            LocStreetBox.Text = loc["Street"]?.ToString() ?? "";
            LocCountyBox.Text = loc["County"]?.ToString() ?? "";
            LocPostalBox.Text = loc["Postal"]?.ToString() ?? "";
        }
        else
        {
            LocAreaBox.Text = LocStreetBox.Text = LocCountyBox.Text = LocPostalBox.Text = "";
        }

        OffenderPedNameBox.Text = report["OffenderPedName"]?.ToString() ?? "";
        OffenderPlateBox.Text = report["OffenderVehicleLicensePlate"]?.ToString() ?? "";
        CourtCaseNumberBox.Text = report["CourtCaseNumber"]?.ToString() ?? "";

        DocumentedDrugsCheck.IsChecked = report["DocumentedDrugs"]?.Value<bool>() == true;
        DocumentedFirearmsCheck.IsChecked = report["DocumentedFirearms"]?.Value<bool>() == true;

        if (report["AttachedReportIds"] is JArray att)
            AttachedReportIdsBox.Text = string.Join(Environment.NewLine, att.Select(t => t.ToString()));
        else
            AttachedReportIdsBox.Text = "";

        _charges.Clear();
        foreach (var row in ArrestChargeRow.CollectionFromCharges(report["Charges"]))
            _charges.Add(row);

        LoadUseOfForce(report["UseOfForce"] as JObject);
    }

    void LoadUseOfForce(JObject? uof)
    {
        UofTypeCombo.SelectedIndex = 0;
        UofTypeOtherBox.Text = "";
        UofJustificationBox.Text = "";
        UofInjurySuspectCheck.IsChecked = false;
        UofInjuryOfficerCheck.IsChecked = false;
        UofWitnessesBox.Text = "";

        if (uof == null || !uof.Properties().Any()) return;

        var typ = uof["Type"]?.ToString() ?? "";
        SelectUofByType(typ);
        UofTypeOtherBox.Text = uof["TypeOther"]?.ToString() ?? "";
        UofJustificationBox.Text = uof["Justification"]?.ToString() ?? "";
        UofInjurySuspectCheck.IsChecked = uof["InjuryToSuspect"]?.Value<bool>() == true;
        UofInjuryOfficerCheck.IsChecked = uof["InjuryToOfficer"]?.Value<bool>() == true;
        UofWitnessesBox.Text = uof["Witnesses"]?.ToString() ?? "";
    }

    void SelectUofByType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            UofTypeCombo.SelectedIndex = 0;
            return;
        }

        foreach (var item in UofTypeCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), type.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                UofTypeCombo.SelectedItem = item;
                return;
            }
        }

        UofTypeCombo.SelectedIndex = 0;
    }

    public JObject BuildReport()
    {
        if (!int.TryParse(ShortYearBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var shortYear))
            shortYear = DateTime.Now.Year % 100;

        var tsText = TimeStampBox.Text.Trim();
        var timeStamp = string.IsNullOrEmpty(tsText) || !NativeMdtFormat.TryParseMdtDateTime(tsText, out var parsedTs)
            ? DateTime.Now
            : parsedTs;

        int? badge = null;
        var badgeTxt = OfficerBadgeBox.Text.Trim();
        if (int.TryParse(badgeTxt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bn))
            badge = bn;

        var status = StatusCombo.SelectedValue is int si ? si : 3;

        var attached = new JArray();
        foreach (var line in AttachedReportIdsBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var id = line.Trim();
            if (id.Length > 0)
                attached.Add(id);
        }

        var charges = ArrestChargeRow.ToJArray(_charges);

        var root = new JObject
        {
            ["Id"] = IdBox.Text.Trim(),
            ["ShortYear"] = shortYear,
            ["TimeStamp"] = timeStamp,
            ["Status"] = status,
            ["Notes"] = NotesBox.Text,
            ["OfficerInformation"] = new JObject
            {
                ["firstName"] = OfficerFirstBox.Text.Trim(),
                ["lastName"] = OfficerLastBox.Text.Trim(),
                ["rank"] = OfficerRankBox.Text.Trim(),
                ["callSign"] = OfficerCallSignBox.Text.Trim(),
                ["agency"] = OfficerAgencyBox.Text.Trim(),
                ["badgeNumber"] = badge.HasValue ? JToken.FromObject(badge.Value) : JValue.CreateNull(),
            },
            ["Location"] = new JObject
            {
                ["Area"] = LocAreaBox.Text.Trim(),
                ["Street"] = LocStreetBox.Text.Trim(),
                ["County"] = LocCountyBox.Text.Trim(),
                ["Postal"] = LocPostalBox.Text.Trim(),
            },
            ["OffenderPedName"] = OffenderPedNameBox.Text.Trim(),
            ["OffenderVehicleLicensePlate"] = OffenderPlateBox.Text.Trim(),
            ["CourtCaseNumber"] = CourtCaseNumberBox.Text.Trim(),
            ["AttachedReportIds"] = attached,
            ["Charges"] = charges,
            ["DocumentedDrugs"] = DocumentedDrugsCheck.IsChecked == true,
            ["DocumentedFirearms"] = DocumentedFirearmsCheck.IsChecked == true,
        };

        var uofTag = (UofTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        if (!string.IsNullOrEmpty(uofTag))
        {
            root["UseOfForce"] = new JObject
            {
                ["Type"] = uofTag,
                ["TypeOther"] = uofTag == "Other" ? UofTypeOtherBox.Text.Trim() : "",
                ["Justification"] = UofJustificationBox.Text.Trim(),
                ["InjuryToSuspect"] = UofInjurySuspectCheck.IsChecked == true,
                ["InjuryToOfficer"] = UofInjuryOfficerCheck.IsChecked == true,
                ["Witnesses"] = UofWitnessesBox.Text.Trim(),
            };
        }

        return root;
    }

    public void ApplyPersonSearchPrefill(string pedName, string? vehicleLicensePlate)
    {
        if (!string.IsNullOrWhiteSpace(pedName))
            OffenderPedNameBox.Text = pedName.Trim();
        if (!string.IsNullOrWhiteSpace(vehicleLicensePlate))
            OffenderPlateBox.Text = vehicleLicensePlate.Trim();
    }

    public void Clear()
    {
        IdBox.Text = ShortYearBox.Text = "";
        TimeStampBox.Text = NativeMdtFormat.FormatDateTimeDisplay(DateTime.Now);
        StatusCombo.SelectedValue = 3;
        NotesBox.Text = "";
        OfficerFirstBox.Text = OfficerLastBox.Text = OfficerRankBox.Text = "";
        OfficerCallSignBox.Text = OfficerAgencyBox.Text = OfficerBadgeBox.Text = "";
        LocAreaBox.Text = LocStreetBox.Text = LocCountyBox.Text = LocPostalBox.Text = "";
        OffenderPedNameBox.Text = OffenderPlateBox.Text = CourtCaseNumberBox.Text = "";
        DocumentedDrugsCheck.IsChecked = false;
        DocumentedFirearmsCheck.IsChecked = false;
        AttachedReportIdsBox.Text = "";
        _charges.Clear();
        LoadUseOfForce(null);
    }
}
