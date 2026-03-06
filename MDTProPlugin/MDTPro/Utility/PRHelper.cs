// Policing Redefined integration (citations, ped menu).
// API: GiveCitationToPed(Ped, Citation) — https://policing-redefined.netlify.app/docs/developer-docs/pr/ped-api/ped-detain-resist
// Docs require the Citation's Ped to match the GiveCitationToPed ped parameter; we use the same ped for both.
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

        internal static void GiveCitation(string pedName, IEnumerable<CitationHandoutCharge> charges) {
            if (string.IsNullOrWhiteSpace(pedName) || charges == null) return;

            // Case-insensitive lookup in both active and persistent ped DB so citation handout works
            // even when name casing differs or the person is in keepInPedDatabase (e.g. from a prior stop).
            MDTProPedData pedData = DataController.GetPedDataByName(pedName);
            if (pedData == null) {
                string msg = SetupController.GetLanguage().inGame.handCitationPersonNotFound;
                if (!string.IsNullOrWhiteSpace(msg)) RageNotification.Show(string.Format(msg, pedName), RageNotification.NotificationType.Warning);
                return;
            }
            Ped ped = pedData.Holder;
            if (ped == null || !ped.IsValid()) {
                string msg = SetupController.GetLanguage().inGame.handCitationPersonNotPresent;
                if (!string.IsNullOrWhiteSpace(msg)) RageNotification.Show(msg, RageNotification.NotificationType.Warning);
                return;
            }

            foreach (CitationHandoutCharge charge in charges) {
                if (charge == null || string.IsNullOrWhiteSpace(charge.Name)) continue;

                // Citation(ped, ...) and GiveCitationToPed(ped, citation) — same ped per PR Ped API docs.
                Citation citation = new Citation(ped, charge.Name, charge.Fine, SetupController.GetLanguage().units.currencySymbol, SetupController.GetConfig().displayCurrencySymbolBeforeNumber, charge.IsArrestable);
                PolicingRedefined.API.PedAPI.GiveCitationToPed(ped, citation);
            }

            string message = string.Format(SetupController.GetLanguage().inGame.handCitationTo ?? "Hand citation to {0}", pedName);
            if (!string.IsNullOrWhiteSpace(message)) RageNotification.ShowSuccess(message);
        }
    }
}
