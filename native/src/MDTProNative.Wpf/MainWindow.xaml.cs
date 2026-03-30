using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views;
using MDTProNative.Wpf.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf;

public partial class MainWindow : Window
{
    readonly MdtConnectionManager _connection;
    readonly TerminalSessionConfig _session;
    readonly ObservableCollection<string> _logLines = new();
    readonly ObservableCollection<NavItem> _nav = new();
    bool _navInit;

    public MainWindow() : this(new TerminalSessionConfig("Operator", "127.0.0.1", 9000)) { }

    public MainWindow(TerminalSessionConfig session)
    {
        InitializeComponent();
        _session = session;
        _connection = new MdtConnectionManager(Dispatcher);
        _connection.TimeUpdated += s => StatusTime.Text = s;
        _connection.LocationUpdated += OnLocationUpdated;
        _connection.CalloutsUpdated += (_, count) =>
        {
            StatusCallouts.Text = count == 0 && !_connection.IsConnected ? "—" : count.ToString();
            if (RightRailActivityTab != null)
                RightRailActivityTab.Text = _connection.IsConnected ? $"CALLOUTS ({count})" : "CALLOUTS (—)";
            RefreshFooterSummary();
        };
        _connection.Log += AppendLog;

        MessageLog.ItemsSource = _logLines;
        _nav.Add(new NavItem("dashboard", "OVERVIEW"));
        _nav.Add(new NavItem("person", "PERSON"));
        _nav.Add(new NavItem("vehicle", "VEHICLE"));
        _nav.Add(new NavItem("firearms", "FIREARMS"));
        _nav.Add(new NavItem("bolo", "BOLO"));
        _nav.Add(new NavItem("reports", "REPORTS"));
        _nav.Add(new NavItem("court", "COURT"));
        _nav.Add(new NavItem("map", "MAP"));
        _nav.Add(new NavItem("officer", "OFFICER"));
        NavList.ItemsSource = _nav;

        MdtShellEvents.OfficerStripRefreshRequested += OnOfficerStripRefreshRequested;
        MdtShellEvents.CadMessageLogged += OnCadMessageLogged;
        SetQuickActionsEnabled(false);

        ApplyTerminalChrome();
        RefreshFooterSummary();
        SetConnectionLamp(online: false);
        Loaded += MainWindow_OnLoaded;

        Closing += async (_, _) =>
        {
            MdtShellEvents.OfficerStripRefreshRequested -= OnOfficerStripRefreshRequested;
            MdtShellEvents.CadMessageLogged -= OnCadMessageLogged;
            if (ContentHost.Content is IMdtBoundView b) b.Bind(null);
            await _connection.DisposeAsync();
        };
    }

    void ApplyTerminalChrome()
    {
        TerminalOperatorLabel.Text = _session.TerminalDisplayName;
        Title = $"MDT Pro — MDC Terminal — {_session.TerminalDisplayName}";
        HostBox.Text = _session.Host;
        PortBox.Text = _session.Port.ToString();
    }

