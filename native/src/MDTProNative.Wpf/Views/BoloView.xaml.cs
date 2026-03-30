using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class BoloView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;

    public BoloView()
    {
        InitializeComponent();
        ExpiresPicker.SelectedDate = DateTime.Today.AddDays(7);
        VehicleList.DisplayMemberPath = nameof(VehicleBoloRow.Display);
        RefreshBtn.Click += async (_, _) => await RefreshAsync();
        AddBoloBtn.Click += async (_, _) => await TryAddAsync();
    }

    Brush R(string key) => (Brush)FindResource(key);

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        VehicleList.ItemsSource = null;
        BoloLinesPanel.ItemsSource = null;
        if (connection?.Http == null) return;
        _ = RefreshAsync();
    }

    async Task RefreshAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var tok = await http.GetDataJsonAsync("activeBolos").ConfigureAwait(false);
            var rows = new List<VehicleBoloRow>();
            JArray? arr = tok as JArray;
            if (arr != null)
            {
                foreach (var t in arr.OfType<JObject>())
                {
                    var plate = t["LicensePlate"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(plate)) continue;
                    rows.Add(new VehicleBoloRow(
                        plate,
                        t["ModelDisplayName"]?.ToString() ?? "",
                        t["IsStolen"]?.Value<bool>() == true,
                        t["CanModifyBOLOs"]?.Value<bool>() == true,
                        t["BOLOs"] as JArray ?? new JArray()));
                }
            }

            await Dispatcher.InvokeAsync(() =>
            {
                VehicleList.ItemsSource = rows;
                BoloLinesPanel.ItemsSource = null;
            });
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                VehicleList.ItemsSource = Array.Empty<VehicleBoloRow>();
                BoloLinesPanel.ItemsSource = null;
            });
        }
    }

    void VehicleList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VehicleList.SelectedItem is not VehicleBoloRow row)
        {
            BoloLinesPanel.ItemsSource = null;
            return;
        }

        var lines = new List<Border>();
        foreach (var b in row.Bolos.OfType<JObject>())
            lines.Add(MakeBoloLineCard(row.Plate, row.CanModify, b));
        BoloLinesPanel.ItemsSource = lines;
    }

    Border MakeBoloLineCard(string plate, bool canModify, JObject b)
    {
        var reasonTok = b["Reason"] ?? b["reason"];
        var reason = reasonTok?.ToString() ?? JTokenDisplay.ForDataCell(b);
        var exp = NativeMdtFormat.IsoDate(b["ExpiresAt"] ?? b["expiresAt"]);
        var issued = NativeMdtFormat.IsoDate(b["IssuedAt"] ?? b["issuedAt"]);
        var by = NativeMdtFormat.Text(b["IssuedBy"] ?? b["issuedBy"]);

        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = reason,
            TextWrapping = TextWrapping.Wrap,
            Foreground = R("CadText"),
            FontWeight = FontWeights.SemiBold
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"Expires {exp}  ·  Issued {issued}  ·  {by}",
            Foreground = R("CadMuted"),
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        if (canModify)
        {
            var rawReason = reasonTok?.ToString() ?? "";
            var rm = new Button
            {
                Content = "REMOVE BOLO",
                Style = (Style)FindResource("CadButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(12, 4, 12, 4)
            };
            rm.Click += async (_, _) => await TryRemoveAsync(plate, rawReason);
            sp.Children.Add(rm);
        }

        return new Border
        {
            BorderBrush = R("CadBorder"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Background = R("CadElevated"),
            Child = sp
        };
    }

    async Task TryAddAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        var plate = NewPlateBox.Text.Trim();
        var reason = NewReasonBox.Text.Trim();
        var model = NewModelBox.Text.Trim();
        if (string.IsNullOrEmpty(plate) || string.IsNullOrEmpty(reason))
        {
            MessageBox.Show("Plate and reason are required.", "BOLO", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var issuedBy = (IssuedByBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(issuedBy)) issuedBy = "MDC";
        var expiresUtc = DateTime.UtcNow.AddDays(7);
        if (ExpiresPicker.SelectedDate is { } localDate)
        {
            var localMidnight = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Local);
            expiresUtc = localMidnight.ToUniversalTime();
        }

        var body = JsonConvert.SerializeObject(new
        {
            LicensePlate = plate,
            Reason = reason,
            ExpiresAt = expiresUtc,
            IssuedBy = issuedBy,
            ModelDisplayName = string.IsNullOrEmpty(model) ? null : model
        });

        try
        {
            var (status, text) = await http.PostActionAsync("addBOLO", body).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (status == HttpStatusCode.OK)
                {
                    try
                    {
                        var jo = JObject.Parse(text);
                        if (jo.Value<bool?>("success") == true)
                        {
                            MdtShellEvents.LogCad("BOLO added.");
                            NewReasonBox.Clear();
                            NewModelBox.Clear();
                            _ = RefreshAsync();
                            return;
                        }

                        MessageBox.Show(jo["error"]?.ToString() ?? text, "BOLO", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch
                    {
                        MessageBox.Show(text, "BOLO", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                    MessageBox.Show(text, "BOLO", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => MessageBox.Show(ex.Message, "BOLO", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    async Task TryRemoveAsync(string plate, string reason)
    {
        var http = _connection?.Http;
        if (http == null) return;
        var body = JsonConvert.SerializeObject(new { LicensePlate = plate.Trim(), Reason = reason });
        try
        {
            var (status, text) = await http.PostActionAsync("removeBOLO", body).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (status == HttpStatusCode.OK)
                {
                    try
                    {
                        var jo = JObject.Parse(text);
                        if (jo.Value<bool?>("success") == true)
                        {
                            MdtShellEvents.LogCad("BOLO removed.");
                            _ = RefreshAsync();
                            return;
                        }

                        MessageBox.Show(jo["error"]?.ToString() ?? text, "BOLO", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch
                    {
                        MessageBox.Show(text, "BOLO", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                    MessageBox.Show(text, "BOLO", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => MessageBox.Show(ex.Message, "BOLO", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    sealed class VehicleBoloRow(string plate, string model, bool stolen, bool canModify, JArray bolos)
    {
        public string Plate { get; } = plate;
        public string Model { get; } = model;
        public bool Stolen { get; } = stolen;
        public bool CanModify { get; } = canModify;
        public JArray Bolos { get; } = bolos;

        public string Display
        {
            get
            {
                var m = string.IsNullOrWhiteSpace(Model) ? "" : $" — {Model}";
                var tag = Stolen ? " [STOLEN]" : "";
                return $"{Plate}{m}{tag}  ({Bolos.Count})";
            }
        }
    }
}
