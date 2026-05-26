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
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LspdFunc = LSPD_First_Response.Mod.API.Functions;

namespace MDTPro.EventListeners
{
    public class CalloutEvents
    {
        /// <summary>LSPDFR <see cref="CalloutAcceptanceState"/> (reference assembly is obfuscated; ILDASM cannot list it). Ordinal 0 = Pending, 1 = Responded, 2 = En route, 3 = Finished — see LSPDFR API docs / LMSDev LSPDFR-API.</summary>
        internal const int LspdfrAcceptanceRevealDelayMs = 450;

        internal static CalloutInformation CalloutInfo;
        internal static List<CalloutInformation> CalloutList = new List<CalloutInformation>();
        /// <summary>Optional unit / availability line shown on MDT clients (set via <c>POST /post/cadUnitStatus</c>).</summary>
        internal static string CadUnitStatus = "";
        internal static long CalloutSnapshotVersion { get; private set; }
        internal static DateTime CalloutSnapshotUpdatedUtc { get; private set; } = DateTime.UtcNow;
        private static readonly object CalloutListLock = new object();
        private static readonly List<(LHandle handle, CalloutInformation info)> CalloutsByHandle = new List<(LHandle, CalloutInformation)>();
        const int CloudShortTextMax = 120;
        const int CloudLongTextMax = 1000;
        const int CloudAdditionalMessageMaxCount = 10;
        const int CloudSnapshotHardMaxBytes = 64 * 1024;

