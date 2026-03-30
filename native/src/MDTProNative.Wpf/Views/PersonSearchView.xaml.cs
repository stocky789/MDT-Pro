using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class PersonSearchView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;
    int _searchGen;
    /// <summary>Last loaded ped (full API object); form saves merge from this for arrays and unknown keys.</summary>
    JObject? _pedBaseForSave;

    public PersonSearchView()
    {
        InitializeComponent();
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

    Brush R(string key) => (Brush)FindResource(key);

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        DetailPanel.Children.Clear();
        DetailPanel.Children.Add(DetailPlaceholder);
        DetailPlaceholder.Visibility = Visibility.Visible;
        _pedBaseForSave = null;
        ClearPedForm();
        PersonDetailTabs.SelectedIndex = 0;
        RecentList.ItemsSource = null;
        HistoryList.ItemsSource = null;
        if (connection?.Http == null) return;
        _ = LoadSidebarsAsync();
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
                ClearPedForm();
                MessageBox.Show("Person not found.", "Person search", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            RenderPed(ped);
        });
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

        DetailPanel.Children.Add(SectionTitle("Status"));
        DetailPanel.Children.Add(FieldGrid(new (string, string)[]
        {
            ("Wanted", NativeMdtFormat.YesNo(p["IsWanted"])),
            ("Warrant", NativeMdtFormat.Text(p["WarrantText"])),
            ("Probation", NativeMdtFormat.YesNo(p["IsOnProbation"])),
            ("Parole", NativeMdtFormat.YesNo(p["IsOnParole"])),
            ("Gang affiliation", NativeMdtFormat.YesNo(p["IsInGang"])),
            ("Times stopped", NativeMdtFormat.Text(p["TimesStopped"])),
            ("Advisory", NativeMdtFormat.Text(p["AdvisoryText"])),
            ("Deceased", NativeMdtFormat.YesNo(p["IsDeceased"])),
        }));

        DetailPanel.Children.Add(SectionTitle("Licenses & permits"));
        DetailPanel.Children.Add(FieldGrid(new (string, string)[]
        {
            ("Driver license", NativeMdtFormat.Text(p["LicenseStatus"])),
            ("License expires", NativeMdtFormat.IsoDate(p["LicenseExpiration"])),
            ("Weapon permit", NativeMdtFormat.Text(p["WeaponPermitStatus"])),
            ("Weapon type", NativeMdtFormat.Text(p["WeaponPermitType"])),
            ("Weapon expires", NativeMdtFormat.IsoDate(p["WeaponPermitExpiration"])),
            ("Hunting", NativeMdtFormat.Text(p["HuntingPermitStatus"])),
            ("Hunting expires", NativeMdtFormat.IsoDate(p["HuntingPermitExpiration"])),
            ("Fishing", NativeMdtFormat.Text(p["FishingPermitStatus"])),
            ("Fishing expires", NativeMdtFormat.IsoDate(p["FishingPermitExpiration"])),
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

        if (p["CourtCases"] is JArray cc && cc.Count > 0)
        {
            DetailPanel.Children.Add(SectionTitle("Court cases"));
            foreach (var c in cc.OfType<JObject>())
            {
                var st = NativeMdtFormat.CourtStatus(c.Value<int?>("Status") ?? 0);
                var num = c["Number"]?.ToString() ?? "—";
                var head = $"{num} · {st}";
                DetailPanel.Children.Add(CourtCaseMini(head, c["ReportId"]?.ToString(), c["SupervisionRecordHint"]?.ToString()));
            }
        }

        _pedBaseForSave = (JObject)p.DeepClone();
        LoadPedFormFromBase();
        PersonDetailTabs.SelectedIndex = 0;

        DetailScroller.ScrollToVerticalOffset(0);
    }

    void ClearPedForm()
    {
        PedFormName.Text = PedFormFirst.Text = PedFormLast.Text = PedFormModel.Text = "";
        PedFormDob.Text = PedFormGender.Text = PedFormAddress.Text = "";
        PedFormWarrant.Text = PedFormAdvisory.Text = PedFormTimesStopped.Text = "";
        PedFormLicStatus.Text = PedFormLicExp.Text = "";
        PedFormWpStatus.Text = PedFormWpType.Text = PedFormWpExp.Text = "";
        PedFormHuntStatus.Text = PedFormHuntExp.Text = PedFormFishStatus.Text = PedFormFishExp.Text = "";
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
        PedFormLicStatus.Text = NativePedVehicleForms.GetStr(p["LicenseStatus"]);
        PedFormLicExp.Text = NativePedVehicleForms.GetStr(p["LicenseExpiration"]);
        PedFormWpStatus.Text = NativePedVehicleForms.GetStr(p["WeaponPermitStatus"]);
        PedFormWpType.Text = NativePedVehicleForms.GetStr(p["WeaponPermitType"]);
        PedFormWpExp.Text = NativePedVehicleForms.GetStr(p["WeaponPermitExpiration"]);
        PedFormHuntStatus.Text = NativePedVehicleForms.GetStr(p["HuntingPermitStatus"]);
        PedFormHuntExp.Text = NativePedVehicleForms.GetStr(p["HuntingPermitExpiration"]);
        PedFormFishStatus.Text = NativePedVehicleForms.GetStr(p["FishingPermitStatus"]);
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
        NativePedVehicleForms.SetStr(o, "LicenseStatus", PedFormLicStatus.Text);
        NativePedVehicleForms.SetStr(o, "LicenseExpiration", PedFormLicExp.Text);
        NativePedVehicleForms.SetStr(o, "WeaponPermitStatus", PedFormWpStatus.Text);
        NativePedVehicleForms.SetStr(o, "WeaponPermitType", PedFormWpType.Text);
        NativePedVehicleForms.SetStr(o, "WeaponPermitExpiration", PedFormWpExp.Text);
        NativePedVehicleForms.SetStr(o, "HuntingPermitStatus", PedFormHuntStatus.Text);
        NativePedVehicleForms.SetStr(o, "HuntingPermitExpiration", PedFormHuntExp.Text);
        NativePedVehicleForms.SetStr(o, "FishingPermitStatus", PedFormFishStatus.Text);
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
        var (status, text) = await http.PostActionAsync("updatePedData", json).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() =>
        {
            if (status == HttpStatusCode.OK && string.Equals(text?.Trim(), "OK", StringComparison.Ordinal))
            {
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
    }

    TextBlock SectionTitle(string s) => new()
    {
        Text = s.ToUpperInvariant(),
        Style = (Style)FindResource("CadSectionTitle"),
        Margin = new Thickness(0, 14, 0, 6)
    };

    Grid FieldGrid((string label, string value)[] rows)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = R("CadText");
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
                Foreground = text,
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
