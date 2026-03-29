using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views;

namespace MDTProNative.Wpf;

public partial class MainWindow : Window
{
    readonly MdtConnectionManager _connection;
    readonly ObservableCollection<string> _logLines = new();
    readonly ObservableCollection<NavItem> _nav = new();
    bool _navInit;

    public MainWindow()
    {
        InitializeComponent();
        _connection = new MdtConnectionManager(Dispatcher);
        _connection.TimeUpdated += s => StatusTime.Text = s;
        _connection.LocationUpdated += s => StatusLocation.Text = s;
        _connection.CalloutsUpdated += (_, count) => StatusCallouts.Text = count == 0 && !_connection.IsConnected ? "—" : count.ToString();
        _connection.Log += AppendLog;

        MessageLog.ItemsSource = _logLines;
        _nav.Add(new NavItem("dashboard", "Dashboard"));
        _nav.Add(new NavItem("person", "Person search"));
        _nav.Add(new NavItem("vehicle", "Vehicle search"));
        _nav.Add(new NavItem("firearms", "Firearms"));
        _nav.Add(new NavItem("bolo", "BOLO"));
        _nav.Add(new NavItem("reports", "Reports"));
        _nav.Add(new NavItem("shiftcourt", "Shift / Court"));
        _nav.Add(new NavItem("map", "Map"));
        _nav.Add(new NavItem("officer", "Officer profile"));
        NavList.ItemsSource = _nav;

        Closing += async (_, _) =>
        {
            if (ContentHost.Content is IMdtBoundView b) b.Bind(null);
            await _connection.DisposeAsync();
        };
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_navInit) return;
        _navInit = true;
        NavList.SelectedIndex = 0;
    }

    void NavList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not NavItem nav) return;
        if (ContentHost.Content is IMdtBoundView old) old.Bind(null);
        var view = CreateView(nav.Id);
        ContentHost.Content = view;
        if (view is IMdtBoundView bound) bound.Bind(_connection.IsConnected ? _connection : null);
    }

    static UserControl CreateView(string id) => id switch
    {
        "dashboard" => new DashboardView(),
        "person" => new PersonSearchView(),
        "vehicle" => new VehicleSearchView(),
        "firearms" => new FirearmsView(),
        "bolo" => new BoloView(),
        "reports" => new ReportsView(),
        "shiftcourt" => new ShiftCourtView(),
        "map" => new MapView(),
        "officer" => new OfficerView(),
        _ => new DashboardView()
    };

    async void Connect_Click(object sender, RoutedEventArgs e)
    {
        ConnectBtn.IsEnabled = false;
        try
        {
            if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 1 || port > 65535)
            {
                MessageBox.Show(this, "Port must be between 1 and 65535.", "MDT Pro Native", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var host = string.IsNullOrWhiteSpace(HostBox.Text) ? "127.0.0.1" : HostBox.Text.Trim();
            await _connection.ConnectAsync(host, port);
            try
            {
                var http = _connection.Http!;
                var officer = await http.GetDataJsonAsync("officerInformation");
                var unit = officer?["callSign"]?.ToString() ?? officer?["callsign"]?.ToString();
                StatusUnit.Text = string.IsNullOrEmpty(unit) ? "—" : unit;
            }
            catch { StatusUnit.Text = "—"; }

            ConnStateText.Text = "ONLINE";
            ConnStateText.Foreground = (System.Windows.Media.Brush)FindResource("CadAccent");
            DisconnectBtn.IsEnabled = true;

            if (ContentHost.Content is IMdtBoundView nb) nb.Bind(_connection);
        }
        catch (HttpRequestException ex)
        {
            AppendLog($"HTTP error: {ex.Message}");
            MessageBox.Show(this, "Could not reach MDT Pro. Is the game on duty and the plugin listening?\n\n" + ex.Message, "MDT Pro Native", MessageBoxButton.OK, MessageBoxImage.Error);
            await DisconnectCoreAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
            MessageBox.Show(this, ex.Message, "MDT Pro Native", MessageBoxButton.OK, MessageBoxImage.Error);
            await DisconnectCoreAsync();
        }
        finally
        {
            ConnectBtn.IsEnabled = true;
        }
    }

    async void Disconnect_Click(object sender, RoutedEventArgs e) => await DisconnectCoreAsync();

    async Task DisconnectCoreAsync()
    {
        await _connection.DisconnectAsync();
        ConnStateText.Text = "OFFLINE";
        ConnStateText.Foreground = (System.Windows.Media.Brush)FindResource("CadUrgent");
        DisconnectBtn.IsEnabled = false;
        StatusTime.Text = "—";
        StatusLocation.Text = "—";
        StatusUnit.Text = "—";
        StatusCallouts.Text = "—";
        if (ContentHost.Content is IMdtBoundView b) b.Bind(null);
    }

    void AppendLog(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        _logLines.Insert(0, $"[{stamp}] {line}");
        while (_logLines.Count > 400)
            _logLines.RemoveAt(_logLines.Count - 1);
    }

    sealed record NavItem(string Id, string Title);
}
