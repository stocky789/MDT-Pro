using System.Threading.Tasks;
using System.Windows.Controls;
using MDTProNative.Wpf.Services;

namespace MDTProNative.Wpf.Views;

public partial class MapView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;

    public MapView() => InitializeComponent();

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        if (connection?.Endpoint == null)
        {
            try { Web.Source = new Uri("about:blank"); } catch { }
            return;
        }
        _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        var ep = _connection?.Endpoint;
        if (ep == null) return;
        try
        {
            await Web.EnsureCoreWebView2Async(null);
            Web.Source = new Uri(ep.HttpUrl("page/map.html"));
        }
        catch
        {
            /* WebView2 runtime missing or blocked */
        }
    }
}
