// Deferred suspect subtitle + hostility after citation handoff (separate fiber so paperwork anim does not block the officer).
using MDTPro;
using System.Collections.Generic;
using System.Linq;
using MDTPro.Setup;
using Rage;
using static MDTPro.Setup.SetupController;

namespace MDTPro.Utility {
    internal static class CitationHandoffPostEffects {
        /// <summary>Runs paperwork (optional), waits, suspect line, waits, violence roll. Game-thread only; starts its own fiber.</summary>
        internal static void ScheduleAfterHandoff(Ped ped, List<PRHelper.CitationHandoutCharge> charges, bool includeStopThePedPaperworkAnimation) {
            if (ped == null || !ped.IsValid() || charges == null || charges.Count == 0) return;
            PoolHandle handle = ped.Handle;
            List<PRHelper.CitationHandoutCharge> copy = CloneCharges(charges);
            GameFiber.StartNew(() => RunDeferredSequence(handle, copy, includeStopThePedPaperworkAnimation));
        }

        private static List<PRHelper.CitationHandoutCharge> CloneCharges(List<PRHelper.CitationHandoutCharge> src) {
            return src.Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => new PRHelper.CitationHandoutCharge { Name = c.Name, Fine = c.Fine, IsArrestable = c.IsArrestable })
                .ToList();
        }

        private static void RunDeferredSequence(PoolHandle pedHandle, List<PRHelper.CitationHandoutCharge> charges, bool paperwork) {
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
