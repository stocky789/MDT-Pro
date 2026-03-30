using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Windows;

public partial class BackupMenuWindow : Window
{
    readonly MdtConnectionManager _connection;
    bool _ultimateBackup;
    ComboBox? _trafficUnitCombo;
    JObject? _integration;

    readonly StackPanel _codeRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
    readonly RadioButton _code1 = new() { Content = "Code 1", Margin = new Thickness(0, 0, 12, 0), GroupName = "BkCode" };
    readonly RadioButton _code2 = new() { Content = "Code 2", Margin = new Thickness(0, 0, 12, 0), GroupName = "BkCode", IsChecked = true };
    readonly RadioButton _code3 = new() { Content = "Code 3", GroupName = "BkCode" };

    /// <summary>Policing Redefined <c>EBackupUnit</c> names for traffic-stop backup (see plugin <c>PostAPIResponse.requestBackup</c>).</summary>
    static readonly (string Api, string Label)[] PrTrafficUnits =
    [
        ("LocalPatrol", "Local patrol"),
        ("StatePatrol", "State patrol"),
        ("LocalSWAT", "Local SWAT"),
        ("NooseSWAT", "NOOSE SWAT"),
        ("LocalK9Patrol", "Local K9"),
        ("StateK9Patrol", "State K9"),
        ("Ambulance", "Ambulance / EMS"),
        ("FireDepartment", "Fire department"),
    ];

    /// <summary>Ultimate Backup traffic-stop API uses whether the unit name contains <c>State</c> to pick state vs local patrol.</summary>
    static readonly (string Api, string Label)[] UbTrafficUnits =
    [
        ("LocalPatrol", "Local patrol"),
        ("StatePatrol", "State patrol"),
    ];

    public BackupMenuWindow(MdtConnectionManager connection)
    {
        InitializeComponent();
        _connection = connection;
        _codeRow.Children.Add(_code1);
        _codeRow.Children.Add(_code2);
        _codeRow.Children.Add(_code3);
        Loaded += async (_, _) => await BuildAsync();
    }

    int SelectedCode
    {
        get
        {
            if (_code3.IsChecked == true) return 3;
            if (_code1.IsChecked == true) return 1;
            return 2;
        }
    }

    async Task BuildAsync()
    {
        RootStack.Children.Clear();
        _trafficUnitCombo = null;

        JObject? lang = null;
        try
        {
            _integration = await _connection.Http!.GetIntegrationJsonAsync() as JObject;
        }
        catch
        {
            _integration = null;
        }

        try
        {
            lang = await _connection.Http!.GetLanguageJsonAsync();
        }
        catch
        {
            /* defaults below */
        }

        _ultimateBackup = string.Equals(_integration?["backupProvider"]?.ToString(), "UltimateBackup", StringComparison.OrdinalIgnoreCase);

        var qa = lang?["quickActions"] as JObject;
        var langUbNote = qa?["backupUltimateBackupNote"]?.ToString();
        var codeLabelUb = qa?["backupResponseCodeLabelUb"]?.ToString();
        if (string.IsNullOrWhiteSpace(codeLabelUb))
            codeLabelUb = "Patrol code";

        ProviderNote.Text = BuildIntegrationNote(langUbNote);
        ProviderNote.Visibility = Visibility.Visible;

        if (_ultimateBackup)
        {
            _code1.Visibility = Visibility.Collapsed;
        }
        else
        {
            _code1.Visibility = Visibility.Visible;
        }

        AddSectionTitle(_ultimateBackup ? codeLabelUb! : "Response code");
        RootStack.Children.Add(_codeRow);

        AddGroup("Patrol & tactical",
            ("localPatrol", "Local Patrol", false),
            ("statePatrol", "State Patrol", false),
            ("localSwat", "Local SWAT", false),
            ("nooseSwat", "NOOSE SWAT", false),
            ("localK9", "Local K9", false),
            ("stateK9", "State K9", false));

        AddGroup("Fire & medical",
            ("ambulance", "Ambulance", false),
            ("fire", "Fire Department", false),
            ("coroner", "Coroner", true),
            ("animalControl", "Animal Control", true));

        AddTrafficStopSection();

        AddGroup("Traffic & support (other)",
            ("transport", "Police Transport", true),
            ("tow", "Tow Service", true),
            ("group", "Group Backup", false));

        AddGroup("Pursuit only",
            ("airLocal", "Air Backup (Local)", false),
            ("airNoose", "Air Backup (NOOSE)", false),
            ("spikeStrips", "Spike Strips", false));

        AddGroup("Other",
            ("felonyStop", "Initiate Felony Stop", false),
            ("dismiss", "Dismiss All Backup", false));

        RootStack.Children.Add(new TextBlock
        {
            Text = "Panic is on the main toolbar.",
            Foreground = (System.Windows.Media.Brush)FindResource("CadMuted"),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11
        });
    }

