using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MDTProNative.Client;
using MDTProNative.Core;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf;

public partial class MainWindow : Window
{
    MdtHttpClient? _http;
    MdtWebSocketSession? _wsTime;
    MdtWebSocketSession? _wsLocation;
    MdtWebSocketSession? _wsCallouts;
    readonly ObservableCollection<string> _calloutLines = new();
    readonly ObservableCollection<string> _logLines = new();
    JArray? _lastCallouts;

    public MainWindow()
    {
        InitializeComponent();
        CalloutList.ItemsSource = _calloutLines;
        MessageLog.ItemsSource = _logLines;
        _calloutLines.Add("— Not connected —");
        CalloutList.SelectionChanged += CalloutList_OnSelectionChanged;
        Closing += async (_, _) => await TeardownAsync();
    }

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

            var host = HostBox.Text.Trim();
            if (string.IsNullOrEmpty(host)) host = "127.0.0.1";

            await TeardownAsync();

            var endpoint = new MdtServerEndpoint(host, port);
            _http = new MdtHttpClient(endpoint);

            var timePlain = await _http.GetCurrentTimePlainAsync();
            Log($"HTTP data/currentTime: {timePlain}");
            var cfg = await _http.GetConfigJsonAsync();
            if (cfg != null)
                Log("HTTP config: OK");

            _wsTime = new MdtWebSocketSession(endpoint);
            _wsTime.MessageReceived += OnTimeMessage;
            await _wsTime.ConnectAsync();
            await _wsTime.SendRawAsync("interval/time");
            Log("WS: interval/time subscribed");

            _wsLocation = new MdtWebSocketSession(endpoint);
            _wsLocation.MessageReceived += OnLocationMessage;
            await _wsLocation.ConnectAsync();
            await _wsLocation.SendRawAsync("interval/playerLocation");
            Log("WS: interval/playerLocation subscribed");

            _wsCallouts = new MdtWebSocketSession(endpoint);
            _wsCallouts.MessageReceived += OnCalloutMessage;
            await _wsCallouts.ConnectAsync();
            await _wsCallouts.SendRawAsync("calloutEvent");
            Log("WS: calloutEvent subscribed");

            try
            {
                var officer = await _http.GetDataJsonAsync("officerInformation", default);
                var unit = officer?["callSign"]?.ToString() ?? officer?["callsign"]?.ToString();
                if (!string.IsNullOrEmpty(unit))
                    StatusUnit.Text = unit;
            }
            catch { /* optional */ }

            ConnStateText.Text = "ONLINE";
            ConnStateText.Foreground = (System.Windows.Media.Brush)FindResource("CadAccent");
            DisconnectBtn.IsEnabled = true;
        }
        catch (HttpRequestException ex)
        {
            Log($"HTTP error: {ex.Message}");
            MessageBox.Show(this, "Could not reach MDT Pro. Is the game on duty and the plugin listening?\n\n" + ex.Message, "MDT Pro Native", MessageBoxButton.OK, MessageBoxImage.Error);
            await TeardownAsync();
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            MessageBox.Show(this, ex.Message, "MDT Pro Native", MessageBoxButton.OK, MessageBoxImage.Error);
            await TeardownAsync();
        }
        finally
        {
            ConnectBtn.IsEnabled = true;
        }
    }

    async void Disconnect_Click(object sender, RoutedEventArgs e) => await TeardownAsync();

    void OnTimeMessage(string request, JToken? response)
    {
        if (request != "time") return;
        var s = response?.ToString().Trim('"') ?? "—";
        Dispatcher.Invoke(() => StatusTime.Text = s);
    }

    void OnLocationMessage(string request, JToken? response)
    {
        if (request != "playerLocation") return;
        var line = response?.ToString(Newtonsoft.Json.Formatting.None) ?? "—";
        Dispatcher.Invoke(() => StatusLocation.Text = line);
    }

    void OnCalloutMessage(string request, JToken? response)
    {
        if (request != "calloutEvent" || response is not JObject root) return;
        var list = root["callouts"] as JArray;
        var count = list?.Count ?? 0;
        Dispatcher.Invoke(() =>
        {
            _lastCallouts = list;
            StatusCallouts.Text = count.ToString();
            _calloutLines.Clear();
            if (list == null || count == 0)
            {
                _calloutLines.Add("— No active callouts —");
                DetailText.Text = "No active callouts.";
                return;
            }
            foreach (var item in list)
            {
                var name = item["Name"]?.ToString() ?? item["name"]?.ToString() ?? "(callout)";
                var loc = item["Location"]?.ToString() ?? item["location"]?.ToString() ?? "";
                _calloutLines.Add(string.IsNullOrEmpty(loc) ? name : $"{name}  @  {loc}");
            }
            if (CalloutList.SelectedIndex < 0 || CalloutList.SelectedIndex >= _calloutLines.Count)
                CalloutList.SelectedIndex = 0;
            UpdateDetailForSelectedCallout();
        });
        Log($"calloutEvent: {count} active");
    }

    void Log(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        Dispatcher.Invoke(() =>
        {
            _logLines.Insert(0, $"[{stamp}] {line}");
            while (_logLines.Count > 500)
                _logLines.RemoveAt(_logLines.Count - 1);
        });
    }

    void CalloutList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetailForSelectedCallout();

    void UpdateDetailForSelectedCallout()
    {
        if (_lastCallouts == null || _lastCallouts.Count == 0) return;
        var i = CalloutList.SelectedIndex;
        if (i < 0 || i >= _lastCallouts.Count) return;
        DetailText.Text = _lastCallouts[i].ToString(Newtonsoft.Json.Formatting.Indented);
    }

    async Task TeardownAsync()
    {
        if (_wsTime != null) { _wsTime.MessageReceived -= OnTimeMessage; await DisposeWs(_wsTime); _wsTime = null; }
        if (_wsLocation != null) { _wsLocation.MessageReceived -= OnLocationMessage; await DisposeWs(_wsLocation); _wsLocation = null; }
        if (_wsCallouts != null) { _wsCallouts.MessageReceived -= OnCalloutMessage; await DisposeWs(_wsCallouts); _wsCallouts = null; }
        _http?.Dispose();
        _http = null;

        Dispatcher.Invoke(() =>
        {
            ConnStateText.Text = "OFFLINE";
            ConnStateText.Foreground = (System.Windows.Media.Brush)FindResource("CadUrgent");
            DisconnectBtn.IsEnabled = false;
            StatusTime.Text = "—";
            StatusLocation.Text = "—";
            StatusCallouts.Text = "—";
            _lastCallouts = null;
            _calloutLines.Clear();
            _calloutLines.Add("— Not connected —");
            DetailText.Text = "Disconnected.";
        });
    }

    static async Task DisposeWs(MdtWebSocketSession? ws)
    {
        if (ws == null) return;
        try { await ws.DisposeAsync(); } catch { }
    }
}
