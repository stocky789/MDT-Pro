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
}
