using MDTPro.Data;
using Rage;
using System;

namespace MDTPro.Utility {
    /// <summary>
    /// Person Search "ID photos" load FiveM's one-image-per-<b>model name</b> catalogue. That is only meaningful for human(oid) pedestrian models.
    /// When handles or records go wrong, garbage like <c>a_c_seagull</c> can be stored and the MDT shows a bird. We denylist obvious non-person archetypes.
    /// Addon humans should use names like <c>mp_m_freemode_01</c> / <c>a_m_y_*</c>; vanilla animals use <c>a_c_*</c>.
    /// </summary>
    internal static class PedPortraitModelHelper {
        /// <summary>Returns true if <paramref name="modelName"/> is safe to use for catalogue ID photos and portrait persistence.</summary>
        internal static bool IsSuitableForCatalogueIdPhoto(string modelName) {
            if (string.IsNullOrWhiteSpace(modelName)) return false;
            string n = modelName.Trim().ToLowerInvariant();
            if (n.Length < 3) return false;
            if (n == "null" || n == "undefined") return false;
            // GTA V ambient / scenario animals, birds, fish, etc.
            if (n.StartsWith("a_c_", StringComparison.OrdinalIgnoreCase)) return false;
            // World props incorrectly associated with a ped record (should not happen, cheap guard)
            if (n.StartsWith("prop_", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>When the live ped uses a non-catalogue model, do not assign portrait fields.</summary>
        internal static bool TryGetPortraitModelFromPed(Ped ped, out uint modelHash, out string modelName) {
            modelHash = 0;
            modelName = null;
            if (ped == null || !ped.IsValid()) return false;
            try {
                string mn = ped.Model.Name;
                if (!IsSuitableForCatalogueIdPhoto(mn)) return false;
                modelHash = (uint)ped.Model.Hash;
                modelName = mn;
                return true;
            } catch {
                return false;
            }
        }

        /// <summary>Apply portrait hash/name from <paramref name="ped"/> only when suitable; never overwrites with animals/props.</summary>
        internal static void AssignPortraitFromPedIfSuitable(Ped ped, MDTProPedData target) {
            if (target == null) return;
            if (!TryGetPortraitModelFromPed(ped, out uint h, out string n)) return;
            target.ModelHash = h;
            target.ModelName = n;
        }

        /// <summary>Clears junk persisted before this guard. Returns true if the row should be saved back to SQLite.</summary>
        internal static bool StripInvalidPortraitModelIfNeeded(MDTProPedData ped) {
            if (ped == null) return false;
            bool had = ped.ModelHash != 0 || !string.IsNullOrEmpty(ped.ModelName);
            if (!had) return false;
            if (IsSuitableForCatalogueIdPhoto(ped.ModelName)) return false;
            ped.ModelHash = 0;
            ped.ModelName = null;
            return true;
        }
    }
}
