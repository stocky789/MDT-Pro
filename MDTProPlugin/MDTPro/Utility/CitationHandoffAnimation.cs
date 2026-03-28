// Vanilla GTA V clipboard idle (world-human scenario clips) — approximates “writing / handing paperwork”
// without calling StopThePed or PR internals. Clip dictionaries ship with the game (see OpenIV / anim lists).
using System;
using MDTPro.Setup;
using Rage;

namespace MDTPro.Utility {
    internal static class CitationHandoffAnimation {
        private const string DictMale = "amb@world_human_clipboard@male@idle_a";
        private const string DictFemale = "amb@world_human_clipboard@female@idle_a";
        private const string ClipName = "idle_c";

        /// <summary>Plays a short clipboard animation on the local player when STP citation flow runs (skipped when PR handles citations).</summary>
        internal static void TryPlayForStopThePed(Ped suspect) {
            if (Main.usePR) return;
            if (SetupController.GetConfig()?.stpCitationPaperworkAnimation != true) return;

            Ped player = Game.LocalPlayer.Character;
            if (player == null || !player.IsValid() || suspect == null || !suspect.IsValid()) return;
            if (player.Handle == suspect.Handle) return;

            float dist;
            try {
                dist = player.DistanceTo(suspect);
            } catch {
                return;
            }
            float maxDist = SetupController.GetConfig()?.stpCitationHandoffMaxDistance ?? 4f;
            if (maxDist < 1.5f) maxDist = 1.5f;
            if (dist > maxDist) return;

            bool playerInVeh = false;
            bool suspectInVeh = false;
            try {
                playerInVeh = player.IsInAnyVehicle(false);
                suspectInVeh = suspect.IsInAnyVehicle(false);
            } catch { /* ignore */ }

            string preferredDict = ResolveDictionaryName(player);
            string animDict = preferredDict;
            if (!TryLoadDictionary(preferredDict, out AnimationDictionary dict)) {
                if (string.Equals(preferredDict, DictFemale, StringComparison.OrdinalIgnoreCase)
                    && TryLoadDictionary(DictMale, out dict))
                    animDict = DictMale;
                else
                    return;
            }

            AnimationFlags flags = AnimationFlags.None;
            if (playerInVeh || suspectInVeh)
                flags |= AnimationFlags.UpperBodyOnly | AnimationFlags.SecondaryTask;

            try {
                Rage.Task t = player.Tasks.PlayAnimation(animDict, ClipName, 5f, flags);
                t?.WaitForCompletion(8000);
            } catch {
                /* ignore — dict or clip mismatch on some builds */
            } finally {
                try { dict.Dismiss(); } catch { /* ignore */ }
            }
        }

        private static bool TryLoadDictionary(string dictName, out AnimationDictionary dict) {
            dict = new AnimationDictionary(dictName);
            dict.Load();
            int waited = 0;
            while (!dict.IsLoaded && waited < 200) {
                GameFiber.Yield();
                waited++;
            }
            if (dict.IsLoaded)
                return true;
            try { dict.Dismiss(); } catch { /* ignore */ }
            dict = null;
            return false;
        }

        private static string ResolveDictionaryName(Ped player) {
            try {
                if (player.IsFemale) return DictFemale;
            } catch { /* ignore */ }
            return DictMale;
        }
    }
}
