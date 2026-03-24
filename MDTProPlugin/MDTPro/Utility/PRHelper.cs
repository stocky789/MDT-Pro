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

            try {
                // PR requires the ped to be stopped for hand-citation. Calling SetPedAsStopped again on an already-stopped ped can desync PR's menu (e.g. missing dismiss).
                // If IsPedStopped is unavailable or fails, we still set stopped so Give Citation keeps working on older PR builds.
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
                            // Unknown state — keep shouldSetStopped true so we match legacy behavior.
                        }
                    }
                    if (shouldSetStopped && setStopped != null) {
                        try {
                            setStopped.Invoke(null, new object[] { ped });
                        } catch {
                            // Ignore if SetPedAsStopped fails
                        }
                    }
                }
                // One frame for PR to apply stop state before queueing citations.
                GameFiber.Yield();
            } catch {
                // Ignore PedAPI reflection failures
            }

            foreach (CitationHandoutCharge charge in charges) {
                if (charge == null || string.IsNullOrWhiteSpace(charge.Name)) continue;

                Citation citation = new Citation(ped, charge.Name, charge.Fine, SetupController.GetLanguage().units.currencySymbol, SetupController.GetConfig().displayCurrencySymbolBeforeNumber, charge.IsArrestable);
                PolicingRedefined.API.PedAPI.GiveCitationToPed(ped, citation);
                // Let PR process each queued handoff; back-to-back calls in one frame can break the ped menu.
                GameFiber.Yield();
            }

            string message = string.Format(SetupController.GetLanguage().inGame.handCitationTo ?? "Hand citation to {0}", pedName);
            if (!string.IsNullOrWhiteSpace(message)) RageNotification.ShowSuccess(message);
        }
    }
}
