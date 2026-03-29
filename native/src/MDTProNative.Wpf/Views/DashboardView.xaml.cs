using System.Net;
using System.Windows;
using System.Windows.Controls;
using MDTProNative.Client;
using MDTProNative.Wpf.Services;
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
            var loc = item["Location"]?.ToString() ?? item["location"]?.ToString() ?? "";
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
        DetailText.Text = _lastCallouts[i].ToString(Newtonsoft.Json.Formatting.Indented);
    }

    async void SetWaypoint_Click(object sender, RoutedEventArgs e)
    {
        var http = _connection?.Http;
        if (http == null)
        {
            MessageBox.Show(Window.GetWindow(this), "Connect to MDT Pro first.", "MDT Pro Native", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var (status, body) = await http.PostActionAsync("setGpsWaypoint", "{}");
            if (status == HttpStatusCode.OK && body == "OK")
                MessageBox.Show(Window.GetWindow(this), "Waypoint set (in-game).", "MDT Pro Native", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(Window.GetWindow(this), body, "MDT Pro Native", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "MDT Pro Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
