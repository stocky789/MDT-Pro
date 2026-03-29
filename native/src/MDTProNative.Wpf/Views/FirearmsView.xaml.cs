using System.Windows;
using System.Windows.Controls;
using MDTProNative.Client;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class FirearmsView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;

    public FirearmsView() => InitializeComponent();

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        if (connection?.Http == null) GridRecent.ItemsSource = null;
        else _ = LoadRecent();
    }

    async void Lookup_Click(object sender, RoutedEventArgs e)
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var body = MdtBodies.JsonString(SerialBox.Text.Trim());
            var (status, text) = await http.PostAsync("data/firearmBySerial", body);
            MessageBox.Show(Window.GetWindow(this),
                string.IsNullOrWhiteSpace(text) ? $"HTTP {(int)status}" : (text.Length > 2000 ? text[..2000] + "…" : text),
                "Firearm lookup", MessageBoxButton.OK,
                status == System.Net.HttpStatusCode.OK ? MessageBoxImage.Information : MessageBoxImage.Warning);
            await LoadRecent();
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message); }
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadRecent();

    async Task LoadRecent()
    {
        var http = _connection?.Http;
        if (http == null) { GridRecent.ItemsSource = null; return; }
        try
        {
            var j = await http.GetDataJsonAsync("recentFirearms");
            GridRecent.ItemsSource = JArrayToDataView.Convert(j);
        }
        catch { GridRecent.ItemsSource = null; }
    }
}
