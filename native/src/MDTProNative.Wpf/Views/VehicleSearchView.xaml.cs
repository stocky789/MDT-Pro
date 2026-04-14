using System.Collections.Generic;
using System.Globalization;
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

public partial class VehicleSearchView : UserControl, IMdtBoundView
{
    const string NearbyEmptyDefault = "No vehicles detected nearby.";
    const string NearbyEmptyDeferred =
        "Live scan did not run. GTA V must be focused (not paused) so the mod can read nearby vehicles. Alt-tab into the game briefly, then press Refresh. Tip: turn off Pause on focus loss in GTA if you want the world to keep simulating while using this MDT.";

    /// <summary>Top-level JSON names accepted by <c>MDTProVehicleData</c> deserialization for <c>updateVehicleData</c>.</summary>
    static readonly HashSet<string> VehicleDataPropertyNames = new(StringComparer.Ordinal)
    {
        "LicensePlate", "ModelName", "ModelDisplayName", "IsStolen", "Owner", "Color", "VinStatus",
        "Make", "Model", "PrimaryColor", "SecondaryColor", "VehicleIdentificationNumber",
        "RegistrationStatus", "RegistrationExpiration", "InsuranceStatus", "InsuranceExpiration", "BOLOs",
    };

    MdtConnectionManager? _connection;
    bool _layoutPersistWired;
    int _searchGen;
    JObject? _currentVehicle;
    public VehicleSearchView()
    {
        InitializeComponent();
        Loaded += OnVehicleSearchLoaded;
        NearbyList.DisplayMemberPath = nameof(NearbyRow.Display);
        HistoryList.DisplayMemberPath = nameof(HistoryRow.Display);
        SearchBtn.Click += async (_, _) => await SearchAsync(QueryBox.Text);
        ContextVehBtn.Click += async (_, _) => await SearchAsync("context");
        RefreshNearbyBtn.Click += async (_, _) =>
        {
            if (_connection?.Http == null) return;
            await MdtBusyUi.RunAsync(ModuleBusy, "VEHICLE MDC", "Refreshing nearby vehicles…", LoadNearbyAsync);
        };
        QueryBox.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                await SearchAsync(QueryBox.Text);
        };
        NearbyList.MouseDoubleClick += async (_, _) =>
        {
            if (NearbyList.SelectedItem is NearbyRow r && !string.IsNullOrWhiteSpace(r.Plate))
                await SearchAsync(r.Plate);
        };
        HistoryList.MouseDoubleClick += async (_, _) =>
        {
            if (HistoryList.SelectedItem is HistoryRow h)
                await SearchAsync(h.PlateQuery);
        };
        SaveVehicleFormBtn.Click += async (_, _) => await SaveVehicleFromFormAsync();
        ClearVehHistoryBtn.Click += async (_, _) => await ClearVehicleHistoryAsync();
    }

    void OnVehicleSearchLoaded(object sender, RoutedEventArgs e)
    {
        if (_layoutPersistWired) return;
        _layoutPersistWired = true;
        UiLayoutHooks.WireVehicleSearch(this);
    }

    Brush R(string key) => (Brush)FindResource(key);

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        _currentVehicle = null;
        ClearVehicleForm();
        VehicleDetailTabs.SelectedIndex = 0;
        DetailPanel.Children.Clear();
        DetailPanel.Children.Add(DetailPlaceholder);
        DetailPlaceholder.Visibility = Visibility.Visible;
        NearbyList.ItemsSource = null;
        HistoryList.ItemsSource = null;
        NearbyEmptyHint.Text = NearbyEmptyDefault;
        NearbyEmptyHint.Visibility = Visibility.Collapsed;
        if (connection?.Http == null) return;
        _ = LoadSidebarsAsync();
    }

    async Task LoadSidebarsAsync()
    {
        await MdtBusyUi.RunAsync(ModuleBusy, "VEHICLE MDC", "Scanning nearby units and search history…", async () =>
        {
            await LoadNearbyAsync();
            await LoadHistoryAsync();
        });
    }

    async Task LoadNearbyAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var fetch = await ReportNearbyVehiclesClient.FetchNearbyAsync(http, 8).ConfigureAwait(false);
            var rows = fetch.Items
                .Select(s => new NearbyRow(s.Plate, s.ModelDisplay ?? "", s.DistanceMeters, s.Stolen))
                .ToList();

            if (fetch.ScanDeferred && rows.Count > 0)
                MdtShellEvents.LogCad("Nearby vehicles: scan deferred — list may be stale until GTA V is focused and you Refresh.");

            await Dispatcher.InvokeAsync(() =>
            {
                NearbyList.ItemsSource = rows;
                NearbyEmptyHint.Text = fetch.ScanDeferred && rows.Count == 0 ? NearbyEmptyDeferred : NearbyEmptyDefault;
                NearbyEmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            });
        }
        catch (Exception ex)
        {
            MdtShellEvents.LogCad("Nearby vehicles: " + ex.Message);
            await Dispatcher.InvokeAsync(() =>
            {
                NearbyList.ItemsSource = Array.Empty<NearbyRow>();
                NearbyEmptyHint.Visibility = Visibility.Visible;
            });
        }
    }

    async Task ClearVehicleHistoryAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        if (MessageBox.Show("Clear all saved vehicle search history on the MDT server?", "Vehicle search", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            var (code, text) = await http.PostActionAsync("clearSearchHistory", "vehicle").ConfigureAwait(false);
            if (code == HttpStatusCode.OK && string.Equals(text?.Trim(), "OK", StringComparison.Ordinal))
                await LoadHistoryAsync();
            else
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Could not clear history ({(int)code}).\n\n{text}", "Vehicle search", MessageBoxButton.OK, MessageBoxImage.Warning));
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show(ex.Message, "Vehicle search", MessageBoxButton.OK, MessageBoxImage.Warning));
        }
    }

    async Task LoadHistoryAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var (_, histBody) = await http.PostAsync("data/searchHistory", "vehicle").ConfigureAwait(false);
            var histList = new List<HistoryRow>();
            if (!string.IsNullOrWhiteSpace(histBody) && histBody.TrimStart().StartsWith('['))
            {
                var ha = JArray.Parse(histBody);
                foreach (var o in ha.OfType<JObject>())
                {
                    var resultPlate = o["ResultName"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(resultPlate))
                        histList.Add(new HistoryRow(resultPlate!, o["LastSearched"]?.ToString() ?? ""));
                }
            }

            await Dispatcher.InvokeAsync(() => { HistoryList.ItemsSource = histList; });
        }
        catch
        {
            await Dispatcher.InvokeAsync(() => { HistoryList.ItemsSource = Array.Empty<HistoryRow>(); });
        }
    }

    async Task SearchAsync(string? rawQuery)
    {
        var http = _connection?.Http;
        if (http == null)
        {
            MessageBox.Show("Connect to MDT Pro first.", "Vehicle search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var query = (rawQuery ?? "").Trim();
        if (string.IsNullOrEmpty(query))
        {
            MessageBox.Show("Enter a plate, VIN, or use CONTEXT VEH.", "Vehicle search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await MdtBusyUi.RunAsync(ModuleBusy, "TRAFFIC / DMV QUERY", "Accessing motor vehicle records…", async () =>
        {
            var gen = ++_searchGen;
            JObject? veh = null;
            try
            {
                // Match browser MDT: vehicleSearch.js posts the raw plate/VIN string.
                var (status, text) = await http.PostAsync("data/specificVehicle", query).ConfigureAwait(false);
                if (gen != _searchGen) return;
                if (status != HttpStatusCode.OK)
                {
                    await Dispatcher.InvokeAsync(() =>
                        MessageBox.Show(
                            $"Vehicle search failed ({(int)status}).\n\n{text}",
                            "Vehicle search",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning));
                    return;
                }

                var t = (text ?? "").Trim();
                if (string.IsNullOrEmpty(t) || t.Equals("null", StringComparison.OrdinalIgnoreCase))
                    veh = null;
                else
                    veh = JToken.Parse(t) as JObject;
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        "Could not read vehicle search response.\n\n" + ex.Message,
                        "Vehicle search",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
                return;
            }

            if (gen != _searchGen) return;

            if (veh == null)
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Vehicle not found.", "Vehicle search", MessageBoxButton.OK, MessageBoxImage.Information));
                return;
            }

            var plate = veh["LicensePlate"]?.ToString()?.Trim() ?? "";
            JArray? searchRec = null;
            JArray? impound = null;
            if (!string.IsNullOrWhiteSpace(plate))
            {
                try
                {
                    var (_, sr) = await http.PostAsync("data/vehicleSearchByPlate", JsonConvert.SerializeObject(plate)).ConfigureAwait(false);
                    if (gen != _searchGen) return;
                    if (!string.IsNullOrWhiteSpace(sr) && sr.TrimStart().StartsWith('['))
                        searchRec = JArray.Parse(sr);
                }
                catch { /* optional */ }

                try
                {
                    var (_, ir) = await http.PostAsync("data/impoundReportsByPlate", JsonConvert.SerializeObject(plate)).ConfigureAwait(false);
                    if (gen != _searchGen) return;
                    if (!string.IsNullOrWhiteSpace(ir) && ir.TrimStart().StartsWith('['))
                        impound = JArray.Parse(ir);
                }
                catch { /* optional */ }
            }

            if (gen != _searchGen) return;
            await Dispatcher.InvokeAsync(() => RenderVehicle(veh, searchRec, impound));
        });
    }

    void RenderVehicle(JObject v, JArray? searchRecords, JArray? impoundRows)
    {
        _currentVehicle = v;
        DetailPanel.Children.Clear();
        DetailPlaceholder.Visibility = Visibility.Collapsed;

        var plate = NativeMdtFormat.Text(v["LicensePlate"]);
        if (plate != "—")
        {
            var band = new TextBlock
            {
                Text = plate.ToUpperInvariant(),
                Style = (Style)FindResource("CadEventBandTitle")
            };
            if (v["IsStolen"]?.Value<bool>() == true)
                band.Foreground = (Brush)FindResource("CadUrgent");
            DetailPanel.Children.Add(band);
        }

        var vehPhotoGen = _searchGen;
        DetailPanel.Children.Add(CreateVehicleCataloguePhotoChrome(v["ModelName"]?.ToString(), vehPhotoGen));

        DetailPanel.Children.Add(SectionTitle("Registration"));
        var text = R("CadText");
        var ok = R("CadSemanticSuccess");
        var warn = R("CadSemanticWarning");
        var bad = R("CadSemanticDanger");
        DetailPanel.Children.Add(FieldGrid(new (string label, string value, Brush valueBrush)[]
        {
            ("Plate", NativeMdtFormat.Text(v["LicensePlate"]), text),
            ("VIN", NativeMdtFormat.Text(v["VehicleIdentificationNumber"]), text),
            ("VIN status", NativeMdtFormat.Text(v["VinStatus"]), NativeVehicleSearchBrushes.ForVinStatus(v["VinStatus"], text, ok, warn, bad)),
            ("Model", NativeMdtFormat.Text(v["ModelDisplayName"]), text),
            ("Make / model", JoinMakeModel(v), text),
            ("Color", NativeMdtFormat.Text(v["Color"]), text),
            ("Primary", NativeMdtFormat.Text(v["PrimaryColor"]), text),
            ("Secondary", NativeMdtFormat.Text(v["SecondaryColor"]), text),
            ("Owner", NativeMdtFormat.Text(v["Owner"]), text),
            ("Stolen", NativeMdtFormat.YesNo(v["IsStolen"]), NativeVehicleSearchBrushes.ForVehicleField(v["IsStolen"], text, ok, warn, bad)),
            ("Registration", NativeMdtFormat.Text(v["RegistrationStatus"]), NativeVehicleSearchBrushes.ForVehicleField(v["RegistrationStatus"], text, ok, warn, bad)),
            ("Reg. expires", NativeMdtFormat.IsoDate(v["RegistrationExpiration"]),
                NativeVehicleSearchBrushes.ForExpirationWithPairedStatus(v["RegistrationExpiration"], v["RegistrationStatus"], text, warn, bad)),
            ("Insurance", NativeMdtFormat.Text(v["InsuranceStatus"]), NativeVehicleSearchBrushes.ForVehicleField(v["InsuranceStatus"], text, ok, warn, bad)),
            ("Ins. expires", NativeMdtFormat.IsoDate(v["InsuranceExpiration"]),
                NativeVehicleSearchBrushes.ForExpirationWithPairedStatus(v["InsuranceExpiration"], v["InsuranceStatus"], text, warn, bad)),
        }));

        if (v["BOLOs"] is JArray bolos && bolos.Count > 0)
        {
            DetailPanel.Children.Add(SectionTitle("BOLOs"));
            foreach (var b in bolos)
            {
                if (b is JObject bo)
                    DetailPanel.Children.Add(BoloRow(bo));
            }
        }

        var canMod = v["CanModifyBOLOs"]?.Value<bool>() == true;
        var plateOk = !string.IsNullOrWhiteSpace(v["LicensePlate"]?.ToString());
        if (canMod && plate != "—")
        {
            var reasonBox = new TextBox { Style = (Style)FindResource("CadTextBox"), Margin = new Thickness(0, 0, 0, 6) };
            var addBtn = new Button
            {
                Content = "ADD BOLO",
                Style = (Style)FindResource("CadAccentButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 8, 0)
            };
            addBtn.Click += async (_, _) => await TryAddBoloAsync(plate, reasonBox.Text, NativeMdtFormat.Text(v["ModelDisplayName"]));
            var impoundBtn = new Button
            {
                Content = "IMPOUND REPORT",
                Style = (Style)FindResource("CadRailOutlineButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 0)
            };
            impoundBtn.Click += (_, _) => TryOpenImpoundReportFromVehicle(v);
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            btnRow.Children.Add(addBtn);
            btnRow.Children.Add(impoundBtn);
            DetailPanel.Children.Add(SectionTitle("Add BOLO (vehicle in range)"));
            DetailPanel.Children.Add(new TextBlock { Text = "REASON", Style = (Style)FindResource("CadFieldLabel"), Margin = new Thickness(0, 0, 0, 4) });
            DetailPanel.Children.Add(reasonBox);
            DetailPanel.Children.Add(btnRow);
        }
        else if (plateOk)
        {
            var impoundBtn = new Button
            {
                Content = "IMPOUND REPORT",
                Style = (Style)FindResource("CadRailOutlineButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12)
            };
            impoundBtn.Click += (_, _) => TryOpenImpoundReportFromVehicle(v);
            DetailPanel.Children.Add(SectionTitle("Create report from vehicle"));
            DetailPanel.Children.Add(impoundBtn);
        }

        if (searchRecords != null && searchRecords.Count > 0)
        {
            DetailPanel.Children.Add(SectionTitle("Vehicle search hits (contraband)"));
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
                DetailPanel.Children.Add(NoteLine(line));
            }
        }

        if (impoundRows != null && impoundRows.Count > 0)
        {
            DetailPanel.Children.Add(SectionTitle("Impound history"));
            foreach (var r in impoundRows.OfType<JObject>())
            {
                var head = $"{NativeMdtFormat.Text(r["Id"])} · {NativeMdtFormat.IsoDate(r["TimeStamp"])} · {NativeMdtFormat.Text(r["Status"])}";
                DetailPanel.Children.Add(NoteLine(head));
                var sub = string.Join(" · ", new[]
                {
                    NativeMdtFormat.Text(r["ImpoundReason"]),
                    NativeMdtFormat.Text(r["TowCompany"]),
                    NativeMdtFormat.Text(r["ImpoundLot"])
                }.Where(s => s != "—"));
                if (!string.IsNullOrWhiteSpace(sub))
                    DetailPanel.Children.Add(NoteLine(sub));
            }
        }

        LoadVehicleFormFromCurrent();
        VehicleDetailTabs.SelectedIndex = 0;

        DetailScroller.ScrollToVerticalOffset(0);
    }

    Border CreateVehicleCataloguePhotoChrome(string? modelName, int gen)
    {
        var outer = new Border
        {
            Width = 200,
            Height = 112,
            Margin = new Thickness(0, 0, 0, 10),
            Background = R("CadElevated"),
            BorderBrush = R("CadBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            ClipToBounds = true
        };
        var img = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        var ph = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(modelName) ? "No model name — catalogue photo unavailable" : "Loading catalogue still…",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = R("CadMuted"),
            FontSize = 10,
            Margin = new Thickness(6),
            Visibility = Visibility.Visible
        };
        var grid = new Grid();
        grid.Children.Add(img);
        grid.Children.Add(ph);
        outer.Child = grid;

        _ = ApplyVehicleCataloguePhotoAsync(img, ph, modelName, gen);
        return outer;
    }

    async Task ApplyVehicleCataloguePhotoAsync(Image img, TextBlock ph, string? modelName, int gen)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (gen != _searchGen) return;
                ph.Visibility = Visibility.Visible;
                ph.Text = "No catalogue photo\n(no vehicle model name)";
                img.Visibility = Visibility.Collapsed;
            });
            return;
        }

        System.Windows.Media.Imaging.BitmapImage? bmp = null;
        try
        {
            bmp = await NativeCatalogueImageLoader.LoadVehicleCataloguePhotoAsync(modelName).ConfigureAwait(false);
        }
        catch
        {
            /* ignore */
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
                ph.Text = "No catalogue photo\n(not on FiveM docs CDN)";
                img.Visibility = Visibility.Collapsed;
            }
        });
    }

    void ClearVehicleForm()
    {
        VehFormPlate.Text = VehFormModelName.Text = VehFormModelDisplay.Text = "";
        VehFormOwner.Text = VehFormColor.Text = VehFormVinStatus.Text = "";
        VehFormMake.Text = VehFormModel.Text = VehFormPriColor.Text = VehFormSecColor.Text = "";
        VehFormVin.Text = VehFormRegStatus.Text = VehFormRegExp.Text = "";
        VehFormInsStatus.Text = VehFormInsExp.Text = "";
        VehFormStolen.IsChecked = false;
    }

    void LoadVehicleFormFromCurrent()
    {
        if (_currentVehicle == null)
        {
            ClearVehicleForm();
            return;
        }

        var v = _currentVehicle;
        VehFormPlate.Text = NativePedVehicleForms.GetStr(v["LicensePlate"]);
        VehFormModelName.Text = NativePedVehicleForms.GetStr(v["ModelName"]);
        VehFormModelDisplay.Text = NativePedVehicleForms.GetStr(v["ModelDisplayName"]);
        VehFormStolen.IsChecked = v["IsStolen"]?.Value<bool>() == true;
        VehFormOwner.Text = NativePedVehicleForms.GetStr(v["Owner"]);
        VehFormColor.Text = NativePedVehicleForms.GetStr(v["Color"]);
        VehFormVinStatus.Text = NativePedVehicleForms.GetStr(v["VinStatus"]);
        VehFormMake.Text = NativePedVehicleForms.GetStr(v["Make"]);
        VehFormModel.Text = NativePedVehicleForms.GetStr(v["Model"]);
        VehFormPriColor.Text = NativePedVehicleForms.GetStr(v["PrimaryColor"]);
        VehFormSecColor.Text = NativePedVehicleForms.GetStr(v["SecondaryColor"]);
        VehFormVin.Text = NativePedVehicleForms.GetStr(v["VehicleIdentificationNumber"]);
        VehFormRegStatus.Text = NativePedVehicleForms.GetStr(v["RegistrationStatus"]);
        VehFormRegExp.Text = NativePedVehicleForms.GetStr(v["RegistrationExpiration"]);
        VehFormInsStatus.Text = NativePedVehicleForms.GetStr(v["InsuranceStatus"]);
        VehFormInsExp.Text = NativePedVehicleForms.GetStr(v["InsuranceExpiration"]);
    }

    JObject BuildVehicleJObjectFromForm()
    {
        if (_currentVehicle == null)
            throw new InvalidOperationException("No vehicle loaded.");
        var o = (JObject)_currentVehicle.DeepClone();
        o.Remove("CanModifyBOLOs");
        NativePedVehicleForms.SetStr(o, "LicensePlate", VehFormPlate.Text);
        NativePedVehicleForms.SetStr(o, "ModelName", VehFormModelName.Text);
        NativePedVehicleForms.SetStr(o, "ModelDisplayName", VehFormModelDisplay.Text);
        o["IsStolen"] = VehFormStolen.IsChecked == true;
        NativePedVehicleForms.SetStr(o, "Owner", VehFormOwner.Text);
        NativePedVehicleForms.SetStr(o, "Color", VehFormColor.Text);
        NativePedVehicleForms.SetStr(o, "VinStatus", VehFormVinStatus.Text);
        NativePedVehicleForms.SetStr(o, "Make", VehFormMake.Text);
        NativePedVehicleForms.SetStr(o, "Model", VehFormModel.Text);
        NativePedVehicleForms.SetStr(o, "PrimaryColor", VehFormPriColor.Text);
        NativePedVehicleForms.SetStr(o, "SecondaryColor", VehFormSecColor.Text);
        NativePedVehicleForms.SetStr(o, "VehicleIdentificationNumber", VehFormVin.Text);
        NativePedVehicleForms.SetStr(o, "RegistrationStatus", VehFormRegStatus.Text);
        NativePedVehicleForms.SetStr(o, "RegistrationExpiration", VehFormRegExp.Text);
        NativePedVehicleForms.SetStr(o, "InsuranceStatus", VehFormInsStatus.Text);
        NativePedVehicleForms.SetStr(o, "InsuranceExpiration", VehFormInsExp.Text);
        KeepOnlyVehicleModelProperties(o);
        return o;
    }

    async Task SaveVehicleFromFormAsync()
    {
        if (_currentVehicle == null)
        {
            MessageBox.Show("Search for a vehicle first.", "Vehicle search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var jo = BuildVehicleJObjectFromForm();
            await PostUpdateVehicleAsync(jo).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Vehicle search", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    async Task PostUpdateVehicleAsync(JObject jo)
    {
        var http = _connection?.Http;
        if (http == null)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show("Connect to MDT Pro first.", "Vehicle search", MessageBoxButton.OK, MessageBoxImage.Information));
            return;
        }

        jo.Remove("CanModifyBOLOs");
        KeepOnlyVehicleModelProperties(jo);
        var json = jo.ToString(Formatting.None);
        await MdtBusyUi.RunAsync(ModuleBusy, "VEHICLE RECORD", "Committing update to MDC host…", async () =>
        {
            var (status, text) = await http.PostActionAsync("updateVehicleData", json).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (status == HttpStatusCode.OK && string.Equals(text?.Trim(), "OK", StringComparison.Ordinal))
                {
                    CadSaveSound.TryPlay();
                    MessageBox.Show("Vehicle record saved.", "Vehicle search", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (status == HttpStatusCode.NotFound)
                {
                    MessageBox.Show(
                        "The server could not apply this vehicle (vehicle must be in the world near you for HTTP updates, same as in-game MDT).\n\n" + text,
                        "Vehicle search",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                MessageBox.Show($"Save failed ({(int)status}).\n\n{text}", "Vehicle search", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }, minimumVisibleMs: 720);
    }

    static void KeepOnlyVehicleModelProperties(JObject o)
    {
        foreach (var p in o.Properties().ToList())
        {
            if (!VehicleDataPropertyNames.Contains(p.Name))
                p.Remove();
        }
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

    async Task TryAddBoloAsync(string plate, string reason, string modelDisplay)
    {
        var http = _connection?.Http;
        if (http == null) return;
        var r = reason.Trim();
        if (string.IsNullOrEmpty(r))
        {
            MessageBox.Show("Enter a BOLO reason.", "Vehicle search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var body = JsonConvert.SerializeObject(new
        {
            LicensePlate = plate.Trim(),
            Reason = r,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IssuedBy = "MDC",
            ModelDisplayName = string.IsNullOrWhiteSpace(modelDisplay) || modelDisplay == "—" ? null : modelDisplay
        });

        string? refreshPlate = null;
        try
        {
            await MdtBusyUi.RunAsync(ModuleBusy, "BOLO NETWORK", "Transmitting BOLO request…", async () =>
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
                                CadSaveSound.TryPlay();
                                MdtShellEvents.LogCad("BOLO added.");
                                refreshPlate = plate;
                                return;
                            }

                            MessageBox.Show(jo["error"]?.ToString() ?? text, "BOLO", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        catch
                        {
                            MdtShellEvents.LogCad("BOLO: " + text);
                        }
                    }
                    else
                        MessageBox.Show(text, "BOLO", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }, minimumVisibleMs: 420);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => MessageBox.Show(ex.Message, "BOLO", MessageBoxButton.OK, MessageBoxImage.Error));
        }

        if (!string.IsNullOrEmpty(refreshPlate))
            await SearchAsync(refreshPlate);
    }

    async Task TryRemoveBoloAsync(string plate, string reason)
    {
        var http = _connection?.Http;
        if (http == null) return;
        var body = JsonConvert.SerializeObject(new { LicensePlate = plate.Trim(), Reason = reason });
        string? refreshPlate = null;
        try
        {
            await MdtBusyUi.RunAsync(ModuleBusy, "BOLO NETWORK", "Clearing BOLO entry…", async () =>
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
                                refreshPlate = plate;
                                return;
                            }

                            MessageBox.Show(jo["error"]?.ToString() ?? text, "BOLO", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        catch
                        {
                            MdtShellEvents.LogCad("BOLO remove: " + text);
                        }
                    }
                    else
                        MessageBox.Show(text, "BOLO", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }, minimumVisibleMs: 420);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => MessageBox.Show(ex.Message, "BOLO", MessageBoxButton.OK, MessageBoxImage.Error));
        }

        if (!string.IsNullOrEmpty(refreshPlate))
            await SearchAsync(refreshPlate);
    }

    Border BoloRow(JObject b)
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

        var plate = _currentVehicle?["LicensePlate"]?.ToString()?.Trim() ?? "";
        var canMod = _currentVehicle?["CanModifyBOLOs"]?.Value<bool>() == true;
        if (canMod && !string.IsNullOrEmpty(plate))
        {
            var rawReason = b["Reason"]?.ToString() ?? b["reason"]?.ToString() ?? "";
            var rm = new Button
            {
                Content = "REMOVE",
                Style = (Style)FindResource("CadButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(12, 4, 12, 4)
            };
            rm.Click += async (_, _) => await TryRemoveBoloAsync(plate, rawReason);
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

    void TryOpenImpoundReportFromVehicle(JObject vehicle)
    {
        if (_connection?.Http == null)
        {
            MessageBox.Show("Connect to MDT Pro first.", "Vehicle search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MdtShellEvents.RequestNavigateToNewReportFromVehicleSearch("impound", (JObject)vehicle.DeepClone());
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

    TextBlock NoteLine(string s) => new()
    {
        Text = s,
        TextWrapping = TextWrapping.Wrap,
        Foreground = R("CadMuted"),
        FontSize = 12,
        Margin = new Thickness(0, 0, 0, 4)
    };

    sealed class NearbyRow
    {
        public NearbyRow(string plate, string model, double? distanceM, bool stolen)
        {
            Plate = plate;
            Model = model;
            DistanceM = distanceM;
            Stolen = stolen;
        }

        public string Plate { get; }
        public string Model { get; }
        public double? DistanceM { get; }
        public bool Stolen { get; }

        public string Display
        {
            get
            {
                var m = string.IsNullOrWhiteSpace(Model) ? "" : $" — {Model}";
                var d = DistanceM.HasValue ? $" ({DistanceM.Value:F1} m)" : "";
                var tag = Stolen ? " [STOLEN]" : "";
                return $"{Plate}{m}{d}{tag}";
            }
        }
    }

    sealed class HistoryRow
    {
        public HistoryRow(string resultPlate, string lastSearched)
        {
            ResultPlate = resultPlate;
            LastSearched = lastSearched;
        }

        public string PlateQuery => ResultPlate;
        public string ResultPlate { get; }
        public string LastSearched { get; }
        public string Display => string.IsNullOrWhiteSpace(LastSearched) ? ResultPlate : $"{ResultPlate}  ·  {LastSearched}";
    }
}
