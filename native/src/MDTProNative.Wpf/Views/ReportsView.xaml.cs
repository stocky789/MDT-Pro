using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Reports;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views
{
public partial class ReportsView : UserControl, IMdtBoundView
{
    const string FormsClrNamespace = "MDTProNative.Wpf.Views.Reports.Forms";

    MdtConnectionManager? _connection;
    string? _selectedType;
    bool _loadingList;
    bool _suppressSelection;

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
        CategoryCombo.ItemsSource = Categories;
        CategoryCombo.SelectedIndex = 0;
        RefreshBtn.Click += async (_, _) => await ReloadListAsync();
        NewBtn.Click += async (_, _) => await NewDraftAsync();
        SaveBtn.Click += async (_, _) => await SaveAsync();
        CategoryCombo.SelectionChanged += async (_, _) => await ReloadListAsync();
        ReportList.SelectionChanged += (_, _) => OnReportSelected();
    }

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        var online = connection?.Http != null;
        RefreshBtn.IsEnabled = NewBtn.IsEnabled = SaveBtn.IsEnabled = CategoryCombo.IsEnabled = online;
        ReportList.ItemsSource = null;
        JsonEditor.Text = "";
        FormHost.Content = null;
        if (!online)
            _formPanes.Clear();
        else
        {
            foreach (var p in _formPanes.Values)
                p.Bind(connection);
        }

        EditorHint.Text = online
            ? "Choose TYPE, then NEW REPORT or select a saved report. Use FORM or JSON, then SAVE TO MDT."
            : "Connect (host/port + Connect) before you can load, create, or save reports.";
        if (online)
            _ = ReloadListAsync();
        else
            _ = Dispatcher.InvokeAsync(ShowFormPlaceholder);
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

    void ShowFormPlaceholder()
    {
        FormHost.Content = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("CadMuted"),
            FontSize = 12,
            Text = "Could not load the structured form for this type. Use the JSON tab or reconnect.",
            Margin = new Thickness(0, 8, 0, 0)
        };
    }

    void AttachFormHost(string? typeKey)
    {
        var pane = GetOrCreateFormPane(typeKey);
        if (pane is UserControl uc)
        {
            FormHost.Content = uc;
            return;
        }

        ShowFormPlaceholder();
    }

    async Task ReloadListAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        _selectedType = CategoryCombo.SelectedValue as string;
        var dataPath = _selectedType != null ? NativeReportDraftFactory.DataPathFor(_selectedType) : null;
        if (string.IsNullOrEmpty(dataPath)) return;

        _loadingList = true;
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
                _suppressSelection = true;
                ReportList.ItemsSource = rows;
                _suppressSelection = false;
                JsonEditor.Text = "";
                AttachFormHost(_selectedType);
                GetOrCreateFormPane(_selectedType)?.Clear();
                EditorHint.Text = rows.Count == 0 ? "No reports yet for this type. Use New report to start." : $"{rows.Count} report(s). Select one to edit.";
            });
        }
        catch (Exception ex)
        {
            MdtShellEvents.LogCad("Reports: list load failed — " + ex.Message);
            await Dispatcher.InvokeAsync(() =>
            {
                ReportList.ItemsSource = Array.Empty<ReportRow>();
                JsonEditor.Text = "";
                AttachFormHost(_selectedType);
                GetOrCreateFormPane(_selectedType)?.Clear();
                EditorHint.Text = "Could not load report list: " + ex.Message;
            });
        }
        finally
        {
            _loadingList = false;
        }
    }

    void OnReportSelected()
    {
        if (_suppressSelection || _loadingList) return;
        if (ReportList.SelectedItem is ReportRow row)
        {
            ReportEditorTabs.SelectedIndex = 0;
            JsonEditor.Text = row.Body.ToString(Formatting.Indented);
            AttachFormHost(_selectedType);
            GetOrCreateFormPane(_selectedType)?.LoadFromReport((JObject)row.Body.DeepClone());
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
            var draft = await NativeReportDraftFactory.CreateDraftAsync(http, type).ConfigureAwait(false);
            if (draft == null)
            {
                MessageBox.Show("Unknown report type.", "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                _suppressSelection = true;
                ReportList.SelectedItem = null;
                _suppressSelection = false;
                ReportEditorTabs.SelectedIndex = 0;
                JsonEditor.Text = draft.ToString(Formatting.Indented);
                AttachFormHost(type);
                GetOrCreateFormPane(type)?.LoadFromReport((JObject)draft.DeepClone());
                EditorHint.Text = "New draft — edit FORM or JSON, then SAVE TO MDT. Arrest saves can take up to ~45s (game thread).";
                MdtShellEvents.LogCad("Reports: new draft " + (draft["Id"]?.ToString() ?? "?"));
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not build draft.\n\n" + ex.Message, "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            var formTab = ReportEditorTabs.SelectedIndex == 0;
            if (formTab)
            {
                var pane = GetOrCreateFormPane(type);
                if (pane != null)
                    body = pane.BuildReport();
                else
                    body = JObject.Parse(JsonEditor.Text);
            }
            else
                body = JObject.Parse(JsonEditor.Text);
        }
        catch (Exception ex)
        {
            var src = ReportEditorTabs.SelectedIndex == 0 ? "form" : "JSON";
            MessageBox.Show($"Could not build report from {src}.\n\n" + ex.Message, "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var id = body["Id"]?.ToString()?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            MessageBox.Show("Report Id is empty. Use NEW REPORT or paste JSON with an Id.", "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var json = body.ToString(Formatting.None);
        try
        {
            MdtShellEvents.LogCad("Reports: saving " + id + " → /post/" + postPath);
            var (status, text) = await http.PostActionAsync(postPath, json).ConfigureAwait(false);
            var trimmed = text?.Trim() ?? "";
            if (status == HttpStatusCode.OK && (string.IsNullOrEmpty(trimmed) || trimmed == "OK"))
            {
                MdtShellEvents.LogCad("Reports: saved " + id);
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Report saved.", "Reports", MessageBoxButton.OK, MessageBoxImage.Information));
                await ReloadListAsync();
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
            MessageBox.Show($"Save failed ({(int)status}).\n\n{userMsg}", "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
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
