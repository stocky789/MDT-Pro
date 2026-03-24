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
using System.Reflection;


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

            // Lookup ped handle: Holder from ped data, or recently identified cache (e.g. when DB entry was loaded from file with no Holder).
            Rage.PoolHandle? pedHandle = null;
            MDTProPedData pedData = DataController.GetPedDataByName(pedName);
            if (pedData != null) {
                Ped holder = pedData.Holder;
                if (holder != null && holder.IsValid())
                    pedHandle = holder.Handle;
            }
            if (!pedHandle.HasValue)
                pedHandle = DataController.GetRecentlyIdentifiedPedHandle(pedName);

            if (!pedHandle.HasValue) {
                if (pedData == null) {
                    string msg = SetupController.GetLanguage().inGame.handCitationPersonNotFound;
                    if (!string.IsNullOrWhiteSpace(msg)) RageNotification.Show(string.Format(msg, pedName), RageNotification.NotificationType.Info);
                } else {
                    string msg = SetupController.GetLanguage().inGame.handCitationPersonNotPresent;
                    if (!string.IsNullOrWhiteSpace(msg)) RageNotification.Show(msg, RageNotification.NotificationType.Info);
                }
                return;
            }

            var handleToUse = pedHandle.Value;
            string name = pedName;

            // PR API must run on game thread. Citation handout and ped menu are game-thread-only.
            if (GameFiber.CanSleepNow) {
                GiveCitationOnGameThread(handleToUse, name, chargesList);
            } else {
                GameFiber.StartNew(() => GiveCitationOnGameThread(handleToUse, name, chargesList));
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

            // Vehicle traffic stops keep the ped in a different PR interaction mode. SetPedAsStopped on an occupant
            // can flatten that to a generic "stopped ped" state and the Ped Stop menu loses Dismiss and other stop options.
            bool inVehicle = false;
            try {
                inVehicle = ped.IsInAnyVehicle(false);
            } catch {
                /* ignore */
            }

            try {
                if (!inVehicle) {
                    // On-foot: ensure PR knows the ped is stopped so hand-citation is available; avoid duplicate SetPedAsStopped when already stopped.
                    var pedApiType = System.Type.GetType("PolicingRedefined.API.PedAPI, PolicingRedefined");
                    if (pedApiType != null) {
                        const BindingFlags pedApiFlags = BindingFlags.Public | BindingFlags.Static;
                        MethodInfo setStopped = pedApiType.GetMethod("SetPedAsStopped", pedApiFlags);
                        MethodInfo isStopped = pedApiType.GetMethod("IsPedStopped", pedApiFlags);
                        bool shouldSetStopped = true;
                        if (isStopped != null) {
                            try {
                                object result = isStopped.Invoke(null, new object[] { ped });
                                if (result is bool alreadyStopped && alreadyStopped)
                                    shouldSetStopped = false;
                            } catch {
                                // Unknown — keep shouldSetStopped true (legacy behavior).
                            }
                        }
                        if (shouldSetStopped && setStopped != null) {
                            try {
                                setStopped.Invoke(null, new object[] { ped });
                            } catch {
                                /* ignore */
                            }
                        }
                    }
                }
                // One frame before queueing citations (after SetPedAsStopped when used).
                GameFiber.Yield();
            } catch {
                // Ignore PedAPI reflection failures
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
