using MDTPro.Setup;
using MDTPro.Data;
using MDTPro.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace MDTPro.Cloud {
    internal enum CloudConnectionState {
        Disconnected = 0,
        Connected = 1
    }

    internal static class CloudPluginBridge {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly object StartLock = new object();
        private static readonly object StatusLock = new object();
        private static readonly object LspdfrShiftStateLock = new object();
        private static readonly AutoResetEvent PollWake = new AutoResetEvent(false);
        private static int _lspdfrShiftStartPending;
        private static string _trackedLspdfrShiftId;
        private static DateTimeOffset? _trackedLspdfrShiftStartedUtc;
        private static bool _started;
        private static CloudConnectionState _connectionState = CloudConnectionState.Disconnected;
        private static string _connectionDetail = "Not connected";
        private static readonly object MaintenanceEchoLock = new object();
        private static Guid? _lastMaintenanceEchoId;
        private static int _lastMaintenanceEchoRevision = int.MinValue;
        /// <summary>RTT of the previous session POST (ms), sent on the next POST so the cloud can show bridge latency in the portal.</summary>
        private static int? _roundTripMsToReportOnNextSessionPost;

        internal static CloudConnectionState ConnectionState {
            get {
                lock (StatusLock) return _connectionState;
            }
        }

        internal static string ConnectionStatusText => ConnectionState == CloudConnectionState.Connected ? "Connected" : "Disconnected";

        internal static string ConnectionDetail {
            get {
                lock (StatusLock) return _connectionDetail;
            }
        }

        internal static bool HasSavedLogin() {
            try {
                Config cfg = SetupController.GetConfig();
                CloudCredentials credentials = CloudCredentialStore.Load(cfg);
                return CloudMode.IsEnabled()
                    && !string.IsNullOrWhiteSpace(cfg.cloudInstallId)
                    && !string.IsNullOrWhiteSpace(credentials.DeviceToken)
                    && (!string.IsNullOrWhiteSpace(credentials.AccessToken) || !string.IsNullOrWhiteSpace(credentials.RefreshToken));
            } catch {
                return false;
            }
        }

        internal static void Start() {
            lock (StartLock) {
                if (_started) return;
                _started = true;
            }

            Thread thread = new Thread(() => {
                try {
                    RunLoop();
                } finally {
                    lock (StartLock) {
                        _started = false;
                    }
                }
            }) {
                IsBackground = true,
                Name = "cloud-plugin-bridge"
            };
            thread.Start();
        }

        private static void RunLoop() {
            while (Server.RunServer) {
                try {
                    PollOnce();
                } catch (Exception ex) {
                    MarkDisconnected(ex.Message);
                    Helper.Log($"Cloud plugin bridge poll failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                }
                PollWake.WaitOne(5000);
            }
            MarkDisconnected("Plugin stopped");
        }

        /// <summary>Queue LSPDFR on-duty cloud shift start for the next bridge poll (same HTTP path as session + commands). Wakes the poll loop immediately.</summary>
        internal static void RequestLspdfrDutyShiftStartFromBridge() {
            if (!CloudMode.IsEnabled()) return;
            Interlocked.Exchange(ref _lspdfrShiftStartPending, 1);
            PollWake.Set();
        }

        internal static void RequestSessionRefresh() {
            if (!CloudMode.IsEnabled()) return;
            PollWake.Set();
        }

        internal static bool TryPollOnceForRecovery(out string detail) {
            detail = null;
            if (!CloudMode.IsEnabled()) {
                detail = "Cloud mode disabled";
                return false;
            }
            try {
                PollOnce();
                detail = ConnectionDetail;
                return ConnectionState == CloudConnectionState.Connected;
            } catch (Exception ex) {
                MarkDisconnected(ex.Message);
                detail = ex.Message;
                Helper.Log($"Cloud plugin bridge recovery poll failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                return false;
            }
        }

        /// <summary>Ends the cloud shift started this LSPDFR session; call from the game thread before <see cref="Server.Stop"/>.</summary>
        internal static void TryEndTrackedLspdfrShiftBlocking() {
            string shiftId;
            DateTimeOffset? startedUtc;
            lock (LspdfrShiftStateLock) {
                shiftId = _trackedLspdfrShiftId;
                startedUtc = _trackedLspdfrShiftStartedUtc;
                _trackedLspdfrShiftId = null;
                _trackedLspdfrShiftStartedUtc = null;
            }
            if (string.IsNullOrWhiteSpace(shiftId) || startedUtc == null) return;
            try {
                if (!CloudMode.IsEnabled()) return;
                Config cfg = SetupController.GetConfig();
                CloudCredentials credentials = CloudCredentialStore.Load(cfg);
                if (string.IsNullOrWhiteSpace(credentials.AccessToken) && !CloudCredentialStore.TryRefresh(cfg, ref credentials)) {
                    Helper.Log("LSPDFR auto shift: could not end cloud shift (no token).", false, Helper.LogSeverity.Warning);
                    return;
                }
                DateTimeOffset ended = DateTimeOffset.UtcNow;
                var payload = new JObject { ["source"] = "lspdfrDuty" };
                if (!LspdfrPostShiftUpsert(cfg, ref credentials, shiftId, "ended", startedUtc, ended, payload))
                    Helper.Log("LSPDFR auto shift: cloud end shift request failed.", false, Helper.LogSeverity.Warning);
            } catch (Exception ex) {
                Helper.Log($"LSPDFR auto shift: end failed: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        private static void PollOnce() {
            if (!CloudMode.IsEnabled()) {
                MarkDisconnected("Cloud mode disabled");
                CloudEncounterLeaseClient.ReleaseCurrent();
                return;
            }
            Config cfg = SetupController.GetConfig();
            CloudCredentials credentials = CloudCredentialStore.Load(cfg);
            if (string.IsNullOrWhiteSpace(credentials.DeviceToken) || string.IsNullOrWhiteSpace(cfg.cloudInstallId)) {
                MarkDisconnected("Cloud login missing");
                CloudEncounterLeaseClient.ReleaseCurrent();
                return;
            }
            if (string.IsNullOrWhiteSpace(credentials.AccessToken) && !CloudCredentialStore.TryRefresh(cfg, ref credentials)) {
                MarkDisconnected("Cloud login expired");
                CloudEncounterLeaseClient.ReleaseCurrent();
                return;
            }
            if (CloudStopThePedBlock.ShouldBlockMdtCloudApiTraffic()) {
                CloudStopThePedBlock.NotifyBlockedIfNeeded();
                MarkDisconnected(CloudStopThePedBlock.BlockedConnectionDetail());
                CloudEncounterLeaseClient.ReleaseCurrent();
                return;
            }
            PostSession(cfg, ref credentials);
            if (Interlocked.Exchange(ref _lspdfrShiftStartPending, 0) == 1) TryLspdfrDutyShiftStartAfterSession(cfg, ref credentials);
            JArray commands = GetArray($"/api/mdt/plugin/commands/pending?installId={Uri.EscapeDataString(cfg.cloudInstallId)}", cfg, ref credentials);
            foreach (JObject command in commands) {
                Guid id;
                if (!Guid.TryParse(command.Value<string>("id"), out id)) continue;
                string status = ExecuteCommand(command, cfg, out JObject result) ? "completed" : "failed";
                Ack(id, status, result, cfg, ref credentials);
            }
            MarkConnected();
        }

        internal static void MarkConnected() {
            lock (StatusLock) {
                _connectionState = CloudConnectionState.Connected;
                _connectionDetail = "Connected";
            }
        }

        private static void MarkDisconnected(string detail) {
            lock (StatusLock) {
                _connectionState = CloudConnectionState.Disconnected;
                _connectionDetail = string.IsNullOrWhiteSpace(detail) ? "Disconnected" : detail;
            }
        }

        static JArray BuildWarrantsJson(System.Collections.Generic.List<WarrantCharge> warrants) {
            var arr = new JArray();
            if (warrants == null) return arr;
            foreach (var w in warrants) {
                if (w == null) continue;
                var o = new JObject {
                    ["name"] = w.Name,
                    ["severity"] = w.Severity,
                    ["issuedAtUtc"] = w.IssuedAtUtc
                };
                if (!string.IsNullOrWhiteSpace(w.ClearedAtUtc)) o["clearedAtUtc"] = w.ClearedAtUtc;
                if (!string.IsNullOrWhiteSpace(w.ClearedByReportType)) o["clearedByReportType"] = w.ClearedByReportType;
                if (!string.IsNullOrWhiteSpace(w.ClearedByReportId)) o["clearedByReportId"] = w.ClearedByReportId;
                arr.Add(o);
            }
            return arr;
        }

        /// <summary>True when the script context has stopped ticking (overlay / alt-tab), not merely <c>Game.IsPaused</c> from LemonUI.</summary>
        private static bool IsGameThreadFrozenForLookup() {
            return GameThreadHeartbeat.IsGameThreadFrozen();
        }

        private static string ReverseLookupName(string name) {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return string.Join(" ", name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Reverse());
        }

        private static bool ExecuteCommand(JObject command, Config cfg, out JObject result) {
            result = new JObject { ["source"] = "plugin" };
            string name = command.Value<string>("command") ?? "";
            JObject payload = command["payload"] as JObject ?? command["Payload"] as JObject;
            if (name.Equals("vehicleLookup", StringComparison.OrdinalIgnoreCase)) {
                string query = payload?.Value<string>("query") ?? payload?.Value<string>("plate") ?? payload?.Value<string>("licensePlate") ?? "";
                if (string.IsNullOrWhiteSpace(query)) {
                    result["error"] = "query is required";
                    return false;
                }
                MDTProVehicleData vehicle = DataController.GetContextVehicleIfValid();
                string resultSource = "live-context";
                if (vehicle == null || !DataController.VehicleMatchesLookup(vehicle, query)) {
                    resultSource = "live-bridge";
                    bool frozen = IsGameThreadFrozenForLookup();
                    if (frozen) {
                        vehicle = DataController.GetCachedNearbyVehicleByPlateOrVin(query) ?? DataController.GetVehicleByPlateOrVin(query);
                        if (vehicle != null) resultSource = "nearby-cache";
                    } else {
                        bool completed = DataController.TryResolveVehicleFromLiveWorldByPlateOrVinBlocking(
                            query,
                            2500,
                            out vehicle,
                            out string failureReason,
                            GameWorkJobTrigger.CloudCommand,
                            "cloud-vehicle-lookup");
                        if (!completed) {
                            vehicle = DataController.GetCachedNearbyVehicleByPlateOrVin(query) ?? DataController.GetVehicleByPlateOrVin(query);
                            if (vehicle != null) resultSource = "nearby-cache";
                            if (vehicle == null) {
                                result["found"] = false;
                                result["error"] = string.IsNullOrWhiteSpace(failureReason) ? "game_busy" : failureReason;
                                return false;
                            }
                        }
                        if (vehicle == null) {
                            vehicle = DataController.GetCachedNearbyVehicleByPlateOrVin(query) ?? DataController.GetVehicleByPlateOrVin(query);
                            if (vehicle != null) resultSource = "nearby-cache";
                        }
                    }
                }
                if (vehicle == null) {
                    result["found"] = false;
                    result["error"] = IsGameThreadFrozenForLookup() ? "paused_no_cached_match" : "not_found";
                    return false;
                }
                if (!DataController.VehicleMatchesLookup(vehicle, query)) {
                    result["found"] = false;
                    result["error"] = "identity_mismatch";
                    return false;
                }
                bool refreshedForContext = DataController.TryRefreshVehicleDocumentsForCloudLookup(vehicle, query);
                bool liveContext = DataController.IsContextVehicleLookup(vehicle, query);
                if (refreshedForContext || DataController.IsContextVehicleLookup(vehicle, query))
                    resultSource = "live-context";
                JObject vehiclePayload = JObject.FromObject(new {
                    vehicle.LicensePlate,
                    vehicle.ModelName,
                    vehicle.ModelDisplayName,
                    vehicle.IsStolen,
                    vehicle.Owner,
                    vehicle.Color,
                    vehicle.VinStatus,
                    vehicle.Make,
                    vehicle.Model,
                    vehicle.PrimaryColor,
                    vehicle.SecondaryColor,
                    vehicle.PrimaryColorSpecific,
                    vehicle.SecondaryColorSpecific,
                    vehicle.VehicleIdentificationNumber,
                    vehicle.RegistrationStatus,
                    RegistrationExpiration = vehicle.RegistrationExpirationVerifiedFromLiveDocument ? vehicle.RegistrationExpiration : null,
                    vehicle.RegistrationExpirationVerifiedFromLiveDocument,
                    vehicle.InsuranceStatus,
                    InsuranceExpiration = vehicle.InsuranceExpirationVerifiedFromLiveDocument ? vehicle.InsuranceExpiration : null,
                    vehicle.InsuranceExpirationVerifiedFromLiveDocument,
                    vehicle.BOLOs,
                    CanModifyBOLOs = liveContext,
                    resultSource,
                    isLiveContext = liveContext,
                    refreshedForContext
                });
                CloudIdentityCache.ApplyVehicleId(vehiclePayload);
                result["found"] = true;
                result["vehicle"] = vehiclePayload;
                return true;
            }
            if (name.Equals("vehicleBoloApply", StringComparison.OrdinalIgnoreCase)) {
                string action = (payload?.Value<string>("action") ?? payload?.Value<string>("Action") ?? "add").Trim().ToLowerInvariant();
                string plate = payload?.Value<string>("licensePlate") ?? payload?.Value<string>("LicensePlate") ?? payload?.Value<string>("plate") ?? "";
                string reason = payload?.Value<string>("reason") ?? payload?.Value<string>("Reason") ?? "";
                string issuedBy = payload?.Value<string>("issuedBy") ?? payload?.Value<string>("IssuedBy") ?? "LSPD";
                string modelDisplayName = payload?.Value<string>("modelDisplayName") ?? payload?.Value<string>("ModelDisplayName") ?? payload?.Value<string>("modelName") ?? payload?.Value<string>("ModelName");
                DateTime expiresAt = DateTime.UtcNow.AddDays(7);
                string expiresAtText = payload?.Value<string>("expiresAt") ?? payload?.Value<string>("ExpiresAt");
                if (!string.IsNullOrWhiteSpace(expiresAtText) && DateTime.TryParse(expiresAtText, out DateTime parsedExpires))
                    expiresAt = parsedExpires;

                bool success;
                if (action == "remove") {
                    if (string.IsNullOrWhiteSpace(reason)) {
                        result["error"] = "reason is required for bolo removal";
                        return false;
                    }
                    success = DataController.TryRemoveBOLOFromVehicleOrStub(plate, reason);
                } else if (action == "add") {
                    if (string.IsNullOrWhiteSpace(plate) || string.IsNullOrWhiteSpace(reason)) {
                        result["error"] = "licensePlate and reason are required";
                        return false;
                    }
                    success = DataController.TryAddBOLOByPlate(plate, reason, expiresAt, issuedBy, modelDisplayName);
                } else {
                    result["error"] = "unsupported action";
                    result["action"] = action;
                    return false;
                }

                result["action"] = action;
                result["licensePlate"] = plate;
                result["reason"] = reason;
                result["applied"] = success;
                if (payload != null && payload["vehicleId"] != null) result["vehicleId"] = payload["vehicleId"];
                return success;
            }
            if (name.Equals("pedLookup", StringComparison.OrdinalIgnoreCase) || name.Equals("personLookup", StringComparison.OrdinalIgnoreCase)) {
                string query = payload?.Value<string>("query") ?? payload?.Value<string>("name") ?? payload?.Value<string>("pedName") ?? "";
                Guid.TryParse(payload?.Value<string>("personId") ?? payload?.Value<string>("PersonId"), out Guid expectPersonId);
                bool hasExpectPersonId = expectPersonId != Guid.Empty;
                MDTProPedData ped = null;
                bool refreshedForContext = false;
                bool contextMatch = DataController.TryRefreshContextPedDocumentsForCloudLookup(query, out ped, out refreshedForContext);
                string resultSource = contextMatch ? "live-context" : "live-bridge";
                if (ped == null && !string.IsNullOrWhiteSpace(query)) {
                    string trimmedQuery = query.Trim();
                    bool frozen = IsGameThreadFrozenForLookup();
                    if (frozen) {
                        ped = DataController.GetPedDataByName(trimmedQuery) ?? DataController.GetPedDataByName(ReverseLookupName(trimmedQuery));
                    } else {
                        bool completed = DataController.TryResolvePedFromLiveWorldByNameBlocking(
                            query,
                            1800,
                            out ped,
                            out string failureReason,
                            GameWorkJobTrigger.CloudCommand,
                            "cloud-ped-lookup");
                        if (!completed) {
                            ped = DataController.GetPedDataByName(trimmedQuery) ?? DataController.GetPedDataByName(ReverseLookupName(trimmedQuery));
                            if (ped == null) {
                                result["found"] = false;
                                result["error"] = string.IsNullOrWhiteSpace(failureReason) ? "game_busy" : failureReason;
                                return false;
                            }
                        }
                        if (ped == null)
                            ped = DataController.GetPedDataByName(trimmedQuery) ?? DataController.GetPedDataByName(ReverseLookupName(trimmedQuery));
                    }
                }
                if (ped == null) {
                    result["found"] = false;
                    result["error"] = IsGameThreadFrozenForLookup() ? "paused_no_cached_match" : "not_found";
                    return false;
                }
                if (!DataController.PedMatchesLookup(ped, query)) {
                    result["found"] = false;
                    result["error"] = "identity_mismatch";
                    return false;
                }
                if (hasExpectPersonId) {
                    string mounted = CloudIdentityCache.GetPersonId(ped);
                    if (!string.IsNullOrWhiteSpace(mounted) &&
                        Guid.TryParse(mounted.Trim(), out var mountedGuid) &&
                        mountedGuid != expectPersonId) {
                        result["found"] = false;
                        result["error"] = "person_id_mismatch";
                        return false;
                    }
                }
                bool authorityDriverLicenseHydrated = DataController.TryHydratePedDocumentsForCloudDisplay(ped, query);
                JObject pedPayload = JObject.FromObject(new {
                    ped.Name,
                    ped.FirstName,
                    ped.LastName,
                    ped.ModelHash,
                    ped.ModelName,
                    ped.PortraitVariantDrawable,
                    ped.PortraitVariantTexture,
                    ped.PortraitFaceDrawable,
                    ped.PortraitFaceTexture,
                    ped.Birthday,
                    ped.Gender,
                    ped.Address,
                    ped.IsInGang,
                    ped.AdvisoryText,
                    ped.TimesStopped,
                    ped.IsWanted,
                    ped.WarrantText,
                    ped.IsOnProbation,
                    ped.IsOnParole,
                    ped.LicenseStatus,
                    ped.LicenseExpiration,
                    ped.LicenseExpirationVerifiedFromLiveDocument,
                    authorityDriverLicenseHydrated,
                    ped.WeaponPermitStatus,
                    ped.WeaponPermitExpiration,
                    ped.WeaponPermitType,
                    ped.FishingPermitStatus,
                    ped.FishingPermitExpiration,
                    ped.HuntingPermitStatus,
                    ped.HuntingPermitExpiration,
                    ped.IncarceratedUntil,
                    ped.IsDeceased,
                    ped.DeceasedAt,
                    ped.IdentificationHistory,
                    ped.Citations,
                    ped.Arrests,
                    resultSource,
                    isLiveContext = true,
                    refreshedForContext
                });
                var warrantsJson = BuildWarrantsJson(ped.Warrants);
                pedPayload["warrants"] = warrantsJson;
                pedPayload["Warrants"] = warrantsJson;
                try {
                    var cases = DataController.GetCourtCasesForPedName(ped.Name);
                    pedPayload["CourtCases"] = cases == null || cases.Count == 0 ? new JArray() : JArray.FromObject(cases);
                } catch {
                    pedPayload["CourtCases"] = new JArray();
                }
                CloudIdentityCache.ApplyPersonId(pedPayload);
                result["found"] = true;
                result["ped"] = pedPayload;
                return true;
            }
            if (name.Equals("alprClear", StringComparison.OrdinalIgnoreCase) || name.Equals("alprAcknowledge", StringComparison.OrdinalIgnoreCase)) {
                if (!cfg.alprCloudEnabled) return false;
                GameFiberHttpBridge.EnqueueFireAndForget(
                    () => ALPR.ALPRController.Clear(),
                    "cloud-alpr-clear",
                    GameWorkJobTrigger.CloudCommand,
                    GameWorkPriority.Interactive,
                    "cloud-alpr-clear");
                return true;
            }
            if (name.Equals("panic", StringComparison.OrdinalIgnoreCase)) {
                if (!cfg.backupPanicCloudEnabled) return false;
                return BackupHelper.RequestPanicBackup();
            }
            if (name.Equals("backup", StringComparison.OrdinalIgnoreCase)) {
                if (!cfg.backupPanicCloudEnabled) return false;
                return BackupHelper.RequestBackup("LocalPatrol", 2);
            }
            if (name.Equals("citationHandoff", StringComparison.OrdinalIgnoreCase)) {
                if (payload == null) return false;
                string offender = payload.Value<string>("offenderPedName") ?? payload.Value<string>("OffenderPedName");
                if (string.IsNullOrWhiteSpace(offender)) return false;
                JArray arr = payload["charges"] as JArray ?? payload["Charges"] as JArray;
                if (arr == null || arr.Count == 0) return false;
                var list = new List<PRHelper.CitationHandoutCharge>();
                foreach (JToken t in arr) {
                    if (!(t is JObject c)) continue;
                    string chargeName = c.Value<string>("name") ?? c.Value<string>("Name");
                    if (string.IsNullOrWhiteSpace(chargeName)) continue;
                    int fine = c.Value<int?>("fine") ?? c.Value<int?>("Fine") ?? 0;
                    bool arrest = c.Value<bool?>("isArrestable") ?? c.Value<bool?>("IsArrestable") ?? false;
                    list.Add(new PRHelper.CitationHandoutCharge { Name = chargeName, Fine = fine, IsArrestable = arrest });
                }
                if (list.Count == 0) return false;
                if (Main.usePR) {
                    GameFiberHttpBridge.EnqueueFireAndForget(
                        () => PRHelper.GiveCitation(offender, list),
                        "cloud-citation-handoff",
                        GameWorkJobTrigger.CloudCommand,
                        GameWorkPriority.Critical,
                        "cloud-citation-handoff-" + offender);
                    return true;
                }
                if (ModIntegration.StpPluginLoaded) {
                    GameFiberHttpBridge.EnqueueFireAndForget(
                        () => StpCitationHelper.GiveCitation(offender, list),
                        "cloud-citation-handoff",
                        GameWorkJobTrigger.CloudCommand,
                        GameWorkPriority.Critical,
                        "cloud-citation-handoff-" + offender);
                    return true;
                }
                Helper.Log("[MDT Pro] citationHandoff cloud command: neither Policing Redefined nor StopThePed integration is active.", false, Helper.LogSeverity.Info);
                return false;
            }
            if (name.Equals("officerInformationLive", StringComparison.OrdinalIgnoreCase)) {
                bool ran = GameFiberHttpBridge.TryExecuteBlocking(
                    () => DataController.SetOfficerInformation(),
                    4500,
                    out var caught,
                    "cloud-officer-information",
                    GameWorkJobTrigger.CloudCommand,
                    GameWorkPriority.Interactive);
                if (!ran) {
                    result["error"] = "game_busy";
                    return false;
                }
                if (caught != null) {
                    result["error"] = caught.Message;
                    return false;
                }
                OfficerInformationData live = DataController.OfficerInformation;
                OfficerInformationData saved = DataController.OfficerInformationData;
                string Coalesce(string a, string b) {
                    if (!string.IsNullOrWhiteSpace(a)) return a.Trim();
                    return (b ?? "").Trim();
                }
                string badge = live.badgeNumber.HasValue ? live.badgeNumber.Value.ToString(CultureInfo.InvariantCulture)
                    : (saved.badgeNumber.HasValue ? saved.badgeNumber.Value.ToString(CultureInfo.InvariantCulture) : "");
                JObject officer = new JObject {
                    ["firstName"] = Coalesce(live.firstName, saved.firstName),
                    ["lastName"] = Coalesce(live.lastName, saved.lastName),
                    ["rank"] = Coalesce(live.rank, saved.rank),
                    ["callSign"] = Coalesce(live.callSign, saved.callSign),
                    ["agency"] = Coalesce(live.agency, saved.agency),
                    ["badgeNumber"] = string.IsNullOrWhiteSpace(badge) ? "" : badge
                };
                if (!string.IsNullOrWhiteSpace(live.agencyScriptName))
                    officer["agencyScriptName"] = live.agencyScriptName.Trim();
                else if (!string.IsNullOrWhiteSpace(saved.agencyScriptName))
                    officer["agencyScriptName"] = saved.agencyScriptName.Trim();
                result["officer"] = officer;
                return true;
            }
            if (name.Equals("maintenanceBroadcast", StringComparison.OrdinalIgnoreCase)) {
                if (payload == null) return false;
                if (TryMarkMaintenancePayloadSeen(payload))
                    ShowMaintenanceFromPayload(payload);
                return true;
            }
            if (name.Equals("buddyMessage", StringComparison.OrdinalIgnoreCase)) {
                if (payload == null) return false;
                ShowBuddyMessageFromPayload(payload);
                return true;
            }
            if (name.Equals("firearmInspectPrCapabilities", StringComparison.OrdinalIgnoreCase)) {
                result["capabilities"] = PRFirearmSearchItemHelper.InspectCapabilities();
                return true;
            }
            if (name.Equals("firearmApplyToPed", StringComparison.OrdinalIgnoreCase) || name.Equals("firearmApplyToVehicle", StringComparison.OrdinalIgnoreCase)) {
                if (payload == null) {
                    result["error"] = "payload is required";
                    return false;
                }
                bool success = false;
                string message = null;
                bool ran = GameFiberHttpBridge.TryExecuteBlocking(() => {
                    success = name.Equals("firearmApplyToPed", StringComparison.OrdinalIgnoreCase)
                        ? PRFirearmSearchItemHelper.TryApplyToPed(payload, out message)
                        : PRFirearmSearchItemHelper.TryApplyToVehicle(payload, out message);
                }, 4500, out var caught, "cloud-firearm-apply", GameWorkJobTrigger.CloudCommand, GameWorkPriority.Interactive);
                if (!ran) {
                    result["error"] = "game_busy";
                    return false;
                }
                if (caught != null) {
                    result["error"] = caught.Message;
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(message)) result["message"] = message;
                result["applied"] = success;
                return success;
            }
            return false;
        }

        private static bool TryExtractMaintenanceIdentity(JObject payload, out Guid id, out int revision) {
            id = Guid.Empty;
            revision = payload["notifyRevision"]?.Value<int>() ?? 0;
            string rawId = payload.Value<string>("id") ?? payload.Value<string>("maintenanceId");
            return Guid.TryParse(rawId, out id);
        }

        private static bool TryMarkMaintenancePayloadSeen(JObject payload) {
            if (!TryExtractMaintenanceIdentity(payload, out var id, out var rev)) return true;
            lock (MaintenanceEchoLock) {
                bool shouldShow = _lastMaintenanceEchoId != id || _lastMaintenanceEchoRevision != rev;
                if (shouldShow) {
                    _lastMaintenanceEchoId = id;
                    _lastMaintenanceEchoRevision = rev;
                }
                return shouldShow;
            }
        }

        private static void TryProcessMaintenanceSessionEcho(JObject root) {
            try {
                JObject m = root["maintenance"] as JObject;
                if (m == null || m.Type == JTokenType.Null) return;
                if (TryMarkMaintenancePayloadSeen(m))
                    ShowMaintenanceFromPayload(m);
            } catch {
                /* ignore malformed session echo */
            }
        }

        private static void ShowBuddyMessageFromPayload(JObject payload) {
            string rank = payload.Value<string>("senderRank") ?? "";
            string first = payload.Value<string>("senderFirstName") ?? "";
            string last = payload.Value<string>("senderLastName") ?? "";
            string sub = string.IsNullOrWhiteSpace(rank) && string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last)
                ? "Buddy message"
                : ("Buddy: " + string.Join(" ", new[] { rank, first, last }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim());
            string body = payload.Value<string>("bodyPreview");
            if (string.IsNullOrWhiteSpace(body)) body = "New message";
            if (body.Length > 400)
                body = body.Substring(0, 397) + "...";
            RageNotification.Show(body, RageNotification.NotificationType.Info, sub);
        }

        private static void ShowMaintenanceFromPayload(JObject payload) {
            string sub = payload.Value<string>("preformattedSubtitle");
            if (string.IsNullOrWhiteSpace(sub)) sub = "Scheduled maintenance";
            string body = payload.Value<string>("preformattedBody");
            if (string.IsNullOrWhiteSpace(body)) {
                string s = payload.Value<string>("startUtc");
                string e = payload.Value<string>("endUtc");
                string t = payload.Value<string>("title");
                body = string.IsNullOrWhiteSpace(t) ? "MDT Cloud maintenance scheduled." : t.Trim();
                if (!string.IsNullOrWhiteSpace(s) && !string.IsNullOrWhiteSpace(e))
                    body += $"~n~{s} – {e}";
            }
            if (body.Length > 400)
                body = body.Substring(0, 397) + "...";
            RageNotification.Show(body, RageNotification.NotificationType.Info, sub);
        }

        private static void PostSession(Config cfg, ref CloudCredentials credentials, bool retry = true) {
            JArray nearbyVehicles = new JArray();
            try {
                foreach (var vehicle in DataController.CachedNearbyVehicles.ToArray()) {
                    var row = new JObject {
                        ["licensePlate"] = vehicle.LicensePlate,
                        ["vehicleIdentificationNumber"] = vehicle.VehicleIdentificationNumber,
                        ["modelName"] = vehicle.ModelName,
                        ["modelDisplayName"] = vehicle.ModelDisplayName,
                        ["ownerName"] = vehicle.Owner,
                        ["color"] = vehicle.Color,
                        ["vinStatus"] = vehicle.VinStatus,
                        ["make"] = vehicle.Make,
                        ["model"] = vehicle.Model,
                        ["primaryColor"] = vehicle.PrimaryColor,
                        ["secondaryColor"] = vehicle.SecondaryColor,
                        ["primaryColorSpecific"] = vehicle.PrimaryColorSpecific,
                        ["secondaryColorSpecific"] = vehicle.SecondaryColorSpecific,
                        ["registrationStatus"] = vehicle.RegistrationStatus,
                        ["registrationExpiration"] = vehicle.RegistrationExpirationVerifiedFromLiveDocument ? vehicle.RegistrationExpiration : null,
                        ["registrationExpirationVerifiedFromLiveDocument"] = vehicle.RegistrationExpirationVerifiedFromLiveDocument,
                        ["insuranceStatus"] = vehicle.InsuranceStatus,
                        ["insuranceExpiration"] = vehicle.InsuranceExpirationVerifiedFromLiveDocument ? vehicle.InsuranceExpiration : null,
                        ["insuranceExpirationVerifiedFromLiveDocument"] = vehicle.InsuranceExpirationVerifiedFromLiveDocument,
                        ["distance"] = vehicle.Distance,
                        ["isStolen"] = vehicle.IsStolen,
                        ["sourceInstallId"] = cfg.cloudInstallId,
                        ["encounterKey"] = BuildVehicleEncounterKey(cfg.cloudInstallId, vehicle.VehicleIdentificationNumber, vehicle.LicensePlate)
                    };
                    CloudIdentityCache.ApplyVehicleId(row);
                    nearbyVehicles.Add(row);
                }
            } catch { }
            JObject contextPed = BuildContextPed();
            JObject contextVehicle = BuildContextVehicle(cfg);
            string locationJson = null;
            try {
                if (DataController.MdtPreferredLocation != null)
                    locationJson = JsonConvert.SerializeObject(DataController.MdtPreferredLocation);
            } catch { /* leave null */ }
            JObject body = new JObject {
                ["installId"] = cfg.cloudInstallId,
                ["currentLocation"] = locationJson,
                ["contextPed"] = contextPed,
                ["contextVehicle"] = contextVehicle ?? new JObject { ["source"] = "nearby-cache", ["count"] = nearbyVehicles.Count },
                ["nearbyVehicles"] = nearbyVehicles,
                ["recentIds"] = BuildRecentIds(),
                ["observedAtUtc"] = DateTimeOffset.UtcNow.ToString("o")
            };
            if (_roundTripMsToReportOnNextSessionPost.HasValue)
                body["lastSessionRoundTripMs"] = _roundTripMsToReportOnNextSessionPost.Value;
            DateTimeOffset sessionPostStartedUtc = DateTimeOffset.UtcNow;
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, CloudMode.ApiBaseUrl() + "/api/mdt/plugin/session")) {
                AddCloudHeaders(request, credentials);
                request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = Http.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult()) {
                    if (retry && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) &&
                        CloudCredentialStore.TryRefresh(cfg, ref credentials)) {
                        PostSession(cfg, ref credentials, retry: false);
                        return;
                    }
                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        CloudCredentialStore.Clear();
                    response.EnsureSuccessStatusCode();
                    long? responseLength = response.Content.Headers.ContentLength;
                    if (responseLength != null && responseLength.Value > 16384)
                        return;
                    string responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(responseText)) {
                        try {
                            TryProcessMaintenanceSessionEcho(JObject.Parse(responseText));
                        } catch {
                            /* ignore */
                        }
                    }
                    int elapsed = (int)(DateTimeOffset.UtcNow - sessionPostStartedUtc).TotalMilliseconds;
                    if (elapsed < 0) elapsed = 0;
                    if (elapsed > 120000) elapsed = 120000;
                    _roundTripMsToReportOnNextSessionPost = elapsed;
                }
            }
        }

        private static JObject BuildContextPed() {
            try {
                MDTProPedData ped = DataController.GetContextPedIfValid();
                if (ped == null) {
                    CloudEncounterLeaseClient.ObserveContextPerson(null);
                    return new JObject { ["source"] = "plugin-session", ["hydrated"] = false };
                }
                bool authorityDriverLicenseHydrated = DataController.TryHydratePedDocumentsForCloudDisplay(ped, ped.Name);
                JObject payload = JObject.FromObject(new {
                    source = "plugin-session",
                    hydrated = true,
                    name = ped.Name,
                    firstName = ped.FirstName,
                    lastName = ped.LastName,
                    birthday = ped.Birthday,
                    gender = ped.Gender,
                    address = ped.Address,
                    modelHash = ped.ModelHash,
                    modelName = ped.ModelName,
                    portraitVariantDrawable = ped.PortraitVariantDrawable,
                    portraitVariantTexture = ped.PortraitVariantTexture,
                    portraitFaceDrawable = ped.PortraitFaceDrawable,
                    portraitFaceTexture = ped.PortraitFaceTexture,
                    isInGang = ped.IsInGang,
                    advisoryText = ped.AdvisoryText,
                    timesStopped = ped.TimesStopped,
                    isWanted = ped.IsWanted,
                    warrantText = ped.WarrantText,
                    isOnProbation = ped.IsOnProbation,
                    isOnParole = ped.IsOnParole,
                    licenseStatus = ped.LicenseStatus,
                    licenseExpiration = ped.LicenseExpiration,
                    licenseExpirationVerifiedFromLiveDocument = ped.LicenseExpirationVerifiedFromLiveDocument,
                    authorityDriverLicenseHydrated,
                    weaponPermitStatus = ped.WeaponPermitStatus,
                    weaponPermitType = ped.WeaponPermitType,
                    weaponPermitExpiration = ped.WeaponPermitExpiration,
                    fishingPermitStatus = ped.FishingPermitStatus,
                    fishingPermitExpiration = ped.FishingPermitExpiration,
                    huntingPermitStatus = ped.HuntingPermitStatus,
                    huntingPermitExpiration = ped.HuntingPermitExpiration,
                    incarceratedUntil = ped.IncarceratedUntil,
                    isDeceased = ped.IsDeceased,
                    deceasedAt = ped.DeceasedAt,
                    citations = ped.Citations,
                    arrests = ped.Arrests,
                    identificationHistory = ped.IdentificationHistory
                });
                var ctxWarrants = BuildWarrantsJson(ped.Warrants);
                payload["warrants"] = ctxWarrants;
                payload["Warrants"] = ctxWarrants;
                try {
                    var cases = DataController.GetCourtCasesForPedName(ped.Name);
                    payload["CourtCases"] = cases == null || cases.Count == 0 ? new JArray() : JArray.FromObject(cases);
                } catch {
                    payload["CourtCases"] = new JArray();
                }
                CloudIdentityCache.ApplyPersonId(payload);
                CloudEncounterLeaseClient.ObserveContextPerson(ped);
                return payload;
            } catch {
                CloudEncounterLeaseClient.ObserveContextPerson(null);
                return new JObject { ["source"] = "plugin-session", ["hydrated"] = false };
            }
        }

        private static JArray BuildRecentIds() {
            try {
                var items = new List<(MDTProPedData Ped, string HeadType, string HeadTs, DateTimeOffset Sort)>();
                foreach (MDTProPedData p in DataController.GetPedSnapshotForRecentIds()) {
                    if (p?.IdentificationHistory == null || p.IdentificationHistory.Count == 0) continue;
                    if (!MDTProPedData.TryGetRecentIdentificationHeadlineForCloud(p.IdentificationHistory, out var headType, out var headTs, out var sortParsed))
                        continue;
                    items.Add((p, headType, headTs ?? "", sortParsed));
                }
                var rows = items
                    .OrderByDescending(x => x.Sort)
                    .ThenByDescending(x => x.HeadTs, StringComparer.Ordinal)
                    .Take(8)
                    .Select(x => {
                        var row = new JObject {
                            ["Name"] = x.Ped.Name,
                            ["Type"] = MDTProPedData.FormatIdentificationTypeForMdtDisplay(x.HeadType),
                            ["Timestamp"] = x.HeadTs,
                            ["IsDeceased"] = x.Ped.IsDeceased,
                            ["Dob"] = x.Ped.Birthday
                        };
                        string pid = CloudIdentityCache.GetPersonId(x.Ped);
                        if (!string.IsNullOrWhiteSpace(pid)) row["personId"] = pid;
                        return row;
                    });
                return new JArray(rows);
            } catch (Exception ex) {
                Helper.Log($"[MDT Pro] BuildRecentIds: {ex.Message}", false, Helper.LogSeverity.Warning);
                return new JArray();
            }
        }

        private static JObject BuildContextVehicle(Config cfg) {
            try {
                MDTProVehicleData vehicle = DataController.GetContextVehicleIfValid();
                if (vehicle == null) return null;
                JObject payload = JObject.FromObject(new {
                    source = "plugin-session",
                    hydrated = true,
                    licensePlate = vehicle.LicensePlate,
                    vehicleIdentificationNumber = vehicle.VehicleIdentificationNumber,
                    ownerName = vehicle.Owner,
                    modelName = vehicle.ModelName,
                    modelDisplayName = vehicle.ModelDisplayName,
                    isStolen = vehicle.IsStolen,
                    color = vehicle.Color,
                    vinStatus = vehicle.VinStatus,
                    make = vehicle.Make,
                    model = vehicle.Model,
                    primaryColor = vehicle.PrimaryColor,
                    secondaryColor = vehicle.SecondaryColor,
                    primaryColorSpecific = vehicle.PrimaryColorSpecific,
                    secondaryColorSpecific = vehicle.SecondaryColorSpecific,
                    registrationStatus = vehicle.RegistrationStatus,
                    registrationExpiration = vehicle.RegistrationExpirationVerifiedFromLiveDocument ? vehicle.RegistrationExpiration : null,
                    registrationExpirationVerifiedFromLiveDocument = vehicle.RegistrationExpirationVerifiedFromLiveDocument,
                    insuranceStatus = vehicle.InsuranceStatus,
                    insuranceExpiration = vehicle.InsuranceExpirationVerifiedFromLiveDocument ? vehicle.InsuranceExpiration : null,
                    insuranceExpirationVerifiedFromLiveDocument = vehicle.InsuranceExpirationVerifiedFromLiveDocument,
                    bolos = vehicle.BOLOs,
                    canModifyBOLOs = true,
                    sourceInstallId = cfg.cloudInstallId,
                    encounterKey = BuildVehicleEncounterKey(cfg.cloudInstallId, vehicle.VehicleIdentificationNumber, vehicle.LicensePlate)
                });
                CloudIdentityCache.ApplyVehicleId(payload);
                MDTProPedData contextPed = DataController.GetContextPedIfValid();
                if (contextPed != null &&
                    !string.IsNullOrWhiteSpace(vehicle.Owner) &&
                    vehicle.Owner.Equals(contextPed.Name, StringComparison.OrdinalIgnoreCase)) {
                    string ownerPersonId = CloudIdentityCache.GetPersonId(contextPed);
                    if (!string.IsNullOrWhiteSpace(ownerPersonId)) payload["ownerPersonId"] = ownerPersonId;
                }
                return payload;
            } catch {
                return null;
            }
        }

        private static string BuildVehicleEncounterKey(string installId, string vin, string plate) {
            string normalizedVin = NormalizeVehicleVin(vin);
            if (!string.IsNullOrWhiteSpace(normalizedVin)) return "vin|" + normalizedVin;
            string normalizedPlate = NormalizeVehicleIdentity(plate);
            string normalizedInstall = NormalizeVehicleIdentity(installId);
            return string.IsNullOrWhiteSpace(normalizedPlate) || string.IsNullOrWhiteSpace(normalizedInstall)
                ? null
                : "install|" + normalizedInstall + "|plate|" + normalizedPlate;
        }

        private static string NormalizeVehicleIdentity(string value) {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return new string(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static string NormalizeVehicleVin(string value) {
            string normalized = NormalizeVehicleIdentity(value);
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 5) return "";
            if (normalized == "0" || normalized == "UNKNOWN" || normalized == "UNK" || normalized == "NA" || normalized == "NONE" || normalized == "NULL") return "";
            return normalized;
        }

        private static JArray GetArray(string path, Config cfg, ref CloudCredentials credentials, bool retry = true) {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, CloudMode.ApiBaseUrl() + path)) {
                AddCloudHeaders(request, credentials);
                using (HttpResponseMessage response = Http.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult()) {
                    if (retry && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) &&
                        CloudCredentialStore.TryRefresh(cfg, ref credentials))
                        return GetArray(path, cfg, ref credentials, retry: false);
                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        CloudCredentialStore.Clear();
                    response.EnsureSuccessStatusCode();
                    string json = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    return JArray.Parse(json);
                }
            }
        }

        private static void Ack(Guid id, string status, JObject result, Config cfg, ref CloudCredentials credentials, bool retry = true) {
            JObject body = new JObject {
                ["status"] = status,
                ["result"] = result ?? new JObject { ["source"] = "plugin" }
            };
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, CloudMode.ApiBaseUrl() + $"/api/mdt/plugin/commands/{id}/ack?installId={Uri.EscapeDataString(cfg.cloudInstallId)}")) {
                AddCloudHeaders(request, credentials);
                request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = Http.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult()) {
                    if (retry && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) &&
                        CloudCredentialStore.TryRefresh(cfg, ref credentials)) {
                        Ack(id, status, result, cfg, ref credentials, retry: false);
                        return;
                    }
                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        CloudCredentialStore.Clear();
                    response.EnsureSuccessStatusCode();
                }
            }
        }

        private static void AddCloudHeaders(HttpRequestMessage request, CloudCredentials credentials) {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.AccessToken);
            request.Headers.TryAddWithoutValidation("X-MDT-Device-Token", credentials.DeviceToken);
        }

        static void TryLspdfrDutyShiftStartAfterSession(Config cfg, ref CloudCredentials credentials) {
            if (!CloudMode.IsEnabled()) return;
            try {
                if (LspdfrTryGetOpenShiftExists(cfg, ref credentials)) {
                    Helper.Log("LSPDFR auto shift: an open shift already exists; not starting another.", true, Helper.LogSeverity.Info);
                    return;
                }
                string shiftId = "cloud-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                DateTimeOffset started = DateTimeOffset.UtcNow;
                var payload = new JObject { ["source"] = "lspdfrDuty" };
                if (!LspdfrPostShiftUpsert(cfg, ref credentials, shiftId, "active", started, null, payload)) return;
                lock (LspdfrShiftStateLock) {
                    _trackedLspdfrShiftId = shiftId;
                    _trackedLspdfrShiftStartedUtc = started;
                }
                GameFiberHttpBridge.EnqueueFireAndForget(() => {
                    try {
                        DataController.StartCurrentShift();
                    } catch (Exception ex) {
                        Helper.Log($"LSPDFR auto shift: local StartCurrentShift failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                    }
                }, "cloud-shift-start-local", GameWorkJobTrigger.CloudCommand, GameWorkPriority.Interactive, "cloud-shift-start-local");
            } catch (Exception ex) {
                Helper.Log($"LSPDFR auto shift: start failed: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        static bool LspdfrTryGetOpenShiftExists(Config cfg, ref CloudCredentials credentials) {
            try {
                using (HttpResponseMessage res = LspdfrSendGetOpenShift(cfg, ref credentials, retryAuth: true)) {
                    if (res.StatusCode == HttpStatusCode.NotFound) return false;
                    if (res.IsSuccessStatusCode) return true;
                    Helper.Log($"LSPDFR auto shift: open-shift check returned HTTP {(int)res.StatusCode}; not starting a new shift.", false, Helper.LogSeverity.Warning);
                    return true;
                }
            } catch (Exception ex) {
                Helper.Log($"LSPDFR auto shift: open-shift check failed: {ex.Message}; not starting a new shift.", false, Helper.LogSeverity.Warning);
                return true;
            }
        }

        static HttpResponseMessage LspdfrSendGetOpenShift(Config cfg, ref CloudCredentials credentials, bool retryAuth) {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, CloudMode.ApiBaseUrl() + "/api/mdt/shifts/open")) {
                AddCloudHeaders(request, credentials);
                HttpResponseMessage response = Http.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
                if (retryAuth && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) &&
                    CloudCredentialStore.TryRefresh(cfg, ref credentials)) {
                    response.Dispose();
                    return LspdfrSendGetOpenShift(cfg, ref credentials, retryAuth: false);
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    CloudCredentialStore.Clear();
                return response;
            }
        }

        static bool LspdfrPostShiftUpsert(Config cfg, ref CloudCredentials credentials, string shiftId, string status, DateTimeOffset? startedUtc, DateTimeOffset? endedUtc, JObject payload, bool retryAuth = true) {
            try {
                JObject body = new JObject {
                    ["shiftId"] = shiftId,
                    ["status"] = status,
                    ["payload"] = payload ?? new JObject()
                };
                if (startedUtc.HasValue) body["startedAtUtc"] = startedUtc.Value.ToString("o");
                if (endedUtc.HasValue) body["endedAtUtc"] = endedUtc.Value.ToString("o");

                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, CloudMode.ApiBaseUrl() + "/api/mdt/shifts")) {
                    AddCloudHeaders(request, credentials);
                    request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = Http.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult()) {
                        if (retryAuth && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) &&
                            CloudCredentialStore.TryRefresh(cfg, ref credentials))
                            return LspdfrPostShiftUpsert(cfg, ref credentials, shiftId, status, startedUtc, endedUtc, payload, retryAuth: false);
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                            CloudCredentialStore.Clear();
                        if (!response.IsSuccessStatusCode) {
                            string err = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                            Helper.Log($"LSPDFR auto shift: HTTP {(int)response.StatusCode} {err}", false, Helper.LogSeverity.Warning);
                            return false;
                        }
                        return true;
                    }
                }
            } catch (Exception ex) {
                Helper.Log($"LSPDFR auto shift: request failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                return false;
            }
        }
    }
}
