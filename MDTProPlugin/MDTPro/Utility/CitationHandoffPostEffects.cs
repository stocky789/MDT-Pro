// Deferred suspect subtitle + hostility after citation handoff (separate fiber so paperwork anim does not block the officer).
using MDTPro;
using MDTPro.Data;
using System.Collections.Generic;
using System.Linq;
using MDTPro.Setup;
using Rage;
using static MDTPro.Setup.SetupController;

namespace MDTPro.Utility {
    internal static class CitationHandoffPostEffects {
        /// <summary>Runs paperwork (optional), waits, in-game success notification, suspect subtitle, waits, violence roll. Game-thread only; starts its own fiber.</summary>
        internal static void ScheduleAfterHandoff(Ped ped, List<PRHelper.CitationHandoutCharge> charges, bool includeStopThePedPaperworkAnimation, string offenderPedName) {
            if (ped == null || !ped.IsValid() || charges == null || charges.Count == 0) return;
            PoolHandle handle = ped.Handle;
            List<PRHelper.CitationHandoutCharge> copy = CloneCharges(charges);
            string nameKey = string.IsNullOrWhiteSpace(offenderPedName) ? null : offenderPedName.Trim();
            GameFiber.StartNew(() => RunDeferredSequence(handle, copy, includeStopThePedPaperworkAnimation, nameKey));
        }

        private static List<PRHelper.CitationHandoutCharge> CloneCharges(List<PRHelper.CitationHandoutCharge> src) {
            return src.Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => new PRHelper.CitationHandoutCharge { Name = c.Name, Fine = c.Fine, IsArrestable = c.IsArrestable })
                .ToList();
        }

        private static void RunDeferredSequence(PoolHandle pedHandle, List<PRHelper.CitationHandoutCharge> charges, bool paperwork, string offenderPedName) {
            try {
                Ped ped = ResolvePed(pedHandle);
                if (ped == null) return;

                if (paperwork && !Main.usePR && GetConfig()?.stpCitationPaperworkAnimation == true)
                    CitationHandoffAnimation.TryPlayForStopThePed(ped);

                int afterPaperwork = GetConfig()?.citationHandoffBehaviorDelayAfterPaperworkMs ?? 1800;
                if (afterPaperwork < 0) afterPaperwork = 0;
                if (afterPaperwork > 120000) afterPaperwork = 120000;
                if (afterPaperwork > 0)
                    GameFiber.Sleep(afterPaperwork);

                TryShowCitationHandedSuccessNotification(offenderPedName, charges);
                GameFiber.Yield();

                ped = ResolvePed(pedHandle);
                if (ped == null) return;
                CitationPedReactionHelper.TryShowSuspectReaction(ped, charges);

                int beforeViolence = GetConfig()?.citationHandoffDelayBeforeViolenceAfterReactionMs ?? 1200;
                if (beforeViolence < 0) beforeViolence = 0;
                if (beforeViolence > 120000) beforeViolence = 120000;
                if (beforeViolence > 0)
                    GameFiber.Sleep(beforeViolence);

                ped = ResolvePed(pedHandle);
                if (ped == null) return;
                CitationPostHandoffViolenceHelper.TryMaybeAggressiveAfterCitation(ped, charges);
            } catch {
                /* ignore */
            }
        }

        private static void TryShowCitationHandedSuccessNotification(string offenderPedName, List<PRHelper.CitationHandoutCharge> charges) {
            try {
                string display = BuildOffenderDisplayNameForCitation(offenderPedName);
                int total = charges?.Where(c => c != null).Sum(c => c.Fine) ?? 0;
                string fineStr = FormatCitationFineTotal(total);
                string fmt = GetLanguage()?.inGame?.citationHandedSuccess;
                if (string.IsNullOrWhiteSpace(fmt))
                    fmt = "You successfully handed {0} a citation for {1}";
                string msg = string.Format(fmt, display, fineStr);
                if (!string.IsNullOrWhiteSpace(msg))
                    RageNotification.ShowSuccess(msg);
            } catch {
                /* ignore */
            }
        }

        /// <summary>Prefer CDF first/last; fall back to splitting the MDT offender name.</summary>
        private static string BuildOffenderDisplayNameForCitation(string offenderPedName) {
            if (!string.IsNullOrWhiteSpace(offenderPedName)) {
                MDTProPedData data = DataController.GetPedDataByName(offenderPedName.Trim());
                if (data != null) {
                    data.TryParseNameIntoFirstLast();
                    string f = data.FirstName?.Trim() ?? "";
                    string l = data.LastName?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(f) && !string.IsNullOrEmpty(l))
                        return $"{f} {l}";
                    if (!string.IsNullOrEmpty(f))
                        return f;
                    if (!string.IsNullOrEmpty(data.Name?.Trim()))
                        return data.Name.Trim();
                }
                return offenderPedName.Trim();
            }
            return "?";
        }

        private static string FormatCitationFineTotal(int amount) {
            var cfg = GetConfig();
            var lang = GetLanguage();
            string sym = lang?.units?.currencySymbol ?? "$";
            if (cfg?.displayCurrencySymbolBeforeNumber == true)
                return $"{sym}{amount}";
            return $"{amount} {sym}";
        }

        private static Ped ResolvePed(PoolHandle h) {
            try {
                Ped p = World.GetEntityByHandle<Ped>(h);
                return p != null && p.IsValid() ? p : null;
            } catch {
                return null;
            }
        }
    }
}
