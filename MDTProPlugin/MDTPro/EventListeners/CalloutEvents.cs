// Ignore Spelling: Callsign Coords

using MDTPro.Data;
using MDTPro.Data.Reports;
using MDTPro.ServerAPI;
using MDTPro.Setup;
using MDTPro.Utility;
using LSPD_First_Response.Mod.API;
using Rage;
using LSPD_First_Response.Mod.Callouts;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using LspdFunc = LSPD_First_Response.Mod.API.Functions;

namespace MDTPro.EventListeners {
    public class CalloutEvents {
        /// <summary>LSPDFR <see cref="CalloutAcceptanceState"/> (reference assembly is obfuscated; ILDASM cannot list it). Ordinal 0 = Pending, 1 = Responded, 2 = En route, 3 = Finished — see LSPDFR API docs / LMSDev LSPDFR-API.</summary>
        internal const int LspdfrAcceptanceRevealDelayMs = 450;

        internal static CalloutInformation CalloutInfo;
        internal static List<CalloutInformation> CalloutList = new List<CalloutInformation>();
        /// <summary>Optional unit / availability line shown on MDT clients (set via <c>POST /post/cadUnitStatus</c>).</summary>
        internal static string CadUnitStatus = "";
        private static readonly object CalloutListLock = new object();
        private static readonly List<(LHandle handle, CalloutInformation info)> CalloutsByHandle = new List<(LHandle, CalloutInformation)>();

        public class CalloutInformation {
            /// <summary>Stable id for MDT / native clients (POST <c>calloutAction</c>).</summary>
            public string Id;
            public string Name;
            public string Description;
            public string Message;
            public string Advisory;
            public string Callsign;
            public string Agency;
            public string Priority;
            public Location Location;
            public float[] Coords = new float[2];
            /// <summary>Live state from LSPDFR / Callout Interface (often reads <see cref="CalloutAcceptanceState.Responded"/> as soon as the call appears — do not use alone for MDT UI).</summary>
            [JsonIgnore]
            public CalloutAcceptanceState AcceptanceState;
            /// <summary>Serialized to clients as <c>AcceptanceState</c>: LSPDFR <see cref="CalloutAcceptanceState.Pending"/> (0) until we intentionally expose real handle state (avoids false <see cref="CalloutAcceptanceState.Responded"/> on dispatch and spurious early <c>OnCalloutAccepted</c>).</summary>
            [JsonProperty("AcceptanceState")]
            public CalloutAcceptanceState ClientAcceptanceState {
                get {
                    if (FinishedTime != null) return AcceptanceState;
                    if (LspdfrAcceptanceExposedToMdt) return AcceptanceState;
                    return CalloutAcceptanceState.Pending;
                }
            }
            public DateTime DisplayedTime;
            /// <summary><see cref="Environment.TickCount"/> when the call was added (for debounce vs spurious <c>OnCalloutAccepted</c>).</summary>
            [JsonIgnore]
            public int DisplayedAtTick;
            /// <summary>When true, MDT/web/native may show LSPDFR handle acceptance state; until then clients always see <see cref="CalloutAcceptanceState.Pending"/> (open dispatch).</summary>
            [JsonIgnore]
            public bool LspdfrAcceptanceExposedToMdt;
            public DateTime? AcceptedTime = null;
            public DateTime? FinishedTime = null;
            public List<string> AdditionalMessages = new List<string>();

            internal CalloutInformation(Callout callout, LHandle handle) {
                Id = Guid.NewGuid().ToString("N");
                Name = callout.FriendlyName;
                Agency = Helper.GetAgencyNameFromScriptName(LSPD_First_Response.Mod.API.Functions.GetCurrentAgencyScriptName()) ?? LSPD_First_Response.Mod.API.Functions.GetCurrentAgencyScriptName();
                // thank you opus49
                if (callout.ScriptInfo is CalloutInterfaceAPI.CalloutInterfaceAttribute calloutInterfaceInfo) {
                    if (calloutInterfaceInfo.Agency.Length > 0) {
                        Agency = calloutInterfaceInfo.Agency;
                    }
                    if (calloutInterfaceInfo.Priority.Length > 0) {
                        Priority = calloutInterfaceInfo.Priority;
                    }
                    Description = calloutInterfaceInfo.Description;
                    Name = calloutInterfaceInfo.Name;
                }
                Message = callout.CalloutMessage;
                Advisory = callout.CalloutAdvisory;
                Callsign = DependencyCheck.IsIPTCommonAvailable() ? Helper.GetCallSignFromIPTCommon() : null;
                Location = new Location(callout.CalloutPosition);
                Coords[0] = callout.CalloutPosition.X;
                Coords[1] = callout.CalloutPosition.Y;
                AcceptanceState = GetCiAwareAcceptanceState(handle, callout);
                DisplayedTime = DateTime.Now;
                DisplayedAtTick = Environment.TickCount;
                LspdfrAcceptanceExposedToMdt = false;
            }
        }