    string BuildIntegrationNote(string? langUltimateBackupNote)
    {
        var j = _integration;
        var backup = j?["backupProvider"]?.ToString() ?? "none";
        if (string.Equals(backup, "none", StringComparison.OrdinalIgnoreCase))
            backup = "none (install Policing Redefined or Ultimate Backup)";

        var pr = j?.Value<bool>("policingRedefinedLoaded") == true;
        var stp = j?.Value<bool>("stopThePedLoaded") == true;
        var stopActive = j?["stopEventsProvider"]?.ToString() ?? "none";
        var stopCfg = j?["integrationStopEvents"]?.ToString() ?? "Auto";
        var backupCfg = j?["integrationBackupProvider"]?.ToString() ?? "Auto";

        var core =
            "Backup provider: " + backup + " (config: " + backupCfg + ").\n" +
            "Mods: Policing Redefined " + (pr ? "loaded" : "not loaded") + "; Stop The Ped " + (stp ? "loaded" : "not loaded") + ".\n" +
            "Traffic stops: active handler is " + stopActive + " (MDT config: " + stopCfg + "). Use traffic-stop backup only while you are in an active stop.\n" +
            (_ultimateBackup
                ? "Ultimate Backup: Code 1 hidden; coroner, animal control, transport, and tow are hidden (Policing Redefined API). Pick local vs state patrol for traffic-stop backup."
                : "Policing Redefined (or no UB): pick the backup unit for traffic-stop requests; other actions use the code above.");

        if (_ultimateBackup && !string.IsNullOrWhiteSpace(langUltimateBackupNote))
            return langUltimateBackupNote.Trim() + "\n\n" + core;

        return core;
    }

    void AddTrafficStopSection()
    {
        AddSectionTitle("Traffic stop backup");
        RootStack.Children.Add(new TextBlock
        {
            Text = "Calls the same /post/requestBackup action as the browser MDT. Requires an active traffic stop (Stop The Ped or Policing Redefined per your integration settings).",
            Foreground = (System.Windows.Media.Brush)FindResource("CadMuted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var trafficLabel = _ultimateBackup
            ? "LOCAL VS STATE PATROL (ULTIMATE BACKUP)"
            : "BACKUP UNIT (POLICING REDEFINED API)";
        RootStack.Children.Add(new TextBlock
        {
            Text = trafficLabel,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Foreground = (System.Windows.Media.Brush)FindResource("CadMuted"),
            Margin = new Thickness(0, 4, 0, 4)
        });
        _trafficUnitCombo = new ComboBox
        {
            Style = (Style)FindResource("CadComboBox"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var units = _ultimateBackup ? UbTrafficUnits : PrTrafficUnits;
        foreach (var (api, label) in units)
            _trafficUnitCombo.Items.Add(new TrafficUnitItem(api, label));
        _trafficUnitCombo.DisplayMemberPath = nameof(TrafficUnitItem.Label);
        _trafficUnitCombo.SelectedIndex = 0;
        RootStack.Children.Add(_trafficUnitCombo);

        var btn = new Button
        {
            Content = "REQUEST TRAFFIC STOP BACKUP",
            Style = (Style)FindResource("CadPrimaryActionButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 10)
        };
        btn.Click += async (_, _) => await RequestTrafficStopBackupAsync();
        RootStack.Children.Add(btn);
    }

    async Task RequestTrafficStopBackupAsync()
    {
        var unit = (_trafficUnitCombo?.SelectedItem as TrafficUnitItem)?.Api ?? "LocalPatrol";
        var body = JsonConvert.SerializeObject(new { action = "trafficStop", responseCode = SelectedCode, unit });
        await SendBackupBodyAsync(body);
    }

    void AddSectionTitle(string t)
    {
        RootStack.Children.Add(new TextBlock
        {
            Text = t.ToUpperInvariant(),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Foreground = (System.Windows.Media.Brush)FindResource("CadMuted"),
            Margin = new Thickness(0, 10, 0, 6)
        });
    }

    void AddGroup(string title, params (string action, string label, bool prOnly)[] items)
    {
        if (!items.Any(i => !(i.prOnly && _ultimateBackup))) return;
        AddSectionTitle(title);
        foreach (var (action, label, prOnly) in items)
        {
            if (prOnly && _ultimateBackup) continue;
            var btn = new Button
            {
                Content = label,
                Style = (Style)FindResource("CadButton"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 6),
                Tag = action
            };
            btn.Click += async (_, _) => await RequestBackup((string)btn.Tag!);
            RootStack.Children.Add(btn);
        }
    }

    async Task RequestBackup(string action)
    {
        var body = JsonConvert.SerializeObject(new { action, responseCode = SelectedCode });
        await SendBackupBodyAsync(body);
    }

    async Task SendBackupBodyAsync(string body)
    {
        try
        {
            var (status, text) = await _connection.Http!.PostActionAsync("requestBackup", body);
            var msg = text;
            try
            {
                var jo = JObject.Parse(text);
                if (jo.Value<bool?>("success") == true) msg = "Backup request sent.";
                else msg = jo["error"]?.ToString() ?? text;
            }
            catch { /* plain */ }

            if (status != System.Net.HttpStatusCode.OK)
                msg = $"HTTP {(int)status}: {msg}";
            MdtShellEvents.LogCad("Backup: " + msg);
        }
        catch (Exception ex) { MdtShellEvents.LogCad("Backup error: " + ex.Message); }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    sealed class TrafficUnitItem(string api, string label)
    {
        public string Api { get; } = api;
        public string Label { get; } = label;
    }
}
