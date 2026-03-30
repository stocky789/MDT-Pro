using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class FirearmsView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;
    bool _layoutPersistWired;
    int _searchGen;

    public FirearmsView()
    {
        InitializeComponent();
        Loaded += OnFirearmsLoaded;
        RecentList.DisplayMemberPath = nameof(RecentWeaponRow.Display);
        SearchBtn.Click += async (_, _) => await SearchAsync(QueryBox.Text);
        QueryBox.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                await SearchAsync(QueryBox.Text);
        };
        RecentList.MouseDoubleClick += async (_, _) =>
        {
            if (RecentList.SelectedItem is RecentWeaponRow rw)
            {
                QueryBox.Text = rw.LookupKey;
                await SearchAsync(rw.LookupKey);
            }
        };
    }

    void OnFirearmsLoaded(object sender, RoutedEventArgs e)
    {
        if (_layoutPersistWired) return;
        _layoutPersistWired = true;
        UiLayoutHooks.WireFirearms(this);
    }

    Brush R(string key) => (Brush)FindResource(key);

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        DetailPanel.Children.Clear();
        DetailPanel.Children.Add(DetailPlaceholder);
        DetailPlaceholder.Visibility = Visibility.Visible;
        RecentList.ItemsSource = null;
        if (connection?.Http == null) return;
        _ = LoadRecentAsync();
    }

    async Task LoadRecentAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        await MdtBusyUi.RunAsync(ModuleBusy, "FIREARMS REGISTRY", "Polling recent weapon activity…", async () =>
        {
        try
        {
            var tok = await http.GetDataJsonAsync("recentFirearms").ConfigureAwait(false);
            var rows = new List<RecentWeaponRow>();
            if (tok is JArray arr)
            {
                foreach (var t in arr.OfType<JObject>())
                {
                    var scratched = t["IsSerialScratched"]?.Value<bool>() == true;
                    var serial = t["SerialNumber"]?.ToString() ?? "";
                    var owner = t["OwnerPedName"]?.ToString() ?? "";
                    var name = (t["WeaponDisplayName"] ?? t["Description"] ?? t["WeaponModelId"])?.ToString()?.Trim() ?? "";
                    var lookup = scratched ? owner : (string.IsNullOrWhiteSpace(serial) ? owner : serial);
                    if (string.IsNullOrWhiteSpace(lookup) && string.IsNullOrWhiteSpace(name)) continue;
                    rows.Add(new RecentWeaponRow(scratched, serial, name, lookup));
                }
            }

            await Dispatcher.InvokeAsync(() =>
            {
                RecentList.ItemsSource = rows;
                RecentEmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            });
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                RecentList.ItemsSource = Array.Empty<RecentWeaponRow>();
                RecentEmptyHint.Visibility = Visibility.Visible;
            });
        }
        });
    }

    async Task SearchAsync(string? raw)
    {
        var http = _connection?.Http;
        if (http == null)
        {
            MessageBox.Show("Connect to MDT Pro first.", "Firearms", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var query = (raw ?? "").Trim();
        if (string.IsNullOrEmpty(query))
        {
            MessageBox.Show("Enter a serial number or owner name.", "Firearms", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await MdtBusyUi.RunAsync(ModuleBusy, "NCIC / WEAPONS", "Querying firearm registry…", async () =>
        {
            var gen = ++_searchGen;
            JObject? bySerial = null;
            try
            {
                var (_, text) = await http.PostAsync("data/firearmBySerial", JsonConvert.SerializeObject(query)).ConfigureAwait(false);
                if (gen != _searchGen) return;
                if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('{'))
                    bySerial = JObject.Parse(text);
            }
            catch
            {
                bySerial = null;
            }

            if (gen != _searchGen) return;

            if (bySerial != null && LooksLikeFirearmHit(bySerial))
            {
                await Dispatcher.InvokeAsync(() => RenderFirearms(new[] { bySerial }, single: true));
                return;
            }

            JArray? byOwner = null;
            try
            {
                var (_, text) = await http.PostAsync("data/firearmsForPed", JsonConvert.SerializeObject(query)).ConfigureAwait(false);
                if (gen != _searchGen) return;
                if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('['))
                    byOwner = JArray.Parse(text);
            }
            catch
            {
                byOwner = null;
            }

            if (gen != _searchGen) return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (byOwner == null || byOwner.Count == 0)
                {
                    MessageBox.Show("No firearm on file for that serial or owner.", "Firearms", MessageBoxButton.OK, MessageBoxImage.Information);
                    DetailPanel.Children.Clear();
                    DetailPanel.Children.Add(DetailPlaceholder);
                    DetailPlaceholder.Visibility = Visibility.Visible;
                    return;
                }

                RenderFirearms(byOwner.OfType<JObject>().ToList(), single: false);
            });
        });
    }

    static bool LooksLikeFirearmHit(JObject o)
    {
        if (o["Id"]?.Value<int>() > 0) return true;
        var hashTok = o["WeaponModelHash"];
        if (hashTok != null && hashTok.Type != JTokenType.Null && hashTok.Value<uint>() > 0)
            return true;
        var owner = o["OwnerPedName"]?.ToString();
        return !string.IsNullOrWhiteSpace(owner);
    }

    void RenderFirearms(IReadOnlyList<JObject> items, bool single)
    {
        DetailPanel.Children.Clear();
        DetailPlaceholder.Visibility = Visibility.Collapsed;

        if (single && items.Count == 1)
        {
            var f = items[0];
            var title = NativeMdtFormat.Text(f["WeaponDisplayName"]);
            if (title == "—") title = NativeMdtFormat.Text(f["Description"]);
            if (title != "—")
            {
                DetailPanel.Children.Add(new TextBlock
                {
                    Text = title.ToUpperInvariant(),
                    Style = (Style)FindResource("CadEventBandTitle")
                });
            }

            if (f["IsStolen"]?.Value<bool>() == true)
            {
                DetailPanel.Children.Add(new TextBlock
                {
                    Text = "STOLEN / HOT",
                    Foreground = (Brush)FindResource("CadUrgent"),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }
        }
        else
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = $"{items.Count} REGISTERED WEAPON(S)",
                Style = (Style)FindResource("CadEventBandTitle")
            });
        }

        foreach (var f in items)
        {
            if (!single)
                DetailPanel.Children.Add(SectionTitle(NativeMdtFormat.Text(f["WeaponDisplayName"])));
            DetailPanel.Children.Add(FieldGrid(new (string, string)[]
            {
                ("Serial", f["IsSerialScratched"]?.Value<bool>() == true ? "Scratched" : NativeMdtFormat.Text(f["SerialNumber"])),
                ("Owner", NativeMdtFormat.Text(f["OwnerPedName"])),
                ("Weapon", NativeMdtFormat.Text(f["WeaponDisplayName"])),
                ("Model ID", NativeMdtFormat.Text(f["WeaponModelId"])),
                ("Stolen", NativeMdtFormat.YesNo(f["IsStolen"])),
                ("Source", NativeMdtFormat.Text(f["Source"])),
                ("First seen", NativeMdtFormat.IsoDate(f["FirstSeenAt"])),
                ("Last seen", NativeMdtFormat.IsoDate(f["LastSeenAt"])),
                ("Notes", NativeMdtFormat.Text(f["Description"])),
            }));
        }

        if (single && items.Count == 1)
            DetailPanel.Children.Add(BuildFirearmDispatchSection(items[0]));

        DetailScroller.ScrollToVerticalOffset(0);
    }

    Border BuildFirearmDispatchSection(JObject f)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        sp.Children.Add(SectionTitle("Dispatch check log"));
        sp.Children.Add(new TextBlock
        {
            Text = "Posts /post/firearmCheckResult (same as browser MDT). Owner name is required.",
            Foreground = R("CadMuted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var scratched = f["IsSerialScratched"]?.Value<bool>() == true;
        TextBox ownerBox = new(), weaponBox = new(), serialBox = new(), modelIdBox = new(), statusBox = new();
        AddLabeledEditor(sp, "Owner name", NativeMdtFormat.Text(f["OwnerPedName"]), ownerBox);
        AddLabeledEditor(sp, "Weapon type / display", NativeMdtFormat.Text(f["WeaponDisplayName"]), weaponBox);
        AddLabeledEditor(sp, "Serial (if known)", scratched ? "" : NativeMdtFormat.Text(f["SerialNumber"]), serialBox);
        AddLabeledEditor(sp, "Weapon model ID", NativeMdtFormat.Text(f["WeaponModelId"]), modelIdBox);
        AddLabeledEditor(sp, "Check status", "Clear", statusBox);

        var btn = new Button
        {
            Content = "LOG CHECK RESULT",
            Style = (Style)FindResource("CadPrimaryActionButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0)
        };
        btn.Click += async (_, _) =>
        {
            var http = _connection?.Http;
            if (http == null)
            {
                MessageBox.Show("Connect to MDT Pro first.", "Firearms", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var owner = ownerBox.Text.Trim();
            if (string.IsNullOrEmpty(owner))
            {
                MessageBox.Show("Owner name is required.", "Firearms", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var body = JsonConvert.SerializeObject(new
            {
                serialNumber = string.IsNullOrWhiteSpace(serialBox.Text) ? null : serialBox.Text.Trim(),
                ownerName = owner,
                weaponType = string.IsNullOrWhiteSpace(weaponBox.Text) ? null : weaponBox.Text.Trim(),
                status = string.IsNullOrWhiteSpace(statusBox.Text) ? null : statusBox.Text.Trim(),
                weaponModelId = string.IsNullOrWhiteSpace(modelIdBox.Text) ? null : modelIdBox.Text.Trim()
            });

            try
            {
                await MdtBusyUi.RunAsync(ModuleBusy, "DISPATCH LOG", "Recording firearm check result…", async () =>
                {
                    var (code, text) = await http.PostActionAsync("firearmCheckResult", body).ConfigureAwait(false);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (code == HttpStatusCode.OK && !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('{'))
                        {
                            var jo = JObject.Parse(text);
                            if (jo.Value<bool?>("success") == true)
                            {
                                CadSaveSound.TryPlay();
                                MessageBox.Show("Check result logged.", "Firearms", MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }

                            MessageBox.Show(jo["error"]?.ToString() ?? text, "Firearms", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        MessageBox.Show(text, "Firearms", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }, minimumVisibleMs: 560);
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(ex.Message, "Firearms", MessageBoxButton.OK, MessageBoxImage.Warning));
            }
        };
        sp.Children.Add(btn);

        return new Border
        {
            BorderBrush = R("CadAccentDim"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Background = R("CadElevated"),
            Child = sp
        };
    }

    void AddLabeledEditor(Panel parent, string label, string initial, TextBox box)
    {
        box.Text = initial == "—" ? "" : initial;
        box.Style = (Style)FindResource("CadTextBoxCadField");
        box.Margin = new Thickness(0, 0, 0, 8);
        parent.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            Style = (Style)FindResource("CadFieldLabel"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        parent.Children.Add(box);
    }

    TextBlock SectionTitle(string s) => new()
    {
        Text = s.ToUpperInvariant(),
        Style = (Style)FindResource("CadSectionTitle"),
        Margin = new Thickness(0, 14, 0, 6)
    };

    Grid FieldGrid((string label, string value)[] rows)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = R("CadText");
        var ri = 0;
        foreach (var row in rows)
        {
            if (row.value == "—") continue;
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
            Grid.SetRow(lb, ri);
            Grid.SetRow(val, ri);
            Grid.SetColumn(lb, 0);
            Grid.SetColumn(val, 1);
            g.Children.Add(lb);
            g.Children.Add(val);
            ri++;
        }
        return g;
    }

    sealed class RecentWeaponRow
    {
        public RecentWeaponRow(bool scratched, string serial, string weaponName, string lookupKey)
        {
            Scratched = scratched;
            Serial = serial;
            WeaponName = weaponName;
            LookupKey = lookupKey;
        }

        public bool Scratched { get; }
        public string Serial { get; }
        public string WeaponName { get; }
        public string LookupKey { get; }

        public string Display
        {
            get
            {
                var s = Scratched ? "Scratched" : (string.IsNullOrWhiteSpace(Serial) ? "—" : Serial);
                var w = string.IsNullOrWhiteSpace(WeaponName) ? "Weapon" : WeaponName;
                return $"{s} — {w}";
            }
        }
    }
}
