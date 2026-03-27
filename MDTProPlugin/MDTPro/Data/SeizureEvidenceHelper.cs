using System;
using System.Collections.Generic;
using System.Linq;
using MDTPro.Data.Reports;

namespace MDTPro.Data {
    /// <summary>Charge-to-evidence mapping for Property and Evidence Receipt (seizure) reports. Determines which drug/firearm types satisfy which arrest charges for court evidence scoring.</summary>
    public static class SeizureEvidenceHelper {
        /// <summary>Drug type ids from seizureOptions.json. Aligned with Policing Redefined EDrugType where applicable.</summary>
        public static readonly IReadOnlyList<string> KnownDrugTypeIds = new[] {
            "Cannabis", "Cocaine", "Methamphetamine", "Amphetamine", "Ritalin", "Heroin", "Fentanyl", "Hydrocodone", "PCP",
            "LSD/Hallucinogen", "Mescaline", "Psilocybin", "Ecstasy/MDMA", "Prescription/Narcotic", "Benzodiazepine",
            "Paraphernalia Only", "Other Controlled Substance"
        };

        /// <summary>Returns the drug type ids that would satisfy evidence for the given charge. Empty list = any drug type satisfies (generic charge). Non-empty = at least one of these must appear in SeizedDrugTypes.</summary>
        public static List<string> GetRequiredDrugTypesForCharge(string chargeName) {
            if (string.IsNullOrWhiteSpace(chargeName)) return new List<string>();
            string n = chargeName.Trim().ToLowerInvariant();

            // Specific drug charges -> require matching type
            if (n.Contains("heroin")) return new List<string> { "Heroin" };
            if (n.Contains("fentanyl")) return new List<string> { "Fentanyl" };
            if (n.Contains("cocaine")) return new List<string> { "Cocaine" };
            if (n.Contains("methamphetamine")) return new List<string> { "Methamphetamine" };
            if (n.Contains("amphetamine") && !n.Contains("methamphetamine")) return new List<string> { "Amphetamine", "Ritalin", "Adderall", "Concerta", "Vyvanse" };
            if (n.Contains("ritalin")) return new List<string> { "Ritalin" };
            if (n.Contains("hydrocodone") || n.Contains("hydrocodone-acetaminophen") || n.Contains("vicodin"))
                return new List<string> { "Hydrocodone", "Vicodin", "Prescription/Narcotic" };
            if (n.Contains("benzodiazepine") || n.Contains("xanax") || n.Contains("valium")) return new List<string> { "Benzodiazepine" };
            if (n.Contains("pcp")) return new List<string> { "PCP" };
            if ((n.Contains("lsd") || n.Contains("hallucinogen") || n.Contains("mescaline") || n.Contains("psilocybin")) && !n.Contains("ecstasy"))
                return new List<string> { "LSD/Hallucinogen", "LSD_Hallucinogen", "Mescaline", "Psilocybin", "LSD" };
            if (n.Contains("ecstasy") || n.Contains("mdma")) return new List<string> { "Ecstasy/MDMA", "Ecstasy_MDMA" };
            if (n.Contains("possession of cannabis") || n.Contains("cannabis over legal limit") || n.Contains("sale or transport of cannabis")
                || n.Contains("possession of marijuana") || n.Contains("cultivation of marijuana"))
                return new List<string> { "Cannabis" };

            // Prescription/Narcotic -> that type OR any (per task: "Prescription/Narcotic" OR any)
            if ((n.Contains("prescription") && n.Contains("narcotic")) || n.Contains("controlled substance (prescription"))
                return new List<string> { "Prescription/Narcotic" }; // Will be treated as "match this OR any" in ChargeSatisfiedBySeizedDrugs

            // Paraphernalia -> Paraphernalia Only OR any (per task)
            if (n.Contains("paraphernalia")) return new List<string> { "Paraphernalia Only" };

            // "Possession Of Controlled Substance" (generic) and same-style charges: any documented drug on the PER satisfies court drug-evidence scoring (same practical effect as the removed duplicate "(Prescription/Narcotic)" charge).
            // Legacy saved charge names that still include "prescription" + "narcotic" hit the branch above.
            if (n.Contains("controlled substance") || n.Contains("trafficking") || n.Contains("for sale")
                || n.Contains("sale or transport") || n.Contains("transport or sale") || n.Contains("transport of meth")
                || n.Contains("manufacturing meth") || n.Contains("under influence of controlled"))
                return new List<string>(); // empty = any

            return new List<string>();
        }

