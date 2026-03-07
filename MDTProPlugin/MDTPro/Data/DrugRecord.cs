namespace MDTPro.Data {
    /// <summary>Persistent drug record from PR search items (pat-down, dead body search).</summary>
    public class DrugRecord {
        public int Id { get; set; }
        public string OwnerPedName { get; set; }
        public string DrugType { get; set; }
        public string DrugCategory { get; set; }
        public string Description { get; set; }
        public string Source { get; set; }
        public string FirstSeenAt { get; set; }
        public string LastSeenAt { get; set; }
    }
}
