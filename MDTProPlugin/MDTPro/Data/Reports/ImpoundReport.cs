namespace MDTPro.Data.Reports {
    /// <summary>Impound report. Notes (remarks) are inherited from <see cref="Report.Notes"/>.</summary>
    public class ImpoundReport : Report {
        public string LicensePlate;
        public string VehicleModel;
        public string Owner;
        public string Vin;
        public string ImpoundReason;
        public string TowCompany;
        public string ImpoundLot;
    }
}
