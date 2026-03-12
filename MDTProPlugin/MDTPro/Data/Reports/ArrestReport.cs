using MDTPro.Setup;
using System.Collections.Generic;

namespace MDTPro.Data.Reports {
    public class ArrestReport : Report {
        public List<Charge> Charges;
        public string OffenderPedName;
        public string OffenderVehicleLicensePlate;
        public string CourtCaseNumber;
        public UseOfForceData UseOfForce;
        /// <summary>Officer documented that drugs were found during this arrest. Used for court evidence when in-game/PR capture did not fire.</summary>
        public bool DocumentedDrugs;
        /// <summary>Officer documented that firearm(s) were found during this arrest. Used for court evidence when in-game/PR capture did not fire.</summary>
        public bool DocumentedFirearms;
        /// <summary>Report IDs attached as evidence (incident, injury, citation). Editable only while Status == Pending.</summary>
        public List<string> AttachedReportIds = new List<string>();

        public class Charge : ArrestGroup.Charge {
            public bool addedByReportInEdit = false;
        }

        public class UseOfForceData {
            public string Type; // Taser, Baton, Fist, Firearm, Other
            public string TypeOther;
            public string Justification;
            public bool InjuryToSuspect;
            public bool InjuryToOfficer;
            public string Witnesses;
        }
    }
}
