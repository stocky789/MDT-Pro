// Random suspect dialogue after a citation is handed off (subtitle). Data: MDTPro/citationPedReactions.json (see defaults).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MDTPro.Setup;
using Newtonsoft.Json;
using Rage;
using static MDTPro.Setup.SetupController;

namespace MDTPro.Utility {
    internal static class CitationPedReactionHelper {
        private static readonly object LoadLock = new object();
        private static CitationPedReactionsRoot _root;
        private static bool _loadFailed;

        private class CitationPedReactionsRoot {
            public List<ReactionRule> rules = new List<ReactionRule>();
            public Dictionary<string, ReactionPool> pools = new Dictionary<string, ReactionPool>(StringComparer.OrdinalIgnoreCase);
        }

        private class ReactionRule {
            public List<string> keywords = new List<string>();
            public string pool { get; set; }
        }

        private class ReactionPool {
            public List<string> clean = new List<string>();
            public List<string> mature = new List<string>();
        }

        /// <summary>Shows a contextual suspect line at the bottom of the screen after citation handoff. Game thread only.</summary>
        internal static void TryShowSuspectReaction(Ped ped, IEnumerable<PRHelper.CitationHandoutCharge> charges) {
            try {
                if (ped == null || !ped.IsValid()) return;
                var cfg = GetConfig();
                if (cfg?.citationPedReactionEnabled != true) return;

                var list = charges?.Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name)).ToList();
                if (list == null || list.Count == 0) return;

                if (!EnsureLoaded()) return;

                string line = PickLine(list, cfg.citationPedReactionAllowProfanity);
                if (string.IsNullOrWhiteSpace(line)) return;

                string prefix = GetLanguage().inGame.citationPedReactionSpeakerPrefix ?? "~o~Suspect:~s~ ";
                int ms = cfg.citationPedReactionDurationMs;
                if (ms < 2000) ms = 2000;
                if (ms > 20000) ms = 20000;

                GameFiber.Yield();
                string full = prefix + line.Trim().Replace('\n', ' ').Replace('\r', ' ');
                Game.DisplaySubtitle(full, ms);
            } catch {
                /* ignore */
            }
        }

        private static bool EnsureLoaded() {
            if (_loadFailed) return false;
            if (_root != null) return _root.pools != null && _root.pools.Count > 0;

            lock (LoadLock) {
                if (_root != null) return _root.pools != null && _root.pools.Count > 0;
                try {
                    string path = CitationPedReactionsPath;
                    if (!File.Exists(path)) {
                        _loadFailed = true;
                        return false;
                    }
                    string json = File.ReadAllText(path);
                    _root = JsonConvert.DeserializeObject<CitationPedReactionsRoot>(json);
                    if (_root?.pools == null || _root.pools.Count == 0) {
                        _root = null;
                        _loadFailed = true;
                        return false;
                    }
                    if (_root.rules == null) _root.rules = new List<ReactionRule>();
                } catch {
                    _loadFailed = true;
                    return false;
                }
            }
            return _root != null;
        }

        private static string PickLine(List<PRHelper.CitationHandoutCharge> charges, bool allowProfanity) {
            string blob = string.Join(" ", charges.Select(c => c.Name)).ToLowerInvariant();
            var matchedPools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ReactionRule rule in _root.rules ?? Enumerable.Empty<ReactionRule>()) {
                if (rule?.keywords == null || string.IsNullOrWhiteSpace(rule.pool)) continue;
                foreach (string kw in rule.keywords) {
                    if (string.IsNullOrWhiteSpace(kw)) continue;
                    if (blob.Contains(kw.Trim().ToLowerInvariant())) {
                        matchedPools.Add(rule.pool);
                        break;
                    }
                }
            }

            if (charges.Any(c => c.IsArrestable))
                matchedPools.Add("arrestable");

            var poolChoices = matchedPools.ToList();
            string poolKey = poolChoices.Count > 0
                ? poolChoices[Helper.GetRandomInt(0, poolChoices.Count - 1)]
                : "generic";

            if (!_root.pools.TryGetValue(poolKey, out ReactionPool pool) || pool == null)
                _root.pools.TryGetValue("generic", out pool);

            if (pool == null) return null;

            var candidates = new List<string>();
            if (pool.clean != null) candidates.AddRange(pool.clean.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (allowProfanity && pool.mature != null) candidates.AddRange(pool.mature.Where(s => !string.IsNullOrWhiteSpace(s)));

            if (candidates.Count == 0 && pool.clean != null)
                candidates.AddRange(pool.clean.Where(s => !string.IsNullOrWhiteSpace(s)));

            if (candidates.Count == 0) return null;
            return candidates[Helper.GetRandomInt(0, candidates.Count - 1)];
        }
    }
}
