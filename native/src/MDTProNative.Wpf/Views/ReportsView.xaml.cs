using System.Collections;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Controls;
using MDTProNative.Wpf.Views.Reports;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views
{
public partial class ReportsView : UserControl, IMdtBoundView
{
    /// <summary>Raised when a report pane may start/stop being the logical editor while <see cref="FormHost"/> is empty (e.g. property form pop-out).</summary>
    internal static event Action? SaveEnableStateMayHaveChanged;

    internal static void RaiseSaveEnableStateMayHaveChanged() => SaveEnableStateMayHaveChanged?.Invoke();

    /// <summary>Set by shell navigation (e.g. Court report link) before the view is created/bound.</summary>
    internal static (string Id, string? TypeKey)? PendingOpenReport;

    /// <summary>Set by shell navigation from Person search: new draft + prefill offender / injured party.</summary>
    internal sealed record PendingPersonSearchReport(string TypeKey, string PedName, string? VehicleLicensePlate);

    internal static PendingPersonSearchReport? PendingNewReportFromPersonSearch;

    internal sealed record PendingVehicleSearchReport(string TypeKey, JObject Vehicle);

    internal static PendingVehicleSearchReport? PendingNewReportFromVehicleSearch;

    const string FormsClrNamespace = "MDTProNative.Wpf.Views.Reports.Forms";

    MdtConnectionManager? _connection;
    string? _selectedType;
    bool _loadingList;
    bool _suppressSelection;
    bool _suppressCategoryReload;
    bool _layoutPersistWired;

    readonly Dictionary<string, IReportFormPane> _formPanes = new(StringComparer.Ordinal);

    static readonly ReportCategory[] Categories =
    [
        new("Incident", "incident"),
        new("Citation", "citation"),
        new("Arrest", "arrest"),
        new("Impound", "impound"),
        new("Traffic incident", "trafficIncident"),
        new("Injury", "injury"),
        new("Property / evidence", "propertyEvidence"),
    ];

    public ReportsView()
    {
        InitializeComponent();
        Loaded += OnReportsLoaded;
        Unloaded += (_, _) => SaveEnableStateMayHaveChanged -= OnSaveEnableStateMayHaveChanged;
        SaveEnableStateMayHaveChanged += OnSaveEnableStateMayHaveChanged;
        CategoryCombo.ItemsSource = Categories;
        CategoryCombo.SelectedIndex = 0;
        RefreshBtn.Click += async (_, _) => await ReloadListAsync();
        NewBtn.Click += async (_, _) => await NewDraftAsync();
        SaveBtn.Click += async (_, _) => await SaveAsync();
        CategoryCombo.SelectionChanged += async (_, _) =>
        {
            if (_suppressCategoryReload) return;
            await ReloadListAsync();
        };
        ReportList.SelectionChanged += (_, _) => OnReportSelected();
    }

    void OnSaveEnableStateMayHaveChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(OnSaveEnableStateMayHaveChanged));
            return;
        }

        UpdateSaveEnabled();
    }

    void OnReportsLoaded(object sender, RoutedEventArgs e)
    {
        if (_layoutPersistWired) return;
        _layoutPersistWired = true;
        UiLayoutHooks.WireReports(this);
    }

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        var online = connection?.Http != null;
        RefreshBtn.IsEnabled = NewBtn.IsEnabled = CategoryCombo.IsEnabled = online;
        SaveBtn.IsEnabled = false;
        ReportList.ItemsSource = null;
        foreach (var p in _formPanes.Values)
            p.CloseDetachSurfaces();
        FormHost.Content = null;
        if (!online)
            _formPanes.Clear();
        else
        {
            foreach (var p in _formPanes.Values)
                p.Bind(connection);
        }

        EditorHint.Text = online
            ? "Choose TYPE, then NEW REPORT or select a saved report. Edit the form, then SAVE TO MDT."
            : "Connect (host/port + Connect) before you can load, create, or save reports.";
        UpdateFormScrollVisibility();
        UpdateSaveEnabled();
        if (online)
        {
            if (PendingOpenReport is { } po)
            {
                PendingOpenReport = null;
                _ = OpenReportFromNavigationAsync(po);
            }
            else if (PendingNewReportFromPersonSearch is { } pn)
            {
                PendingNewReportFromPersonSearch = null;
                _ = OpenNewReportFromPersonSearchAsync(pn);
            }
            else if (PendingNewReportFromVehicleSearch is { } pv)
            {
                PendingNewReportFromVehicleSearch = null;
                _ = OpenNewReportFromVehicleSearchAsync(pv);
            }
            else
                _ = ReloadListAsync();
        }
        else
            _ = Dispatcher.InvokeAsync(ShowFormPlaceholder);
    }

    async Task OpenReportFromNavigationAsync((string Id, string? TypeKey) target)
    {
        await MdtBusyUi.RunAsync(ModuleBusy, "REPORTS", "Opening report…", async () =>
        {
            var opened = await TryOpenReportByIdAsync(target.Id, target.TypeKey).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!opened)
                    MessageBox.Show(
                        $"Could not find report “{target.Id}” in any saved list for this terminal.\n\nIf it exists under another TYPE, pick the correct category and use REFRESH LIST.",
                        "Reports",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
            });
        }, minimumVisibleMs: 320);
    }

    /// <summary>Loads lists until <paramref name="reportId"/> is found; returns false if not in any category.</summary>
    async Task<bool> TryOpenReportByIdAsync(string reportId, string? preferredTypeKey)
    {
        static bool CategoryExists(string? key) =>
            !string.IsNullOrEmpty(key) && Categories.Any(c => string.Equals(c.TypeKey, key, StringComparison.Ordinal));

        var order = new List<string>();
        if (CategoryExists(preferredTypeKey))
            order.Add(preferredTypeKey!);
        foreach (var c in Categories)
        {
            if (!order.Contains(c.TypeKey, StringComparer.Ordinal))
                order.Add(c.TypeKey);
        }

        foreach (var typeKey in order)
        {
            await ReloadListCoreAsync(typeKey).ConfigureAwait(false);
            var hit = await Dispatcher.InvokeAsync(() =>
            {
                foreach (var item in ReportList.Items)
                {
                    if (item is ReportRow row && string.Equals(row.Id, reportId, StringComparison.OrdinalIgnoreCase))
                    {
                        _suppressSelection = true;
                        ReportList.SelectedItem = row;
                        _suppressSelection = false;
                        OnReportSelected();
                        return true;
                    }
                }

                return false;
            });
            if (hit) return true;
        }

        return false;
    }

    static string? FormTypeNameFor(string typeKey) => typeKey switch
    {
        "incident" => "IncidentReportForm",
        "citation" => "CitationReportForm",
        "arrest" => "ArrestReportForm",
        "impound" => "ImpoundReportForm",
        "trafficIncident" => "TrafficIncidentReportForm",
        "injury" => "InjuryReportForm",
        "propertyEvidence" => "PropertyEvidenceReportForm",
        _ => null,
    };

    Type? TryResolveFormType(string typeKey)
    {
        var simple = FormTypeNameFor(typeKey);
        if (string.IsNullOrEmpty(simple)) return null;
        return typeof(ReportsView).Assembly.GetType($"{FormsClrNamespace}.{simple}");
    }

    IReportFormPane? GetOrCreateFormPane(string? typeKey)
    {
        if (string.IsNullOrEmpty(typeKey)) return null;
        if (_formPanes.TryGetValue(typeKey, out var existing))
            return existing;

        var t = TryResolveFormType(typeKey);
        if (t == null || !typeof(UserControl).IsAssignableFrom(t) || !typeof(IReportFormPane).IsAssignableFrom(t))
            return null;

        object raw;
        try
        {
            raw = Activator.CreateInstance(t)!;
        }
        catch
        {
            return null;
        }

        if (raw is not IReportFormPane inst || raw is not UserControl)
            return null;

        inst.Bind(_connection);
        _formPanes[typeKey] = inst;
        return inst;
    }

    void UpdateFormScrollVisibility()
    {
        FormScrollViewer.Visibility = FormHost.Content != null ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateSaveEnabled()
    {
        var online = _connection?.Http != null;
        if (!online)
        {
            SaveBtn.IsEnabled = false;
            return;
        }

        if (FormHost.Content is UserControl ucPane && ucPane is IReportFormPane)
        {
            SaveBtn.IsEnabled = true;
            return;
        }

        var typeKey = CategoryCombo.SelectedValue as string;
        if (!string.IsNullOrEmpty(typeKey)
            && _formPanes.TryGetValue(typeKey, out var detached)
            && detached.IsDetachedFromHost)
        {
            SaveBtn.IsEnabled = true;
            return;
        }

        SaveBtn.IsEnabled = false;
    }

    void ShowFormPlaceholder()
    {
        FormHost.Content = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("CadMuted"),
            FontSize = 12,
            Text = "Could not load the structured form for this type. Reconnect or open the browser MDT for this report type.",
            Margin = new Thickness(0, 8, 0, 0)
        };
        UpdateFormScrollVisibility();
        UpdateSaveEnabled();
    }

    void AttachFormHost(string? typeKey)
    {
        var pane = GetOrCreateFormPane(typeKey);
        if (pane is UserControl uc)
        {
            FormHost.Content = uc;
            UpdateFormScrollVisibility();
            UpdateSaveEnabled();
            return;
        }

        ShowFormPlaceholder();
    }

    void ClearReportEditorSurface()
    {
        foreach (var p in _formPanes.Values)
            p.CloseDetachSurfaces();
        FormHost.Content = null;
        UpdateFormScrollVisibility();
        UpdateSaveEnabled();
    }

    void SetIdleEditorHint()
    {
        var n = 0;
        if (ReportList.ItemsSource is IEnumerable e)
        {
            foreach (var _ in e) n++;
        }

        EditorHint.Text = n == 0
            ? "No reports yet for this type. Click NEW REPORT to create a draft with a new ID."
            : $"{n} report(s). Select one to edit, or click NEW REPORT for a new draft.";
    }

    async Task ReloadListAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        _loadingList = true;
        try
        {
            await MdtBusyUi.RunAsync(ModuleBusy, "REPORTS", "Loading saved reports…", async () => await ReloadListCoreAsync());
        }
        finally
        {
            _loadingList = false;
        }
    }

    async Task ReloadListCoreAsync(string? typeKeyOverride = null, bool preserveOpenEditor = false)
    {
        var http = _connection?.Http;
        if (http == null) return;
        string? sel = typeKeyOverride;
        if (string.IsNullOrEmpty(sel))
        {
            await Dispatcher.InvokeAsync(() => { sel = CategoryCombo.SelectedValue as string; });
        }
        _selectedType = sel;
        var dataPath = _selectedType != null ? NativeReportDraftFactory.DataPathFor(_selectedType) : null;
        if (string.IsNullOrEmpty(dataPath)) return;

        try
        {
            var tok = await http.GetDataJsonAsync(dataPath).ConfigureAwait(false);
            var rows = new List<ReportRow>();
            if (tok is JArray arr)
            {
                foreach (var o in arr.OfType<JObject>())
                {
                    var id = o["Id"]?.ToString() ?? "(no id)";
                    var ts = NativeMdtFormat.IsoDate(o["TimeStamp"]);
                    rows.Add(new ReportRow(id, $"{id}  ·  {ts}", o));
                }
            }

            rows.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));

            await Dispatcher.InvokeAsync(() =>
            {
                _suppressCategoryReload = true;
                try
                {
                    if (typeKeyOverride != null)
                        CategoryCombo.SelectedValue = typeKeyOverride;
                }
                finally
                {
                    _suppressCategoryReload = false;
                }

                _suppressSelection = true;
                ReportList.ItemsSource = rows;
                ReportList.SelectedItem = null;
                _suppressSelection = false;
                if (!preserveOpenEditor)
                {
                    ClearReportEditorSurface();
                    SetIdleEditorHint();
                }
            });
        }
        catch (Exception ex)
        {
            MdtShellEvents.LogCad("Reports: list load failed — " + ex.Message);
            await Dispatcher.InvokeAsync(() =>
            {
                ReportList.ItemsSource = Array.Empty<ReportRow>();
                if (!preserveOpenEditor)
                    ClearReportEditorSurface();
                EditorHint.Text = "Could not load report list: " + ex.Message;
            });
        }
    }

    void OnReportSelected()
    {
        if (_suppressSelection || _loadingList) return;
        if (ReportList.SelectedItem is ReportRow row)
        {
            AttachFormHost(_selectedType);
            GetOrCreateFormPane(_selectedType)?.LoadFromReport((JObject)row.Body.DeepClone());
            EditorHint.Text = "Editing selected report — change fields as needed, then SAVE TO MDT.";
        }
        else
        {
            ClearReportEditorSurface();
            SetIdleEditorHint();
        }
    }

    async Task NewDraftAsync()
    {
        var http = _connection?.Http;
        if (http == null)
        {
            MessageBox.Show("Connect to MDT Pro first.", "Reports", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var type = CategoryCombo.SelectedValue as string;
        if (string.IsNullOrEmpty(type)) return;

        try
        {
            await MdtBusyUi.RunAsync(ModuleBusy, "REPORTS", "Allocating new report draft…", async () =>
            {
                var draft = await NativeReportDraftFactory.CreateDraftAsync(http, type).ConfigureAwait(false);
                if (draft == null)
                {
                    await Dispatcher.InvokeAsync(() =>
                        MessageBox.Show("Unknown report type.", "Reports", MessageBoxButton.OK, MessageBoxImage.Warning));
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    _suppressSelection = true;
                    ReportList.SelectedItem = null;
                    _suppressSelection = false;
                    AttachFormHost(type);
                    GetOrCreateFormPane(type)?.LoadFromReport((JObject)draft.DeepClone());
                    EditorHint.Text = "New draft — complete the form, then SAVE TO MDT. Arrest saves can take up to ~45s (game thread).";
                    MdtShellEvents.LogCad("Reports: new draft " + (draft["Id"]?.ToString() ?? "?"));
                });
            }, minimumVisibleMs: 480);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not build draft.\n\n" + ex.Message, "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    async Task OpenNewReportFromPersonSearchAsync(PendingPersonSearchReport pr)
    {
        var http = _connection?.Http;
        if (http == null)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show("Connect to MDT Pro first.", "Reports", MessageBoxButton.OK, MessageBoxImage.Information));
            return;
        }

        if (NativeReportDraftFactory.DataPathFor(pr.TypeKey) == null)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Unknown report type “{pr.TypeKey}”.", "Reports", MessageBoxButton.OK, MessageBoxImage.Warning));
            await Dispatcher.InvokeAsync(() => _ = ReloadListAsync());
            return;
        }

        try
        {
            await MdtBusyUi.RunAsync(ModuleBusy, "REPORTS", "New report from person search…", async () =>
            {
                var draft = await NativeReportDraftFactory.CreateDraftAsync(http, pr.TypeKey).ConfigureAwait(false);
                if (draft == null)
                {
                    await Dispatcher.InvokeAsync(() =>
                        MessageBox.Show("Could not create draft for this report type.", "Reports", MessageBoxButton.OK, MessageBoxImage.Warning));
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    _suppressCategoryReload = true;
                    try
                    {
                        CategoryCombo.SelectedValue = pr.TypeKey;
                    }
                    finally
                    {
                        _suppressCategoryReload = false;
                    }

                    _suppressSelection = true;
                    ReportList.SelectedItem = null;
                    _suppressSelection = false;
                    AttachFormHost(pr.TypeKey);
                    var pane = GetOrCreateFormPane(pr.TypeKey);
                    pane?.LoadFromReport((JObject)draft.DeepClone());
                    pane?.ApplyPersonSearchPrefill(pr.PedName, pr.VehicleLicensePlate);
                    EditorHint.Text =
                        $"New draft — subject “{pr.PedName}” prefilled from person search. Complete the form, then SAVE TO MDT.";
                    MdtShellEvents.LogCad("Reports: new draft from person search " + pr.TypeKey + " · " + pr.PedName);
                    UpdateFormScrollVisibility();
                    UpdateSaveEnabled();
                });

                await ReloadListCoreAsync(null, preserveOpenEditor: true).ConfigureAwait(false);
            }, minimumVisibleMs: 480);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open new report from person search.\n\n" + ex.Message, "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    async Task OpenNewReportFromVehicleSearchAsync(PendingVehicleSearchReport pr)
    {
        var http = _connection?.Http;
        if (http == null)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show("Connect to MDT Pro first.", "Reports", MessageBoxButton.OK, MessageBoxImage.Information));
            return;
        }

        if (NativeReportDraftFactory.DataPathFor(pr.TypeKey) == null)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Unknown report type “{pr.TypeKey}”.", "Reports", MessageBoxButton.OK, MessageBoxImage.Warning));
            await Dispatcher.InvokeAsync(() => _ = ReloadListAsync());
            return;
        }

        try
        {
            await MdtBusyUi.RunAsync(ModuleBusy, "REPORTS", "New report from vehicle search…", async () =>
            {
                var draft = await NativeReportDraftFactory.CreateDraftAsync(http, pr.TypeKey).ConfigureAwait(false);
                if (draft == null)
                {
                    await Dispatcher.InvokeAsync(() =>
                        MessageBox.Show("Could not create draft for this report type.", "Reports", MessageBoxButton.OK, MessageBoxImage.Warning));
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    _suppressCategoryReload = true;
                    try
                    {
                        CategoryCombo.SelectedValue = pr.TypeKey;
                    }
                    finally
                    {
                        _suppressCategoryReload = false;
                    }

                    _suppressSelection = true;
                    ReportList.SelectedItem = null;
                    _suppressSelection = false;
                    AttachFormHost(pr.TypeKey);
                    var pane = GetOrCreateFormPane(pr.TypeKey);
                    pane?.LoadFromReport((JObject)draft.DeepClone());
                    pane?.ApplyVehicleSearchPrefill(pr.Vehicle);
                    EditorHint.Text =
                        "New draft — vehicle fields prefilled from vehicle search. Complete the form, then SAVE TO MDT.";
                    MdtShellEvents.LogCad("Reports: new draft from vehicle search " + pr.TypeKey);
                    UpdateFormScrollVisibility();
                    UpdateSaveEnabled();
                });

                await ReloadListCoreAsync(null, preserveOpenEditor: true).ConfigureAwait(false);
            }, minimumVisibleMs: 480);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open new report from vehicle search.\n\n" + ex.Message, "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    async Task SaveAsync()
    {
        var http = _connection?.Http;
        if (http == null)
        {
            MessageBox.Show("Connect to MDT Pro first.", "Reports", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var type = CategoryCombo.SelectedValue as string;
        var postPath = type != null ? NativeReportPostPaths.PostPathFor(type) : null;
        if (string.IsNullOrEmpty(postPath))
        {
            MessageBox.Show("Unknown report type.", "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        JObject body;
        try
        {
            var pane = GetOrCreateFormPane(type);
            if (pane == null)
            {
                MessageBox.Show("Could not load the structured form for this report type.", "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            body = pane.BuildReport();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not build report from the form.\n\n" + ex.Message, "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var id = body["Id"]?.ToString()?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            MessageBox.Show("Report Id is empty. Use NEW REPORT to create a draft.", "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var json = body.ToString(Formatting.None);
        try
        {
            await MdtBusyUi.RunAsync(ModuleBusy, "REPORTS", "Transmitting report to MDT host…", async () =>
            {
                MdtShellEvents.LogCad("Reports: saving " + id + " → /post/" + postPath);
                var (status, text) = await http.PostActionAsync(postPath, json).ConfigureAwait(false);
                var trimmed = text?.Trim() ?? "";
                if (status == HttpStatusCode.OK && (string.IsNullOrEmpty(trimmed) || trimmed == "OK"))
                {
                    MdtShellEvents.LogCad("Reports: saved " + id);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        CadSaveSound.TryPlay();
                        MessageBox.Show("Report saved.", "Reports", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                    await ReloadListCoreAsync();
                    var savedId = body["Id"]?.ToString();
                    if (!string.IsNullOrEmpty(savedId))
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            foreach (var item in ReportList.Items)
                            {
                                if (item is ReportRow row && string.Equals(row.Id, savedId, StringComparison.Ordinal))
                                {
                                    ReportList.SelectedItem = row;
                                    break;
                                }
                            }
                        });
                    }

                    return;
                }

                string userMsg = trimmed;
                try
                {
                    var jo = JObject.Parse(trimmed);
                    if (jo["error"] != null)
                        userMsg = jo["error"]!.ToString();
                    else if (jo["success"]?.Value<bool>() == false && jo["error"] != null)
                        userMsg = jo["error"]!.ToString();
                }
                catch { /* plain text */ }

                MdtShellEvents.LogCad("Reports: save failed " + id + " HTTP " + (int)status + " — " + userMsg);
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Save failed ({(int)status}).\n\n{userMsg}", "Reports", MessageBoxButton.OK, MessageBoxImage.Warning));
            }, minimumVisibleMs: 520);
        }
        catch (Exception ex)
        {
            MdtShellEvents.LogCad("Reports: save error — " + ex.Message);
            MessageBox.Show("Request failed.\n\n" + ex.Message, "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    sealed record ReportCategory(string Label, string TypeKey);

    sealed class ReportRow
    {
        public ReportRow(string id, string display, JObject body)
        {
            Id = id;
            Display = display;
            Body = body;
        }

        public string Id { get; }
        public string Display { get; }
        public JObject Body { get; }
    }
}
}
