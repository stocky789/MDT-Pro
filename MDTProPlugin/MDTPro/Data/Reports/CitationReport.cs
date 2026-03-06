using MDTPro.Setup;
using System.Collections.Generic;

namespace MDTPro.Data.Reports {
    public class CitationReport : Report {
        public List<Charge> Charges;
        public string OffenderPedName;
        public string OffenderVehicleLicensePlate;
        public string CourtCaseNumber;
        /// <summary>Total fine amount when the citation was closed (set when status becomes Closed).</summary>
        public int? FinalAmount;
        
        public class Charge : CitationGroup.Charge {
            public bool addedByReportInEdit = false;
        }
    }
}
