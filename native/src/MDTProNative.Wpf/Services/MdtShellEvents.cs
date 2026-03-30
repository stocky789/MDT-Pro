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
}
