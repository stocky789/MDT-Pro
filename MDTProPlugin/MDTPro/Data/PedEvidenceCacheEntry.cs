using System;

namespace MDTPro.Data {
    /// <summary>Persisted evidence context for a ped. Used for ped_evidence_cache table and loading into in-memory cache.</summary>
    public struct PedEvidenceCacheEntry {
        public string PedName;
        public DateTime CapturedAt;
        public bool HadWeapon;
        public bool WasWanted;
        public bool WasPatDown;
        public bool WasDrunk;
        public bool WasFleeing;
        public bool AssaultedPed;
        public bool DamagedVehicle;
        public bool HadIllegalWeapon;
        public bool ViolatedSupervision;
        public bool Resisted;
    }
}
