using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace MDTProNative.Wpf.Windows;

/// <summary>Native layout for the same content as <c>narcoticsCheatsheet.js</c>.</summary>
public partial class NarcoticsCheatsheetWindow : Window
{
    public NarcoticsCheatsheetWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Build();
    }

    void Build()
    {
        BodyStack.Children.Clear();
        AddHeading("Drug schedules");
        BodyStack.Children.Add(MakeDataGrid(new[]
        {
            new[] { "Schedule", "Definition", "Examples" },
            new[] { "I", "No accepted medical use, high abuse potential", "Heroin, LSD, Cannabis*, Ecstasy/MDMA, Peyote, Psilocybin, DMT, Crack, illicit GHB, K2/Spice, bath salts" },
            new[] { "II", "High abuse potential, severe dependence risk", "Cocaine (salt), Meth, PCP, Fentanyl, Oxycodone, Hydrocodone, Adderall, Ritalin, Morphine, Methadone, Codeine" },
            new[] { "III", "Moderate abuse potential, accepted medical use", "Ketamine, Anabolic steroids, FDA GHB products, some codeine combos, Tylenol w/ codeine" },
            new[] { "IV", "Low abuse potential", "Xanax, Valium, Ativan, Tramadol, Ambien, Soma, Rohypnol" },
            new[] { "V", "Lowest — cough/antidiarrheal", "Lyrica, Lomotil, limited-codeine syrups" }
        }));
        BodyStack.Children.Add(Note("* Cannabis scheduling vs retail rules vary by jurisdiction; treat as controlled for illicit sale/possession per your RP server."));

        AddHeading("Quick: drug → schedule");
        BodyStack.Children.Add(MakeQuickScheduleGrid(
            "Schedule I: Heroin, LSD, Ecstasy, Peyote, Psilocybin, DMT, Cannabis (many schedules), Crack, illicit GHB, K2, bath salts",
            "Schedule II: Powder cocaine, Meth, PCP, Fentanyl, OxyContin, Vicodin, Adderall, Ritalin, Morphine, Codeine",
            "Schedule III: Ketamine, Steroids, some codeine combos",
            "Schedule IV: Xanax, Valium, Tramadol, Rohypnol, Soma"));

        AddHeading("Street names");
        BodyStack.Children.Add(MakeSlangGrid());

        AddHeading("Signs at a glance");
        BodyStack.Children.Add(MakeDataGrid(new[]
        {
            new[] { "Substance", "Pupils", "Behavior / signs" },
            new[] { "Stimulants (coke, meth)", "Dilated", "Hyperactivity, talkative, paranoia, bruxism, no sleep" },
            new[] { "Depressants (heroin, opioids)", "Pinpoint", "Drowsy, nodding, slurred speech, slow respiration" },
            new[] { "Cannabis", "Bloodshot / dilated", "Odor, slow reaction, dry mouth" },
            new[] { "Hallucinogens", "Dilated", "Altered perception, disorientation" },
            new[] { "Benzos", "Normal / dilated", "Slurred speech, drowsiness, stumbling" },
            new[] { "Fentanyl", "Pinpoint", "Extreme sedation, respiratory depression" }
        }));

        AddHeading("Sale vs personal use (general)");
        BodyStack.Children.Add(BulletList(
            "Marijuana: under ~28.5g often personal; larger amounts + packaging may support sale (jurisdiction-dependent in RP).",
            "Cocaine / heroin / meth: quantity, baggies, scales, cash, multiple doses → sale / trafficking indicators.",
            "Pills: large counts without prescription.",
            "Scales, baggies, divided portions, large cash → possession for sale."));

        AddHeading("Charge severity (quick)");
        BodyStack.Children.Add(MakeQuickScheduleGrid(
            "Possession — misdemeanor / felony",
            "For sale / transport / trafficking — felony",
            "Manufacturing meth — felony",
            "Grow operations — misd. or felony",
            "Paraphernalia / under influence — typically misdemeanor"));

        AddHeading("Paraphernalia");
        BodyStack.Children.Add(BulletList(
            "Smoking: pipes, bongs, papers, wraps",
            "Injection: needles, spoons, tourniquets, cotton",
            "Snorting: mirrors, razors, straws, bills",
            "General: scales, baggies, unlabeled bottles",
            "Meth: glass pipes, bulbs, lithium, pseudo packaging"));

        AddHeading("Fentanyl — stay safe");
        var alert = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("CadElevated"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("CadUrgent"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = BulletList(
                "Nitrile gloves if drugs may be present; mask if powder visible.",
                "Narcan for opioid OD — not for brief skin contact. If unwell: fresh air, wash, medical.",
                "Often cut into coke, meth, heroin — avoid sniffing or tasting powder.")
        };
        BodyStack.Children.Add(alert);

        AddHeading("Evidence");
        BodyStack.Children.Add(BulletList(
            "Bag, label (when / where / who / case), photograph qty & packaging.",
            "PER in MDT for seizures; log every handoff."));

        AddHeading("Search quick");
        BodyStack.Children.Add(BulletList(
            "PC: plain view, odor, consent, SIA",
            "Vehicle: inventory; some jurisdictions — odor",
            "Person: Terry pat → full search if cuffed",
            "Miranda: custodial interview only"));
    }

    void AddHeading(string text) =>
        BodyStack.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 8),
            Foreground = (System.Windows.Media.Brush)FindResource("CadAccent")
        });

    static TextBlock Note(string t) => new()
    {
        Text = t,
        TextWrapping = TextWrapping.Wrap,
        FontStyle = FontStyles.Italic,
        Margin = new Thickness(0, 0, 0, 12),
        FontSize = 12
    };

    DataGrid MakeDataGrid(string[][] rows)
    {
        var table = new DataTable();
        foreach (var c in rows[0])
            table.Columns.Add(c, typeof(string));
        for (var i = 1; i < rows.Length; i++)
        {
            var dr = table.NewRow();
            for (var j = 0; j < rows[i].Length && j < table.Columns.Count; j++)
                dr[j] = rows[i][j];
            table.Rows.Add(dr);
        }
        var dg = new DataGrid
        {
            ItemsSource = table.DefaultView,
            IsReadOnly = true,
            AutoGenerateColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MaxHeight = 320,
            Margin = new Thickness(0, 0, 0, 12),
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
        return dg;
    }

    UniformGrid MakeQuickScheduleGrid(params string[] cells)
    {
        var g = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var c in cells)
        {
            var b = new Border
            {
                BorderBrush = (System.Windows.Media.Brush)FindResource("CadBorder"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 6, 6),
                Child = new TextBlock { Text = c, TextWrapping = TextWrapping.Wrap, FontSize = 12 }
            };
            g.Children.Add(b);
        }
        return g;
    }

    UniformGrid MakeSlangGrid()
    {
        var pairs = new (string title, string body)[]
        {
            ("Heroin", "H, smack, horse, junk, black tar, boy"),
            ("Cocaine", "Coke, blow, snow, flake, crack, rock"),
            ("Meth", "Ice, crystal, glass, crank, Tina, shards"),
            ("Fentanyl", "Fetty, China girl, apache, blues, M30 fakes"),
            ("Weed", "Pot, bud, ganja, grass, reefer"),
            ("MDMA", "Molly, E, X, ecstasy, rolls"),
            ("LSD", "Acid, tabs, blotter"),
            ("PCP", "Angel dust, wet, sherm, fry"),
            ("Syrup", "Lean, drank, sizzurp"),
            ("Xanax", "Bars, z-bars, footballs"),
            ("GHB", "G, liquid G"),
            ("Ketamine", "K, Special K, vitamin K")
        };
        var g = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (title, body) in pairs)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 8, 8) };
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 12 });
            sp.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (System.Windows.Media.Brush)FindResource("CadMuted") });
            g.Children.Add(sp);
        }
        return g;
    }

    static StackPanel BulletList(params string[] items)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var item in items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = "• ", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(new TextBlock { Text = item, TextWrapping = TextWrapping.Wrap });
            p.Children.Add(row);
        }
        return p;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
