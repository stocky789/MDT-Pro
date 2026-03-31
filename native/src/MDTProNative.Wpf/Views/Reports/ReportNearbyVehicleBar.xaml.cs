using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports;

/// <summary>Toolbar strip: nearby vehicles + context vehicle, applies full CDF snapshot to a report via <see cref="VehicleDetailReady"/>.</summary>
public partial class ReportNearbyVehicleBar : UserControl
{
    MdtConnectionManager? _connection;

    public ReportNearbyVehicleBar()
    {
        InitializeComponent();
        RefreshBtn.Click += async (_, _) => await RunRefreshAsync();
        ContextBtn.Click += async (_, _) => await RunApplyContextAsync();
        ApplyBtn.Click += async (_, _) => await RunApplySelectedAsync();
    }

    /// <summary>Raised on the UI thread with the JSON from <c>/data/specificVehicle</c>.</summary>
    public event EventHandler<JObject>? VehicleDetailReady;

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        var online = connection?.Http != null;
        IsEnabled = online;
        if (!online)
        {
            NearbyCombo.ItemsSource = null;
            SetStatus(null, null);
            return;
        }

        _ = RunRefreshAsync();
    }

    async Task RunRefreshAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        SetBusy(true);
        SetStatus(null, null);
        try
        {
            var fetch = await ReportNearbyVehiclesClient.FetchNearbyAsync(http).ConfigureAwait(false);
            var rows = fetch.Items;
            await Dispatcher.InvokeAsync(() =>
            {
                NearbyCombo.ItemsSource = rows.Select(r => new NearbyRowVm(r)).ToList();
                NearbyCombo.DisplayMemberPath = nameof(NearbyRowVm.Display);
                NearbyCombo.SelectedValuePath = nameof(NearbyRowVm.Plate);
                if (rows.Count > 0)
                    NearbyCombo.SelectedIndex = 0;
                if (fetch.ScanDeferred)
                {
                    if (rows.Count == 0)
                        SetStatus("GTA V must be focused for a live scan. Alt-tab into the game, then Refresh.", isError: true);
                    else
                        SetStatus("Scan was deferred — list may be outdated. Focus GTA V and Refresh before Apply.", isError: false);
                }
                else
                    SetStatus(null, null);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => SetStatus(ex.Message, isError: true));
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => SetBusy(false));
        }
    }

    async Task RunApplyContextAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        SetBusy(true);
        SetStatus("Loading context vehicle…", null);
        try
        {
            var jo = await ReportNearbyVehiclesClient.FetchSpecificVehicleAsync(http, "context").ConfigureAwait(false);
            await FinishApplyAsync(jo, "No context vehicle — stand by a stopped car or set context in-game.");
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => SetBusy(false));
        }
    }

    async Task RunApplySelectedAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        string? plate = null;
        await Dispatcher.InvokeAsync(() =>
        {
            plate = NearbyCombo.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(plate) && NearbyCombo.SelectedItem is NearbyRowVm row)
                plate = row.Plate;
            if (string.IsNullOrWhiteSpace(plate) && NearbyCombo.Items.Count > 0)
            {
                NearbyCombo.SelectedIndex = 0;
                plate = NearbyCombo.SelectedValue as string
                    ?? (NearbyCombo.SelectedItem as NearbyRowVm)?.Plate;
            }
        });

        if (string.IsNullOrWhiteSpace(plate))
        {
            await Dispatcher.InvokeAsync(() =>
                SetStatus("No nearby vehicles — drive closer or press Refresh.", isError: true));
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            SetBusy(true);
            SetStatus("Loading vehicle record…", null);
        });
        try
        {
            var jo = await ReportNearbyVehiclesClient.FetchSpecificVehicleAsync(http, plate).ConfigureAwait(false);
            await FinishApplyAsync(jo, "Could not resolve that plate (try Refresh, then Apply again).");
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => SetBusy(false));
        }
    }

    async Task FinishApplyAsync(JObject? jo, string emptyMessage)
    {
        if (jo == null)
        {
            await Dispatcher.InvokeAsync(() => SetStatus(emptyMessage, isError: true));
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            SetStatus(null, null);
            VehicleDetailReady?.Invoke(this, jo);
        });
    }

    void SetBusy(bool busy)
    {
        RefreshBtn.IsEnabled = !busy && _connection?.Http != null;
        ContextBtn.IsEnabled = !busy && _connection?.Http != null;
        ApplyBtn.IsEnabled = !busy && _connection?.Http != null;
    }

    void SetStatus(string? message, bool? isError)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            StatusText.Visibility = Visibility.Collapsed;
            StatusText.Text = "";
            return;
        }

        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = message;
        StatusText.Foreground = isError == true
            ? (Brush)FindResource("CadUrgent")
            : (Brush)FindResource("ReportDocInkBrush");
    }

    sealed class NearbyRowVm(ReportNearbyVehiclesClient.NearbySummary s)
    {
        public string Plate { get; } = s.Plate;

        public string Display
        {
            get
            {
                var m = string.IsNullOrWhiteSpace(s.ModelDisplay) ? "" : $" — {s.ModelDisplay}";
                var d = s.DistanceMeters.HasValue ? $" ({s.DistanceMeters.Value:F1} m)" : "";
                var tag = s.Stolen ? " [STOLEN]" : "";
                return $"{Plate}{m}{d}{tag}";
            }
        }
    }
}
