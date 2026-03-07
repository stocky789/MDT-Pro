namespace MDTPro.Data {
    /// <summary>Persistent record of items found during a vehicle search (PR SearchItemsAPI).</summary>
    public class VehicleSearchRecord {
        public int Id { get; set; }
        public string LicensePlate { get; set; }
        public string ItemType { get; set; }
        public string DrugType { get; set; }
        public string ItemLocation { get; set; }
        public string Description { get; set; }
        public uint WeaponModelHash { get; set; }
        public string WeaponModelId { get; set; }
        public string Source { get; set; }
        public string CapturedAt { get; set; }
    }
}
