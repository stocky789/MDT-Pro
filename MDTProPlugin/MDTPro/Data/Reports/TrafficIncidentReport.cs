using System.Collections.Generic;

namespace MDTPro.Data.Reports {
    public class TrafficIncidentReport : Report {
        public string[] DriverNames;
        public string[] PassengerNames;
        public string[] PedestrianNames;
        public string[] VehiclePlates;
        public string[] VehicleModels;
        public bool InjuryReported;
        public string InjuryDetails;
        public string CollisionType;
    }
}
