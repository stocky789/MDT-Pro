using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace MDTPro.Utility {
    /// <summary>
    /// Maps GTA V spawn model names (<see cref="MDTProVehicleData.ModelName"/>) to manufacturer and in-game English display names,
    /// using embedded data derived from the same game metadata community dumps use (aligned with sites such as GTABase).
    /// </summary>
    internal static class GtaVVehicleMakeModelCatalog {
        sealed class Row {
            [JsonProperty("Make")]
            public string Make { get; set; }
            [JsonProperty("Model")]
            public string Model { get; set; }
        }

        static readonly object LoadLock = new object();
        static Dictionary<string, Row> _bySpawnLower;
        static bool _loadAttempted;

        internal static void TryEnrichFromSpawnName(string modelSpawnName, Data.MDTProVehicleData vehicle) {
            if (vehicle == null || string.IsNullOrWhiteSpace(modelSpawnName)) return;
            EnsureLoaded();
            if (_bySpawnLower == null) return;
            var key = modelSpawnName.Trim().ToLowerInvariant();
            if (!_bySpawnLower.TryGetValue(key, out var row) || row == null) return;
            if (string.IsNullOrWhiteSpace(vehicle.Make) && !string.IsNullOrWhiteSpace(row.Make))
                vehicle.Make = row.Make.Trim();
            if (string.IsNullOrWhiteSpace(vehicle.Model) && !string.IsNullOrWhiteSpace(row.Model))
                vehicle.Model = row.Model.Trim();
        }

        static void EnsureLoaded() {
            if (_loadAttempted) return;
            lock (LoadLock) {
                if (_loadAttempted) return;
                _loadAttempted = true;
                try {
                    var asm = Assembly.GetExecutingAssembly();
                    var res = asm.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith("GtaVVehicleMakeModel.json", StringComparison.OrdinalIgnoreCase));
                    if (res == null) return;
                    using (var s = asm.GetManifestResourceStream(res))
                    using (var r = new StreamReader(s)) {
                        var json = r.ReadToEnd();
                        _bySpawnLower = JsonConvert.DeserializeObject<Dictionary<string, Row>>(json)
                            ?? new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
                    }
                } catch {
                    _bySpawnLower = null;
                }
            }
        }
    }
}
