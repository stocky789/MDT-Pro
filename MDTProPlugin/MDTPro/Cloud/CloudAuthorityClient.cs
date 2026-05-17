using MDTPro.Data;
using MDTPro.Setup;
using MDTPro.Utility;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;

namespace MDTPro.Cloud {
    internal static class CloudAuthorityClient {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private static string HydrateSnapshotPath => Path.Combine(SetupController.DataPath, "cloudHydrateSnapshot.json");
        /// <summary>Never overwrite these from cloud policy JSON — they are machine-local bridge / scheduler preferences (same keys as MDT Cloud <c>ClientLocalKeys</c> contract).</summary>
        private static readonly HashSet<string> NeverApplyFromCloudEffectiveConfig = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "gameWorkMode", "gameWorkBridgeBudgetMsPerTick", "passivePickupFirearmScanEnabled"
        };

        internal static void ApplyEffectiveConfig() {
            if (!CloudMode.IsEnabled()) return;
            if (CloudStopThePedBlock.ShouldBlockMdtCloudApiTraffic()) return;
            Config cfg = SetupController.GetConfig();
            CloudCredentials credentials = CloudCredentialStore.Load(cfg);
            if (string.IsNullOrWhiteSpace(credentials.AccessToken)) return;
            try {
                JObject response = GetJson("/api/config/effective", credentials, cfg);
                JObject settings = response["serverSettings"] as JObject;
                if (settings == null) return;

                Type cfgType = typeof(Config);
                foreach (var property in settings.Properties()) {
                    if (NeverApplyFromCloudEffectiveConfig.Contains(property.Name)) continue;
                    FieldInfo field = cfgType.GetField(property.Name, BindingFlags.Instance | BindingFlags.Public);
                    if (field == null) continue;
                    object value = property.Value.ToObject(field.FieldType);
                    field.SetValue(cfg, value);
                }

                Helper.WriteToJsonFile(SetupController.ConfigPath, cfg);
                SetupController.ClearCache();
                Helper.Log($"Cloud settings policy applied (version {response["version"]}).", false, Helper.LogSeverity.Info);
            } catch (Exception ex) {
                Helper.Log($"Cloud settings policy could not be applied: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        internal static void HydrateLocalCache() {
            if (!CloudMode.IsEnabled()) return;
            if (CloudStopThePedBlock.ShouldBlockMdtCloudApiTraffic()) return;
            Config cfg = SetupController.GetConfig();
            CloudCredentials credentials = CloudCredentialStore.Load(cfg);
            if (string.IsNullOrWhiteSpace(credentials.AccessToken) || string.IsNullOrWhiteSpace(credentials.DeviceToken) || string.IsNullOrWhiteSpace(cfg.cloudInstallId)) return;
            try {
                string since = cfg.cloudHydrateSinceUtc;
                string path = string.IsNullOrWhiteSpace(since)
                    ? "/api/mdt/sync/bootstrap?installId=" + Uri.EscapeDataString(cfg.cloudInstallId)
                    : "/api/mdt/sync/delta?installId=" + Uri.EscapeDataString(cfg.cloudInstallId) + "&since=" + Uri.EscapeDataString(since);
                JObject response = GetJson(path, credentials, cfg);
                File.WriteAllText(HydrateSnapshotPath, response.ToString(Newtonsoft.Json.Formatting.Indented));

                CloudSyncQueue.SuppressEnqueue = true;
                try {
                    JArray people = response["people"] as JArray;
                    if (people != null) {
                        foreach (JObject item in people) {
                            if (item.Value<DateTime?>("deletedAtUtc").HasValue) continue;
                            Database.SavePed(ToPed(item));
                            CloudIdentityCache.RememberHydrateRecord("person", item);
                            SaveHydrateRecord("person", item);
                        }
                    }

                    JArray vehicles = response["vehicles"] as JArray;
                    if (vehicles != null) {
                        foreach (JObject item in vehicles) {
                            if (item.Value<DateTime?>("deletedAtUtc").HasValue) continue;
                            if (IsLocalVehicleHydrateRecord(item, cfg.cloudInstallId))
                                Database.SaveVehicle(ToVehicle(item));
                            CloudIdentityCache.RememberHydrateRecord("vehicle", item);
                            SaveHydrateRecord("vehicle", item);
                        }
                    }

                    StoreHydrateRecords("report", response["reports"] as JArray, "reportId");
                    StoreHydrateRecords("courtCase", response["courtCases"] as JArray, "caseNumber");
                    StoreHydrateRecords("supervisionTerm", response["supervisionTerms"] as JArray, "id");
                    StoreHydrateRecords("supervisionEvent", response["supervisionEvents"] as JArray, "id");
                    StoreHydrateRecords("custodyCredit", response["custodyCredits"] as JArray, "id");
                    StoreHydrateRecords("alpr", response["alprEvents"] as JArray, "id");
                } finally {
                    CloudSyncQueue.SuppressEnqueue = false;
                }

                string serverTime = response.Value<string>("serverTimeUtc");
                if (!string.IsNullOrWhiteSpace(serverTime)) {
                    cfg.cloudHydrateSinceUtc = serverTime;
                    Helper.WriteToJsonFile(SetupController.ConfigPath, cfg);
                    SetupController.ClearCache();
                }

                Helper.Log("Cloud hydrate completed for shared people, vehicles, reports, court and ALPR metadata.", false, Helper.LogSeverity.Info);
            } catch (Exception ex) {
                Helper.Log($"Cloud hydrate failed: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        private static void StoreHydrateRecords(string entityType, JArray items, string keyProperty) {
            if (items == null) return;
            foreach (JObject item in items) SaveHydrateRecord(entityType, item, keyProperty);
        }

        private static void SaveHydrateRecord(string entityType, JObject item, string keyProperty = "id") {
            if (item == null) return;
            string key = item.Value<string>(keyProperty) ?? item.Value<string>("id");
            if (string.IsNullOrWhiteSpace(key)) return;
            if (item.Value<DateTime?>("deletedAtUtc").HasValue) {
                Database.DeleteCloudHydrateRecord(entityType, key);
                return;
            }
            Database.SaveCloudHydrateRecord(entityType, key, item);
        }

        private static bool IsLocalVehicleHydrateRecord(JObject item, string localInstallId) {
            if (item == null || string.IsNullOrWhiteSpace(localInstallId)) return false;
            JObject payload = item["payload"] as JObject ?? new JObject();
            string sourceInstallId = item.Value<string>("sourceInstallId") ??
                                     payload.Value<string>("sourceInstallId") ??
                                     payload.Value<string>("SourceInstallId");
            return !string.IsNullOrWhiteSpace(sourceInstallId) &&
                   sourceInstallId.Equals(localInstallId, StringComparison.OrdinalIgnoreCase);
        }

        private static JObject GetJson(string path, CloudCredentials credentials, Config cfg) {
            HttpResponseMessage response = SendGet(path, credentials);
            if ((response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) &&
                CloudCredentialStore.TryRefresh(cfg, ref credentials)) {
                response.Dispose();
                response = SendGet(path, credentials);
            }

            using (response) {
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    CloudCredentialStore.Clear();
                response.EnsureSuccessStatusCode();
                string json = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                return JObject.Parse(json);
            }
        }

        private static HttpResponseMessage SendGet(string path, CloudCredentials credentials) {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, CloudMode.ApiBaseUrl() + path)) {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.AccessToken);
                if (!string.IsNullOrWhiteSpace(credentials.DeviceToken))
                    request.Headers.TryAddWithoutValidation("X-MDT-Device-Token", credentials.DeviceToken);
                return Http.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }

        private static string PickString(JObject primary, JObject secondary, params string[] keys) {
            foreach (string key in keys) {
                string value = primary?.Value<string>(key);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            foreach (string key in keys) {
                string value = secondary?.Value<string>(key);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return null;
        }

        private static MDTProPedData ToPed(JObject item) {
            JObject payload = item["payload"] as JObject ?? new JObject();
            JObject nestedPayload = payload["payload"] as JObject ?? payload["Payload"] as JObject ?? new JObject();
            return new MDTProPedData {
                Name = item.Value<string>("name"),
                FirstName = item.Value<string>("firstName"),
                LastName = item.Value<string>("lastName"),
                Birthday = item.Value<string>("birthday"),
                Address = item.Value<string>("address"),
                Gender = PickString(payload, nestedPayload, "gender", "Gender"),
                IsWanted = item.Value<bool?>("isWanted") ?? false,
                WarrantText = item.Value<string>("warrantText"),
                IsOnProbation = item.Value<bool?>("isOnProbation") ?? false,
                IsOnParole = item.Value<bool?>("isOnParole") ?? false,
                IncarceratedUntil = item.Value<string>("incarceratedUntilUtc"),
                LicenseStatus = item.Value<string>("licenseStatus") ?? PickString(payload, nestedPayload, "licenseStatus", "LicenseStatus"),
                LicenseExpiration = PickString(payload, nestedPayload, "licenseExpiration", "LicenseExpiration"),
                WeaponPermitStatus = item.Value<string>("weaponPermitStatus") ?? PickString(payload, nestedPayload, "weaponPermitStatus", "WeaponPermitStatus"),
                WeaponPermitType = PickString(payload, nestedPayload, "weaponPermitType", "WeaponPermitType"),
                WeaponPermitExpiration = PickString(payload, nestedPayload, "weaponPermitExpiration", "WeaponPermitExpiration"),
                FishingPermitStatus = PickString(payload, nestedPayload, "fishingPermitStatus", "FishingPermitStatus"),
                FishingPermitExpiration = PickString(payload, nestedPayload, "fishingPermitExpiration", "FishingPermitExpiration"),
                HuntingPermitStatus = PickString(payload, nestedPayload, "huntingPermitStatus", "HuntingPermitStatus"),
                HuntingPermitExpiration = PickString(payload, nestedPayload, "huntingPermitExpiration", "HuntingPermitExpiration"),
                TimesStopped = item.Value<int?>("timesStopped") ?? 0,
                IsDeceased = item.Value<bool?>("isDeceased") ?? false,
                DeceasedAt = item.Value<string>("deceasedAtUtc"),
                ModelName = PickString(payload, nestedPayload, "modelName", "ModelName")
            };
        }

        private static MDTProVehicleData ToVehicle(JObject item) {
            JObject payload = item["payload"] as JObject ?? new JObject();
            return new MDTProVehicleData {
                LicensePlate = item.Value<string>("licensePlate"),
                VehicleIdentificationNumber = item.Value<string>("vehicleIdentificationNumber"),
                Owner = item.Value<string>("ownerName"),
                ModelName = item.Value<string>("modelName"),
                Model = item.Value<string>("modelName"),
                RegistrationStatus = item.Value<string>("registrationStatus"),
                InsuranceStatus = item.Value<string>("insuranceStatus"),
                VinStatus = payload.Value<string>("vinStatus")
            };
        }
    }
}
