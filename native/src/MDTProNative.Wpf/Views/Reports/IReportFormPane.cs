using MDTProNative.Wpf.Services;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports;

/// <summary>Structured report editor panel; JSON shape matches MDT Pro plugin report types (PascalCase).</summary>
public interface IReportFormPane
{
    void Bind(MdtConnectionManager? connection);
    void LoadFromReport(JObject report);
    /// <summary>Builds a report object including base <see cref="Report"/> fields.</summary>
    JObject BuildReport();
    void Clear();

    /// <summary>True when the pane is still the active editor but not hosted in <c>FormHost</c> (e.g. pop-out window).</summary>
    bool IsDetachedFromHost => false;

    /// <summary>Closes any secondary surface detached from <c>FormHost</c> before clearing the host or disposing panes.</summary>
    void CloseDetachSurfaces() { }

    /// <summary>Optional: after a new draft load, prefill offender / injured party from person search navigation.</summary>
    void ApplyPersonSearchPrefill(string pedName, string? vehicleLicensePlate) { }

    /// <summary>Optional: after a new draft load, prefill from vehicle search (e.g. impound).</summary>
    void ApplyVehicleSearchPrefill(JObject vehicleSnapshot) { }
}
