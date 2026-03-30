// Ignore Spelling: Callsign Coords

using MDTPro.Data;
using MDTPro.Data.Reports;
using MDTPro.Setup;
using MDTPro.Utility;
using LSPD_First_Response.Mod.API;
using LSPD_First_Response.Mod.Callouts;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LspdFunc = LSPD_First_Response.Mod.API.Functions;

namespace MDTPro.EventListeners {
    public class CalloutEvents {
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
            public CalloutAcceptanceState AcceptanceState;
            public DateTime DisplayedTime;
            public DateTime? AcceptedTime = null;
            public DateTime? FinishedTime = null;
            public List<string> AdditionalMessages = new List<string>();

            internal CalloutInformation(Callout callout) {
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
                AcceptanceState = callout.AcceptanceState;
                DisplayedTime = DateTime.Now;
            }
        }

        internal delegate void CalloutEventHandler(CalloutInformation calloutInfo);
        internal static event CalloutEventHandler OnCalloutEvent;

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
            var info = new CalloutInformation(callout);

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

            OnCalloutEvent?.Invoke(info);
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
                info.AcceptanceState = callout != null ? callout.AcceptanceState : LspdFunc.GetCalloutAcceptanceState(handle);
                info.AcceptedTime = DateTime.Now;
            }
            OnCalloutEvent?.Invoke(info);
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
                info.AcceptanceState = callout != null ? callout.AcceptanceState : LspdFunc.GetCalloutAcceptanceState(handle);
                info.FinishedTime = DateTime.Now;
            }
            OnCalloutEvent?.Invoke(info);
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

        internal static void SendAdditionalMessage(string message) {
            if (CalloutInfo != null) {
                CalloutInfo.AdditionalMessages.Add(message);
                OnCalloutEvent?.Invoke(CalloutInfo);
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