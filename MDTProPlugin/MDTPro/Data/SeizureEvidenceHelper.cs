using System;
using System.Collections.Generic;
using System.Linq;
using MDTPro.Data.Reports;

namespace MDTPro.Data
{
    /// <summary>Charge-to-evidence mapping for Property and Evidence Receipt (seizure) reports. Determines which drug/firearm types and quantities satisfy arrest charges for court evidence scoring.</summary>
    public static class SeizureEvidenceHelper
    {
        public const int UnsupportedEscalatedChargeCap = 20;
        public const int WeakEscalatedChargeCap = 35;

        /// <summary>Drug type ids from seizureOptions.json. Aligned with Policing Redefined EDrugType where applicable.</summary>
        public static readonly IReadOnlyList<string> KnownDrugTypeIds = new[] {
            "Cannabis", "Cocaine", "Methamphetamine", "Amphetamine", "Ritalin", "Heroin", "Fentanyl", "Hydrocodone", "PCP",
            "LSD/Hallucinogen", "Mescaline", "Psilocybin", "Ecstasy/MDMA", "Prescription/Narcotic", "Benzodiazepine",
            "Paraphernalia Only", "Other Controlled Substance"
        };

        public enum QuantityTier
        {
            Unknown = 0,
            Trace = 1,
            Minimal = 2,
            Personal = 3,
            PersonalPlus = 4,
            Moderate = 5,
            Distribution = 6,
            Trafficking = 7,
            Bulk = 8
        }

        public enum DrugChargeProofLevel
        {
            Possession = 0,
            CannabisOver28g = 1,
            PossessionForSale = 2,
            Trafficking = 3,
            Manufacturing = 4
        }

        public class DrugEvidenceAssessment
        {
            public string ChargeName;
            public string DrugType;
            public string Quantity;
            public string QuantityTier;
            public string RequiredProofLevel;
            public bool TypeMatched;
            public bool SupportsCharge;
            public string SupportedLesserLevel;
            public string ReasonCode;
            public string Reason;
            public int? ConvictionChanceCap;
            public float QuantityWeight;
        }

        public static List<string> GetRequiredDrugTypesForCharge(string chargeName)
        {
            if (string.IsNullOrWhiteSpace(chargeName)) return new List<string>();
            string n = chargeName.Trim().ToLowerInvariant();

            if (n.Contains("heroin")) return new List<string> { "Heroin" };
            if (n.Contains("fentanyl")) return new List<string> { "Fentanyl" };
            if (n.Contains("cocaine")) return new List<string> { "Cocaine" };
            if (n.Contains("methamphetamine")) return new List<string> { "Methamphetamine" };
            if (n.Contains("amphetamine") && !n.Contains("methamphetamine")) return new List<string> { "Amphetamine", "Ritalin", "Adderall", "Concerta", "Vyvanse" };
            if (n.Contains("ritalin")) return new List<string> { "Ritalin" };
            if (n.Contains("hydrocodone") || n.Contains("hydrocodone-acetaminophen") || n.Contains("vicodin"))
                return new List<string> { "Hydrocodone", "Vicodin", "Prescription/Narcotic" };
            if (n.Contains("prescription pill") || n.Contains("prescription drug") || n.Contains("oxycontin") || n.Contains("codeine") || n.Contains("methadone") || n.Contains("morphine"))
                return new List<string> { "Prescription/Narcotic", "Hydrocodone", "Vicodin", "Ritalin", "Benzodiazepine" };
            if (n.Contains("benzodiazepine") || n.Contains("xanax") || n.Contains("valium")) return new List<string> { "Benzodiazepine" };
            if (n.Contains("pcp")) return new List<string> { "PCP" };
            if ((n.Contains("lsd") || n.Contains("hallucinogen") || n.Contains("mescaline") || n.Contains("psilocybin")) && !n.Contains("ecstasy"))
                return new List<string> { "LSD/Hallucinogen", "LSD_Hallucinogen", "Mescaline", "Psilocybin", "LSD" };
            if (n.Contains("ecstasy") || n.Contains("mdma")) return new List<string> { "Ecstasy/MDMA", "Ecstasy_MDMA" };
            if (n.Contains("possession of cannabis") || n.Contains("cannabis over legal limit") || n.Contains("sale or transport of cannabis")
                || n.Contains("possession of marijuana") || n.Contains("cultivation of marijuana"))
                return new List<string> { "Cannabis" };

            if ((n.Contains("prescription") && n.Contains("narcotic")) || n.Contains("controlled substance (prescription"))
                return new List<string> { "Prescription/Narcotic" };

            if (n.Contains("paraphernalia")) return new List<string> { "Paraphernalia Only" };

            if (n.Contains("controlled substance") || n.Contains("trafficking") || n.Contains("for sale")
                || n.Contains("sale or transport") || n.Contains("transport or sale") || n.Contains("transport of meth")
                || n.Contains("manufacturing meth") || n.Contains("under influence of controlled") || n.Contains("intent to distribute"))
                return new List<string>();

            return new List<string>();
        }

