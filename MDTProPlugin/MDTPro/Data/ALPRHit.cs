using System;
using System.Collections.Generic;

namespace MDTPro.Data {
    /// <summary>
    /// Represents a single ALPR scan hit with flags (e.g. no insurance, stolen).
    /// </summary>
    public class ALPRHit {
        public string Plate { get; set; }
        public string Owner { get; set; }
        public string ModelDisplayName { get; set; }
        /// <summary>Vehicle color for display (e.g. "Black / White").</summary>
        public string VehicleColor { get; set; }
        public List<string> Flags { get; set; } = new List<string>();
        /// <summary>When the hit was produced. ALPR paths set this in UTC (<see cref="DateTimeKind.Utc"/>) so it matches session pruning and web serialization.</summary>
        public DateTime TimeScanned { get; set; }

        public bool HasFlags => Flags != null && Flags.Count > 0;
    }
}
