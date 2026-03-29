// In-game citation delivery when Policing Redefined is not used and StopThePed has no plugin ticket API.
using System.Collections.Generic;
using MDTPro.Setup;
using MDTPro.Utility;
using Rage;
using RAGENativeUI;
using RAGENativeUI.Elements;
using static MDTPro.Setup.SetupController;

namespace MDTPro.UI {
    internal static class CitationHandoffMenu {
        /// <summary>Blocks on the game fiber until the officer confirms delivery or closes the menu.</summary>
        /// <returns>True if the officer chose Deliver; false if the menu was closed without confirming.</returns>
        internal static bool TryShowModal(Ped suspect, string pedDisplayName, List<PRHelper.CitationHandoutCharge> charges) {
            if (Main.usePR) return false;
            if (suspect == null || !suspect.IsValid() || charges == null) return false;
            var list = new List<PRHelper.CitationHandoutCharge>();
            foreach (var c in charges) {
                if (c != null && !string.IsNullOrWhiteSpace(c.Name)) list.Add(c);
            }
            if (list.Count == 0) return false;

            var lang = GetLanguage().inGame;
            string subtitle = string.IsNullOrWhiteSpace(lang.stpCitationHandoffMenuSubtitle)
                ? "~b~Citation~s~ — {0}"
                : lang.stpCitationHandoffMenuSubtitle;
            subtitle = subtitle.Contains("{0}")
                ? string.Format(subtitle, pedDisplayName ?? "?")
                : subtitle;

            var pool = new MenuPool();
            var menu = new UIMenu("MDT Pro", subtitle);
            pool.Add(menu);

            string sym = GetLanguage().units?.currencySymbol ?? "$";
            string chargeDesc = lang.stpCitationHandoffChargeDescription ?? "";

            foreach (var c in list) {
                string line = $"{c.Name} — {sym}{c.Fine}";
                if (c.IsArrestable) line += " (~r~arrestable~s~)";
                var item = new UIMenuItem(line, chargeDesc) { Enabled = false };
                menu.AddItem(item);
            }

            var deliver = new UIMenuItem(
                lang.stpCitationHandoffDeliver ?? "Deliver citation",
                lang.stpCitationHandoffDeliverDescription ?? "Confirm you are handing the written citation to the suspect.");
            menu.AddItem(deliver);

            bool delivered = false;
            deliver.Activated += (_, __) => {
                delivered = true;
                menu.Visible = false;
            };

            menu.RefreshIndex();
            menu.Visible = true;

            while (menu.Visible && !delivered) {
                pool.ProcessMenus();
                GameFiber.Yield();
            }

            menu.Visible = false;
            pool.CloseAllMenus();
            return delivered;
        }
    }
}
