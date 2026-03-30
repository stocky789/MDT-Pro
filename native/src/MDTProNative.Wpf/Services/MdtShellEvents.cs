namespace MDTProNative.Wpf.Services;

/// <summary>Cross-view hooks into the main MDT shell (taskbar, CAD message log).</summary>
public static class MdtShellEvents
{
    public static event Action? OfficerStripRefreshRequested;

    /// <summary>Raised when a view wants a line in the shell CAD log (any thread).</summary>
    public static event Action<string>? CadMessageLogged;

    public static void RequestOfficerStripRefresh() => OfficerStripRefreshRequested?.Invoke();

    public static void LogCad(string line) => CadMessageLogged?.Invoke(line);
}
