// Policing Redefined integration (citations, ped menu).
// API: GiveCitationToPed(Ped, Citation) — https://policing-redefined.netlify.app/docs/developer-docs/pr/ped-api/ped-detain-resist
// Docs require the Citation's Ped to match the GiveCitationToPed ped parameter; we use the same ped for both.
// Must run on game thread: PR API and ped menu expect game-thread execution.
using MDTPro.Data;
using MDTPro.Setup;
using PolicingRedefined.Interaction.Assets.PedAttributes;
using Rage;
using System.Collections.Generic;
using System.Linq;


namespace MDTPro.Utility {
    internal class PRHelper {
        internal class CitationHandoutCharge {
            public string Name;
            public int Fine;
            public bool IsArrestable;
        }

        /// <summary>Queues citation handoff to the specified ped. Safe to call from any thread; PR API runs on game thread.</summary>
        internal static void GiveCitation(string pedName, IEnumerable<CitationHandoutCharge> charges) {
            if (string.IsNullOrWhiteSpace(pedName) || charges == null) return;

            var chargesList = charges.ToList();
            if (chargesList.Count == 0) return;

            // Lookup on caller thread (often HTTP); we only need ped handle for deferral.
            MDTProPedData pedData = DataController.GetPedDataByName(pedName);
            if (pedData == null) {
                string msg = SetupController.GetLanguage().inGame.handCitationPersonNotFound;
                if (!string.IsNullOrWhiteSpace(msg)) RageNotification.Show(string.Format(msg, pedName), RageNotification.NotificationType.Info);
                return;
            }
            Ped holder = pedData.Holder;
            if (holder == null || !holder.IsValid()) {
                string msg = SetupController.GetLanguage().inGame.handCitationPersonNotPresent;
                if (!string.IsNullOrWhiteSpace(msg)) RageNotification.Show(msg, RageNotification.NotificationType.Info);
                return;
            }

            var pedHandle = holder.Handle;
            string name = pedName;

            // PR API must run on game thread. Citation handout and ped menu are game-thread-only.
            if (GameFiber.CanSleepNow) {
                GiveCitationOnGameThread(pedHandle, name, chargesList);
            } else {
                GameFiber.StartNew(() => GiveCitationOnGameThread(pedHandle, name, chargesList));
            }
        }

        private static void GiveCitationOnGameThread(Rage.PoolHandle pedHandle, string pedName, List<CitationHandoutCharge> charges) {
            Ped ped = null;
            try {
                ped = Rage.World.GetEntityByHandle<Ped>(pedHandle);
            } catch { }
            if (ped == null || !ped.IsValid()) {
                string msg = SetupController.GetLanguage().inGame.handCitationPersonNotPresent;
                if (!string.IsNullOrWhiteSpace(msg)) RageNotification.Show(msg, RageNotification.NotificationType.Info);
                return;
            }

            try {
                // PR may require ped to be marked as stopped for the hand-citation menu option to be enabled.
                var pedApiType = System.Type.GetType("PolicingRedefined.API.PedAPI, PolicingRedefined");
                if (pedApiType != null) {
                    var setStopped = pedApiType.GetMethod("SetPedAsStopped", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    setStopped?.Invoke(null, new object[] { ped });
                }
            } catch {
                // Ignore if SetPedAsStopped not available or fails
            }

            foreach (CitationHandoutCharge charge in charges) {
                if (charge == null || string.IsNullOrWhiteSpace(charge.Name)) continue;

                Citation citation = new Citation(ped, charge.Name, charge.Fine, SetupController.GetLanguage().units.currencySymbol, SetupController.GetConfig().displayCurrencySymbolBeforeNumber, charge.IsArrestable);
                PolicingRedefined.API.PedAPI.GiveCitationToPed(ped, citation);
            }

            string message = string.Format(SetupController.GetLanguage().inGame.handCitationTo ?? "Hand citation to {0}", pedName);
            if (!string.IsNullOrWhiteSpace(message)) RageNotification.ShowSuccess(message);
        }
    }
}
