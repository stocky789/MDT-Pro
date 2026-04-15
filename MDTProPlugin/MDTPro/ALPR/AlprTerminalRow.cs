using MDTPro.Data;
using System;

namespace MDTPro.ALPR {
    internal sealed class AlprTerminalRow {
        internal AlprTerminalRow(string plateKey, ALPRHit hit, string sensorId, string compactSummary, DateTime utcNow) {
            PlateKey = plateKey;
            Hit = hit;
            SensorId = sensorId ?? "";
            CompactSummary = compactSummary ?? "";
            LastReadUtc = utcNow;
        }

        internal string PlateKey { get; }
        internal ALPRHit Hit { get; set; }
        internal string SensorId { get; set; }
        internal string CompactSummary { get; set; }
        internal DateTime LastReadUtc { get; set; }
    }
}