    async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_OnLoaded;
        if (DesignerProperties.GetIsInDesignMode(this)) return;
        await ConnectFromFormAsync(isInitialAuto: true);
    }

    void OnOfficerStripRefreshRequested() => _ = UpdateOfficerStripAsync();

    void OnCadMessageLogged(string line) => Dispatcher.BeginInvoke(() => AppendLog(line));

    void OnLocationUpdated(string s)
    {
        StatusLocation.ToolTip = string.IsNullOrWhiteSpace(s) || s == "—" ? null : s;
        StatusLocation.Text = string.IsNullOrWhiteSpace(s) ? "—" : TruncateStatusLine(s, 180);
        RefreshFooterSummary();
    }

    void HomeModuleBtn_OnClick(object sender, RoutedEventArgs e)
    {
        if (_nav.Count > 0)
            NavList.SelectedItem = _nav[0];
    }

    void RefreshFooterSummary()
    {
        var host = string.IsNullOrWhiteSpace(HostBox?.Text) ? "—" : HostBox.Text.Trim();
        var loc = StatusLocation?.Text ?? "—";
        var calls = StatusCallouts?.Text ?? "—";
        if (!_connection.IsConnected)
            FooterSummaryText.Text = $"NO LINK  •  HOST {host}";
        else
            FooterSummaryText.Text = $"ACTIVE CALLOUTS {calls}  •  {TruncateStatusLine(loc, 64)}";
    }

    void SetConnectionLamp(bool online)
    {
        if (ConnLamp == null) return;
        if (online)
        {
            ConnLamp.Fill = (Brush)FindResource("CadOnline");
            ConnLamp.Stroke = (Brush)FindResource("CadPanel");
            ConnLamp.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0x2E, 0xCC, 0x71),
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.9
            };
        }
        else
        {
            ConnLamp.Fill = (Brush)FindResource("CadUrgent");
            ConnLamp.Stroke = (Brush)FindResource("CadBorder");
            ConnLamp.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0xE7, 0x4C, 0x3C),
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.85
            };
        }
    }

    static string TruncateStatusLine(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        return s[..max] + "…";
    }

    void SetQuickActionsEnabled(bool on)
    {
        QuickPanicBtn.IsEnabled = QuickBackupBtn.IsEnabled = QuickAlprBtn.IsEnabled = on;
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
        "court" => new NativeCourtView { Margin = new Thickness(8) },
        "map" => new MapView(),
        "officer" => new OfficerView(),
        _ => new DashboardView()
    };

    async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectFromFormAsync(isInitialAuto: false);

    async Task ConnectFromFormAsync(bool isInitialAuto)
    {
        ConnectBtn.IsEnabled = false;
        try
        {
            if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 1 || port > 65535)
            {
                AppendLog("Port must be between 1 and 65535.");
                if (!isInitialAuto)
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
                await UpdateOfficerStripAsync();
            }
            catch
            {
                StatusUnit.Text = "—";
                ClearOfficerStrip();
            }

            ConnStateText.Text = "ONLINE";
            ConnStateText.Foreground = (Brush)FindResource("CadOnline");
            SetConnectionLamp(online: true);
            DisconnectBtn.IsEnabled = true;
            SetQuickActionsEnabled(true);
            RefreshFooterSummary();

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
        ConnStateText.Foreground = (Brush)FindResource("CadUrgent");
        SetConnectionLamp(online: false);
        DisconnectBtn.IsEnabled = false;
        StatusTime.Text = "—";
        StatusLocation.Text = "—";
        StatusLocation.ToolTip = null;
        StatusUnit.Text = "—";
        StatusCallouts.Text = "—";
        if (RightRailActivityTab != null)
            RightRailActivityTab.Text = "CALLOUTS (—)";
        ClearOfficerStrip();
        SetQuickActionsEnabled(false);
        if (ContentHost.Content is IMdtBoundView b) b.Bind(null);
        RefreshFooterSummary();
    }

    void ClearOfficerStrip()
    {
        OfFirst.Text = OfLast.Text = OfBadge.Text = OfRank.Text = OfCall.Text = OfDept.Text = "—";
    }

    async Task UpdateOfficerStripAsync()
    {
        var http = _connection.Http;
        if (http == null) { ClearOfficerStrip(); return; }
        try
        {
            var j = await http.GetDataJsonAsync("officerInformation") as JObject;
            if (j == null) { ClearOfficerStrip(); return; }
            _ = Dispatcher.BeginInvoke(() =>
            {
                OfFirst.Text = F(j["firstName"]);
                OfLast.Text = F(j["lastName"]);
                OfBadge.Text = F(j["badgeNumber"]);
                OfRank.Text = F(j["rank"]);
                OfCall.Text = F(j["callSign"]);
                OfDept.Text = F(j["agency"]);
            });
        }
        catch { _ = Dispatcher.BeginInvoke(ClearOfficerStrip); }
    }

    static string F(JToken? t)
    {
        var s = t?.ToString().Trim();
        return string.IsNullOrEmpty(s) ? "—" : s!;
    }

    async void QuickPanic_Click(object sender, RoutedEventArgs e)
    {
        if (!_connection.IsConnected) return;
        try
        {
            var body = JsonConvert.SerializeObject(new { action = "panic" });
            var (_, text) = await _connection.Http!.PostActionAsync("requestBackup", body);
            var msg = text;
            try
            {
                var jo = JObject.Parse(text);
                msg = jo.Value<bool?>("success") == true ? "Panic backup requested." : (jo["error"]?.ToString() ?? text);
            }
            catch { /* plain */ }
            AppendLog("Panic: " + msg);
        }
        catch (Exception ex) { AppendLog("Panic error: " + ex.Message); }
    }

    void QuickBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!_connection.IsConnected) return;
        new BackupMenuWindow(_connection) { Owner = this }.Show();
    }

    async void QuickAlpr_Click(object sender, RoutedEventArgs e)
    {
        if (!_connection.IsConnected) return;
        try
        {
            var (_, text) = await _connection.Http!.PostActionAsync("alprClear", "{}");
            AppendLog(text == "OK" ? "ALPR cleared." : "ALPR: " + text);
        }
        catch (Exception ex) { AppendLog("ALPR error: " + ex.Message); }
    }

    void QuickNotepad_Click(object sender, RoutedEventArgs e)
    {
        new NotepadWindow { Owner = this }.Show();
    }

    void QuickNarcotics_Click(object sender, RoutedEventArgs e)
    {
        new NarcoticsCheatsheetWindow { Owner = this }.Show();
    }

    void QuickOfficer_Click(object sender, RoutedEventArgs e)
    {
        var o = _nav.FirstOrDefault(x => x.Id == "officer");
        if (o != null) NavList.SelectedItem = o;
    }

    async void QuickSettings_Click(object sender, RoutedEventArgs e)
    {
        string? ver = null;
        if (_connection.IsConnected && _connection.Http != null)
        {
            try { ver = await _connection.Http.GetVersionPlainAsync(); } catch { /* ignore */ }
        }
        new SettingsWindow(_connection.IsConnected ? _connection : null, ver) { Owner = this }.Show();
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
