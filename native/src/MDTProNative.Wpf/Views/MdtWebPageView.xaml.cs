using System.Windows;
using System.Windows.Controls;
using MDTProNative.Wpf.Services;

namespace MDTProNative.Wpf.Views;

public partial class MdtWebPageView : UserControl, IMdtBoundView
{
    public static readonly DependencyProperty PageNameProperty = DependencyProperty.Register(
        nameof(PageName),
        typeof(string),
        typeof(MdtWebPageView),
        new PropertyMetadata("map"));

    public static readonly DependencyProperty ShowToolbarProperty = DependencyProperty.Register(
        nameof(ShowToolbar),
        typeof(bool),
        typeof(MdtWebPageView),
        new PropertyMetadata(true, OnShowToolbarChanged));

    MdtConnectionManager? _connection;

    public MdtWebPageView()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncToolbar();
    }

    void SyncToolbar() => ToolbarRow.Visibility = ShowToolbar ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Page file name without extension (e.g. <c>reports</c> → <c>/page/reports</c>).</summary>
    public string PageName
    {
        get => (string)GetValue(PageNameProperty);
        set => SetValue(PageNameProperty, value);
    }

    public bool ShowToolbar
    {
        get => (bool)GetValue(ShowToolbarProperty);
        set => SetValue(ShowToolbarProperty, value);
    }

    static void OnShowToolbarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MdtWebPageView v)
            v.SyncToolbar();
    }

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        ErrorPanel.Visibility = Visibility.Collapsed;
        if (connection?.Endpoint == null)
        {
            try { Web.Source = new Uri("about:blank"); } catch { /* ignore */ }
            ErrorText.Text = "Connect to MDT Pro to load this module (same host/port as the in-game web MDT).";
            ErrorPanel.Visibility = Visibility.Visible;
            return;
        }

        _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        var ep = _connection?.Endpoint;
        if (ep == null) return;
        ErrorPanel.Visibility = Visibility.Collapsed;
        var page = string.IsNullOrWhiteSpace(PageName) ? "map" : PageName.Trim();
        try
        {
            await Web.EnsureCoreWebView2Async(null);
            Web.Source = new Uri(ep.HttpUrl($"page/{page}"));
        }
        catch (Exception ex)
        {
            ErrorText.Text = "WebView2 could not start (runtime missing, blocked, or denied). Install the WebView2 Runtime, then reconnect.\n\n" + ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }

    void Reload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Web.CoreWebView2?.Reload();
        }
        catch
        {
            _ = LoadAsync();
        }
    }
}
