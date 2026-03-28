// Rare post-citation suspect aggression (melee or firearm) via TASK_COMBAT_PED. Game thread only.
using MDTPro;
using MDTPro.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using MDTPro.Setup;
using Rage;
using Rage.Native;
using static MDTPro.Setup.SetupController;

namespace MDTPro.Utility {
    internal static class CitationPostHandoffViolenceHelper {
        private const uint UnarmedHash = 0xA2719263u;
        /// <summary>GTA weapon type groups that use gunplay (GET_WEAPONTYPE_GROUP).</summary>
        private static readonly int[] FirearmWeaponGroups = {
            416676503,           // pistol
            unchecked((int)3337201093u), // SMG
            860033945,           // shotgun
            970310034,           // assault rifle
            1159398588,          // MG
            unchecked((int)3082541095u), // heavy
            unchecked((int)3337704109u), // sniper
        };

        private static uint _cooldownUntilGameTime;

        /// <summary>After a successful citation handoff, rarely make the suspect attack the player. Game thread only.</summary>
        internal static void TryMaybeAggressiveAfterCitation(Ped suspect, IEnumerable<PRHelper.CitationHandoutCharge> charges) {
            try {
                var cfg = GetConfig();
                if (cfg?.citationPostHandoffViolenceEnabled != true) return;

                Ped player = Main.Player;
                if (suspect == null || !suspect.IsValid() || player == null || !player.IsValid()) return;
                if (suspect.IsPlayer) return;
                if (suspect.IsDead || suspect.IsInjured) return;
                if (player.IsDead) return;

                if (cfg.citationPostHandoffViolenceMaleOnly && !IsEligibleMaleSuspectForViolence(suspect))
                    return;

                int cdMs = cfg.citationPostHandoffViolenceCooldownMs;
                if (cdMs < 0) cdMs = 0;
                if (cdMs > 0 && Game.GameTime < _cooldownUntilGameTime) return;

                var list = charges?.Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name)).ToList();
                if (list == null || list.Count == 0) return;

                float p = ComputeChance(cfg, list, suspect);
                if (p <= 0f) return;
                float maxC = cfg.citationPostHandoffViolenceMaxChance;
                if (maxC > 0f && p > maxC) p = maxC;

                int roll = Helper.GetRandomInt(0, 9999);
                if (roll >= p * 10000f) return;

                uint gun = TryGetFirearmHash(suspect, cfg);
                bool wantsShoot = gun != 0
                    && Helper.GetRandomInt(0, 9999) < cfg.citationPostHandoffViolenceShootWhenArmedChance * 10000f;

                ApplyCombat(suspect, player, wantsShoot, gun);

                if (cdMs > 0)
                    _cooldownUntilGameTime = Game.GameTime + (uint)cdMs;

