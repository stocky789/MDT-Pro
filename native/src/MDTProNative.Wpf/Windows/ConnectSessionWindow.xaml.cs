using System.IO;
using System.Net.Http;
using System.Windows;
using MDTProNative.Client;
using MDTProNative.Core;
using MDTProNative.Wpf.Services;

namespace MDTProNative.Wpf.Windows;

public partial class ConnectSessionWindow : Window
{
    public TerminalSessionConfig? ResultConfig { get; private set; }

    public ConnectSessionWindow()
    {
        InitializeComponent();
        var s = SessionStartupStore.Load();
        OfficerNameBox.Text = s.TerminalOfficerName ?? "";
        HostBox.Text = string.IsNullOrWhiteSpace(s.Host) ? "127.0.0.1" : s.Host;
        PortBox.Text = s.Port > 0 ? s.Port.ToString() : "9000";
    }

    void Exit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 1 || port > 65535)
        {
            MessageBox.Show(this, "Port must be between 1 and 65535.", "Connect", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var host = string.IsNullOrWhiteSpace(HostBox.Text) ? "127.0.0.1" : HostBox.Text.Trim();
        var display = OfficerNameBox.Text.Trim();

        ConnectBtn.IsEnabled = false;
        var ok = false;
        try
        {
            var endpoint = new MdtServerEndpoint(host, port);
            using var probe = new MdtHttpClient(endpoint);
            string? ver;
            try
            {
                ver = await probe.GetVersionPlainAsync().ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                MessageBox.Show(this,
                    "Could not reach MDT Pro on that address. Is the game running on duty, the plugin listening, and the firewall allowing this port?\n\n" + ex.Message,
                    "Connect", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrEmpty(ver))
            {
                MessageBox.Show(this,
                    "The server responded but did not return a plugin version. Check the IP, port, and that this is MDT Pro's HTTP service.",
                    "Connect", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (RememberCheck.IsChecked == true)
            {
                SessionStartupStore.Save(new SessionStartupSettings
                {
                    TerminalOfficerName = string.IsNullOrEmpty(display) ? null : display,
                    Host = host,
                    Port = port
                });
            }

            ResultConfig = new TerminalSessionConfig(
                string.IsNullOrEmpty(display) ? "Operator" : display,
                host,
                port);

            ok = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Connect failed unexpectedly:\n\n" + ex.Message, "Connect", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!ok)
                ConnectBtn.IsEnabled = true;
        }
    }
}
