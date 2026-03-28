// Pending StopThePed-path citation delivery: closed in MDT first, handed in-game via keybind + menu (see CitationHandoffKeybind).
using System;
using System.Collections.Generic;
using System.Linq;
using MDTPro.Setup;
using MDTPro.UI;
using Rage;
using static MDTPro.Setup.SetupController;

namespace MDTPro.Utility {
    internal static class StpCitationHandoffQueue {
        private static readonly object Sync = new object();
        private static string _pedName;
        private static List<PRHelper.CitationHandoutCharge> _charges;
        private static DateTime _queuedUtc;
        private static ulong _queueToken;

        internal static void Clear() {
            lock (Sync) {
                _pedName = null;
                _charges = null;
                unchecked { _queueToken++; }
            }
        }

        internal static void Enqueue(string pedName, List<PRHelper.CitationHandoutCharge> charges) {
            if (Main.usePR) return;
            if (string.IsNullOrWhiteSpace(pedName) || charges == null || charges.Count == 0) return;
            lock (Sync) {
                unchecked { _queueToken++; }
                _pedName = pedName.Trim();
                _charges = charges
                    .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name))
                    .Select(c => new PRHelper.CitationHandoutCharge {
                        Name = c.Name,
                        Fine = c.Fine,
                        IsArrestable = c.IsArrestable,
                    })
                    .ToList();
                _queuedUtc = DateTime.UtcNow;
            }
        }

        private static bool IsExpiredUnlocked() {
            int minutes = GetConfig()?.stpCitationHandoffPendingExpireMinutes ?? 45;
            if (minutes <= 0) return false;
            return (DateTime.UtcNow - _queuedUtc).TotalMinutes > minutes;
        }

        /// <summary>Game-thread only. Opens the handoff menu when a pending citation exists, the key was pressed, distance is OK, and the ped resolves.</summary>
        internal static void TryProcessKeyPress() {
            if (Main.usePR || !ModIntegration.StpPluginLoaded) return; // PR handles citations via PedAPI; no MDT handoff menu

            string pedName;
            List<PRHelper.CitationHandoutCharge> chargesCopy;
            ulong tokenAtStart;
            lock (Sync) {
                if (string.IsNullOrWhiteSpace(_pedName) || _charges == null || _charges.Count == 0)
                    return;
                if (IsExpiredUnlocked()) {
                    _pedName = null;
                    _charges = null;
                    unchecked { _queueToken++; }
                    string expired = GetLanguage().inGame.stpCitationHandoffPendingExpired
                        ?? "The pending citation handoff expired. Close the citation again from the MDT if needed.";
                    if (!string.IsNullOrWhiteSpace(expired))
                        RageNotification.Show(expired, RageNotification.NotificationType.Info);
                    return;
                }
                tokenAtStart = _queueToken;
                pedName = _pedName;
                chargesCopy = _charges.Select(c => new PRHelper.CitationHandoutCharge {
                    Name = c.Name,
                    Fine = c.Fine,
                    IsArrestable = c.IsArrestable,
                }).ToList();
            }

            if (!PRHelper.TryGetCitationPedHandle(pedName, out Rage.PoolHandle handle, out _))
                return;

            Ped ped = null;
            try { ped = World.GetEntityByHandle<Ped>(handle); } catch { }
            if (ped == null || !ped.IsValid()) {
                string msg = GetLanguage().inGame.handCitationPersonNotPresent;
                if (!string.IsNullOrWhiteSpace(msg))
                    RageNotification.Show(msg, RageNotification.NotificationType.Info);
                return;
            }

            Ped player = Game.LocalPlayer.Character;
            if (player == null || !player.IsValid()) return;

            float maxD = GetConfig()?.stpCitationHandoffMaxDistance ?? 4f;
            if (maxD < 1.5f) maxD = 1.5f;
            float dist;
            try {
                dist = player.DistanceTo(ped);
            } catch {
                return;
            }
            if (dist > maxD) {
                string fmt = GetLanguage().inGame.stpCitationHandoffTooFar
                    ?? "Move closer to the suspect (within ~{0}m) to hand the citation.";
                if (!string.IsNullOrWhiteSpace(fmt))
                    RageNotification.Show(string.Format(fmt, Math.Round(maxD, 1)), RageNotification.NotificationType.Info);
                return;
            }

            bool delivered = CitationHandoffMenu.TryShowModal(ped, pedName, chargesCopy);
            if (!delivered) return;

            lock (Sync) {
                if (_queueToken == tokenAtStart) {
                    _pedName = null;
                    _charges = null;
                    unchecked { _queueToken++; }
                }
            }

            string okMsg = string.Format(GetLanguage().inGame.handCitationTo ?? "Hand citation to {0}", pedName);
            if (!string.IsNullOrWhiteSpace(okMsg))
                RageNotification.ShowSuccess(okMsg);

            CitationHandoffPostEffects.ScheduleAfterHandoff(ped, chargesCopy, includeStopThePedPaperworkAnimation: true, pedName);
        }
    }
}
