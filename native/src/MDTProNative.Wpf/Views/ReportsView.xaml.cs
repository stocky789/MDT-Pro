using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class ReportsView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };

    static readonly string[] DataPaths =
    [
        "incidentReports",
        "citationReports",
        "arrestReports",
        "impoundReports",
        "trafficIncidentReports",
        "injuryReports",
        "propertyEvidenceReports",
    ];

    public ReportsView()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshActiveTab();
    }

    void Tabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl && _connection?.Http != null)
            _ = RefreshActiveTab();
    }

    public void Bind(MdtConnectionManager? connection)
    {
        _timer.Stop();
        _connection = connection;
        AutoRefresh.IsChecked = false;
        if (connection?.Http != null) _ = RefreshActiveTab();
        else
        {
            GIncident.ItemsSource = null;
            GCitation.ItemsSource = null;
            GArrest.ItemsSource = null;
            GImpound.ItemsSource = null;
            GTraffic.ItemsSource = null;
            GInjury.ItemsSource = null;
            GProperty.ItemsSource = null;
        }
    }

    void Auto_Changed(object sender, RoutedEventArgs e)
    {
        if (AutoRefresh.IsChecked == true) _timer.Start();
        else _timer.Stop();
    }

    async void RefreshTab_Click(object sender, RoutedEventArgs e) => await RefreshActiveTab();

    async Task RefreshActiveTab()
    {
        var http = _connection?.Http;
        if (http == null) return;
        var idx = Tabs.SelectedIndex;
        if (idx < 0 || idx >= DataPaths.Length) return;
        var path = DataPaths[idx];
        try
        {
            var j = await http.GetDataJsonAsync(path);
            var grid = idx switch
            {
                0 => GIncident,
                1 => GCitation,
                2 => GArrest,
                3 => GImpound,
                4 => GTraffic,
                5 => GInjury,
                6 => GProperty,
                _ => GIncident
            };
            grid.ItemsSource = JArrayToDataView.Convert(j);
        }
        catch { /* ignore */ }
    }
}
