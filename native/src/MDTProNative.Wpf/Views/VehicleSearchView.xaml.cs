using System.Windows;
using System.Windows.Controls;
using MDTProNative.Client;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class VehicleSearchView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;

    public VehicleSearchView() => InitializeComponent();

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        if (connection?.Http == null) ResultBox.Text = "";
    }

    async void Search_Click(object sender, RoutedEventArgs e) => await Run(MdtBodies.JsonString(PlateBox.Text.Trim()));

    async void Context_Click(object sender, RoutedEventArgs e)
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var j = await http.GetDataJsonAsync("contextVehicle");
            ResultBox.Text = j?.ToString(Formatting.Indented) ?? "(null)";
        }
        catch (Exception ex) { ResultBox.Text = ex.Message; }
    }

    async void Nearby_Click(object sender, RoutedEventArgs e)
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var (status, text) = await http.PostAsync("data/nearbyVehicles", "5");
            if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('['))
                ResultBox.Text = JToken.Parse(text).ToString(Formatting.Indented);
            else
                ResultBox.Text = $"HTTP {(int)status}: {text}";
        }
        catch (Exception ex) { ResultBox.Text = ex.Message; }
    }

    async Task Run(string body)
    {
        var http = _connection?.Http;
        if (http == null) { ResultBox.Text = "Not connected."; return; }
        try
        {
            var (status, text) = await http.PostAsync("data/specificVehicle", body);
            if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('{'))
                ResultBox.Text = JToken.Parse(text).ToString(Formatting.Indented);
            else
                ResultBox.Text = $"HTTP {(int)status}: {text}";
        }
        catch (Exception ex) { ResultBox.Text = ex.Message; }
    }
}
