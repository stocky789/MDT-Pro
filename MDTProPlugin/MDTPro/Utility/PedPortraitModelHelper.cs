using MDTPro.Data;
using Rage;
using Rage.Native;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MDTPro.Utility {
    /// <summary>
    /// Person Search ID photos resolve to bundled <c>/image/peds/{model}.webp</c> or <c>{model}__{drawable}_{texture}.webp</c>.
    /// We persist <b>hair</b> (component 2) and <b>face</b> (component 0) drawable/texture; clients try both filename pairs because community catalogue sources often key
    /// <c>[model][d][t]</c> to one slot or the other.
    /// Live reads use <see cref="Rage.Ped.Model"/> spawn names (StopThePed license flow); SQLite may still hold legacy bracket keys from older tooling — normalize on load.
    /// </summary>
    internal static class PedPortraitModelHelper {
        internal const int PortraitHairComponentId = 2;
        internal const int PortraitFaceComponentId = 0;

        /// <summary>ReportsPlus / community catalogue style: <c>[a_m_y_cop_01][4][2]</c> (spawn + face or hair slot indices).</summary>
        static readonly Regex BracketPortraitRegex = new Regex(
            @"^\s*\[([^\]]+)\]\s*\[(\d+)\]\s*\[(\d+)\]\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Returns true if <paramref name="modelName"/> is safe to use for catalogue ID photos and portrait persistence.</summary>
        internal static bool IsSuitableForCatalogueIdPhoto(string modelName) {
            if (string.IsNullOrWhiteSpace(modelName)) return false;
            string n = modelName.Trim().ToLowerInvariant();
            if (n.Length < 3) return false;
            if (n == "null" || n == "undefined") return false;
            if (n.StartsWith("a_c_", StringComparison.OrdinalIgnoreCase)) return false;
            if (n.StartsWith("prop_", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>Maps stored or API model strings to the lowercase spawn name used under <c>images/peds/</c>.</summary>
        internal static string CanonCatalogueSpawnName(string modelNameRaw) {
            if (string.IsNullOrWhiteSpace(modelNameRaw)) return null;
            string t = modelNameRaw.Trim();
            Match m = BracketPortraitRegex.Match(t);
            if (m.Success)
                return m.Groups[1].Value.Trim().ToLowerInvariant();
            return t.ToLowerInvariant();
        }

        internal static bool TryParseBracketCatalogueKey(string raw, out string spawnLower, out int drawable, out int texture) {
            spawnLower = null;
            drawable = texture = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            Match m = BracketPortraitRegex.Match(raw.Trim());
            if (!m.Success) return false;
            spawnLower = m.Groups[1].Value.Trim().ToLowerInvariant();
            if (!IsSuitableForCatalogueIdPhoto(spawnLower)) return false;
            drawable = int.Parse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            texture = int.Parse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (drawable < 0 || texture < 0) return false;
            return true;
        }

        /// <summary>Fix legacy SQLite rows: bracket <c>ModelName</c> → spawn + optional face pair when portrait columns were empty.</summary>
        internal static bool NormalizeStoredPortraitRow(MDTProPedData ped) {
            if (ped == null) return false;
            bool changed = false;
            if (!string.IsNullOrEmpty(ped.ModelName) && TryParseBracketCatalogueKey(ped.ModelName, out string spawn, out int bd, out int bt)) {
                if (!string.Equals(ped.ModelName, spawn, StringComparison.Ordinal)) {
                    ped.ModelName = spawn;
                    changed = true;
                }
                bool noPortrait = ped.PortraitVariantDrawable == null && ped.PortraitVariantTexture == null
                    && ped.PortraitFaceDrawable == null && ped.PortraitFaceTexture == null;
                if (noPortrait) {
                    ped.PortraitFaceDrawable = bd;
                    ped.PortraitFaceTexture = bt;
                    changed = true;
                }
            } else if (!string.IsNullOrEmpty(ped.ModelName)) {
                string lower = ped.ModelName.Trim().ToLowerInvariant();
                if (!string.Equals(ped.ModelName, lower, StringComparison.Ordinal)) {
                    ped.ModelName = lower;
                    changed = true;
                }
            }
            return changed;
        }

        internal static bool TryGetPortraitModelFromPed(Ped ped, out uint modelHash, out string modelName) {
            modelHash = 0;
            modelName = null;
            if (ped == null || !ped.IsValid()) return false;
            try {
                string mn = CanonCatalogueSpawnName(ped.Model.Name);
                if (string.IsNullOrEmpty(mn) || !IsSuitableForCatalogueIdPhoto(mn)) return false;
                modelHash = (uint)ped.Model.Hash;
                modelName = mn;
                return true;
            } catch {
                return false;
            }
        }

        internal static bool TryGetComponentVariationFromPed(Ped ped, int componentId, out int drawable, out int texture) {
            drawable = 0;
            texture = 0;
            if (ped == null || !ped.IsValid()) return false;
            try {
                drawable = NativeFunction.Natives.GET_PED_DRAWABLE_VARIATION<int>(ped, componentId);
                texture = NativeFunction.Natives.GET_PED_TEXTURE_VARIATION<int>(ped, componentId);
                if (drawable < 0) drawable = 0;
                if (texture < 0) texture = 0;
                return true;
            } catch {
                return false;
            }
        }

        internal static void AssignPortraitVariationFromPed(Ped ped, MDTProPedData target) {
            if (target == null) return;
            if (ped == null || !ped.IsValid()) {
                target.PortraitVariantDrawable = null;
                target.PortraitVariantTexture = null;
                target.PortraitFaceDrawable = null;
                target.PortraitFaceTexture = null;
                return;
            }
            if (TryGetComponentVariationFromPed(ped, PortraitHairComponentId, out int hd, out int ht)) {
                target.PortraitVariantDrawable = hd;
                target.PortraitVariantTexture = ht;
            } else {
                target.PortraitVariantDrawable = null;
                target.PortraitVariantTexture = null;
            }
            if (TryGetComponentVariationFromPed(ped, PortraitFaceComponentId, out int fd, out int ft)) {
                target.PortraitFaceDrawable = fd;
                target.PortraitFaceTexture = ft;
            } else {
                target.PortraitFaceDrawable = null;
                target.PortraitFaceTexture = null;
            }
        }

        internal static void AssignPortraitFromPedIfSuitable(Ped ped, MDTProPedData target) {
            if (target == null) return;
            if (!TryGetPortraitModelFromPed(ped, out uint h, out string n)) return;
            target.ModelHash = h;
            target.ModelName = n;
            AssignPortraitVariationFromPed(ped, target);
        }

        internal static bool StripInvalidPortraitModelIfNeeded(MDTProPedData ped) {
            if (ped == null) return false;
            bool had = ped.ModelHash != 0 || !string.IsNullOrEmpty(ped.ModelName);
            if (!had) return false;
            string spawn = CanonCatalogueSpawnName(ped.ModelName ?? "");
            if (IsSuitableForCatalogueIdPhoto(spawn)) return false;
            ped.ModelHash = 0;
            ped.ModelName = null;
            ped.PortraitVariantDrawable = null;
            ped.PortraitVariantTexture = null;
            ped.PortraitFaceDrawable = null;
            ped.PortraitFaceTexture = null;
            return true;
        }
    }
}
