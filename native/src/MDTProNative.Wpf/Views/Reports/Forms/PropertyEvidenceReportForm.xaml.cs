using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MDTProNative.Client;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views;
using MDTProNative.Wpf.Views.Reports;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

public sealed class SeizedDrugRow : INotifyPropertyChanged
{
    string _drugType = "";
    string _quantity = "";

    public string DrugType
    {
        get => _drugType;
        set
        {
            if (_drugType == value) return;
            _drugType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayLine));
        }
    }

    public string Quantity
    {
        get => _quantity;
        set
        {
            if (_quantity == value) return;
            _quantity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayLine));
        }
    }

    /// <summary>Matches browser list display: <c>type</c> or <c>type (qty)</c>.</summary>
    public string DisplayLine => string.IsNullOrWhiteSpace(Quantity) ? DrugType : $"{DrugType} ({Quantity})";

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SeizedFirearmRow
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string DisplayLine => string.IsNullOrWhiteSpace(Label) ? Id : Label;
}

/// <summary>Label/value row for seizure combo boxes (same semantics as browser <c>propertyEvidenceSection.js</c>).</summary>
public sealed class SeizureListItem
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

public partial class PropertyEvidenceReportForm : UserControl, IReportFormPane
{
    public bool IsDetachedFromHost => _popoutWindow != null;

    public void CloseDetachSurfaces() => ClosePopoutIfOpen();

    static readonly string[] ScheduleOrder = ["I", "II", "III", "IV", "V", "Other", "Paraphernalia"];

    static readonly Dictionary<string, string> ScheduleLabels = new(StringComparer.Ordinal)
    {
        ["I"] = "Schedule I",
        ["II"] = "Schedule II",
        ["III"] = "Schedule III",
        ["IV"] = "Schedule IV",
        ["V"] = "Schedule V",
        ["Other"] = "Other / unspecified",
        ["Paraphernalia"] = "Paraphernalia"
    };

    readonly ObservableCollection<SeizedDrugRow> _drugs = new();
    readonly ObservableCollection<SeizedFirearmRow> _firearms = new();
    readonly List<DrugTypeEntry> _allDrugTypes = new();
    MdtConnectionManager? _connection;
    bool _suppressScheduleChange;
    Window? _popoutWindow;
    ContentControl? _formHostWhilePopped;

    struct DrugTypeEntry
    {
        public string Id;
        public string Name;
        public string Schedule;
    }

