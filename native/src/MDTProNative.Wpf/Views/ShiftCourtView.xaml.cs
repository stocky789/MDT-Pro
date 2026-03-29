using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;

namespace MDTProNative.Wpf.Views;

public partial class ShiftCourtView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(6) };

    public ShiftCourtView()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await LoadAll();
    }

    public void Bind(MdtConnectionManager? connection)
    {
        _timer.Stop();
        _connection = connection;
        AutoRefresh.IsChecked = false;
        if (connection?.Http != null) _ = LoadAll();
        else
        {
            ShiftGrid.ItemsSource = null;
            CourtGrid.ItemsSource = null;
        }
    }

    void Auto_Changed(object sender, RoutedEventArgs e)
    {
        if (AutoRefresh.IsChecked == true) _timer.Start();
        else _timer.Stop();
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAll();

    async Task LoadAll()
    {
        var http = _connection?.Http;
        if (http == null) { ShiftGrid.ItemsSource = null; CourtGrid.ItemsSource = null; return; }
        try
        {
            var sh = await http.GetDataJsonAsync("shiftHistory");
            ShiftGrid.ItemsSource = JArrayToDataView.Convert(sh);
        }
        catch { ShiftGrid.ItemsSource = null; }
        try
        {
            var c = await http.GetDataJsonAsync("court");
            CourtGrid.ItemsSource = JArrayToDataView.Convert(c);
        }
        catch { CourtGrid.ItemsSource = null; }
    }
}