        /// <summary>
        /// Callout Interface dispatches often resolve through LSPDFR’s <see cref="Callout"/> first; that object’s <see cref="Callout.AcceptanceState"/> can read as <see cref="CalloutAcceptanceState.Responded"/>
        /// while CI still treats the call as pending. Prefer CI’s <c>GetCalloutFromHandle</c> when present (via <see cref="CalloutHandleResolver.TryGetCalloutFromCalloutInterfaceOnly"/> reflection), and reconcile with <see cref="LspdFunc.GetCalloutAcceptanceState"/> when the two disagree.
        /// </summary>
        internal static CalloutAcceptanceState GetCiAwareAcceptanceState(LHandle handle, Callout resolvedCallout) {
            if (resolvedCallout?.ScriptInfo is CalloutInterfaceAPI.CalloutInterfaceAttribute) {
                CalloutAcceptanceState handleState = handle != null ? LspdFunc.GetCalloutAcceptanceState(handle) : resolvedCallout.AcceptanceState;
                if (CalloutInterfaceAPI.Functions.IsCalloutInterfaceAvailable && handle != null) {
                    try {
                        var ciCallout = CalloutHandleResolver.TryGetCalloutFromCalloutInterfaceOnly(handle);
                        if (ciCallout != null) {
                            var ciState = ciCallout.AcceptanceState;
                            if (handleState == CalloutAcceptanceState.Pending && ciState != CalloutAcceptanceState.Pending)
                                return handleState;
                            if (handleState != CalloutAcceptanceState.Pending && ciState == CalloutAcceptanceState.Pending)
                                return ciState;
                            return ciState;
                        }
                    } catch { }
                    // Resolver failed: if LSPDFR handle is still pending, do not trust the proxy instance’s “responded” read.
                    if (handleState == CalloutAcceptanceState.Pending)
                        return CalloutAcceptanceState.Pending;
                    return resolvedCallout.AcceptanceState;
                }
                return resolvedCallout.AcceptanceState;
            }
            if (handle != null) {
                try {
                    return LspdFunc.GetCalloutAcceptanceState(handle);
                } catch {
                    return resolvedCallout?.AcceptanceState ?? CalloutAcceptanceState.Pending;
                }
            }
            return resolvedCallout?.AcceptanceState ?? CalloutAcceptanceState.Pending;
        }

        internal delegate void CalloutEventHandler(CalloutInformation calloutInfo);
        internal static event CalloutEventHandler OnCalloutEvent;

