using System;
using System.Collections.Generic;
using System.Reflection;
using CommonDataFramework.Modules.PedDatabase;
using Rage;

namespace MDTPro.Utility {
    /// <summary>
    /// Resolves warrant / wanted reason text from Common Data Framework and LSPDFR Persona.
    /// MDT must not invent random charges when CDF only exposes <see cref="PedData.Wanted"/> as a bool.
    /// CDF 1.0.0.8 (the version we ship against) does not expose any warrant text — this resolver returns null
    /// in that case and Person Search shows only the Wanted pill, which is the intended fallback.
    /// Future CDF / LSPDFR releases may add a warrant string property; the named candidates list below covers
    /// the plausible names and avoids reflective scans for properties named "wanted*" / "warrant*" that could
    /// accidentally pick up unrelated metadata (e.g. WantedSince timestamps).
    /// </summary>
    internal static class CdfWarrantTextResolver {
        static readonly string[] CdfNamedStringCandidates = new[] {
            "WarrantText", "WarrantReason", "WantedReason", "WarrantDetails", "WantedDescription",
            "ActiveWarrant", "WarrantInformation", "WarrantInfo", "WarrantChargeDescription", "WarrantCharges",
        };

        /// <summary>When <paramref name="cdf"/> is wanted, reads any known warrant string from CDF then LSPDFR Persona; otherwise null.</summary>
        internal static string TryResolveWarrantText(PedData cdf, Ped holder) {
            if (cdf == null || !cdf.Wanted) return null;

            string s = TryReadNamedStringProperties(cdf, CdfNamedStringCandidates);
            if (!string.IsNullOrWhiteSpace(s)) return s.Trim();

            s = TryScanStringPropertiesForWarrantKeywords(cdf, excludePropertyNames: null);
            if (!string.IsNullOrWhiteSpace(s)) return s.Trim();

            object persona = TryGetPersona(holder);
            if (persona == null) return null;

            s = TryReadNamedStringProperties(persona, CdfNamedStringCandidates);
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        static object TryGetPersona(Ped holder) {
            if (holder == null || !holder.IsValid()) return null;
            try {
                return LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(holder);
            } catch {
                return null;
            }
        }

        static string TryReadNamedStringProperties(object target, string[] names) {
            if (target == null) return null;
            Type t = target.GetType();
            foreach (string name in names) {
                PropertyInfo pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (pi == null || pi.PropertyType != typeof(string)) continue;
                try {
                    string v = pi.GetValue(target) as string;
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                } catch { /* property throws */ }
            }
            return null;
        }

        static string TryScanStringPropertiesForWarrantKeywords(object target, string[] excludePropertyNames) {
            if (target == null) return null;
            var excludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (excludePropertyNames != null) {
                foreach (string n in excludePropertyNames)
                    if (!string.IsNullOrEmpty(n)) excludes.Add(n);
            }
            foreach (PropertyInfo pi in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if (pi.PropertyType != typeof(string)) continue;
                if (excludes.Contains(pi.Name)) continue;
                string n = pi.Name;
                if (n.Equals("Wanted", StringComparison.OrdinalIgnoreCase)) continue;
                if (n.IndexOf("warrant", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("wanted", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                try {
                    string v = pi.GetValue(target) as string;
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                } catch { }
            }
            return null;
        }
    }
}
