using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

public partial class ShiftHistoryView : UserControl, IMdtBoundView
{
    MdtConnectionManager? _connection;
    List<ShiftRow> _shifts = [];

    public ShiftHistoryView()
    {
        InitializeComponent();
        ShiftList.DisplayMemberPath = nameof(ShiftRow.Display);
        RefreshShiftsBtn.Click += async (_, _) => await LoadShiftsAsync();
    }

    public void Bind(MdtConnectionManager? connection)
    {
        _connection = connection;
        ShiftList.ItemsSource = null;
        ShiftDetailText.Text = "";
        if (connection?.Http == null) return;
        _ = LoadShiftsAsync();
    }

    public void RequestReload()
    {
        if (_connection?.Http != null)
            _ = LoadShiftsAsync();
    }

    async Task LoadShiftsAsync()
    {
        var http = _connection?.Http;
        if (http == null) return;
        try
        {
            var tok = await http.GetDataJsonAsync("shiftHistory").ConfigureAwait(false);
            var rows = new List<ShiftRow>();
            if (tok is JArray arr)
            {
                foreach (var t in arr.OfType<JObject>())
                {
                    var start = ParseTime(t["startTime"]);
                    var end = ParseTime(t["endTime"]);
                    var reports = t["reports"] is JArray jr
                        ? jr.Select(x => x?.ToString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                        : [];
                    rows.Add(new ShiftRow(start, end, reports));
                }
            }

            rows.Sort((a, b) => b.SortKey.CompareTo(a.SortKey));

            await Dispatcher.InvokeAsync(() =>
            {
                _shifts = rows;
                ShiftList.ItemsSource = rows;
                ShiftDetailText.Text = rows.Count == 0 ? "No shift history on file." : "Select a shift.";
            });
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _shifts = [];
                ShiftList.ItemsSource = null;
                ShiftDetailText.Text = "Could not load shift history.";
            });
        }
    }

    static DateTime? ParseTime(JToken? t)
    {
        if (t == null || t.Type == JTokenType.Null) return null;
        var s = t.ToString();
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime();
        return null;
    }

    void ShiftList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShiftList.SelectedItem is not ShiftRow row)
        {
            ShiftDetailText.Text = _shifts.Count == 0 ? "" : "Select a shift.";
            return;
        }

        if (row.ReportIds.Count == 0)
        {
            ShiftDetailText.Text = "(No report IDs recorded for this shift.)";
            return;
        }

        ShiftDetailText.Text = string.Join(Environment.NewLine, row.ReportIds);
    }

    sealed class ShiftRow
    {
        public ShiftRow(DateTime? start, DateTime? end, List<string> reportIds)
        {
            Start = start;
            End = end;
            ReportIds = reportIds;
        }

        public DateTime? Start { get; }
        public DateTime? End { get; }
        public List<string> ReportIds { get; }

        public DateTime SortKey => End ?? Start ?? DateTime.MinValue;

        public string Display
        {
            get
            {
                var a = Start?.ToString("g", CultureInfo.CurrentCulture) ?? "—";
                var b = End?.ToString("g", CultureInfo.CurrentCulture) ?? "open";
                return $"{a}  →  {b}  ·  {ReportIds.Count} rpt";
            }
        }
    }
}
