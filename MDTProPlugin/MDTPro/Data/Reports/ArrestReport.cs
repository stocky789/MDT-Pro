using MDTPro.Setup;
using System.Collections.Generic;

namespace MDTPro.Data.Reports {
    public class ArrestReport : Report {
        public List<Charge> Charges;
        public string OffenderPedName;
        public string OffenderVehicleLicensePlate;
        public string CourtCaseNumber;
        public UseOfForceData UseOfForce;

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