        /// <summary>After MDT <c>accept</c> or in-game accept, allow clients to see LSPDFR state (game thread).</summary>
        internal static void MarkLspdfrAcceptanceExposedForCalloutId(string calloutId) {
            if (string.IsNullOrWhiteSpace(calloutId)) return;
            var id = calloutId.Trim();
            lock (CalloutListLock) {
                foreach (var (h, i) in CalloutsByHandle) {
                    if (i?.Id == null || !string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                    var c = CalloutHandleResolver.TryGetCallout(h);
                    i.AcceptanceState = GetCiAwareAcceptanceState(h, c);
                    i.LspdfrAcceptanceExposedToMdt = true;
                    if (i.AcceptedTime == null) i.AcceptedTime = DateTime.Now;
                    break;
                }
            }
        }

        static void RaiseCalloutEvent(CalloutInformation info) {
            lock (CalloutListLock) {
                ApplyLspdfrAcceptanceVisibilityRulesUnlocked();
            }
            OnCalloutEvent?.Invoke(info);
        }

        /// <summary>After the reveal delay, expose real state only when <see cref="GetCiAwareAcceptanceState"/> is not <see cref="CalloutAcceptanceState.Pending"/> (raw LSPDFR handle can read Responded while CI-aware is still Pending — do not flip <see cref="LspdfrAcceptanceExposedToMdt"/> early).</summary>
        static void ApplyLspdfrAcceptanceVisibilityRulesUnlocked() {
            foreach (var (h, info) in CalloutsByHandle) {
                if (info == null || h == null || info.FinishedTime != null || info.LspdfrAcceptanceExposedToMdt) continue;
                int elapsed = unchecked(Environment.TickCount - info.DisplayedAtTick);
                if (elapsed < 0) elapsed = int.MaxValue;
                if (elapsed < LspdfrAcceptanceRevealDelayMs) continue;
                try {
                    var c = CalloutHandleResolver.TryGetCallout(h);
                    var ciAware = GetCiAwareAcceptanceState(h, c);
                    if (ciAware == CalloutAcceptanceState.Pending) continue;
                    info.AcceptanceState = ciAware;
                    info.LspdfrAcceptanceExposedToMdt = true;
                    if (info.AcceptedTime == null) info.AcceptedTime = DateTime.Now;
                } catch { }
            }
        }

        private const int MaxCalloutsInList = 20;
        private static bool _calloutHandlersRegistered;

        /// <summary>Subscribe to LSPDFR callout events (always — uses LSPDFR to resolve handles; CI is optional for metadata / sendMessage).</summary>
        internal static void AddCalloutEventHandlers() {
            if (_calloutHandlersRegistered) return;
            _calloutHandlersRegistered = true;
            LSPD_First_Response.Mod.API.Events.OnCalloutDisplayed += OnCalloutDisplayedForCi;
            LSPD_First_Response.Mod.API.Events.OnCalloutFinished += OnCalloutFinishedForCi;
            LSPD_First_Response.Mod.API.Events.OnCalloutAccepted += OnCalloutAcceptedForCi;
        }

        /// <summary>Detach LSPDFR callout handlers on plugin unload (same delegate instances as <see cref="AddCalloutEventHandlers"/>).</summary>
        internal static void RemoveCalloutEventHandlers() {
            if (!_calloutHandlersRegistered) return;
            LSPD_First_Response.Mod.API.Events.OnCalloutDisplayed -= OnCalloutDisplayedForCi;
            LSPD_First_Response.Mod.API.Events.OnCalloutFinished -= OnCalloutFinishedForCi;
            LSPD_First_Response.Mod.API.Events.OnCalloutAccepted -= OnCalloutAcceptedForCi;
            _calloutHandlersRegistered = false;
        }

        private static void OnCalloutDisplayedForCi(LHandle handle) {
            if (handle == null) return;
            Callout callout = CalloutHandleResolver.TryGetCallout(handle);
            if (callout == null) {
                Helper.Log("MDT Pro: OnCalloutDisplayed — could not resolve Callout from handle (LSPDFR + CalloutInterface). Active Call list will miss this dispatch.", true, Helper.LogSeverity.Warning);
                return;
            }
            var info = new CalloutInformation(callout, handle);

            lock (CalloutListLock) {
                CalloutInfo = info;
                CalloutsByHandle.Insert(0, (handle, info));
                CalloutList.Insert(0, info);
                while (CalloutsByHandle.Count > MaxCalloutsInList) {
                    CalloutsByHandle.RemoveAt(CalloutsByHandle.Count - 1);
                    CalloutList.RemoveAt(CalloutList.Count - 1);
                }
            }

            if (SetupController.GetConfig().addCalloutSuspectNamesFromMessages) {
                TryAddCalloutSuspectNameFromText(info.Message);
                TryAddCalloutSuspectNameFromText(info.Advisory);
            }

            RaiseCalloutEvent(info);
            ScheduleLspdfrAcceptanceRevealBroadcast();
        }

        /// <summary>Re-evaluate handle state after the reveal delay so clients update even if no further LSPDFR events fire.</summary>
        static void ScheduleLspdfrAcceptanceRevealBroadcast() {
            GameFiber.StartNew(() => {
                try {
                    GameFiber.Wait(LspdfrAcceptanceRevealDelayMs + 75);
                    lock (CalloutListLock) { ApplyLspdfrAcceptanceVisibilityRulesUnlocked(); }
                    WebSocketHandler.BroadcastCalloutPayload();
                } catch { }
            });
        }

        private static void OnCalloutAcceptedForCi(LHandle handle) {
            if (handle == null) return;
            var callout = CalloutHandleResolver.TryGetCallout(handle);
            CalloutInformation info = null;
            lock (CalloutListLock) {
                foreach (var (h, i) in CalloutsByHandle) {
                    if (object.ReferenceEquals(h, handle)) { info = i; break; }
                }
                if (info == null) return;
                info.AcceptanceState = GetCiAwareAcceptanceState(handle, callout);
                int elapsed = unchecked(Environment.TickCount - info.DisplayedAtTick);
                if (elapsed < 0) elapsed = int.MaxValue;
                if (elapsed >= LspdfrAcceptanceRevealDelayMs) {
                    info.LspdfrAcceptanceExposedToMdt = true;
                    if (info.AcceptedTime == null) info.AcceptedTime = DateTime.Now;
                }
            }
            RaiseCalloutEvent(info);
        }

        private static void OnCalloutFinishedForCi(LHandle handle) {
            if (handle == null) return;
            var callout = CalloutHandleResolver.TryGetCallout(handle);
            CalloutInformation info = null;
            lock (CalloutListLock) {
                foreach (var (h, i) in CalloutsByHandle) {
                    if (object.ReferenceEquals(h, handle)) { info = i; break; }
                }
                if (info == null) return;
                info.AcceptanceState = GetCiAwareAcceptanceState(handle, callout);
                info.FinishedTime = DateTime.Now;
                info.LspdfrAcceptanceExposedToMdt = true;
            }
            RaiseCalloutEvent(info);
        }

        /// <summary>Patterns to extract a suspect name from callout dispatch text (e.g. "associated with Joe Thomas" -> Joe Thomas).</summary>
        private static readonly Regex[] CalloutSuspectNamePatterns = new[] {
            new Regex(@"associated with\s+([A-Za-z]+\s+[A-Za-z]+)", RegexOptions.IgnoreCase),
            new Regex(@"sightings of\s+([A-Za-z]+\s+[A-Za-z]+)", RegexOptions.IgnoreCase),
            new Regex(@"person (?:named\s+)?([A-Za-z]+\s+[A-Za-z]+)", RegexOptions.IgnoreCase),
            new Regex(@"identified as\s+([A-Za-z]+\s+[A-Za-z]+)", RegexOptions.IgnoreCase),
            new Regex(@"suspect\s+([A-Za-z]+\s+[A-Za-z]+)", RegexOptions.IgnoreCase),
        };

        /// <summary>Resolve the LSPDFR handle for a callout id from the active list (for game-thread actions).</summary>
        internal static bool TryGetHandleForCalloutId(string calloutId, out LHandle handle) {
            handle = null;
            if (string.IsNullOrWhiteSpace(calloutId)) return false;
            lock (CalloutListLock) {
                foreach (var (h, info) in CalloutsByHandle) {
                    if (info?.Id != null && string.Equals(info.Id, calloutId.Trim(), StringComparison.OrdinalIgnoreCase)) {
                        handle = h;
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool TryGetCalloutInformation(string calloutId, out CalloutInformation info) {
            info = null;
            if (string.IsNullOrWhiteSpace(calloutId)) return false;
            lock (CalloutListLock) {
                foreach (var (_, i) in CalloutsByHandle) {
                    if (i?.Id != null && string.Equals(i.Id, calloutId.Trim(), StringComparison.OrdinalIgnoreCase)) {
                        info = i;
                        return true;
                    }
                }
            }
            return false;
        }

        internal static void SendAdditionalMessage(string message) {
            if (CalloutInfo != null) {
                CalloutInfo.AdditionalMessages.Add(message);
                RaiseCalloutEvent(CalloutInfo);
            }
            if (SetupController.GetConfig().addCalloutSuspectNamesFromMessages) TryAddCalloutSuspectNameFromText(message);
        }

        private static void TryAddCalloutSuspectNameFromText(string text) {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (var re in CalloutSuspectNamePatterns) {
                var m = re.Match(text);
                if (m.Success && m.Groups.Count > 1) {
                    string name = m.Groups[1].Value.Trim();
                    if (name.Length >= 3) DataController.AddCalloutSuspectNameToDatabase(name);
                    break;
                }
            }
        }
    }
}