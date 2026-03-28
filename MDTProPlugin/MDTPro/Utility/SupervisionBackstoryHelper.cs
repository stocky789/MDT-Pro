using MDTPro.Setup;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MDTPro.Utility {
    /// <summary>
    /// Builds coherent prior arrest charge lists for peds on CDF probation/parole using real entries from arrestOptions.json
    /// (weighted by statutory range and probation likelihood). Avoids hand-maintained charge "sets" that drift when options change.
    /// </summary>
    internal static class SupervisionBackstoryHelper {
        private static readonly Random Rng = new Random();

        internal static List<ArrestGroup.Charge> GetAllArrestCharges() {
            var list = new List<ArrestGroup.Charge>();
            foreach (var group in SetupController.GetArrestOptions() ?? Enumerable.Empty<ArrestGroup>()) {
                if (group?.charges == null) continue;
                foreach (var c in group.charges) {
                    if (c == null || string.IsNullOrWhiteSpace(c.name)) continue;
                    list.Add(c);
                }
            }
            return list;
        }

        /// <summary>Case-insensitive lookup for syncing ped_arrests from an existing synthetic court case.</summary>
        internal static ArrestGroup.Charge FindArrestChargeTemplate(string chargeName) {
            if (string.IsNullOrWhiteSpace(chargeName)) return null;
            foreach (var group in SetupController.GetArrestOptions() ?? Enumerable.Empty<ArrestGroup>()) {
                if (group?.charges == null) continue;
                var hit = group.charges.FirstOrDefault(c => c != null && string.Equals(c.name, chargeName, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return CloneCharge(hit);
            }
            return new ArrestGroup.Charge {
                name = chargeName.Trim(),
                minFine = 0,
                maxFine = 500,
                canRevokeLicense = false,
                isArrestable = true,
                minDays = 0,
                maxDays = 90,
                probation = 0.4f,
                canBeWarrant = false
            };
        }

        internal static ArrestGroup.Charge CloneCharge(ArrestGroup.Charge c) {
            if (c == null) return null;
            return new ArrestGroup.Charge {
                name = c.name,
                minFine = c.minFine,
                maxFine = c.maxFine,
                canRevokeLicense = c.canRevokeLicense,
                isArrestable = c.isArrestable,
                minDays = c.minDays,
                maxDays = c.maxDays,
                probation = c.probation,
                canBeWarrant = c.canBeWarrant
            };
        }

        /// <summary>Guaranteed at least 2 distinct charges when the pool allows (parole biases toward custody ranges).</summary>
        internal static List<ArrestGroup.Charge> BuildCoherentSupervisionCharges(bool isParole) {
            var pool = GetAllArrestCharges();
            if (pool.Count == 0) return new List<ArrestGroup.Charge>();

            var primaryPool = isParole ? FilterParolePrimaryPool(pool) : pool;
            if (primaryPool.Count == 0) primaryPool = pool;

            var primary = WeightedPick(primaryPool, c => PrimaryWeight(c, isParole));
            var result = new List<ArrestGroup.Charge> { CloneCharge(primary) };

            int extrasTarget = Rng.Next(1, 4); // 1–3 additional (2–4 total)
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primary.name };
            var secondaryPool = pool.Where(c => c != null && !used.Contains(c.name)).ToList();

            for (int i = 0; i < extrasTarget && secondaryPool.Count > 0; i++) {
                var pick = WeightedPick(secondaryPool, SecondaryWeight);
                result.Add(CloneCharge(pick));
                used.Add(pick.name);
                secondaryPool = secondaryPool.Where(c => !used.Contains(c.name)).ToList();
            }

            if (result.Count < 2 && pool.Any(c => c != null && !used.Contains(c.name))) {
                var fallback = pool.First(c => c != null && !used.Contains(c.name));
                result.Add(CloneCharge(fallback));
            }

            return result;
        }

        private static List<ArrestGroup.Charge> FilterParolePrimaryPool(List<ArrestGroup.Charge> pool) {
            int MaxCap(ArrestGroup.Charge c) => c.maxDays ?? c.minDays;
            return pool.Where(c => {
                if (c == null) return false;
                if (c.minDays >= 90) return true;
                if (MaxCap(c) >= 180) return true;
                string n = (c.name ?? "").ToLowerInvariant();
                return n.Contains("murder") || n.Contains("manslaughter") || n.Contains("kidnapping")
                    || n.Contains("robbery") || n.Contains("assault with") || n.Contains("rape")
                    || n.Contains("sexual assault") || n.Contains("voluntary manslaughter");
            }).ToList();
        }

        private static float PrimaryWeight(ArrestGroup.Charge c, bool isParole) {
            int maxCap = c.maxDays ?? c.minDays;
            float custody = Math.Max(c.minDays, maxCap);
            float w = 0.35f + c.probation * 3.5f;
            if (custody >= 365) w += 6f;
            else if (custody >= 90) w += 3f;
            else if (custody >= 30) w += 1.5f;
            if (isParole) w += custody * 0.02f;
            return Math.Max(0.05f, w);
        }

        private static float SecondaryWeight(ArrestGroup.Charge c) {
            int maxCap = c.maxDays ?? c.minDays;
            float custody = Math.Max(c.minDays, maxCap);
            float w = 0.4f + c.probation * 2.5f;
            if (custody <= 180) w += 1.2f;
            string n = (c.name ?? "").ToLowerInvariant();
            if (n.Contains("elud") || n.Contains("evad") || n.Contains("reckless") || n.Contains("dui")
                || n.Contains("controlled substance") || n.Contains("resist")) w += 0.8f;
            return Math.Max(0.05f, w);
        }

        private static ArrestGroup.Charge WeightedPick(List<ArrestGroup.Charge> list, Func<ArrestGroup.Charge, float> weightFn) {
            float total = list.Sum(c => weightFn(c));
            float r = (float)(Rng.NextDouble() * total);
            foreach (var c in list) {
                r -= weightFn(c);
                if (r <= 0) return c;
            }
            return list[list.Count - 1];
        }
    }
}
