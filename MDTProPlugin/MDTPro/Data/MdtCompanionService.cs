using MDTPro.Cloud;
using MDTPro.Data.Reports;
using MDTPro.ServerAPI;
using MDTPro.Setup;
using MDTPro.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace MDTPro.Data
{
    /// <summary>Shared quick-MDT workflows used by the web endpoints and the LemonUI in-game computer.</summary>
    internal static class MdtCompanionService
    {
        private static readonly HttpClient CloudHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

        internal enum CompanionDataSource
        {
            Local = 0,
            Cloud = 1
        }

        internal sealed class CompanionModeStatus
        {
            internal CompanionDataSource Source;
            internal bool CloudConfigured;
            internal bool HasSavedLogin;
            internal CloudConnectionState ConnectionState;
            internal string Detail;

            internal bool UsesCloud => Source == CompanionDataSource.Cloud;
            internal string DisplayMode => UsesCloud ? "Cloud" : "Local";
        }

        internal sealed class PedLookupResult
        {
            internal MDTProPedData PedData;
            internal List<CourtData> CourtCases = new List<CourtData>();
            internal string Json = "null";
            internal string Query = "";
        }

        internal sealed class VehicleLookupResult
        {
            internal MDTProVehicleData VehicleData;
            internal string Query = "";
        }

        internal sealed class NearbyVehicleRow
        {
            public string LicensePlate;
            public string VehicleIdentificationNumber;
            public string ModelDisplayName;
            public float? Distance;
            public bool IsStolen;
            public string SourceInstallId;
        }

        internal sealed class RecentIdRow
        {
            public string Name;
            public string Type;
            public string Timestamp;
            public bool IsDeceased;
        }

        internal sealed class CitationSaveResult
        {
            internal bool Success;
            internal string Error;
            internal CitationReport Report;
        }

        internal static CompanionModeStatus GetModeStatus()
        {
            bool configured = CloudMode.IsEnabled();
            bool hasLogin = false;
            try { hasLogin = CloudPluginBridge.HasSavedLogin(); } catch { }
            CloudConnectionState state = CloudPluginBridge.ConnectionState;
            string detail = CloudPluginBridge.ConnectionDetail;
            bool cloudActive = configured && hasLogin && state == CloudConnectionState.Connected;
            return new CompanionModeStatus
            {
                Source = cloudActive ? CompanionDataSource.Cloud : CompanionDataSource.Local,
                CloudConfigured = configured,
                HasSavedLogin = hasLogin,
                ConnectionState = state,
                Detail = string.IsNullOrWhiteSpace(detail) ? (cloudActive ? "Connected" : "Disconnected") : detail
            };
        }

        internal static PedLookupResult LookupPed(string query, bool saveSearchHistory)
        {
            if (ShouldUseCloud())
                return TryCloudOrLocal(
                    () => LookupPedCloud(query),
                    () => LookupPedLocal(query, saveSearchHistory),
                    "cloud ped lookup");
            return LookupPedLocal(query, saveSearchHistory);
        }

        internal static PedLookupResult LookupVehicleOwner(MDTProVehicleData vehicle, bool saveSearchHistory)
        {
            if (vehicle == null || string.IsNullOrWhiteSpace(vehicle.Owner))
                return new PedLookupResult { Query = "" };
            if (ShouldUseCloud())
                return TryCloudOrLocal(
                    () => LookupVehicleOwnerCloud(vehicle),
                    () => LookupPedLocal(vehicle.Owner, saveSearchHistory),
                    "cloud vehicle owner lookup");
            return LookupPedLocal(vehicle.Owner, saveSearchHistory);
        }

        private static PedLookupResult LookupPedLocal(string query, bool saveSearchHistory)
        {
            string name = (query ?? "").Trim();
            string reversedName = ReverseName(name);
            bool wantsContext = IsContextQuery(name);

            MDTProPedData pedData = null;
            if (!string.IsNullOrEmpty(name) && !wantsContext)
            {
                MDTProPedData contextPed = DataController.GetContextPedIfValid();
                if (contextPed != null && (NameEquals(contextPed.Name, name) || NameEquals(contextPed.Name, reversedName)))
                    pedData = contextPed;
            }
            if (pedData == null && !string.IsNullOrEmpty(name))
                pedData = DataController.GetPedDataByName(name) ?? DataController.GetPedDataByName(reversedName);
            if (pedData == null && !string.IsNullOrEmpty(name) && !wantsContext)
            {
                pedData = DataController.PedDatabase.FirstOrDefault(o =>
                    NameEquals(o?.Name, name) || NameEquals(o?.Name, reversedName));
            }
            if (pedData == null && wantsContext)
                pedData = DataController.GetContextPedIfValid();

            if (saveSearchHistory)
            {
                Database.SaveSearchHistoryEntry("ped", name, pedData?.Name, pedData?.Birthday);
                WebSocketHandler.BroadcastDataInvalidation("pedSearch");
            }

            return BuildPedResult(pedData, name, reversedName);
        }

        internal static PedLookupResult GetContextPed()
        {
            if (ShouldUseCloud())
                return TryCloudOrLocal(
                    GetContextPedCloud,
                    GetContextPedLocal,
                    "cloud context ped lookup");
            return GetContextPedLocal();
        }

        private static PedLookupResult GetContextPedLocal()
        {
            MDTProPedData pedData = DataController.GetContextPedIfValid();
            string name = pedData?.Name?.Trim() ?? "";
            return BuildPedResult(pedData, name, ReverseName(name));
        }

        internal static VehicleLookupResult LookupVehicle(string plateOrVin, bool saveSearchHistory)
        {
            if (ShouldUseCloud())
                return TryCloudOrLocal(
                    () => LookupVehicleCloud(plateOrVin),
                    () => LookupVehicleLocal(plateOrVin, saveSearchHistory),
                    "cloud vehicle lookup");
            return LookupVehicleLocal(plateOrVin, saveSearchHistory);
        }

        private static VehicleLookupResult LookupVehicleLocal(string plateOrVin, bool saveSearchHistory)
        {
            string query = (plateOrVin ?? "").Trim();
            bool wantsContext = IsContextQuery(query);
            MDTProVehicleData vehicleData = null;
            string selectedSource = "none";

            if (wantsContext)
            {
                vehicleData = DataController.GetContextVehicleIfValid();
                if (vehicleData != null) selectedSource = "context";
            }
            else if (!string.IsNullOrEmpty(query))
            {
                MDTProVehicleData contextVeh = DataController.GetContextVehicleIfValid();
                if (contextVeh != null)
                {
                    string key = DataController.NormalizeVehiclePlateKey(query);
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(contextVeh.LicensePlate)
                        && DataController.NormalizeVehiclePlateKey(contextVeh.LicensePlate) == key)
                    {
                        vehicleData = contextVeh;
                        selectedSource = "context";
                    }
                    else if (!string.IsNullOrEmpty(contextVeh.VehicleIdentificationNumber)
                        && string.Equals(contextVeh.VehicleIdentificationNumber.Trim(), query, StringComparison.OrdinalIgnoreCase))
                    {
                        vehicleData = contextVeh;
                        selectedSource = "context";
                    }
                }
            }
            if (vehicleData == null && !string.IsNullOrEmpty(query) && !wantsContext)
            {
                vehicleData = DataController.GetCachedNearbyVehicleByPlateOrVin(query);
                if (vehicleData != null) selectedSource = "nearby-cache";
            }
            if (!string.IsNullOrEmpty(query) && !wantsContext)
            {
                MDTProVehicleData storedVehicleData = DataController.GetVehicleByPlateOrVin(query);
                if (storedVehicleData != null)
                {
                    MDTProVehicleData selectedBeforeMerge = vehicleData;
                    vehicleData = DataController.PreferRicherVehicleForDisplay(vehicleData, storedVehicleData);
                    if (selectedBeforeMerge == null || object.ReferenceEquals(vehicleData, storedVehicleData))
                    {
                        selectedSource = SetupController.GetConfig()?.localVehicleLookupDebugLogging == true
                            ? DataController.GetVehicleStorageSource(vehicleData)
                            : "cache-or-database";
                    }
                }
            }
            if (vehicleData == null && !string.IsNullOrEmpty(query) && !wantsContext)
            {
                vehicleData = DataController.TryResolveVehicleFromLiveWorldByPlateOrVinBlocking(query, 500);
                if (vehicleData != null) selectedSource = "live-world";
            }
            if (vehicleData == null && !string.IsNullOrEmpty(query) && !wantsContext)
            {
                vehicleData = DataController.GetCachedNearbyVehicleByPlateOrVin(query);
                if (vehicleData != null) selectedSource = "nearby-cache";
            }

            vehicleData = HydrateVehicleForDisplay(vehicleData, query, selectedSource);

            if (saveSearchHistory)
            {
                string historyQuery = wantsContext ? (vehicleData?.LicensePlate ?? "context") : query;
                Database.SaveSearchHistoryEntry("vehicle", historyQuery, vehicleData?.LicensePlate);
                WebSocketHandler.BroadcastDataInvalidation("vehicleSearch");
            }

            return new VehicleLookupResult { VehicleData = vehicleData, Query = query };
        }

        internal static VehicleLookupResult GetContextVehicle()
        {
            if (ShouldUseCloud())
                return TryCloudOrLocal(
                    GetContextVehicleCloud,
                    GetContextVehicleLocal,
                    "cloud context vehicle lookup");
            return GetContextVehicleLocal();
        }

        private static VehicleLookupResult GetContextVehicleLocal()
        {
            MDTProVehicleData vehicleData = DataController.GetContextVehicleIfValid();
            vehicleData = HydrateVehicleForDisplay(vehicleData, vehicleData?.LicensePlate ?? "context", "context");
            return new VehicleLookupResult { VehicleData = vehicleData, Query = vehicleData?.LicensePlate ?? "context" };
        }

        private static bool LookupResultHasData<T>(T result)
        {
            if (result is PedLookupResult ped)
                return ped.PedData != null;
            if (result is VehicleLookupResult vehicle)
                return vehicle.VehicleData != null;
            return result != null;
        }

        internal static List<NearbyVehicleRow> GetNearbyVehicles(int limit, bool explicitScan, out bool scanCompleted)
        {
            if (ShouldUseCloud())
            {
                try
                {
                    return GetNearbyVehiclesCloud(limit, explicitScan, out scanCompleted);
                }
                catch (Exception ex)
                {
                    if (!CloudFallbackEnabled()) throw;
                    Helper.Log($"In-game MDT cloud nearby vehicles failed; using local fallback: {ex.Message}", false, Helper.LogSeverity.Warning);
                }
            }
            if (limit < 1) limit = 1;
            if (limit > 20) limit = 20;
            scanCompleted = DataController.RefreshCachedNearbyVehiclesOnGameFiberBlocking(explicitScan ? 1500 : 450, explicitScan);
            string installId = SetupController.GetConfig()?.cloudInstallId;
            return DataController.GetCachedNearbyVehicles(limit)
                .Select(x => new NearbyVehicleRow
                {
                    LicensePlate = x.LicensePlate,
                    VehicleIdentificationNumber = x.VehicleIdentificationNumber,
                    ModelDisplayName = x.ModelDisplayName,
                    Distance = x.Distance,
                    IsStolen = x.IsStolen,
                    SourceInstallId = installId
                })
                .ToList();
        }

        internal static List<RecentIdRow> GetRecentIds(int limit = 8)
        {
            if (ShouldUseCloud())
                return TryCloudOrLocal(
                    () => GetRecentIdsCloud(limit),
                    () => GetRecentIdsLocal(limit),
                    "cloud recent IDs");
            return GetRecentIdsLocal(limit);
        }

        private static List<RecentIdRow> GetRecentIdsLocal(int limit = 8)
        {
            if (limit < 1) limit = 1;
            if (limit > 20) limit = 20;
            return DataController.GetPedSnapshotForRecentIds()
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name) && p.IdentificationHistory != null && p.IdentificationHistory.Count > 0)
                .Select(p => new { p.Name, Latest = p.IdentificationHistory[0], p.IsDeceased })
                .OrderByDescending(x => x.Latest.Timestamp)
                .Take(limit)
                .Select(x => new RecentIdRow
                {
                    Name = x.Name,
                    Type = MDTProPedData.FormatIdentificationTypeForMdtDisplay(x.Latest.Type),
                    Timestamp = x.Latest.Timestamp,
                    IsDeceased = x.IsDeceased
                })
                .ToList();
        }

        internal static CitationSaveResult SaveCitationReport(CitationReport report)
        {
            if (ShouldUseCloud())
                return TryCloudOrLocal(
                    () => SaveCitationReportCloud(report),
                    () => SaveCitationReportLocal(report),
                    "cloud citation save");
            return SaveCitationReportLocal(report);
        }

        private static CitationSaveResult SaveCitationReportLocal(CitationReport report)
        {
            try
            {
                if (report == null)
                    return new CitationSaveResult { Success = false, Error = "Invalid report data." };
                RefreshReportLocationIfNeeded(report);
                if (report.Charges == null) report.Charges = new List<CitationReport.Charge>();
                DataController.AddReport(report);
                Database.SaveCitationReport(report);
                return new CitationSaveResult { Success = true, Report = report };
            }
            catch (Exception ex)
            {
                Helper.Log($"[createCitationReport] {ex.Message}", true, Helper.LogSeverity.Error);
                try { System.IO.File.AppendAllText(SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createCitationReport:\n{Helper.SanitizeExceptionForLog(ex)}"); } catch { }
                return new CitationSaveResult { Success = false, Error = ex.Message, Report = report };
            }
        }

        internal static CitationReport BuildQuickCitationReport(MDTProPedData ped, MDTProVehicleData vehicle, List<CitationReport.Charge> charges, bool closeNow)
        {
            DateTime now = DateTime.Now;
            var report = new CitationReport
            {
                Id = GenerateReportId("citation", "C"),
                ShortYear = now.Year % 100,
                OfficerInformation = DataController.OfficerInformation,
                Location = DataController.MdtPreferredLocation,
                TimeStamp = now,
                Status = closeNow ? ReportStatus.Closed : ReportStatus.Open,
                Notes = "",
                OffenderPedName = ped?.Name ?? "",
                OffenderVehicleLicensePlate = vehicle?.LicensePlate ?? "",
                CourtCaseNumber = null,
                Charges = charges ?? new List<CitationReport.Charge>()
            };
            RefreshReportLocationIfNeeded(report);
            return report;
        }

        internal static string GenerateReportId(string type, string fallbackPrefix)
        {
            Config cfg = SetupController.GetConfig();
            DateTime now = DateTime.Now;
            int shortYear = now.Year % 100;
            int index = 1;
            if (string.Equals(type, "citation", StringComparison.OrdinalIgnoreCase))
            {
                index += DataController.CitationReports?.Count(r => r != null && r.ShortYear == shortYear) ?? 0;
            }
            string prefix = fallbackPrefix ?? "RPT";
            try
            {
                var map = SetupController.GetLanguage()?.reports?.idTypeMap;
                string mapped = type == "citation" ? map?.citation : null;
                if (!string.IsNullOrWhiteSpace(mapped)) prefix = mapped;
            }
            catch { /* default */ }
            string id = string.IsNullOrWhiteSpace(cfg?.reportIdFormat) ? "{type}-{shortYear}-{index}" : cfg.reportIdFormat;
            int pad = cfg?.reportIdIndexPad > 0 ? cfg.reportIdIndexPad : 1;
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                string candidate = id;
                candidate = candidate.Replace("{type}", prefix);
                candidate = candidate.Replace("{shortYear}", shortYear.ToString("00"));
                candidate = candidate.Replace("{year}", now.Year.ToString());
                candidate = candidate.Replace("{month}", now.Month.ToString());
                candidate = candidate.Replace("{day}", now.Day.ToString());
                candidate = candidate.Replace("{index}", (index + attempt).ToString().PadLeft(pad, '0'));
                if (DataController.CitationReports?.Any(r => string.Equals(r?.Id, candidate, StringComparison.OrdinalIgnoreCase)) != true)
                    return candidate;
            }
            return $"{prefix}-{shortYear:00}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        internal static void FormatIdentificationHistoryTypesForMdt(JObject jo)
        {
            if (jo == null) return;
            if (!(jo["IdentificationHistory"] is JArray arr)) return;
            foreach (JToken token in arr)
            {
                if (token is JObject entry && entry["Type"]?.Type == JTokenType.String)
                {
                    string raw = entry.Value<string>("Type");
                    entry["Type"] = MDTProPedData.FormatIdentificationTypeForMdtDisplay(raw);
                }
            }
        }

        private static PedLookupResult BuildPedResult(MDTProPedData pedData, string query, string reversedName)
        {
            if (pedData != null)
            {
                pedData.Birthday = MDTProPedData.FormatBirthday(pedData.Birthday);
                DataController.RefreshPedPortraitForPersonSearchBlocking(pedData, query, reversedName);
                DataController.TryRefreshSupervisionFromLiveWorld(pedData, query, reversedName);
                DataController.TryHydratePedDocumentsForCloudDisplay(pedData, query);
                pedData.Birthday = MDTProPedData.FormatBirthday(pedData.Birthday);
                DataController.KeepPedInDatabase(pedData);
                if (MDTProPedData.IsMinimalIdentity(pedData))
                {
                    Helper.Log($"[MDTPro] Person Search returning minimal-identity ped (will show N/A): {pedData.Name}", false, Helper.LogSeverity.Info);
                }
            }

            var result = new PedLookupResult { PedData = pedData, Query = query ?? "" };
            if (pedData == null)
            {
                result.Json = JsonConvert.SerializeObject(pedData);
                return result;
            }

            result.CourtCases = DataController.GetCourtCasesForPedName(pedData.Name) ?? new List<CourtData>();
            JObject jo = JObject.Parse(JsonConvert.SerializeObject(pedData));
            FormatIdentificationHistoryTypesForMdt(jo);
            jo["CourtCases"] = JArray.FromObject(result.CourtCases);
            jo["SupervisionTerms"] = BuildSupervisionTermsFromCourtCases(result.CourtCases);
            result.Json = jo.ToString(Formatting.None);
            return result;
        }

        private static JArray BuildSupervisionTermsFromCourtCases(List<CourtData> cases)
        {
            var arr = new JArray();
            if (cases == null) return arr;
            foreach (var courtCase in cases)
            {
                var order = courtCase?.SupervisionOrder;
                if (order == null) continue;
                if (!string.Equals(order.Status, "Active", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(order.Status, "PendingCustodyRelease", StringComparison.OrdinalIgnoreCase)) continue;
                arr.Add(JObject.FromObject(new
                {
                    Id = courtCase.Number,
                    PersonId = (string)null,
                    SourceCaseNumber = courtCase.Number,
                    order.Type,
                    order.Status,
                    order.RiskLevel,
                    order.StartUtc,
                    order.EndUtc,
                    order.CustodyReleaseUtc,
                    order.ParoleEligibleUtc,
                    order.ViolationCount,
                    order.Notes,
                    order.Conditions
                }));
            }
            return arr;
        }

        private static bool ShouldUseCloud()
        {
            var status = GetModeStatus();
            return status.CloudConfigured && status.HasSavedLogin;
        }

        private static bool CloudFallbackEnabled()
        {
            try { return SetupController.GetConfig()?.cloudOfflineFallbackEnabled != false; } catch { return true; }
        }

        private static T TryCloudOrLocal<T>(Func<T> cloudAction, Func<T> localAction, string operation)
        {
            try
            {
                T cloudResult = cloudAction();
                if (LookupResultHasData(cloudResult))
                    return cloudResult;
                if (CloudFallbackEnabled())
                {
                    Helper.Log($"In-game MDT {operation} returned no cloud match; using local fallback.", false, Helper.LogSeverity.Info);
                    return localAction();
                }
                return cloudResult;
            }
            catch (Exception ex)
            {
                if (TryRecoverCloudConnectionBlocking(operation, ex))
                {
                    try
                    {
                        T retryResult = cloudAction();
                        if (LookupResultHasData(retryResult))
                            return retryResult;
                        if (CloudFallbackEnabled())
                        {
                            Helper.Log($"In-game MDT {operation} returned no cloud match after reconnect; using local fallback.", false, Helper.LogSeverity.Info);
                            return localAction();
                        }
                        return retryResult;
                    }
                    catch (Exception retryEx)
                    {
                        ex = retryEx;
                        Helper.Log($"In-game MDT {operation} failed after cloud reconnect: {retryEx.Message}", false, Helper.LogSeverity.Warning);
                    }
                }
                if (!CloudFallbackEnabled())
                    throw;
                Helper.Log($"In-game MDT {operation} failed after cloud recovery; using local fallback: {ex.Message}", false, Helper.LogSeverity.Warning);
                return localAction();
            }
        }

        private static bool TryRecoverCloudConnectionBlocking(string operation, Exception failure)
        {
            if (!GameFiber.CanSleepNow)
                return false;
            if (!CloudMode.IsEnabled() || !CloudPluginBridge.HasSavedLogin())
                return false;

            bool capturedPause = false;
            bool savedPause = false;
            try
            {
                try
                {
                    savedPause = Game.IsPaused;
                    capturedPause = true;
                    Game.IsPaused = true;
                }
                catch { /* best effort */ }

                string reason = failure?.Message;
                if (string.IsNullOrWhiteSpace(reason)) reason = CloudPluginBridge.ConnectionDetail;
                Helper.Log($"In-game MDT cloud connection lost during {operation}: {reason}", false, Helper.LogSeverity.Warning);

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    ShowCloudRecoveryPopup(attempt, reason);
                    string detail;
                    bool connected = CloudPluginBridge.TryPollOnceForRecovery(out detail);
                    if (connected)
                    {
                        RageNotification.Show("MDT Cloud reconnected. Continuing with cloud records.", RageNotification.NotificationType.Success, "MDT Cloud");
                        return true;
                    }
                    reason = string.IsNullOrWhiteSpace(detail) ? reason : detail;
                    if (attempt < 3)
                        SleepWithRecoveryPopup(attempt, reason, 5000);
                }

                RageNotification.Show("Falling back to local MDT. Cloud records will not be used until the connection returns.", RageNotification.NotificationType.Info, "MDT Cloud disconnected");
                return false;
            }
            finally
            {
                if (capturedPause)
                {
                    try { Game.IsPaused = savedPause; } catch { /* ignore */ }
                }
            }
        }

        private static void ShowCloudRecoveryPopup(int attempt, string reason)
        {
            string detail = string.IsNullOrWhiteSpace(reason) ? "Connection lost." : reason;
            RageNotification.Show(
                $"Cloud connection lost.~n~Retrying MDT Cloud ({attempt}/3).~n~{TruncateForNotification(detail, 120)}",
                RageNotification.NotificationType.Error,
                "MDT Cloud");
            try
            {
                Game.DisplaySubtitle($"~r~MDT Cloud disconnected~s~ - retry {attempt}/3. Game paused.", 1200);
            }
            catch { /* ignore */ }
        }

        private static void SleepWithRecoveryPopup(int attempt, string reason, int milliseconds)
        {
            int elapsed = 0;
            while (elapsed < milliseconds)
            {
                int remaining = Math.Max(1, (milliseconds - elapsed + 999) / 1000);
                try
                {
                    Game.DisplaySubtitle($"~r~MDT Cloud disconnected~s~ - retry {attempt + 1}/3 in {remaining}s. Game paused.", 1100);
                }
                catch { /* ignore */ }
                GameFiber.Sleep(Math.Min(1000, milliseconds - elapsed));
                elapsed += 1000;
            }
        }

        private static string TruncateForNotification(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= max ? value : value.Substring(0, Math.Max(0, max - 3)) + "...";
        }

        private static PedLookupResult LookupPedCloud(string query)
        {
            string body = (query ?? "").Trim();
            string json = CloudSend("/data/specificPed", body, "text/plain");
            return BuildCloudPedResult(json, body);
        }

        private static PedLookupResult LookupVehicleOwnerCloud(MDTProVehicleData vehicle)
        {
            JObject body = new JObject
            {
                ["name"] = vehicle.Owner ?? ""
            };
            if (!string.IsNullOrWhiteSpace(vehicle.OwnerPersonId))
                body["personId"] = vehicle.OwnerPersonId.Trim();
            string text = body.ToString(Formatting.None);
            string json = CloudSend("/data/specificPed", text, "application/json");
            return BuildCloudPedResult(json, vehicle.Owner);
        }

        private static PedLookupResult GetContextPedCloud()
        {
            string json = CloudSend("/data/contextPed", "context", "text/plain");
            return BuildCloudPedResult(json, "context");
        }

        private static VehicleLookupResult LookupVehicleCloud(string query)
        {
            string body = (query ?? "").Trim();
            string json = CloudSend("/data/specificVehicle", body, "text/plain");
            return BuildCloudVehicleResult(json, body);
        }

        private static VehicleLookupResult GetContextVehicleCloud()
        {
            string json = CloudSend("/data/contextVehicle", "context", "text/plain");
            return BuildCloudVehicleResult(json, "context");
        }

        private static List<NearbyVehicleRow> GetNearbyVehiclesCloud(int limit, bool explicitScan, out bool scanCompleted)
        {
            if (limit < 1) limit = 1;
            if (limit > 20) limit = 20;
            string path = explicitScan ? "/data/nearbyVehicles?scan=explicit" : "/data/nearbyVehicles";
            using (HttpResponseMessage response = CloudSendRaw(path, limit.ToString(), "text/plain", retry: true))
            {
                scanCompleted = !response.Headers.TryGetValues("X-MdtPro-Nearby-Scan", out IEnumerable<string> values)
                    || !values.Any(v => string.Equals(v, "deferred", StringComparison.OrdinalIgnoreCase));
                string json = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(ExtractCloudError(json, response.StatusCode));
                return JsonConvert.DeserializeObject<List<NearbyVehicleRow>>(json) ?? new List<NearbyVehicleRow>();
            }
        }

        private static List<RecentIdRow> GetRecentIdsCloud(int limit)
        {
            string json = CloudSend("/data/recentIds", "", "text/plain", retry: true);
            var rows = JsonConvert.DeserializeObject<List<RecentIdRow>>(json) ?? new List<RecentIdRow>();
            if (limit < 1) limit = 1;
            if (limit > 20) limit = 20;
            return rows.Take(limit).ToList();
        }

        private static CitationSaveResult SaveCitationReportCloud(CitationReport report)
        {
            if (report == null)
                return new CitationSaveResult { Success = false, Error = "Invalid report data." };
            RefreshReportLocationIfNeeded(report);
            if (report.Charges == null) report.Charges = new List<CitationReport.Charge>();

            string body = JsonConvert.SerializeObject(report);
            using (HttpResponseMessage response = CloudSendRaw("/post/createCitationReport", body, "application/json", retry: true))
            {
                string text = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    return new CitationSaveResult { Success = false, Error = ExtractCloudError(text, response.StatusCode), Report = report };
                if (response.Headers.TryGetValues("X-Mdt-Report-Id", out IEnumerable<string> ids))
                {
                    string id = ids.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(id)) report.Id = id.Trim();
                }
                StoreCloudCitationInMemory(report);
                return new CitationSaveResult { Success = true, Report = report };
            }
        }

        private static void StoreCloudCitationInMemory(CitationReport report)
        {
            if (report == null || string.IsNullOrWhiteSpace(report.Id)) return;
            int index = DataController.citationReports.FindIndex(x => string.Equals(x?.Id, report.Id, StringComparison.Ordinal));
            if (index >= 0) DataController.citationReports[index] = report;
            else DataController.citationReports.Add(report);
        }

        private static PedLookupResult BuildCloudPedResult(string json, string query)
        {
            if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                return new PedLookupResult { Query = query ?? "", Json = "null" };

            JObject jo = JObject.Parse(json);
            NormalizeCloudPascalAliases(jo);
            NormalizeBirthdayAliases(jo);
            FormatIdentificationHistoryTypesForMdt(jo);
            var ped = jo.ToObject<MDTProPedData>();
            if (ped != null) ped.Birthday = MDTProPedData.FormatBirthday(ped.Birthday);
            var cases = jo["CourtCases"] == null || jo["CourtCases"].Type == JTokenType.Null
                ? new List<CourtData>()
                : jo["CourtCases"].ToObject<List<CourtData>>() ?? new List<CourtData>();
            foreach (CourtData courtCase in cases) DataController.NormalizeCourtCaseOutcome(courtCase);
            jo["CourtCases"] = JArray.FromObject(cases);
            return new PedLookupResult
            {
                PedData = ped,
                CourtCases = cases,
                Json = jo.ToString(Formatting.None),
                Query = query ?? ""
            };
        }

        private static VehicleLookupResult BuildCloudVehicleResult(string json, string query)
        {
            if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                return new VehicleLookupResult { Query = query ?? "" };

            JObject jo = JObject.Parse(json);
            NormalizeCloudPascalAliases(jo);
            var vehicle = jo.ToObject<MDTProVehicleData>();
            vehicle = HydrateVehicleForDisplay(vehicle, query, "cloud-lookup") ?? vehicle;
            return new VehicleLookupResult
            {
                VehicleData = vehicle,
                Query = query ?? ""
            };
        }

        private static void NormalizeCloudPascalAliases(JObject jo)
        {
            if (jo == null) return;
            foreach (JProperty prop in jo.Properties().ToList())
            {
                if (string.IsNullOrEmpty(prop.Name)) continue;
                string pascal = char.ToUpperInvariant(prop.Name[0]) + prop.Name.Substring(1);
                if (!jo.ContainsKey(pascal))
                    jo[pascal] = prop.Value.DeepClone();
            }
            CopyAlias(jo, "id", "PersonId");
            CopyAlias(jo, "personId", "PersonId");
            CopyAlias(jo, "vehicleId", "VehicleId");
            CopyAlias(jo, "id", "VehicleId");
            CopyAlias(jo, "ownerPersonId", "OwnerPersonId");
            CopyAlias(jo, "ownerName", "Owner");
            CopyAlias(jo, "licensePlate", "LicensePlate");
        }

        private static void NormalizeBirthdayAliases(JObject jo)
        {
            if (jo == null) return;
            string formatted = MDTProPedData.FormatBirthday(
                jo.Value<string>("Birthday")
                ?? jo.Value<string>("birthday")
                ?? jo.Value<string>("DateOfBirth")
                ?? jo.Value<string>("dateOfBirth")
                ?? jo.Value<string>("Dob")
                ?? jo.Value<string>("dob"));
            if (string.IsNullOrWhiteSpace(formatted)) return;
            jo["Birthday"] = formatted;
            jo["birthday"] = formatted;
        }

        private static void CopyAlias(JObject jo, string from, string to)
        {
            if (jo[from] != null && jo[to] == null)
                jo[to] = jo[from].DeepClone();
        }

        private static string CloudSend(string path, string body, string contentType)
        {
            return CloudSend(path, body ?? "", contentType, retry: true);
        }

        private static string CloudSend(string path, string body, string contentType, bool retry)
        {
            using (HttpResponseMessage response = CloudSendRaw(path, body, contentType, retry))
            {
                string text = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(ExtractCloudError(text, response.StatusCode));
                return text;
            }
        }

        private static HttpResponseMessage CloudSendRaw(string path, string body, string contentType, bool retry)
        {
            Config cfg = SetupController.GetConfig();
            CloudCredentials credentials = CloudCredentialStore.Load(cfg);
            if (string.IsNullOrWhiteSpace(credentials.AccessToken) && !CloudCredentialStore.TryRefresh(cfg, ref credentials))
                throw new InvalidOperationException("Cloud login expired.");

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, CloudMode.ApiBaseUrl() + path);
            request.Content = new StringContent(body ?? "", Encoding.UTF8, contentType ?? "text/plain");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.AccessToken);
            if (!string.IsNullOrWhiteSpace(credentials.DeviceToken))
                request.Headers.TryAddWithoutValidation("X-MDT-Device-Token", credentials.DeviceToken);
            if (!string.IsNullOrWhiteSpace(cfg?.cloudInstallId))
                request.Headers.TryAddWithoutValidation("X-MDT-Install-Id", cfg.cloudInstallId);

            HttpResponseMessage response = CloudHttp.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
            if (retry && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden))
            {
                response.Dispose();
                if (CloudCredentialStore.TryRefresh(cfg, ref credentials))
                    return CloudSendRaw(path, body, contentType, retry: false);
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                CloudCredentialStore.Clear();
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict && response.StatusCode != HttpStatusCode.BadRequest)
                response.EnsureSuccessStatusCode();
            return response;
        }

        private static string ExtractCloudError(string text, HttpStatusCode statusCode)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    JObject o = JObject.Parse(text);
                    string error = o.Value<string>("error");
                    if (!string.IsNullOrWhiteSpace(error)) return error;
                }
                catch { /* use raw text */ }
                string trimmed = text.Trim();
                if (trimmed.Length > 240) trimmed = trimmed.Substring(0, 237) + "...";
                if (!string.IsNullOrWhiteSpace(trimmed)) return trimmed;
            }
            return "MDT Cloud request failed: HTTP " + (int)statusCode;
        }

        private static MDTProVehicleData HydrateVehicleForDisplay(MDTProVehicleData vehicleData, string query = null, string selectedSource = null)
        {
            if (vehicleData == null) return null;
            return DataController.HydrateVehicleFromLiveCdfForDisplay(vehicleData, query, selectedSource);
        }

        private static bool IsContextQuery(string value)
        {
            return string.Equals(value, "context", StringComparison.OrdinalIgnoreCase)
                || value == "%context"
                || string.Equals(value, "current", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReverseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return string.Join(" ", name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Reverse());
        }

        private static bool NameEquals(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
                && a.Equals(b, StringComparison.OrdinalIgnoreCase);
        }

        private static void RefreshReportLocationIfNeeded(Report report)
        {
            if (report == null) return;
            DataController.RefreshMdtLocationOnGameFiberBlocking(350);
            if (report.Location == null || IsEmptyLocation(report.Location))
                report.Location = DataController.MdtPreferredLocation;
        }

        private static bool IsEmptyLocation(Location location)
        {
            if (location == null) return true;
            return string.IsNullOrWhiteSpace(location.Area)
                && string.IsNullOrWhiteSpace(location.Street)
                && string.IsNullOrWhiteSpace(location.County)
                && string.IsNullOrWhiteSpace(location.Postal);
        }
    }
}
