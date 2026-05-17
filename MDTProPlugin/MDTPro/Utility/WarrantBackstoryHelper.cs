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
            return BuildActiveWarrants(null, minCount, maxCount);
        }

        internal static List<WarrantCharge> BuildActiveWarrants(string seedKey, int minCount = 1, int maxCount = 3) {
            var pool = GetWarrantableCharges();
            if (pool.Count == 0) return new List<WarrantCharge>();

            Random rng = string.IsNullOrWhiteSpace(seedKey) ? Rng : new Random(StableSeed(seedKey));
            int n = Math.Max(minCount, Math.Min(maxCount, rng.Next(minCount, maxCount + 1)));
            n = Math.Min(n, pool.Count);

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<WarrantCharge>();
            var shuffled = pool.OrderBy(_ => rng.Next()).ToList();

            string issued = BuildIssuedAt(seedKey, rng);
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

        static string BuildIssuedAt(string seedKey, Random rng) {
            if (string.IsNullOrWhiteSpace(seedKey)) return DateTime.UtcNow.ToString("o");
            var baseDate = new DateTime(2024, 1, 1, 9, 0, 0, DateTimeKind.Utc);
            return baseDate.AddDays(rng.Next(0, 730)).AddMinutes(rng.Next(0, 480)).ToString("o");
        }

        static int StableSeed(string value) {
            unchecked {
                const int offset = unchecked((int)2166136261);
                const int prime = 16777619;
                int hash = offset;
                foreach (char ch in value.Trim().ToUpperInvariant()) {
                    hash ^= ch;
                    hash *= prime;
                }
                return hash == int.MinValue ? int.MaxValue : Math.Abs(hash);
            }
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
