using System.IO;
using Newtonsoft.Json;

namespace MDTProNative.Wpf.Services;

/// <summary>Persisted native shell layout (splitter positions, CAD log).</summary>
public sealed class UiLayoutPreferences
{
    public double? MainActionsWidth { get; set; }
    public double? MainRelatedWidth { get; set; }
    public double? CadLogHeight { get; set; }
    public bool? CadLogCollapsed { get; set; }

    public double? PersonSidebarWidth { get; set; }
    public double? PersonLeftVerticalRatio { get; set; }

    public double? VehicleSidebarWidth { get; set; }
    public double? VehicleLeftVerticalRatio { get; set; }

    public double? FirearmsSidebarWidth { get; set; }
    public double? ReportsSidebarWidth { get; set; }
    public double? DashboardSidebarWidth { get; set; }
    public double? BoloSidebarWidth { get; set; }
    public double? ShiftHistorySidebarWidth { get; set; }
}

public static class UiLayoutStore
{
    static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDTProNative", "ui_layout.json");

    public static UiLayoutPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new UiLayoutPreferences();
            var json = File.ReadAllText(FilePath);
            return JsonConvert.DeserializeObject<UiLayoutPreferences>(json) ?? new UiLayoutPreferences();
        }
        catch
        {
            return new UiLayoutPreferences();
        }
    }

    public static void Save(UiLayoutPreferences p)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(p, Formatting.Indented));
        }
        catch { /* ignore */ }
    }
}
