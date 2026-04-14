using System.Collections.ObjectModel;
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
    bool _layoutPersistWired;
    JArray? _lastCallouts;
    readonly ObservableCollection<CalloutListRow> _rows = new();

    public DashboardView()
    {
        InitializeComponent();
        Loaded += OnDashboardLoaded;
        CalloutList.ItemsSource = _rows;
        InitCadPresets();
        RefreshActionButtons();
    }

    void InitCadPresets()
    {
        CadPresetCombo.Items.Clear();
        void Add(string label, string value) =>
            CadPresetCombo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        Add("10-8 — Available", "10-8 | Available");
        Add("10-97 — En route", "10-97 | En route");
        Add("10-23 — On scene", "10-23 | On scene");
        Add("10-95 — Traffic stop", "10-95 | Traffic stop");
        Add("10-7 — Out of service / meal", "10-7 | Out of service");
        Add("10-6 — Busy (other)", "10-6 | Busy");
        CadPresetCombo.SelectedIndex = 0;
    }

    void OnDashboardLoaded(object sender, RoutedEventArgs e)
    {
        if (_layoutPersistWired) return;
        _layoutPersistWired = true;
        UiLayoutHooks.WireDashboard(this);
    }

    public void Bind(MdtConnectionManager? connection)
    {
        if (_connection != null)
            _connection.CalloutsUpdated -= OnCalloutsUpdated;
        _connection = connection;
        if (_connection?.Http == null)
        {
            _lastCallouts = null;
            _rows.Clear();
            DetailText.Text = "";
            CadStatusReadout.Text = "—";
            RefreshActionButtons();
            return;
        }
        _connection.CalloutsUpdated += OnCalloutsUpdated;
        _connection.ReplayLastCallouts(OnCalloutsUpdated);
    }

    void OnCalloutsUpdated(JArray? list, int count, string? cadUnitStatus)
    {
        CadStatusReadout.Text = string.IsNullOrWhiteSpace(cadUnitStatus) ? "—" : cadUnitStatus;
        _lastCallouts = list;
        _rows.Clear();
        if (list == null || count == 0)
        {
            DetailText.Text = "No active callouts.";
            RefreshActionButtons();
            return;
        }
        foreach (var item in list)
        {
            var row = CalloutListRow.TryFromToken(item);
            if (row != null)
                _rows.Add(row);
        }
        if (_rows.Count == 0)
        {
            DetailText.Text = "Callouts are present but missing Id fields. Deploy the updated MDT Pro plugin.";
            RefreshActionButtons();
            return;
        }
        if (CalloutList.SelectedItem == null || CalloutList.SelectedIndex < 0 || CalloutList.SelectedIndex >= _rows.Count)
            CalloutList.SelectedIndex = 0;
        UpdateDetail();
        RefreshActionButtons();
    }

    void CalloutList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDetail();
        RefreshActionButtons();
    }

    void UpdateDetail()
    {
        if (CalloutList.SelectedItem is CalloutListRow row)
            DetailText.Text = JTokenDisplay.FormatCalloutDetail(row.Source);
        else
            DetailText.Text = _rows.Count == 0 ? "No active callouts." : "Select a callout.";
    }

    void RefreshActionButtons()
    {
        if (CalloutList.SelectedItem is not CalloutListRow row)
        {
            BtnAccept.IsEnabled = false;
            BtnEnRoute.IsEnabled = false;
            BtnGps.IsEnabled = false;
            return;
        }
        BtnAccept.IsEnabled = row.AcceptanceState == 0;
        BtnEnRoute.IsEnabled = row.AcceptanceState == 1;
        BtnGps.IsEnabled = true;
    }

    CalloutListRow? SelectedRow() => CalloutList.SelectedItem as CalloutListRow;

    async void SetCadStatus_Click(object sender, RoutedEventArgs e)
    {
        var http = _connection?.Http;
        if (http == null)
        {
            MdtShellEvents.LogCad("CAD status: connect to MDT Pro first.");
            return;
        }
        var custom = CadCustomStatus.Text?.Trim() ?? "";
        string status;
        if (!string.IsNullOrEmpty(custom))
            status = custom;
        else if (CadPresetCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
            status = tag;
        else
        {
            MdtShellEvents.LogCad("CAD status: choose a preset or enter a custom line.");
            return;
        }
        try
        {
            var body = JsonConvert.SerializeObject(new { status });
            var (code, text) = await http.PostActionAsync("cadUnitStatus", body);
            if (code == HttpStatusCode.OK)
                MdtShellEvents.LogCad("CAD unit status updated.");
            else
                MdtShellEvents.LogCad("CAD status: " + text);
        }
        catch (Exception ex) { MdtShellEvents.LogCad("CAD status error: " + ex.Message); }
    }

    async void Accept_Click(object sender, RoutedEventArgs e) => await RunCalloutAction("accept");

    async void EnRoute_Click(object sender, RoutedEventArgs e) => await RunCalloutAction("enroute");

    async Task RunCalloutAction(string action)
    {
        var http = _connection?.Http;
        var row = SelectedRow();
        if (http == null || row == null)
        {
            MdtShellEvents.LogCad("Callout action: connect and select a call.");
            return;
        }
        try
        {
            var body = JsonConvert.SerializeObject(new { action, calloutId = row.Id });
            var (_, text) = await http.PostActionAsync("calloutAction", body);
            var jo = ParseJsonLoose(text);
            var ok = jo?["success"]?.Value<bool>() == true;
            if (ok)
                MdtShellEvents.LogCad(action + ": OK.");
            else
                MdtShellEvents.LogCad(action + ": " + (jo?["error"]?.ToString() ?? text));
        }
        catch (Exception ex) { MdtShellEvents.LogCad(action + " error: " + ex.Message); }
    }

    static JObject? ParseJsonLoose(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JObject.Parse(text); } catch { return null; }
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
            if (SelectedRow()?.Source is { } co)
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
