using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class OfficerView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;

    public OfficerView() => InitializeComponent();

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        if (connection?.Http != null) _ = LoadOfficerAsync();
    }

    async void Load_Click(object sender, RoutedEventArgs e) => await LoadOfficerAsync();

    async Task LoadOfficerAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var j = await http.GetDataJsonAsync("officerInformationData") as JObject;
            if (j == null) return;
            First.Text = j["firstName"]?.ToString() ?? "";
            Last.Text = j["lastName"]?.ToString() ?? "";
            Rank.Text = j["rank"]?.ToString() ?? "";
            CallSign.Text = j["callSign"]?.ToString() ?? "";
            Agency.Text = j["agency"]?.ToString() ?? "";
            Badge.Text = j["badgeNumber"]?.ToString() ?? "";
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message); }
    }

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        var http = _connection?.Http;
        if (http == null) return;
        int? badge = int.TryParse(Badge.Text.Trim(), out var b) ? b : null;
        var o = new JObject
        {
            ["firstName"] = First.Text.Trim(),
            ["lastName"] = Last.Text.Trim(),
            ["rank"] = Rank.Text.Trim(),
            ["callSign"] = CallSign.Text.Trim(),
            ["agency"] = Agency.Text.Trim(),
            ["badgeNumber"] = badge.HasValue ? JToken.FromObject(badge.Value) : JValue.CreateNull()
        };
        try
        {
            var (status, text) = await http.PostActionAsync("updateOfficerInformationData", o.ToString(Newtonsoft.Json.Formatting.None));
            MessageBox.Show(Window.GetWindow(this), text, "Officer", MessageBoxButton.OK,
                status == System.Net.HttpStatusCode.OK ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message); }
    }
}