        public class CalloutInformation
        {
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
            public CalloutAcceptanceState ClientAcceptanceState
            {
                get
                {
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

            internal CalloutInformation(Callout callout, LHandle handle)
            {
                Id = Guid.NewGuid().ToString("N");
                Name = callout.FriendlyName;
                Agency = Helper.GetAgencyNameFromScriptName(LSPD_First_Response.Mod.API.Functions.GetCurrentAgencyScriptName()) ?? LSPD_First_Response.Mod.API.Functions.GetCurrentAgencyScriptName();
                // thank you opus49
                if (callout.ScriptInfo is CalloutInterfaceAPI.CalloutInterfaceAttribute calloutInterfaceInfo)
                {
                    if (calloutInterfaceInfo.Agency.Length > 0)
                    {
                        Agency = calloutInterfaceInfo.Agency;
                    }
                    if (calloutInterfaceInfo.Priority.Length > 0)
                    {
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
        internal static CalloutAcceptanceState GetCiAwareAcceptanceState(LHandle handle, Callout resolvedCallout)
        {
            if (resolvedCallout?.ScriptInfo is CalloutInterfaceAPI.CalloutInterfaceAttribute)
            {
                CalloutAcceptanceState handleState = handle != null ? LspdFunc.GetCalloutAcceptanceState(handle) : resolvedCallout.AcceptanceState;
                if (CalloutInterfaceAPI.Functions.IsCalloutInterfaceAvailable && handle != null)
                {
                    try
                    {
                        var ciCallout = CalloutHandleResolver.TryGetCalloutFromCalloutInterfaceOnly(handle);
                        if (ciCallout != null)
                        {
                            var ciState = ciCallout.AcceptanceState;
                            if (handleState == CalloutAcceptanceState.Pending && ciState != CalloutAcceptanceState.Pending)
                                return handleState;
                            if (handleState != CalloutAcceptanceState.Pending && ciState == CalloutAcceptanceState.Pending)
                                return ciState;
                            return ciState;
                        }
                    }
                    catch { }
                    // Resolver failed: if LSPDFR handle is still pending, do not trust the proxy instance’s “responded” read.
                    if (handleState == CalloutAcceptanceState.Pending)
                        return CalloutAcceptanceState.Pending;
                    return resolvedCallout.AcceptanceState;
                }
                return resolvedCallout.AcceptanceState;
            }
            if (handle != null)
            {
                try
                {
                    return LspdFunc.GetCalloutAcceptanceState(handle);
                }
                catch
                {
                    return resolvedCallout?.AcceptanceState ?? CalloutAcceptanceState.Pending;
                }
            }
            return resolvedCallout?.AcceptanceState ?? CalloutAcceptanceState.Pending;
        }

        internal sealed class CalloutCapabilities
        {
            public bool closeSupported = true;
            public string closeMode = "currentOnly";
            public bool cadStatusSupported = true;
        }

        internal sealed class LocalLegacyCalloutSnapshot
        {
            public List<LocalLegacyCalloutProjection> callouts;
            public string cadUnitStatus;
            public DateTime observedAtUtc;
            public DateTime updatedAtUtc;
            public long calloutSnapshotVersion;
            public CalloutCapabilities capabilities;
        }

        internal sealed class CloudCalloutSnapshot
        {
            public List<JObject> callouts;
            public string cadUnitStatus;
            public DateTime observedAtUtc;
            public DateTime updatedAtUtc;
            public long calloutSnapshotVersion;
            public CalloutCapabilities capabilities;
        }

        internal class CloudCalloutProjection
        {
            public string Id;
            public string Name;
            public string Description;
            public string Message;
            public string Advisory;
            public string Callsign;
            public string Agency;
            public string Priority;
            public Location Location;
            public CalloutAcceptanceState AcceptanceState;
            public DateTime DisplayedTime;
            public DateTime? AcceptedTime;
            public DateTime? FinishedTime;
            public List<string> AdditionalMessages;
            public bool IsCurrent;
            public List<string> AvailableActions;
        }

        internal sealed class LocalLegacyCalloutProjection : CloudCalloutProjection
        {
            public float[] Coords;
        }

        internal delegate void CalloutEventHandler(CalloutInformation calloutInfo);
        internal static event CalloutEventHandler OnCalloutEvent;

        /// <summary>After MDT <c>accept</c> or in-game accept, allow clients to see LSPDFR state (game thread).</summary>
        internal static void MarkLspdfrAcceptanceExposedForCalloutId(string calloutId)
        {
            if (string.IsNullOrWhiteSpace(calloutId)) return;
            var id = calloutId.Trim();
            lock (CalloutListLock)
            {
                foreach (var (h, i) in CalloutsByHandle)
                {
                    if (i?.Id == null || !string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                    var c = CalloutHandleResolver.TryGetCallout(h);
                    i.AcceptanceState = GetCiAwareAcceptanceState(h, c);
                    i.LspdfrAcceptanceExposedToMdt = true;
                    if (i.AcceptedTime == null) i.AcceptedTime = DateTime.Now;
                    TouchSnapshotUnlocked();
                    break;
                }
            }
        }

        static void RaiseCalloutEvent(CalloutInformation info)
        {
            lock (CalloutListLock)
            {
                ApplyLspdfrAcceptanceVisibilityRulesUnlocked();
                TouchSnapshotUnlocked();
            }
            OnCalloutEvent?.Invoke(info);
            MDTPro.Cloud.CloudPluginBridge.RequestSessionRefresh();
        }

        static void TouchSnapshotUnlocked()
        {
            CalloutSnapshotVersion++;
            CalloutSnapshotUpdatedUtc = DateTime.UtcNow;
        }

        /// <summary>After the reveal delay, expose real state only when <see cref="GetCiAwareAcceptanceState"/> is not <see cref="CalloutAcceptanceState.Pending"/> (raw LSPDFR handle can read Responded while CI-aware is still Pending — do not flip <see cref="LspdfrAcceptanceExposedToMdt"/> early).</summary>
        static void ApplyLspdfrAcceptanceVisibilityRulesUnlocked()
        {
            foreach (var (h, info) in CalloutsByHandle)
            {
                if (info == null || h == null || info.FinishedTime != null || info.LspdfrAcceptanceExposedToMdt) continue;
                int elapsed = unchecked(Environment.TickCount - info.DisplayedAtTick);
                if (elapsed < 0) elapsed = int.MaxValue;
                if (elapsed < LspdfrAcceptanceRevealDelayMs) continue;
                try
                {
                    var c = CalloutHandleResolver.TryGetCallout(h);
                    var ciAware = GetCiAwareAcceptanceState(h, c);
                    if (ciAware == CalloutAcceptanceState.Pending) continue;
                    info.AcceptanceState = ciAware;
                    info.LspdfrAcceptanceExposedToMdt = true;
                    if (info.AcceptedTime == null) info.AcceptedTime = DateTime.Now;
                }
                catch { }
            }
        }

        private const int MaxCalloutsInList = 20;
        private static bool _calloutHandlersRegistered;

        /// <summary>Subscribe to LSPDFR callout events (always — uses LSPDFR to resolve handles; CI is optional for metadata / sendMessage).</summary>
        internal static void AddCalloutEventHandlers()
        {
            if (_calloutHandlersRegistered) return;
            _calloutHandlersRegistered = true;
            LSPD_First_Response.Mod.API.Events.OnCalloutDisplayed += OnCalloutDisplayedForCi;
            LSPD_First_Response.Mod.API.Events.OnCalloutFinished += OnCalloutFinishedForCi;
            LSPD_First_Response.Mod.API.Events.OnCalloutAccepted += OnCalloutAcceptedForCi;
        }

        /// <summary>Detach LSPDFR callout handlers on plugin unload (same delegate instances as <see cref="AddCalloutEventHandlers"/>).</summary>
        internal static void RemoveCalloutEventHandlers()
        {
            if (!_calloutHandlersRegistered) return;
            LSPD_First_Response.Mod.API.Events.OnCalloutDisplayed -= OnCalloutDisplayedForCi;
            LSPD_First_Response.Mod.API.Events.OnCalloutFinished -= OnCalloutFinishedForCi;
            LSPD_First_Response.Mod.API.Events.OnCalloutAccepted -= OnCalloutAcceptedForCi;
            _calloutHandlersRegistered = false;
        }

        private static void OnCalloutDisplayedForCi(LHandle handle)
        {
            if (handle == null) return;
            Callout callout = CalloutHandleResolver.TryGetCallout(handle);
            if (callout == null)
            {
                Helper.Log("MDT Pro: OnCalloutDisplayed — could not resolve Callout from handle (LSPDFR + CalloutInterface). Active Call list will miss this dispatch.", true, Helper.LogSeverity.Warning);
                return;
            }
            var info = new CalloutInformation(callout, handle);

            lock (CalloutListLock)
            {
                CalloutInfo = info;
                CalloutsByHandle.Insert(0, (handle, info));
                CalloutList.Insert(0, info);
                while (CalloutsByHandle.Count > MaxCalloutsInList)
                {
                    CalloutsByHandle.RemoveAt(CalloutsByHandle.Count - 1);
                    CalloutList.RemoveAt(CalloutList.Count - 1);
                }
            }

            if (SetupController.GetConfig().addCalloutSuspectNamesFromMessages)
            {
                TryAddCalloutSuspectNameFromText(info.Message);
                TryAddCalloutSuspectNameFromText(info.Advisory);
            }

            RaiseCalloutEvent(info);
            ScheduleLspdfrAcceptanceRevealBroadcast();
        }

        /// <summary>Re-evaluate handle state after the reveal delay so clients update even if no further LSPDFR events fire.</summary>
        static void ScheduleLspdfrAcceptanceRevealBroadcast()
        {
            GameFiber.StartNew(() =>
            {
                try
                {
                    GameFiber.Wait(LspdfrAcceptanceRevealDelayMs + 75);
                    lock (CalloutListLock)
                    {
                        ApplyLspdfrAcceptanceVisibilityRulesUnlocked();
                        TouchSnapshotUnlocked();
                    }
                    WebSocketHandler.BroadcastCalloutPayload();
                    MDTPro.Cloud.CloudPluginBridge.RequestSessionRefresh();
                }
                catch { }
            });
        }

        private static void OnCalloutAcceptedForCi(LHandle handle)
        {
            if (handle == null) return;
            var callout = CalloutHandleResolver.TryGetCallout(handle);
            CalloutInformation info = null;
            lock (CalloutListLock)
            {
                foreach (var (h, i) in CalloutsByHandle)
                {
                    if (object.ReferenceEquals(h, handle)) { info = i; break; }
                }
                if (info == null) return;
                info.AcceptanceState = GetCiAwareAcceptanceState(handle, callout);
                int elapsed = unchecked(Environment.TickCount - info.DisplayedAtTick);
                if (elapsed < 0) elapsed = int.MaxValue;
                if (elapsed >= LspdfrAcceptanceRevealDelayMs)
                {
                    info.LspdfrAcceptanceExposedToMdt = true;
                    if (info.AcceptedTime == null) info.AcceptedTime = DateTime.Now;
                }
            }
            RaiseCalloutEvent(info);
        }

        private static void OnCalloutFinishedForCi(LHandle handle)
        {
            if (handle == null) return;
            var callout = CalloutHandleResolver.TryGetCallout(handle);
            CalloutInformation info = null;
            lock (CalloutListLock)
            {
                foreach (var (h, i) in CalloutsByHandle)
                {
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
        internal static bool TryGetHandleForCalloutId(string calloutId, out LHandle handle)
        {
            handle = null;
            if (string.IsNullOrWhiteSpace(calloutId)) return false;
            lock (CalloutListLock)
            {
                foreach (var (h, info) in CalloutsByHandle)
                {
                    if (info?.Id != null && string.Equals(info.Id, calloutId.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        handle = h;
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool TryGetCalloutInformation(string calloutId, out CalloutInformation info)
        {
            info = null;
            if (string.IsNullOrWhiteSpace(calloutId)) return false;
            lock (CalloutListLock)
            {
                foreach (var (_, i) in CalloutsByHandle)
                {
                    if (i?.Id != null && string.Equals(i.Id, calloutId.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        info = i;
                        return true;
                    }
                }
            }
            return false;
        }

        internal static void MarkCalloutEnRoute(string calloutId)
        {
            if (string.IsNullOrWhiteSpace(calloutId)) return;
            var id = calloutId.Trim();
            lock (CalloutListLock)
            {
                foreach (var (_, info) in CalloutsByHandle)
                {
                    if (info?.Id == null || !string.Equals(info.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                    info.AcceptanceState = (CalloutAcceptanceState)2;
                    info.LspdfrAcceptanceExposedToMdt = true;
                    if (info.AcceptedTime == null) info.AcceptedTime = DateTime.Now;
                    TouchSnapshotUnlocked();
                    return;
                }
            }
        }

        internal static void MarkCalloutFinished(string calloutId)
        {
            if (string.IsNullOrWhiteSpace(calloutId)) return;
            var id = calloutId.Trim();
            lock (CalloutListLock)
            {
                foreach (var (_, info) in CalloutsByHandle)
                {
                    if (info?.Id == null || !string.Equals(info.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                    info.AcceptanceState = (CalloutAcceptanceState)3;
                    info.LspdfrAcceptanceExposedToMdt = true;
                    if (info.FinishedTime == null) info.FinishedTime = DateTime.Now;
                    TouchSnapshotUnlocked();
                    return;
                }
            }
        }

        internal static void AddTrackedAdditionalMessage(string calloutId, string message)
        {
            if (string.IsNullOrWhiteSpace(calloutId) || string.IsNullOrWhiteSpace(message)) return;
            var id = calloutId.Trim();
            lock (CalloutListLock)
            {
                foreach (var (_, info) in CalloutsByHandle)
                {
                    if (info?.Id == null || !string.Equals(info.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                    info.AdditionalMessages.Add(message.Trim());
                    TouchSnapshotUnlocked();
                    return;
                }
            }
        }

        /// <summary>
        /// LSPDFR exposes only <c>Functions.StopCurrentCallout()</c> in the inspected reference assembly, so Close may target only the newest tracked active callout.
        /// Older rows remain visible for context but cannot safely invoke the current-callout-only close command.
        /// </summary>
        internal static bool IsCurrentClosableCallout(string calloutId)
        {
            if (string.IsNullOrWhiteSpace(calloutId)) return false;
            var id = calloutId.Trim();
            lock (CalloutListLock)
            {
                ApplyLspdfrAcceptanceVisibilityRulesUnlocked();
                foreach (var (_, info) in CalloutsByHandle)
                {
                    if (info == null || info.FinishedTime != null) continue;
                    if (info.ClientAcceptanceState == CalloutAcceptanceState.Pending) continue;
                    return string.Equals(info.Id, id, StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        internal static void SetCadUnitStatus(string statusText)
        {
            lock (CalloutListLock)
            {
                CadUnitStatus = (statusText ?? "").Trim();
                TouchSnapshotUnlocked();
            }
            CalloutInterfaceCadPublisher.TryPublishCadUnitStatus(CadUnitStatus);
            WebSocketHandler.BroadcastCalloutPayload();
            MDTPro.Cloud.CloudPluginBridge.RequestSessionRefresh();
        }

        internal static LocalLegacyCalloutSnapshot BuildLocalLegacySnapshot()
        {
            lock (CalloutListLock)
            {
                ApplyLspdfrAcceptanceVisibilityRulesUnlocked();
                var currentClosableId = GetCurrentClosableCalloutIdUnlocked();
                return new LocalLegacyCalloutSnapshot
                {
                    callouts = CalloutList.Select(c => ToLocalLegacyProjection(c, currentClosableId)).ToList(),
                    cadUnitStatus = CadUnitStatus ?? "",
                    observedAtUtc = DateTime.UtcNow,
                    updatedAtUtc = CalloutSnapshotUpdatedUtc,
                    calloutSnapshotVersion = CalloutSnapshotVersion,
                    capabilities = new CalloutCapabilities()
                };
            }
        }

        internal static CloudCalloutSnapshot BuildCloudSnapshot()
        {
            lock (CalloutListLock)
            {
                ApplyLspdfrAcceptanceVisibilityRulesUnlocked();
                var currentClosableId = GetCurrentClosableCalloutIdUnlocked();
                var callouts = new List<JObject>();
                int totalBytes = 2;
                foreach (var c in CalloutList)
                {
                    var projected = ToCloudProjection(c, currentClosableId);
                    var bytes = System.Text.Encoding.UTF8.GetByteCount(projected.ToString(Formatting.None));
                    if (totalBytes + bytes > CloudSnapshotHardMaxBytes) break;
                    totalBytes += bytes;
                    callouts.Add(projected);
                }
                return new CloudCalloutSnapshot
                {
                    callouts = callouts,
                    cadUnitStatus = CadUnitStatus ?? "",
                    observedAtUtc = DateTime.UtcNow,
                    updatedAtUtc = CalloutSnapshotUpdatedUtc,
                    calloutSnapshotVersion = CalloutSnapshotVersion,
                    capabilities = new CalloutCapabilities()
                };
            }
        }

        static string GetCurrentClosableCalloutIdUnlocked()
        {
            foreach (var (_, info) in CalloutsByHandle)
            {
                if (info == null || info.FinishedTime != null) continue;
                if (info.ClientAcceptanceState == CalloutAcceptanceState.Pending) continue;
                return info.Id;
            }
            return null;
        }

        static LocalLegacyCalloutProjection ToLocalLegacyProjection(CalloutInformation info, string currentClosableId)
        {
            var p = new LocalLegacyCalloutProjection();
            FillProjection(p, info, currentClosableId);
            p.Coords = info?.Coords == null ? new float[0] : (float[])info.Coords.Clone();
            return p;
        }

        static JObject ToCloudProjection(CalloutInformation info, string currentClosableId)
        {
            if (info == null) return new JObject();
            var state = (int)info.ClientAcceptanceState;
            var isCurrent = !string.IsNullOrWhiteSpace(currentClosableId) && string.Equals(info.Id, currentClosableId, StringComparison.OrdinalIgnoreCase);
            var actions = BuildAvailableActionObject(info, isCurrent);
            return new JObject
            {
                ["schemaVersion"] = 1,
                ["id"] = CleanCloudShortText(info.Id),
                ["name"] = CleanCloudShortText(info.Name),
                ["description"] = CleanCloudLongText(info.Description),
                ["message"] = CleanCloudLongText(info.Message),
                ["advisory"] = CleanCloudLongText(info.Advisory),
                ["agency"] = CleanCloudShortText(info.Agency),
                ["priority"] = CleanCloudShortText(info.Priority),
                ["callsign"] = CleanCloudShortText(info.Callsign),
                ["status"] = CalloutStatusText(state),
                ["acceptanceState"] = state,
                ["location"] = BuildCloudLocation(info.Location),
                ["displayedTimeUtc"] = ToUtcString(info.DisplayedTime),
                ["acceptedTimeUtc"] = ToUtcString(info.AcceptedTime),
                ["finishedTimeUtc"] = ToUtcString(info.FinishedTime),
                ["additionalMessages"] = BuildCloudAdditionalMessages(info.AdditionalMessages),
                ["isCurrent"] = isCurrent,
                ["availableActions"] = actions
            };
        }

        static void FillProjection(CloudCalloutProjection p, CalloutInformation info, string currentClosableId)
        {
            if (p == null || info == null) return;
            p.Id = info.Id;
            p.Name = info.Name;
            p.Description = info.Description;
            p.Message = info.Message;
            p.Advisory = info.Advisory;
            p.Callsign = info.Callsign;
            p.Agency = info.Agency;
            p.Priority = info.Priority;
            p.Location = info.Location;
            p.AcceptanceState = info.ClientAcceptanceState;
            p.DisplayedTime = info.DisplayedTime;
            p.AcceptedTime = info.AcceptedTime;
            p.FinishedTime = info.FinishedTime;
            p.AdditionalMessages = info.AdditionalMessages == null ? new List<string>() : new List<string>(info.AdditionalMessages);
            p.IsCurrent = !string.IsNullOrWhiteSpace(currentClosableId) && string.Equals(info.Id, currentClosableId, StringComparison.OrdinalIgnoreCase);
            p.AvailableActions = BuildAvailableActions(info, p.IsCurrent);
        }

        static JObject BuildCloudLocation(Location location)
        {
            return new JObject
            {
                ["street"] = CleanCloudShortText(location?.Street),
                ["area"] = CleanCloudShortText(location?.Area),
                ["county"] = CleanCloudShortText(location?.County),
                ["postal"] = CleanCloudShortText(location?.Postal)
            };
        }

        static JArray BuildCloudAdditionalMessages(List<string> messages)
        {
            var arr = new JArray();
            if (messages == null) return arr;
            foreach (var message in messages.Take(CloudAdditionalMessageMaxCount))
                arr.Add(CleanCloudLongText(message));
            return arr;
        }

        static JObject BuildAvailableActionObject(CalloutInformation info, bool isCurrentClosable)
        {
            var pending = info != null && info.FinishedTime == null && info.ClientAcceptanceState == CalloutAcceptanceState.Pending;
            var active = info != null && info.FinishedTime == null && !pending;
            return new JObject
            {
                ["attach"] = pending,
                ["close"] = active && isCurrentClosable,
                ["enRoute"] = active,
                ["sendMessage"] = active
            };
        }

        static List<string> BuildAvailableActions(CalloutInformation info, bool isCurrentClosable)
        {
            var actions = new List<string>();
            if (info == null || info.FinishedTime != null) return actions;
            if (info.ClientAcceptanceState == CalloutAcceptanceState.Pending)
                actions.Add("attach");
            else
            {
                actions.Add("enRoute");
                actions.Add("sendMessage");
                if (isCurrentClosable) actions.Add("close");
            }
            return actions;
        }

        static string CleanCloudShortText(string value) => CleanCloudText(value, CloudShortTextMax);

        static string CleanCloudLongText(string value) => CleanCloudText(value, CloudLongTextMax);

        static string CleanCloudText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var clean = Regex.Replace(value, "~[a-z0-9_]+~", "", RegexOptions.IgnoreCase).Trim();
            clean = Regex.Replace(clean, @"[\u0000-\u0008\u000B\u000C\u000E-\u001F]", "");
            return clean.Length <= maxLength ? clean : clean.Substring(0, maxLength);
        }

        static string CalloutStatusText(int state)
        {
            switch (state)
            {
                case 0: return "Pending";
                case 1: return "Responded";
                case 2: return "EnRoute";
                case 3: return "Finished";
                default: return "Unknown";
            }
        }

        static string ToUtcString(DateTime value)
        {
            var dt = value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Local) : value;
            return dt.ToUniversalTime().ToString("o");
        }

        static JToken ToUtcString(DateTime? value)
        {
            if (value == null) return JValue.CreateNull();
            return ToUtcString(value.Value);
        }

        internal static void SendAdditionalMessage(string message)
        {
            if (CalloutInfo != null)
            {
                CalloutInfo.AdditionalMessages.Add(message);
                RaiseCalloutEvent(CalloutInfo);
            }
            if (SetupController.GetConfig().addCalloutSuspectNamesFromMessages) TryAddCalloutSuspectNameFromText(message);
        }

        private static void TryAddCalloutSuspectNameFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (var re in CalloutSuspectNamePatterns)
            {
                var m = re.Match(text);
                if (m.Success && m.Groups.Count > 1)
                {
                    string name = m.Groups[1].Value.Trim();
                    if (name.Length >= 3) DataController.AddCalloutSuspectNameToDatabase(name);
                    break;
                }
            }
        }
    }
}