    public PropertyEvidenceReportForm()
    {
        InitializeComponent();
        SeizedDrugsList.ItemsSource = _drugs;
        SeizedFirearmsList.ItemsSource = _firearms;
        AddDrugBtn.Click += (_, _) => AddDrugFromSelection();
        AddFirearmBtn.Click += (_, _) => AddFirearmFromSelection();
        DrugScheduleCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressScheduleChange) return;
            RepopulateDrugTypeCombo();
        };
        RecentIdsBtn.Click += async (_, _) => await OpenRecentIdsAsync();
        ExportPdfBtn.Click += (_, _) => ExportPdf();
        PopoutBtn.Click += (_, _) => TogglePopout();
        ApplyBrandingToChrome(ReportBrandingFallback.ActiveTemplate, "regional_crime_lab (offline)");
    }

    ReportFormBaseControls BaseControls => new()
    {
        Id = GeneralIdBox,
        ShortYear = GeneralShortYearBox,
        Date = GeneralDatePicker,
        Time = GeneralTimeBox,
        Status = StatusCombo,
        Notes = NotesBox,
        OffFirst = OffFirstBox,
        OffLast = OffLastBox,
        OffBadge = OffBadgeBox,
        OffRank = OffRankBox,
        OffCall = OffCallBox,
        OffAgency = OffAgencyBox,
        LocArea = LocAreaBox,
        LocStreet = LocStreetBox,
        LocCounty = LocCountyBox,
        LocPostal = LocPostalBox
    };

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        if (connection?.Http == null)
        {
            ClearSeizureOptionUi(disconnected: true);
            ApplyBrandingToChrome(ReportBrandingFallback.ActiveTemplate, "regional_crime_lab (offline)");
            return;
        }

        SeizureOptionsHint.Visibility = Visibility.Collapsed;
        _ = LoadSeizureOptionsAsync(connection.Http);
        _ = LoadReportBrandingAsync(connection.Http);
    }

    async Task LoadReportBrandingAsync(MdtHttpClient http)
    {
        try
        {
            var tok = await http.GetDataJsonAsync("reportBranding?reportType=propertyEvidence").ConfigureAwait(false);
            if (tok is not JObject root)
            {
                await Dispatcher.InvokeAsync(() =>
                    ApplyBrandingToChrome(ReportBrandingFallback.ActiveTemplate, "regional_crime_lab (fallback)"));
                return;
            }

            var active = root["activeTemplate"] as JObject ?? ReportBrandingFallback.ActiveTemplate;
            var id = root["activeTemplateId"]?.ToString() ?? "regional_crime_lab";
            await Dispatcher.InvokeAsync(() => ApplyBrandingToChrome(active, id));
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
                ApplyBrandingToChrome(ReportBrandingFallback.ActiveTemplate, "regional_crime_lab (fallback)"));
        }
    }

    void ApplyBrandingToChrome(JObject? active, string templateIdHint)
    {
        active ??= ReportBrandingFallback.ActiveTemplate;
        BrandingLeftColumn.Text = (active["leftColumn"]?.ToString() ?? "").Replace("\n", Environment.NewLine);
        BrandingCenterTitle.Text = active["centerTitle"]?.ToString() ?? "";
        BrandingRightTitle.Text = (active["rightTitle"]?.ToString() ?? "").Replace("\n", Environment.NewLine);
        BrandingFooter.Text = active["footer"]?.ToString() ?? "";
        DocumentTitleBlock.Text = active["propertyEvidenceTitle"]?.ToString() ?? "Property & Evidence Receipt";
        BrandingTemplateHint.Text = $"Branding: {templateIdHint}";
    }

    async Task OpenRecentIdsAsync()
    {
        var http = _connection?.Http;
        if (http == null)
        {
            MessageBox.Show("Connect to MDT Pro first.", "Recent IDs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        JArray? arr;
        try
        {
            var tok = await http.GetDataJsonAsync("recentIds").ConfigureAwait(false);
            arr = tok as JArray;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load recent IDs.\n\n" + ex.Message, "Recent IDs", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var owner = Window.GetWindow(this);
        var dlg = new Window
        {
            Title = "Recent IDs",
            Width = 420,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Background = (System.Windows.Media.Brush?)owner?.TryFindResource("CadPanel") ?? System.Windows.Media.Brushes.White
        };
        var list = new ListBox { Margin = new Thickness(12) };
        var rows = new List<string>();
        if (arr != null)
        {
            foreach (var o in arr.OfType<JObject>())
            {
                var name = o["Name"]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                var typ = o["Type"]?.ToString();
                rows.Add(string.IsNullOrEmpty(typ) ? name : $"{name}  ({typ})");
            }
        }

        if (rows.Count == 0)
        {
            MessageBox.Show(owner, "No recent IDs. Collect an ID from a ped in game.", "Recent IDs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        list.ItemsSource = rows;
        var panel = new DockPanel();
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        var addBtn = new Button { Content = "ADD TO SUBJECTS", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(16, 6, 16, 6) };
        var closeBtn = new Button { Content = "CLOSE", Padding = new Thickness(16, 6, 16, 6) };
        btnRow.Children.Add(addBtn);
        btnRow.Children.Add(closeBtn);
        DockPanel.SetDock(btnRow, Dock.Bottom);
        panel.Children.Add(btnRow);
        panel.Children.Add(list);
        dlg.Content = panel;

        void InsertSelection()
        {
            if (list.SelectedItem is not string line) return;
            var name = line.Split("  (", 2)[0].Trim();
            if (string.IsNullOrEmpty(name)) return;
            var existing = ReportFormPaneFields.LinesToStringArray(SubjectPedNamesBox.Text);
            if (existing.OfType<JValue>().Any(v => string.Equals(v.ToString().Trim(), name, StringComparison.OrdinalIgnoreCase)))
                return;
            SubjectPedNamesBox.Text = string.IsNullOrWhiteSpace(SubjectPedNamesBox.Text)
                ? name
                : SubjectPedNamesBox.Text.TrimEnd() + Environment.NewLine + name;
        }

        addBtn.Click += (_, _) =>
        {
            InsertSelection();
            dlg.Close();
        };
        closeBtn.Click += (_, _) => dlg.Close();
        list.MouseDoubleClick += (_, _) =>
        {
            InsertSelection();
            dlg.Close();
        };

        dlg.ShowDialog();
    }

    void ExportPdf()
    {
        var scroll = DocumentBodyScroll;
        var prevClip = scroll.ClipToBounds;
        scroll.ClipToBounds = false;

        try
        {
            var root = DocumentPrintRoot;
            var paperWidth = double.IsNaN(root.MaxWidth) || root.MaxWidth <= 0 ? 920 : root.MaxWidth;
            root.Measure(new Size(paperWidth, double.PositiveInfinity));
            root.Arrange(new Rect(0, 0, root.DesiredSize.Width, Math.Max(root.DesiredSize.Height, 1)));
            root.UpdateLayout();

            var pd = new PrintDialog();
            if (pd.ShowDialog() != true)
                return;

            pd.PrintVisual(root, "MDT Property/Evidence " + (GeneralIdBox.Text.Trim().Length > 0 ? GeneralIdBox.Text.Trim() : "report"));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Print / PDF failed.\n\n" + ex.Message, "Export PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            scroll.ClipToBounds = prevClip;
            DocumentPrintRoot.InvalidateMeasure();
            DocumentPrintRoot.InvalidateArrange();
            scroll.InvalidateMeasure();
        }
    }

    static ContentControl? FindFormHost(DependencyObject? start)
    {
        for (var p = start; p != null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is ContentControl cc && cc.Name == "FormHost")
                return cc;
        }
        return null;
    }

    void ClosePopoutIfOpen()
    {
        if (_popoutWindow == null) return;
        try
        {
            _popoutWindow.Close();
        }
        catch
        {
            _popoutWindow = null;
        }
    }

    void TogglePopout()
    {
        if (_popoutWindow != null)
        {
            _popoutWindow.Close();
            return;
        }

        var host = FindFormHost(this);
        if (host == null)
        {
            MessageBox.Show("Could not find form host to pop out.", "Pop out", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _formHostWhilePopped = host;
        host.Content = null;
        var owner = Window.GetWindow(this);
        _popoutWindow = new Window
        {
            Title = "Property / evidence receipt — " + (GeneralIdBox.Text.Trim().Length > 0 ? GeneralIdBox.Text.Trim() : "draft"),
            Width = Math.Min(960, (owner?.ActualWidth ?? 800) + 40),
            Height = Math.Min(900, (owner?.ActualHeight ?? 640) + 40),
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = this,
            Background = System.Windows.Media.Brushes.White
        };
        _popoutWindow.Closed += PopoutWindowOnClosed;
        _popoutWindow.Show();
        ReportsView.RaiseSaveEnableStateMayHaveChanged();
    }

    void PopoutWindowOnClosed(object? sender, EventArgs e)
    {
        _popoutWindow = null;
        if (_formHostWhilePopped != null && _formHostWhilePopped.Content == null)
            _formHostWhilePopped.Content = this;
        _formHostWhilePopped = null;
        ReportsView.RaiseSaveEnableStateMayHaveChanged();
    }

    async Task LoadSeizureOptionsAsync(MdtHttpClient http)
    {
        try
        {
            var j = await http.GetSeizureOptionsJsonAsync().ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => ApplySeizureOptions(j));
        }
        catch
        {
            await Dispatcher.InvokeAsync(() => ClearSeizureOptionUi(disconnected: false));
        }
    }

    void ClearSeizureOptionUi(bool disconnected)
    {
        _allDrugTypes.Clear();
        DrugScheduleCombo.ItemsSource = null;
        DrugTypeCombo.ItemsSource = null;
        DrugQtyCombo.ItemsSource = null;
        FirearmTypeCombo.ItemsSource = null;
        AddDrugBtn.IsEnabled = false;
        AddFirearmBtn.IsEnabled = false;
        SeizureOptionsHint.Visibility = Visibility.Visible;
        SeizureOptionsHint.Text = disconnected
            ? "Connect to MDT Pro to load seizure dropdowns (same /seizureOptions as the browser)."
            : "Could not load /seizureOptions. Reconnect or ensure the in-game plugin is running.";
    }

    void ApplySeizureOptions(JObject? root)
    {
        _allDrugTypes.Clear();
        if (root == null)
        {
            ClearSeizureOptionUi(disconnected: false);
            return;
        }

        if (root["drugTypes"] is JArray dArr)
        {
            foreach (var t in dArr.OfType<JObject>())
            {
                var id = t["id"]?.ToString() ?? t["name"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(id)) continue;
                var name = t["name"]?.ToString() ?? id;
                var sched = t["schedule"]?.ToString();
                if (string.IsNullOrWhiteSpace(sched)) sched = "Other";
                _allDrugTypes.Add(new DrugTypeEntry { Id = id.Trim(), Name = name.Trim(), Schedule = sched.Trim() });
            }
        }

        var schedules = new List<SeizureListItem> { new() { Value = "", Label = "All schedules" } };
        foreach (var s in ScheduleOrder)
        {
            if (_allDrugTypes.Any(d => string.Equals(d.Schedule, s, StringComparison.Ordinal)))
                schedules.Add(new SeizureListItem
                {
                    Value = s,
                    Label = ScheduleLabels.TryGetValue(s, out var lab) ? lab : s
                });
        }

        _suppressScheduleChange = true;
        DrugScheduleCombo.ItemsSource = schedules;
        DrugScheduleCombo.SelectedIndex = 0;
        _suppressScheduleChange = false;
        RepopulateDrugTypeCombo();

        var qty = new List<SeizureListItem>();
        if (root["drugQuantities"] is JArray qArr)
        {
            foreach (var t in qArr.OfType<JObject>())
            {
                var id = t["id"]?.ToString() ?? "";
                var name = t["name"]?.ToString() ?? id;
                if (string.IsNullOrWhiteSpace(name)) continue;
                qty.Add(new SeizureListItem { Value = id, Label = name });
            }
        }
        DrugQtyCombo.ItemsSource = qty;
        if (qty.Count > 0)
            DrugQtyCombo.SelectedIndex = 0;

        var firearms = new List<SeizureListItem>();
        if (root["firearmTypes"] is JArray fArr)
        {
            foreach (var t in fArr.OfType<JObject>())
            {
                var id = t["id"]?.ToString() ?? t["name"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(id)) continue;
                var name = t["name"]?.ToString() ?? id;
                firearms.Add(new SeizureListItem { Value = id.Trim(), Label = name.Trim() });
            }
        }
        FirearmTypeCombo.ItemsSource = firearms;
        if (firearms.Count > 0)
            FirearmTypeCombo.SelectedIndex = 0;

        var ok = _allDrugTypes.Count > 0 && qty.Count > 0;
        AddDrugBtn.IsEnabled = ok;
        AddFirearmBtn.IsEnabled = firearms.Count > 0;
        SeizureOptionsHint.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        if (!ok)
            SeizureOptionsHint.Text = "Seizure options from MDT are empty. Check plugin defaults (seizureOptions.json).";

        if (_firearms.Count > 0)
            RefreshFirearmDisplayLabels();
    }

    void RefreshFirearmDisplayLabels()
    {
        var ids = _firearms.Select(f => f.Id).ToList();
        _firearms.Clear();
        foreach (var id in ids)
            _firearms.Add(new SeizedFirearmRow { Id = id, Label = ResolveFirearmLabel(id) });
    }

    void RepopulateDrugTypeCombo()
    {
        var sched = DrugScheduleCombo.SelectedValue as string ?? "";
        IEnumerable<DrugTypeEntry> filtered = string.IsNullOrEmpty(sched)
            ? _allDrugTypes
            : _allDrugTypes.Where(d => string.Equals(d.Schedule, sched, StringComparison.Ordinal));
        var list = filtered.ToList();
        if (list.Count == 0)
            list = _allDrugTypes.ToList();

        var items = list.Select(d => new SeizureListItem { Value = d.Id, Label = d.Name }).ToList();
        DrugTypeCombo.ItemsSource = items;
        DrugTypeCombo.SelectedIndex = items.Count > 0 ? 0 : -1;
    }

    void AddDrugFromSelection()
    {
        var typeId = DrugTypeCombo.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(typeId))
        {
            MessageBox.Show("Select a substance type.", "Property / evidence", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var qty = DrugQtyCombo.SelectedValue as string ?? "";
        _drugs.Add(new SeizedDrugRow { DrugType = typeId.Trim(), Quantity = qty.Trim() });
    }

    void AddFirearmFromSelection()
    {
        var id = FirearmTypeCombo.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(id))
        {
            MessageBox.Show("Select a firearm type.", "Property / evidence", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var label = FirearmTypeCombo.SelectedItem is SeizureListItem it ? it.Label : id.Trim();
        _firearms.Add(new SeizedFirearmRow { Id = id.Trim(), Label = label.Trim() });
    }

    void RemoveSeizedDrug_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SeizedDrugRow row })
            _drugs.Remove(row);
    }

    void RemoveFirearm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SeizedFirearmRow row })
            _firearms.Remove(row);
    }

    public void LoadFromReport(JObject report)
    {
        ReportFormPaneFields.LoadBase(report, BaseControls);
        SubjectPedNamesBox.Text = ReportFormPaneFields.StringArrayToLines(report["SubjectPedNames"]);
        if (string.IsNullOrWhiteSpace(SubjectPedNamesBox.Text) && report["SubjectPedName"] != null)
        {
            var legacy = report["SubjectPedName"]?.ToString();
            if (!string.IsNullOrWhiteSpace(legacy))
                SubjectPedNamesBox.Text = legacy;
        }

        LoadDrugsIntoCollection(report["SeizedDrugs"], _drugs);
        if (_drugs.Count == 0 && report["SeizedDrugTypes"] is JArray legacyTypes && legacyTypes.Count > 0)
        {
            foreach (var t in legacyTypes)
            {
                var s = t?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(s))
                    _drugs.Add(new SeizedDrugRow { DrugType = s, Quantity = "" });
            }
        }

        _firearms.Clear();
        if (report["SeizedFirearmTypes"] is JArray fa)
        {
            foreach (var t in fa)
            {
                var id = t?.ToString()?.Trim();
                if (string.IsNullOrEmpty(id)) continue;
                _firearms.Add(new SeizedFirearmRow { Id = id, Label = ResolveFirearmLabel(id) });
            }
        }
        OtherContrabandNotesBox.Text = report["OtherContrabandNotes"]?.ToString() ?? "";
    }

    string ResolveFirearmLabel(string id)
    {
        if (FirearmTypeCombo.ItemsSource is IEnumerable<SeizureListItem> items)
        {
            var hit = items.FirstOrDefault(x => string.Equals(x.Value, id, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit.Label;
        }
        return id;
    }

    static void LoadDrugsIntoCollection(JToken? tok, ObservableCollection<SeizedDrugRow> col)
    {
        col.Clear();
        if (tok is not JArray arr) return;
        foreach (var item in arr.OfType<JObject>())
        {
            var dt = item["DrugType"]?.ToString() ?? item["drugType"]?.ToString() ?? "";
            var q = item["Quantity"]?.ToString() ?? item["quantity"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(dt) && string.IsNullOrWhiteSpace(q)) continue;
            col.Add(new SeizedDrugRow { DrugType = dt, Quantity = q });
        }
    }

    public JObject BuildReport()
    {
        var root = new JObject();
        ReportFormPaneFields.WriteBase(root, BaseControls);

        var subjectNames = ReportFormPaneFields.LinesToStringArray(SubjectPedNamesBox.Text);
        root["SubjectPedNames"] = subjectNames;
        root["SubjectPedName"] = subjectNames.Count > 0 ? subjectNames[0] : JValue.CreateNull();

        var drugs = DrugsToJArray(_drugs);
        root["SeizedDrugs"] = drugs;
        var types = new JArray();
        foreach (var o in drugs.OfType<JObject>())
        {
            var dt = o["DrugType"]?.ToString();
            if (!string.IsNullOrWhiteSpace(dt)) types.Add(dt.Trim());
        }
        root["SeizedDrugTypes"] = types;

        var fa = new JArray();
        foreach (var f in _firearms)
        {
            if (!string.IsNullOrWhiteSpace(f.Id))
                fa.Add(f.Id.Trim());
        }
        root["SeizedFirearmTypes"] = fa;

        var notes = OtherContrabandNotesBox.Text.Trim();
        root["OtherContrabandNotes"] = string.IsNullOrEmpty(notes) ? JValue.CreateNull() : notes;

        return root;
    }

    static JArray DrugsToJArray(IEnumerable<SeizedDrugRow> rows)
    {
        var a = new JArray();
        foreach (var r in rows)
        {
            var dt = r.DrugType?.Trim() ?? "";
            if (string.IsNullOrEmpty(dt)) continue;
            a.Add(new JObject
            {
                ["DrugType"] = dt,
                ["Quantity"] = r.Quantity?.Trim() ?? ""
            });
        }
        return a;
    }

    public void Clear()
    {
        ReportFormPaneFields.ClearBase(BaseControls, 1);
        SubjectPedNamesBox.Text = "";
        _drugs.Clear();
        _firearms.Clear();
        OtherContrabandNotesBox.Text = "";
    }
}
