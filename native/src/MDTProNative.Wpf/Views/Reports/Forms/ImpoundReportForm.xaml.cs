using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Reports;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

public partial class ImpoundReportForm : UserControl, IReportFormPane
{
    sealed record StatusPick(int Value, string Label);

    // Match MDTPro/main/scripts/reportSections/impoundSection.js (IMPOUND_LOTS, TOW_COMPANIES, reason select options).
    static readonly string[] ImpoundReasonOptions =
    [
        "",
        "Stolen recovery",
        "Abandoned",
        "Evidence",
        "Traffic violation",
        "No insurance",
        "Other",
    ];

    static readonly string[] TowCompanyOptions = ["", "Camel Towing", "Davis Towing"];

    static readonly string[] ImpoundLotOptions =
    [
        "LSPD Auto Impound — Mission Row (Sinner St & Vespucci Blvd)",
        "LSPD Auto Impound — Davis (Roy Lowenstein Blvd & Innocence Blvd)",
    ];

    public ImpoundReportForm()
    {
        InitializeComponent();
        ImpoundReasonCombo.ItemsSource = ImpoundReasonOptions;
        TowCompanyCombo.ItemsSource = TowCompanyOptions;
        ImpoundLotCombo.ItemsSource = ImpoundLotOptions;
        ImpoundReasonCombo.SelectedIndex = 0;
        TowCompanyCombo.SelectedIndex = 0;
        ImpoundLotCombo.SelectedIndex = Random.Shared.Next(ImpoundLotOptions.Length);
        StatusCombo.ItemsSource = new StatusPick[]
        {
            new(0, "Closed"),
            new(1, "Open"),
            new(2, "Canceled"),
            new(3, "Pending"),
        };
        StatusCombo.DisplayMemberPath = nameof(StatusPick.Label);
        StatusCombo.SelectedValuePath = nameof(StatusPick.Value);
        StatusCombo.SelectedValue = 1;
        ExportPdfBtn.Click += (_, _) =>
            ReportDocumentBrandingHelper.PrintToPdf(DocumentBodyScroll, DocumentPrintRoot,
                "MDT Impound " + (IdBox.Text.Trim().Length > 0 ? IdBox.Text.Trim() : "report"));
        ReportDocumentBrandingHelper.ApplyChrome(null, "impoundTitle", "Vehicle Tow / Impound Report", DocHeader, BrandingTemplateHint, "offline", BrandingFooter);
        NearbyVehicleBar.VehicleDetailReady += (_, vehicle) => ApplyVehicleSnapshot(vehicle);
        ReportFormCopyButtons.Wire(CopyReportIdBtn, IdBox);
        ReportFormCopyButtons.Wire(CopyLicensePlateBtn, LicensePlateBox);
    }

    public void ApplyVehicleSearchPrefill(JObject vehicleSnapshot) => ApplyVehicleSnapshot(vehicleSnapshot);

    void ApplyVehicleSnapshot(JObject vehicle)
    {
        var s = ReportMdtVehicleSnapshot.FromVehicleJson(vehicle);
        if (s == null) return;
        if (!string.IsNullOrEmpty(s.Plate)) LicensePlateBox.Text = s.Plate;
        if (!string.IsNullOrEmpty(s.ModelDisplayName)) VehicleModelBox.Text = s.ModelDisplayName;
        if (!string.IsNullOrEmpty(s.Owner)) OwnerBox.Text = s.Owner;
        if (!string.IsNullOrEmpty(s.Vin)) VinBox.Text = s.Vin;
        if (string.IsNullOrWhiteSpace(PersonAtFaultNameBox.Text) && !string.IsNullOrEmpty(s.Owner))
            PersonAtFaultNameBox.Text = s.Owner;
    }

