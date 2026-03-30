using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class OfficerView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;
    readonly DispatcherTimer _shiftUiTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    DateTime? _shiftStartLocal;

    public OfficerView()
    {
        InitializeComponent();
        _shiftUiTimer.Tick += (_, _) => TickShiftDuration();
    }

    public void Bind(MdtConnectionManager? connection)
    {
        _shiftUiTimer.Stop();
        _connection = connection;
        _shiftStartLocal = null;
        if (connection?.Http != null)
        {
            _ = LoadAllAsync();
            _shiftUiTimer.Start();
        }
        else
        {
            ClearShiftUi();
            ClearMetrics();
        }

        ShiftHistory.Bind(connection);
    }

    void ClearShiftUi()
    {
        ShiftStartText.Text = "—";
        ShiftDurationText.Text = "";
        StartShiftBtn.IsEnabled = true;
        EndShiftBtn.IsEnabled = false;
        _shiftStartLocal = null;
    }

    void ClearMetrics()
    {
        MShifts.Text = MAvgDur.Text = MInc.Text = MCit.Text = MArr.Text = MTot.Text = MPer.Text = "—";
    }

    async Task LoadAllAsync()
    {
        await MdtBusyUi.RunAsync(ModuleBusy, "OFFICER MODULE", "Synchronizing profile and shift status…", async () =>
        {
            await LoadOfficerFieldsAsync();
            await ApplyShiftFromServerAsync();
        });
        await LoadMetricsAsync();
    }

    async Task LoadOfficerFieldsAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var j = await http.GetDataJsonAsync("officerInformationData") as JObject;
            ApplyOfficerJson(j);
        }
        catch { /* ignore */ }
    }

    void ApplyOfficerJson(JObject? j)
    {
        if (j == null) return;
        First.Text = j["firstName"]?.ToString() ?? "";
        Last.Text = j["lastName"]?.ToString() ?? "";
        Rank.Text = j["rank"]?.ToString() ?? "";
        CallSign.Text = j["callSign"]?.ToString() ?? "";
        Agency.Text = j["agency"]?.ToString() ?? "";
        Badge.Text = j["badgeNumber"]?.ToString() ?? "";
    }

    async Task ApplyShiftFromServerAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var j = await http.GetDataJsonAsync("currentShift") as JObject;
            Dispatcher.Invoke(() => UpdateShiftUi(j));
        }
        catch { Dispatcher.Invoke(ClearShiftUi); }
    }

    void UpdateShiftUi(JObject? shift)
    {
        var startTok = shift?["startTime"];
        if (startTok == null || startTok.Type == JTokenType.Null)
        {
            ShiftStartText.Text = "Off duty";
            ShiftDurationText.Text = "";
            StartShiftBtn.IsEnabled = true;
            EndShiftBtn.IsEnabled = false;
            _shiftStartLocal = null;
            return;
        }
        StartShiftBtn.IsEnabled = false;
        EndShiftBtn.IsEnabled = true;
        if (DateTime.TryParse(startTok.ToString(), out var st))
        {
            var local = st.ToLocalTime();
            _shiftStartLocal = local;
            ShiftStartText.Text = $"Started: {local:g}";
        }
        else
        {
            _shiftStartLocal = null;
            ShiftStartText.Text = $"Started: {startTok}";
        }
        TickShiftDuration();
    }

    void TickShiftDuration()
    {
        if (_shiftStartLocal == null || !EndShiftBtn.IsEnabled)
        {
            if (_shiftStartLocal == null) ShiftDurationText.Text = "";
            return;
        }
        var elapsed = DateTime.Now - _shiftStartLocal.Value;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        ShiftDurationText.Text = $"Duration: {elapsed:hh\\:mm\\:ss}";
    }

    async Task ModifyShiftAsync(string startOrEnd)
    {
        var http = _connection?.Http;
        if (http == null) return;
        var detail = string.Equals(startOrEnd, "start", StringComparison.OrdinalIgnoreCase)
            ? "Starting duty shift…"
            : "Ending duty shift…";
        await MdtBusyUi.RunAsync(ModuleBusy, "SHIFT CONTROL", detail, async () =>
        {
            try
            {
                var (status, text) = await http.PostActionAsync("modifyCurrentShift", startOrEnd);
                if (status == HttpStatusCode.OK && text == "OK")
                {
                    await ApplyShiftFromServerAsync();
                    Dispatcher.Invoke(ShiftHistory.RequestReload);
                }
                else
                    MdtShellEvents.LogCad("Shift: " + text);
            }
            catch (Exception ex) { MdtShellEvents.LogCad("Shift error: " + ex.Message); }
        }, minimumVisibleMs: 520);
    }

    async void StartShift_Click(object sender, RoutedEventArgs e) => await ModifyShiftAsync("start");

    async void EndShift_Click(object sender, RoutedEventArgs e) => await ModifyShiftAsync("end");

    async void FillFromGame_Click(object sender, RoutedEventArgs e)
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            await MdtBusyUi.RunAsync(ModuleBusy, "OFFICER PROFILE", "Pulling live data from session…", async () =>
            {
                var live = await http.GetDataJsonAsync("officerInformation") as JObject;
                await Dispatcher.InvokeAsync(() => ApplyOfficerJson(live));
                await SaveOfficerCoreAsync();
            }, minimumVisibleMs: 640);
            MdtShellEvents.RequestOfficerStripRefresh();
        }
        catch (Exception ex) { MdtShellEvents.LogCad("Officer fill error: " + ex.Message); }
    }

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        await SaveOfficerInternalAsync();
        MdtShellEvents.RequestOfficerStripRefresh();
    }

    async Task SaveOfficerInternalAsync()
    {
        if (_connection?.Http == null) return;
        var ok = false;
        await MdtBusyUi.RunAsync(ModuleBusy, "OFFICER PROFILE", "Writing personnel record…", async () =>
        {
            ok = await SaveOfficerCoreAsync();
        }, minimumVisibleMs: 620);
        if (ok)
            await Dispatcher.InvokeAsync(() => CadSaveSound.TryPlay());
    }

    async Task<bool> SaveOfficerCoreAsync()
    {
        var http = _connection?.Http;
        if (http == null) return false;
        var json = await Dispatcher.InvokeAsync(() =>
        {
            int? badge = int.TryParse(Badge.Text.Trim(), out var b) ? b : null;
            var o = new JObject
            {
                ["firstName"] = string.IsNullOrWhiteSpace(First.Text) ? null : First.Text.Trim(),
                ["lastName"] = string.IsNullOrWhiteSpace(Last.Text) ? null : Last.Text.Trim(),
                ["rank"] = string.IsNullOrWhiteSpace(Rank.Text) ? null : Rank.Text.Trim(),
                ["callSign"] = string.IsNullOrWhiteSpace(CallSign.Text) ? null : CallSign.Text.Trim(),
                ["agency"] = string.IsNullOrWhiteSpace(Agency.Text) ? null : Agency.Text.Trim(),
                ["badgeNumber"] = badge.HasValue ? JToken.FromObject(badge.Value) : JValue.CreateNull()
            };
            return o.ToString(Formatting.None);
        });
        try
        {
            var (status, text) = await http.PostActionAsync("updateOfficerInformationData", json);
            if (status != HttpStatusCode.OK || text != "OK")
            {
                MdtShellEvents.LogCad("Officer save: " + text);
                return false;
            }

            MdtShellEvents.LogCad("Officer profile saved.");
            return true;
        }
        catch (Exception ex)
        {
            MdtShellEvents.LogCad("Officer save error: " + ex.Message);
            return false;
        }
    }

    async void RefreshMetrics_Click(object sender, RoutedEventArgs e) => await LoadMetricsAsync();

    async Task LoadMetricsAsync()
    {
        await MdtBusyUi.RunAsync(ModuleBusy, "CAREER STATS", "Loading officer metrics…", LoadMetricsCoreAsync);
    }

    async Task LoadMetricsCoreAsync()
    {
        var http = _connection?.Http;
        if (http == null)
        {
            await Dispatcher.InvokeAsync(ClearMetrics);
            return;
        }

        try
        {
            var j = await http.GetDataJsonAsync("officerMetrics") as JObject;
            if (j == null) return;
            var avgMs = j["averageShiftDurationMs"]?.Value<double>() ?? 0;
            var avg = TimeSpan.FromMilliseconds(avgMs);
            await Dispatcher.InvokeAsync(() =>
            {
                MShifts.Text = j["totalShifts"]?.ToString() ?? "0";
                MAvgDur.Text = avg.TotalHours >= 1 ? $"{(int)avg.TotalHours}h {avg.Minutes}m" : $"{avg.Minutes}m {avg.Seconds}s";
                MInc.Text = j["totalIncidentReports"]?.ToString();
                MCit.Text = j["totalCitationReports"]?.ToString();
                MArr.Text = j["totalArrestReports"]?.ToString();
                MTot.Text = j["totalReports"]?.ToString();
                MPer.Text = j["reportsPerShift"]?.ToString();
            });
        }
        catch { await Dispatcher.InvokeAsync(ClearMetrics); }
    }
}
