namespace MDTProNative.Wpf.Services;

public static class NativeReportPostPaths
{
    static readonly Dictionary<string, string> ByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["incident"] = "createIncidentReport",
        ["citation"] = "createCitationReport",
        ["arrest"] = "createArrestReport",
        ["impound"] = "createImpoundReport",
        ["trafficIncident"] = "createTrafficIncidentReport",
        ["injury"] = "createInjuryReport",
        ["propertyEvidence"] = "createPropertyEvidenceReceiptReport",
    };

    public static string? PostPathFor(string reportType) =>
        ByType.TryGetValue(reportType, out var p) ? p : null;
}