    public void Bind(MdtConnectionManager? connection)
    {
        NearbyVehicleBar.Bind(connection);
        if (connection?.Http == null)
        {
            ReportDocumentBrandingHelper.ApplyChrome(null, "impoundTitle", "Vehicle Tow / Impound Report", DocHeader, BrandingTemplateHint, "offline", BrandingFooter);
            return;
        }

        _ = ReportDocumentBrandingHelper.LoadBrandingAsync(connection, "impound", "impoundTitle", "Vehicle Tow / Impound Report", DocHeader, BrandingTemplateHint, Dispatcher, BrandingFooter);
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
            StatusCombo.SelectedValue = 1;

        NotesBox.Text = report["Notes"]?.ToString() ?? "";

        ReportFormJson.ReadOfficer(report["OfficerInformation"], out var offFirst, out var offLast, out var offRank, out var offCall, out var offAgency, out var offBadge);
        OfficerFirstBox.Text = offFirst;
        OfficerLastBox.Text = offLast;
        OfficerRankBox.Text = offRank;
        OfficerCallSignBox.Text = offCall;
        OfficerAgencyBox.Text = offAgency;
        OfficerBadgeBox.Text = offBadge;

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

        LicensePlateBox.Text = report["LicensePlate"]?.ToString() ?? "";
        VehicleModelBox.Text = report["VehicleModel"]?.ToString() ?? "";
        OwnerBox.Text = report["Owner"]?.ToString() ?? "";
        PersonAtFaultNameBox.Text = report["PersonAtFaultName"]?.ToString() ?? "";
        VinBox.Text = report["Vin"]?.ToString() ?? "";
        SelectComboFromSaved(ImpoundReasonCombo, ImpoundReasonOptions, report["ImpoundReason"]?.ToString());
        SelectComboFromSaved(TowCompanyCombo, TowCompanyOptions, report["TowCompany"]?.ToString());
        SelectImpoundLotCombo(report["ImpoundLot"]?.ToString());
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

        var status = StatusCombo.SelectedValue is int si ? si : 1;

        var personAtFault = PersonAtFaultNameBox.Text.Trim();
        JToken personTok = string.IsNullOrEmpty(personAtFault)
            ? JValue.CreateNull()
            : personAtFault;

        return new JObject
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
            ["LicensePlate"] = LicensePlateBox.Text.Trim(),
            ["VehicleModel"] = VehicleModelBox.Text.Trim(),
            ["Owner"] = OwnerBox.Text.Trim(),
            ["PersonAtFaultName"] = personTok,
            ["Vin"] = VinBox.Text.Trim(),
            ["ImpoundReason"] = ComboSelectionText(ImpoundReasonCombo),
            ["TowCompany"] = ComboSelectionText(TowCompanyCombo),
            ["ImpoundLot"] = ComboSelectionText(ImpoundLotCombo),
        };
    }

    public void Clear()
    {
        IdBox.Text = ShortYearBox.Text = "";
        TimeStampBox.Text = NativeMdtFormat.FormatDateTimeDisplay(DateTime.Now);
        StatusCombo.SelectedValue = 1;
        NotesBox.Text = "";
        OfficerFirstBox.Text = OfficerLastBox.Text = OfficerRankBox.Text = "";
        OfficerCallSignBox.Text = OfficerAgencyBox.Text = OfficerBadgeBox.Text = "";
        LocAreaBox.Text = LocStreetBox.Text = LocCountyBox.Text = LocPostalBox.Text = "";
        LicensePlateBox.Text = VehicleModelBox.Text = OwnerBox.Text = "";
        PersonAtFaultNameBox.Text = VinBox.Text = "";
        ImpoundReasonCombo.ItemsSource = ImpoundReasonOptions;
        ImpoundReasonCombo.SelectedIndex = 0;
        TowCompanyCombo.ItemsSource = TowCompanyOptions;
        TowCompanyCombo.SelectedIndex = 0;
        ImpoundLotCombo.ItemsSource = ImpoundLotOptions;
        ImpoundLotCombo.SelectedIndex = Random.Shared.Next(ImpoundLotOptions.Length);
    }

    static void SelectComboFromSaved(ComboBox combo, IReadOnlyList<string> baseline, string? saved)
    {
        var s = saved ?? "";
        if (!string.IsNullOrEmpty(s) && !baseline.Contains(s))
            combo.ItemsSource = baseline.Concat(new[] { s }).ToList();
        else
            combo.ItemsSource = baseline;
        combo.SelectedItem = s;
    }

    void SelectImpoundLotCombo(string? saved)
    {
        var s = (saved ?? "").Trim();
        if (string.IsNullOrEmpty(s))
        {
            ImpoundLotCombo.ItemsSource = ImpoundLotOptions;
            ImpoundLotCombo.SelectedIndex = Random.Shared.Next(ImpoundLotOptions.Length);
            return;
        }

        if (ImpoundLotOptions.Contains(s))
        {
            ImpoundLotCombo.ItemsSource = ImpoundLotOptions;
            ImpoundLotCombo.SelectedItem = s;
        }
        else
        {
            ImpoundLotCombo.ItemsSource = ImpoundLotOptions.Concat(new[] { s }).ToList();
            ImpoundLotCombo.SelectedItem = s;
        }
    }

    static string ComboSelectionText(ComboBox combo) =>
        (combo.SelectedItem as string ?? "").Trim();
}
