using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Views.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class NativeCourtView : UserControl, IMdtBoundView
{
    /// <summary>Matches browser <c>court.js</c> default <c>language.court.pleaMap || ['Not Guilty', 'Guilty', 'No Contest']</c>.</summary>
    static readonly string[] DefaultCourtPleaOptions = ["Not Guilty", "Guilty", "No Contest"];

    MdtConnectionManager? _connection;
    JArray _cases = new();
    string[] _courtPleaOptions = DefaultCourtPleaOptions;
    Dictionary<string, JObject> _reportSummariesById = new(StringComparer.OrdinalIgnoreCase);

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
        SortFilter.ItemsSource = new[]
        {
            "Recently updated",
            "Highest risk first",
            "Newest case number"
        };
        SortFilter.SelectedIndex = 0;
        RefreshBtn.Click += async (_, _) => await LoadAsync();
        FilterBox.TextChanged += (_, _) => RenderCases();
        StatusFilter.SelectionChanged += (_, _) => RenderCases();
        SortFilter.SelectionChanged += (_, _) => RenderCases();
    }

    Brush R(string key) => (Brush)FindResource(key);

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        CaseStack.Children.Clear();
        _reportSummariesById.Clear();
        _courtPleaOptions = DefaultCourtPleaOptions;
        if (connection?.Http == null)
        {
            CaseStack.Children.Add(Muted("Connect to MDT Pro to load the court docket."));
            return;
        }
        _ = LoadAsync();
        _ = RefreshCourtPleaOptionsFromLanguageAsync();
    }

    async Task RefreshCourtPleaOptionsFromLanguageAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var lang = await http.GetLanguageJsonAsync().ConfigureAwait(false);
            if (lang?["court"]?["pleaMap"] is not JArray map || map.Count == 0)
                return;
            var list = new List<string>();
            foreach (var t in map)
            {
                if (t.Type != JTokenType.String) return;
                var s = t.Value<string>()?.Trim();
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
            if (list.Count == 0) return;
            var arr = list.ToArray();
            await Dispatcher.InvokeAsync(() =>
            {
                if (_connection?.Http != http) return;
                _courtPleaOptions = arr;
                RenderCases();
            });
        }
        catch
        {
            // keep defaults
        }
    }

    async Task LoadAsync()
    {
        await MdtBusyUi.RunAsync(DocketBusy, "COURT DOCKET", "Synchronizing case files…", LoadCoreAsync);
    }

    async Task LoadCoreAsync()
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

        _reportSummariesById.Clear();
        try
        {
            var idSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _cases.OfType<JObject>())
            {
                var rid = t["ReportId"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(rid) && rid != "MDT-SUPERVISION-BACKSTORY")
                    idSet.Add(rid);
                if (t["AttachedReportIds"] is JArray att)
                {
                    foreach (var x in att)
                    {
                        var s = x?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(s)) idSet.Add(s);
                    }
                }
            }

            if (idSet.Count > 0 && http != null)
            {
                var (_, sumText) = await http.PostAsync("data/reportSummaries", JsonConvert.SerializeObject(idSet.ToList()))
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(sumText) && sumText.TrimStart().StartsWith('['))
                {
                    var arr = JArray.Parse(sumText);
                    foreach (var o in arr.OfType<JObject>())
                    {
                        var id = o["id"]?.ToString();
                        if (!string.IsNullOrEmpty(id))
                            _reportSummariesById[id] = o;
                    }
                }
            }
        }
        catch
        {
            _reportSummariesById.Clear();
        }

        await Dispatcher.InvokeAsync(RenderCases);
    }

    void RenderCases()
    {
        CaseStack.Children.Clear();
        var q = (FilterBox.Text ?? "").Trim().ToLowerInvariant();
        var statusIdx = StatusFilter.SelectedIndex;
        if (statusIdx < 0) statusIdx = 0;
        var sortIdx = SortFilter.SelectedIndex;
        if (sortIdx < 0) sortIdx = 0;

        var filtered = _cases.OfType<JObject>().Where(c => c != null).Where(c =>
        {
            if (statusIdx > 0 && c.Value<int?>("Status") != statusIdx - 1)
                return false;
            if (string.IsNullOrEmpty(q)) return true;
            return (c["Number"]?.ToString() ?? "").ToLowerInvariant().Contains(q)
                   || (c["PedName"]?.ToString() ?? "").ToLowerInvariant().Contains(q)
                   || (c["ReportId"]?.ToString() ?? "").ToLowerInvariant().Contains(q);
        }).ToList();

        filtered = sortIdx switch
        {
            1 => filtered.OrderByDescending(c => c.Value<int?>("RepeatOffenderScore") ?? 0).ToList(),
            2 => filtered.OrderByDescending(c => c.Value<int?>("ShortYear") ?? 0).ToList(),
            _ => filtered.OrderByDescending(c => c["LastUpdatedUtc"]?.ToString() ?? "").ToList()
        };

        foreach (var c in filtered)
            CaseStack.Children.Add(BuildCaseExpander(c));

        if (filtered.Count == 0)
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
        var dateBits = RowHeaderDateSummary(c, status);

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
            body.Children.Add(NoteBorder(
                "Prior disposition (reconstructed record for supervision backstory). Charges align with Person Search arrest history where applicable."));

        body.Children.Add(SectionTitle("Case profile"));
        body.Children.Add(BuildCaseProfileGrid(c, synth));

        body.Children.Add(SectionTitle("Case timeline"));
        body.Children.Add(BuildTimelinePanel(c));

        body.Children.Add(SectionTitle("Parties & venue"));
        body.Children.Add(BuildPartiesPanel(ped, c, synth));

        body.Children.Add(SectionTitle("Charges"));
        var charges = c["Charges"] as JArray;
        if (charges == null || charges.Count == 0)
            body.Children.Add(Muted("No charges on file."));
        else
        {
            foreach (var ch in charges.OfType<JObject>())
                body.Children.Add(ChargeCard(ch, status));
        }

        var concluded = status == 1 || status == 2;
        if (concluded)
        {
            var totalFine = 0;
            var totalTime = 0;
            var life = 0;
            foreach (var ch in charges?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                NativeCourtFormatting.AccumulateChargeTotals(ch, status, ref totalFine, ref totalTime, ref life);

            body.Children.Add(SectionTitle("Sentence totals"));
            var sg = KvGrid(new (string, string)[]
            {
                ("Total fine", NativeCourtFormatting.FormatCurrency(totalFine)),
                ("Total incarceration", NativeCourtFormatting.FormatTotalTime(totalTime, life))
            });
            body.Children.Add(sg);
        }

        body.Children.Add(SectionTitle("Scoring & evidence"));
        body.Children.Add(BuildScoringPanel(c));

        body.Children.Add(SectionTitle("Evidence breakdown"));
        body.Children.Add(BuildEvidenceExpander(c));

        body.Children.Add(SectionTitle("Trial model"));
        body.Children.Add(BuildTrialModelPanel(c, status, concluded));

        body.Children.Add(SectionTitle("Disposition"));
        body.Children.Add(BuildDispositionPanel(c, synth, status));

        if (status == 1 && c["LicenseRevocations"] is JArray rev && rev.Count > 0)
        {
            body.Children.Add(SectionTitle("License revocations"));
            body.Children.Add(BuildRevocationsList(rev));
        }

        body.Children.Add(SectionTitle("Attached reports"));
        body.Children.Add(BuildAttachedReportsSection(c, synth, status));

        if (status == 0 && !synth)
            body.Children.Add(BuildActionsPanel(c));

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

    static string RowHeaderDateSummary(JObject c, int status)
    {
        var hearing = c["HearingDateUtc"];
        var resolve = c["ResolveAtUtc"];
        var last = c["LastUpdatedUtc"];
        JToken? pick;
        if (status == 0 && (resolve != null && resolve.Type != JTokenType.Null || hearing != null && hearing.Type != JTokenType.Null))
            pick = resolve ?? hearing;
        else
            pick = hearing ?? resolve ?? last;
        return NativeMdtFormat.IsoDate(pick);
    }

    Grid BuildCaseProfileGrid(JObject c, bool synth)
    {
        var rows = new List<(string, string)>
        {
            ("Case number", c["Number"]?.ToString() ?? "—"),
            ("Short year", (c.Value<int?>("ShortYear") ?? 0).ToString()),
            ("Status", NativeMdtFormat.CourtStatus(c.Value<int?>("Status") ?? 0)),
            ("Plea", c["Plea"]?.ToString() ?? "—"),
            ("Public defender", NativeMdtFormat.YesNo(c["HasPublicDefender"])),
            ("Judge", c["JudgeName"]?.ToString() ?? "—"),
            ("Prosecutor", c["ProsecutorName"]?.ToString() ?? "—"),
            ("Defense attorney", c["DefenseAttorneyName"]?.ToString() ?? "—"),
            ("Priors on file", PriorSummary(c)),
            ("Prior convictions", (c.Value<int?>("PriorConvictionCount") ?? 0).ToString())
        };
        if (!string.IsNullOrWhiteSpace(c["OfficerTestimonySummary"]?.ToString()))
            rows.Add(("Officer testimony (summary)", c["OfficerTestimonySummary"]!.ToString()));
        if (synth && !string.IsNullOrWhiteSpace(c["SupervisionRecordHint"]?.ToString()))
            rows.Add(("Supervision hint", c["SupervisionRecordHint"]!.ToString()));
        return KvGrid(rows.ToArray());
    }

    StackPanel BuildTimelinePanel(JObject c)
    {
        var p = new StackPanel();
        void AddRow(string label, JToken? tok)
        {
            if (tok == null || tok.Type == JTokenType.Null) return;
            var s = NativeMdtFormat.IsoDate(tok);
            if (s == "—") return;
            p.Children.Add(KvGrid(new[] { (label, s) }));
        }

        AddRow("Hearing date", c["HearingDateUtc"]);
        AddRow("Created", c["CreatedAtUtc"]);
        AddRow("Last updated", c["LastUpdatedUtc"]);
        AddRow("Court / resolve date", c["ResolveAtUtc"]);
        if (p.Children.Count == 0)
            p.Children.Add(Muted("No timeline timestamps on file."));
        return p;
    }

    StackPanel BuildPartiesPanel(string ped, JObject c, bool synth)
    {
        var p = new StackPanel();
        p.Children.Add(FieldLabelRow("Defendant"));
        p.Children.Add(DefendantLink(ped));
        p.Children.Add(FieldLabelRow("Primary report"));
        p.Children.Add(PrimaryReportBlock(c, synth));
        return p;
    }

    UIElement PrimaryReportBlock(JObject c, bool synth)
    {
        var reportId = c["ReportId"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(reportId))
            return Muted("—");

        if (synth)
        {
            var sp = new StackPanel();
            sp.Children.Add(Muted(reportId));
            sp.Children.Add(Muted(
                "No patrol arrest report is linked. This file was generated so supervision status matches charge history on file."));
            return sp;
        }

        _reportSummariesById.TryGetValue(reportId, out var sum);
        var typeKey = sum?["type"]?.ToString();
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        var h = new Hyperlink(new Run(reportId)) { Foreground = R("CadAccent") };
        h.Click += (_, _) => MdtShellEvents.RequestNavigateToReport(reportId, typeKey);
        tb.Inlines.Add(h);
        var sub = sum?["typeLabel"]?.ToString();
        if (!string.IsNullOrWhiteSpace(sub))
            tb.Inlines.Add(new Run($"  ({sub})") { Foreground = R("CadMuted") });
        tb.Inlines.Add(new Run("  ") { Foreground = R("CadMuted") });
        var open = new Hyperlink(new Run("Open in Reports")) { Foreground = R("CadOrange") };
        open.Click += (_, _) => MdtShellEvents.RequestNavigateToReport(reportId, typeKey);
        tb.Inlines.Add(open);
        return tb;
    }

    UIElement DefendantLink(string name)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        if (string.IsNullOrWhiteSpace(name) || name == "—")
        {
            tb.Text = "—";
            tb.Foreground = R("CadText");
            return tb;
        }

        var h = new Hyperlink(new Run(name)) { Foreground = R("CadAccent") };
        h.Click += (_, _) => MdtShellEvents.RequestNavigateToPersonSearch(name);
        tb.Inlines.Add(h);
        tb.Inlines.Add(new Run("  ") { Foreground = R("CadMuted") });
        var go = new Hyperlink(new Run("Search in Person")) { Foreground = R("CadOrange") };
        go.Click += (_, _) => MdtShellEvents.RequestNavigateToPersonSearch(name);
        tb.Inlines.Add(go);
        return tb;
    }

    static string CourtNameLine(JObject c)
    {
        var name = c["CourtName"]?.ToString();
        if (string.IsNullOrWhiteSpace(name)) return "—";
        var ty = c["CourtType"]?.ToString();
        return string.IsNullOrWhiteSpace(ty) ? name : $"{name} ({ty})";
    }

    Expander BuildEvidenceExpander(JObject c)
    {
        var ex = new Expander
        {
            Header = "View evidence breakdown (exhibits & charge filing)",
            Foreground = R("CadText"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        var content = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        var hasAny = c.Value<bool?>("EvidenceHadWeapon") == true
                     || c.Value<bool?>("EvidenceWasWanted") == true
                     || c.Value<bool?>("EvidenceAssaultedPed") == true
                     || c.Value<bool?>("EvidenceDamagedVehicle") == true
                     || c.Value<bool?>("EvidenceResisted") == true
                     || c.Value<bool?>("EvidenceHadDrugs") == true
                     || c.Value<bool?>("EvidenceUseOfForce") == true
                     || c.Value<bool?>("EvidenceWasDrunk") == true
                     || c.Value<bool?>("EvidenceWasFleeing") == true
                     || c.Value<bool?>("EvidenceViolatedSupervision") == true
                     || c.Value<bool?>("EvidenceWasPatDown") == true
                     || c.Value<bool?>("EvidenceIllegalWeapon") == true;

        content.Children.Add(new TextBlock
        {
            Text = hasAny
                ? "In-game evidence was captured for this case."
                : "No in-game evidence captured, or hooks did not fire for this ped.",
            Foreground = R("CadMuted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 12
        });

        var ros = c.Value<int?>("RepeatOffenderScore") ?? 0;
        content.Children.Add(EvidenceRow($"Repeat offender score: {ros}", ros > 0));

        var mult = c.Value<float?>("SentenceMultiplier") ?? 1f;
        content.Children.Add(EvidenceRow($"Sentence multiplier: ×{mult:0.00}", mult > 1f));

        static string JoinBreakdown(JObject caseObj, string arrayKey)
        {
            if (caseObj[arrayKey] is not JArray a || a.Count == 0) return "";
            var parts = a.Select(x => x?.ToString()?.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            return parts.Count == 0 ? "" : $" ({string.Join(", ", parts)})";
        }

        void AddExhibit(string label, string key, bool always = false)
        {
            var on = always || (key != "_arrest" && c.Value<bool?>(key) == true);
            if (!on) return;
            var text = key == "_arrest"
                ? $"Exhibit — Arrest report: {c["ReportId"]?.ToString() ?? "—"}"
                : $"Exhibit — {label}: YES";
            content.Children.Add(EvidenceRow(text, true));
        }

        AddExhibit("Firearm recovered" + JoinBreakdown(c, "EvidenceFirearmTypesBreakdown"), "EvidenceHadWeapon");
        AddExhibit("Drugs" + JoinBreakdown(c, "EvidenceDrugTypesBreakdown"), "EvidenceHadDrugs");
        AddExhibit("", "_arrest", always: true);
        AddExhibit("Active warrant documentation", "EvidenceWasWanted");
        AddExhibit("Assault evidence", "EvidenceAssaultedPed");
        AddExhibit("Vehicle/property damage", "EvidenceDamagedVehicle");
        AddExhibit("Resistance", "EvidenceResisted");
        AddExhibit("Use of force documentation", "EvidenceUseOfForce");
        AddExhibit("Intoxication", "EvidenceWasDrunk");
        AddExhibit("Fleeing", "EvidenceWasFleeing");
        AddExhibit("Supervision violation", "EvidenceViolatedSupervision");
        AddExhibit("Pat-down / search", "EvidenceWasPatDown");
        AddExhibit("Illegal weapon", "EvidenceIllegalWeapon");

        if (c["Charges"] is JArray chg && chg.Count > 0)
        {
            foreach (var t in chg.OfType<JObject>())
            {
                var nm = t["Name"]?.ToString() ?? "—";
                var arrest = t.Value<bool?>("IsArrestable");
                var tag = arrest == true ? "ARRESTABLE" : arrest == false ? "CIVIL" : "—";
                content.Children.Add(EvidenceRow($"{nm}: {tag}", false));
            }
        }
        else
            content.Children.Add(EvidenceRow("No charges filed", false));

        ex.Content = content;
        return ex;
    }

    Border EvidenceRow(string text, bool emphasize)
    {
        return new Border
        {
            BorderBrush = R("CadAccentDim"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 4, 0, 4),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = emphasize ? R("CadText") : R("CadMuted"),
                FontSize = 12
            }
        };
    }

    Grid BuildTrialModelPanel(JObject c, int status, bool concluded)
    {
        var rows = new List<(string, string)>
        {
            ("Prosecution strength", $"{(c.Value<float?>("ProsecutionStrength") ?? 0f):0.0}"),
            ("Defense strength", $"{(c.Value<float?>("DefenseStrength") ?? 0f):0.0}"),
            ("Conviction probability", $"{c.Value<int?>("ConvictionChance") ?? 0}%"),
            ("Docket pressure", $"{((c.Value<float?>("DocketPressure") ?? 0f) * 100f):0}%"),
            ("District policy adjustment", $"{((c.Value<float?>("PolicyAdjustment") ?? 0f) * 100f):0.0}%")
        };
        if (concluded)
        {
            var jury = c.Value<bool?>("IsJuryTrial") == true
                ? $"{c.Value<int?>("JuryVotesForConviction") ?? 0}–{c.Value<int?>("JuryVotesForAcquittal") ?? 0} / {c.Value<int?>("JurySize") ?? 0}"
                : "Bench trial";
            rows.Add(("Jury", jury));
        }
        else
        {
            rows.Add(("Jury", TrialSummary(c)));
        }

        return KvGrid(rows.ToArray());
    }

    StackPanel BuildDispositionPanel(JObject c, bool synth, int status)
    {
        var sp = new StackPanel();
        if (status == 0 && !synth)
        {
            sp.Children.Add(Muted("Outcome notes and plea are edited in Case actions at the bottom of this case."));
        }
        else
        {
            sp.Children.Add(FieldLabelRow("Outcome notes"));
            sp.Children.Add(ReadOnlyMultiline(c["OutcomeNotes"]?.ToString() ?? ""));
        }

        if (status != 0)
        {
            sp.Children.Add(FieldLabelRow("Verdict & outcome reasoning"));
            sp.Children.Add(ReadOnlyMultiline(c["OutcomeReasoning"]?.ToString() ?? ""));
            if (status == 1 && !string.IsNullOrWhiteSpace(c["SentenceReasoning"]?.ToString()))
            {
                sp.Children.Add(FieldLabelRow("Sentencing rationale"));
                sp.Children.Add(ReadOnlyMultiline(c["SentenceReasoning"]!.ToString()!));
            }
        }

        return sp;
    }

    TextBox ReadOnlyMultiline(string text) => new()
    {
        Text = text,
        IsReadOnly = true,
        AcceptsReturn = true,
        MinHeight = 72,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Style = (Style)FindResource("CadTextBoxCadField"),
        Margin = new Thickness(0, 4, 0, 8)
    };

    StackPanel BuildRevocationsList(JArray rev)
    {
        var sp = new StackPanel();
        foreach (var t in rev)
        {
            var s = t?.ToString();
            if (string.IsNullOrWhiteSpace(s)) continue;
            sp.Children.Add(new TextBlock
            {
                Text = "• " + s,
                TextWrapping = TextWrapping.Wrap,
                Foreground = R("CadText"),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        return sp;
    }

    UIElement BuildAttachedReportsSection(JObject c, bool synth, int status)
    {
        var attached = c["AttachedReportIds"] as JArray ?? new JArray();
        var pending = status == 0;
        var resolveAtOk = true;
        if (pending && c["ResolveAtUtc"]?.ToString() is { } ru && NativeMdtFormat.TryParseMdtDateTime(ru, out var resolveAt))
            resolveAtOk = DateTime.UtcNow < resolveAt.ToUniversalTime();

        if (attached.Count == 0 && !pending)
            return Muted("No attached evidence reports.");

        var sp = new StackPanel();
        if (pending)
            sp.Children.Add(Muted(
                "Attached reports count as evidence. Relevant ones carry full weight; others still count but carry less weight."));
        if (attached.Count == 0)
        {
            sp.Children.Add(Muted("No report IDs attached yet."));
        }
        else
        {
            foreach (var t in attached)
            {
                var rid = t?.ToString()?.Trim();
                if (string.IsNullOrEmpty(rid)) continue;
                sp.Children.Add(AttachedReportRow(c, rid, pending && resolveAtOk, status));
            }
        }

        if (synth)
            return sp;

        return sp;
    }

    Border AttachedReportRow(JObject caseObj, string reportId, bool canDetach, int caseStatus)
    {
        _reportSummariesById.TryGetValue(reportId, out var sum);
        var typeKey = sum?["type"]?.ToString();
        var typeLabel = sum?["typeLabel"]?.ToString() ?? "—";
        var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 4) };

        var left = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = R("CadText") };
        var hl = new Hyperlink(new Run(reportId)) { Foreground = R("CadAccent") };
        hl.Click += (_, _) => MdtShellEvents.RequestNavigateToReport(reportId, typeKey);
        left.Inlines.Add(hl);
        if (caseStatus != 0 || !string.IsNullOrWhiteSpace(typeLabel))
            left.Inlines.Add(new Run($"  ({typeLabel})") { Foreground = R("CadMuted") });
        DockPanel.SetDock(left, Dock.Left);
        dock.Children.Add(left);

        if (canDetach)
        {
            var detach = new Button
            {
                Content = "DETACH",
                Style = (Style)FindResource("CadRailOutlineButton"),
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 2, 10, 2)
            };
            var num = caseObj["Number"]?.ToString() ?? "";
            detach.Click += async (_, _) =>
            {
                var http = _connection?.Http;
                if (http == null) return;
                detach.IsEnabled = false;
                try
                {
                    var payload = new JObject { ["courtCaseNumber"] = num, ["reportId"] = reportId };
                    var (code, _) = await http.PostActionAsync("detachReportFromCourtCase", payload.ToString(Formatting.None))
                        .ConfigureAwait(false);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        detach.IsEnabled = true;
                        if (code == HttpStatusCode.OK)
                            MessageBox.Show("Report detached.", "Court", MessageBoxButton.OK, MessageBoxImage.Information);
                        else
                            MessageBox.Show("Detach failed.", "Court", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    if (code == HttpStatusCode.OK)
                        await LoadCoreAsync();
                }
                catch
                {
                    await Dispatcher.InvokeAsync(() => detach.IsEnabled = true);
                }
            };
            DockPanel.SetDock(detach, Dock.Right);
            dock.Children.Add(detach);
        }
        else if (caseStatus != 0)
        {
            var view = new Button
            {
                Content = "OPEN",
                Style = (Style)FindResource("CadRailOutlineButton"),
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 2, 10, 2)
            };
            view.Click += (_, _) => MdtShellEvents.RequestNavigateToReport(reportId, typeKey);
            DockPanel.SetDock(view, Dock.Right);
            dock.Children.Add(view);
        }

        return new Border
        {
            BorderBrush = R("CadAccentDim"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Background = R("CadElevated"),
            Child = dock
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
        var line = NativeCourtFormatting.ChargeDetailLine(ch, caseStatus, out _);
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
            Text = line,
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

    Border BuildActionsPanel(JObject c)
    {
        var num = c["Number"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(num))
            return NoteBorder("Missing case number; cannot post updates.");

        var sp = new StackPanel();
        sp.Children.Add(SectionTitle("Case actions"));

        sp.Children.Add(FieldLabelRow("Plea"));
        var pleaOptions = _courtPleaOptions ?? DefaultCourtPleaOptions;
        var pleaCb = new ComboBox
        {
            Style = (Style)FindResource("CadComboBox"),
            Margin = new Thickness(0, 4, 0, 8),
            ItemsSource = pleaOptions
        };
        var pleaCurrent = (c["Plea"]?.ToString() ?? "").Trim();
        var pleaIdx = -1;
        for (var i = 0; i < pleaOptions.Length; i++)
        {
            if (string.Equals(pleaOptions[i], pleaCurrent, StringComparison.OrdinalIgnoreCase))
            {
                pleaIdx = i;
                break;
            }
        }
        pleaCb.SelectedIndex = pleaIdx >= 0 ? pleaIdx : 0;
        sp.Children.Add(pleaCb);

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

        sp.Children.Add(FieldLabelRow("Evidence report ID (attach / detach)"));
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
            Content = "SAVE PLEA & NOTES",
            Style = (Style)FindResource("CadPrimaryActionButton"),
            Margin = new Thickness(0, 0, 8, 8)
        };
        var dismissBtn = new Button
        {
            Content = "DISMISS CASE",
            Style = (Style)FindResource("CadRailOutlineButton"),
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

        JObject BuildStatusPayload(int newStatus) => new JObject
        {
            ["Number"] = num,
            ["Status"] = newStatus,
            ["Plea"] = pleaCb.SelectedItem as string ?? pleaOptions[0],
            ["OutcomeNotes"] = notesTb.Text ?? "",
            ["OutcomeReasoning"] = c["OutcomeReasoning"]?.ToString() ?? "",
            ["IsJuryTrial"] = c["IsJuryTrial"],
            ["JurySize"] = c["JurySize"],
            ["JuryVotesForConviction"] = c["JuryVotesForConviction"],
            ["JuryVotesForAcquittal"] = c["JuryVotesForAcquittal"],
            ["HasPublicDefender"] = c["HasPublicDefender"]
        };

        applyBtn.Click += async (_, _) =>
        {
            var http = _connection?.Http;
            if (http == null) return;
            var payload = BuildStatusPayload(0);
            await MdtBusyUi.RunAsync(DocketBusy, "COURT", "Recording case outcome…", async () =>
            {
                var (code, resp) = await http.PostActionAsync("updateCourtCaseStatus", payload.ToString(Formatting.None)).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (code == HttpStatusCode.OK)
                    {
                        CadCourtCaseSound.TryPlay();
                        MessageBox.Show("Case updated.", "Court", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                        MessageBox.Show($"Update failed ({(int)code}).\n\n{resp}", "Court", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                if (code == HttpStatusCode.OK)
                    await LoadCoreAsync();
            }, minimumVisibleMs: 540);
        };

        dismissBtn.Click += async (_, _) =>
        {
            var http = _connection?.Http;
            if (http == null) return;
            var payload = BuildStatusPayload(3);
            await MdtBusyUi.RunAsync(DocketBusy, "COURT", "Dismissing case…", async () =>
            {
                var (code, resp) = await http.PostActionAsync("updateCourtCaseStatus", payload.ToString(Formatting.None)).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (code == HttpStatusCode.OK)
                    {
                        CadCourtCaseSound.TryPlay();
                        MessageBox.Show("Case dismissed.", "Court", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                        MessageBox.Show($"Dismiss failed ({(int)code}).\n\n{resp}", "Court", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                if (code == HttpStatusCode.OK)
                    await LoadCoreAsync();
            }, minimumVisibleMs: 540);
        };

        forceBtn.Click += async (_, _) =>
        {
            var http = _connection?.Http;
            if (http == null) return;
            var pleaValForce = pleaCb.SelectedItem as string ?? pleaOptions[0];
            var payload = new JObject
            {
                ["Number"] = num,
                ["Plea"] = pleaValForce,
                ["OutcomeNotes"] = notesTb.Text ?? ""
            };
            await MdtBusyUi.RunAsync(DocketBusy, "COURT", "Running automated resolution…", async () =>
            {
                var (code, resp) = await http.PostActionAsync("forceResolveCourtCase", payload.ToString(Formatting.None)).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (code == HttpStatusCode.OK)
                        MessageBox.Show("Case resolved (simulation).", "Court", MessageBoxButton.OK, MessageBoxImage.Information);
                    else
                        MessageBox.Show($"Auto-resolve failed ({(int)code}).\n\n{resp}", "Court", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                if (code == HttpStatusCode.OK)
                    await LoadCoreAsync();
            }, minimumVisibleMs: 540);
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
            await MdtBusyUi.RunAsync(DocketBusy, "COURT", "Linking evidence report…", async () =>
            {
                var (code, resp) = await http.PostActionAsync("attachReportToCourtCase", payload.ToString(Formatting.None)).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (code == HttpStatusCode.OK)
                        MessageBox.Show("Report attached.", "Court", MessageBoxButton.OK, MessageBoxImage.Information);
                    else
                        MessageBox.Show($"Attach failed ({(int)code}).\n\n{resp}", "Court", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                if (code == HttpStatusCode.OK)
                    await LoadCoreAsync();
            }, minimumVisibleMs: 520);
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
            await MdtBusyUi.RunAsync(DocketBusy, "COURT", "Unlinking evidence report…", async () =>
            {
                var (code, resp) = await http.PostActionAsync("detachReportFromCourtCase", payload.ToString(Formatting.None)).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (code == HttpStatusCode.OK)
                        MessageBox.Show("Report detached.", "Court", MessageBoxButton.OK, MessageBoxImage.Information);
                    else
                        MessageBox.Show($"Detach failed ({(int)code}).\n\n{resp}", "Court", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                if (code == HttpStatusCode.OK)
                    await LoadCoreAsync();
            }, minimumVisibleMs: 520);
        };

        btnRow.Children.Add(applyBtn);
        btnRow.Children.Add(dismissBtn);
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

    UIElement BuildScoringPanel(JObject c)
    {
        var ev = Math.Max(0, c.Value<int?>("EvidenceScore") ?? 0);
        var band = ev < 35 ? "Low" : ev < 60 ? "Medium" : "Strong";
        var rows = new (string, string)[]
        {
            ("Repeat offender score", (c.Value<int?>("RepeatOffenderScore") ?? 0).ToString()),
            ("Sentence multiplier", $"{(c.Value<float?>("SentenceMultiplier") ?? 1f):0.00}×"),
            ("Severity score", (c.Value<int?>("SeverityScore") ?? 0).ToString()),
            ("Evidence score", $"{ev} ({band})"),
            ("Court district", string.IsNullOrWhiteSpace(c["CourtDistrict"]?.ToString()) ? "—" : c["CourtDistrict"]!.ToString()!),
            ("Court", CourtNameLine(c))
        };
        var g = KvGrid(rows);
        if (band != "Low")
            return g;

        var sp = new StackPanel();
        sp.Children.Add(g);
        sp.Children.Add(new TextBlock
        {
            Text = "Limited physical evidence – case may rely on officer testimony.",
            Foreground = R("CadMuted"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0)
        });
        return sp;
    }

    Grid KvGrid((string label, string value)[] rows)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 220 });
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
