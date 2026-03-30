using System;
using System.Collections.Generic;
using System.Linq;
using MDTPro.Data;
using Newtonsoft.Json.Linq;

namespace MDTPro.Utility {
    /// <summary>
    /// Built-in report header templates (GTA HD-universe–adjacent) and resolver for <c>/data/reportBranding</c>.
    /// Default for unmapped agencies / property evidence: <c>regional_crime_lab</c>.
    /// </summary>
    public static class ReportBrandingResolver {
        public const string DefaultTemplateId = "regional_crime_lab";

        /// <summary>GET /data/reportBranding — JSON for native WPF and browser MDT.</summary>
        public static JObject BuildResponse(string reportType, OfficerInformationData officer) {
            var catalog = BuildCatalog();
            var id = ResolveTemplateId(reportType, officer);
            JObject active;
            if (catalog.TryGetValue(id, out var byResolvedId) && byResolvedId != null)
                active = byResolvedId;
            else if (catalog.TryGetValue(DefaultTemplateId, out var byDefaultId) && byDefaultId != null)
                active = byDefaultId;
            else
                active = catalog.Values.FirstOrDefault(v => v != null) ?? MinimalFallbackTemplate(id);
            var catTok = new JObject();
            foreach (var kv in catalog)
                catTok[kv.Key] = kv.Value;
            return new JObject {
                ["catalog"] = catTok,
                ["activeTemplateId"] = id,
                ["activeTemplate"] = active
            };
        }

        static JObject MinimalFallbackTemplate(string requestedId) {
            var jo = new JObject {
                ["id"] = string.IsNullOrEmpty(requestedId) ? DefaultTemplateId : requestedId,
                ["leftColumn"] = "Evidence receiving (fallback header)",
                ["centerTitle"] = "LAB",
                ["rightTitle"] = "Property & Evidence Receipt",
                ["footer"] = "MDT Pro — report branding catalog missing expected templates."
            };
            AddStandardReportDocumentTitles(jo);
            return jo;
        }

        /// <summary>Per-report document headings (native WPF + browser). Agency block still comes from left/center/right/footer.</summary>
        static void AddStandardReportDocumentTitles(JObject template) {
            template["propertyEvidenceTitle"] = "Property & Evidence Receipt";
            template["incidentTitle"] = "General Incident Report (IR)";
            template["citationTitle"] = "Uniform Traffic Citation — Violation Notice";
            template["arrestTitle"] = "Arrest & Booking Report";
            template["impoundTitle"] = "Vehicle Tow / Impound Report";
            template["trafficIncidentTitle"] = "Traffic Collision Report (TCR)";
            template["injuryTitle"] = "Injury / Medical Incident Report";
        }

        static Dictionary<string, JObject> BuildCatalog() {
            var d = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

            void Add(string id, string leftColumn, string centerTitle, string rightTitle, string footer) {
                var jo = new JObject {
                    ["id"] = id,
                    ["leftColumn"] = leftColumn,
                    ["centerTitle"] = centerTitle,
                    ["rightTitle"] = rightTitle,
                    ["footer"] = footer
                };
                AddStandardReportDocumentTitles(jo);
                d[id] = jo;
            }

            Add("regional_crime_lab",
                "San Andreas Regional Crime Laboratory\n" +
                "Evidence Receiving — Los Santos County\n" +
                "P.O. Box 1200, Los Santos, SA 90001\n" +
                "Phone: (555) 555-0100  Fax: (555) 555-0101",
                "SARL",
                "San Andreas Regional Crime Laboratory\nEvidence Receipt",
                "Regional lab intake — revised for MDT Pro roleplay.");

            Add("lssd_coroner",
                "County of Los Santos\nDepartment of Coroner\n" +
                "Strawberry, Los Santos, SA\n" +
                "Serving Communities With Science Fact\n" +
                "Phone: (555) 555-0200",
                "CORONER",
                "County of Los Santos\nForensic Evidence Receipt",
                "Coroner / ME-adjacent intake — roleplay.");

            Add("lspd_submission",
                "Los Santos Police Department\n" +
                "Evidence Control / Submission Desk\n" +
                "Mission Row Station\n" +
                "Los Santos, SA",
                "LSPD",
                "LSPD Evidence Submission Cover\n(Regional lab testing)",
                "Submitting agency block — receiving lab per server policy.");

            Add("fib_adjacent",
                "Federal Investigation Bureau\n" +
                "Los Santos Field Office\n" +
                "Pillbox Hill, Los Santos, SA\n" +
                "Secure evidence routing",
                "FIB",
                "Federal Evidence Receipt\n(Controlled routing)",
                "Federal-adjacent — map agencies explicitly.");

            Add("humane_adjacent",
                "Humane Labs and Research (cover)\n" +
                "Blaine County, SA\n" +
                "Restricted — authorized submissions only",
                "HLR",
                "Specialized Technical Evidence Receipt",
                "Optional conspiracy / IAA-adjacent — not routine PD default.");

            return d;
        }

        /// <summary>Longest key match wins for maps.</summary>
        public static string ResolveTemplateId(string reportType, OfficerInformationData officer) {
            var agency = officer?.agency?.Trim() ?? "";
            var script = officer?.agencyScriptName?.Trim() ?? "";

            // 1) LSPDFR script name (exact)
            if (!string.IsNullOrEmpty(script) && ScriptToTemplate.TryGetValue(script, out var byScript))
                return byScript;

            // 2) Agency display string (longest substring match)
            var agencyKey = AgencyToTemplate.Keys
                .Where(k => agency.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(k => k.Length)
                .FirstOrDefault();
            if (agencyKey != null)
                return AgencyToTemplate[agencyKey];

            // 3) Keyword heuristics
            var a = agency.ToUpperInvariant();
            if (a.Contains("FIB") || a.Contains("IAA") || a.Contains("FEDERAL INVESTIGATION"))
                return "fib_adjacent";
            if (a.Contains("HUMANE"))
                return "humane_adjacent";
            if (a.Contains("LSPD") || a.Contains("LOS SANTOS POLICE"))
                return "lspd_submission";
            if (a.Contains("SHERIFF") || a.Contains("LSSD") || a.Contains("BCSO"))
                return "lssd_coroner";

            // 4) Report-type default (property / evidence → regional lab)
            if (string.Equals(reportType, "propertyEvidence", StringComparison.OrdinalIgnoreCase))
                return DefaultTemplateId;

            return DefaultTemplateId;
        }

        static readonly Dictionary<string, string> ScriptToTemplate = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "FIB", "fib_adjacent" },
            { "LSPD", "lspd_submission" },
            { "LSSD", "lssd_coroner" },
            { "BCSO", "lssd_coroner" },
            { "HumaneLabs", "humane_adjacent" }
        };

        static readonly Dictionary<string, string> AgencyToTemplate = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Federal Investigation Bureau", "fib_adjacent" },
            { "FIB", "fib_adjacent" },
            { "IAA", "fib_adjacent" },
            { "Humane Labs", "humane_adjacent" },
            { "Los Santos Police Department", "lspd_submission" },
            { "LSPD", "lspd_submission" },
            { "Los Santos County Sheriff", "lssd_coroner" },
            { "LSSD", "lssd_coroner" },
            { "Blaine County Sheriff", "lssd_coroner" },
            { "BCSO", "lssd_coroner" }
        };
    }
}