        /// <summary>True if the seized drug types satisfy evidence for the given charge. Empty required list = any drug type counts. Non-empty = at least one required type must be in seizedDrugTypes.</summary>
        public static bool ChargeSatisfiedBySeizedDrugs(string chargeName, List<string> seizedDrugTypes) {
            var required = GetRequiredDrugTypesForCharge(chargeName);
            if (required == null || required.Count == 0) return seizedDrugTypes != null && seizedDrugTypes.Count > 0;
            if (seizedDrugTypes == null || seizedDrugTypes.Count == 0) return false;
            foreach (string r in required) {
                if (seizedDrugTypes.Any(s => string.Equals(s, r, StringComparison.OrdinalIgnoreCase))) return true;
            }
            // Prescription/Narcotic and Paraphernalia: "OR any" — if we have any drug at all, it counts
            string n = (chargeName ?? "").ToLowerInvariant();
            if ((n.Contains("prescription") && n.Contains("narcotic")) || n.Contains("paraphernalia"))
                return seizedDrugTypes.Count > 0;
            return false;
        }

        /// <summary>True if firearm evidence is satisfied: case has firearm charge AND seizure report has at least one firearm type.</summary>
        public static bool IsFirearmChargeSatisfiedBySeizedFirearms(bool hasFirearmCharge, List<string> seizedFirearmTypes) {
            if (!hasFirearmCharge) return false;
            return seizedFirearmTypes != null && seizedFirearmTypes.Count > 0;
        }

        /// <summary>Returns 0-1 weight for drug quantity (higher = more significant for conviction). Used for evidence score bonus.</summary>
        public static float GetQuantityWeight(string quantity) {
            if (string.IsNullOrWhiteSpace(quantity)) return 0.5f;
            string q = quantity.Trim().ToLowerInvariant();
            if (q.Contains("trace") || q == "—" || q == "-") return 0.3f;
            if (q.Contains("1 baggie") || q.Contains("1 pill") || q.Contains("1 capsule") || q.Contains("less than 1g")) return 0.4f;
            if (q.Contains("2 baggie") || q.Contains("1 bundle") || q.Contains("1g") || q.Contains("2g")) return 0.5f;
            if (q.Contains("3+ baggie") || q.Contains("2 bundle") || q.Contains("3.5g") || q.Contains("5g") || q.Contains("multiple pill") || q.Contains("multiple capsule")) return 0.7f;
            if (q.Contains("3+ bundle") || q.Contains("10g") || q.Contains("1 ounce") || q.Contains("1 brick")) return 0.85f;
            if (q.Contains("multiple ounce") || q.Contains("multiple brick") || q.Contains("1 pound") || q.Contains("kilogram")) return 1f;
            return 0.5f;
        }

        /// <summary>Total quantity weight from seized drug entries (sum, capped at 1). Higher quantity = stronger evidence.</summary>
        public static float GetTotalQuantityWeight(List<PropertyEvidenceReceiptReport.SeizedDrugEntry> seizedDrugs) {
            if (seizedDrugs == null || seizedDrugs.Count == 0) return 0.5f;
            float sum = 0f;
            foreach (var d in seizedDrugs) {
                if (d == null) continue;
                sum += GetQuantityWeight(d.Quantity);
            }
            return Math.Min(1f, sum / Math.Max(1, seizedDrugs.Count) + (seizedDrugs.Count > 1 ? 0.1f * (seizedDrugs.Count - 1) : 0f));
        }
    }
}
