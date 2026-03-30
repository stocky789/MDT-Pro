using System.Diagnostics;
using System.Windows;
using MDTProNative.Wpf.Services;

namespace MDTProNative.Wpf.Windows;

public partial class SettingsWindow : Window
{
    readonly MdtConnectionManager? _connection;

    public SettingsWindow(MdtConnectionManager? connection, string? version)
    {
        InitializeComponent();
        _connection = connection;
        VersionText.Text = string.IsNullOrEmpty(version) ? "Version: (not connected)" : $"MDT Pro plugin version: {version}";
        var online = connection?.Endpoint != null;
        OpenCustomizationBrowserBtn.IsEnabled = online;
        OpenWebHomeBrowserBtn.IsEnabled = online;
        CustomizationHost.Bind(connection);
    }

    void OpenCustomizationBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (_connection?.Endpoint == null) return;
        var url = _connection.Endpoint.HttpUrl("page/customization");
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    void OpenWebHomeBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (_connection?.Endpoint == null) return;
        var url = _connection.Endpoint.HttpUrl("page/index");
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
