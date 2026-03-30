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
        var imm = ImmersionStore.Load();
        ImmSoundCheck.IsChecked = imm.TerminalSounds;
        ImmCalloutCheck.IsChecked = imm.CalloutChime;
        ImmMotionCheck.IsChecked = imm.SubtleAnimations;
        ImmClickSoundCheck.IsChecked = imm.ClickSounds;
        Closed += SettingsWindow_OnClosed;
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

    void SaveImmersion_Click(object sender, RoutedEventArgs e) => PersistImmersionAndApply();

    void SettingsWindow_OnClosed(object? sender, EventArgs e) => PersistImmersionAndApply();

    void PersistImmersionAndApply()
    {
        var prev = ImmersionStore.Load();
        ImmersionStore.Save(new ImmersionPreferences
        {
            TerminalSounds = ImmSoundCheck.IsChecked == true,
            CalloutChime = ImmCalloutCheck.IsChecked == true,
            SubtleAnimations = ImmMotionCheck.IsChecked == true,
            ClickSounds = ImmClickSoundCheck.IsChecked == true,
            ClickSoundPath = prev.ClickSoundPath,
            MuteAllSoundEffects = prev.MuteAllSoundEffects
        });
        if (Owner is MDTProNative.Wpf.MainWindow mw)
            mw.ApplyImmersionPreferences();
    }
}