        public static bool ChargeSatisfiedBySeizedDrugs(string chargeName, List<string> seizedDrugTypes)
        {
            var required = GetRequiredDrugTypesForCharge(chargeName);
            if (required == null || required.Count == 0) return seizedDrugTypes != null && seizedDrugTypes.Count > 0;
            if (seizedDrugTypes == null || seizedDrugTypes.Count == 0) return false;
            foreach (string r in required)
            {
                if (seizedDrugTypes.Any(s => string.Equals(s, r, StringComparison.OrdinalIgnoreCase))) return true;
            }
            string n = (chargeName ?? "").ToLowerInvariant();
            if ((n.Contains("prescription") && n.Contains("narcotic")) || n.Contains("paraphernalia"))
                return seizedDrugTypes.Count > 0;
            return false;
        }

        public static bool IsFirearmChargeSatisfiedBySeizedFirearms(bool hasFirearmCharge, List<string> seizedFirearmTypes)
        {
            if (!hasFirearmCharge) return false;
            return seizedFirearmTypes != null && seizedFirearmTypes.Count > 0;
        }

        public static DrugChargeProofLevel GetRequiredProofLevel(string chargeName)
        {
            string n = (chargeName ?? "").ToLowerInvariant();
            if (n.Contains("manufactur") || n.Contains("cultivation")) return DrugChargeProofLevel.Manufacturing;
            if (n.Contains("trafficking") || n.Contains("transport or sale") || n.Contains("sale or transport") || n.Contains("transport of meth")) return DrugChargeProofLevel.Trafficking;
            if (n.Contains("intent to distribute") || n.Contains("for sale")) return DrugChargeProofLevel.PossessionForSale;
            if ((n.Contains("marijuana") || n.Contains("cannabis")) && (n.Contains("28 grams") || n.Contains("28g"))) return DrugChargeProofLevel.CannabisOver28g;
            return DrugChargeProofLevel.Possession;
        }

        public static bool IsEscalatedDrugCharge(string chargeName)
        {
            var level = GetRequiredProofLevel(chargeName);
            return level == DrugChargeProofLevel.PossessionForSale || level == DrugChargeProofLevel.Trafficking || level == DrugChargeProofLevel.Manufacturing || level == DrugChargeProofLevel.CannabisOver28g;
        }

        public static QuantityTier GetQuantityTier(string quantity)
        {
            if (string.IsNullOrWhiteSpace(quantity)) return QuantityTier.Unknown;
            string q = quantity.Trim().ToLowerInvariant().Replace("-", "–");

            switch (q)
            {
                case "—":
                case "–":
                case "other":
                    return QuantityTier.Unknown;

                case "trace amount":
                    return QuantityTier.Trace;

                case "less than 1g":
                    return QuantityTier.Minimal;

                case "1g":
                case "1 baggie":
                case "1 pill":
                case "1 capsule":
                case "1 vial":
                    return QuantityTier.Personal;

                case "2g":
                case "2 baggies":
                case "2–5 pills":
                case "2–5 capsules":
                case "2–5 vials":
                    return QuantityTier.PersonalPlus;

                case "3.5g":
                case "5g":
                case "3–5 baggies":
                case "1 bundle":
                case "6–20 pills":
                case "6–20 capsules":
                case "6–20 vials":
                    return QuantityTier.Moderate;

                case "10g–27g":
                case "28g–99g":
                case "1 ounce":
                case "6–10 baggies":
                case "2 bundles":
                case "21+ pills":
                case "21+ capsules":
                case "21+ vials":
                    return QuantityTier.Distribution;

                case "100g+":
                case "multiple ounces":
                case "11+ baggies":
                case "3+ bundles":
                case "100+ pills":
                case "100+ capsules":
                case "100+ vials":
                case "1 brick":
                    return QuantityTier.Trafficking;

                case "500g+":
                case "1 pound+":
                case "1 kilogram+":
                case "multiple bricks":
                    return QuantityTier.Bulk;
            }

            return QuantityTier.Unknown;
        }

        public static float GetQuantityWeight(string quantity)
        {
            switch (GetQuantityTier(quantity))
            {
                case QuantityTier.Trace: return 0.1f;
                case QuantityTier.Minimal: return 0.2f;
                case QuantityTier.Personal: return 0.35f;
                case QuantityTier.PersonalPlus: return 0.45f;
                case QuantityTier.Moderate: return 0.6f;
                case QuantityTier.Distribution: return 0.8f;
                case QuantityTier.Trafficking: return 0.9f;
                case QuantityTier.Bulk: return 1f;
                default: return 0.5f;
            }
        }

