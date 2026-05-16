using MDTPro.Data;
using MDTPro.Setup;
using MDTPro.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MDTPro.Cloud {
    internal static class CloudIdentityCache {
        private static readonly object LockObj = new object();
        private static bool _loaded;
        private static Dictionary<string, string> _people = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _vehicles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _vehicleOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string CachePath => Path.Combine(SetupController.DataPath, "cloudIdentityMap.json");

        internal static string GetPersonId(MDTProPedData ped) {
            if (ped == null) return null;
            return GetPersonId(BuildPersonKey(ped.Name, ped.Birthday, ped.Address, ped.Gender, ped.ModelHash.ToString(), ped.ModelName));
        }

        internal static string GetVehicleId(MDTProVehicleData vehicle) {
            if (vehicle == null) return null;
            return GetVehicleId(BuildVehicleKey(vehicle.VehicleIdentificationNumber, vehicle.LicensePlate));
        }

        internal static void ApplyPersonId(JObject payload) {
            if (payload == null || !string.IsNullOrWhiteSpace(payload.Value<string>("personId"))) return;
            string id = GetPersonId(BuildPersonKey(
                payload.Value<string>("name") ?? payload.SelectToken("payload.Name")?.ToString(),
                payload.Value<string>("birthday") ?? payload.SelectToken("payload.Birthday")?.ToString(),
                payload.Value<string>("address") ?? payload.SelectToken("payload.Address")?.ToString(),
                payload.Value<string>("gender") ?? payload.SelectToken("payload.Gender")?.ToString(),
                payload.Value<string>("modelHash") ?? payload.SelectToken("payload.ModelHash")?.ToString(),
                payload.Value<string>("modelName") ?? payload.SelectToken("payload.ModelName")?.ToString()));
            if (!string.IsNullOrWhiteSpace(id)) payload["personId"] = id;
        }

        internal static void ApplyVehicleId(JObject payload) {
            if (payload == null) return;
            string key = BuildVehicleKeyFromPayload(payload);
            if (string.IsNullOrWhiteSpace(key)) return;
            string id = GetVehicleId(key);
            if (!string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(payload.Value<string>("vehicleId"))) payload["vehicleId"] = id;
            string ownerPersonId = GetVehicleOwnerPersonId(key);
            if (!string.IsNullOrWhiteSpace(ownerPersonId) && string.IsNullOrWhiteSpace(payload.Value<string>("ownerPersonId"))) payload["ownerPersonId"] = ownerPersonId;
        }

        internal static void RememberPerson(MDTProPedData ped, string personId) {
            if (ped == null) return;
            RememberPerson(BuildPersonKey(ped.Name, ped.Birthday, ped.Address, ped.Gender, ped.ModelHash.ToString(), ped.ModelName), personId);
        }

        internal static void RememberVehicle(MDTProVehicleData vehicle, string vehicleId) {
            if (vehicle == null) return;
            RememberVehicle(BuildVehicleKey(vehicle.VehicleIdentificationNumber, vehicle.LicensePlate), vehicleId);
        }

        internal static void RememberFromSyncResult(string entityType, JObject payload, string entityId) {
            if (payload == null || string.IsNullOrWhiteSpace(entityId)) return;
            if (entityType.Equals("person", StringComparison.OrdinalIgnoreCase))
                RememberPerson(BuildPersonKey(
                    payload.Value<string>("name") ?? payload.SelectToken("payload.Name")?.ToString(),
                    payload.Value<string>("birthday") ?? payload.SelectToken("payload.Birthday")?.ToString(),
                    payload.Value<string>("address") ?? payload.SelectToken("payload.Address")?.ToString(),
                    payload.Value<string>("gender") ?? payload.SelectToken("payload.Gender")?.ToString(),
                    payload.Value<string>("modelHash") ?? payload.SelectToken("payload.ModelHash")?.ToString(),
                    payload.Value<string>("modelName") ?? payload.SelectToken("payload.ModelName")?.ToString()), entityId);
            else if (entityType.Equals("vehicle", StringComparison.OrdinalIgnoreCase))
            {
                string key = BuildVehicleKeyFromPayload(payload);
                RememberVehicle(key, entityId);
                RememberVehicleOwner(key,
                    payload.Value<string>("ownerPersonId") ?? payload.Value<string>("OwnerPersonId"));
            }
        }

        internal static void RememberHydrateRecord(string entityType, JObject item) {
            if (item == null) return;
            string id = item.Value<string>("id");
            if (string.IsNullOrWhiteSpace(id)) return;
            JObject payload = item["payload"] as JObject ?? new JObject();
            if (entityType.Equals("person", StringComparison.OrdinalIgnoreCase))
                RememberPerson(BuildPersonKey(
                    item.Value<string>("name"),
                    item.Value<string>("birthday"),
                    item.Value<string>("address"),
                    payload.Value<string>("gender"),
                    payload.Value<string>("modelHash"),
                    payload.Value<string>("modelName")), id);
            else if (entityType.Equals("vehicle", StringComparison.OrdinalIgnoreCase))
            {
                string key = BuildVehicleKeyFromHydrateRecord(item, payload);
                RememberVehicle(key, id);
                RememberVehicleOwner(key,
                    item.Value<string>("ownerPersonId") ?? payload.Value<string>("ownerPersonId") ?? payload.Value<string>("OwnerPersonId"));
            }
        }

        private static string GetPersonId(string key) {
            if (string.IsNullOrWhiteSpace(key)) return null;
            EnsureLoaded();
            lock (LockObj) return _people.TryGetValue(key, out var id) ? id : null;
        }

        private static string GetVehicleId(string key) {
            if (string.IsNullOrWhiteSpace(key)) return null;
            EnsureLoaded();
            lock (LockObj) return _vehicles.TryGetValue(key, out var id) ? id : null;
        }

        private static string GetVehicleOwnerPersonId(string key) {
            if (string.IsNullOrWhiteSpace(key)) return null;
            EnsureLoaded();
            lock (LockObj) return _vehicleOwners.TryGetValue(key, out var id) ? id : null;
        }

        private static void RememberPerson(string key, string personId) {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(personId)) return;
            EnsureLoaded();
            lock (LockObj) {
                if (_people.TryGetValue(key, out var existing) && !existing.Equals(personId, StringComparison.OrdinalIgnoreCase)) {
                    Helper.Log("Cloud identity cache ignored conflicting person mapping for the same encounter fingerprint.", false, Helper.LogSeverity.Warning);
                    return;
                }
                _people[key] = personId;
                PersistLocked();
            }
        }

        private static void RememberVehicle(string key, string vehicleId) {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(vehicleId)) return;
            EnsureLoaded();
            lock (LockObj) {
                if (_vehicles.TryGetValue(key, out var existing) && !existing.Equals(vehicleId, StringComparison.OrdinalIgnoreCase)) {
                    Helper.Log("Cloud identity cache ignored conflicting vehicle mapping for the same vehicle fingerprint.", false, Helper.LogSeverity.Warning);
                    return;
                }
                _vehicles[key] = vehicleId;
                PersistLocked();
            }
        }

        private static void RememberVehicleOwner(string key, string ownerPersonId) {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(ownerPersonId)) return;
            EnsureLoaded();
            lock (LockObj) {
                if (_vehicleOwners.TryGetValue(key, out var existing) && !existing.Equals(ownerPersonId, StringComparison.OrdinalIgnoreCase))
                    Helper.Log("Cloud identity cache updated vehicle owner mapping from cloud data.", false, Helper.LogSeverity.Info);
                _vehicleOwners[key] = ownerPersonId;
                PersistLocked();
            }
        }

        private static string BuildPersonKey(string name, string birthday, string address, string gender, string modelHash, string modelName) {
            string n = Normalize(name);
            if (string.IsNullOrWhiteSpace(n)) return null;
            string b = Normalize(birthday);
            string a = Normalize(address);
            string g = Normalize(gender);
            string mh = Normalize(modelHash);
            string mn = Normalize(modelName);
            if (string.IsNullOrWhiteSpace(b) && string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(mh) && string.IsNullOrWhiteSpace(mn))
                return null;
            return string.Join("|", new[] { n, b, a, g, mh, mn });
        }

        private static string BuildVehicleKey(string vin, string plate) {
            string v = NormalizeVin(vin);
            if (!string.IsNullOrWhiteSpace(v)) return "vin|" + v;
            string p = NormalizePlate(plate);
            string installId = Normalize(CurrentInstallId());
            return string.IsNullOrWhiteSpace(p) || string.IsNullOrWhiteSpace(installId) ? null : "install|" + installId + "|plate|" + p;
        }

        private static string BuildVehicleKeyFromPayload(JObject payload) {
            if (payload == null) return null;
            string vin = payload.Value<string>("vehicleIdentificationNumber") ?? payload.Value<string>("vin") ?? payload.SelectToken("payload.VehicleIdentificationNumber")?.ToString();
            string plate = payload.Value<string>("licensePlate") ?? payload.Value<string>("plate") ?? payload.SelectToken("payload.LicensePlate")?.ToString();
            string sourceInstallId = payload.Value<string>("sourceInstallId") ?? payload.Value<string>("installId") ?? payload.SelectToken("payload.sourceInstallId")?.ToString() ?? payload.SelectToken("payload.SourceInstallId")?.ToString();
            return BuildVehicleKey(vin, plate, sourceInstallId, requireCurrentInstallForPlate: false);
        }

        private static string BuildVehicleKeyFromHydrateRecord(JObject item, JObject payload) {
            string vin = item.Value<string>("vehicleIdentificationNumber") ?? payload.Value<string>("vehicleIdentificationNumber") ?? payload.Value<string>("VehicleIdentificationNumber");
            string plate = item.Value<string>("licensePlate") ?? payload.Value<string>("licensePlate") ?? payload.Value<string>("LicensePlate");
            string sourceInstallId = item.Value<string>("sourceInstallId") ?? payload.Value<string>("sourceInstallId") ?? payload.Value<string>("SourceInstallId");
            return BuildVehicleKey(vin, plate, sourceInstallId, requireCurrentInstallForPlate: true);
        }

        private static string BuildVehicleKey(string vin, string plate, string sourceInstallId, bool requireCurrentInstallForPlate) {
            string v = NormalizeVin(vin);
            if (!string.IsNullOrWhiteSpace(v)) return "vin|" + v;
            string p = NormalizePlate(plate);
            string installId = Normalize(sourceInstallId);
            string currentInstallId = Normalize(CurrentInstallId());
            if (string.IsNullOrWhiteSpace(installId)) {
                if (requireCurrentInstallForPlate) return null;
                installId = currentInstallId;
            }
            if (requireCurrentInstallForPlate && !string.Equals(installId, currentInstallId, StringComparison.OrdinalIgnoreCase))
                return null;
            return string.IsNullOrWhiteSpace(p) || string.IsNullOrWhiteSpace(installId) ? null : "install|" + installId + "|plate|" + p;
        }

        private static string NormalizeVin(string value) {
            string v = NormalizePlate(value);
            if (string.IsNullOrWhiteSpace(v) || v.Length < 5) return "";
            if (v == "0" || v == "UNKNOWN" || v == "UNK" || v == "NA" || v == "NONE" || v == "NULL") return "";
            return v;
        }

        private static string CurrentInstallId() {
            try {
                return SetupController.GetConfig()?.cloudInstallId;
            } catch {
                return null;
            }
        }

        private static string Normalize(string value) {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var normalized = value.Trim().ToUpperInvariant();
            return normalized == "0" ? "" : normalized;
        }

        private static string NormalizePlate(string value) {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return new string(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static void EnsureLoaded() {
            if (_loaded) return;
            lock (LockObj) {
                if (_loaded) return;
                try {
                    if (File.Exists(CachePath)) {
                        JObject root = JObject.Parse(File.ReadAllText(CachePath));
                        _people = root["people"]?.ToObject<Dictionary<string, string>>() ?? _people;
                        _vehicles = root["vehicles"]?.ToObject<Dictionary<string, string>>() ?? _vehicles;
                        _vehicleOwners = root["vehicleOwners"]?.ToObject<Dictionary<string, string>>() ?? _vehicleOwners;
                    }
                } catch (Exception ex) {
                    Helper.Log($"Cloud identity cache load failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                } finally {
                    _loaded = true;
                }
            }
        }

        private static void PersistLocked() {
            try {
                string dir = Path.GetDirectoryName(CachePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                JObject root = new JObject {
                    ["people"] = JObject.FromObject(_people),
                    ["vehicles"] = JObject.FromObject(_vehicles),
                    ["vehicleOwners"] = JObject.FromObject(_vehicleOwners)
                };
                File.WriteAllText(CachePath, root.ToString(Formatting.None));
            } catch (Exception ex) {
                Helper.Log($"Cloud identity cache persist failed: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }
    }
}
