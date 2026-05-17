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
            string q = quantity.Trim().ToLowerInvariant();
            if (q == "—" || q == "-" || q.Contains("trace")) return QuantityTier.Trace;
            if (q.Contains("less than 1g")) return QuantityTier.Minimal;
            if (q.Contains("1 baggie") || q.Contains("1 pill") || q.Contains("1 capsule") || q.Contains("1 vial") || q == "1g") return QuantityTier.Personal;
            if (q.Contains("2 baggie") || q.Contains("2 pill") || q.Contains("2-5 pill") || q.Contains("2–5 pill") || q.Contains("2 capsule") || q.Contains("2-5 capsule") || q.Contains("2–5 capsule") || q.Contains("2 vial") || q.Contains("2-5 vial") || q.Contains("2–5 vial") || q == "2g") return QuantityTier.PersonalPlus;
            if (q.Contains("3–5") || q.Contains("3-5") || q.Contains("3+ baggie") || q.Contains("3.5g") || q.Contains("5g") || q.Contains("1 bundle") || q.Contains("multiple pill") || q.Contains("multiple capsule") || q.Contains("multiple vial") || q.Contains("6–20 pill") || q.Contains("6-20 pill") || q.Contains("6–20 capsule") || q.Contains("6-20 capsule") || q.Contains("6–20 vial") || q.Contains("6-20 vial")) return QuantityTier.Moderate;
            if (q.Contains("6–10 baggie") || q.Contains("6-10 baggie") || q.Contains("2 bundle") || q.Contains("10g") || q.Contains("10g–27g") || q.Contains("10g-27g") || q.Contains("1 ounce") || q.Contains("28g") || q.Contains("28g–99g") || q.Contains("28g-99g") || q.Contains("21+ pill") || q.Contains("21+ capsule") || q.Contains("21+ vial")) return QuantityTier.Distribution;
            if (q.Contains("11+ baggie") || q.Contains("3+ bundle") || q.Contains("multiple ounce") || q.Contains("100g") || q.Contains("1 brick") || q.Contains("100+ pill") || q.Contains("100+ capsule") || q.Contains("100+ vial")) return QuantityTier.Trafficking;
            if (q.Contains("multiple brick") || q.Contains("1 pound") || q.Contains("500g") || q.Contains("kilogram") || q.Contains("kilo")) return QuantityTier.Bulk;
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

        public static DrugEvidenceAssessment AssessDrugEvidenceForCharge(string chargeName, PropertyEvidenceReceiptReport.SeizedDrugEntry seizedDrug, IEnumerable<string> salesIndicators = null, IEnumerable<string> manufacturingIndicators = null, string otherNotes = null)
        {
            string drugType = seizedDrug?.DrugType ?? "";
            string quantity = seizedDrug?.Quantity ?? "";
            var tier = GetQuantityTier(quantity);
            var level = GetRequiredProofLevel(chargeName);
            bool typeMatched = ChargeSatisfiedBySeizedDrugs(chargeName, string.IsNullOrWhiteSpace(drugType) ? new List<string>() : new List<string> { drugType });
            bool hasSalesIndicators = HasAny(salesIndicators) || TextHasAny(otherNotes, "scale", "cash", "ledger", "pay-owe", "pay owe", "packaging", "buyer", "sale", "distribution");
            bool hasManufacturingIndicators = HasAny(manufacturingIndicators) || TextHasAny(otherNotes, "lab", "precursor", "chemical", "cook", "manufactur", "extraction", "grow", "plant", "processing", "ventilation");
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
                        supports = tier != QuantityTier.Trace || !string.IsNullOrWhiteSpace(quantity);
                        reasonCode = supports ? "quantity_supports_possession" : "trace_amount_weak_possession";
                        reason = supports ? "The seized quantity supports a possession-level drug charge." : "Only a trace amount was documented, making possession evidence weak.";
                        break;
                    case DrugChargeProofLevel.CannabisOver28g:
                        supports = tier >= QuantityTier.Distribution;
                        lesser = supports ? null : "Simple possession";
                        reasonCode = supports ? "cannabis_quantity_over_28g" : "quantity_below_cannabis_28g_threshold";
                        reason = supports ? "The documented cannabis quantity supports the over-28g charge." : "The receipt did not document enough cannabis to support the over-28g charge.";
                        cap = supports ? (int?)null : UnsupportedEscalatedChargeCap;
                        break;
                    case DrugChargeProofLevel.PossessionForSale:
                        supports = tier >= QuantityTier.Distribution || (tier >= QuantityTier.Moderate && hasSalesIndicators);
                        lesser = supports ? null : "Simple possession";
                        reasonCode = supports ? (hasSalesIndicators ? "sales_indicators_support_for_sale" : "quantity_supports_for_sale") : "quantity_personal_use_not_for_sale";
                        reason = supports
                            ? (hasSalesIndicators ? "The documented quantity and sales indicators support possession for sale." : "The documented quantity supports possession for sale.")
                            : "The documented quantity supports possession, but not possession for sale without sales indicators.";
                        cap = supports ? (int?)null : (tier >= QuantityTier.Moderate ? WeakEscalatedChargeCap : UnsupportedEscalatedChargeCap);
                        break;
                    case DrugChargeProofLevel.Trafficking:
                        supports = tier >= QuantityTier.Trafficking || (tier >= QuantityTier.Distribution && hasSalesIndicators);
                        lesser = supports ? null : (tier >= QuantityTier.Distribution ? "Possession for sale" : "Simple possession");
                        reasonCode = supports ? (hasSalesIndicators ? "sales_indicators_support_trafficking" : "bulk_quantity_supports_trafficking") : "quantity_too_low_for_trafficking";
                        reason = supports
                            ? (hasSalesIndicators && tier < QuantityTier.Trafficking ? "Distribution quantity and sales indicators support the trafficking count." : "Bulk quantity supports the trafficking count.")
                            : "The seized amount was too small to support trafficking by itself.";
                        cap = supports ? (int?)null : (tier >= QuantityTier.Distribution ? WeakEscalatedChargeCap : UnsupportedEscalatedChargeCap);
                        break;
                    case DrugChargeProofLevel.Manufacturing:
                        supports = hasManufacturingIndicators;
                        lesser = supports ? null : (tier >= QuantityTier.Distribution ? "Possession for sale" : "Simple possession");
                        reasonCode = supports ? "manufacturing_indicators_documented" : "no_manufacturing_indicators";
                        reason = supports ? "Manufacturing, lab, grow, or precursor indicators were documented." : "Quantity alone does not support a manufacturing charge without lab, grow, precursor, or processing evidence.";
                        cap = supports ? (int?)null : UnsupportedEscalatedChargeCap;
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

        public static DrugEvidenceAssessment BestAssessmentForCharge(string chargeName, IEnumerable<PropertyEvidenceReceiptReport.SeizedDrugEntry> seizedDrugs, IEnumerable<string> salesIndicators = null, IEnumerable<string> manufacturingIndicators = null, string otherNotes = null)
        {
            if (seizedDrugs == null) return null;
            DrugEvidenceAssessment best = null;
            foreach (var drug in seizedDrugs)
            {
                if (drug == null) continue;
                var current = AssessDrugEvidenceForCharge(chargeName, drug, salesIndicators, manufacturingIndicators, otherNotes);
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

        private static bool HasAny(IEnumerable<string> values)
        {
            return values != null && values.Any(v => !string.IsNullOrWhiteSpace(v));
        }

        private static bool TextHasAny(string text, params string[] terms)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string n = text.ToLowerInvariant();
            return terms.Any(t => n.Contains(t));
        }
    }
}
