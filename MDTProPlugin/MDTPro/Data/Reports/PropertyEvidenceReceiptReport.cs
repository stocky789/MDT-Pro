using System.Collections.Generic;

namespace MDTPro.Data.Reports {
    /// <summary>Property and Evidence Receipt — records seized contraband (drugs, firearms, other) from subject(s). Attachable to arrest reports for court evidence.</summary>
    public class PropertyEvidenceReceiptReport : Report {
        /// <summary>Person(s) from whom property/evidence was seized. Multiple subjects supported (e.g. joint seizure).</summary>
        public List<string> SubjectPedNames = new List<string>();
        /// <summary>Drugs seized: each entry has DrugType and Quantity (e.g. Baggie, Bundle, Grams). Aligned with Policing Redefined search item descriptions.</summary>
        public List<SeizedDrugEntry> SeizedDrugs = new List<SeizedDrugEntry>();
        /// <summary>Firearms seized. Each add creates an entry (duplicates allowed for multiple of same type).</summary>
        public List<string> SeizedFirearmTypes = new List<string>();
        /// <summary>Optional free-text notes for other contraband not covered by drug/firearm dropdowns.</summary>
        public string OtherContrabandNotes;

        /// <summary>Legacy: single subject. Used for backward compat and IsAttachedReportRelevantToCase. Prefer SubjectPedNames.</summary>
        public string SubjectPedName {
            get => SubjectPedNames != null && SubjectPedNames.Count > 0 ? SubjectPedNames[0] : null;
            set {
                if (SubjectPedNames == null) SubjectPedNames = new List<string>();
                if (!string.IsNullOrWhiteSpace(value)) {
                    if (SubjectPedNames.Count == 0) SubjectPedNames.Add(value);
                    else SubjectPedNames[0] = value;
                } else if (SubjectPedNames.Count > 0) SubjectPedNames.RemoveAt(0);
            }
        }

        /// <summary>Legacy: flat drug type list for ChargeSatisfiedBySeizedDrugs. Extracted from SeizedDrugs.</summary>
        public List<string> SeizedDrugTypes {
            get {
                if (SeizedDrugs == null || SeizedDrugs.Count == 0) return new List<string>();
                var types = new List<string>();
                foreach (var d in SeizedDrugs) {
                    if (!string.IsNullOrWhiteSpace(d?.DrugType)) types.Add(d.DrugType);
                }
                return types;
            }
        }

        public class SeizedDrugEntry {
            public string DrugType;
            public string Quantity;
        }
    }
}
