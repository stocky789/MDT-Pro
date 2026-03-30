using System.Windows.Controls;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Reports;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

public partial class TrafficIncidentReportForm : UserControl, IReportFormPane
{
    public TrafficIncidentReportForm()
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
        DriverNamesBox.Text = ReportFormPaneFields.StringArrayToLines(report["DriverNames"]);
        PassengerNamesBox.Text = ReportFormPaneFields.StringArrayToLines(report["PassengerNames"]);
        PedestrianNamesBox.Text = ReportFormPaneFields.StringArrayToLines(report["PedestrianNames"]);
        VehiclePlatesBox.Text = ReportFormPaneFields.StringArrayToLines(report["VehiclePlates"]);
        VehicleModelsBox.Text = ReportFormPaneFields.StringArrayToLines(report["VehicleModels"]);
        InjuryReportedCheck.IsChecked = ReportFormPaneFields.ReadBool(report["InjuryReported"]);
        InjuryDetailsBox.Text = report["InjuryDetails"]?.ToString() ?? "";
        CollisionTypeBox.Text = report["CollisionType"]?.ToString() ?? "";
    }

    public JObject BuildReport()
    {
        var root = new JObject();
        ReportFormPaneFields.WriteBase(root, BaseControls);
        root["DriverNames"] = ReportFormPaneFields.LinesToStringArray(DriverNamesBox.Text);
        root["PassengerNames"] = ReportFormPaneFields.LinesToStringArray(PassengerNamesBox.Text);
        root["PedestrianNames"] = ReportFormPaneFields.LinesToStringArray(PedestrianNamesBox.Text);
        root["VehiclePlates"] = ReportFormPaneFields.LinesToStringArray(VehiclePlatesBox.Text);
        root["VehicleModels"] = ReportFormPaneFields.LinesToStringArray(VehicleModelsBox.Text);
        root["InjuryReported"] = InjuryReportedCheck.IsChecked == true;
        var inj = InjuryDetailsBox.Text.Trim();
        root["InjuryDetails"] = string.IsNullOrEmpty(inj) ? JValue.CreateNull() : inj;
        var col = CollisionTypeBox.Text.Trim();
        root["CollisionType"] = string.IsNullOrEmpty(col) ? JValue.CreateNull() : col;
        return root;
    }

    public void Clear()
    {
        ReportFormPaneFields.ClearBase(BaseControls, 1);
        DriverNamesBox.Text = PassengerNamesBox.Text = PedestrianNamesBox.Text = "";
        VehiclePlatesBox.Text = VehicleModelsBox.Text = "";
        InjuryReportedCheck.IsChecked = false;
        InjuryDetailsBox.Text = CollisionTypeBox.Text = "";
    }
}
