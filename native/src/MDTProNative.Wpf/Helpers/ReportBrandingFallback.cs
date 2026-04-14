using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Helpers;

/// <summary>When <c>/data/reportBranding</c> is unavailable, match plugin default <c>regional_crime_lab</c>.</summary>
public static class ReportBrandingFallback
{
    public static JObject ActiveTemplate { get; } = new()
    {
        ["id"] = "regional_crime_lab",
        ["leftColumn"] =
            "San Andreas Regional Crime Laboratory\n" +
            "Evidence Receiving — Los Santos County\n" +
            "P.O. Box 1200, Los Santos, SA 90001\n" +
            "Phone: (555) 555-0100  Fax: (555) 555-0101",
        ["centerTitle"] = "SARL",
        ["rightTitle"] = "San Andreas Regional Crime Laboratory\nEvidence Receipt",
        ["footer"] = "Offline / fallback header — connect to MDT Pro for live agency branding.",
        ["propertyEvidenceTitle"] = "Property & Evidence Receipt",
        ["incidentTitle"] = "General Incident Report (IR)",
        ["citationTitle"] = "Uniform Traffic Citation — Violation Notice",
        ["arrestTitle"] = "Arrest & Booking Report",
        ["impoundTitle"] = "Vehicle Tow / Impound Report",
        ["trafficIncidentTitle"] = "Traffic Collision Report (TCR)",
        ["injuryTitle"] = "Injury / Medical Incident Report",
        ["sealBadgeFile"] = "sagov-badge.png"
    };
}