                string warn = GetLanguage()?.inGame?.citationPostHandoffViolenceNotify;
                if (!string.IsNullOrWhiteSpace(warn))
                    RageNotification.Show(warn, RageNotification.NotificationType.Error);
            } catch {
                /* ignore */
            }
        }

        private static float ComputeChance(Config cfg, List<PRHelper.CitationHandoutCharge> list, Ped suspect) {
            float p = cfg.citationPostHandoffViolenceBaseChance;
            if (p <= 0f) return 0f;

            int totalFine = 0;
            foreach (var c in list) {
                if (c.Fine > 0) totalFine += c.Fine;
            }
            float fineExtra = totalFine * cfg.citationPostHandoffViolenceFinePerDollar;
            float fineCap = cfg.citationPostHandoffViolenceFineBonusCap;
            if (fineCap > 0f && fineExtra > fineCap) fineExtra = fineCap;
            p += fineExtra;

            if (list.Any(c => c.IsArrestable))
                p += cfg.citationPostHandoffViolenceArrestableBonus;

            string blob = string.Join(" ", list.Select(c => c.Name)).ToLowerInvariant();
            if (blob.IndexOf("assault", StringComparison.Ordinal) >= 0
                || blob.IndexOf("resist", StringComparison.Ordinal) >= 0
                || blob.IndexOf("battery", StringComparison.Ordinal) >= 0
                || blob.IndexOf("disorderly", StringComparison.Ordinal) >= 0) {
                p += cfg.citationPostHandoffViolenceHostileChargeBonus;
            }

            try {
                if (NativeFunction.Natives.IS_PED_DRUNK<bool>(suspect))
                    p += cfg.citationPostHandoffViolenceDrunkBonus;
            } catch { /* native missing */ }

            return p;
        }

        /// <summary>CDF gender from person data when set; otherwise the ped model&apos;s male flag.</summary>
        private static bool IsEligibleMaleSuspectForViolence(Ped ped) {
            try {
                MDTProPedData data = DataController.GetPedDataForPed(ped);
                if (data != null && !string.IsNullOrWhiteSpace(data.Gender)) {
                    string g = data.Gender.Trim().ToLowerInvariant();
                    if (g.StartsWith("female", StringComparison.Ordinal) || g.Equals("f", StringComparison.Ordinal))
                        return false;
                    if (g.StartsWith("male", StringComparison.Ordinal) || g.Equals("m", StringComparison.Ordinal))
                        return true;
                }
            } catch {
                /* ignore */
            }
            try {
                return ped.IsMale;
            } catch {
                return false;
            }
        }

        private static uint TryGetFirearmHash(Ped ped, Config cfg) {
            uint h;

            if (cfg.citationPostHandoffViolenceTryCdfWeapon && DataController.TryGetCdfPedDataFirearmHash(ped, out h) && h != 0u)
                return h;

            if (cfg.citationPostHandoffViolenceTryPedSearchItemsWeapon) {
                h = DataController.TryGetPedSearchItemsFirearmHash(ped);
                if (h != 0u) return h;
            }

            return TryGetFirearmHashNative(ped);
        }

        private static uint TryGetFirearmHashNative(Ped ped) {
            try {
                uint best = NativeFunction.Natives.GET_BEST_PED_WEAPON<uint>(ped, true);
                if (best != UnarmedHash && IsFirearmGroup(NativeFunction.Natives.GET_WEAPONTYPE_GROUP<int>(best)))
                    return best;
            } catch {
                return 0;
            }

            try {
                foreach (WeaponDescriptor w in ped.Inventory.Weapons) {
                    if (w == null) continue;
                    uint h = (uint)w.Hash;
                    if (h == UnarmedHash) continue;
                    if (IsFirearmGroup(NativeFunction.Natives.GET_WEAPONTYPE_GROUP<int>(h)))
                        return h;
                }
            } catch {
                /* ignore */
            }

            return 0;
        }

        private static bool IsFirearmGroup(int group) {
            for (int i = 0; i < FirearmWeaponGroups.Length; i++) {
                if (FirearmWeaponGroups[i] == group) return true;
            }
            return false;
        }

        private static void ApplyCombat(Ped suspect, Ped player, bool equipGun, uint gunHash) {
            try {
                player.Tasks.ClearImmediately();
            } catch {
                try {
                    NativeFunction.Natives.CLEAR_PED_TASKS_IMMEDIATELY(player);
                } catch {
                    /* ignore */
                }
            }

            NativeFunction.Natives.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS(suspect, false);
            NativeFunction.Natives.SET_PED_KEEP_TASK(suspect, true);

            TryExitVehicleForCombat(suspect);

            if (equipGun && gunHash != 0) {
                try {
                    NativeFunction.Natives.SET_CURRENT_PED_WEAPON(suspect, gunHash, true);
                } catch {
                    /* ignore */
                }
            }

            // Standard combat task; ped uses equipped weapon or melee.
            NativeFunction.Natives.TASK_COMBAT_PED(suspect, player, 0, 16);
        }

        /// <summary>Drivers/passengers rarely fight effectively while seated; leave first when stopped (traffic-style citation).</summary>
        private static void TryExitVehicleForCombat(Ped suspect) {
            try {
                if (suspect == null || !suspect.IsValid() || !suspect.IsInAnyVehicle(false)) return;
                Vehicle v = suspect.CurrentVehicle;
                if (v == null || !v.Exists()) return;
                suspect.Tasks.LeaveVehicle(LeaveVehicleFlags.None);
                for (int i = 0; i < 120; i++) {
                    GameFiber.Yield();
                    if (!suspect.IsValid() || !suspect.IsInAnyVehicle(false)) break;
                }
            } catch {
                /* ignore */
            }
        }
    }
}
