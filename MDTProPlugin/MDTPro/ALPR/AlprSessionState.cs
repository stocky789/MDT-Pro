using MDTPro.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MDTPro.ALPR {
    /// <summary>In-memory ALPR terminal history and per-plate dedupe (game fiber only).</summary>
    internal sealed class AlprSessionState {
        private readonly Dictionary<string, AlprTerminalRow> _byPlate = new Dictionary<string, AlprTerminalRow>(StringComparer.OrdinalIgnoreCase);
        private readonly List<AlprTerminalRow> _order = new List<AlprTerminalRow>();

        internal void Clear() {
            _byPlate.Clear();
            _order.Clear();
        }

        internal void Prune(int maxRows, int historyMinutes) {
            if (maxRows <= 0) maxRows = 8;
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-System.Math.Max(1, historyMinutes));
            for (int i = _order.Count - 1; i >= 0; i--) {
                if (_order[i].LastReadUtc < cutoff) {
                    var row = _order[i];
                    _order.RemoveAt(i);
                    _byPlate.Remove(row.PlateKey);
                }
            }
            while (_order.Count > maxRows) {
                var oldest = _order[_order.Count - 1];
                _order.RemoveAt(_order.Count - 1);
                _byPlate.Remove(oldest.PlateKey);
            }
        }

        /// <summary>Upsert a row for this plate after a good sensor read.</summary>
        internal void RecordRead(string plateKey, ALPRHit hit, string sensorId, string compactSummary) {
            if (string.IsNullOrEmpty(plateKey) || hit == null) return;
            var now = DateTime.UtcNow;
            if (_byPlate.TryGetValue(plateKey, out AlprTerminalRow existing)) {
                existing.Hit = hit;
                existing.SensorId = sensorId ?? "";
                existing.CompactSummary = compactSummary ?? "";
                existing.LastReadUtc = now;
                _order.Remove(existing);
                _order.Insert(0, existing);
                return;
            }
            var row = new AlprTerminalRow(plateKey, hit, sensorId, compactSummary, now);
            _byPlate[plateKey] = row;
            _order.Insert(0, row);
        }

        internal IReadOnlyList<AlprTerminalRow> GetRowsSnapshot(int maxRows) {
            if (maxRows <= 0) maxRows = 8;
            return _order.Take(maxRows).ToList();
        }

        internal bool HasPlate(string plateKey) {
            return !string.IsNullOrEmpty(plateKey) && _byPlate.ContainsKey(plateKey);
        }
    }
}
