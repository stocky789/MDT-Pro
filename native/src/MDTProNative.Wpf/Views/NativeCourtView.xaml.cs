using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class NativeCourtView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;
    JArray _cases = new();

    public NativeCourtView()
    {
        InitializeComponent();
        StatusFilter.ItemsSource = new[]
        {
            "All statuses",
            "Pending",
            "Convicted",
            "Acquitted",
            "Dismissed"
        };
        StatusFilter.SelectedIndex = 0;
        RefreshBtn.Click += async (_, _) => await LoadAsync();
        FilterBox.TextChanged += (_, _) => RenderCases();
        StatusFilter.SelectionChanged += (_, _) => RenderCases();
    }

    Brush R(string key) => (Brush)FindResource(key);

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        CaseStack.Children.Clear();
        if (connection?.Http == null)
        {
            CaseStack.Children.Add(Muted("Connect to MDT Pro to load the court docket."));
            return;
        }
        _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var token = await http.GetDataJsonAsync("court").ConfigureAwait(false);
            _cases = token is JArray a ? a : new JArray();
        }
        catch
        {
            _cases = new JArray();
        }
        await Dispatcher.InvokeAsync(RenderCases);
    }

    void RenderCases()
    {
        CaseStack.Children.Clear();
        var q = (FilterBox.Text ?? "").Trim().ToLowerInvariant();
        var statusIdx = StatusFilter.SelectedIndex;
        if (statusIdx < 0) statusIdx = 0;

        var filtered = _cases.OfType<JObject>().Where(c => c != null).Where(c =>
        {
            if (statusIdx > 0 && c.Value<int?>("Status") != statusIdx - 1)
                return false;
            if (string.IsNullOrEmpty(q)) return true;
            return (c["Number"]?.ToString() ?? "").ToLowerInvariant().Contains(q)
                   || (c["PedName"]?.ToString() ?? "").ToLowerInvariant().Contains(q)
                   || (c["ReportId"]?.ToString() ?? "").ToLowerInvariant().Contains(q);
        }).OrderByDescending(c => c["LastUpdatedUtc"]?.ToString() ?? "");

        foreach (var c in filtered)
            CaseStack.Children.Add(BuildCaseExpander(c));

        if (!filtered.Any())
            CaseStack.Children.Add(Muted("No cases match the current filter."));
    }

    UIElement Muted(string text) => new TextBlock
    {
        Text = text,
        Foreground = R("CadMuted"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 8, 4, 8)
    };

    Expander BuildCaseExpander(JObject c)
    {
        var status = c.Value<int?>("Status") ?? 0;
        var statusText = NativeMdtFormat.CourtStatus(status);
        var ped = c["PedName"]?.ToString() ?? "—";
        var num = c["Number"]?.ToString() ?? "—";
        var district = c["CourtDistrict"]?.ToString();
        var dateBits = new[] { c["HearingDateUtc"], c["ResolveAtUtc"], c["LastUpdatedUtc"] }
            .Select(t => NativeMdtFormat.IsoDate(t))
            .Where(s => s != "—").FirstOrDefault() ?? "—";

        var header = new TextBlock { TextWrapping = TextWrapping.Wrap };
        header.Inlines.Add(new Run(ped) { FontWeight = FontWeights.Bold, Foreground = R("CadOrange") });
        header.Inlines.Add(new Run($"  ·  {num}") { Foreground = R("CadMuted") });
        if (!string.IsNullOrWhiteSpace(district))
            header.Inlines.Add(new Run($"  ·  {district}") { Foreground = R("CadMuted") });
        header.Inlines.Add(new Run($"  ·  {dateBits}") { Foreground = R("CadMuted") });
        header.Inlines.Add(new Run($"  ·  {statusText}")
        {
            Foreground = status switch
            {
                0 => R("CadAccent"),
                1 => R("CadUrgent"),
                2 => R("CadOnline"),
                _ => R("CadOrange")
            }
        });

        var body = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var synth = c.Value<bool?>("IsSyntheticSupervisionBackstory") == true
                    || string.Equals(c["ReportId"]?.ToString(), "MDT-SUPERVISION-BACKSTORY", StringComparison.Ordinal);
        if (synth)
            body.Children.Add(NoteBorder("Prior disposition (reconstructed record for supervision backstory)."));

        body.Children.Add(KvGrid(new (string, string)[]
        {
            ("Report ID", c["ReportId"]?.ToString() ?? "—"),
            ("Trial", TrialSummary(c)),
            ("Priors on file", PriorSummary(c)),
            ("Court", string.IsNullOrWhiteSpace(c["CourtName"]?.ToString())
                ? "—"
                : $"{c["CourtName"]} ({c["CourtType"] ?? "—"})"),
            ("Judge", c["JudgeName"]?.ToString() ?? "—"),
            ("Prosecutor", c["ProsecutorName"]?.ToString() ?? "—"),
            ("Defense", c["DefenseAttorneyName"]?.ToString() ?? "—"),
        }));

        var chargesTitle = SectionTitle("Charges");
        body.Children.Add(chargesTitle);
        var charges = c["Charges"] as JArray;
        if (charges == null || charges.Count == 0)
            body.Children.Add(Muted("No charges on file."));
        else
        {
            foreach (var ch in charges.OfType<JObject>())
                body.Children.Add(ChargeCard(ch, status));
        }

        if (status == 0 && !synth)
            body.Children.Add(BuildActionsPanel(c));

        if (!string.IsNullOrWhiteSpace(c["OutcomeNotes"]?.ToString()))
        {
            body.Children.Add(SectionTitle("Notes"));
            body.Children.Add(WrapText(c["OutcomeNotes"]!.ToString()));
        }

        return new Expander
        {
            Header = header,
            Content = body,
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = R("CadText"),
            BorderBrush = R("CadAccentDim"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Background = R("CadPanel")
        };
    }

    static string TrialSummary(JObject c)
    {
        if (c.Value<bool?>("IsJuryTrial") != true)
            return "Bench trial";
        var conv = c.Value<int?>("JuryVotesForConviction") ?? 0;
        var acq = c.Value<int?>("JuryVotesForAcquittal") ?? 0;
        var size = c.Value<int?>("JurySize") ?? 0;
        var st = c.Value<int?>("Status") ?? 0;
        return st == 0 ? $"Jury pool: {size}" : $"Jury {conv}–{acq} / {size}";
    }

    static string PriorSummary(JObject c)
    {
        var cts = c.Value<int?>("PriorCitationCount") ?? 0;
        var arr = c.Value<int?>("PriorArrestCount") ?? 0;
        if (cts == 0 && arr == 0) return "—";
        return $"{cts} citation(s), {arr} arrest(s)";
    }

    Border ChargeCard(JObject ch, int caseStatus)
    {
        var name = ch["Name"]?.ToString() ?? "—";
        var fine = ch["Fine"]?.ToString() ?? "0";
        var time = ch["Time"];
        var timeStr = time == null || time.Type == JTokenType.Null ? "—" : time.ToString();
        var detail = $"Fine: {fine}  ·  Time (days / statutory): {timeStr}";

        var outcome = ch["Outcome"];
        var oc = outcome?.Value<int?>();
        var displayOutcome = oc is null or 0 ? caseStatus : oc.Value;
        var outcomeLabel = displayOutcome switch
        {
            1 => "Convicted",
            2 => "Acquitted",
            3 => "Dismissed",
            _ => caseStatus == 0 ? "Pending" : NativeMdtFormat.CourtStatus(caseStatus)
        };

        var pillFg = displayOutcome switch
        {
            1 => R("CadUrgent"),
            2 => R("CadOnline"),
            3 => R("CadOrange"),
            _ => R("CadAccent")
        };
        var pillBg = displayOutcome switch
        {
            1 => new SolidColorBrush(Color.FromRgb(0x3A, 0x12, 0x12)),
            2 => new SolidColorBrush(Color.FromRgb(0x0F, 0x2A, 0x18)),
            3 => new SolidColorBrush(Color.FromRgb(0x32, 0x28, 0x0C)),
            _ => new SolidColorBrush(Color.FromRgb(0x12, 0x22, 0x38))
        };
        var pill = new Border
        {
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Top,
            BorderThickness = new Thickness(1),
            BorderBrush = pillFg,
            Background = pillBg,
            Child = new TextBlock
            {
                Text = outcomeLabel.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = pillFg
            }
        };

        var head = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(pill, Dock.Right);
        head.Children.Add(pill);
        head.Children.Add(new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.SemiBold,
            Foreground = R("CadText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 8, 0)
        });

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            Foreground = R("CadMuted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 12
        });

        return new Border
        {
            BorderBrush = R("CadAccentDim"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
            Background = R("CadElevated"),
            Child = new Border
            {
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = R("CadAmberGold"),
                Padding = new Thickness(8, 0, 0, 0),
                Child = stack
            }
        };
    }

    TextBlock SectionTitle(string s) => new()
    {
        Text = s.ToUpperInvariant(),
        Style = (Style)FindResource("CadSectionTitle"),
        Margin = new Thickness(0, 12, 0, 6)
    };

    Border NoteBorder(string text) => new()
    {
        BorderBrush = R("CadAccentDim"),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(10),
        Margin = new Thickness(0, 0, 0, 10),
        Background = R("CadElevated"),
        Child = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = R("CadMuted"),
            FontSize = 12
        }
    };

    TextBlock WrapText(string s) => new()
    {
        Text = s,
        TextWrapping = TextWrapping.Wrap,
        Foreground = R("CadText"),
        FontSize = 12,
        Margin = new Thickness(0, 0, 0, 8)
    };

    Border BuildActionsPanel(JObject c)
    {
        var num = c["Number"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(num))
            return NoteBorder("Missing case number; cannot post updates.");

        var sp = new StackPanel();
        sp.Children.Add(SectionTitle("Case actions"));

        sp.Children.Add(FieldLabelRow("Plea"));
        var pleaTb = new TextBox
        {
            Text = c["Plea"]?.ToString() ?? "Not Guilty",
            Style = (Style)FindResource("CadTextBoxCadField"),
            Margin = new Thickness(0, 4, 0, 8)
        };
        sp.Children.Add(pleaTb);

        sp.Children.Add(FieldLabelRow("Outcome notes"));
        var notesTb = new TextBox
        {
            Text = c["OutcomeNotes"]?.ToString() ?? "",
            AcceptsReturn = true,
            MinHeight = 56,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Style = (Style)FindResource("CadTextBoxCadField"),
            Margin = new Thickness(0, 4, 0, 8)
        };
        sp.Children.Add(notesTb);

        sp.Children.Add(FieldLabelRow("Manual outcome"));
        var statusCb = new ComboBox
        {
            Style = (Style)FindResource("CadComboBox"),
            Margin = new Thickness(0, 4, 0, 8)
        };
        statusCb.ItemsSource = new[]
        {
            new { Label = "Convicted", Value = 1 },
            new { Label = "Acquitted", Value = 2 },
            new { Label = "Dismissed", Value = 3 },
        };
        statusCb.DisplayMemberPath = "Label";
        statusCb.SelectedValuePath = "Value";
        statusCb.SelectedIndex = 0;
        sp.Children.Add(statusCb);

        sp.Children.Add(FieldLabelRow("Evidence report ID (incident / injury / citation)"));
        var attachTb = new TextBox
        {
            Style = (Style)FindResource("CadTextBoxCadField"),
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 4, 0, 4),
            ToolTip = "Attach or detach one report ID at a time."
        };
        sp.Children.Add(attachTb);

        var btnRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var applyBtn = new Button
        {
            Content = "APPLY OUTCOME",
            Style = (Style)FindResource("CadPrimaryActionButton"),
            Margin = new Thickness(0, 0, 8, 8)
        };
        var forceBtn = new Button
        {
            Content = "AUTO-RESOLVE",
            Style = (Style)FindResource("CadRailOutlineButton"),
            Margin = new Thickness(0, 0, 8, 8)
        };
        var attachBtn = new Button
        {
            Content = "ATTACH",
            Style = (Style)FindResource("CadRailOutlineButton"),
            Margin = new Thickness(0, 0, 8, 8)
        };
        var detachBtn = new Button
        {
            Content = "DETACH",
            Style = (Style)FindResource("CadRailOutlineButton"),
            Margin = new Thickness(0, 0, 8, 8)
        };

        applyBtn.Click += async (_, _) =>
        {
            var http = _connection?.Http;
            if (http == null) return;
            var st = statusCb.SelectedValue is int i ? i : 1;
            var payload = new JObject
            {
                ["Number"] = num,
                ["Status"] = st,
                ["Plea"] = pleaTb.Text?.Trim() ?? "",
                ["OutcomeNotes"] = notesTb.Text ?? "",
                ["OutcomeReasoning"] = ""
            };
            payload["IsJuryTrial"] = c["IsJuryTrial"];
            payload["JurySize"] = c["JurySize"];
            payload["JuryVotesForConviction"] = c["JuryVotesForConviction"];
            payload["JuryVotesForAcquittal"] = c["JuryVotesForAcquittal"];
            payload["HasPublicDefender"] = c["HasPublicDefender"];
            var (code, resp) = await http.PostActionAsync("updateCourtCaseStatus", payload.ToString(Formatting.None)).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (code == HttpStatusCode.OK)
                    MessageBox.Show("Case updated.", "Court", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show($"Update failed ({(int)code}).\n\n{resp}", "Court", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            if (code == HttpStatusCode.OK)
                await LoadAsync();
        };

        forceBtn.Click += async (_, _) =>
        {
            var http = _connection?.Http;
            if (http == null) return;
            var payload = new JObject
            {
                ["Number"] = num,
                ["Plea"] = pleaTb.Text?.Trim() ?? "",
                ["OutcomeNotes"] = notesTb.Text ?? ""
            };
            var (code, resp) = await http.PostActionAsync("forceResolveCourtCase", payload.ToString(Formatting.None)).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (code == HttpStatusCode.OK)
                    MessageBox.Show("Case resolved (simulation).", "Court", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show($"Auto-resolve failed ({(int)code}).\n\n{resp}", "Court", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            if (code == HttpStatusCode.OK)
                await LoadAsync();
        };

        attachBtn.Click += async (_, _) =>
        {
            var http = _connection?.Http;
            if (http == null) return;
            var rid = attachTb.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(rid))
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Enter a report ID.", "Court", MessageBoxButton.OK, MessageBoxImage.Information));
                return;
            }

            var payload = new JObject { ["courtCaseNumber"] = num, ["reportId"] = rid };
            var (code, resp) = await http.PostActionAsync("attachReportToCourtCase", payload.ToString(Formatting.None)).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (code == HttpStatusCode.OK)
                    MessageBox.Show("Report attached.", "Court", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show($"Attach failed ({(int)code}).\n\n{resp}", "Court", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            if (code == HttpStatusCode.OK)
                await LoadAsync();
        };

        detachBtn.Click += async (_, _) =>
        {
            var http = _connection?.Http;
            if (http == null) return;
            var rid = attachTb.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(rid))
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Enter a report ID.", "Court", MessageBoxButton.OK, MessageBoxImage.Information));
                return;
            }

            var payload = new JObject { ["courtCaseNumber"] = num, ["reportId"] = rid };
            var (code, resp) = await http.PostActionAsync("detachReportFromCourtCase", payload.ToString(Formatting.None)).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (code == HttpStatusCode.OK)
                    MessageBox.Show("Report detached.", "Court", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show($"Detach failed ({(int)code}).\n\n{resp}", "Court", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            if (code == HttpStatusCode.OK)
                await LoadAsync();
        };

        btnRow.Children.Add(applyBtn);
        btnRow.Children.Add(forceBtn);
        btnRow.Children.Add(attachBtn);
        btnRow.Children.Add(detachBtn);
        sp.Children.Add(btnRow);

        return new Border
        {
            BorderBrush = R("CadAccentDim"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 12, 0, 8),
            Background = R("CadElevated"),
            Child = sp
        };
    }

    TextBlock FieldLabelRow(string s) => new()
    {
        Text = s.ToUpperInvariant(),
        Style = (Style)FindResource("CadFieldLabel"),
        Margin = new Thickness(0, 8, 0, 0)
    };

    Grid KvGrid((string label, string value)[] rows)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 200 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        for (var i = 0; i < rows.Length; i++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lb = new TextBlock
            {
                Text = rows[i].label.ToUpperInvariant(),
                Style = (Style)FindResource("CadFieldLabel"),
                Margin = new Thickness(0, 4, 12, 4),
                VerticalAlignment = VerticalAlignment.Top
            };
            var val = new TextBlock
            {
                Text = rows[i].value,
                TextWrapping = TextWrapping.Wrap,
                Foreground = R("CadText"),
                Margin = new Thickness(0, 4, 0, 4)
            };
            Grid.SetRow(lb, i);
            Grid.SetRow(val, i);
            Grid.SetColumn(lb, 0);
            Grid.SetColumn(val, 1);
            g.Children.Add(lb);
            g.Children.Add(val);
        }
        return g;
    }
}
