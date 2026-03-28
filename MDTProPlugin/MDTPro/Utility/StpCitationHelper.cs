// StopThePed: reflect public static citation & ticket APIs (References/StopThePed decompile + runtime discovery).
using MDTPro.Setup;
using Rage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MDTPro.Utility {
    internal static class StpCitationHelper {
        private static bool discoveryDone;
        private static List<MethodInfo> citationMethods;
        private static bool loggedNoApi;

        private static bool ShouldScanAssemblyForCitations(string assemblyName) {
            return string.Equals(assemblyName, "StopThePed", StringComparison.OrdinalIgnoreCase);
        }

        private static bool NameLooksLikeCitationApi(string methodName) {
            if (string.IsNullOrEmpty(methodName)) return false;
            string n = methodName.ToLowerInvariant();
            if (n.Contains("inject") || n.Contains("getsearch") || n.Contains("searchitem")) return false;
            if (n.Contains("dispatch") && !n.Contains("ticket") && !n.Contains("citation")) return false;
            if (n.Contains("issueticket") || n.Contains("issue_ticket")) return true;
            if (n.Contains("ticket") && (n.Contains("issue") || n.Contains("give") || n.Contains("add") || n.Contains("hand") || n.Contains("create") || n.Contains("write") || n.Contains("submit")))
                return true;
            if (n.Contains("citation") && (n.Contains("give") || n.Contains("issue") || n.Contains("add") || n.Contains("hand") || n.Contains("create") || n.Contains("submit")))
                return true;
            if (n.Contains("trafficcitation")) return true;
            if (n.Contains("fine") && (n.Contains("issue") || n.Contains("give") || n.Contains("add")) && (n.Contains("ped") || n.Contains("suspect") || n.Contains("citizen")))
                return true;
            return false;
        }

        private static Type[] SafeGetExportedTypes(Assembly asm) {
            try {
                return asm.GetExportedTypes();
            } catch {
                try {
                    return asm.GetTypes();
                } catch (ReflectionTypeLoadException ex) {
                    return ex.Types?.Where(t => t != null).ToArray() ?? Array.Empty<Type>();
                } catch {
                    return Array.Empty<Type>();
                }
            }
        }

        private static bool MethodHasPedParameter(MethodInfo m, out int pedParameterIndex) {
            pedParameterIndex = -1;
            var ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++) {
                if (ps[i].ParameterType == typeof(Ped)) {
                    pedParameterIndex = i;
                    return true;
                }
            }
            return false;
        }

        private static void Discover() {
            if (discoveryDone) return;
            discoveryDone = true;
            citationMethods = new List<MethodInfo>();
            try {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                    if (asm.IsDynamic) continue;
                    if (!ShouldScanAssemblyForCitations(asm.GetName().Name)) continue;
                    foreach (Type t in SafeGetExportedTypes(asm)) {
                        if (t == null) continue;
                        try {
                            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                                if (!m.IsStatic || m.IsSpecialName) continue;
                                if (!NameLooksLikeCitationApi(m.Name)) continue;
                                if (!MethodHasPedParameter(m, out _)) continue;
                                if (m.ReturnType != typeof(void) && m.ReturnType != typeof(bool)) continue;
                                citationMethods.Add(m);
                            }
                        } catch { /* type */ }
                    }
                }
                citationMethods = citationMethods.OrderByDescending(m => m.GetParameters().Length).ToList();
                if (citationMethods.Count > 0)
                    Game.LogTrivial($"[MDT Pro] Citation handoff: discovered {citationMethods.Count} candidate method(s) on StopThePed.");
            } catch (Exception ex) {
                Game.LogTrivial($"[MDT Pro] Citation discovery: {ex.Message}");
                citationMethods = new List<MethodInfo>();
            }
        }

        /// <summary>Try to hand off closed MDT citation charges into StopThePed (reflection). Safe from any thread.</summary>
        internal static void GiveCitation(string pedName, IEnumerable<PRHelper.CitationHandoutCharge> charges) {
            if (Main.usePR) return;
            if (string.IsNullOrWhiteSpace(pedName) || charges == null) return;
            var list = charges.ToList();
            if (list.Count == 0) return;

            if (!PRHelper.TryGetCitationPedHandle(pedName, out Rage.PoolHandle pedHandle, out _)) return;

            if (GameFiber.CanSleepNow)
                GiveOnGameThread(pedHandle, pedName, list);
            else
                GameFiber.StartNew(() => GiveOnGameThread(pedHandle, pedName, list));
        }

        private static void GiveOnGameThread(Rage.PoolHandle pedHandle, string pedName, List<PRHelper.CitationHandoutCharge> charges) {
            Ped ped = null;
            try { ped = World.GetEntityByHandle<Ped>(pedHandle); } catch { }
            if (ped == null || !ped.IsValid()) {
                string msg = SetupController.GetLanguage().inGame.handCitationPersonNotPresent;
                if (!string.IsNullOrWhiteSpace(msg))
                    RageNotification.Show(RageNotification.AppendStpCitationMdtBrowserHint(msg), RageNotification.NotificationType.Info);
                return;
            }

            try {
                Discover();
                int ok = TryStopThePedPluginHandoff(ped, charges);

                if (ok > 0) {
                    string message = string.Format(SetupController.GetLanguage().inGame.handCitationTo ?? "Hand citation to {0}", pedName);
                    if (!string.IsNullOrWhiteSpace(message))
                        RageNotification.ShowSuccess(RageNotification.AppendStpCitationMdtBrowserHint(message));
                    CitationHandoffPostEffects.ScheduleAfterHandoff(ped, charges, includeStopThePedPaperworkAnimation: true, pedName);
                } else {
                    // No STP plugin API: queue for in-game keybind + menu (not instant popup).
                    StpCitationHandoffQueue.Enqueue(pedName, charges);
                    var lang = SetupController.GetLanguage().inGame;
                    string keyLabel = UI.CitationHandoffKeybind.CurrentKeyLabel;
                    string queued = lang.stpCitationHandoffQueued
                        ?? "Citation saved. When you are ~g~close to the suspect~s~, press ~b~{0}~s~ to open the handoff menu.";
                    if (!string.IsNullOrWhiteSpace(queued))
                        RageNotification.Show(RageNotification.AppendStpCitationMdtBrowserHint(string.Format(queued, keyLabel)), RageNotification.NotificationType.Info);
                }
            } catch (Exception ex) {
                Game.LogTrivial($"[MDT Pro] StopThePed citation handoff error: {ex.Message}");
            }
        }

        /// <summary>Some mods expose (Ped, string allCharges) instead of per-charge calls.</summary>
        private static bool TryInvokeBatchSummary(List<MethodInfo> methods, Ped ped, List<PRHelper.CitationHandoutCharge> charges) {
            string summary = string.Join("; ", charges.Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => $"{c.Name} (${c.Fine}{(c.IsArrestable ? ", arrestable" : "")})"));
            if (string.IsNullOrWhiteSpace(summary)) return false;
            foreach (MethodInfo method in methods) {
                ParameterInfo[] ps = method.GetParameters();
                if (ps.Length != 2) continue;
                int pi = -1;
                for (int i = 0; i < 2; i++) {
                    if (ps[i].ParameterType == typeof(Ped)) pi = i;
                }
                if (pi < 0 || ps[1 - pi].ParameterType != typeof(string)) continue;
                object[] args = new object[2];
                args[pi] = ped;
                args[1 - pi] = summary;
                try {
                    object r = method.Invoke(null, args);
                    if (r is bool b) return b;
                    return true;
                } catch { /* next */ }
            }
            return false;
        }

        /// <summary>Attempts reflection-based handoff into StopThePed. Returns count of charges accepted (0 if no API or all invocations failed).</summary>
        private static int TryStopThePedPluginHandoff(Ped ped, List<PRHelper.CitationHandoutCharge> charges) {
            if (citationMethods == null || citationMethods.Count == 0) {
                if (!loggedNoApi) {
                    loggedNoApi = true;
                    Game.LogTrivial("[MDT Pro] StopThePed has no public plugin citation API — opening MDT Pro handoff menu.");
                }
                return 0;
            }

            int ok = 0;
            foreach (var charge in charges) {
                if (charge == null || string.IsNullOrWhiteSpace(charge.Name)) continue;
                foreach (MethodInfo method in citationMethods) {
                    if (TryInvokeMethod(method, ped, charge)) {
                        ok++;
                        break;
                    }
                }
            }

            if (ok == 0)
                ok = TryInvokeBatchSummary(citationMethods, ped, charges) ? charges.Count(c => c != null && !string.IsNullOrWhiteSpace(c.Name)) : 0;
            return ok;
        }

        private static bool TryInvokeMethod(MethodInfo method, Ped ped, PRHelper.CitationHandoutCharge charge) {
            ParameterInfo[] ps = method.GetParameters();
            object[] args = new object[ps.Length];
            int stringSlot = 0;
            for (int i = 0; i < ps.Length; i++) {
                Type pt = ps[i].ParameterType;
                if (pt == typeof(Ped)) {
                    args[i] = ped;
                    continue;
                }
                if (pt == typeof(string)) {
                    args[i] = stringSlot++ == 0 ? charge.Name : $"{charge.Fine}";
                    continue;
                }
                if (pt == typeof(int)) {
                    args[i] = charge.Fine;
                    continue;
                }
                if (pt == typeof(bool)) {
                    args[i] = charge.IsArrestable;
                    continue;
                }
                if (pt == typeof(uint)) {
                    args[i] = unchecked((uint)Math.Max(0, charge.Fine));
                    continue;
                }
                if (pt == typeof(float)) {
                    args[i] = (float)charge.Fine;
                    continue;
                }
                if (pt == typeof(double)) {
                    args[i] = (double)charge.Fine;
                    continue;
                }
                if (pt.IsEnum) {
                    try {
                        args[i] = Enum.ToObject(pt, charge.Fine);
                        continue;
                    } catch {
                        return false;
                    }
                }
                return false;
            }

            try {
                object r = method.Invoke(null, args);
                if (r is bool b) return b;
                return true;
            } catch {
                return false;
            }
        }
    }
}