        public static DrugEvidenceAssessment AssessDrugEvidenceForCharge(string chargeName, PropertyEvidenceReceiptReport.SeizedDrugEntry seizedDrug)
        {
            string drugType = seizedDrug?.DrugType ?? "";
            string quantity = seizedDrug?.Quantity ?? "";
            var tier = GetQuantityTier(quantity);
            var level = GetRequiredProofLevel(chargeName);
            bool typeMatched = ChargeSatisfiedBySeizedDrugs(chargeName, string.IsNullOrWhiteSpace(drugType) ? new List<string>() : new List<string> { drugType });
            bool supports = false;
            string lesser = null;
            string reasonCode = "drug_type_not_matched";
            string reason = "The seized drug type did not match this charge.";
            int? cap = null;

            if (typeMatched)
            {
                switch (level)
                {
                    case DrugChargeProofLevel.Possession:
                        supports = tier != QuantityTier.Unknown || !string.IsNullOrWhiteSpace(quantity);
                        reasonCode = tier == QuantityTier.Trace ? "trace_amount_weak_possession" : "quantity_supports_possession";
                        reason = tier == QuantityTier.Trace ? "Only a trace amount was documented, making possession evidence weak." : "The seized quantity supports a possession-level drug charge.";
                        break;
                    case DrugChargeProofLevel.CannabisOver28g:
                        supports = tier >= QuantityTier.Distribution;
                        lesser = supports ? null : "Simple possession";
                        reasonCode = supports ? "cannabis_quantity_over_28g" : "quantity_below_cannabis_28g_threshold";
                        reason = supports ? "The documented cannabis quantity supports the over-28g charge." : "The receipt did not document enough cannabis to support the over-28g charge.";
                        cap = supports ? (int?)null : UnsupportedEscalatedChargeCap;
                        break;
                    case DrugChargeProofLevel.PossessionForSale:
                        supports = tier >= QuantityTier.Distribution;
                        lesser = supports ? null : "Simple possession";
                        reasonCode = supports ? "quantity_supports_for_sale" : "quantity_personal_use_not_for_sale";
                        reason = supports
                            ? "The documented quantity supports possession for sale."
                            : "The documented quantity supports possession, but not possession for sale.";
                        cap = supports ? (int?)null : (tier >= QuantityTier.Moderate ? WeakEscalatedChargeCap : UnsupportedEscalatedChargeCap);
                        break;
                    case DrugChargeProofLevel.Trafficking:
                        supports = tier >= QuantityTier.Trafficking;
                        lesser = supports ? null : (tier >= QuantityTier.Distribution ? "Possession for sale" : "Simple possession");
                        reasonCode = supports ? (tier >= QuantityTier.Bulk ? "bulk_quantity_supports_trafficking" : "quantity_supports_trafficking") : "quantity_too_low_for_trafficking";
                        reason = supports
                            ? (tier >= QuantityTier.Bulk ? "Bulk quantity supports the trafficking count." : "The documented quantity supports the trafficking count.")
                            : "The seized amount was too small to support trafficking by quantity alone.";
                        cap = supports ? (int?)null : (tier >= QuantityTier.Distribution ? WeakEscalatedChargeCap : UnsupportedEscalatedChargeCap);
                        break;
                    case DrugChargeProofLevel.Manufacturing:
                        supports = false;
                        lesser = tier >= QuantityTier.Trafficking ? "Trafficking" : (tier >= QuantityTier.Distribution ? "Possession for sale" : "Simple possession");
                        reasonCode = "quantity_only_not_manufacturing";
                        reason = "The property evidence receipt documents seized quantity, but quantity alone does not establish manufacturing.";
                        cap = UnsupportedEscalatedChargeCap;
                        break;
                }
            }

            return new DrugEvidenceAssessment
            {
                ChargeName = chargeName ?? "",
                DrugType = drugType,
                Quantity = quantity,
                QuantityTier = tier.ToString(),
                RequiredProofLevel = level.ToString(),
                TypeMatched = typeMatched,
                SupportsCharge = typeMatched && supports,
                SupportedLesserLevel = lesser,
                ReasonCode = reasonCode,
                Reason = reason,
                ConvictionChanceCap = cap,
                QuantityWeight = GetQuantityWeight(quantity)
            };
        }

        public static DrugEvidenceAssessment BestAssessmentForCharge(string chargeName, IEnumerable<PropertyEvidenceReceiptReport.SeizedDrugEntry> seizedDrugs)
        {
            if (seizedDrugs == null) return null;
            DrugEvidenceAssessment best = null;
            foreach (var drug in seizedDrugs)
            {
                if (drug == null) continue;
                var current = AssessDrugEvidenceForCharge(chargeName, drug);
                if (current == null || !current.TypeMatched) continue;
                if (best == null || Rank(current) > Rank(best)) best = current;
            }
            return best;
        }

        public static float GetTotalQuantityWeight(List<PropertyEvidenceReceiptReport.SeizedDrugEntry> seizedDrugs)
        {
            if (seizedDrugs == null || seizedDrugs.Count == 0) return 0.5f;
            float sum = 0f;
            int count = 0;
            foreach (var d in seizedDrugs)
            {
                if (d == null) continue;
                sum += GetQuantityWeight(d.Quantity);
                count++;
            }
            if (count == 0) return 0.5f;
            return Math.Min(1f, (sum / count) + (count > 1 ? 0.08f * (count - 1) : 0f));
        }

        private static int Rank(DrugEvidenceAssessment a)
        {
            int rank = a.SupportsCharge ? 1000 : 0;
            rank += (int)(a.QuantityWeight * 100);
            if (a.ConvictionChanceCap.HasValue) rank -= a.ConvictionChanceCap.Value;
            return rank;
        }

    }
}
