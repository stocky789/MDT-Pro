using System.Windows.Controls;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class MapView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;

    public MapView()
    {
        InitializeComponent();
        RefreshBtn.Click += async (_, _) => await RefreshAsync();
    }

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        LocationText.Text = "—";
        PostalText.Text = "—";
        if (connection?.Http == null) return;
        _ = RefreshAsync();
    }

    async Task RefreshAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var locTok = await http.GetDataJsonAsync("playerLocation").ConfigureAwait(false);

            string postalText = "—";
            try
            {
                var postalTok = await http.GetDataJsonAsync("activePostalCodeSet").ConfigureAwait(false);
                if (postalTok != null && postalTok.Type != JTokenType.Null)
                    postalText = JTokenDisplay.ForDataCell(postalTok);
            }
            catch
            {
                postalText = "—";
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (locTok == null || locTok.Type == JTokenType.Null)
                    LocationText.Text = "No fix (link the session and ensure the game is running).";
                else if (locTok is JObject jo)
                    LocationText.Text = JTokenDisplay.FormatDocument(jo);
                else
                    LocationText.Text = JTokenDisplay.ForDataCell(locTok);

                PostalText.Text = string.IsNullOrWhiteSpace(postalText) ? "—" : postalText;
            });
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                LocationText.Text = "Could not read player location.";
                PostalText.Text = "—";
            });
        }
    }
}
