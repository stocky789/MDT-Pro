namespace MDTProNative.Wpf.Services;

/// <summary>Cross-view hooks into the main MDT shell (taskbar, CAD message log).</summary>
public static class MdtShellEvents
{
    public static event Action? OfficerStripRefreshRequested;

    /// <summary>Raised when a view wants a line in the shell CAD log (any thread).</summary>
    public static event Action<string>? CadMessageLogged;

    /// <summary>Switch to Person search and load the named subject (UI thread).</summary>
    public static event Action<string>? NavigateToPersonSearchRequested;

    /// <summary>Switch to Reports and open the report with optional API type key (e.g. <c>arrest</c>, <c>incident</c>).</summary>
    public static event Action<string, string?>? NavigateToReportRequested;

    /// <summary>Switch to Reports, create a new draft of <paramref name="reportTypeKey"/>, and prefill from person search.</summary>
    public static event Action<string, string, string?>? NavigateToNewReportFromPersonSearchRequested;

    public static void RequestOfficerStripRefresh() => OfficerStripRefreshRequested?.Invoke();

    public static void LogCad(string line) => CadMessageLogged?.Invoke(line);

    public static void RequestNavigateToPersonSearch(string? pedName)
    {
        var t = pedName?.Trim();
        if (string.IsNullOrEmpty(t)) return;
        NavigateToPersonSearchRequested?.Invoke(t);
    }

    public static void RequestNavigateToReport(string? reportId, string? reportTypeKey = null)
    {
        var id = reportId?.Trim();
        if (string.IsNullOrEmpty(id)) return;
        var tk = string.IsNullOrWhiteSpace(reportTypeKey) ? null : reportTypeKey.Trim();
        NavigateToReportRequested?.Invoke(id, tk);
    }

    public static void RequestNavigateToNewReportFromPersonSearch(string? reportTypeKey, string? pedName, string? vehicleLicensePlate = null)
    {
        var rk = reportTypeKey?.Trim() ?? "";
        var pn = pedName?.Trim() ?? "";
        if (rk.Length == 0 || pn.Length == 0) return;
        var plate = string.IsNullOrWhiteSpace(vehicleLicensePlate) ? null : vehicleLicensePlate.Trim();
        NavigateToNewReportFromPersonSearchRequested?.Invoke(rk, pn, plate);
    }
}
