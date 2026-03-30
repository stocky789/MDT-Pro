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
                active = (JObject)byResolvedId.DeepClone();
            else if (catalog.TryGetValue(DefaultTemplateId, out var byDefaultId) && byDefaultId != null)
                active = (JObject)byDefaultId.DeepClone();
            else
                active = MinimalFallbackTemplate(id);
            ApplyOfficerSealOverride(active, officer);
            var catTok = new JObject();
            foreach (var kv in catalog)
                catTok[kv.Key] = kv.Value;
            return new JObject {
                ["catalog"] = catTok,
                ["activeTemplateId"] = id,
                ["activeTemplate"] = active
            };
        }

        /// <summary>LSPDFR script name wins for seal art so the badge matches the officer's department.</summary>
        static void ApplyOfficerSealOverride(JObject active, OfficerInformationData officer) {
            var file = SealBadgeFileForScript(officer?.agencyScriptName);
            if (!string.IsNullOrEmpty(file))
                active["sealBadgeFile"] = file;
        }

        static string SealBadgeFileForScript(string script) {
            if (string.IsNullOrWhiteSpace(script)) return null;
            switch (script.Trim().ToUpperInvariant()) {
                case "LSPD": return "lspd-badge.png";
                case "LSSD": return "lssd-badge.png";
                case "BCSO": return "bcso-badge.png";
                case "FIB": return "fib-badge.png";
                case "SAHP": return "sahp-badge.png";
                case "SAFD": return "safd-badge.png";
                default: return null;
            }
        }

        static JObject MinimalFallbackTemplate(string requestedId) {
            var jo = new JObject {
                ["id"] = string.IsNullOrEmpty(requestedId) ? DefaultTemplateId : requestedId,
                ["leftColumn"] = "Evidence receiving (fallback header)",
                ["centerTitle"] = "LAB",
                ["rightTitle"] = "Property & Evidence Receipt",
                ["footer"] = "MDT Pro — report branding catalog missing expected templates.",
                ["sealBadgeFile"] = "sagov-badge.png"
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

            /// <param name="sealBadgeFile">Filename in <c>plugins/DepartmentStyling/images/</c> (Department Styling plugin).</param>
            void Add(string id, string leftColumn, string centerTitle, string rightTitle, string footer, string sealBadgeFile) {
                var jo = new JObject {
                    ["id"] = id,
                    ["leftColumn"] = leftColumn,
                    ["centerTitle"] = centerTitle,
                    ["rightTitle"] = rightTitle,
                    ["footer"] = footer,
                    ["sealBadgeFile"] = sealBadgeFile
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
                "Regional lab intake — revised for MDT Pro.",
                "sagov-badge.png");

            Add("lssd_coroner",
                "County of Los Santos\nDepartment of Coroner\n" +
                "Strawberry, Los Santos, SA\n" +
                "Serving Communities With Science Fact\n" +
                "Phone: (555) 555-0200",
                "CORONER",
                "County of Los Santos\nForensic Evidence Receipt",
                "Coroner / ME-adjacent intake.",
                "lssd-badge.png");

            Add("lssd_patrol",
                "Los Santos County Sheriff's Department\n" +
                "Patrol Operations\n" +
                "Los Santos County, San Andreas",
                "LSSD",
                "Los Santos County Sheriff's Department\nOfficial Report",
                "Sheriff patrol and general law-enforcement reports.",
                "lssd-badge.png");

            Add("bcso_patrol",
                "Blaine County Sheriff's Office\n" +
                "Patrol Division\n" +
                "Blaine County, San Andreas",
                "BCSO",
                "Blaine County Sheriff's Office\nOfficial Report",
                "BCSO patrol and general law-enforcement reports.",
                "bcso-badge.png");

            Add("lspd_submission",
                "Los Santos Police Department\n" +
                "Evidence Control / Submission Desk\n" +
                "Mission Row Station\n" +
                "Los Santos, SA",
                "LSPD",
                "LSPD Evidence Submission Cover\n(Regional lab testing)",
                "Submitting agency block — receiving lab per local policy.",
                "lspd-badge.png");

            Add("fib_adjacent",
                "Federal Investigation Bureau\n" +
                "Los Santos Field Office\n" +
                "Pillbox Hill, Los Santos, SA\n" +
                "Secure evidence routing",
                "FIB",
                "Federal Evidence Receipt\n(Controlled routing)",
                "Federal-adjacent — map agencies explicitly.",
                "fib-badge.png");

            Add("humane_adjacent",
                "Humane Labs and Research (cover)\n" +
                "Blaine County, SA\n" +
                "Restricted — authorized submissions only",
                "HLR",
                "Specialized Technical Evidence Receipt",
                "Optional conspiracy / IAA-adjacent — not routine PD default.",
                "sagov-badge.png");

            return d;
        }

        /// <summary>Longest key match wins for maps.</summary>
        public static string ResolveTemplateId(string reportType, OfficerInformationData officer) {
            var agency = officer?.agency?.Trim() ?? "";
            var script = officer?.agencyScriptName?.Trim() ?? "";
            var isPropertyEvidence = string.Equals(reportType, "propertyEvidence", StringComparison.OrdinalIgnoreCase);
            var agencyUp = agency.ToUpperInvariant();

            // Explicit coroner / ME units (display name)
            if (agencyUp.Contains("CORONER") || agencyUp.Contains("MEDICAL EXAMINER") || agencyUp.Contains("M.E."))
                return "lssd_coroner";

            // LSSD / BCSO: coroner-style header only for property/evidence; patrol reports use sheriff templates
            if (string.Equals(script, "LSSD", StringComparison.OrdinalIgnoreCase))
                return isPropertyEvidence ? "lssd_coroner" : "lssd_patrol";
            if (string.Equals(script, "BCSO", StringComparison.OrdinalIgnoreCase))
                return isPropertyEvidence ? "lssd_coroner" : "bcso_patrol";

            // Other LSPDFR script names
            if (!string.IsNullOrEmpty(script) && ScriptToTemplate.TryGetValue(script, out var byScript))
                return byScript;

            // Agency display string (longest substring match)
            var agencyKey = AgencyToTemplate.Keys
                .Where(k => agency.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(k => k.Length)
                .FirstOrDefault();
            if (agencyKey != null) {
                var t = AgencyToTemplate[agencyKey];
                if (isPropertyEvidence && (t == "lssd_patrol" || t == "bcso_patrol"))
                    return "lssd_coroner";
                return t;
            }

            // Keyword heuristics
            var a = agency.ToUpperInvariant();
            if (a.Contains("FIB") || a.Contains("IAA") || a.Contains("FEDERAL INVESTIGATION"))
                return "fib_adjacent";
            if (a.Contains("HUMANE"))
                return "humane_adjacent";
            if (a.Contains("LSPD") || a.Contains("LOS SANTOS POLICE"))
                return "lspd_submission";
            if (a.Contains("BCSO") || a.Contains("BLAINE COUNTY SHERIFF"))
                return isPropertyEvidence ? "lssd_coroner" : "bcso_patrol";
            if (a.Contains("SHERIFF") || a.Contains("LSSD") || a.Contains("LOS SANTOS COUNTY SHERIFF"))
                return isPropertyEvidence ? "lssd_coroner" : "lssd_patrol";

            if (isPropertyEvidence)
                return DefaultTemplateId;

            return DefaultTemplateId;
        }

        static readonly Dictionary<string, string> ScriptToTemplate = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "FIB", "fib_adjacent" },
            { "LSPD", "lspd_submission" },
            { "HumaneLabs", "humane_adjacent" }
        };

        static readonly Dictionary<string, string> AgencyToTemplate = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Federal Investigation Bureau", "fib_adjacent" },
            { "FIB", "fib_adjacent" },
            { "IAA", "fib_adjacent" },
            { "Humane Labs", "humane_adjacent" },
            { "Los Santos Police Department", "lspd_submission" },
            { "LSPD", "lspd_submission" },
            { "Los Santos County Sheriff", "lssd_patrol" },
            { "LSSD", "lssd_patrol" },
            { "Blaine County Sheriff", "bcso_patrol" },
            { "BCSO", "bcso_patrol" }
        };
    }
}
