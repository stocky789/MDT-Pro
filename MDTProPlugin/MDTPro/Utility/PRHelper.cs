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

            MDTProPedData pedData = DataController.PedDatabase.FirstOrDefault(x => x.Name == pedName);
            if (pedData == null) return;
            Ped ped = pedData.Holder;
            if (ped == null || !ped.IsValid()) return;

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
