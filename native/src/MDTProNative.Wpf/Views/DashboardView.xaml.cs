using System.Net;
using System.Windows;
using System.Windows.Controls;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class DashboardView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;
    JArray? _lastCallouts;
    readonly System.Collections.ObjectModel.ObservableCollection<string> _lines = new();

    public DashboardView()
    {
        InitializeComponent();
        CalloutList.ItemsSource = _lines;
        _lines.Add("— Not connected —");
    }

    public void Bind(MdtConnectionManager? connection)
    {
        if (_connection != null)
            _connection.CalloutsUpdated -= OnCalloutsUpdated;
        _connection = connection;
        if (_connection != null)
            _connection.CalloutsUpdated += OnCalloutsUpdated;
        if (_connection?.Http == null)
        {
            _lastCallouts = null;
            _lines.Clear();
            _lines.Add("— Not connected —");
            DetailText.Text = "";
        }
    }

    void OnCalloutsUpdated(JArray? list, int count)
    {
        _lastCallouts = list;
        _lines.Clear();
        if (list == null || count == 0)
        {
            _lines.Add("— No active callouts —");
            DetailText.Text = "No active callouts.";
            return;
        }
        foreach (var item in list)
        {
            var name = item["Name"]?.ToString() ?? item["name"]?.ToString() ?? "(callout)";
            var locTok = item["Location"] ?? item["location"];
            var loc = locTok != null ? JTokenDisplay.FormatLocation(locTok) : "";
            _lines.Add(string.IsNullOrEmpty(loc) ? name : $"{name}  @  {loc}");
        }
        if (CalloutList.SelectedIndex < 0 || CalloutList.SelectedIndex >= _lines.Count)
            CalloutList.SelectedIndex = 0;
        UpdateDetail();
    }

    void CalloutList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetail();

    void UpdateDetail()
    {
        if (_lastCallouts == null || _lastCallouts.Count == 0) return;
        var i = CalloutList.SelectedIndex;
        if (i < 0 || i >= _lastCallouts.Count) return;
        var sel = _lastCallouts[i];
        DetailText.Text = sel is JObject jo ? JTokenDisplay.FormatDocument(jo) : JTokenDisplay.ForDataCell(sel);
    }

    async void SetWaypoint_Click(object sender, RoutedEventArgs e)
    {
        var http = _connection?.Http;
        if (http == null)
        {
            MdtShellEvents.LogCad("GPS: connect to MDT Pro first.");
            return;
        }
        try
        {
            var body = "{}";
            var i = CalloutList.SelectedIndex;
            if (_lastCallouts != null && i >= 0 && i < _lastCallouts.Count && _lastCallouts[i] is JObject co)
            {
                var coords = co["Coords"] as JArray;
                if (coords != null && coords.Count >= 2 && coords[0].Type != JTokenType.Null && coords[1].Type != JTokenType.Null)
                    body = JsonConvert.SerializeObject(new { x = coords[0].Value<float>(), y = coords[1].Value<float>() });
            }
            var (status, resp) = await http.PostActionAsync("setGpsWaypoint", body);
            if (status == HttpStatusCode.OK && resp == "OK")
                MdtShellEvents.LogCad("GPS waypoint set (in-game)." + (body != "{}" ? " Used selected callout coords." : ""));
            else
                MdtShellEvents.LogCad("GPS waypoint: " + resp);
        }
        catch (Exception ex) { MdtShellEvents.LogCad("GPS error: " + ex.Message); }
    }
}
