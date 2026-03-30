using System.Windows;
using System.Windows.Controls;
using MDTProNative.Wpf.Views;

namespace MDTProNative.Wpf.Services;

/// <summary>Wires GridSplitters to <see cref="UiLayoutStore"/> for module sidebars.</summary>
public static class UiLayoutHooks
{
    const double MinRatio = 0.12;
    const double MaxRatio = 0.88;

    public static void WirePersonSearch(PersonSearchView v)
    {
        var p = UiLayoutStore.Load();
        ApplyWidth(v.PersonSidebarColumn, p.PersonSidebarWidth, v.PersonSidebarColumn.MinWidth);
        ApplyVerticalRatio(v.PersonSidebarInnerGrid, v.PersonTopListRow, v.PersonBottomListRow, p.PersonLeftVerticalRatio);
        v.PersonMainSplitter.DragCompleted += (_, _) => SavePerson(v);
        v.PersonSidebarSplitter.DragCompleted += (_, _) => SavePerson(v);
    }

    public static void WireVehicleSearch(VehicleSearchView v)
    {
        var p = UiLayoutStore.Load();
        ApplyWidth(v.VehicleSidebarColumn, p.VehicleSidebarWidth, v.VehicleSidebarColumn.MinWidth);
        ApplyVerticalRatio(v.VehicleSidebarInnerGrid, v.VehicleTopListRow, v.VehicleBottomListRow, p.VehicleLeftVerticalRatio);
        v.VehicleMainSplitter.DragCompleted += (_, _) => SaveVehicle(v);
        v.VehicleSidebarSplitter.DragCompleted += (_, _) => SaveVehicle(v);
    }

    public static void WireFirearms(FirearmsView v)
    {
        var p = UiLayoutStore.Load();
        ApplyWidth(v.FirearmsSidebarColumn, p.FirearmsSidebarWidth, v.FirearmsSidebarColumn.MinWidth);
        v.FirearmsSplitter.DragCompleted += (_, _) =>
        {
            var prefs = UiLayoutStore.Load();
            prefs.FirearmsSidebarWidth = v.FirearmsSidebarColumn.ActualWidth;
            UiLayoutStore.Save(prefs);
        };
    }

    public static void WireReports(ReportsView v)
    {
        var p = UiLayoutStore.Load();
        ApplyWidth(v.ReportsSidebarColumn, p.ReportsSidebarWidth, v.ReportsSidebarColumn.MinWidth);
        v.ReportsSplitter.DragCompleted += (_, _) =>
        {
            var prefs = UiLayoutStore.Load();
            prefs.ReportsSidebarWidth = v.ReportsSidebarColumn.ActualWidth;
            UiLayoutStore.Save(prefs);
        };
    }

    public static void WireDashboard(DashboardView v)
    {
        var p = UiLayoutStore.Load();
        ApplyWidth(v.DashboardSidebarColumn, p.DashboardSidebarWidth, v.DashboardSidebarColumn.MinWidth);
        v.DashboardSplitter.DragCompleted += (_, _) =>
        {
            var prefs = UiLayoutStore.Load();
            prefs.DashboardSidebarWidth = v.DashboardSidebarColumn.ActualWidth;
            UiLayoutStore.Save(prefs);
        };
    }

    public static void WireBolo(BoloView v)
    {
        var p = UiLayoutStore.Load();
        ApplyWidth(v.BoloSidebarColumn, p.BoloSidebarWidth, v.BoloSidebarColumn.MinWidth);
        v.BoloSplitter.DragCompleted += (_, _) =>
        {
            var prefs = UiLayoutStore.Load();
            prefs.BoloSidebarWidth = v.BoloSidebarColumn.ActualWidth;
            UiLayoutStore.Save(prefs);
        };
    }

    public static void WireShiftHistory(ShiftHistoryView v)
    {
        var p = UiLayoutStore.Load();
        ApplyWidth(v.ShiftHistorySidebarColumn, p.ShiftHistorySidebarWidth, v.ShiftHistorySidebarColumn.MinWidth);
        v.ShiftHistorySplitter.DragCompleted += (_, _) =>
        {
            var prefs = UiLayoutStore.Load();
            prefs.ShiftHistorySidebarWidth = v.ShiftHistorySidebarColumn.ActualWidth;
            UiLayoutStore.Save(prefs);
        };
    }

    static void ApplyWidth(ColumnDefinition col, double? saved, double minWidth)
    {
        if (saved is { } w && w >= minWidth && !double.IsNaN(w) && !double.IsInfinity(w))
            col.Width = new GridLength(w);
    }

    static void ApplyVerticalRatio(Grid innerGrid, RowDefinition topStarRow, RowDefinition bottomStarRow, double? ratio)
    {
        if (ratio is not { } r || r < MinRatio || r > MaxRatio) return;
        topStarRow.Height = new GridLength(r, GridUnitType.Star);
        bottomStarRow.Height = new GridLength(1 - r, GridUnitType.Star);
    }

    static double? MeasureListRowRatio(Grid g, int topListRow, int bottomListRow)
    {
        double ht = 0, hb = 0;
        foreach (UIElement c in g.Children)
        {
            if (c is not FrameworkElement fe) continue;
            var row = Grid.GetRow(fe);
            if (row == topListRow) ht = Math.Max(ht, fe.ActualHeight);
            else if (row == bottomListRow) hb = Math.Max(hb, fe.ActualHeight);
        }
        var sum = ht + hb;
        if (sum < 24) return null;
        return ht / sum;
    }

    static void SavePerson(PersonSearchView v)
    {
        var prefs = UiLayoutStore.Load();
        prefs.PersonSidebarWidth = v.PersonSidebarColumn.ActualWidth;
        var ratio = MeasureListRowRatio(v.PersonSidebarInnerGrid, 1, 4);
        if (ratio is >= MinRatio and <= MaxRatio) prefs.PersonLeftVerticalRatio = ratio;
        UiLayoutStore.Save(prefs);
    }

    static void SaveVehicle(VehicleSearchView v)
    {
        var prefs = UiLayoutStore.Load();
        prefs.VehicleSidebarWidth = v.VehicleSidebarColumn.ActualWidth;
        var ratio = MeasureListRowRatio(v.VehicleSidebarInnerGrid, 1, 4);
        if (ratio is >= MinRatio and <= MaxRatio) prefs.VehicleLeftVerticalRatio = ratio;
        UiLayoutStore.Save(prefs);
    }
}
