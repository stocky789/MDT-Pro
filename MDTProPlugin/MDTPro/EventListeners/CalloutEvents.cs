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

namespace MDTPro.EventListeners {
    public class CalloutEvents {
        internal static CalloutInformation CalloutInfo;

        public class CalloutInformation {
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

        internal static void AddCalloutEventWithCI() {
            LSPD_First_Response.Mod.API.Events.OnCalloutDisplayed += Events_OnCalloutDisplayed;
            LSPD_First_Response.Mod.API.Events.OnCalloutFinished += Events_OnCalloutFinished;
            LSPD_First_Response.Mod.API.Events.OnCalloutAccepted += Events_OnCalloutAccepted;
            void Events_OnCalloutDisplayed(LHandle handle) {
                if (handle == null) return;
                Callout callout = CalloutInterface.API.Functions.GetCalloutFromHandle(handle);

                CalloutInfo = new CalloutInformation(callout);

                if (SetupController.GetConfig().addCalloutSuspectNamesFromMessages) {
                    TryAddCalloutSuspectNameFromText(CalloutInfo.Message);
                    TryAddCalloutSuspectNameFromText(CalloutInfo.Advisory);
                }

                OnCalloutEvent?.Invoke(CalloutInfo);
            }

            void Events_OnCalloutAccepted(LHandle handle) {
                if (handle == null || CalloutInfo == null) return;
                Callout callout = CalloutInterface.API.Functions.GetCalloutFromHandle(handle);

                CalloutInfo.AcceptanceState = callout.AcceptanceState;
                CalloutInfo.AcceptedTime = DateTime.Now;

                OnCalloutEvent?.Invoke(CalloutInfo);
            }

            void Events_OnCalloutFinished(LHandle handle) {
                if (handle == null || CalloutInfo == null) return;
                Callout callout = CalloutInterface.API.Functions.GetCalloutFromHandle(handle);

                CalloutInfo.AcceptanceState = callout.AcceptanceState;
                CalloutInfo.FinishedTime = DateTime.Now;

                OnCalloutEvent?.Invoke(CalloutInfo);
            }
        }

        /// <summary>Patterns to extract a suspect name from callout dispatch text (e.g. "associated with Joe Thomas" -> Joe Thomas).</summary>
        private static readonly Regex[] CalloutSuspectNamePatterns = new[] {
            new Regex(@"associated with\s+([A-Za-z]+\s+[A-Za-z]+)", RegexOptions.IgnoreCase),
            new Regex(@"sightings of\s+([A-Za-z]+\s+[A-Za-z]+)", RegexOptions.IgnoreCase),
            new Regex(@"person (?:named\s+)?([A-Za-z]+\s+[A-Za-z]+)", RegexOptions.IgnoreCase),
            new Regex(@"identified as\s+([A-Za-z]+\s+[A-Za-z]+)", RegexOptions.IgnoreCase),
            new Regex(@"suspect\s+([A-Za-z]+\s+[A-Za-z]+)", RegexOptions.IgnoreCase),
        };

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