using MDTPro.Setup;
using System.Collections.Generic;

namespace MDTPro.Data.Reports {
    public class ArrestReport : Report {
        public List<Charge> Charges;
        public string OffenderPedName;
        public string OffenderVehicleLicensePlate;
        public string CourtCaseNumber;

        public class Charge : ArrestGroup.Charge {
            public bool addedByReportInEdit = false;
        }
    }
}
