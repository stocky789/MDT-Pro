using MDTPro.Data;
using MDTPro.Setup;
using PolicingRedefined.Interaction.Assets.PedAttributes;
using Rage;
using System.Linq;


namespace MDTPro.Utility {
    internal class PRHelper {
        internal static void GiveCitation(CourtData courtData) {
            MDTProPedData pedData = DataController.PedDatabase.FirstOrDefault(x => x.Name == courtData.PedName);
            if (pedData == null) return;
            Ped ped = pedData.Holder;
            if (ped == null || !ped.IsValid()) return;
            foreach (CourtData.Charge charge in courtData.Charges) {
                bool isArrestable = charge.IsArrestable ?? false;
                Citation citation = new Citation(ped, charge.Name, charge.Fine, SetupController.GetLanguage().units.currencySymbol, SetupController.GetConfig().displayCurrencySymbolBeforeNumber, isArrestable);
                PolicingRedefined.API.PedAPI.GiveCitationToPed(ped, citation);
            }
            string message = string.Format(SetupController.GetLanguage().inGame.handCitationTo ?? "Hand citation to {0}", courtData.PedName ?? "");
            if (!string.IsNullOrWhiteSpace(message)) RageNotification.ShowSuccess(message);
        }
    }
}
