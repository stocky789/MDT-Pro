using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MDTProNative.Client;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class PersonSearchView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;

    public PersonSearchView() => InitializeComponent();

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        if (connection?.Http == null)
        {
            ResultBox.Text = "";
            RecentList.Items.Clear();
        }
    }

    async void Search_Click(object sender, RoutedEventArgs e) => await RunSearch(MdtBodies.JsonString(NameBox.Text.Trim()));

    async void Context_Click(object sender, RoutedEventArgs e)
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var j = await http.GetDataJsonAsync("contextPed");
            ResultBox.Text = j?.ToString(Formatting.Indented) ?? "(null)";
        }
        catch (Exception ex) { ResultBox.Text = ex.Message; }
    }

    async void Recent_Click(object sender, RoutedEventArgs e)
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var j = await http.GetDataJsonAsync("recentIds");
            RecentList.Items.Clear();
            if (j is JArray arr)
            {
                foreach (var o in arr)
                {
                    var n = o["Name"]?.ToString() ?? o.ToString();
                    RecentList.Items.Add(n);
                }
            }
        }
        catch { /* ignore */ }
    }

    void RecentList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedItem is string s)
        {
            NameBox.Text = s;
            _ = RunSearch(MdtBodies.JsonString(s));
        }
    }

    async Task RunSearch(string body)
    {
        var http = _connection?.Http;
        if (http == null)
        {
            ResultBox.Text = "Not connected.";
            return;
        }
        if (string.IsNullOrWhiteSpace(body) || body == "\"\"")
        {
            ResultBox.Text = "Enter a name.";
            return;
        }
        try
        {
            var (status, text) = await http.PostAsync("data/specificPed", body);
            if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('{'))
                ResultBox.Text = JToken.Parse(text).ToString(Formatting.Indented);
            else
                ResultBox.Text = $"HTTP {(int)status}: {text}";
        }
        catch (Exception ex) { ResultBox.Text = ex.Message; }
    }
}
