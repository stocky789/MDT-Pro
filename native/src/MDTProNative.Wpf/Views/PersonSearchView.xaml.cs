using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MDTProNative.Client;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class PersonSearchView : UserControl, IMdtBoundView
{
    /// <summary>Set by shell navigation (e.g. Court defendant link) before the view is created/bound.</summary>
    internal static string? PendingInitialPersonQuery;

    MdtConnectionManager? _connection;
    bool _layoutPersistWired;
    int _searchGen;
    /// <summary>Last loaded ped (full API object); form saves merge from this for arrays and unknown keys.</summary>
    JObject? _pedBaseForSave;
    string? _loadedPedName;
    bool _vehiclesTabLoaded;
    bool _firearmsTabLoaded;
    bool _reportsTabLoaded;

    public PersonSearchView()
    {
        InitializeComponent();
        Loaded += OnPersonSearchLoaded;
        PersonDetailTabs.SelectionChanged += PersonDetailTabs_OnSelectionChanged;
        RecentList.DisplayMemberPath = nameof(RecentRow.Display);
        HistoryList.DisplayMemberPath = nameof(HistoryRow.Display);
        SearchBtn.Click += async (_, _) => await SearchNamedAsync(QueryBox.Text);
        ContextPedBtn.Click += async (_, _) => await SearchNamedAsync("context");
        RefreshSidebarBtn.Click += async (_, _) => await LoadSidebarsAsync();
        QueryBox.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                await SearchNamedAsync(QueryBox.Text);
        };
        RecentList.MouseDoubleClick += async (_, _) =>
        {
            if (RecentList.SelectedItem is RecentRow r)
                await SearchNamedAsync(r.Name);
        };
        HistoryList.MouseDoubleClick += async (_, _) =>
        {
            if (HistoryList.SelectedItem is HistoryRow h)
                await SearchNamedAsync(h.ResultName);
        };
        SavePedFormBtn.Click += async (_, _) => await SavePedFromFormAsync();
        ClearPedHistoryBtn.Click += async (_, _) => await ClearPedHistoryAsync();
    }

    void OnPersonSearchLoaded(object sender, RoutedEventArgs e)
    {
        if (_layoutPersistWired) return;
        _layoutPersistWired = true;
        UiLayoutHooks.WirePersonSearch(this);
    }

    /// <summary>Fills CDF-aligned combo lists and selects <paramref name="stored"/> or appends a legacy value if needed.</summary>
    static void RepopulateStringCombo(ComboBox cb, IReadOnlyList<string> canonical, string? stored)
    {
        cb.Items.Clear();
        foreach (var s in canonical)
            cb.Items.Add(s);
        var t = stored?.Trim() ?? "";
        if (t.Length == 0)
        {
            cb.SelectedIndex = 0;
            return;
        }

        for (var i = 0; i < cb.Items.Count; i++)
        {
            if (string.Equals(cb.Items[i]?.ToString(), t, StringComparison.OrdinalIgnoreCase))
            {
                cb.SelectedIndex = i;
                return;
            }
        }

        cb.Items.Add(t);
        cb.SelectedIndex = cb.Items.Count - 1;
    }

    static void RepopulateWeaponPermitTypeCombo(ComboBox cb, string? stored)
    {
        cb.Items.Clear();
        foreach (var (label, value) in CdfPedFormOptions.WeaponPermitTypes)
            cb.Items.Add(new ComboBoxItem { Content = label, Tag = value });

        var normalized = CdfPedFormOptions.NormalizeWeaponPermitType(stored);
        foreach (var it in cb.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals((it.Tag as string) ?? "", normalized, StringComparison.Ordinal))
            {
                cb.SelectedItem = it;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(stored))
        {
            var raw = stored.Trim();
            var extra = new ComboBoxItem { Content = raw, Tag = raw };
            cb.Items.Add(extra);
            cb.SelectedItem = extra;
        }
        else
            cb.SelectedIndex = 0;
    }

    static string SelectedComboString(ComboBox cb) =>
        cb.SelectedItem == null ? "" : cb.SelectedItem.ToString() ?? "";

    static string WeaponPermitTypeFromCombo(ComboBox cb)
    {
        if (cb.SelectedValue != null)
            return cb.SelectedValue.ToString() ?? "";
        if (cb.SelectedItem is ComboBoxItem it)
            return it.Tag as string ?? "";
        return "";
    }

    Brush R(string key) => (Brush)FindResource(key);

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        DetailPanel.Children.Clear();
        DetailPanel.Children.Add(DetailPlaceholder);
        DetailPlaceholder.Visibility = Visibility.Visible;
        _pedBaseForSave = null;
        _loadedPedName = null;
        _vehiclesTabLoaded = _firearmsTabLoaded = _reportsTabLoaded = false;
        ClearPedForm();
        ClearAuxiliaryPersonTabs();
        PersonDetailTabs.SelectedIndex = 0;
        RecentList.ItemsSource = null;
        HistoryList.ItemsSource = null;
        if (connection?.Http == null) return;
        var pending = PendingInitialPersonQuery?.Trim();
        if (!string.IsNullOrEmpty(pending))
        {
            PendingInitialPersonQuery = null;
            QueryBox.Text = pending;
            _ = InitializePersonSearchFromShellAsync(pending);
        }
        else
            _ = LoadSidebarsAsync();
    }

    async Task InitializePersonSearchFromShellAsync(string pedName)
    {
        await LoadSidebarsAsync();
        await SearchNamedAsync(pedName);
    }

    async Task ClearPedHistoryAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        if (MessageBox.Show("Clear all saved person search history on the MDT server?", "Person search", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            var (code, text) = await http.PostActionAsync("clearSearchHistory", "ped").ConfigureAwait(false);
            if (code == HttpStatusCode.OK && string.Equals(text?.Trim(), "OK", StringComparison.Ordinal))
                await LoadSidebarsAsync();
            else
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Could not clear history ({(int)code}).\n\n{text}", "Person search", MessageBoxButton.OK, MessageBoxImage.Warning));
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show(ex.Message, "Person search", MessageBoxButton.OK, MessageBoxImage.Warning));
        }
    }

    async Task LoadSidebarsAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        await MdtBusyUi.RunAsync(ModuleBusy, "PERSON / ID MDC", "Loading recent IDs and search history…", async () =>
        {
        try
        {
            var recentTok = await http.GetDataJsonAsync("recentIds").ConfigureAwait(false);
            var recent = recentTok is JArray ra
                ? ra.OfType<JObject>().Select(o => new RecentRow(
                    o["Name"]?.ToString() ?? "",
                    o["Type"]?.ToString() ?? "",
                    NativeMdtFormat.IsoDate(o["Timestamp"]))).Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList()
                : new List<RecentRow>();

            var (_, histBody) = await http.PostAsync("data/searchHistory", "ped").ConfigureAwait(false);
            var histList = new List<HistoryRow>();
            if (!string.IsNullOrWhiteSpace(histBody) && histBody.TrimStart().StartsWith('['))
            {
                var ha = JArray.Parse(histBody);
                foreach (var o in ha.OfType<JObject>())
                {
                    var rn = o["ResultName"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(rn))
                        histList.Add(new HistoryRow(rn!, o["LastSearched"]?.ToString() ?? ""));
                }
            }

            await Dispatcher.InvokeAsync(() =>
            {
                RecentList.ItemsSource = recent;
                RecentEmptyHint.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                HistoryList.ItemsSource = histList;
            });
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                RecentList.ItemsSource = Array.Empty<RecentRow>();
                RecentEmptyHint.Visibility = Visibility.Visible;
                HistoryList.ItemsSource = Array.Empty<HistoryRow>();
            });
        }
        });
    }

    async Task SearchNamedAsync(string? rawQuery)
    {
        var http = _connection?.Http;
        if (http == null)
        {
            MessageBox.Show("Connect to MDT Pro first.", "Person search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var query = (rawQuery ?? "").Trim();
        if (string.IsNullOrEmpty(query))
        {
            MessageBox.Show("Enter a name to search.", "Person search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await MdtBusyUi.RunAsync(ModuleBusy, "CJIS / ID QUERY", "Retrieving subject record…", async () =>
        {
            var gen = ++_searchGen;
            JObject? ped = null;
            try
            {
                // Match browser MDT: pedSearch.js posts the raw name string (not JSON.stringify), so the plugin receives plain text.
                var (status, text) = await http.PostAsync("data/specificPed", query).ConfigureAwait(false);
                if (gen != _searchGen) return;
                if (status != HttpStatusCode.OK)
                {
                    await Dispatcher.InvokeAsync(() =>
                        MessageBox.Show(
                            $"Person search request failed ({(int)status}).\n\n{text}",
                            "Person search",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning));
                    return;
                }

                if (string.IsNullOrWhiteSpace(text) || text.Trim() == "null")
                    ped = null;
                else
                    ped = JToken.Parse(text) as JObject;
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        "Could not read person search response.\n\n" + ex.Message,
                        "Person search",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
                return;
            }

            if (gen != _searchGen) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (ped == null)
                {
                    _pedBaseForSave = null;
                    _loadedPedName = null;
                    _vehiclesTabLoaded = _firearmsTabLoaded = _reportsTabLoaded = false;
                    ClearPedForm();
                    ClearAuxiliaryPersonTabs();
                    MessageBox.Show("Person not found.", "Person search", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                RenderPed(ped);
            });
        });
    }

    FrameworkElement BuildPersonSearchReportActions(string pedName)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(new TextBlock
        {
            Text = "CREATE REPORT FROM SUBJECT",
            Style = (Style)FindResource("CadSectionTitle"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        sp.Children.Add(new TextBlock
        {
            Text = "Opens Reports with a new draft and this person prefilled (same workflow as the browser MDT person search).",
            Foreground = R("CadMuted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var row = new WrapPanel();
        void AddBtn(string label, string reportTypeKey)
        {
            var b = new Button
            {
                Content = label,
                Style = (Style)FindResource("CadRailOutlineButton"),
                Margin = new Thickness(0, 0, 8, 6),
                Padding = new Thickness(12, 6, 12, 6)
            };
            var name = pedName;
            var rk = reportTypeKey;
            b.Click += (_, _) =>
            {
                if (_connection?.Http == null)
                {
                    MessageBox.Show("Connect to MDT Pro first.", "Person search", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                MdtShellEvents.RequestNavigateToNewReportFromPersonSearch(rk, name, null);
            };
            row.Children.Add(b);
        }

        AddBtn("NEW CITATION", "citation");
        AddBtn("NEW ARREST", "arrest");
        AddBtn("NEW INJURY", "injury");
        sp.Children.Add(row);
        return sp;
    }

    void RenderPed(JObject p)
    {
        DetailPanel.Children.Clear();
        DetailPlaceholder.Visibility = Visibility.Collapsed;

        var displayName = NativeMdtFormat.Text(p["Name"]);
        if (displayName != "—")
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = displayName.ToUpperInvariant(),
                Style = (Style)FindResource("CadEventBandTitle")
            });
        }

        var pedPhotoGen = _searchGen;
        DetailPanel.Children.Add(CreatePedCataloguePhotoChrome(p["ModelName"]?.ToString(), pedPhotoGen));

        var pedForNewReports = p["Name"]?.ToString()?.Trim() ?? "";
        if (pedForNewReports.Length > 0)
            DetailPanel.Children.Add(BuildPersonSearchReportActions(pedForNewReports));

        DetailPanel.Children.Add(SectionTitle("Subject"));
        DetailPanel.Children.Add(FieldGrid(new (string, string)[]
        {
            ("Name", NativeMdtFormat.Text(p["Name"])),
            ("First", NativeMdtFormat.Text(p["FirstName"])),
            ("Last", NativeMdtFormat.Text(p["LastName"])),
            ("Model", NativeMdtFormat.Text(p["ModelName"])),
            ("DOB", NativeMdtFormat.IsoDate(p["Birthday"])),
            ("Gender", NativeMdtFormat.Text(p["Gender"])),
            ("Address", NativeMdtFormat.Text(p["Address"])),
        }));

        var text = R("CadText");
        var ok = R("CadSemanticSuccess");
        var warn = R("CadSemanticWarning");
        var bad = R("CadSemanticDanger");

        DetailPanel.Children.Add(SectionTitle("Status"));
        DetailPanel.Children.Add(FieldGrid(new (string label, string value, Brush valueBrush)[]
        {
            ("Wanted", NativeMdtFormat.YesNo(p["IsWanted"]), NativePedSearchBrushes.ForCdfValue(p["IsWanted"], text, ok, warn, bad)),
            ("Warrant", NativeMdtFormat.Text(p["WarrantText"]), NativePedSearchBrushes.ForWarrantDisplay(NativeMdtFormat.Text(p["WarrantText"]), ok, bad)),
            ("Probation", NativeMdtFormat.YesNo(p["IsOnProbation"]), NativePedSearchBrushes.ForCdfValue(p["IsOnProbation"], text, ok, warn, bad)),
            ("Parole", NativeMdtFormat.YesNo(p["IsOnParole"]), NativePedSearchBrushes.ForCdfValue(p["IsOnParole"], text, ok, warn, bad)),
            ("Gang affiliation", NativeMdtFormat.YesNo(p["IsInGang"]), NativePedSearchBrushes.ForCdfValue(p["IsInGang"], text, ok, warn, bad)),
            ("Times stopped", NativeMdtFormat.Text(p["TimesStopped"]), NativePedSearchBrushes.ForCdfValue(p["TimesStopped"], text, ok, warn, bad)),
            ("Advisory", NativeMdtFormat.Text(p["AdvisoryText"]), NativePedSearchBrushes.ForAdvisoryDisplay(NativeMdtFormat.Text(p["AdvisoryText"]), text, bad)),
            ("Deceased", NativeMdtFormat.YesNo(p["IsDeceased"]), NativePedSearchBrushes.ForCdfValue(p["IsDeceased"], text, ok, warn, bad)),
        }));

        DetailPanel.Children.Add(SectionTitle("Licenses & permits"));
        DetailPanel.Children.Add(FieldGrid(new (string label, string value, Brush valueBrush)[]
        {
            ("Driver license", NativeMdtFormat.Text(p["LicenseStatus"]), NativePedSearchBrushes.ForCdfValue(p["LicenseStatus"], text, ok, warn, bad)),
            ("License expires", NativeMdtFormat.IsoDate(p["LicenseExpiration"]), NativePedSearchBrushes.ForExpirationToken(p["LicenseExpiration"], text, warn)),
            ("Weapon permit", NativeMdtFormat.Text(p["WeaponPermitStatus"]), NativePedSearchBrushes.ForCdfValue(p["WeaponPermitStatus"], text, ok, warn, bad)),
            ("Weapon type", NativeMdtFormat.Text(p["WeaponPermitType"]), text),
            ("Weapon expires", NativeMdtFormat.IsoDate(p["WeaponPermitExpiration"]), NativePedSearchBrushes.ForExpirationToken(p["WeaponPermitExpiration"], text, warn)),
            ("Hunting", NativeMdtFormat.Text(p["HuntingPermitStatus"]), NativePedSearchBrushes.ForCdfValue(p["HuntingPermitStatus"], text, ok, warn, bad)),
            ("Hunting expires", NativeMdtFormat.IsoDate(p["HuntingPermitExpiration"]), NativePedSearchBrushes.ForExpirationToken(p["HuntingPermitExpiration"], text, warn)),
            ("Fishing", NativeMdtFormat.Text(p["FishingPermitStatus"]), NativePedSearchBrushes.ForCdfValue(p["FishingPermitStatus"], text, ok, warn, bad)),
            ("Fishing expires", NativeMdtFormat.IsoDate(p["FishingPermitExpiration"]), NativePedSearchBrushes.ForExpirationToken(p["FishingPermitExpiration"], text, warn)),
        }));

        DetailPanel.Children.Add(SectionTitle("Citations"));
        DetailPanel.Children.Add(BulletBlock(NativeMdtFormat.StringList(p["Citations"]), (Brush)FindResource("CadAmberGold")));

        var arrestBrush = (Brush)FindResource("CadUrgent");
        DetailPanel.Children.Add(SectionTitle("Arrest history"));
        DetailPanel.Children.Add(BulletBlock(NativeMdtFormat.StringList(p["Arrests"]), arrestBrush));

        if (p["IdentificationHistory"] is JArray idh && idh.Count > 0)
        {
            DetailPanel.Children.Add(SectionTitle("ID history"));
            foreach (var entry in idh.OfType<JObject>())
            {
                var line = $"{NativeMdtFormat.IsoDate(entry["Timestamp"])} — {entry["Type"]}";
                DetailPanel.Children.Add(NoteLine(line));
            }
        }

        _loadedPedName = p["Name"]?.ToString()?.Trim();
        if (string.IsNullOrEmpty(_loadedPedName))
            _loadedPedName = null;
        _vehiclesTabLoaded = _firearmsTabLoaded = _reportsTabLoaded = false;
        PrimeAuxiliaryTabPlaceholders();
        FillCourtTabFromPed(p);

        _pedBaseForSave = (JObject)p.DeepClone();
        LoadPedFormFromBase();
        PersonDetailTabs.SelectedIndex = 0;

        DetailScroller.ScrollToVerticalOffset(0);
    }

    Border CreatePedCataloguePhotoChrome(string? modelName, int gen)
    {
        var outer = new Border
        {
            Width = 118,
            Height = 148,
            Margin = new Thickness(0, 0, 0, 10),
            Background = R("CadElevated"),
            BorderBrush = R("CadBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            ClipToBounds = true
        };
        var img = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        var ph = new TextBlock
        {
            Text = "Loading catalogue still…",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = R("CadMuted"),
            FontSize = 10,
            Margin = new Thickness(6),
            Visibility = Visibility.Visible
        };
        if (!NativeCatalogueImageLoader.IsPedPortraitModelSuitable(modelName))
            ph.Text = "No ID catalogue photo\n(model not in portrait set)";

        var grid = new Grid();
        grid.Children.Add(img);
        grid.Children.Add(ph);
        outer.Child = grid;

        var http = _connection?.Http;
        _ = ApplyPedCataloguePhotoAsync(img, ph, http, modelName, gen);
        return outer;
    }

    async Task ApplyPedCataloguePhotoAsync(Image img, TextBlock ph, MdtHttpClient? http, string? modelName, int gen)
    {
        if (!NativeCatalogueImageLoader.IsPedPortraitModelSuitable(modelName))
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (gen != _searchGen) return;
                ph.Visibility = Visibility.Visible;
                ph.Text = "No ID catalogue photo\n(animal / prop model or unknown)";
                img.Visibility = Visibility.Collapsed;
            });
            return;
        }

        System.Windows.Media.Imaging.BitmapImage? bmp = null;
        try
        {
            bmp = await NativeCatalogueImageLoader.LoadPedIdPhotoAsync(http, modelName).ConfigureAwait(false);
        }
        catch
        {
            /* same as browser: hide failures */
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (gen != _searchGen) return;
            if (bmp != null)
            {
                img.Source = bmp;
                img.Visibility = Visibility.Visible;
                ph.Visibility = Visibility.Collapsed;
            }
            else
            {
                ph.Visibility = Visibility.Visible;
                ph.Text = "No ID catalogue photo\n(not in MDT bundle or FiveM docs)";
                img.Visibility = Visibility.Collapsed;
            }
        });
    }

    void ClearAuxiliaryPersonTabs()
    {
        PedVehiclesPanel.Children.Clear();
        PedFirearmsPanel.Children.Clear();
        PedReportsPanel.Children.Clear();
        PedCourtPanel.Children.Clear();
        PedVehiclesPanel.Children.Add(AuxTabHint("Search for a person to see vehicles, firearms, reports, and court cases."));
    }

    void PrimeAuxiliaryTabPlaceholders()
    {
        PedVehiclesPanel.Children.Clear();
        PedFirearmsPanel.Children.Clear();
        PedReportsPanel.Children.Clear();
        PedVehiclesPanel.Children.Add(AuxTabHint("Open this tab to load vehicles registered to this subject (same owner name in the vehicle database)."));
        PedFirearmsPanel.Children.Add(AuxTabHint("Open this tab to load firearms registered to this owner (same as Firearms module: serial first, else owner list)."));
        PedReportsPanel.Children.Add(AuxTabHint("Open this tab to list citations, arrests, incidents, and other reports linked to this name."));
    }

    TextBlock AuxTabHint(string msg) => new()
    {
        Text = msg,
        TextWrapping = TextWrapping.Wrap,
        Foreground = R("CadMuted"),
        FontSize = 12,
        Margin = new Thickness(0, 4, 0, 0)
    };

    /// <summary>Subject-centric chrome so auxiliary tabs read as one dossier, not a pasted vehicle/firearms screen.</summary>
    void AddSubjectDossierHeader(Panel panel, string sectionLabel, string scopeNote)
    {
        var name = string.IsNullOrEmpty(_loadedPedName) ? "—" : _loadedPedName.ToUpperInvariant();
        panel.Children.Add(new TextBlock
        {
            Text = $"{sectionLabel}  ·  {name}",
            FontFamily = (FontFamily)FindResource("CadFontMono"),
            Foreground = R("CadAccent"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        if (!string.IsNullOrWhiteSpace(scopeNote))
        {
            panel.Children.Add(new TextBlock
            {
                Text = scopeNote,
                Foreground = R("CadMuted"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
        }
        panel.Children.Add(new Border { Height = 1, Background = R("CadBorder"), Margin = new Thickness(0, 0, 0, 12) });
    }

    void FillCourtTabFromPed(JObject p)
    {
        PedCourtPanel.Children.Clear();
        AddSubjectDossierHeader(PedCourtPanel, "COURT & SUPERVISION", "Docket lines keyed to this master name. Not a full case management system — read-only index from the live plugin court database.");
        if (p["CourtCases"] is not JArray cc || cc.Count == 0)
        {
            PedCourtPanel.Children.Add(new TextBlock
            {
                Text = "No court matters on file for this subject index.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = R("CadMuted"),
                FontSize = 12
            });
            return;
        }

        foreach (var c in cc.OfType<JObject>())
        {
            var st = NativeMdtFormat.CourtStatus(c.Value<int?>("Status") ?? 0);
            var num = c["Number"]?.ToString() ?? "—";
            var head = $"{num} · {st}";
            PedCourtPanel.Children.Add(CourtCaseMini(head, c["ReportId"]?.ToString(), c["SupervisionRecordHint"]?.ToString()));
        }
    }

    void PersonDetailTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, PersonDetailTabs)) return;
        if (string.IsNullOrEmpty(_loadedPedName)) return;
        var http = _connection?.Http;
        if (http == null) return;
        var gen = _searchGen;
        switch (PersonDetailTabs.SelectedIndex)
        {
            case 2 when !_vehiclesTabLoaded:
                _vehiclesTabLoaded = true;
                _ = LoadPedVehiclesTabAsync(http, _loadedPedName!, gen);
                break;
            case 3 when !_firearmsTabLoaded:
                _firearmsTabLoaded = true;
                _ = LoadPedFirearmsTabAsync(http, _loadedPedName!, gen);
                break;
            case 4 when !_reportsTabLoaded:
                _reportsTabLoaded = true;
                _ = LoadPedReportsTabAsync(http, _loadedPedName!, gen);
                break;
        }
    }

    async Task LoadPedVehiclesTabAsync(MdtHttpClient http, string pedName, int gen)
    {
        await MdtBusyUi.RunAsync(ModuleBusy, "SUBJECT VEHICLES", "Loading vehicles by owner…", async () =>
        {
            JArray? stubs = null;
            try
            {
                var (_, text) = await http.PostAsync("data/pedVehicles", JsonConvert.SerializeObject(pedName)).ConfigureAwait(false);
                if (gen != _searchGen) return;
                if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('['))
                    stubs = JArray.Parse(text);
            }
            catch { /* ignore */ }

            if (gen != _searchGen) return;
            if (stubs == null || stubs.Count == 0)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (gen != _searchGen) return;
                    PedVehiclesPanel.Children.Clear();
                    AddSubjectDossierHeader(PedVehiclesPanel, "REGISTERED VEHICLE INDEX", "Only vehicles whose registered owner string matches this subject name in the MVD database.");
                    PedVehiclesPanel.Children.Add(new TextBlock
                    {
                        Text = "No owner matches — nothing in the vehicle file is indexed to this exact name.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = R("CadMuted"),
                        FontSize = 12
                    });
                });
                return;
            }

            var plates = stubs.OfType<JObject>()
                .Select(o => o["LicensePlate"]?.ToString()?.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var cards = new List<(JObject veh, JArray? search, JArray? impound)>();
            foreach (var plate in plates)
            {
                if (gen != _searchGen) return;
                JObject? veh = null;
                try
                {
                    var (_, vText) = await http.PostAsync("data/specificVehicle", JsonConvert.SerializeObject(plate)).ConfigureAwait(false);
                    if (gen != _searchGen) return;
                    if (!string.IsNullOrWhiteSpace(vText) && vText.TrimStart().StartsWith('{'))
                        veh = JObject.Parse(vText);
                }
                catch { /* skip */ }

                if (veh == null) continue;

                JArray? searchRec = null;
                JArray? impound = null;
                try
                {
                    var (_, sText) = await http.PostAsync("data/vehicleSearchByPlate", JsonConvert.SerializeObject(plate)).ConfigureAwait(false);
                    if (gen != _searchGen) return;
                    if (!string.IsNullOrWhiteSpace(sText) && sText.TrimStart().StartsWith('['))
                        searchRec = JArray.Parse(sText);
                }
                catch { /* optional */ }

                try
                {
                    var (_, iText) = await http.PostAsync("data/impoundReportsByPlate", JsonConvert.SerializeObject(plate)).ConfigureAwait(false);
                    if (gen != _searchGen) return;
                    if (!string.IsNullOrWhiteSpace(iText) && iText.TrimStart().StartsWith('['))
                        impound = JArray.Parse(iText);
                }
                catch { /* optional */ }

                cards.Add((veh, searchRec, impound));
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (gen != _searchGen) return;
                PedVehiclesPanel.Children.Clear();
                AddSubjectDossierHeader(PedVehiclesPanel, "REGISTERED VEHICLE INDEX", "Only vehicles whose registered owner string matches this subject name in the MVD database. Summary is dossier-style; expand for full technical file (VIN, docs, BOLO narrative, impound, frisk hits). Use Vehicle module to run plates or modify BOLOs when the unit is in range.");
                if (cards.Count == 0)
                {
                    PedVehiclesPanel.Children.Add(new TextBlock
                    {
                        Text = "No linked units resolved (plates from owner index did not return vehicle records — try Vehicle search).",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = R("CadMuted"),
                        FontSize = 12
                    });
                    return;
                }

                for (var i = 0; i < cards.Count; i++)
                    AppendSubjectVehicleDossierCard(PedVehiclesPanel, i + 1, cards[i].veh, cards[i].search, cards[i].impound);
            });
        }, minimumVisibleMs: 480);
    }

    void AppendSubjectVehicleDossierCard(Panel target, int unitIndex, JObject v, JArray? searchRecords, JArray? impoundRows)
    {
        var plate = NativeMdtFormat.Text(v["LicensePlate"]);
        var owner = NativeMdtFormat.Text(v["Owner"]);
        var ownerMatches = !string.IsNullOrEmpty(_loadedPedName) && owner != "—"
            && string.Equals(owner.Trim(), _loadedPedName.Trim(), StringComparison.OrdinalIgnoreCase);

        var outer = new Border
        {
            BorderBrush = R("CadBorder"),
            BorderThickness = new Thickness(1),
            Background = R("CadElevated"),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(2)
        };

        var root = new StackPanel();

        root.Children.Add(new TextBlock
        {
            Text = $"LINKED UNIT {unitIndex:D2}  ·  OWNER INDEX",
            Foreground = R("CadMuted"),
            FontSize = 9,
            FontFamily = (FontFamily)FindResource("CadFontCondensed"),
            Margin = new Thickness(0, 0, 0, 6)
        });

        if (plate != "—")
        {
            var band = new TextBlock
            {
                Text = plate.ToUpperInvariant(),
                Style = (Style)FindResource("CadEventBandTitle"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            if (v["IsStolen"]?.Value<bool>() == true)
                band.Foreground = (Brush)FindResource("CadUrgent");
            root.Children.Add(band);
        }

        var stolen = v["IsStolen"]?.Value<bool>() == true;
        var sum1 = string.Join("  ·  ", new[]
        {
            NativeMdtFormat.Text(v["ModelDisplayName"]),
            NativeMdtFormat.Text(v["Color"]),
            stolen ? "STOLEN — VERIFY HIT" : "Not flagged stolen"
        }.Where(s => !string.IsNullOrWhiteSpace(s) && s != "—"));
        root.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(sum1) ? "—" : sum1,
            TextWrapping = TextWrapping.Wrap,
            Foreground = stolen ? (Brush)FindResource("CadUrgent") : R("CadText"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var regIns = string.Join("  ·  ", new[]
        {
            "Reg: " + NativeMdtFormat.Text(v["RegistrationStatus"]),
            "Ins: " + NativeMdtFormat.Text(v["InsuranceStatus"])
        });
        root.Children.Add(new TextBlock
        {
            Text = regIns,
            Foreground = R("CadMuted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        root.Children.Add(new TextBlock
        {
            Text = owner == "—"
                ? "Registered owner: —"
                : $"Registered owner: {owner}" + (ownerMatches ? "  (matches this subject index)" : "  (name differs from subject index — verify linkage)"),
            Foreground = ownerMatches ? R("CadSemanticSuccess") : R("CadAmberGold"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var detail = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        detail.Children.Add(SectionTitle("Technical / MVR file"));
        detail.Children.Add(FieldGrid(new (string, string)[]
        {
            ("Plate", NativeMdtFormat.Text(v["LicensePlate"])),
            ("VIN", NativeMdtFormat.Text(v["VehicleIdentificationNumber"])),
            ("VIN status", NativeMdtFormat.Text(v["VinStatus"])),
            ("Make / model", JoinMakeModel(v)),
            ("Primary", NativeMdtFormat.Text(v["PrimaryColor"])),
            ("Secondary", NativeMdtFormat.Text(v["SecondaryColor"])),
            ("Reg. expires", NativeMdtFormat.IsoDate(v["RegistrationExpiration"])),
            ("Ins. expires", NativeMdtFormat.IsoDate(v["InsuranceExpiration"])),
        }));

        if (v["BOLOs"] is JArray bolos && bolos.Count > 0)
        {
            detail.Children.Add(SectionTitle("Active BOLO narrative"));
            foreach (var b in bolos)
            {
                if (b is JObject bo)
                    detail.Children.Add(PersonVehicleBoloRow(bo));
            }
        }

        if (searchRecords != null && searchRecords.Count > 0)
        {
            detail.Children.Add(SectionTitle("Frisk / search hits"));
            foreach (var r in searchRecords.OfType<JObject>())
            {
                var line = string.Join(" · ", new[]
                {
                    NativeMdtFormat.Text(r["ItemType"]),
                    NativeMdtFormat.Text(r["Description"]),
                    NativeMdtFormat.Text(r["DrugType"]),
                    NativeMdtFormat.Text(r["ItemLocation"]),
                    NativeMdtFormat.IsoDate(r["CapturedAt"])
                }.Where(s => s != "—"));
                if (string.IsNullOrWhiteSpace(line)) line = JTokenDisplay.ForDataCell(r);
                detail.Children.Add(NoteLine(line));
            }
        }

        if (impoundRows != null && impoundRows.Count > 0)
        {
            detail.Children.Add(SectionTitle("Impound history"));
            foreach (var r in impoundRows.OfType<JObject>())
            {
                var head = $"{NativeMdtFormat.Text(r["Id"])} · {NativeMdtFormat.IsoDate(r["TimeStamp"])} · {NativeMdtFormat.Text(r["Status"])}";
                detail.Children.Add(NoteLine(head));
                var sub = string.Join(" · ", new[]
                {
                    NativeMdtFormat.Text(r["ImpoundReason"]),
                    NativeMdtFormat.Text(r["TowCompany"]),
                    NativeMdtFormat.Text(r["ImpoundLot"])
                }.Where(s => s != "—"));
                if (!string.IsNullOrWhiteSpace(sub))
                    detail.Children.Add(NoteLine(sub));
            }
        }

        var exp = new Expander
        {
            Header = "Expand full vehicle file (VIN, documents, BOLO, impound, frisk)",
            IsExpanded = false,
            Foreground = R("CadText"),
            FontSize = 11,
            Margin = new Thickness(-4, 0, 0, 0)
        };
        exp.Content = detail;
        root.Children.Add(exp);

        outer.Child = root;
        target.Children.Add(outer);
    }

    static string JoinMakeModel(JObject v)
    {
        var mk = v["Make"]?.ToString()?.Trim();
        var md = v["Model"]?.ToString()?.Trim();
        if (string.IsNullOrEmpty(mk) && string.IsNullOrEmpty(md)) return "—";
        if (string.IsNullOrEmpty(mk)) return md ?? "—";
        if (string.IsNullOrEmpty(md)) return mk ?? "—";
        return $"{mk} {md}";
    }

    Border PersonVehicleBoloRow(JObject b)
    {
        var reason = NativeMdtFormat.Text(b["Reason"]?.Type == JTokenType.Null ? b["reason"] : b["Reason"]);
        if (reason == "—") reason = JTokenDisplay.ForDataCell(b);
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

    async Task LoadPedFirearmsTabAsync(MdtHttpClient http, string pedName, int gen)
    {
        await MdtBusyUi.RunAsync(ModuleBusy, "SUBJECT FIREARMS", "Querying registry by owner…", async () =>
        {
            JArray? arr = null;
            try
            {
                var (_, text) = await http.PostAsync("data/firearmsForPed", JsonConvert.SerializeObject(pedName)).ConfigureAwait(false);
                if (gen != _searchGen) return;
                if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('['))
                    arr = JArray.Parse(text);
            }
            catch { /* ignore */ }

            await Dispatcher.InvokeAsync(() =>
            {
                if (gen != _searchGen) return;
                PedFirearmsPanel.Children.Clear();
                AddSubjectDossierHeader(PedFirearmsPanel, "WEAPONS ATTRIBUTION FILE", "One line per registered or attributed firearm for this subject name (NCIC-style roster, not the full interactive firearms workstation).");
                if (arr == null || arr.Count == 0)
                {
                    PedFirearmsPanel.Children.Add(new TextBlock
                    {
                        Text = "No weapons on file for this name in the registry extract.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = R("CadMuted"),
                        FontSize = 12
                    });
                    return;
                }

                PedFirearmsPanel.Children.Add(new TextBlock
                {
                    Text = $"{arr.Count} line(s) · serials reflect database state (scrubbed if reported).",
                    Foreground = R("CadSubTabActiveText"),
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 0, 10)
                });
                AppendPedFirearmRoster(PedFirearmsPanel, arr.OfType<JObject>().ToList());
            });
        }, minimumVisibleMs: 480);
    }

    void AppendPedFirearmRoster(Panel target, IReadOnlyList<JObject> items)
    {
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        for (var c = 0; c < 4; c++)
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void H(int col, string t)
        {
            var tb = new TextBlock
            {
                Text = t,
                Foreground = R("CadMuted"),
                FontSize = 9,
                FontFamily = (FontFamily)FindResource("CadFontCondensed"),
                Margin = new Thickness(0, 0, 10, 4)
            };
            Grid.SetColumn(tb, col);
            headerGrid.Children.Add(tb);
        }
        H(0, "WEAPON");
        H(1, "SERIAL");
        H(2, "FLAGS");
        H(3, "ACTIVITY");
        target.Children.Add(headerGrid);

        foreach (var f in items)
        {
            var w = NativeMdtFormat.Text(f["WeaponDisplayName"]);
            if (w == "—") w = NativeMdtFormat.Text(f["Description"]);
            if (w == "—") w = "—";
            var serial = f["IsSerialScratched"]?.Value<bool>() == true
                ? "SCRATCHED"
                : NativeMdtFormat.Text(f["SerialNumber"]);
            var flags = new List<string>();
            if (f["IsStolen"]?.Value<bool>() == true) flags.Add("STOLEN");
            if (f["IsSerialScratched"]?.Value<bool>() == true) flags.Add("SERIAL VOID");
            var flagStr = flags.Count == 0 ? "—" : string.Join(" · ", flags);
            var act = "Last " + NativeMdtFormat.IsoDate(f["LastSeenAt"]);
            if (act == "Last —") act = "Seen " + NativeMdtFormat.IsoDate(f["FirstSeenAt"]);

            var row = new Border
            {
                BorderBrush = R("CadBorder"),
                BorderThickness = new Thickness(1),
                Background = R("CadPanel"),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(2)
            };
            var g = new Grid();
            for (var c = 0; c < 4; c++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            void Cell(int col, string text, bool mono = false, Brush? fg = null)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = fg ?? R("CadText"),
                    FontSize = 11,
                    FontFamily = mono ? new FontFamily("Consolas") : (FontFamily)FindResource("CadFontMono"),
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(tb, col);
                g.Children.Add(tb);
            }

            Cell(0, w);
            Cell(1, serial, mono: true);
            Cell(2, flagStr, fg: flags.Contains("STOLEN") ? (Brush)FindResource("CadUrgent") : R("CadMuted"));
            Cell(3, act);
            row.Child = g;
            target.Children.Add(row);

            var src = NativeMdtFormat.Text(f["Source"]);
            var notes = NativeMdtFormat.Text(f["Description"]);
            if (src != "—" || notes != "—")
            {
                target.Children.Add(new TextBlock
                {
                    Text = string.Join(" · ", new[] { src != "—" ? "Src: " + src : "", notes != "—" ? notes : "" }.Where(s => s.Length > 0)),
                    Foreground = R("CadMuted"),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 0, 0, 10)
                });
            }
        }
    }

    static bool TryParseReportRowTime(JToken? t, out DateTime dt)
    {
        dt = default;
        if (t == null || t.Type == JTokenType.Null) return false;
        try
        {
            if (t.Type == JTokenType.Date)
            {
                dt = t.Value<DateTime>();
                return true;
            }
            var s = t.ToString();
            if (!string.IsNullOrWhiteSpace(s) && NativeMdtFormat.TryParseMdtDateTime(s, out dt))
                return true;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                return true;
        }
        catch
        {
            // ignore
        }
        return false;
    }

    static string? PedBucketToNativeReportType(string pedKey) => pedKey switch
    {
        "citations" => "citation",
        "arrests" => "arrest",
        "incidents" => "incident",
        "propertyEvidence" => "propertyEvidence",
        "injuries" => "injury",
        "impounds" => "impound",
        _ => null
    };

    static string? ResolveNativeReportTypeKey(JObject? sum, string pedBucketKey)
    {
        var api = sum?["type"]?.ToString()?.Trim();
        if (!string.IsNullOrEmpty(api) && NativeReportDraftFactory.DataPathFor(api) != null)
            return api;
        var fromBucket = PedBucketToNativeReportType(pedBucketKey);
        return !string.IsNullOrEmpty(fromBucket) && NativeReportDraftFactory.DataPathFor(fromBucket) != null
            ? fromBucket
            : null;
    }

    static JObject? FindReportObjectById(JToken? tok, string id)
    {
        if (tok is not JArray arr || string.IsNullOrWhiteSpace(id)) return null;
        foreach (var t in arr)
        {
            if (t is not JObject o) continue;
            var rid = o["Id"]?.ToString()?.Trim();
            if (string.Equals(rid, id, StringComparison.OrdinalIgnoreCase))
                return o;
        }
        return null;
    }

    static string HumanizeReportFieldKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        var chars = new List<char>(key.Length + 4);
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(key[i - 1]))
                chars.Add(' ');
            chars.Add(c);
        }
        return new string(chars.ToArray()).ToUpperInvariant();
    }

    static int ReportDetailPropertyOrder(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "ID" => 0,
            "TIMESTAMP" => 1,
            "STATUS" => 2,
            "SHORTYEAR" => 3,
            "NOTES" => 4,
            "OFFENDERPEDNAME" => 5,
            "OFFENDERVEHICLELICENSEPLATE" => 6,
            "COURTCASENUMBER" => 7,
            "LOCATION" => 8,
            "OFFICERINFORMATION" => 9,
            "CHARGES" => 10,
            "USEOFFORCE" => 11,
            _ => 50
        };
    }

    void AppendJObjectLabeledReportDetail(Panel target, JObject report)
    {
        var first = true;
        foreach (var p in report.Properties()
                     .OrderBy(x => ReportDetailPropertyOrder(x.Name))
                     .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (p.Value.Type == JTokenType.Null) continue;

            if (p.Name.Equals("UseOfForce", StringComparison.OrdinalIgnoreCase) && p.Value is JObject uofObj)
            {
                AppendUseOfForceReadOnlyBlock(target, uofObj, first);
                first = false;
                continue;
            }

            var val = JTokenDisplay.FormatPropertyValue(p.Value);
            if (string.IsNullOrWhiteSpace(val)) continue;

            var label = new TextBlock
            {
                Text = HumanizeReportFieldKey(p.Name),
                Foreground = R("CadMuted"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, first ? 0 : 10, 0, 2)
            };
            first = false;
            target.Children.Add(label);
            target.Children.Add(new TextBlock
            {
                Text = val,
                Foreground = R("CadText"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    void AppendUseOfForceReadOnlyBlock(Panel target, JObject uof, bool isFirstSection)
    {
        var title = new TextBlock
        {
            Text = HumanizeReportFieldKey("UseOfForce"),
            Foreground = R("CadMuted"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, isFirstSection ? 0 : 10, 0, 4)
        };
        target.Children.Add(title);

        var card = new Border
        {
            Background = R("CadElevated"),
            BorderBrush = R("CadBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(10, 8, 10, 10),
            Margin = new Thickness(0, 0, 0, 2)
        };
        var inner = new StackPanel();

        var typeTag = uof["Type"]?.ToString()?.Trim() ?? "";
        AppendReportDetailLabeledRow(inner, "FORCE TYPE", ResolveUofTypeDisplayLabel(typeTag), R("CadText"));

        var typeOther = uof["TypeOther"]?.ToString()?.Trim();
        if (!string.IsNullOrEmpty(typeOther))
            AppendReportDetailLabeledRow(inner, "OTHER (SPECIFY)", typeOther, R("CadText"));

        var justification = uof["Justification"]?.ToString()?.Trim();
        if (!string.IsNullOrEmpty(justification))
        {
            inner.Children.Add(new TextBlock
            {
                Text = "JUSTIFICATION",
                Style = (Style)FindResource("CadFieldLabel"),
                Margin = new Thickness(0, 6, 0, 2)
            });
            inner.Children.Add(new TextBlock
            {
                Text = justification,
                Foreground = R("CadText"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        static bool BoolLoose(JToken? t)
        {
            if (t == null || t.Type == JTokenType.Null) return false;
            if (t.Type == JTokenType.Boolean) return t.Value<bool>();
            if (bool.TryParse(t.ToString(), out var b)) return b;
            return int.TryParse(t.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i != 0;
        }

        var injSuspect = BoolLoose(uof["InjuryToSuspect"]);
        var injOfficer = BoolLoose(uof["InjuryToOfficer"]);
        var ok = R("CadSemanticSuccess");
        var bad = R("CadSemanticDanger");
        AppendReportDetailLabeledRow(inner, "INJURY TO SUBJECT", injSuspect ? "Yes" : "No", injSuspect ? bad : ok);
        AppendReportDetailLabeledRow(inner, "INJURY TO OFFICER", injOfficer ? "Yes" : "No", injOfficer ? bad : ok);

        var witnesses = uof["Witnesses"]?.ToString()?.Trim();
        if (!string.IsNullOrEmpty(witnesses))
        {
            inner.Children.Add(new TextBlock
            {
                Text = "WITNESSES",
                Style = (Style)FindResource("CadFieldLabel"),
                Margin = new Thickness(0, 6, 0, 2)
            });
            inner.Children.Add(new TextBlock
            {
                Text = witnesses,
                Foreground = R("CadText"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
        }

        card.Child = inner;
        target.Children.Add(card);
    }

    static string ResolveUofTypeDisplayLabel(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return "Not applicable";
        return tag.Trim() switch
        {
            "Taser" => "Taser",
            "Baton" => "Baton",
            "Fist" => "Fist / hands",
            "Firearm" => "Firearm",
            "Other" => "Other",
            var x => x
        };
    }

    void AppendReportDetailLabeledRow(Panel panel, string labelUpper, string value, Brush valueBrush)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lb = new TextBlock
        {
            Text = labelUpper,
            Style = (Style)FindResource("CadFieldLabel"),
            Margin = new Thickness(0, 1, 10, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        var val = new TextBlock
        {
            Text = value,
            Foreground = valueBrush,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(lb, 0);
        Grid.SetColumn(val, 1);
        grid.Children.Add(lb);
        grid.Children.Add(val);
        panel.Children.Add(grid);
    }

    Border BuildReportTimelineRow((DateTime when, string id, string typeLabel, string status, string? sub, JArray? items, string? reportTypeKey) e, int gen)
    {
        var outer = new Border
        {
            BorderBrush = R("CadBorder"),
            BorderThickness = new Thickness(1),
            Background = R("CadPanel"),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var dock = new DockPanel();
        var bar = new Border
        {
            Width = 3,
            Background = R("CadAccent"),
            Margin = new Thickness(0, 0, 12, 0)
        };
        DockPanel.SetDock(bar, Dock.Left);
        dock.Children.Add(bar);

        var header = new StackPanel();
        var dateStr = e.when == default
            ? "DATE UNKNOWN"
            : NativeMdtFormat.FormatDateTimeDisplay(e.when).ToUpperInvariant();
        header.Children.Add(new TextBlock
        {
            Text = dateStr,
            FontFamily = (FontFamily)FindResource("CadFontMono"),
            FontSize = 9,
            Foreground = R("CadAccent")
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{e.typeLabel.ToUpperInvariant()}  ·  {e.id}  ·  {e.status}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = R("CadText"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 0)
        });
        if (!string.IsNullOrWhiteSpace(e.sub) && e.sub != "—")
            header.Children.Add(new TextBlock
            {
                Text = e.sub,
                Foreground = R("CadText"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        if (e.items != null)
        {
            foreach (var it in e.items)
                header.Children.Add(new TextBlock
                {
                    Text = "• " + it,
                    Foreground = R("CadMuted"),
                    FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
        }

        header.Children.Add(new TextBlock
        {
            Text = "Expand for full on-file report",
            Foreground = R("CadMuted"),
            FontSize = 9,
            Margin = new Thickness(0, 6, 0, 0),
            FontStyle = FontStyles.Italic
        });

        var detailRoot = new StackPanel { Margin = new Thickness(8, 4, 0, 0) };
        detailRoot.Children.Add(new TextBlock
        {
            Text = "Open this section to pull the complete record from the MDT host.",
            Foreground = R("CadMuted"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        });

        var exp = new Expander
        {
            Header = header,
            Content = detailRoot,
            IsExpanded = false,
            Foreground = R("CadText"),
            Background = Brushes.Transparent,
            Margin = new Thickness(10, 10, 12, 10)
        };

        var loaded = false;
        var loading = false;
        exp.Expanded += async (_, _) =>
        {
            if (loaded || loading) return;
            if (string.IsNullOrEmpty(e.reportTypeKey))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (gen != _searchGen) return;
                    detailRoot.Children.Clear();
                    detailRoot.Children.Add(new TextBlock
                    {
                        Text = "This row has no mapped report type on the server summary. Open the Reports module to look up the ID manually.",
                        Foreground = R("CadMuted"),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap
                    });
                });
                loaded = true;
                return;
            }

            var dataPath = NativeReportDraftFactory.DataPathFor(e.reportTypeKey);
            if (string.IsNullOrEmpty(dataPath))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (gen != _searchGen) return;
                    detailRoot.Children.Clear();
                    detailRoot.Children.Add(new TextBlock
                    {
                        Text = "Native client does not have a form mapping for this report type yet.",
                        Foreground = R("CadMuted"),
                        TextWrapping = TextWrapping.Wrap
                    });
                });
                loaded = true;
                return;
            }

            loading = true;
            await Dispatcher.InvokeAsync(() =>
            {
                if (gen != _searchGen) return;
                detailRoot.Children.Clear();
                detailRoot.Children.Add(new TextBlock
                {
                    Text = "Pulling full record…",
                    Foreground = R("CadMuted"),
                    FontSize = 11
                });
            });

            var http = _connection?.Http;
            if (http == null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (gen != _searchGen) return;
                    detailRoot.Children.Clear();
                    detailRoot.Children.Add(new TextBlock
                    {
                        Text = "Not connected to an MDT session.",
                        Foreground = R("CadMuted"),
                        TextWrapping = TextWrapping.Wrap
                    });
                    loading = false;
                });
                return;
            }

            try
            {
                var tok = await http.GetDataJsonAsync(dataPath).ConfigureAwait(false);
                if (gen != _searchGen)
                {
                    loading = false;
                    return;
                }

                var full = FindReportObjectById(tok, e.id);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (gen != _searchGen) return;
                    detailRoot.Children.Clear();
                    if (full == null)
                    {
                        detailRoot.Children.Add(new TextBlock
                        {
                            Text = $"Report {e.id} was not found in the current {dataPath} list (it may have been deleted since this search).",
                            Foreground = R("CadMuted"),
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
                    else
                    {
                        AppendJObjectLabeledReportDetail(detailRoot, full);
                        detailRoot.Children.Add(new TextBlock
                        {
                            Text = "Read-only snapshot — edit or save from the Reports module.",
                            Foreground = R("CadMuted"),
                            FontSize = 10,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 12, 0, 0)
                        });
                    }
                    loaded = true;
                    loading = false;
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (gen != _searchGen) return;
                    detailRoot.Children.Clear();
                    detailRoot.Children.Add(new TextBlock
                    {
                        Text = "Could not load full report: " + ex.Message,
                        Foreground = R("CadText"),
                        TextWrapping = TextWrapping.Wrap
                    });
                    detailRoot.Children.Add(new TextBlock
                    {
                        Text = "Collapse and expand to retry.",
                        Foreground = R("CadMuted"),
                        FontSize = 10,
                        Margin = new Thickness(0, 6, 0, 0)
                    });
                    loading = false;
                });
            }
        };

        dock.Children.Add(exp);
        outer.Child = dock;
        return outer;
    }

    async Task LoadPedReportsTabAsync(MdtHttpClient http, string pedName, int gen)
    {
        await MdtBusyUi.RunAsync(ModuleBusy, "SUBJECT REPORTS", "Collecting report index…", async () =>
        {
            JObject? pedRep = null;
            try
            {
                var (_, text) = await http.PostAsync("data/pedReports", JsonConvert.SerializeObject(pedName)).ConfigureAwait(false);
                if (gen != _searchGen) return;
                if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('{'))
                    pedRep = JObject.Parse(text);
            }
            catch { /* ignore */ }

            var ids = new List<string>();
            void Collect(string key)
            {
                if (pedRep?[key] is not JArray a) return;
                foreach (var t in a)
                {
                    var id = t["Id"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                }
            }

            Collect("citations");
            Collect("arrests");
            Collect("incidents");
            Collect("propertyEvidence");
            Collect("injuries");
            Collect("impounds");

            JArray? summaries = null;
            if (ids.Count > 0)
            {
                try
                {
                    var (_, sumText) = await http.PostAsync("data/reportSummaries", JsonConvert.SerializeObject(ids)).ConfigureAwait(false);
                    if (gen != _searchGen) return;
                    if (!string.IsNullOrWhiteSpace(sumText) && sumText.TrimStart().StartsWith('['))
                        summaries = JArray.Parse(sumText);
                }
                catch { /* optional */ }
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (gen != _searchGen) return;
                PedReportsPanel.Children.Clear();
                AddSubjectDossierHeader(PedReportsPanel, "MASTER EVENT LOG", "");

                if (pedRep == null)
                {
                    PedReportsPanel.Children.Add(new TextBlock { Text = "Could not load report index.", Foreground = R("CadMuted"), TextWrapping = TextWrapping.Wrap });
                    return;
                }

                var sumById = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                if (summaries != null)
                {
                    foreach (var t in summaries.OfType<JObject>())
                    {
                        var id = t["id"]?.ToString();
                        if (!string.IsNullOrEmpty(id)) sumById[id] = t;
                    }
                }

                var timeline = new List<(DateTime when, string id, string typeLabel, string status, string? sub, JArray? items, string? reportTypeKey)>();
                void Accum(string pedKey, string fallback)
                {
                    if (pedRep![pedKey] is not JArray rows) return;
                    foreach (var row in rows.OfType<JObject>())
                    {
                        var id = row["Id"]?.ToString()?.Trim() ?? "—";
                        _ = TryParseReportRowTime(row["TimeStamp"], out var when);
                        var st = NativeMdtFormat.Text(row["Status"]);
                        sumById.TryGetValue(id, out var sum);
                        var typeLabel = sum?["typeLabel"]?.ToString() ?? fallback;
                        var sub = sum?["subtitle"]?.ToString();
                        var itemArr = sum?["items"] as JArray;
                        var reportTypeKey = ResolveNativeReportTypeKey(sum, pedKey);
                        timeline.Add((when, id, typeLabel, st, sub, itemArr, reportTypeKey));
                    }
                }

                Accum("citations", "Citation");
                Accum("arrests", "Arrest");
                Accum("incidents", "Incident");
                Accum("propertyEvidence", "Property & Evidence");
                Accum("injuries", "Injury");
                Accum("impounds", "Impound");

                timeline.Sort((a, b) => b.when.CompareTo(a.when));

                foreach (var entry in timeline)
                    PedReportsPanel.Children.Add(BuildReportTimelineRow(entry, gen));

                if (timeline.Count == 0)
                    PedReportsPanel.Children.Add(new TextBlock
                    {
                        Text = "No report rows returned for this name in the master index (citations, arrests, incidents, property/evidence, injuries, impounds).",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = R("CadMuted"),
                        FontSize = 12,
                        Margin = new Thickness(0, 8, 0, 0)
                    });
            });
        }, minimumVisibleMs: 480);
    }

    void ClearPedForm()
    {
        PedFormName.Text = PedFormFirst.Text = PedFormLast.Text = PedFormModel.Text = "";
        PedFormDob.Text = PedFormGender.Text = PedFormAddress.Text = "";
        PedFormWarrant.Text = PedFormAdvisory.Text = PedFormTimesStopped.Text = "";
        PedFormLicExp.Text = "";
        PedFormWpExp.Text = "";
        PedFormHuntExp.Text = PedFormFishExp.Text = "";
        RepopulateStringCombo(PedFormLicStatus, CdfPedFormOptions.DriverLicenseStates, null);
        RepopulateStringCombo(PedFormWpStatus, CdfPedFormOptions.DocumentStatuses, null);
        RepopulateStringCombo(PedFormHuntStatus, CdfPedFormOptions.DocumentStatuses, null);
        RepopulateStringCombo(PedFormFishStatus, CdfPedFormOptions.DocumentStatuses, null);
        RepopulateWeaponPermitTypeCombo(PedFormWpType, null);
        PedFormIncarcerated.Text = PedFormDeceasedAt.Text = "";
        PedFormWanted.IsChecked = PedFormProbation.IsChecked = PedFormParole.IsChecked = false;
        PedFormGang.IsChecked = PedFormDeceased.IsChecked = false;
    }

    void LoadPedFormFromBase()
    {
        if (_pedBaseForSave == null)
        {
            ClearPedForm();
            return;
        }

        var p = _pedBaseForSave;
        PedFormName.Text = NativePedVehicleForms.GetStr(p["Name"]);
        PedFormFirst.Text = NativePedVehicleForms.GetStr(p["FirstName"]);
        PedFormLast.Text = NativePedVehicleForms.GetStr(p["LastName"]);
        PedFormModel.Text = NativePedVehicleForms.GetStr(p["ModelName"]);
        PedFormDob.Text = NativePedVehicleForms.GetStr(p["Birthday"]);
        PedFormGender.Text = NativePedVehicleForms.GetStr(p["Gender"]);
        PedFormAddress.Text = NativePedVehicleForms.GetStr(p["Address"]);
        PedFormWanted.IsChecked = NativePedVehicleForms.GetBool(p["IsWanted"]);
        PedFormProbation.IsChecked = NativePedVehicleForms.GetBool(p["IsOnProbation"]);
        PedFormParole.IsChecked = NativePedVehicleForms.GetBool(p["IsOnParole"]);
        PedFormGang.IsChecked = NativePedVehicleForms.GetBool(p["IsInGang"]);
        PedFormDeceased.IsChecked = NativePedVehicleForms.GetBool(p["IsDeceased"]);
        PedFormWarrant.Text = NativePedVehicleForms.GetStr(p["WarrantText"]);
        PedFormAdvisory.Text = NativePedVehicleForms.GetStr(p["AdvisoryText"]);
        PedFormTimesStopped.Text = NativePedVehicleForms.GetInt(p["TimesStopped"], 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        RepopulateStringCombo(PedFormLicStatus, CdfPedFormOptions.DriverLicenseStates, NativePedVehicleForms.GetStr(p["LicenseStatus"]));
        PedFormLicExp.Text = NativePedVehicleForms.GetStr(p["LicenseExpiration"]);
        RepopulateStringCombo(PedFormWpStatus, CdfPedFormOptions.DocumentStatuses, NativePedVehicleForms.GetStr(p["WeaponPermitStatus"]));
        RepopulateWeaponPermitTypeCombo(PedFormWpType, NativePedVehicleForms.GetStr(p["WeaponPermitType"]));
        PedFormWpExp.Text = NativePedVehicleForms.GetStr(p["WeaponPermitExpiration"]);
        RepopulateStringCombo(PedFormHuntStatus, CdfPedFormOptions.DocumentStatuses, NativePedVehicleForms.GetStr(p["HuntingPermitStatus"]));
        PedFormHuntExp.Text = NativePedVehicleForms.GetStr(p["HuntingPermitExpiration"]);
        RepopulateStringCombo(PedFormFishStatus, CdfPedFormOptions.DocumentStatuses, NativePedVehicleForms.GetStr(p["FishingPermitStatus"]));
        PedFormFishExp.Text = NativePedVehicleForms.GetStr(p["FishingPermitExpiration"]);
        PedFormIncarcerated.Text = NativePedVehicleForms.GetStr(p["IncarceratedUntil"]);
        PedFormDeceasedAt.Text = NativePedVehicleForms.GetStr(p["DeceasedAt"]);
    }

    JObject BuildPedJObjectFromForm()
    {
        if (_pedBaseForSave == null)
            throw new InvalidOperationException("No person loaded.");
        var o = (JObject)_pedBaseForSave.DeepClone();
        NativePedVehicleForms.SetStr(o, "Name", PedFormName.Text);
        NativePedVehicleForms.SetStr(o, "FirstName", PedFormFirst.Text);
        NativePedVehicleForms.SetStr(o, "LastName", PedFormLast.Text);
        NativePedVehicleForms.SetStr(o, "ModelName", PedFormModel.Text);
        NativePedVehicleForms.SetStr(o, "Birthday", PedFormDob.Text);
        NativePedVehicleForms.SetStr(o, "Gender", PedFormGender.Text);
        NativePedVehicleForms.SetStr(o, "Address", PedFormAddress.Text);
        NativePedVehicleForms.SetBool(o, "IsWanted", PedFormWanted);
        NativePedVehicleForms.SetBool(o, "IsOnProbation", PedFormProbation);
        NativePedVehicleForms.SetBool(o, "IsOnParole", PedFormParole);
        NativePedVehicleForms.SetBool(o, "IsInGang", PedFormGang);
        NativePedVehicleForms.SetBool(o, "IsDeceased", PedFormDeceased);
        NativePedVehicleForms.SetStr(o, "WarrantText", PedFormWarrant.Text);
        NativePedVehicleForms.SetStr(o, "AdvisoryText", PedFormAdvisory.Text);
        NativePedVehicleForms.SetInt(o, "TimesStopped", PedFormTimesStopped.Text, NativePedVehicleForms.GetInt(_pedBaseForSave["TimesStopped"], 0));
        NativePedVehicleForms.SetStr(o, "LicenseStatus", SelectedComboString(PedFormLicStatus));
        NativePedVehicleForms.SetStr(o, "LicenseExpiration", PedFormLicExp.Text);
        NativePedVehicleForms.SetStr(o, "WeaponPermitStatus", SelectedComboString(PedFormWpStatus));
        NativePedVehicleForms.SetStr(o, "WeaponPermitType", WeaponPermitTypeFromCombo(PedFormWpType));
        NativePedVehicleForms.SetStr(o, "WeaponPermitExpiration", PedFormWpExp.Text);
        NativePedVehicleForms.SetStr(o, "HuntingPermitStatus", SelectedComboString(PedFormHuntStatus));
        NativePedVehicleForms.SetStr(o, "HuntingPermitExpiration", PedFormHuntExp.Text);
        NativePedVehicleForms.SetStr(o, "FishingPermitStatus", SelectedComboString(PedFormFishStatus));
        NativePedVehicleForms.SetStr(o, "FishingPermitExpiration", PedFormFishExp.Text);
        NativePedVehicleForms.SetStr(o, "IncarceratedUntil", PedFormIncarcerated.Text);
        NativePedVehicleForms.SetStr(o, "DeceasedAt", PedFormDeceasedAt.Text);
        o.Remove("CourtCases");
        return o;
    }

    async Task SavePedFromFormAsync()
    {
        if (_pedBaseForSave == null)
        {
            MessageBox.Show("Search for a person first.", "Person search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var jo = BuildPedJObjectFromForm();
            await PostUpdatePedAsync(jo).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Person search", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    async Task PostUpdatePedAsync(JObject jo)
    {
        var http = _connection?.Http;
        if (http == null)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show("Connect to MDT Pro first.", "Person search", MessageBoxButton.OK, MessageBoxImage.Information));
            return;
        }

        jo.Remove("CourtCases");
        var json = jo.ToString(Formatting.None);
        await MdtBusyUi.RunAsync(ModuleBusy, "ID / PED RECORD", "Committing identification update…", async () =>
        {
            var (status, text) = await http.PostActionAsync("updatePedData", json).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (status == HttpStatusCode.OK && string.Equals(text?.Trim(), "OK", StringComparison.Ordinal))
                {
                    CadSaveSound.TryPlay();
                    MessageBox.Show("Ped record saved.", "Person search", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (status == HttpStatusCode.NotFound)
                {
                    MessageBox.Show(
                        "The server could not apply this ped (subject must be in the world near you for HTTP updates, same as in-game MDT).\n\n" + text,
                        "Person search",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                MessageBox.Show($"Save failed ({(int)status}).\n\n{text}", "Person search", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }, minimumVisibleMs: 720);
    }

    TextBlock SectionTitle(string s) => new()
    {
        Text = s.ToUpperInvariant(),
        Style = (Style)FindResource("CadSectionTitle"),
        Margin = new Thickness(0, 14, 0, 6)
    };

    Grid FieldGrid((string label, string value)[] rows)
    {
        var t = R("CadText");
        var withBrush = new (string label, string value, Brush valueBrush)[rows.Length];
        for (var i = 0; i < rows.Length; i++)
            withBrush[i] = (rows[i].label, rows[i].value, t);
        return FieldGrid(withBrush);
    }

    Grid FieldGrid((string label, string value, Brush valueBrush)[] rows)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var r = 0;
        foreach (var row in rows)
        {
            if (row.value == "—" && row.label is not ("Warrant" or "Advisory"))
                continue;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lb = new TextBlock
            {
                Text = row.label.ToUpperInvariant(),
                Style = (Style)FindResource("CadFieldLabel"),
                Margin = new Thickness(0, 4, 12, 4),
                VerticalAlignment = VerticalAlignment.Top
            };
            var val = new TextBlock
            {
                Text = row.value,
                Foreground = row.valueBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 3)
            };
            Grid.SetRow(lb, r);
            Grid.SetRow(val, r);
            Grid.SetColumn(lb, 0);
            Grid.SetColumn(val, 1);
            g.Children.Add(lb);
            g.Children.Add(val);
            r++;
        }
        return g;
    }

    Border BulletBlock(IEnumerable<string> lines, Brush accent)
    {
        var sp = new StackPanel();
        var list = lines.ToList();
        if (list.Count == 0)
            sp.Children.Add(new TextBlock { Text = "—", Foreground = R("CadMuted") });
        else
        {
            foreach (var line in list)
            {
                var b = new Border
                {
                    BorderThickness = new Thickness(4, 0, 0, 0),
                    BorderBrush = accent,
                    Padding = new Thickness(10, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = R("CadElevated"),
                    Child = new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Foreground = R("CadText") }
                };
                sp.Children.Add(b);
            }
        }
        return new Border { Child = sp };
    }

    TextBlock NoteLine(string s) => new()
    {
        Text = s,
        TextWrapping = TextWrapping.Wrap,
        Foreground = R("CadMuted"),
        FontSize = 12,
        Margin = new Thickness(0, 0, 0, 4)
    };

    Border CourtCaseMini(string headline, string? reportId, string? hint)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = headline, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = R("CadText") });
        if (!string.IsNullOrWhiteSpace(reportId))
            sp.Children.Add(new TextBlock
            {
                Text = reportId,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Foreground = R("CadMuted"),
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        if (!string.IsNullOrWhiteSpace(hint))
            sp.Children.Add(new TextBlock
            {
                Text = hint,
                Foreground = R("CadSubTabActiveText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            });
        return new Border
        {
            BorderBrush = R("CadBorder"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Background = R("CadPanel"),
            Child = sp
        };
    }

    sealed class RecentRow
    {
        public RecentRow(string name, string type, string when)
        {
            Name = name;
            Type = type;
            When = when;
        }

        public string Name { get; }
        public string Type { get; }
        public string When { get; }
        public string Display => string.IsNullOrWhiteSpace(When) ? $"{Name}  ({Type})" : $"{Name}  ({Type})  ·  {When}";
    }

    sealed class HistoryRow
    {
        public HistoryRow(string resultName, string lastSearched)
        {
            ResultName = resultName;
            LastSearched = lastSearched;
        }

        public string ResultName { get; }
        public string LastSearched { get; }
        public string Display => string.IsNullOrWhiteSpace(LastSearched) ? ResultName : $"{ResultName}  ·  {LastSearched}";
    }
}
