using MDTPro.Data;
using MDTPro.Setup;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MDTPro.Utility {
    /// <summary>Builds coherent active warrant lines from <c>arrestOptions.json</c> (<c>canBeWarrant</c> charges only).</summary>
    internal static class WarrantBackstoryHelper {
        static readonly Random Rng = new Random();

        internal static List<WarrantCharge> BuildActiveWarrants(int minCount = 1, int maxCount = 3) {
            var pool = GetWarrantableCharges();
            if (pool.Count == 0) return new List<WarrantCharge>();

            int n = Math.Max(minCount, Math.Min(maxCount, Rng.Next(minCount, maxCount + 1)));
            n = Math.Min(n, pool.Count);

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<WarrantCharge>();
            var shuffled = pool.OrderBy(_ => Rng.Next()).ToList();

            string issued = DateTime.UtcNow.ToString("o");
            foreach (var c in shuffled) {
                if (c == null || string.IsNullOrWhiteSpace(c.name) || !used.Add(c.name)) continue;
                result.Add(new WarrantCharge {
                    Name = c.name.Trim(),
                    Severity = ClassifySeverity(c),
                    IssuedAtUtc = issued
                });
                if (result.Count >= n) break;
            }

            return result;
        }

        static List<ArrestGroup.Charge> GetWarrantableCharges() {
            var list = new List<ArrestGroup.Charge>();
            foreach (var group in SetupController.GetArrestOptions() ?? Enumerable.Empty<ArrestGroup>()) {
                if (group?.charges == null) continue;
                foreach (var c in group.charges) {
                    if (c != null && c.canBeWarrant && !string.IsNullOrWhiteSpace(c.name))
                        list.Add(c);
                }
            }
            return list;
        }

        static string ClassifySeverity(ArrestGroup.Charge c) {
            int maxCap = c.maxDays ?? c.minDays;
            int custody = Math.Max(c.minDays, maxCap);
            return custody >= 365 ? "Felony" : "Misdemeanor";
        }
    }
}
