namespace MDTPro.Data {
    /// <summary>Persistent firearm record from PR search items (pat-down, dead body search).</summary>
    public class FirearmRecord {
        public int Id { get; set; }
        public string SerialNumber { get; set; }
        public string OwnerPedName { get; set; }
        public string WeaponModelId { get; set; }
        /// <summary>In-game display name from native (matches what player sees). API-authoritative.</summary>
        public string WeaponDisplayName { get; set; }
        public uint WeaponModelHash { get; set; }
        public bool IsStolen { get; set; }
        public string Description { get; set; }
        public string Source { get; set; }
        public string FirstSeenAt { get; set; }
        public string LastSeenAt { get; set; }
    }
}
