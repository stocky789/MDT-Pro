using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class BoloView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };

    public BoloView()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await Load();
    }

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        _timer.Stop();
        if (connection?.Http != null)
            _ = Load();
    }

    void Auto_Changed(object sender, RoutedEventArgs e)
    {
        if (AutoRefresh.IsChecked == true) _timer.Start();
        else _timer.Stop();
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await Load();

    async Task Load()
    {
        var http = _connection?.Http;
        if (http == null) { BoloGrid.ItemsSource = null; return; }
        try
        {
            var j = await http.GetDataJsonAsync("activeBolos");
            BoloGrid.ItemsSource = JArrayToDataView.Convert(j);
        }
        catch { BoloGrid.ItemsSource = null; }
    }

    void BoloGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BoloGrid.SelectedItem is not System.Data.DataRowView row) return;
        try
        {
            RemPlate.Text = row["LicensePlate"]?.ToString() ?? row["licensePlate"]?.ToString() ?? "";
            RemReason.Text = row["Reason"]?.ToString() ?? row["reason"]?.ToString() ?? "";
        }
        catch { /* columns vary */ }
    }

    async void Add_Click(object sender, RoutedEventArgs e)
    {
        var http = _connection?.Http;
        if (http == null) return;
        var payload = JsonConvert.SerializeObject(new
        {
            LicensePlate = AddPlate.Text.Trim(),
            Reason = AddReason.Text.Trim(),
            IssuedBy = string.IsNullOrWhiteSpace(AddAgency.Text) ? "LSPD" : AddAgency.Text.Trim(),
            ExpiresAt = default(DateTime),
            ModelDisplayName = (string?)null
        });
        try
        {
            var (status, text) = await http.PostActionAsync("addBOLO", payload);
            MessageBox.Show(Window.GetWindow(this), text, "BOLO", MessageBoxButton.OK,
                status == System.Net.HttpStatusCode.OK ? MessageBoxImage.Information : MessageBoxImage.Warning);
            await Load();
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message); }
    }

    async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var http = _connection?.Http;
        if (http == null) return;
        var payload = JsonConvert.SerializeObject(new { LicensePlate = RemPlate.Text.Trim(), Reason = RemReason.Text.Trim() });
        try
        {
            var (status, text) = await http.PostActionAsync("removeBOLO", payload);
            MessageBox.Show(Window.GetWindow(this), text, "BOLO", MessageBoxButton.OK,
                status == System.Net.HttpStatusCode.OK ? MessageBoxImage.Information : MessageBoxImage.Warning);
            await Load();
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message); }
    }
}
