using System.Collections.Generic;
using Newtonsoft.Json;

namespace MDTPro.Data {
    public class CourtData {
        internal CourtData(string pedName, string number, string reportId, int shortYear) {
            PedName = pedName;
            Number = number;
            ReportId = reportId;
            ShortYear = shortYear;
            CreatedAtUtc = System.DateTime.UtcNow.ToString("o");
            LastUpdatedUtc = CreatedAtUtc;
        }

        public CourtData() { }

        public string PedName;
        public string Number;
        public string ReportId;

        /// <summary>Stable id for MDT-generated prior cases that explain CDF probation/parole (no patrol arrest report).</summary>
        public const string SyntheticSupervisionReportId = "MDT-SUPERVISION-BACKSTORY";

        /// <summary>True when this case is a reconstructed prior judgment for supervision backstory (API / MDT UI).</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsSyntheticSupervisionBackstory => string.Equals(ReportId, SyntheticSupervisionReportId, System.StringComparison.Ordinal);

        public bool ShouldSerializeIsSyntheticSupervisionBackstory() => IsSyntheticSupervisionBackstory;

        /// <summary>Short explanation for officers in Person Search / Court (serialized only for synthetic cases).</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string SupervisionRecordHint => IsSyntheticSupervisionBackstory
            ? "Reconstructed prior judgment on file: statutory charges align with this subject’s current probation/parole status. Not linked to a patrol arrest report in MDT."
            : null;

        public bool ShouldSerializeSupervisionRecordHint() => IsSyntheticSupervisionBackstory;

        public int ShortYear;
        public int Status = 0;
        public bool IsJuryTrial = false;
        public int JurySize = 0;
        public int JuryVotesForConviction = 0;
        public int JuryVotesForAcquittal = 0;
        public int PriorCitationCount = 0;
        public int PriorArrestCount = 0;
        public int PriorConvictionCount = 0;
        public int SeverityScore = 0;
        public int EvidenceScore = 0;
        /// <summary>0=Low (&lt;35), 1=Medium (35-59), 2=High (≥60). Computed from EvidenceScore for API.</summary>
        public int EvidenceBand = 0;
        public bool EvidenceHadWeapon = false;
        public bool EvidenceWasWanted = false;
        public bool EvidenceWasPatDown = false;
        public bool EvidenceWasDrunk = false;
        public bool EvidenceWasFleeing = false;
        public bool EvidenceAssaultedPed = false;
        public bool EvidenceDamagedVehicle = false;
        public bool EvidenceIllegalWeapon = false;
        public bool EvidenceViolatedSupervision = false;
        public bool EvidenceResisted = false;
        public bool EvidenceHadDrugs = false;
        public bool EvidenceUseOfForce = false;
        /// <summary>When drug evidence comes from seizure report: specific types documented (e.g. "Heroin", "Cocaine"). Null or empty = generic evidence (drug_records or DocumentedDrugs).</summary>
        public List<string> EvidenceDrugTypesBreakdown;
        /// <summary>When firearm evidence comes from seizure report: specific types documented (e.g. "Pistol", "Rifle"). Null or empty = generic (DocumentedFirearms or in-game).</summary>
        public List<string> EvidenceFirearmTypesBreakdown;
        public int RepeatOffenderScore = 0;
        public int ConvictionChance = 0;
        public string ResolveAtUtc;
        public float SentenceMultiplier = 1f;
        public float ProsecutionStrength = 0f;
        public float DefenseStrength = 0f;
        public float DocketPressure = 0f;
        public float PolicyAdjustment = 0f;
        public string CourtDistrict;
        public string CourtName;
        public string CourtType;
        public bool HasPublicDefender = true;
        public string Plea = "Not Guilty";
        public string JudgeName;
        public string ProsecutorName;
        public string DefenseAttorneyName;
        public string HearingDateUtc;
        public string CreatedAtUtc;
        public string LastUpdatedUtc;
        public string OutcomeNotes;
        public string OutcomeReasoning;
        /// <summary>Reasoning for the sentence imposed (aggravating/mitigating factors, judge remarks). Only set when Status = 1 (convicted).</summary>
        public string SentenceReasoning;
        /// <summary>License revocations ordered by the court upon conviction (e.g. "Driver's License Revoked", "Firearms Permit Revoked (10 years)"). Based on California law.</summary>
        public List<string> LicenseRevocations = new List<string>();
        /// <summary>Report IDs attached to this case (evidence). Editable until court date; then frozen.</summary>
        public List<string> AttachedReportIds = new List<string>();
        /// <summary>Optional one-paragraph summary of officer testimony from evidence flags and charges. For exhibit display.</summary>
        public string OfficerTestimonySummary;
        public List<Charge> Charges = new List<Charge>();

        public class Charge {
            internal Charge(string name, int fine, int? time) {
                Name = name;
                Fine = fine;
                Time = time;
            }

            internal Charge(string name, int fine, int? time, bool isArrestable) {
                Name = name;
                Fine = fine;
                Time = time;
                IsArrestable = isArrestable;
            }

            internal Charge(string name, int fine, int? time, bool isArrestable, int minDays, int? maxDays) {
                Name = name;
                Fine = fine;
                Time = time;
                IsArrestable = isArrestable;
                MinDays = minDays;
                MaxDays = maxDays;
            }

            public Charge() { }

            public string Name;
            public int Fine;
            public int? Time;
            /// <summary>Statutory minimum jail days for sentencing range. Used with MaxDays at resolution.</summary>
            public int MinDays;
            /// <summary>Statutory maximum jail days. Null = life sentence.</summary>
            public int? MaxDays;
            public bool? IsArrestable;
            /// <summary>0 Pending, 1 Convicted, 2 Acquitted, 3 Dismissed. Set at resolution.</summary>
            public int Outcome;
            /// <summary>Per-charge conviction chance at resolution (for display/log).</summary>
            public int? ConvictionChance;
            /// <summary>Actual sentence days applied (after range roll and multiplier) when convicted.</summary>
            public int? SentenceDaysServed;
        }

        public void AddCharge(Charge charge) {
            Charges.Add(charge);
        }
    }
}
