using CommonDataFramework.Modules;
using CommonDataFramework.Modules.PedDatabase;
using CommonDataFramework.Modules.VehicleDatabase;
using MDTPro.Data.Reports;
using MDTPro.Setup;
using MDTPro.Utility;
using LSPD_First_Response.Engine.Scripting.Entities;
using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace MDTPro.Data {
    public class DataController {
        private const string LifeIncarcerationValue = "LIFE";
        private const double RealDaysPerGameYear = 7d;
        private const double GameDaysPerYear = 365d;
        private const float MaxSentenceMultiplier = 2.5f;
        private const int DefaultJuryTrialSeverityThreshold = 15;
        private const int DefaultCourtRosterRotationDays = 14;

        private class CourtDistrictProfile {
            public string District;
            public string CourtName;
            public string CourtType;
            public string[] Judges;
            public string[] Prosecutors;
            public string[] DefenseAttorneys;
            public float PolicyAdjustment;
        }

        private static readonly CourtDistrictProfile LosSantosDistrict = new CourtDistrictProfile {
            District = "Los Santos Judicial District",
            CourtName = "Los Santos Superior Court",
            CourtType = "Superior Court",
            Judges = new[] { "Hon. K. Martinez", "Hon. S. Alvarez", "Hon. D. Whitaker", "Hon. T. Ellison" },
            Prosecutors = new[] { "A. Mercer", "L. O'Neil", "M. Reeves" },
            DefenseAttorneys = new[] { "Public Defender Office", "C. Price", "R. Sinclair" },
            PolicyAdjustment = 0.02f,
        };

        private static readonly CourtDistrictProfile BlaineDistrict = new CourtDistrictProfile {
            District = "Blaine County Circuit",
            CourtName = "Blaine County Courthouse",
            CourtType = "Circuit Court",
            Judges = new[] { "Hon. R. Bennett", "Hon. J. Monroe", "Hon. P. Gaines" },
            Prosecutors = new[] { "T. Caldwell", "J. Holloway", "D. Pritchard" },
            DefenseAttorneys = new[] { "Public Defender Office", "N. Harper", "M. Lott" },
            PolicyAdjustment = 0.05f,
        };

        private static readonly CourtDistrictProfile IslandDistrict = new CourtDistrictProfile {
            District = "Special Territory Docket",
            CourtName = "San Andreas Territorial Tribunal",
            CourtType = "Special Jurisdiction Court",
            Judges = new[] { "Hon. I. Navarro", "Hon. V. Cross" },
            Prosecutors = new[] { "S. DeLuca", "E. Rowan" },
            DefenseAttorneys = new[] { "Public Defender Office", "B. Donovan", "F. Maddox" },
            PolicyAdjustment = -0.03f,
        };

        private static readonly object _pedDbLock = new object();
        private static List<MDTProPedData> pedDatabase = new List<MDTProPedData>();
        public static IReadOnlyList<MDTProPedData> PedDatabase { get { return GetPedDatabase(); } }

        private static List<MDTProPedData> keepInPedDatabase = new List<MDTProPedData>();

        private static List<MDTProVehicleData> vehicleDatabase = new List<MDTProVehicleData>();
        public static IReadOnlyList<MDTProVehicleData> VehicleDatabase { get { return GetVehicleDatabase(); } }

        private static List<MDTProVehicleData> keepInVehicleDatabase = new List<MDTProVehicleData>();
        private static readonly object _vehicleDbLock = new object();
        private static readonly Random random = new Random();
        private static readonly HashSet<PoolHandle> resolvedPedHandles = new HashSet<PoolHandle>();
        private static readonly object _resolvedPedHandlesLock = new object();
        private static readonly object _contextPedLock = new object();
        private static MDTProPedData _lastContextPedData;
        private static DateTime _lastContextPedSetAt = DateTime.MinValue;
        private static readonly TimeSpan ContextPedTtl = TimeSpan.FromSeconds(60);

        /// <summary>Maps ped name -> (handle, timestamp) for recently identified peds. Citation handout uses this when Holder is null (e.g. DB loaded from file, stub).</summary>
        private static readonly Dictionary<string, (Rage.PoolHandle Handle, DateTime At)> recentlyIdentifiedPedHandles = new Dictionary<string, (Rage.PoolHandle, DateTime)>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _recentlyIdentifiedLock = new object();
        private static readonly TimeSpan RecentlyIdentifiedTtl = TimeSpan.FromMinutes(5);

        // Evidence trigger reliability (for UI: only show reliably tracked items in court breakdown):
        // Reliable: HadWeapon (native at arrest), WasWanted (LSPDFR persona or MDT ped IsWanted/WarrantText), AssaultedPed (damage native + player check), DamagedVehicle (damage native).
        // Resisted: from PR PedAPI.GetPedResistanceAction at arrest (Flee, Attack, Uncooperative = resisted).
        // Unreliable / conditional: WasPatDown (PR OnPedPatDown only; no LSPDFR), WasDrunk (IS_PED_DRUNK rarely set by game),
        // WasFleeing (only true if ped is fleeing at exact moment we run; chase-then-stop usually misses), HadIllegalWeapon (needs CDF permit data),
        // ViolatedSupervision (only from our DB prior cases; no game/PR API).
        private class PedEvidenceContext {
            public bool HadWeapon;
            public bool WasWanted;
            public bool WasPatDown;
            public bool WasDrunk;
            public bool WasFleeing;
            public bool AssaultedPed;
            public bool DamagedVehicle;
            public bool HadIllegalWeapon;
            public bool ViolatedSupervision;
            public bool Resisted;
            public DateTime CapturedAt = DateTime.UtcNow;
        }

        private static readonly Dictionary<string, PedEvidenceContext> pedEvidenceCache =
            new Dictionary<string, PedEvidenceContext>(StringComparer.OrdinalIgnoreCase);
        private static readonly object pedEvidenceLock = new object();
        /// <summary>Ped handles we've seen fleeing; looked up at arrest by handle so we don't miss due to name mismatch during chase.</summary>
        private static readonly Dictionary<Rage.PoolHandle, DateTime> fleeingPedHandles = new Dictionary<Rage.PoolHandle, DateTime>();
        /// <summary>Handles that caused vehicle damage (e.g. during pursuit); merged at arrest when we may not have had name at surrender.</summary>
        private static readonly Dictionary<Rage.PoolHandle, DateTime> damagedVehicleHandles = new Dictionary<Rage.PoolHandle, DateTime>();
        /// <summary>Handles that assaulted the player; merged at arrest in case game clears damage state.</summary>
        private static readonly Dictionary<Rage.PoolHandle, DateTime> assaultedPedHandles = new Dictionary<Rage.PoolHandle, DateTime>();
        /// <summary>Handles that were armed when we saw them (e.g. at surrender); at arrest they may be disarmed so we merge by handle.</summary>
        private static readonly Dictionary<Rage.PoolHandle, DateTime> hadWeaponHandles = new Dictionary<Rage.PoolHandle, DateTime>();

        private static readonly HashSet<string> capturedVehicleSearchPlates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object capturedVehicleSearchLock = new object();
        private const int MaxCapturedVehicleSearchPlates = 100;

        private static readonly object capturedPickupHandlesLock = new object();
        private static readonly HashSet<uint> capturedPickupHandles = new HashSet<uint>();

        private static DateTime lastFirearmPollDebugLog = DateTime.MinValue;

        /// <summary>GTA V weapon hashes for melee/throwables that should not appear in Firearms Check. PR/CDF return all weapons; we filter to actual firearms only.</summary>
        private static readonly HashSet<uint> MeleeAndNonFirearmHashes = new HashSet<uint> {
            2460120199u, 2508868239u, 3441901897u, 3638508604u, 4192643659u, 2227010557u, 2725352035u,
            2343591895u, 1141786504u, 1317494643u, 4191993645u, 2578778090u, 3713923289u, 1737195953u,
            419712736u, 2484171525u, 940833800u, 3756226112u, 600439132u, 2694266206u, 1233104067u,
            2481070269u, 615608432u, 3125143736u, 2874559379u, 4256991824u, 126349499u, 741814745u,
            101631238u, 3126027122u, 3215233542u, 4222310262u
        };

        internal static List<CourtData> courtDatabase = new List<CourtData>();
        public static IReadOnlyList<CourtData> CourtDatabase => courtDatabase;

        internal static OfficerInformationData OfficerInformationData = new OfficerInformationData();
        internal static OfficerInformationData OfficerInformation = new OfficerInformationData();

        private static ShiftData currentShiftData = new ShiftData();
        internal static ShiftData CurrentShiftData => currentShiftData;

        internal static List<ShiftData> shiftHistoryData = new List<ShiftData>();
        public static IReadOnlyList<ShiftData> ShiftHistoryData => shiftHistoryData;

        internal static List<IncidentReport> incidentReports = new List<IncidentReport>();
        public static IReadOnlyList<IncidentReport> IncidentReports => incidentReports;

        internal static List<CitationReport> citationReports = new List<CitationReport>();
        public static IReadOnlyList<CitationReport> CitationReports => citationReports;

        internal static List<ArrestReport> arrestReports = new List<ArrestReport>();
        public static IReadOnlyList<ArrestReport> ArrestReports => arrestReports;

        internal static List<ImpoundReport> impoundReports = new List<ImpoundReport>();
        public static IReadOnlyList<ImpoundReport> ImpoundReports => impoundReports;

        internal static List<TrafficIncidentReport> trafficIncidentReports = new List<TrafficIncidentReport>();
        public static IReadOnlyList<TrafficIncidentReport> TrafficIncidentReports => trafficIncidentReports;

        internal static List<InjuryReport> injuryReports = new List<InjuryReport>();
        public static IReadOnlyList<InjuryReport> InjuryReports => injuryReports;

        internal static List<PropertyEvidenceReceiptReport> propertyEvidenceReports = new List<PropertyEvidenceReceiptReport>();
        public static IReadOnlyList<PropertyEvidenceReceiptReport> PropertyEvidenceReports => propertyEvidenceReports;

        /// <summary>Real (UTC) creation time for reports added this session. Used by recentReports so "last 60 min" works even when useInGameTime is on.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, DateTime> _reportRealCreatedAt = new System.Collections.Generic.Dictionary<string, DateTime>();
        internal static DateTime? GetReportRealCreatedAt(string reportId) {
            if (string.IsNullOrEmpty(reportId)) return null;
            lock (_reportRealCreatedAt) {
                return _reportRealCreatedAt.TryGetValue(reportId, out var dt) ? (DateTime?)dt : null;
            }
        }

        internal static Location PlayerLocation = new Location();
        internal static string CurrentTime = World.TimeOfDay.ToString();
        internal static PlayerCoords PlayerCoords = new PlayerCoords();

        internal static string ActivePostalCodeSet;

        internal static void SetDatabases() {
            SetPedDatabase();
            SetVehicleDatabase();
        }

        /// <summary>Cached on game thread for /data/nearbyVehicles so the HTTP handler never touches game entities.</summary>
        internal static List<CachedNearbyVehicleEntry> CachedNearbyVehicles = new List<CachedNearbyVehicleEntry>();
        private static readonly object _cachedNearbyLock = new object();
        internal struct CachedNearbyVehicleEntry {
            public string LicensePlate;
            public string ModelDisplayName;
            public float? Distance;
            public bool IsStolen;
        }

        internal static void SetDynamicData() {
            UpdatePlayerLocation();
            CurrentTime = World.TimeOfDay.ToString();
            UpdateCachedNearbyVehicles();
        }

        private static void UpdateCachedNearbyVehicles() {
            var list = new List<CachedNearbyVehicleEntry>();
            if (Main.Player != null && Main.Player.Exists()) {
                lock (_vehicleDbLock) {
                    foreach (var v in vehicleDatabase) {
                        if (string.IsNullOrEmpty(v.LicensePlate)) continue;
                        float? dist = null;
                        try {
                            if (v.Holder != null && v.Holder.Exists())
                                dist = (float?)Math.Round(Main.Player.DistanceTo(v.Holder), 1);
                        } catch { }
                        list.Add(new CachedNearbyVehicleEntry {
                            LicensePlate = v.LicensePlate,
                            ModelDisplayName = v.ModelDisplayName,
                            Distance = dist,
                            IsStolen = v.IsStolen
                        });
                    }
                }
                list.Sort((a, b) => (a.Distance ?? float.MaxValue).CompareTo(b.Distance ?? float.MaxValue));
            }
            lock (_cachedNearbyLock) {
                CachedNearbyVehicles = list;
            }
        }

        /// <summary>Called from HTTP handler; returns cached list so handler never touches game entities.</summary>
        internal static List<CachedNearbyVehicleEntry> GetCachedNearbyVehicles(int limit) {
            if (limit < 1) limit = 1;
            if (limit > 20) limit = 20;
            lock (_cachedNearbyLock) {
                return (CachedNearbyVehicles ?? new List<CachedNearbyVehicleEntry>()).Take(limit).ToList();
            }
        }

        private static void PopulatePedDatabase() {
            if (!Main.Player.Exists()) {
                Helper.Log("Failed to get nearby peds; Invalid player", true, Helper.LogSeverity.Error);
                return;
            }
            Ped[] nearbyPeds = Main.Player.GetNearbyPeds(SetupController.GetConfig().maxNumberOfNearbyPedsOrVehicles);
            for (int i = 0; i < nearbyPeds.Length; i++) {
                Ped p = nearbyPeds[i];
                if (p == null || !p.Exists()) continue;
                try {
                    ResolvePedForReEncounter(p);
                } catch (Exception ex) {
                    Helper.Log($"Skipping ped in PopulatePedDatabase: {ex.Message}", false, Helper.LogSeverity.Warning);
                }
            }
        }

        private static void PopulateVehicleDatabase() {
            if (!Main.Player.Exists()) {
                Helper.Log("Failed to get nearby vehicles; Invalid player", true, Helper.LogSeverity.Error);
                return;
            }
            lock (_vehicleDbLock) {
                int limit = SetupController.GetConfig().maxNumberOfNearbyPedsOrVehicles * SetupController.GetConfig().databaseLimitMultiplier;
                if (vehicleDatabase.Count > limit) {
                    List<MDTProVehicleData> keysToRemove = vehicleDatabase.Take(SetupController.GetConfig().maxNumberOfNearbyPedsOrVehicles).ToList();
                    foreach (MDTProVehicleData key in keysToRemove) {
                        if (keepInVehicleDatabase.Any(x => x.LicensePlate == key.LicensePlate)) continue;
                        vehicleDatabase.Remove(key);
                    }
                }
            }
            Vehicle[] nearbyVehicles = Main.Player.GetNearbyVehicles(SetupController.GetConfig().maxNumberOfNearbyPedsOrVehicles);
            for (int i = 0; i < nearbyVehicles.Length; i++) {
                Vehicle v = nearbyVehicles[i];
                if (v == null || !v.Exists()) continue;
                try {
                    MDTProVehicleData mdtProVehicleData = new MDTProVehicleData(v);
                    if (mdtProVehicleData == null || mdtProVehicleData.LicensePlate == null) continue;
                    bool exists;
                    lock (_vehicleDbLock) {
                        exists = vehicleDatabase.Any(x => x.LicensePlate == mdtProVehicleData.LicensePlate);
                    }
                    if (exists) continue;
                    TryApplyReEncounterVehicleProfile(mdtProVehicleData, v);
                    MergeBOLOsFromStubByPlate(mdtProVehicleData);
                    lock (_vehicleDbLock) {
                        if (vehicleDatabase.Any(x => x.LicensePlate == mdtProVehicleData.LicensePlate)) continue;
                        vehicleDatabase.Add(mdtProVehicleData);
                    }
                } catch (Exception ex) {
                    Helper.Log($"Skipping vehicle in PopulateVehicleDatabase: {ex.Message}", false, Helper.LogSeverity.Warning);
                }
            }
        }

        private static void TryApplyReEncounterVehicleProfile(MDTProVehicleData currentVehicleData, Vehicle vehicle) {
            MDTProVehicleData persistentMatch = GetReEncounterVehicleCandidate(currentVehicleData, vehicle);
            if (persistentMatch == null) return;

            string originalPlate = currentVehicleData.LicensePlate;
            currentVehicleData.ApplyPersistentVehicleIdentity(persistentMatch);
            SyncSingleVehicleToCDF(currentVehicleData);
            KeepVehicleInDatabase(currentVehicleData);
            Helper.Log($"Vehicle re-encounter matched by model+owner: {originalPlate} => same as {persistentMatch.LicensePlate} ({currentVehicleData.Owner})", false, Helper.LogSeverity.Info);
        }

        /// <summary>Called when Policing Redefined fires OnVehicleStopped. Resolves the driver and ensures the vehicle is in the DB so the MDT has them for citations/reports.</summary>
        internal static void ResolveVehicleAndDriverForStop(Vehicle vehicle) {
            if (vehicle == null || !vehicle.Exists()) return;
            try {
                Ped driver = vehicle.Driver;
                if (driver != null && driver.IsValid()) ResolvePedForReEncounter(driver);

                MDTProVehicleData mdtProVehicleData = new MDTProVehicleData(vehicle);
                if (mdtProVehicleData.LicensePlate == null) return;
                TryApplyReEncounterVehicleProfile(mdtProVehicleData, vehicle);
                MergeBOLOsFromStubByPlate(mdtProVehicleData);
                lock (_vehicleDbLock) {
                    if (!vehicleDatabase.Any(x => x.LicensePlate == mdtProVehicleData.LicensePlate))
                        vehicleDatabase.Add(mdtProVehicleData);
                }
                KeepVehicleInDatabase(mdtProVehicleData);
            } catch (Exception ex) {
                Helper.Log($"ResolveVehicleAndDriverForStop: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        private static MDTProVehicleData GetReEncounterVehicleCandidate(MDTProVehicleData currentVehicleData, Vehicle vehicle) {
            if (currentVehicleData == null) return null;
            if (string.IsNullOrEmpty(currentVehicleData.ModelName)) return null;

            string ownerForMatch = null;
            Ped driver = vehicle?.Driver;
            if (driver != null && driver.IsValid()) {
                MDTProPedData driverData = GetPedDataForPed(driver);
                if (driverData != null && !string.IsNullOrEmpty(driverData.Name))
                    ownerForMatch = driverData.Name;
            }
            if (string.IsNullOrEmpty(ownerForMatch)) ownerForMatch = currentVehicleData.Owner;
            if (string.IsNullOrEmpty(ownerForMatch)) return null;

            bool driverIsKnownPed;
            lock (_pedDbLock) {
                driverIsKnownPed = keepInPedDatabase.Any(p => p != null && p.Name?.Equals(ownerForMatch, StringComparison.OrdinalIgnoreCase) == true);
            }
            float chance = driverIsKnownPed
                ? SetupController.GetConfig().reEncounterVehicleChanceWhenPedKnown
                : SetupController.GetConfig().reEncounterVehicleChance;
            if (chance <= 0f) chance = SetupController.GetConfig().reEncounterChance;
            if (chance <= 0f) return null;
            if (chance >= 1f) chance = 1f;
            if (random.NextDouble() > chance) return null;

            string ownerLower = ownerForMatch.ToLower();
            string modelLower = currentVehicleData.ModelName?.ToLower() ?? "";

            List<MDTProVehicleData> candidates;
            lock (_vehicleDbLock) {
                candidates = keepInVehicleDatabase
                    .Where(v => v != null && !string.IsNullOrEmpty(v.LicensePlate) && !string.IsNullOrEmpty(v.ModelName))
                    .Where(v => !vehicleDatabase.Any(active => active.LicensePlate == v.LicensePlate))
                    .Where(v => (v.Owner?.ToLower() ?? "") == ownerLower && (v.ModelName?.ToLower() ?? "") == modelLower)
                    .ToList();
            }

            if (candidates.Count == 0) return null;
            return candidates[random.Next(candidates.Count)];
        }

        private static void SetPedDatabase() {
            lock (_pedDbLock) {
                if (pedDatabase.Count > SetupController.GetConfig().maxNumberOfNearbyPedsOrVehicles * SetupController.GetConfig().databaseLimitMultiplier) {
                    List<MDTProPedData> keysToRemove = pedDatabase.Take(SetupController.GetConfig().maxNumberOfNearbyPedsOrVehicles).ToList();
                    foreach (MDTProPedData key in keysToRemove) {
                        if (keepInPedDatabase.Any(x => x.Name == key.Name)) continue;
                        pedDatabase.Remove(key);
                    }
                }
            }
            PopulatePedDatabase();
        }

        private static void SetVehicleDatabase() {
            lock (_vehicleDbLock) {
                if (vehicleDatabase.Count > SetupController.GetConfig().maxNumberOfNearbyPedsOrVehicles * SetupController.GetConfig().databaseLimitMultiplier) {
                    List<MDTProVehicleData> keysToRemove = vehicleDatabase.Take(SetupController.GetConfig().maxNumberOfNearbyPedsOrVehicles).ToList();
                    foreach (MDTProVehicleData key in keysToRemove) {
                        if (keepInVehicleDatabase.Any(x => x.LicensePlate == key.LicensePlate)) continue;
                        vehicleDatabase.Remove(key);
                    }
                }
            }
            PopulateVehicleDatabase();
        }

        private static List<MDTProPedData> GetPedDatabase() {
            lock (_pedDbLock) {
                return pedDatabase.ToList();
            }
        }

        private static List<MDTProVehicleData> GetVehicleDatabase() {
            lock (_vehicleDbLock) {
                return vehicleDatabase.ToList();
            }
        }

        /// <summary>Look up vehicle by license plate for ALPR. Returns MDT record if present (stolen, registration, etc.).</summary>
        internal static MDTProVehicleData GetVehicleByLicensePlate(string plate) {
            if (string.IsNullOrWhiteSpace(plate)) return null;
            string key = plate.Trim();
            lock (_vehicleDbLock) {
                var v = vehicleDatabase.FirstOrDefault(x => string.Equals(x.LicensePlate, key, StringComparison.OrdinalIgnoreCase));
                if (v != null) return v;
                return keepInVehicleDatabase.FirstOrDefault(x => string.Equals(x.LicensePlate, key, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Look up vehicle by plate or VIN. Checks both vehicleDatabase and keepInVehicleDatabase.</summary>
        internal static MDTProVehicleData GetVehicleByPlateOrVin(string plateOrVin) {
            if (string.IsNullOrWhiteSpace(plateOrVin)) return null;
            string key = plateOrVin.Trim();
            lock (_vehicleDbLock) {
                var v = vehicleDatabase.FirstOrDefault(x =>
                    string.Equals(x.LicensePlate, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.VehicleIdentificationNumber, key, StringComparison.OrdinalIgnoreCase));
                if (v != null) return v;
                return keepInVehicleDatabase.FirstOrDefault(x =>
                    string.Equals(x.LicensePlate, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.VehicleIdentificationNumber, key, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>All vehicles that have at least one active (non-expired) BOLO, for the BOLO noticeboard. In-world vehicles first (so CanModifyBOLOs is true when applicable); then persistent by plate, deduped.</summary>
        internal static System.Collections.Generic.List<object> GetActiveBOLOs() {
            var list = new System.Collections.Generic.List<object>();
            var seenPlates = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (_vehicleDbLock) {
                foreach (var v in vehicleDatabase) {
                    if (v?.BOLOs == null || v.BOLOs.Length == 0 || string.IsNullOrEmpty(v.LicensePlate)) continue;
                    var activeBolos = v.BOLOs.Where(b => IsBOLOActive(b)).ToArray();
                    if (activeBolos.Length == 0) continue;
                    if (seenPlates.Contains(v.LicensePlate)) continue;
                    seenPlates.Add(v.LicensePlate);
                    list.Add(new {
                        v.LicensePlate,
                        v.ModelDisplayName,
                        v.IsStolen,
                        BOLOs = activeBolos,
                        CanModifyBOLOs = v.CanModifyBOLOs
                    });
                }
                foreach (var v in keepInVehicleDatabase) {
                    if (v?.BOLOs == null || v.BOLOs.Length == 0 || string.IsNullOrEmpty(v.LicensePlate)) continue;
                    if (seenPlates.Contains(v.LicensePlate)) continue;
                    var activeBolos = v.BOLOs.Where(b => IsBOLOActive(b)).ToArray();
                    if (activeBolos.Length == 0) continue;
                    seenPlates.Add(v.LicensePlate);
                    list.Add(new {
                        v.LicensePlate,
                        v.ModelDisplayName,
                        v.IsStolen,
                        BOLOs = activeBolos,
                        CanModifyBOLOs = false
                    });
                }
            }
            return list;
        }

        /// <summary>Called when CDF removes ped data (entity despawned). Removes from pedDatabase only; keepInPedDatabase and SQLite unchanged.</summary>
        public static void OnCDFPedDataRemoved(Rage.Ped ped, PedData pedData) {
            if (pedData == null) return;
            string name = null;
            try { name = pedData.FullName; } catch { }
            uint? handle = null;
            try { if (ped != null && ped.Exists()) handle = ped.Handle; } catch { }
            lock (_pedDbLock) {
                pedDatabase.RemoveAll(p => {
                    if (p == null) return false;
                    try {
                        if (!string.IsNullOrEmpty(name) && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) {
                            if (handle.HasValue && p.Holder != null && p.Holder.IsValid() && p.Holder.Handle == handle.Value) return true;
                            if (p.Holder == null || !p.Holder.IsValid()) return true;
                        }
                        if (handle.HasValue && p.Holder != null && p.Holder.IsValid() && p.Holder.Handle == handle.Value) return true;
                    } catch { /* Holder disposed */ }
                    return false;
                });
            }
        }

        /// <summary>Called when CDF removes vehicle data (entity despawned). Removes from vehicleDatabase only; keepInVehicleDatabase and SQLite unchanged.</summary>
        public static void OnCDFVehicleDataRemoved(Rage.Vehicle vehicle, VehicleData vehicleData) {
            if (vehicleData == null) return;
            string plate = null;
            try {
                if (vehicle != null && vehicle.Exists()) plate = vehicle.LicensePlate;
                if (string.IsNullOrEmpty(plate) && vehicleData.Holder != null && vehicleData.Holder.Exists()) plate = vehicleData.Holder.LicensePlate;
            } catch { }
            uint? handle = null;
            try { if (vehicle != null && vehicle.Exists()) handle = vehicle.Handle; } catch { }
            lock (_vehicleDbLock) {
                vehicleDatabase.RemoveAll(v => {
                    if (v == null) return false;
                    try {
                        if (!string.IsNullOrEmpty(plate) && string.Equals(v.LicensePlate, plate, StringComparison.OrdinalIgnoreCase)) {
                            if (handle.HasValue && v.Holder != null && v.Holder.Exists() && v.Holder.Handle == handle.Value) return true;
                            if (v.Holder == null || !v.Holder.Exists()) return true;
                        }
                        if (handle.HasValue && v.Holder != null && v.Holder.Exists() && v.Holder.Handle == handle.Value) return true;
                    } catch { /* Holder disposed */ }
                    return false;
                });
            }
        }

        /// <summary>Sync MDT ped data to CDF so PR and other mods see correct license/permits. MDT SQLite is source of truth for court-ordered revocations.</summary>
        internal static void SyncPedDatabaseWithCDF() {
            foreach (MDTProPedData databasePed in PedDatabase) {
                SyncSinglePedToCDF(databasePed);
            }
        }

        /// <summary>Push a single ped's MDT data to CDF (DriversLicense, WeaponPermit, FishingPermit, etc.). Ensures CDF/PR reflect our persisted revocations.</summary>
        private static void SyncSinglePedToCDF(MDTProPedData databasePed) {
            if (databasePed == null || databasePed.CDFPedData == null) return;
            try {
                databasePed.CDFPedData.Wanted = databasePed.IsWanted;
                databasePed.CDFPedData.IsOnProbation = databasePed.IsOnProbation;
                databasePed.CDFPedData.IsOnParole = databasePed.IsOnParole;
                databasePed.CDFPedData.Citations = databasePed.Citations?.Count ?? 0;
                databasePed.CDFPedData.TimesStopped = databasePed.TimesStopped;
                try { databasePed.CDFPedData.AdvisoryText = databasePed.AdvisoryText ?? ""; } catch { }

                if (!string.IsNullOrEmpty(databasePed.LicenseStatus) && Enum.TryParse(databasePed.LicenseStatus, true, out ELicenseState licenseStatusValue)) {
                    databasePed.CDFPedData.DriversLicenseState = licenseStatusValue;
                }
                if (!string.IsNullOrEmpty(databasePed.LicenseExpiration) && DateTime.TryParse(databasePed.LicenseExpiration, out DateTime licenseExp)) {
                    try { databasePed.CDFPedData.DriversLicenseExpiration = licenseExp; } catch { }
                }

                if (databasePed.CDFPedData.WeaponPermit != null && !string.IsNullOrEmpty(databasePed.WeaponPermitStatus) && Enum.TryParse(databasePed.WeaponPermitStatus, true, out EDocumentStatus weaponStatus)) {
                    try { databasePed.CDFPedData.WeaponPermit.Status = weaponStatus; } catch { }
                }
                if (databasePed.CDFPedData.WeaponPermit != null && !string.IsNullOrEmpty(databasePed.WeaponPermitExpiration) && DateTime.TryParse(databasePed.WeaponPermitExpiration, out DateTime weaponExp)) {
                    try { databasePed.CDFPedData.WeaponPermit.ExpirationDate = weaponExp; } catch { }
                }

                if (databasePed.CDFPedData.FishingPermit != null && !string.IsNullOrEmpty(databasePed.FishingPermitStatus) && Enum.TryParse(databasePed.FishingPermitStatus, true, out EDocumentStatus fishingStatus)) {
                    try { databasePed.CDFPedData.FishingPermit.Status = fishingStatus; } catch { }
                }
                if (databasePed.CDFPedData.FishingPermit != null && !string.IsNullOrEmpty(databasePed.FishingPermitExpiration) && DateTime.TryParse(databasePed.FishingPermitExpiration, out DateTime fishingExp)) {
                    try { databasePed.CDFPedData.FishingPermit.ExpirationDate = fishingExp; } catch { }
                }

                if (databasePed.CDFPedData.HuntingPermit != null && !string.IsNullOrEmpty(databasePed.HuntingPermitStatus) && Enum.TryParse(databasePed.HuntingPermitStatus, true, out EDocumentStatus huntingStatus)) {
                    try { databasePed.CDFPedData.HuntingPermit.Status = huntingStatus; } catch { }
                }
                if (databasePed.CDFPedData.HuntingPermit != null && !string.IsNullOrEmpty(databasePed.HuntingPermitExpiration) && DateTime.TryParse(databasePed.HuntingPermitExpiration, out DateTime huntingExp)) {
                    try { databasePed.CDFPedData.HuntingPermit.ExpirationDate = huntingExp; } catch { }
                }
            } catch (Exception ex) {
                Helper.Log($"SyncSinglePedToCDF skip ped: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>Add a BOLO to a vehicle. Works for in-world vehicles (via CDF) or persistent/stub records. Accepts optional modelDisplayName for new stubs.</summary>
        internal static bool TryAddBOLOByPlate(string licensePlate, string reason, DateTime expiresAt, string issuedBy, string modelDisplayName = null) {
            if (string.IsNullOrWhiteSpace(licensePlate) || string.IsNullOrWhiteSpace(reason)) return false;
            var plate = licensePlate.Trim();
            var bolo = new VehicleBOLO(reason.Trim(), DateTime.UtcNow, expiresAt, issuedBy ?? "LSPD");
            // 1. Try in-world vehicle first
            if (TryAddBOLOToVehicle(plate, reason, expiresAt, issuedBy)) return true;
            // 2. Look up in keepInVehicleDatabase or create stub
            MDTProVehicleData vData;
            lock (_vehicleDbLock) {
                vData = keepInVehicleDatabase.FirstOrDefault(v => v != null && string.Equals(v.LicensePlate, plate, StringComparison.OrdinalIgnoreCase));
            }
            if (vData == null) {
                vData = new MDTProVehicleData {
                    LicensePlate = plate,
                    ModelDisplayName = modelDisplayName?.Trim(),
                    BOLOs = new[] { bolo }
                };
            } else {
                var list = new List<VehicleBOLO>(vData.BOLOs ?? Array.Empty<VehicleBOLO>());
                list.Add(bolo);
                vData.BOLOs = list.ToArray();
            }
            try {
                KeepVehicleInDatabase(vData);
                Database.SaveVehicle(vData);
                return true;
            } catch (Exception ex) {
                Helper.Log($"TryAddBOLOByPlate failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                return false;
            }
        }

        /// <summary>Add a BOLO to a vehicle in world. Returns true if vehicle was in world and BOLO was added. Vehicle must be in vehicleDatabase with valid Holder.</summary>
        internal static bool TryAddBOLOToVehicle(string licensePlate, string reason, DateTime expiresAt, string issuedBy) {
            if (string.IsNullOrWhiteSpace(licensePlate) || string.IsNullOrWhiteSpace(reason)) return false;
            MDTProVehicleData vData;
            lock (_vehicleDbLock) {
                vData = vehicleDatabase.FirstOrDefault(v => v != null && v.Holder != null && v.Holder.Exists() && string.Equals(v.LicensePlate, licensePlate.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            if (vData?.CDFVehicleData == null) return false;
            try {
                var bolo = new VehicleBOLO(reason.Trim(), DateTime.UtcNow, expiresAt, issuedBy ?? "LSPD");
                vData.CDFVehicleData.AddBOLO(bolo);
                vData.BOLOs = vData.CDFVehicleData.GetAllBOLOs();
                KeepVehicleInDatabase(vData);
                Database.SaveVehicle(vData);
                return true;
            } catch (Exception ex) {
                Helper.Log($"TryAddBOLOToVehicle failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                return false;
            }
        }

        /// <summary>Remove a BOLO from a vehicle by reason. Works for in-world vehicles and persistent/stub records.</summary>
        internal static bool TryRemoveBOLOFromVehicleOrStub(string licensePlate, string reason) {
            if (string.IsNullOrWhiteSpace(licensePlate)) return false;
            var plate = licensePlate.Trim();
            var reasonTrim = (reason ?? "").Trim();
            MDTProVehicleData vData;
            lock (_vehicleDbLock) {
                vData = vehicleDatabase.FirstOrDefault(v => v != null && v.Holder != null && v.Holder.Exists() && string.Equals(v.LicensePlate, plate, StringComparison.OrdinalIgnoreCase));
                if (vData == null)
                    vData = keepInVehicleDatabase.FirstOrDefault(v => v != null && string.Equals(v.LicensePlate, plate, StringComparison.OrdinalIgnoreCase));
            }
            if (vData == null) return false;
            if (vData.CDFVehicleData != null) {
                var bolos = vData.CDFVehicleData.GetAllBOLOs();
                if (bolos != null && bolos.Length > 0) {
                    VehicleBOLO toRemove = null;
                    foreach (var b in bolos) {
                        if (b != null && string.Equals(b.Reason, reasonTrim, StringComparison.OrdinalIgnoreCase)) {
                            toRemove = b;
                            break;
                        }
                    }
                    if (toRemove != null) {
                        try {
                            vData.CDFVehicleData.RemoveBOLO(toRemove);
                            vData.BOLOs = vData.CDFVehicleData.GetAllBOLOs();
                            KeepVehicleInDatabase(vData);
                            Database.SaveVehicle(vData);
                            return true;
                        } catch (Exception ex) {
                            Helper.Log($"TryRemoveBOLOFromVehicleOrStub failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                            return false;
                        }
                    }
                }
            }
            if (vData.BOLOs == null || vData.BOLOs.Length == 0) return false;
            var list = vData.BOLOs.Where(b => b == null || !string.Equals(b.Reason, reasonTrim, StringComparison.OrdinalIgnoreCase)).ToList();
            if (list.Count == vData.BOLOs.Length) return false;
            vData.BOLOs = list.ToArray();
            try {
                KeepVehicleInDatabase(vData);
                Database.SaveVehicle(vData);
                return true;
            } catch (Exception ex) {
                Helper.Log($"TryRemoveBOLOFromVehicleOrStub failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                return false;
            }
        }

        /// <summary>Remove a BOLO from a vehicle by reason. Works for in-world and stub records.</summary>
        internal static bool TryRemoveBOLOFromVehicle(string licensePlate, string reason) {
            return TryRemoveBOLOFromVehicleOrStub(licensePlate, reason);
        }

        /// <summary>True if the vehicle has at least one active (non-expired) BOLO. Used by ALPR to add BOLO flag.</summary>
        internal static bool HasActiveBOLOs(MDTProVehicleData v) {
            if (v?.BOLOs == null || v.BOLOs.Length == 0) return false;
            return v.BOLOs.Any(IsBOLOActive);
        }

        /// <summary>Returns true if the BOLO is still active (not expired). Uses IsActive when available, else Expires vs UtcNow.</summary>
        private static bool IsBOLOActive(VehicleBOLO b) {
            if (b == null) return false;
            try {
                var isActiveProp = b.GetType().GetProperty("IsActive");
                if (isActiveProp != null && isActiveProp.PropertyType == typeof(bool))
                    return (bool)isActiveProp.GetValue(b);
            } catch { }
            try {
                var expiresProp = b.GetType().GetProperty("Expires");
                if (expiresProp != null && expiresProp.GetValue(b) is DateTime expires)
                    return expires > DateTime.UtcNow;
            } catch { }
            return true;
        }

        /// <summary>Merge BOLOs from a plate-matched stub (keepInVehicleDatabase) into an in-world vehicle so noticeboard BOLOs apply when the car spawns. Replaces stub with in-world record in keepInVehicleDatabase so persistence is correct.</summary>
        internal static void MergeBOLOsFromStubByPlate(MDTProVehicleData inWorldVehicle) {
            if (inWorldVehicle?.CDFVehicleData == null || string.IsNullOrWhiteSpace(inWorldVehicle.LicensePlate)) return;
            string plate = inWorldVehicle.LicensePlate.Trim();
            MDTProVehicleData stub;
            lock (_vehicleDbLock) {
                stub = keepInVehicleDatabase.FirstOrDefault(v => v != null && string.Equals(v.LicensePlate, plate, StringComparison.OrdinalIgnoreCase)
                    && v != inWorldVehicle && (v.Holder == null || !v.Holder.Exists()));
            }
            if (stub?.BOLOs == null || stub.BOLOs.Length == 0) return;
            var existingReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (inWorldVehicle.BOLOs != null) {
                foreach (var b in inWorldVehicle.BOLOs)
                    if (b?.Reason != null) existingReasons.Add(b.Reason);
            }
            bool merged = false;
            foreach (var b in stub.BOLOs) {
                if (b == null || !IsBOLOActive(b) || existingReasons.Contains(b.Reason ?? "")) continue;
                try {
                    DateTime issued, expires;
                    string issuedBy;
                    try {
                        issued = (DateTime)(b.GetType().GetProperty("Issued")?.GetValue(b) ?? DateTime.UtcNow);
                        expires = (DateTime)(b.GetType().GetProperty("Expires")?.GetValue(b) ?? DateTime.UtcNow.AddDays(7));
                        issuedBy = (string)(b.GetType().GetProperty("IssuedBy")?.GetValue(b)) ?? "LSPD";
                    } catch {
                        issued = DateTime.UtcNow;
                        expires = DateTime.UtcNow.AddDays(7);
                        issuedBy = "LSPD";
                    }
                    var cdfBolo = new VehicleBOLO(b.Reason ?? "Unknown", issued, expires, issuedBy);
                    inWorldVehicle.CDFVehicleData.AddBOLO(cdfBolo);
                    var list = new List<VehicleBOLO>(inWorldVehicle.BOLOs ?? Array.Empty<VehicleBOLO>());
                    list.Add(cdfBolo);
                    inWorldVehicle.BOLOs = list.ToArray();
                    merged = true;
                } catch (Exception ex) {
                    Helper.Log($"MergeBOLOsFromStubByPlate skip BOLO: {ex.Message}", false, Helper.LogSeverity.Warning);
                }
            }
            if (!merged) return;
            lock (_vehicleDbLock) {
                keepInVehicleDatabase.RemoveAll(v => v == stub);
                if (!keepInVehicleDatabase.Any(x => x != null && string.Equals(x.LicensePlate, plate, StringComparison.OrdinalIgnoreCase)))
                    keepInVehicleDatabase.Add(inWorldVehicle);
            }
            try {
                Database.SaveVehicle(inWorldVehicle);
            } catch (Exception ex) {
                Helper.Log($"MergeBOLOsFromStubByPlate SaveVehicle: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        internal static void SyncVehicleDatabaseWithCDF() {
            foreach (MDTProVehicleData databaseVehicle in VehicleDatabase) {
                if (databaseVehicle == null) continue;
                SyncSingleVehicleToCDF(databaseVehicle);
            }
        }

        /// <summary>Push MDT vehicle data to CDF VehicleData so PR and other mods see IsStolen, Registration, Insurance, and BOLOs.</summary>
        private static void SyncSingleVehicleToCDF(MDTProVehicleData databaseVehicle) {
            if (databaseVehicle?.CDFVehicleData == null) return;
            try {
                databaseVehicle.CDFVehicleData.IsStolen = databaseVehicle.IsStolen;
                if (databaseVehicle.CDFVehicleData.Registration != null) {
                    if (!string.IsNullOrEmpty(databaseVehicle.RegistrationStatus) && Enum.TryParse(databaseVehicle.RegistrationStatus, true, out EDocumentStatus rs)) {
                        try { databaseVehicle.CDFVehicleData.Registration.Status = rs; } catch { }
                    }
                    if (!string.IsNullOrEmpty(databaseVehicle.RegistrationExpiration) && DateTime.TryParse(databaseVehicle.RegistrationExpiration, out DateTime regExp)) {
                        try { databaseVehicle.CDFVehicleData.Registration.ExpirationDate = regExp; } catch { }
                    }
                }
                if (databaseVehicle.CDFVehicleData.Insurance != null) {
                    if (!string.IsNullOrEmpty(databaseVehicle.InsuranceStatus) && Enum.TryParse(databaseVehicle.InsuranceStatus, true, out EDocumentStatus ins)) {
                        try { databaseVehicle.CDFVehicleData.Insurance.Status = ins; } catch { }
                    }
                    if (!string.IsNullOrEmpty(databaseVehicle.InsuranceExpiration) && DateTime.TryParse(databaseVehicle.InsuranceExpiration, out DateTime insExp)) {
                        try { databaseVehicle.CDFVehicleData.Insurance.ExpirationDate = insExp; } catch { }
                    }
                }
                // Sync persisted BOLOs to CDF so PR and other mods see them on re-encountered vehicles
                if (databaseVehicle.BOLOs != null && databaseVehicle.BOLOs.Length > 0) {
                    var existingBolos = databaseVehicle.CDFVehicleData.GetAllBOLOs() ?? Array.Empty<VehicleBOLO>();
                    foreach (var b in databaseVehicle.BOLOs) {
                        if (b == null || !b.IsActive) continue;
                        bool alreadyInCdf = existingBolos.Any(eb => eb != null && string.Equals(eb.Reason, b.Reason, StringComparison.OrdinalIgnoreCase));
                        if (!alreadyInCdf) {
                            DateTime issued, expires;
                            string issuedBy;
                            try {
                                issued = (DateTime)(b.GetType().GetProperty("Issued")?.GetValue(b) ?? DateTime.UtcNow);
                                expires = (DateTime)(b.GetType().GetProperty("Expires")?.GetValue(b) ?? DateTime.UtcNow.AddDays(7));
                                issuedBy = (string)(b.GetType().GetProperty("IssuedBy")?.GetValue(b)) ?? "LSPD";
                            } catch {
                                issued = DateTime.UtcNow;
                                expires = DateTime.UtcNow.AddDays(7);
                                issuedBy = "LSPD";
                            }
                            var cdfBolo = new VehicleBOLO(b.Reason ?? "Unknown", issued, expires, issuedBy);
                            databaseVehicle.CDFVehicleData.AddBOLO(cdfBolo);
                        }
                    }
                }
            } catch (Exception ex) {
                Helper.Log($"SyncSingleVehicleToCDF skip: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        public static void KeepPedInDatabase(MDTProPedData pedData) {
            lock (_pedDbLock) {
                if (!keepInPedDatabase.Any(x => x.Name == pedData.Name)) keepInPedDatabase.Add(pedData);
            }
            Database.SavePed(pedData);
        }

        /// <summary>Get ped data by ped reference. Tries name lookup first, then Holder-based fallback (for re-encounters).</summary>
        internal static MDTProPedData GetPedDataForPed(Ped ped) {
            if (ped == null || !ped.IsValid()) return null;
            string pedName = null;
            try { pedName = ped.GetPedData()?.FullName; } catch { }
            if (string.IsNullOrEmpty(pedName)) {
                try {
                    var persona = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(ped);
                    if (persona != null && !string.IsNullOrEmpty(persona.FullName)) pedName = persona.FullName;
                } catch { }
            }
            lock (_pedDbLock) {
                if (!string.IsNullOrEmpty(pedName)) {
                    MDTProPedData byName = pedDatabase.FirstOrDefault(x => x.Name?.Equals(pedName, StringComparison.OrdinalIgnoreCase) == true);
                    if (byName != null) return byName;
                    byName = keepInPedDatabase.FirstOrDefault(x => x.Name?.Equals(pedName, StringComparison.OrdinalIgnoreCase) == true);
                    if (byName != null) return byName;
                }
                var byHolder = pedDatabase.FirstOrDefault(x => x.Holder != null && x.Holder.IsValid() && x.Holder.Handle == ped.Handle);
                if (byHolder != null) return byHolder;
            }
            return null;
        }

        /// <summary>Get ped data by name (case-insensitive). Checks pedDatabase first, then keepInPedDatabase. Used when handing citations so the offender is found even if not currently "nearby".</summary>
        internal static MDTProPedData GetPedDataByName(string pedName) {
            if (string.IsNullOrWhiteSpace(pedName)) return null;
            lock (_pedDbLock) {
                MDTProPedData byName = pedDatabase.FirstOrDefault(x => x.Name != null && x.Name.Equals(pedName, StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName;
                byName = keepInPedDatabase.FirstOrDefault(x => x.Name != null && x.Name.Equals(pedName, StringComparison.OrdinalIgnoreCase));
                return byName;
            }
        }

        /// <summary>Marks a ped as deceased. Updates in-memory ped and persists to DB. Idempotent: skips if already marked.</summary>
        internal static void MarkPedDeceased(string pedName, string attackerName = null, string weaponInfo = null) {
            if (string.IsNullOrWhiteSpace(pedName)) return;
            var existing = GetPedDataByName(pedName);
            if (existing != null && existing.IsDeceased) return;
            string deceasedAt = DateTime.UtcNow.ToString("o");
            lock (_pedDbLock) {
                MDTProPedData ped = pedDatabase.FirstOrDefault(x => x.Name != null && x.Name.Equals(pedName, StringComparison.OrdinalIgnoreCase))
                    ?? keepInPedDatabase.FirstOrDefault(x => x.Name != null && x.Name.Equals(pedName, StringComparison.OrdinalIgnoreCase));
                if (ped != null) {
                    ped.IsDeceased = true;
                    ped.DeceasedAt = deceasedAt;
                }
            }
            Database.MarkPedDeceased(pedName, deceasedAt);
            Helper.Log($"DEATH LOG: {pedName} (deceased). Attacker: {(string.IsNullOrEmpty(attackerName) ? "unknown" : attackerName)}. Weapon: {(string.IsNullOrEmpty(weaponInfo) ? "—" : weaponInfo)}", false, Helper.LogSeverity.Warning);
        }

        /// <summary>Refreshes our stored ped's wanted status from CDF so PR dispatch results (warrants) show in MDT Person Search. Call when PR runs a ped through dispatch.</summary>
        internal static void RefreshPedWantedStatusFromCDF(Ped ped) {
            if (ped == null || !ped.IsValid()) return;
            try {
                PedData cdf = ped.GetPedData();
                if (cdf == null || string.IsNullOrWhiteSpace(cdf.FullName)) return;
                string name = cdf.FullName.Trim();
                MDTProPedData ourPed = GetPedDataByName(name);
                if (ourPed == null) return;
                ourPed.IsWanted = cdf.Wanted;
                ourPed.WarrantText = cdf.Wanted ? CitationArrestHelper.GetRandomWarrantCharge().name : null;
                KeepPedInDatabase(ourPed);
                Database.SavePed(ourPed);
            } catch (Exception ex) {
                Helper.Log($"RefreshPedWantedStatusFromCDF: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        internal static void AddIdentificationEvent(Ped ped, string eventType) {
            if (ped == null || !ped.IsValid()) return;
            string pedName = null;
            try { pedName = ped.GetPedData()?.FullName; } catch { }
            if (string.IsNullOrEmpty(pedName)) {
                try {
                    var persona = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(ped);
                    if (persona != null && !string.IsNullOrEmpty(persona.FullName)) pedName = persona.FullName;
                } catch { }
            }
            if (string.IsNullOrEmpty(pedName)) return;

            StoreIdentifiedPedHandle(pedName, ped.Handle);

            MDTProPedData pedData;
            lock (_pedDbLock) {
                // Prefer exact ped match (Holder) first — we're identifying THIS person, so use their record
                pedData = pedDatabase.FirstOrDefault(x => x.Holder != null && x.Holder.IsValid() && x.Holder.Handle == ped.Handle)
                    ?? pedDatabase.FirstOrDefault(x => x.Name?.Equals(pedName, StringComparison.OrdinalIgnoreCase) == true)
                    ?? keepInPedDatabase.FirstOrDefault(x => x.Name?.Equals(pedName, StringComparison.OrdinalIgnoreCase) == true);
                if (pedData == null) {
                    pedData = new MDTProPedData { Name = pedName };
                    pedData.ModelHash = (uint)ped.Model.Hash;
                    pedData.ModelName = ped.Model.Name;
                    pedData.TryParseNameIntoFirstLast();
                    pedData.IdentificationHistory = new List<MDTProPedData.IdentificationEntry>();
                    if (!pedDatabase.Any(x => x.Name == pedName)) pedDatabase.Add(pedData);
                } else {
                    if (!pedDatabase.Any(x => x.Name == pedData.Name)) pedDatabase.Add(pedData);
                    // Always update model from the ped we just identified — ensures ID photo matches the person in front of you
                    pedData.ModelHash = (uint)ped.Model.Hash;
                    pedData.ModelName = ped.Model.Name;
                }
                if (pedData.IdentificationHistory == null) pedData.IdentificationHistory = new List<MDTProPedData.IdentificationEntry>();
                pedData.IdentificationHistory.Insert(0, new MDTProPedData.IdentificationEntry {
                    Type = eventType,
                    Timestamp = System.DateTime.UtcNow.ToString("o")
                });
                if (pedData.IdentificationHistory.Count > 10) pedData.IdentificationHistory.RemoveAt(pedData.IdentificationHistory.Count - 1);
            }
            KeepPedInDatabase(pedData);
            Database.SavePed(pedData);
            SetContextPed(pedData);
        }

        internal static void LoadPedDatabaseFromFile() {
            List<MDTProPedData> fileContent = SetupController.GetMDTProPedData();
            lock (_pedDbLock) {
                pedDatabase.Clear();
                keepInPedDatabase.Clear();
                foreach (MDTProPedData data in fileContent) {
                    if (data == null || data.Name == null) continue;
                    if (!keepInPedDatabase.Any(x => x.Name == data.Name)) keepInPedDatabase.Add(data);
                    if (!pedDatabase.Any(x => x.Name == data.Name)) pedDatabase.Add(data);
                }
            }
        }

        internal static List<MDTProPedData> GetPedDataToSave() {
            return keepInPedDatabase;
        }

        /// <summary>Lock order: always acquire _pedDbLock before _vehicleDbLock to avoid deadlock.</summary>
        public static void KeepVehicleInDatabase(MDTProVehicleData vehicleData) {
            MDTProPedData pedData = null;
            lock (_pedDbLock) {
                pedData = pedDatabase.FirstOrDefault(x => x.Name == vehicleData.Owner);
                if (pedData != null) {
                    pedData.Name = vehicleData.Owner;
                    if (!keepInPedDatabase.Any(x => x.Name == pedData.Name)) keepInPedDatabase.Add(pedData);
                }
            }
            if (pedData != null) Database.SavePed(pedData);

            lock (_vehicleDbLock) {
                if (!keepInVehicleDatabase.Any(x => x.LicensePlate == vehicleData.LicensePlate)) keepInVehicleDatabase.Add(vehicleData);
            }
            Database.SaveVehicle(vehicleData);
        }

        internal static void LoadVehicleDatabaseFromFile() {
            lock (_vehicleDbLock) {
                vehicleDatabase.Clear();
                keepInVehicleDatabase.Clear();
                List<MDTProVehicleData> fileContent = SetupController.GetMDTProVehicleData();
                foreach (MDTProVehicleData data in fileContent) {
                    if (data == null || data.LicensePlate == null) continue;
                    if (!keepInVehicleDatabase.Any(x => x.LicensePlate == data.LicensePlate)) keepInVehicleDatabase.Add(data);
                    if (!vehicleDatabase.Any(x => x.LicensePlate == data.LicensePlate)) vehicleDatabase.Add(data);
                }
            }
        }

        internal static List<MDTProVehicleData> GetVehicleDataToSave() {
            return keepInVehicleDatabase;
        }

        internal static bool UpdatePedData(MDTProPedData pedData) {
            if (pedData == null) return false;
            lock (_pedDbLock) {
                int index = pedDatabase.FindIndex(x => x != null && string.Equals(x.Name, pedData.Name, StringComparison.OrdinalIgnoreCase));
                if (index == -1) {
                    Helper.Log("Failed to update Ped database - ped not in world.", false, Helper.LogSeverity.Warning);
                    return false;
                }
                pedDatabase[index] = pedData;
            }
            return true;
        }

        internal static void AddCDFPedDataPedToDatabase(PedData pedData) {
            if (pedData == null) return;
            try {
                if (pedData.Holder != null && pedData.Holder.IsValid()) {
                    ResolvePedForReEncounter(pedData.Holder);
                    return;
                }

                MDTProPedData mdtProPedData = new MDTProPedData(pedData);
                if (mdtProPedData == null || mdtProPedData.Name == null) return;
                lock (_pedDbLock) {
                    if (pedDatabase.Any(x => x.Name == mdtProPedData.Name)) return;
                }
                TryApplyReEncounterProfile(mdtProPedData);
                lock (_pedDbLock) {
                    if (pedDatabase.Any(x => x.Name == mdtProPedData.Name)) return;
                    pedDatabase.Add(mdtProPedData);
                }
            } catch (Exception ex) {
                Helper.Log($"AddCDFPedDataPedToDatabase failed: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>Add a name-only stub so a callout-mentioned suspect appears in Person Search. Used when callouts do not register the suspect with CDF.</summary>
        internal static void AddCalloutSuspectNameToDatabase(string fullName) {
            if (string.IsNullOrWhiteSpace(fullName)) return;
            fullName = fullName.Trim();
            if (fullName.Length < 3) return;
            MDTProPedData stub = null;
            lock (_pedDbLock) {
                if (pedDatabase.Any(x => x.Name != null && x.Name.Equals(fullName, StringComparison.OrdinalIgnoreCase))) return;
                if (keepInPedDatabase.Any(x => x.Name != null && x.Name.Equals(fullName, StringComparison.OrdinalIgnoreCase))) return;
                stub = new MDTProPedData { Name = fullName };
                stub.TryParseNameIntoFirstLast();
                stub.Citations = new List<CitationGroup.Charge>();
                stub.Arrests = new List<ArrestGroup.Charge>();
                stub.IdentificationHistory = new List<MDTProPedData.IdentificationEntry>();
                pedDatabase.Add(stub);
                keepInPedDatabase.Add(stub);
            }
            KeepPedInDatabase(stub);
            Database.SavePed(stub);
        }

        internal static bool UpdateVehicleData(MDTProVehicleData vehicleData) {
            if (vehicleData == null) return false;
            lock (_vehicleDbLock) {
                int index = vehicleDatabase.FindIndex(x => x != null && string.Equals(x.LicensePlate, vehicleData.LicensePlate, StringComparison.OrdinalIgnoreCase));
                if (index == -1) {
                    Helper.Log("Failed to update Vehicle database - vehicle not in world.", false, Helper.LogSeverity.Warning);
                    return false;
                }
                vehicleDatabase[index] = vehicleData;
            }
            return true;
        }

        internal static void StartCurrentShift() {
            currentShiftData.startTime = SetupController.GetConfig().useInGameTime ? DateTime.ParseExact(World.TimeOfDay.ToString(), "HH:mm:ss", CultureInfo.InvariantCulture) : DateTime.Now;
        }

        internal static void EndCurrentShift() {
            if (currentShiftData.startTime == null) return;

            currentShiftData.endTime = SetupController.GetConfig().useInGameTime ? DateTime.ParseExact(World.TimeOfDay.ToString(), "HH:mm:ss", CultureInfo.InvariantCulture) : DateTime.Now;
            shiftHistoryData.Add(currentShiftData);
            Database.SaveShift(currentShiftData);
            currentShiftData = new ShiftData();
            ShiftHistoryUpdated?.Invoke();
        }

        internal static event Action ShiftHistoryUpdated;

        internal static void AddReportToCurrentShift(string reportId) {
            if (currentShiftData.startTime == null || currentShiftData.reports.Contains(reportId)) return;
            currentShiftData.reports.Add(reportId);
        }

        internal static void AddReport(Report report) {
            if (report is CitationReport citationReport) {
                if (!string.IsNullOrEmpty(citationReport.OffenderPedName)) {
                    int pedIndex = pedDatabase.FindIndex(pedData => pedData.Name?.ToLower() == citationReport.OffenderPedName.ToLower());
                    if (pedIndex != -1) {
                        MDTProPedData pedDataToAdd = pedDatabase[pedIndex];

                        pedDataToAdd.Citations.AddRange(citationReport.Charges.Where(x => !x.addedByReportInEdit));

                        KeepPedInDatabase(pedDataToAdd);
                        pedDatabase[pedIndex] = pedDataToAdd;
                    }
                }

                if (!string.IsNullOrEmpty(citationReport.OffenderVehicleLicensePlate)) {
                    MDTProVehicleData vehicleDataToAdd;
                    lock (_vehicleDbLock) {
                        vehicleDataToAdd = vehicleDatabase.FirstOrDefault(vehicleData => vehicleData.LicensePlate?.ToLower() == citationReport.OffenderVehicleLicensePlate.ToLower());
                    }
                    if (vehicleDataToAdd != null) KeepVehicleInDatabase(vehicleDataToAdd);
                }

                // Citations are resolved directly and should not create court cases.
                citationReport.CourtCaseNumber = null;

                int index = citationReports.FindIndex(x => x.Id == citationReport.Id);
                CitationReport existingCitation = index >= 0 ? citationReports[index] as CitationReport : null;
                bool wasAlreadyClosed = existingCitation?.Status == ReportStatus.Closed;
                bool newlyClosed = citationReport.Status == ReportStatus.Closed && citationReport.Charges != null && citationReport.Charges.Count > 0 && !wasAlreadyClosed;

                if (newlyClosed) {
                    // Set FinalAmount once when the citation becomes closed; do not recalculate on later saves.
                    var chargesToHand = citationReport.Charges
                        .Select(charge => new PRHelper.CitationHandoutCharge {
                            Name = charge.name,
                            Fine = Helper.GetRandomInt(charge.minFine, charge.maxFine),
                            IsArrestable = charge.isArrestable,
                        })
                        .ToList();
                    citationReport.FinalAmount = chargesToHand.Sum(c => c.Fine);
                    Database.SaveCitationReport(citationReport);

                    if (Main.usePR) {
                        PRHelper.GiveCitation(citationReport.OffenderPedName, chargesToHand);
                    }
                } else if (citationReport.Status == ReportStatus.Closed) {
                    // Already closed: persist any other edits without recalculating FinalAmount or re-issuing citation.
                    Database.SaveCitationReport(citationReport);
                }

                if (index != -1) {
                    citationReports[index] = citationReport;
                } else {
                    citationReports.Add(citationReport);
                }
            } else if (report is ArrestReport arrestReport) {
                if (!string.IsNullOrEmpty(arrestReport.OffenderPedName)) {
                    MDTProPedData pedDataToAdd = GetPedDataByName(arrestReport.OffenderPedName);
                    if (pedDataToAdd != null) {
                        pedDataToAdd.Arrests.AddRange(arrestReport.Charges.Where(x => !x.addedByReportInEdit));

                        // Arrest satisfies warrant: clear wanted status and sync to CDF so MDT and PR stay consistent
                        pedDataToAdd.IsWanted = false;
                        pedDataToAdd.WarrantText = null;
                        SyncSinglePedToCDF(pedDataToAdd);

                        KeepPedInDatabase(pedDataToAdd);
                    }
                }

                if (!string.IsNullOrEmpty(arrestReport.OffenderVehicleLicensePlate)) {
                    MDTProVehicleData vehicleDataToAdd;
                    lock (_vehicleDbLock) {
                        vehicleDataToAdd = vehicleDatabase.FirstOrDefault(vehicleData => vehicleData.LicensePlate?.ToLower() == arrestReport.OffenderVehicleLicensePlate.ToLower());
                    }
                    if (vehicleDataToAdd != null) KeepVehicleInDatabase(vehicleDataToAdd);
                }

                // Only create/update court case when arrest is Closed. Pending = collecting evidence (attach reports).
                if (arrestReport.Status == ReportStatus.Closed) {
                    string courtCaseNumber = arrestReport.CourtCaseNumber ?? Helper.GetCourtCaseNumber();
                    arrestReport.CourtCaseNumber = courtCaseNumber;

                    CourtData courtData = new CourtData(
                        arrestReport.OffenderPedName,
                        courtCaseNumber,
                        arrestReport.Id,
                        int.Parse(DateTime.Now.ToString("yy"))
                    );

                    if (arrestReport.AttachedReportIds != null && arrestReport.AttachedReportIds.Count > 0) {
                        courtData.AttachedReportIds.AddRange(arrestReport.AttachedReportIds);
                    }

                    foreach (ArrestReport.Charge charge in arrestReport.Charges) {
                        int? time;
                        if (charge.maxDays == null) {
                            if (Helper.GetRandomInt(0, 1) == 0) {
                                time = Helper.GetRandomInt(charge.minDays, charge.minDays * 2);
                            } else {
                                time = null;
                            }
                        } else {
                            time = Helper.GetRandomInt(charge.minDays, (int)charge.maxDays);
                        }
                        courtData.AddCharge(
                            new CourtData.Charge(
                                charge.name,
                                Helper.GetRandomInt(charge.minFine, charge.maxFine),
                                time,
                                charge.isArrestable
                            )
                        );
                    }

                    courtData.EvidenceUseOfForce = arrestReport.UseOfForce != null && !string.IsNullOrEmpty(arrestReport.UseOfForce.Type);
                    BuildCourtCaseMetadata(courtData, arrestReport.OffenderPedName, arrestReport.Location);
                    ApplyRepeatOffenderSentencing(courtData);

                    if (!string.IsNullOrEmpty(arrestReport.OffenderPedName)) {
                        int pedIndex = pedDatabase.FindIndex(pedData => pedData.Name?.ToLower() == arrestReport.OffenderPedName.ToLower());
                        if (pedIndex != -1) {
                            MDTProPedData pedDataToUpdate = pedDatabase[pedIndex];
                            UpdatePedIncarcerationFromCourtData(pedDataToUpdate, courtData);
                            KeepPedInDatabase(pedDataToUpdate);
                            pedDatabase[pedIndex] = pedDataToUpdate;
                        }
                    }

                    if (!courtDatabase.Any(x => x.Number == courtCaseNumber)) {
                        if (courtDatabase.Count > SetupController.GetConfig().courtDatabaseMaxEntries) {
                            Database.DeleteCourtCase(courtDatabase[0].Number);
                            courtDatabase.RemoveAt(0);
                        }
                        courtDatabase.Add(courtData);
                    }
                }

                int index = arrestReports.FindIndex(x => x.Id == arrestReport.Id);
                if (index != -1) {
                    arrestReports[index] = arrestReport;
                } else {
                    arrestReports.Add(arrestReport);
                }
            } else if (report is IncidentReport incidentReport) {
                foreach (string offenderPedName in incidentReport.OffenderPedsNames) {
                    if (!string.IsNullOrEmpty(offenderPedName)) {
                        MDTProPedData pedDataToAdd = pedDatabase.FirstOrDefault(pedData => pedData.Name?.ToLower() == offenderPedName.ToLower());
                        if (pedDataToAdd != null) KeepPedInDatabase(pedDataToAdd);
                    }
                }

                foreach (string witnessPedName in incidentReport.WitnessPedsNames) {
                    if (!string.IsNullOrEmpty(witnessPedName)) {
                        MDTProPedData pedDataToAdd = pedDatabase.FirstOrDefault(pedData => pedData.Name?.ToLower() == witnessPedName.ToLower());
                        if (pedDataToAdd != null) KeepPedInDatabase(pedDataToAdd);
                    }
                }

                int index = incidentReports.FindIndex(x => x.Id == incidentReport.Id);
                if (index != -1) {
                    incidentReports[index] = incidentReport;
                } else {
                    incidentReports.Add(incidentReport);
                }
            } else if (report is ImpoundReport impoundReport) {
                int index = impoundReports.FindIndex(x => x.Id == impoundReport.Id);
                if (index != -1) impoundReports[index] = impoundReport;
                else impoundReports.Add(impoundReport);
            } else if (report is TrafficIncidentReport trafficReport) {
                int index = trafficIncidentReports.FindIndex(x => x.Id == trafficReport.Id);
                if (index != -1) trafficIncidentReports[index] = trafficReport;
                else trafficIncidentReports.Add(trafficReport);
            } else if (report is InjuryReport injuryReport) {
                int index = injuryReports.FindIndex(x => x.Id == injuryReport.Id);
                if (index != -1) injuryReports[index] = injuryReport;
                else injuryReports.Add(injuryReport);
            } else if (report is PropertyEvidenceReceiptReport perReport) {
                int index = propertyEvidenceReports.FindIndex(x => x.Id == perReport.Id);
                if (index != -1) propertyEvidenceReports[index] = perReport;
                else propertyEvidenceReports.Add(perReport);
            }
            lock (_reportRealCreatedAt) {
                _reportRealCreatedAt[report.Id] = DateTime.UtcNow;
            }
            AddReportToCurrentShift(report.Id);
        }

        private static void TryApplyReEncounterProfile(MDTProPedData currentPedData) {
            MDTProPedData persistentMatch = GetReEncounterCandidate(currentPedData);
            if (persistentMatch == null) return;

            string originalName = currentPedData.Name;
            currentPedData.ApplyPersistentIdentity(persistentMatch);
            currentPedData.TimesStopped = Math.Max(currentPedData.TimesStopped, persistentMatch.TimesStopped + 1);

            if (currentPedData.CDFPedData != null) {
                currentPedData.CDFPedData.Wanted = currentPedData.IsWanted;
                currentPedData.CDFPedData.IsOnProbation = currentPedData.IsOnProbation;
                currentPedData.CDFPedData.IsOnParole = currentPedData.IsOnParole;
                currentPedData.CDFPedData.Citations = currentPedData.Citations?.Count ?? 0;
                currentPedData.CDFPedData.TimesStopped = currentPedData.TimesStopped;
                currentPedData.TrySyncCDFPersonaToPersistentIdentity();
                SyncSinglePedToCDF(currentPedData);
            }

            KeepPedInDatabase(currentPedData);
            Helper.Log($"Re-encounter matched by model: {originalName} => {currentPedData.Name}", false, Helper.LogSeverity.Info);
        }

        internal static void ResolvePedForReEncounter(Ped ped) {
            if (ped == null || !ped.IsValid()) return;
            if (NativeFunction.Natives.IS_PED_FLEEING<bool>(ped)) MarkPedFleeing(ped);
            lock (_resolvedPedHandlesLock) {
                PruneResolvedPedHandlesCore();
                if (resolvedPedHandles.Contains(ped.Handle)) return;
                resolvedPedHandles.Add(ped.Handle);
            }

            MDTProPedData mdtProPedData = new MDTProPedData(ped);
            if (mdtProPedData == null || string.IsNullOrEmpty(mdtProPedData.Name)) return;

            StoreIdentifiedPedHandle(mdtProPedData.Name, ped.Handle);

            mdtProPedData.TryParseNameIntoFirstLast();

            MDTProPedData existingPed;
            lock (_pedDbLock) {
                existingPed = pedDatabase.FirstOrDefault(x => x.Name == mdtProPedData.Name);
                if (existingPed != null) {
                    // Always refresh wanted status from CDF so PR dispatch results (warrants) show in MDT search
                    existingPed.IsWanted = mdtProPedData.IsWanted;
                    existingPed.WarrantText = mdtProPedData.WarrantText;
                    if (existingPed.CDFPedData == null) {
                        existingPed.LicenseStatus = mdtProPedData.LicenseStatus;
                        existingPed.LicenseExpiration = mdtProPedData.LicenseExpiration;
                        existingPed.WeaponPermitStatus = mdtProPedData.WeaponPermitStatus;
                        existingPed.WeaponPermitExpiration = mdtProPedData.WeaponPermitExpiration;
                        existingPed.WeaponPermitType = mdtProPedData.WeaponPermitType;
                        existingPed.FishingPermitStatus = mdtProPedData.FishingPermitStatus;
                        existingPed.FishingPermitExpiration = mdtProPedData.FishingPermitExpiration;
                        existingPed.HuntingPermitStatus = mdtProPedData.HuntingPermitStatus;
                        existingPed.HuntingPermitExpiration = mdtProPedData.HuntingPermitExpiration;
                        // Fill identity when stub/callout record has no CDF data (e.g. callout suspects)
                        if (!string.IsNullOrEmpty(mdtProPedData.FirstName)) existingPed.FirstName = existingPed.FirstName ?? mdtProPedData.FirstName;
                        if (!string.IsNullOrEmpty(mdtProPedData.LastName)) existingPed.LastName = existingPed.LastName ?? mdtProPedData.LastName;
                        if (!string.IsNullOrEmpty(mdtProPedData.Birthday)) existingPed.Birthday = existingPed.Birthday ?? mdtProPedData.Birthday;
                        if (!string.IsNullOrEmpty(mdtProPedData.Gender)) existingPed.Gender = existingPed.Gender ?? mdtProPedData.Gender;
                        if (!string.IsNullOrEmpty(mdtProPedData.Address)) existingPed.Address = existingPed.Address ?? mdtProPedData.Address;
                        // Always update model from current encounter so ID photo matches the person in front of you
                        if (mdtProPedData.ModelHash != 0) existingPed.ModelHash = mdtProPedData.ModelHash;
                        if (!string.IsNullOrEmpty(mdtProPedData.ModelName)) existingPed.ModelName = mdtProPedData.ModelName;
                        existingPed.TryParseNameIntoFirstLast();
                    }
                }
            }
            if (existingPed != null) {
                KeepPedInDatabase(existingPed);
                Database.SavePed(existingPed);
                SetContextPed(existingPed);
                return;
            }

            TryApplyReEncounterProfile(mdtProPedData);
            lock (_pedDbLock) {
                if (pedDatabase.Any(x => x.Name == mdtProPedData.Name)) return;
                pedDatabase.Add(mdtProPedData);
            }
            if (!MDTProPedData.IsMinimalIdentity(mdtProPedData))
                Database.SavePed(mdtProPedData);
            SetContextPed(mdtProPedData);

            // Delayed CDF retry: if CDF was null or minimal, PR may populate shortly; re-read after 2s and merge identity
            if (mdtProPedData.CDFPedData == null || MDTProPedData.IsMinimalIdentity(mdtProPedData)) {
                uint pedHandle = ped.Handle;
                if (pedHandle == 0) return; // invalid handle - skip retry
                string pedName = mdtProPedData.Name;
                GameFiber.StartNew(() => {
                    GameFiber.Wait(2000);
                    try {
                        Ped p = null;
                        try { p = World.GetEntityByHandle<Ped>(pedHandle); } catch { return; } // ped despawned - expected, no log
                        if (p == null || !p.IsValid()) return;
                        MDTProPedData updated = new MDTProPedData(p);
                        if (updated.CDFPedData == null || MDTProPedData.IsMinimalIdentity(updated)) return;
                        MDTProPedData existing = null;
                        lock (_pedDbLock) {
                            existing = pedDatabase.FirstOrDefault(x => x != null && string.Equals(x.Name, pedName, StringComparison.OrdinalIgnoreCase));
                            if (existing == null) return;
                            if (!string.IsNullOrEmpty(updated.Birthday)) existing.Birthday = existing.Birthday ?? updated.Birthday;
                            if (!string.IsNullOrEmpty(updated.Gender)) existing.Gender = existing.Gender ?? updated.Gender;
                            if (!string.IsNullOrEmpty(updated.Address)) existing.Address = existing.Address ?? updated.Address;
                            if (!string.IsNullOrEmpty(updated.LicenseStatus)) existing.LicenseStatus = existing.LicenseStatus ?? updated.LicenseStatus;
                            if (!string.IsNullOrEmpty(updated.LicenseExpiration)) existing.LicenseExpiration = existing.LicenseExpiration ?? updated.LicenseExpiration;
                            if (!string.IsNullOrEmpty(updated.WeaponPermitStatus)) existing.WeaponPermitStatus = existing.WeaponPermitStatus ?? updated.WeaponPermitStatus;
                            if (!string.IsNullOrEmpty(updated.WeaponPermitExpiration)) existing.WeaponPermitExpiration = existing.WeaponPermitExpiration ?? updated.WeaponPermitExpiration;
                            if (!string.IsNullOrEmpty(updated.WeaponPermitType)) existing.WeaponPermitType = existing.WeaponPermitType ?? updated.WeaponPermitType;
                            if (!string.IsNullOrEmpty(updated.FishingPermitStatus)) existing.FishingPermitStatus = existing.FishingPermitStatus ?? updated.FishingPermitStatus;
                            if (!string.IsNullOrEmpty(updated.FishingPermitExpiration)) existing.FishingPermitExpiration = existing.FishingPermitExpiration ?? updated.FishingPermitExpiration;
                            if (!string.IsNullOrEmpty(updated.HuntingPermitStatus)) existing.HuntingPermitStatus = existing.HuntingPermitStatus ?? updated.HuntingPermitStatus;
                            if (!string.IsNullOrEmpty(updated.HuntingPermitExpiration)) existing.HuntingPermitExpiration = existing.HuntingPermitExpiration ?? updated.HuntingPermitExpiration;
                            if (updated.ModelHash != 0) existing.ModelHash = updated.ModelHash;
                            if (!string.IsNullOrEmpty(updated.ModelName)) existing.ModelName = updated.ModelName;
                            existing.TryParseNameIntoFirstLast();
                        }
                        KeepPedInDatabase(existing);
                        Database.SavePed(existing);
                        Helper.Log($"[MDTPro] Delayed CDF retry filled identity for: {pedName}", false, Helper.LogSeverity.Info);
                    } catch (Exception ex) {
                        // Expected when ped despawned before retry - don't spam log
                        bool isStaleHandle = ex.Message?.IndexOf("Invalid handle", StringComparison.OrdinalIgnoreCase) >= 0
                            || ex.Message?.IndexOf("EntityType", StringComparison.OrdinalIgnoreCase) >= 0
                            || ex.Message?.IndexOf("not a handle to an entity", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isStaleHandle)
                            Helper.Log($"[MDTPro] Delayed CDF retry failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                    }
                }, "MDTPro-CDF-retry");
            }
        }

        /// <summary>Caller must hold _resolvedPedHandlesLock.</summary>
        private static void PruneResolvedPedHandlesCore() {
            if (resolvedPedHandles.Count < 3000) return;
            List<PoolHandle> toRemove = new List<PoolHandle>();
            foreach (var h in resolvedPedHandles) {
                try {
                    if (World.GetEntityByHandle<Entity>(h) == null) toRemove.Add(h);
                } catch { toRemove.Add(h); }
            }
            foreach (var h in toRemove) resolvedPedHandles.Remove(h);
            if (resolvedPedHandles.Count > 4000) resolvedPedHandles.Clear();
        }

        private static void PruneResolvedPedHandles() {
            lock (_resolvedPedHandlesLock) {
                PruneResolvedPedHandlesCore();
            }
        }

        internal static void SetContextPed(MDTProPedData pedData) {
            if (pedData == null) return;
            lock (_contextPedLock) {
                _lastContextPedData = pedData;
                _lastContextPedSetAt = DateTime.UtcNow;
            }
        }

        internal static MDTProPedData GetContextPedIfValid() {
            lock (_contextPedLock) {
                if (_lastContextPedData == null) return null;
                if (DateTime.UtcNow - _lastContextPedSetAt > ContextPedTtl) {
                    _lastContextPedData = null;
                    return null;
                }
                return _lastContextPedData;
            }
        }

        /// <summary>Store ped handle when we identify someone. Citation handout uses this when Holder is null.</summary>
        internal static void StoreIdentifiedPedHandle(string pedName, Rage.PoolHandle handle) {
            if (string.IsNullOrWhiteSpace(pedName)) return;
            lock (_recentlyIdentifiedLock) {
                recentlyIdentifiedPedHandles[pedName.Trim()] = (handle, DateTime.UtcNow);
                PruneRecentlyIdentifiedHandles();
            }
        }

        /// <summary>Get a recently identified ped's handle for citation handout when Holder is null.</summary>
        internal static Rage.PoolHandle? GetRecentlyIdentifiedPedHandle(string pedName) {
            if (string.IsNullOrWhiteSpace(pedName)) return null;
            lock (_recentlyIdentifiedLock) {
                if (!recentlyIdentifiedPedHandles.TryGetValue(pedName.Trim(), out var entry))
                    return null;
                if (DateTime.UtcNow - entry.At > RecentlyIdentifiedTtl) {
                    recentlyIdentifiedPedHandles.Remove(pedName.Trim());
                    return null;
                }
                return entry.Handle;
            }
        }

        private static void PruneRecentlyIdentifiedHandles() {
            if (recentlyIdentifiedPedHandles.Count < 200) return;
            var cutoff = DateTime.UtcNow - RecentlyIdentifiedTtl;
            foreach (var k in recentlyIdentifiedPedHandles.Where(x => x.Value.At < cutoff).Select(x => x.Key).ToList())
                recentlyIdentifiedPedHandles.Remove(k);
        }

        private static MDTProPedData GetReEncounterCandidate(MDTProPedData currentPedData) {
            if (currentPedData == null) return null;
            bool hasModel = (currentPedData.ModelHash != 0) || !string.IsNullOrEmpty(currentPedData.ModelName);
            if (!hasModel) return null;

            float chance = SetupController.GetConfig().reEncounterChance;
            if (chance <= 0f) return null;
            if (chance >= 1f) chance = 1f;
            if (random.NextDouble() > chance) return null;

            List<MDTProPedData> candidates;
            lock (_pedDbLock) {
                candidates = keepInPedDatabase
                    .Where(ped => ped != null && !string.IsNullOrEmpty(ped.Name))
                    .Where(IsPedAvailableForEncounter)
                    .Where(ped => !pedDatabase.Any(activePed => activePed.Name == ped.Name))
                    .Where(ped => {
                        if (currentPedData.ModelHash != 0 && ped.ModelHash != 0) {
                            return ped.ModelHash == currentPedData.ModelHash;
                        }
                        if (!string.IsNullOrEmpty(currentPedData.ModelName) && !string.IsNullOrEmpty(ped.ModelName)) {
                            return ped.ModelName == currentPedData.ModelName;
                        }
                        return false;
                    })
                    .ToList();
            }

            if (candidates.Count == 0) return null;
            return candidates[random.Next(candidates.Count)];
        }

        private static bool IsPedAvailableForEncounter(MDTProPedData pedData) {
            if (pedData == null) return false;
            if (string.IsNullOrEmpty(pedData.IncarceratedUntil)) return true;
            if (string.Equals(pedData.IncarceratedUntil, LifeIncarcerationValue, StringComparison.OrdinalIgnoreCase)) return false;

            if (!DateTime.TryParse(
                pedData.IncarceratedUntil,
                null,
                DateTimeStyles.RoundtripKind,
                out DateTime incarceratedUntil)) {
                return true;
            }

            return incarceratedUntil <= DateTime.UtcNow;
        }

        private static void UpdatePedIncarcerationFromCourtData(MDTProPedData pedData, CourtData courtData, Config config = null) {
            if (pedData == null || courtData?.Charges == null) return;

            int totalDays = 0;
            bool hasLifeSentence = false;

            foreach (CourtData.Charge charge in courtData.Charges) {
                if (charge.Outcome != 1) continue; // Only convicted charges (1 = Convicted)
                if (charge.Time == null) {
                    hasLifeSentence = true;
                    continue;
                }
                int days = charge.SentenceDaysServed ?? charge.Time ?? 0;
                if (days > 0) totalDays += days;
            }

            if (hasLifeSentence) {
                pedData.IncarceratedUntil = LifeIncarcerationValue;
                return;
            }

            if (totalDays <= 0) return;

            DateTime baseDate = DateTime.UtcNow;
            if (string.Equals(pedData.IncarceratedUntil, LifeIncarcerationValue, StringComparison.OrdinalIgnoreCase)) return;
            if (DateTime.TryParse(
                pedData.IncarceratedUntil,
                null,
                DateTimeStyles.RoundtripKind,
                out DateTime existingEnd) && existingEnd > baseDate) {
                baseDate = existingEnd;
            }

            double scaledRealDays = totalDays * (RealDaysPerGameYear / GameDaysPerYear);
            config = config ?? SetupController.GetConfig();
            float paroleThreshold = config.courtParoleThresholdRealDays;
            if (paroleThreshold > 0 && scaledRealDays >= paroleThreshold) {
                // Long sentence — release on parole after a fraction of the time served
                double paroleReleaseDays = scaledRealDays * Math.Max(0.1f, Math.Min(1f, config.courtParoleReleaseFraction));
                pedData.IncarceratedUntil = baseDate.AddDays(paroleReleaseDays).ToString("o");
                pedData.IsOnParole = true;
            } else {
                pedData.IncarceratedUntil = baseDate.AddDays(scaledRealDays).ToString("o");
                pedData.IsOnProbation = true;
            }
        }

        /// <summary>Computes license revocations based on California law. Driver's license: canRevokeLicense charges. Firearms: felonies (lifetime), domestic violence/protective order (lifetime), violent misdemeanors (10 years). Fishing: wildlife violations only.</summary>
        private static List<string> ComputeLicenseRevocations(CourtData courtData) {
            var revocations = new List<string>();
            if (courtData?.Charges == null || courtData.Charges.Count == 0) return revocations;

            var arrestOptions = SetupController.GetArrestOptions();
            var chargeLookup = new Dictionary<string, ArrestGroup.Charge>(StringComparer.OrdinalIgnoreCase);
            if (arrestOptions != null) {
                foreach (var group in arrestOptions) {
                    if (group?.charges == null) continue;
                    foreach (var c in group.charges) {
                        if (!string.IsNullOrEmpty(c?.name)) chargeLookup[c.name] = c;
                    }
                }
            }

            bool driversLicenseRevoked = false;
            bool firearmsRevoked = false;
            string firearmsDuration = null; // "Lifetime" or "10 years"
            bool fishingRevoked = false;

            foreach (CourtData.Charge charge in courtData.Charges) {
                if (charge.Outcome != 1) continue; // Only convicted charges
                if (string.IsNullOrEmpty(charge.Name)) continue;
                string name = charge.Name.Trim();

                // Driver's license: use canRevokeLicense from arrest options (CA: DUI, reckless driving, hit-and-run, evading, etc.)
                if (!driversLicenseRevoked && chargeLookup.TryGetValue(name, out var arrestCharge) && arrestCharge.canRevokeLicense) {
                    driversLicenseRevoked = true;
                }

                // Firearms: California PC 29805, 26202 — felonies = lifetime; domestic violence / protective order = lifetime; violent misdemeanors = 10 years
                if (!firearmsRevoked) {
                    bool isFelony = IsFelonyChargeName(name);
                    bool isDomesticViolence = name.IndexOf("Domestic Violence", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("Violation Of Protective Order", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("Corporal Injury", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isViolentMisdemeanor = IsViolentMisdemeanorChargeName(name);

                    if (isFelony || isDomesticViolence) {
                        firearmsRevoked = true;
                        firearmsDuration = "Lifetime";
                    } else if (isViolentMisdemeanor && firearmsDuration != "Lifetime") {
                        firearmsRevoked = true;
                        firearmsDuration = "10 years";
                    }
                }

                // Fishing: only for fish/wildlife code violations (poaching, illegal take, etc.)
                if (!fishingRevoked && (name.IndexOf("Fish", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Wildlife", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Poach", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Game", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Grand Theft", StringComparison.OrdinalIgnoreCase) < 0)) {
                    fishingRevoked = true;
                }
            }

            if (driversLicenseRevoked) revocations.Add("Driver's License Revoked");
            if (firearmsRevoked) revocations.Add(string.IsNullOrEmpty(firearmsDuration) ? "Firearms Permit Revoked" : $"Firearms Permit Revoked ({firearmsDuration})");
            if (fishingRevoked) revocations.Add("Sport Fishing Privileges Revoked");

            return revocations;
        }

        private static bool IsFelonyChargeName(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            // Felony categories: must match every felony in arrestOptions.json (Felony Traffic, Felony Firearms, Robbery/Theft, Drug felony-level, Custody, Manslaughter/Homicide, Sexual/Kidnapping, Arson, and felony-level charges in other groups)
            return n.Contains("felony ") || n.Contains("murder") || n.Contains("manslaughter") || n.Contains("rape")
                || n.Contains("kidnapping") || n.Contains("robbery") || n.Contains("carjacking") || n.Contains("home invasion")
                || n.Contains("first degree burglary") || n.Contains("burglary") || n.Contains("attempted murder") || n.Contains("attempted robbery") || n.Contains("attempted armed")
                || n.Contains("mayhem") || n.Contains("arson") || n.Contains("reckless burning") || n.Contains("unlawful burning") || n.Contains("explosive") || n.Contains("possession of firearm by felon") || n.Contains("possession of firearm in commission of felony")
                || n.Contains("assault weapon") || n.Contains("machine gun") || n.Contains("short-barreled")
                || n.Contains("discharging firearm at inhabited") || n.Contains("trafficking") || n.Contains("manufacturing meth")
                || n.Contains("for sale") || n.Contains("transport of meth") || n.Contains("transport or sale of meth") || n.Contains("sale or transport of cannabis")
                || n.Contains("aggravated kidnapping") || n.Contains("dui causing") || n.Contains("hit and run causing") || n.Contains("street racing causing")
                || n.Contains("felony reckless evading") || n.Contains("reckless evading peace officer") || n.Contains("grand theft auto") || n.Contains("possession of stolen vehicle")
                || n.Contains("assault with deadly weapon") || n.Contains("assault with deadly weapon (firearm)") || n.Contains("altering or removing firearm serial")
                || n.Contains("escape from custody") || n.Contains("escape from jail") || n.Contains("aid in escape")
                || n.Contains("accessory after the fact") || n.Contains("accessory before the fact")
                || n.Contains("failure to register as sex offender")
                || n.Contains("battery causing serious bodily injury") || n.Contains("malicious wounding")
                || (n.Contains("vandalism") && n.Contains("or more") && !n.Contains("under $400"));
        }

        private static bool IsViolentMisdemeanorChargeName(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("brandishing") || n.Contains("simple assault") || n.Contains("aggravated assault")
                || n.Contains("simple battery") || n.Contains("battery on") || n.Contains("battery with deadly")
                || n.Contains("sexual battery") || n.Contains("malicious wounding")
                || n.Contains("assault on peace officer") || n.Contains("assault on firefighter")
                || n.Contains("criminal threats") || n.Contains("negligent discharge") || n.Contains("shooting from vehicle")
                || n.Contains("domestic violence") || n.Contains("corporal injury") || n.Contains("violation of protective order")
                || n.Contains("reckless endangerment") || n.Contains("wanton endangerment") || n.Contains("mutual combat") || n.Contains("battery (mutual")
                || n.Contains("sexual assault") || n.Contains("vehicular assault");
        }

        private static bool IsHomicideChargeName(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("murder") || n.Contains("manslaughter");
        }

        // Charge classification is name-based only: citation and arrest options (citationOptions.json, arrestOptions.json) define charges by name; only the charge name is persisted on reports/court, not the group. So we match substrings of charge.Name against the names in those files.

        /// <summary>True if the charge is vehicle-related (GTA, stolen vehicle, DUI, vehicular, evading, VIN, impound, etc.). Matches arrest/citation charge names from arrestOptions.json and citationOptions.json.</summary>
        private static bool IsVehicleRelatedChargeName(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("grand theft auto") || n.Contains("stolen vehicle") || n.Contains("possession of stolen vehicle")
                || n.Contains("dui") || n.Contains("dwi") || n.Contains("driving under the influence") || n.Contains("under the influence")
                || n.Contains("chemical test") || n.Contains("field sobriety") || n.Contains("refusal to submit")
                || n.Contains("vehicular") || n.Contains("evading") || n.Contains("vin ") || n.Contains("vin tampering") || n.Contains("defaced vin")
                || n.Contains("hit and run") || n.Contains("hit-and-run") || n.Contains("reckless driving") || n.Contains("street racing")
                || n.Contains("carjacking") || n.Contains("driving on suspended") || n.Contains("driving without license") || n.Contains("driving without valid license") || n.Contains("driving with license expired")
                || n.Contains("failure to present") || n.Contains("refusing to provide identification")
                || n.Contains("refusal to sign traffic") || n.Contains("wrong side of road") || n.Contains("driving on wrong side")
                || (n.Contains("vehicle") && (n.Contains("theft") || n.Contains("stolen") || n.Contains("evidence")));
        }

        /// <summary>True if the charge is firearm/weapon-related. Matches arrest charge names from arrestOptions.json (Misdemeanor/Felony Firearms / Weapons groups). Used so the system can treat firearm charges consistently (e.g. evidence relevance, future use).</summary>
        private static bool IsFirearmChargeName(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("firearm") || n.Contains("deadly weapon") || n.Contains("assault weapon") || n.Contains("machine gun")
                || n.Contains("short-barreled") || n.Contains("short barreled") || n.Contains("brandishing")
                || n.Contains("negligent discharge") || n.Contains("concealed") && n.Contains("permit")
                || n.Contains("possession of ammunition") || n.Contains("altered") && (n.Contains("serial") || n.Contains("serial number"))
                || n.Contains("removing firearm serial") || n.Contains("discharging firearm") || n.Contains("shooting from vehicle")
                || n.Contains("stolen firearm") || n.Contains("explosive device") || n.Contains("detonate explosive");
        }

        /// <summary>True if the case has at least one vehicle-related charge. Impound reports only count as evidence for such cases.</summary>
        private static bool IsCaseVehicleRelated(CourtData courtData) {
            if (courtData?.Charges == null) return false;
            return courtData.Charges.Any(c => c != null && IsVehicleRelatedChargeName(c.Name ?? ""));
        }

        /// <summary>True if the case has at least one firearm/weapon-related charge. Available for evidence relevance or UI (e.g. when to treat DocumentedFirearms as directly relevant).</summary>
        private static bool IsCaseFirearmRelated(CourtData courtData) {
            if (courtData?.Charges == null) return false;
            return courtData.Charges.Any(c => c != null && IsFirearmChargeName(c.Name ?? ""));
        }

        /// <summary>True if the charge is drug/narcotics-related (possession, sale, trafficking, paraphernalia, etc.). Matches arrest/citation charge names. Used for evidence relevance and consistency (e.g. when drug evidence is directly relevant).</summary>
        private static bool IsDrugRelatedChargeName(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("controlled substance") || n.Contains("drug paraphernalia") || n.Contains("under influence of controlled")
                || n.Contains("trafficking") || n.Contains("for sale") || n.Contains("sale or transport") || n.Contains("transport or sale") || n.Contains("transport of meth")
                || n.Contains("manufacturing meth") || n.Contains("possession of cannabis") || n.Contains("possession of cocaine")
                || n.Contains("possession of methamphetamine") || n.Contains("possession of heroin") || n.Contains("possession of pcp")
                || n.Contains("possession of lsd") || n.Contains("hallucinogen") || n.Contains("possession of ecstasy") || n.Contains("mdma")
                || n.Contains("possession of fentanyl") || n.Contains("prescription") && n.Contains("narcotic");
        }

        /// <summary>True if the case has at least one drug-related charge. Used for evidence relevance (e.g. DocumentedDrugs / drug records directly relevant).</summary>
        private static bool IsCaseDrugRelated(CourtData courtData) {
            if (courtData?.Charges == null) return false;
            return courtData.Charges.Any(c => c != null && IsDrugRelatedChargeName(c.Name ?? ""));
        }

        /// <summary>True if the charge indicates evading, fleeing, or pursuit. Used to infer EvidenceWasFleeing when in-game capture misses (chase-then-stop clears fleeing state).</summary>
        private static bool IsEvadingOrFleeingChargeName(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("evading") || n.Contains("evad ") || n.Contains("pursuit") || n.Contains("flee");
        }

        /// <summary>True if this attached report is relevant to the case (same defendant, or report type matches charge type). Only relevant reports get evidence bonus.</summary>
        private static bool IsAttachedReportRelevantToCase(CourtData courtData, string reportId, string reportType) {
            if (courtData == null || string.IsNullOrWhiteSpace(reportId) || string.IsNullOrWhiteSpace(reportType)) return false;
            string defendant = (courtData.PedName ?? "").Trim();
            if (reportType == "incident") {
                IncidentReport r = IncidentReports?.FirstOrDefault(x => x.Id == reportId);
                if (r?.OffenderPedsNames == null || r.OffenderPedsNames.Length == 0) return false;
                return r.OffenderPedsNames.Any(n => string.Equals(n?.Trim(), defendant, StringComparison.OrdinalIgnoreCase));
            }
            if (reportType == "citation") {
                CitationReport r = CitationReports?.FirstOrDefault(x => x.Id == reportId);
                if (r == null) return false;
                return string.Equals((r.OffenderPedName ?? "").Trim(), defendant, StringComparison.OrdinalIgnoreCase);
            }
            if (reportType == "injury") {
                // Injury reports document harm/death; always relevant when attached (e.g. victim death for homicide, assault injuries).
                return true;
            }
            if (reportType == "trafficIncident") {
                TrafficIncidentReport r = TrafficIncidentReports?.FirstOrDefault(x => x.Id == reportId);
                if (r == null) return false;
                bool defendantIsDriver = r.DriverNames != null && r.DriverNames.Any(n => string.Equals(n?.Trim(), defendant, StringComparison.OrdinalIgnoreCase));
                if (defendantIsDriver) return true;
                // If case is vehicle-related (DUI, GTA, etc.) the traffic report might still document the scene even if defendant not listed as driver yet.
                return IsCaseVehicleRelated(courtData);
            }
            if (reportType == "impound") {
                // Impound only relevant for vehicle-related charges (GTA, stolen recovery, evidence, etc.).
                return IsCaseVehicleRelated(courtData);
            }
            if (reportType == "propertyEvidence") {
                // Seizure (Property/Evidence Receipt) is relevant when any subject matches defendant, or when attached to the arrest.
                PropertyEvidenceReceiptReport r = PropertyEvidenceReports?.FirstOrDefault(x => x.Id == reportId);
                if (r == null) return false;
                if (r.SubjectPedNames != null && r.SubjectPedNames.Any(n => string.Equals((n ?? "").Trim(), defendant, StringComparison.OrdinalIgnoreCase))) return true;
                // Attached to the arrest (we only evaluate reports in AttachedReportIds, so attachment implies relevance)
                return true;
            }
            return false;
        }

        /// <summary>True if any attached injury report indicates death/fatal outcome (Severity or Treatment). Used for homicide conviction cap.</summary>
        private static bool HasQualifyingDeathOrFatalInjuryReport(CourtData courtCase) {
            if (courtCase?.AttachedReportIds == null || courtCase.AttachedReportIds.Count == 0 || InjuryReports == null) return false;
            foreach (string reportId in courtCase.AttachedReportIds) {
                InjuryReport ir = InjuryReports.FirstOrDefault(r => r.Id == reportId);
                if (ir == null) continue;
                string severity = (ir.Severity ?? "").Trim().ToLowerInvariant();
                string treatment = (ir.Treatment ?? "").Trim().ToLowerInvariant();
                if (severity.Contains("fatal") || severity.Contains("death") || severity.Contains("critical") || severity.Contains("deceased"))
                    return true;
                if (treatment.Contains("deceased") || treatment.Contains("doa") || treatment.Contains("pronounced dead") || treatment.Contains("pronounced deceased"))
                    return true;
            }
            return false;
        }

        /// <summary>Per-charge conviction chance (0-100). Case chance + tier modifier + variance, then skewed so higher values are rarer. Roll is always 1-100; convicted if roll &lt;= chance.</summary>
        private static int GetPerChargeConvictionChance(CourtData courtCase, CourtData.Charge charge) {
            Config config = SetupController.GetConfig();
            int baseChance = courtCase.ConvictionChance;
            int variance = Helper.GetRandomInt(-4, 4);
            int tierMod = 0;
            string name = charge?.Name ?? "";
            bool isHomicide = IsHomicideChargeName(name);
            bool hasDeathReport = HasQualifyingDeathOrFatalInjuryReport(courtCase);

            if (isHomicide) {
                if (hasDeathReport) tierMod -= 10;  // Homicide with documented death report: still harder but not as much
                else tierMod -= 25;                 // Homicide without death/fatal injury report: much harder
            } else if (IsFelonyChargeName(name)) tierMod -= 10;
            else if (IsViolentMisdemeanorChargeName(name)) tierMod = 0;
            else tierMod = 5;
            int chance = Math.Max(15, Math.Min(85, baseChance + variance + tierMod));

            // Homicide without death report: cap conviction chance so documentation matters
            if (isHomicide && !hasDeathReport && config.courtConvictionHomicideNoDeathReportCap > 0)
                chance = Math.Min(chance, config.courtConvictionHomicideNoDeathReportCap);

            // Skew so higher conviction chances are rarer: more often reduce, rarely boost
            int skewRoll = Helper.GetRandomInt(1, 100);
            if (skewRoll <= 50) chance -= Helper.GetRandomInt(8, 22);      // 50%: substantial drop
            else if (skewRoll <= 80) chance -= Helper.GetRandomInt(0, 10); // 30%: small drop
            else if (skewRoll <= 95) { /* 15%: no change */ }
            else chance += Helper.GetRandomInt(0, 12);                     // 5%: rare boost

            return Math.Max(15, Math.Min(85, chance));
        }

        /// <summary>Applies court-ordered license revocations to ped data. Sets LicenseStatus, WeaponPermitStatus, FishingPermitStatus when applicable. Uses CDF/PR enums: DriversLicenseState (ELicenseState), WeaponPermit/FishingPermit.Status (EDocumentStatus) per https://policing-redefined.netlify.app/docs/developer-docs/cdf/peds/permits</summary>
        private static void ApplyLicenseRevocationsToPed(MDTProPedData pedData, List<string> revocations) {
            if (pedData == null || revocations == null || revocations.Count == 0) return;
            foreach (string r in revocations) {
                if (r?.IndexOf("Driver", StringComparison.OrdinalIgnoreCase) >= 0) {
                    // CDF DriversLicenseState (ELicenseState); CDF docs don't list enum members; "Revoked" parses when ELicenseState includes it
                    pedData.LicenseStatus = "Revoked";
                    break;
                }
            }
            foreach (string r in revocations) {
                if (r?.IndexOf("Firearm", StringComparison.OrdinalIgnoreCase) >= 0) {
                    pedData.WeaponPermitStatus = EDocumentStatus.Revoked.ToString();
                    break;
                }
            }
            foreach (string r in revocations) {
                if (r?.IndexOf("Fishing", StringComparison.OrdinalIgnoreCase) >= 0) {
                    pedData.FishingPermitStatus = EDocumentStatus.Revoked.ToString();
                    break;
                }
            }
        }

        internal static bool UpdateCourtCaseOutcome(
            string number,
            int status,
            string plea,
            bool? isJuryTrial,
            int? jurySize,
            int? juryVotesForConviction,
            int? juryVotesForAcquittal,
            bool? hasPublicDefender,
            string outcomeNotes,
            string outcomeReasoning = null) {
            CourtData courtCase = courtDatabase.Find(x => x.Number == number);
            if (courtCase == null) return false;

            courtCase.Status = status;
            if (!string.IsNullOrWhiteSpace(plea)) courtCase.Plea = plea;
            if (isJuryTrial.HasValue) courtCase.IsJuryTrial = isJuryTrial.Value;
            if (jurySize.HasValue) courtCase.JurySize = Math.Max(0, jurySize.Value);
            if (juryVotesForConviction.HasValue) courtCase.JuryVotesForConviction = Math.Max(0, juryVotesForConviction.Value);
            if (juryVotesForAcquittal.HasValue) courtCase.JuryVotesForAcquittal = Math.Max(0, juryVotesForAcquittal.Value);
            if (hasPublicDefender.HasValue) courtCase.HasPublicDefender = hasPublicDefender.Value;
            if (outcomeNotes != null) courtCase.OutcomeNotes = outcomeNotes;
            if (outcomeReasoning != null) courtCase.OutcomeReasoning = outcomeReasoning;
            if (status == 3) {
                if (string.IsNullOrWhiteSpace(courtCase.OutcomeReasoning)) {
                    string[] dismissed = new[] {
                        "Charges were dismissed. The case did not proceed to trial.",
                        "The charges were dismissed. The prosecution declined to proceed.",
                        "The case was dismissed. The matter was not tried on the merits.",
                        "Charges were dismissed. Insufficient evidence to proceed.",
                        "The case was dismissed on procedural grounds. The matter did not go to trial.",
                        "The prosecution declined to proceed. The charges were dismissed.",
                        "The case was dismissed without prejudice. The matter was not tried on the merits.",
                        "Charges were dismissed with prejudice. The case will not be refiled.",
                        "The court entered nolle prosequi. The prosecution elected not to proceed.",
                        "The case was dismissed. The prosecution could not meet its burden at this stage.",
                        "Charges were dismissed. The matter did not proceed to trial due to evidentiary issues.",
                        "The case was dismissed. Key witnesses were unavailable or evidence was suppressed.",
                        "The charges were dismissed. Procedural defects led to dismissal.",
                        "The case was dismissed. The prosecution elected to decline prosecution.",
                        "Charges were dismissed. The case did not reach trial.",
                    };
                    courtCase.OutcomeReasoning = dismissed[Helper.GetRandomInt(0, dismissed.Length - 1)];
                }
                courtCase.SentenceReasoning = null;
            } else if (status == 2) {
                courtCase.SentenceReasoning = null;
            }
            courtCase.LastUpdatedUtc = DateTime.UtcNow.ToString("o");

            if (!string.IsNullOrEmpty(courtCase.PedName)) {
                int pedIndex = pedDatabase.FindIndex(pedData => pedData.Name?.ToLower() == courtCase.PedName?.ToLower());
                if (pedIndex != -1) {
                    MDTProPedData pedData = pedDatabase[pedIndex];

                    if (status == 1) {
                        UpdatePedIncarcerationFromCourtData(pedData, courtCase);
                        courtCase.LicenseRevocations = ComputeLicenseRevocations(courtCase);
                        ApplyLicenseRevocationsToPed(pedData, courtCase.LicenseRevocations);
                        if (courtCase.LicenseRevocations.Count > 0) {
                            string revocationText = "The court further ordered: " + string.Join("; ", courtCase.LicenseRevocations) + ".";
                            courtCase.OutcomeReasoning = string.IsNullOrEmpty(courtCase.OutcomeReasoning)
                                ? revocationText
                                : courtCase.OutcomeReasoning.TrimEnd('.', ' ') + ". " + revocationText;
                        }
                        pedData.IsOnProbation = true;
                        pedData.IsWanted = false;
                        pedData.WarrantText = null;
                        SyncSinglePedToCDF(pedData);
                    } else if (status == 2 || status == 3) {
                        pedData.IsWanted = false;
                        pedData.WarrantText = null;
                        SyncSinglePedToCDF(pedData);
                    }

                    KeepPedInDatabase(pedData);
                    pedDatabase[pedIndex] = pedData;
                    Database.SavePed(pedData);
                }
            }

            Database.SaveCourtCase(courtCase);
            return true;
        }

        internal static void CaptureEvidenceForPed(Ped ped) {
            if (ped == null || !ped.IsValid()) return;
            try {
                Persona persona = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(ped);
                if (persona == null || string.IsNullOrEmpty(persona.FullName)) return;

                // 0xA2719263 = WEAPON_UNARMED hash; GET_BEST_PED_WEAPON returns unarmed if ped has no weapon
                bool hadWeapon = NativeFunction.Natives.GET_BEST_PED_WEAPON<uint>(ped, false) != 0xA2719263u;
                MDTProPedData dbPed = GetPedDataForPed(ped);
                // Use both LSPDFR persona and MDT ped data: MDT is authoritative for warrants (IsWanted/WarrantText)
                bool wasWanted = persona.Wanted || (dbPed != null && (dbPed.IsWanted || !string.IsNullOrWhiteSpace(dbPed.WarrantText)));
                // IS_PED_DRUNK rarely set by game; fallback to TASK_MOTION_DRUNK (1160)
                bool wasDrunk = NativeFunction.Natives.IS_PED_DRUNK<bool>(ped)
                    || IsPedTaskActive(ped, 1160); // TASK_MOTION_DRUNK
                // IS_PED_FLEEING only true at exact moment; also check flee-related task indices
                bool wasFleeing = NativeFunction.Natives.IS_PED_FLEEING<bool>(ped)
                    || IsPedFleeingTaskActive(ped);

                // clearAfterRead: false so we don't clear damage state on first check (improves reliability)
                Ped[] nearbyPeds = ped.GetNearbyPeds(50);
                bool assaultedPed = false;
                Ped playerPed = Main.Player;
                if (playerPed != null && playerPed.IsValid() && playerPed != ped) {
                    try {
                        assaultedPed = NativeFunction.Natives.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY<bool>(playerPed, ped, false);
                    } catch { /* ped/player may have been invalidated */ }
                }
                if (!assaultedPed && nearbyPeds != null) {
                    foreach (Ped victim in nearbyPeds) {
                        if (victim == null || victim == ped || !victim.IsValid()) continue;
                        if (!ped.IsValid()) break; // suspect despawned mid-loop
                        try {
                            if (NativeFunction.Natives.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY<bool>(victim, ped, false)) {
                                assaultedPed = true;
                                break;
                            }
                        } catch { /* victim/ped invalid - skip */ }
                    }
                }

                // Vehicle damage: any vehicle damaged by suspect counts (body damage, not just totalled). (1) Vehicles within 50m (chase wreckage spread out). (2) Suspect's current vehicle if they're still in it. Capture at arrest is best-effort; pursuit damage is also logged when they surrender (MarkPedFleeing).
                bool damagedVehicle = CheckVehicleDamageByPed(ped);

                // Illegal weapon carry: armed but weapon permit status is not valid (uses CDF WeaponPermit when available)
                // Also check probation/parole violation. Use Holder fallback for re-encounters.
                bool hadIllegalWeapon = false;
                bool violatedSupervision = false;
                if (dbPed != null) {
                    if (hadWeapon && !string.IsNullOrEmpty(dbPed.WeaponPermitStatus)) {
                        hadIllegalWeapon = !dbPed.WeaponPermitStatus.Equals("Valid", StringComparison.OrdinalIgnoreCase);
                    }
                    if (dbPed.IsOnProbation || dbPed.IsOnParole) violatedSupervision = true;
                }
                // CDF fallback: when ped not yet in our DB (first stop), check CDF WeaponPermit directly
                if (hadWeapon && !hadIllegalWeapon) {
                    try {
                        var cdfPed = ped.GetPedData();
                        if (cdfPed?.WeaponPermit != null && cdfPed.WeaponPermit.Status != EDocumentStatus.Valid)
                            hadIllegalWeapon = true;
                    } catch { }
                }

                bool resisted = GetPedResistanceFromPR(ped) || assaultedPed;

                // Re-validate ped before using Handle (may have been invalidated during long operations above)
                if (!ped.IsValid()) return;

                string cacheKey = dbPed?.Name ?? persona.FullName;
                lock (pedEvidenceLock) {
                    PruneStaleEvidenceEntries();
                    bool wasFleeingByHandle = false;
                    bool damagedVehicleByHandle = false;
                    bool assaultedByHandle = false;
                    bool hadWeaponByHandle = false;
                    try {
                        wasFleeingByHandle = fleeingPedHandles.Remove(ped.Handle);
                        damagedVehicleByHandle = damagedVehicleHandles.Remove(ped.Handle);
                        assaultedByHandle = assaultedPedHandles.Remove(ped.Handle);
                        hadWeaponByHandle = hadWeaponHandles.Remove(ped.Handle);
                    } catch { /* ped invalidated - Handle may throw */ }
                    if (!pedEvidenceCache.TryGetValue(cacheKey, out PedEvidenceContext ctx)) {
                        ctx = new PedEvidenceContext();
                        pedEvidenceCache[cacheKey] = ctx;
                    }
                    ctx.HadWeapon = hadWeapon || hadWeaponByHandle;
                    ctx.WasWanted = wasWanted;
                    ctx.WasDrunk = wasDrunk;
                    ctx.WasFleeing = ctx.WasFleeing || wasFleeing || wasFleeingByHandle;
                    ctx.AssaultedPed = assaultedPed || assaultedByHandle;
                    ctx.DamagedVehicle = ctx.DamagedVehicle || damagedVehicle || damagedVehicleByHandle;
                    ctx.HadIllegalWeapon = hadIllegalWeapon;
                    if (hadWeaponByHandle && !ctx.HadIllegalWeapon) {
                        if (dbPed != null && !string.IsNullOrEmpty(dbPed.WeaponPermitStatus) && !dbPed.WeaponPermitStatus.Equals("Valid", StringComparison.OrdinalIgnoreCase))
                            ctx.HadIllegalWeapon = true;
                        if (!ctx.HadIllegalWeapon) {
                            try {
                                var cdfPed = ped.GetPedData();
                                if (cdfPed?.WeaponPermit != null && cdfPed.WeaponPermit.Status != EDocumentStatus.Valid)
                                    ctx.HadIllegalWeapon = true;
                            } catch { }
                        }
                    }
                    ctx.ViolatedSupervision = violatedSupervision;
                    ctx.Resisted = resisted;
                    ctx.CapturedAt = DateTime.UtcNow;
                }
            } catch (Exception e) {
                // "address cannot be zero" / invalid entity - expected when ped despawned mid-capture; don't warn
                bool isInvalidEntity = e.Message?.IndexOf("address cannot be zero", StringComparison.OrdinalIgnoreCase) >= 0
                    || e.Message?.IndexOf("Invalid handle", StringComparison.OrdinalIgnoreCase) >= 0
                    || e.Message?.IndexOf("not a handle to an entity", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isInvalidEntity)
                    Helper.Log($"Evidence capture failed: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>Returns true if the given task index is active on the ped. Uses GET_IS_TASK_ACTIVE.</summary>
        private static bool IsPedTaskActive(Ped ped, int taskIndex) {
            if (ped == null || !ped.IsValid()) return false;
            try {
                return NativeFunction.Natives.GET_IS_TASK_ACTIVE<bool>(ped, taskIndex);
            } catch { return false; }
        }

        /// <summary>Returns true if the ped has any flee-related task active (chase-then-stop often misses IS_PED_FLEEING).</summary>
        private static bool IsPedFleeingTaskActive(Ped ped) {
            if (ped == null || !ped.IsValid()) return false;
            // Task indices from eTaskTypes: SHOCKING_EVENT_FLEE 330, REACT_TO_PURSUIT 495, LEAVE_CAR_AND_FLEE 601,
            // EXHAUSTED_FLEE 869, SCENARIO_FLEE 877, SMART_FLEE 881, REACT_AND_FLEE 1814, VEHICLE_FLEE 1941
            int[] fleeTasks = { 330, 495, 601, 869, 877, 881, 1814, 1941 };
            foreach (int idx in fleeTasks)
                if (IsPedTaskActive(ped, idx)) return true;
            return false;
        }

        /// <summary>True if any vehicle has been damaged by this ped or their vehicle. During pursuits, GTA attributes collision damage to the suspect's vehicle, not the ped, so we check both.</summary>
        private static bool CheckVehicleDamageByPed(Ped ped) {
            if (ped == null || !ped.IsValid()) return false;
            try {
                Vehicle[] nearbyVehicles = ped.GetNearbyVehicles(50);
                // (1) Direct: vehicle damaged by ped entity (rare; usually applies to on-foot damage)
                bool any = nearbyVehicles != null && nearbyVehicles.Any(v =>
                    v != null && v.IsValid() &&
                    NativeFunction.Natives.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY<bool>(v, ped, false));
                // (2) Suspect's current vehicle damaged by ped (e.g. they were driving, crashed)
                if (!any && ped.IsInAnyVehicle(false)) {
                    Vehicle suspectVehicle = ped.CurrentVehicle;
                    if (suspectVehicle != null && suspectVehicle.IsValid() &&
                        NativeFunction.Natives.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY<bool>(suspectVehicle, ped, false))
                        any = true;
                }
                // (3) Key fix: during pursuit, collision damage is attributed to the suspect's VEHICLE, not the ped
                if (!any && ped.IsInAnyVehicle(false)) {
                    Vehicle suspectVehicle = ped.CurrentVehicle;
                    if (suspectVehicle != null && suspectVehicle.IsValid() && nearbyVehicles != null) {
                        any = nearbyVehicles.Any(v =>
                            v != null && v.IsValid() && v != suspectVehicle &&
                            NativeFunction.Natives.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY<bool>(v, suspectVehicle, false));
                    }
                }
                return any;
            } catch { return false; }
        }

        internal static void MarkPedFleeing(Ped ped) {
            if (ped == null || !ped.IsValid()) return;
            try {
                bool damagedVehicle = CheckVehicleDamageByPed(ped);
                Ped playerPed = Main.Player;
                bool assaultedPlayer = playerPed != null && playerPed.IsValid() && playerPed != ped &&
                    NativeFunction.Natives.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY<bool>(playerPed, ped, false);
                bool hadWeapon = NativeFunction.Natives.GET_BEST_PED_WEAPON<uint>(ped, false) != 0xA2719263u; // WEAPON_UNARMED
                DateTime now = DateTime.UtcNow;
                lock (pedEvidenceLock) {
                    fleeingPedHandles[ped.Handle] = now;
                    if (damagedVehicle) damagedVehicleHandles[ped.Handle] = now;
                    if (assaultedPlayer) assaultedPedHandles[ped.Handle] = now;
                    if (hadWeapon) hadWeaponHandles[ped.Handle] = now;
                    Persona persona = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(ped);
                    if (persona != null && !string.IsNullOrEmpty(persona.FullName)) {
                        string cacheKey = GetPedDataForPed(ped)?.Name ?? persona.FullName;
                        if (!pedEvidenceCache.TryGetValue(cacheKey, out PedEvidenceContext ctx)) {
                            ctx = new PedEvidenceContext();
                            pedEvidenceCache[cacheKey] = ctx;
                        }
                        ctx.WasFleeing = true;
                        if (damagedVehicle) ctx.DamagedVehicle = true;
                        if (assaultedPlayer) ctx.AssaultedPed = true;
                        if (hadWeapon) ctx.HadWeapon = true;
                        ctx.CapturedAt = now;
                    }
                }
            } catch (Exception e) {
                Helper.Log($"Fleeing capture failed: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        internal static void MarkPedPatDown(Ped ped) {
            if (ped == null || !ped.IsValid()) return;
            try {
                Persona persona = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(ped);
                if (persona == null || string.IsNullOrEmpty(persona.FullName)) return;
                string cacheKey = GetPedDataForPed(ped)?.Name ?? persona.FullName;
                lock (pedEvidenceLock) {
                    if (!pedEvidenceCache.TryGetValue(cacheKey, out PedEvidenceContext ctx)) {
                        ctx = new PedEvidenceContext();
                        pedEvidenceCache[cacheKey] = ctx;
                    }
                    ctx.WasPatDown = true;
                }
                CaptureFirearmsFromPed(ped, "Pat-down");
            } catch (Exception e) {
                Helper.Log($"PatDown capture failed: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>When firearmDebugLogging is true, reflects PolicingRedefined assembly and logs public types/events that might relate to weapons. Run once on load to discover what PR exposes.</summary>
        internal static void LogPRAssemblyFirearmDiagnostics() {
            if (!Main.usePR || !SetupController.GetConfig().firearmDebugLogging) return;
            try {
                Type knownType = Type.GetType("PolicingRedefined.API.EventsAPI, PolicingRedefined")
                    ?? Type.GetType("PolicingRedefined.API.SearchItemsAPI, PolicingRedefined");
                if (knownType == null) return;
                var asm = knownType.Assembly;
                var types = asm.GetExportedTypes();
                var keywords = new[] { "Weapon", "Firearm", "Serial", "Dispatch", "Search", "WeaponItem", "FirearmItem" };
                var relevant = new List<string>();
                foreach (var t in types) {
                    string fn = t.FullName ?? "";
                    if (keywords.Any(k => fn.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                        relevant.Add(t.IsClass ? $"Type: {fn}" : (t.IsEnum ? $"Enum: {fn}" : $"Event/Other: {fn}"));
                }
                foreach (var t in types) {
                    try {
                        foreach (var e in t.GetEvents(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)) {
                            string en = e.Name ?? "";
                            if (keywords.Any(k => en.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                                relevant.Add($"Event: {t.FullName}.{en}");
                        }
                    } catch { }
                }
                relevant.Sort();
                Helper.Log($"[Firearm] PR assembly ({asm.GetName().Name} {asm.GetName().Version}): {relevant.Count} relevant types/events:\n  " + string.Join("\n  ", relevant.Take(60)), false, Helper.LogSeverity.Info);
                if (relevant.Count > 60)
                    Helper.Log($"[Firearm] ... and {relevant.Count - 60} more. See docs/FIREARM-DATA-SOURCES.md", false, Helper.LogSeverity.Info);
            } catch (Exception ex) {
                Helper.Log($"[Firearm] PR assembly diagnostics failed: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>Uses PR SearchItemsAPI.GetPedSearchItems to capture weapon/firearm items and persist to firearm_records.</summary>
        internal static void CaptureFirearmsFromPed(Ped ped, string source = "Search") {
            if (ped == null || !ped.IsValid() || !Main.usePR) return;
            try {
                if (SetupController.GetConfig().firearmDebugLogging)
                    Helper.Log($"[Firearm] CaptureFirearmsFromPed called: source={source}, pedHandle={ped?.Handle}", false, Helper.LogSeverity.Info);
                string ownerName = (GetPedDataForPed(ped)?.Name ?? LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(ped)?.FullName)?.Trim();
                if (string.IsNullOrWhiteSpace(ownerName))
                    ownerName = source.Contains("Dead") ? "Unknown (unidentified body)" : "Unknown";
                if (string.IsNullOrWhiteSpace(ownerName)) {
                    if (SetupController.GetConfig().firearmDebugLogging)
                        Helper.Log($"[Firearm] CaptureFirearmsFromPed skipped: no owner name for source={source}", false, Helper.LogSeverity.Info);
                    return;
                }
                int count = CaptureFirearmsFromPedWithOwner(ped, ownerName, source);
                if (SetupController.GetConfig().firearmDebugLogging && count > 0)
                    Helper.Log($"[Firearm] CaptureFirearmsFromPed saved {count} record(s) from {source}", false, Helper.LogSeverity.Info);
            } catch (Exception e) {
                Helper.Log($"Firearm/drug capture failed: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>Captures firearms from ped using an explicit owner (e.g. "Evidence (pickup)" for player-held weapons). Returns count of firearm records captured.</summary>
        private static int CaptureFirearmsFromPedWithOwner(Ped ped, string ownerName, string source) {
            if (ped == null || !ped.IsValid() || !Main.usePR || string.IsNullOrWhiteSpace(ownerName)) return 0;
            bool debug = SetupController.GetConfig().firearmDebugLogging;
            try {
                Type searchApiType = Type.GetType("PolicingRedefined.API.SearchItemsAPI, PolicingRedefined")
                    ?? Type.GetType("PolicingRedefined.API.SearchItemAPI, PolicingRedefined")
                    ?? Type.GetType("PolicingRedefined.Interaction.Assets.SearchItemsAPI, PolicingRedefined");
                if (searchApiType == null) {
                    if (debug) Helper.Log("[Firearm] CaptureFirearmsFromPedWithOwner: SearchItemsAPI type not found (PR not loaded or API changed)", false, Helper.LogSeverity.Info);
                    return 0;
                }

                MethodInfo getItems = searchApiType.GetMethod("GetPedSearchItems", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Rage.Ped) }, null);
                if (getItems == null) {
                    if (debug) Helper.Log("[Firearm] CaptureFirearmsFromPedWithOwner: GetPedSearchItems method not found", false, Helper.LogSeverity.Info);
                    return 0;
                }

                object result = getItems.Invoke(null, new object[] { ped });
                if (result == null) {
                    if (debug) Helper.Log($"[Firearm] CaptureFirearmsFromPedWithOwner: GetPedSearchItems returned null for source={source}, owner={ownerName}", false, Helper.LogSeverity.Info);
                    return 0;
                }

                System.Collections.IEnumerable list = result as System.Collections.IEnumerable;
                if (list == null) {
                    if (debug) Helper.Log($"[Firearm] CaptureFirearmsFromPedWithOwner: GetPedSearchItems result not IEnumerable for source={source}", false, Helper.LogSeverity.Info);
                    return 0;
                }

                int rawCount = 0;
                foreach (var _ in list) { rawCount++; }
                if (debug) Helper.Log($"[Firearm] CaptureFirearmsFromPedWithOwner: GetPedSearchItems returned {rawCount} raw item(s) for source={source}", false, Helper.LogSeverity.Info);

                var records = ExtractFirearmRecordsFromItemList(list, ownerName, source);
                if (debug && rawCount > 0 && records.Count == 0)
                    Helper.Log($"[Firearm] CaptureFirearmsFromPedWithOwner: {rawCount} item(s) but 0 firearm records extracted (possibly melee/non-firearm filtered)", false, Helper.LogSeverity.Info);
                var drugRecords = new List<DrugRecord>();
                string now = DateTime.UtcNow.ToString("o");

                foreach (object item in list) {
                    if (item == null) continue;
                    Type t = item.GetType();
                    bool isDrug = t.Name.Contains("DrugItem") || t.Name.Contains("Drug");
                    if (!isDrug) continue;
                    string drugType = null;
                    string drugCategory = null;
                    string drugDesc = null;
                    foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                        try {
                            object val = prop.GetValue(item);
                            if (val == null) continue;
                            if (prop.Name == "DrugType") drugType = val.ToString();
                            else if (prop.Name == "Value" || prop.Name == "Description") drugDesc = val?.ToString();
                        } catch { }
                    }
                    if (!string.IsNullOrEmpty(drugType)) {
                        drugRecords.Add(new DrugRecord {
                            OwnerPedName = ownerName,
                            DrugType = drugType,
                            DrugCategory = drugCategory,
                            Description = drugDesc,
                            Source = source,
                            FirstSeenAt = now,
                            LastSeenAt = now
                        });
                    }
                }

                if (records.Count > 0 || drugRecords.Count > 0) {
                    if (!ped.Exists()) return 0;
                    if (records.Count > 0) {
                        Database.SaveFirearmRecords(records);
                        Helper.Log($"[Firearm] Saved {records.Count} firearm record(s) from {source} (owner: {ownerName})", false, Helper.LogSeverity.Info);
                    }
                    if (drugRecords.Count > 0) Database.SaveDrugRecords(drugRecords);
                }
                return records.Count;
            } catch (Exception e) {
                Helper.Log($"Firearm/drug capture failed: {e.Message}", false, Helper.LogSeverity.Warning);
                return 0;
            }
        }

        /// <summary>Fallback when PR GetPedSearchItems returns nothing: capture player's held weapon via game native. PR often doesn't add pickup weapons to player search items, so this ensures something shows in Firearms Check.</summary>
        private static void TryCapturePlayerHeldWeaponFallback() {
            if (Main.Player == null || !Main.Player.IsValid()) return;
            try {
                uint weaponHash = NativeFunction.Natives.GET_SELECTED_PED_WEAPON<uint>(Main.Player, false);
                if (weaponHash == 0u || weaponHash == 0xA2719263u) return; // WEAPON_UNARMED
                if (IsMeleeOrNonFirearm(weaponHash, null)) return;

                string displayName = GetWeaponDisplayNameFromHash(weaponHash);
                if (string.IsNullOrWhiteSpace(displayName)) displayName = $"Weapon ({weaponHash})";

                if (SetupController.GetConfig().firearmDebugLogging)
                    Helper.Log($"[Firearm] Fallback saving player-held weapon: {displayName} (hash={weaponHash})", false, Helper.LogSeverity.Info);

                var record = new FirearmRecord {
                    SerialNumber = null,
                    IsSerialScratched = false,
                    OwnerPedName = "Evidence (pickup)",
                    WeaponModelId = null,
                    WeaponDisplayName = displayName,
                    WeaponModelHash = weaponHash,
                    IsStolen = false,
                    Description = null,
                    Source = "Evidence (pickup)",
                    FirstSeenAt = DateTime.UtcNow.ToString("o"),
                    LastSeenAt = DateTime.UtcNow.ToString("o")
                };
                Database.SaveFirearmRecords(new List<FirearmRecord> { record });
                Helper.Log($"[Firearm] Saved 1 firearm record from fallback (player-held: {displayName})", false, Helper.LogSeverity.Info);
            } catch (Exception e) {
                Helper.Log($"Firearm fallback failed: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>Poll-based: for vehicles in vehicleDatabase with valid Holder, if PR reports vehicle searched and we haven't captured yet, persist search items.</summary>
        internal static void TryCaptureVehicleSearches() {
            if (!Main.usePR) return;
            try {
                MethodInfo getHasSearched = null;
                var searchApiTypeNames = new[] {
                    "PolicingRedefined.API.SearchItemsAPI, PolicingRedefined",
                    "PolicingRedefined.Interaction.Assets.SearchItemsAPI, PolicingRedefined",
                    "PolicingRedefined.API.VehicleAPI, PolicingRedefined"
                };
                foreach (string typeName in searchApiTypeNames) {
                    Type apiType = Type.GetType(typeName);
                    if (apiType == null) continue;
                    getHasSearched = apiType.GetMethod("GetHasVehicleBeenSearched",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase,
                        null, new[] { typeof(Rage.Vehicle) }, null);
                    if (getHasSearched != null) break;
                }
                if (getHasSearched == null) return;

                List<MDTProVehicleData> vehiclesToCheck;
                lock (_vehicleDbLock) {
                    vehiclesToCheck = vehicleDatabase.Where(v => v?.Holder != null && v.Holder.Exists() && !string.IsNullOrEmpty(v.LicensePlate)).ToList();
                }
                foreach (var vData in vehiclesToCheck) {
                    Vehicle v = vData.Holder;
                    if (v == null || !v.Exists()) continue;
                    string plate = vData.LicensePlate?.Trim();
                    if (string.IsNullOrEmpty(plate)) continue;
                    bool alreadyCaptured;
                    lock (capturedVehicleSearchLock) {
                        alreadyCaptured = capturedVehicleSearchPlates.Contains(plate);
                    }
                    if (alreadyCaptured) continue;
                    bool searched = false;
                    try {
                        object r = getHasSearched.Invoke(null, new object[] { v });
                        searched = r is bool b && b;
                    } catch { }
                    if (!searched) continue;
                    CaptureVehicleSearchItems(v);
                    lock (capturedVehicleSearchLock) {
                        capturedVehicleSearchPlates.Add(plate);
                        if (capturedVehicleSearchPlates.Count > MaxCapturedVehicleSearchPlates) {
                            var toRemove = capturedVehicleSearchPlates.Take(MaxCapturedVehicleSearchPlates / 2).ToList();
                            foreach (var p in toRemove) capturedVehicleSearchPlates.Remove(p);
                        }
                    }
                }
            } catch (Exception e) {
                Helper.Log($"TryCaptureVehicleSearches failed: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>Captures firearms from (1) player's held weapon when PR has run a check on it, (2) weapon pickups on the ground via GetPickupSearchItems if PR exposes it. Ground/pickup weapons use "Evidence (ground)" as owner.</summary>
        internal static void TryCapturePickupAndPlayerFirearms() {
            if (!Main.usePR || Main.Player == null || !Main.Player.IsValid()) return;
            try {
                // Try GetPedSearchItems on the player - when you pick up a weapon and run a firearm check, PR may add it to the player's search items. Use "Evidence (pickup)" as owner so it appears in Firearms Check.
                int prCount = CaptureFirearmsFromPedWithOwner(Main.Player, "Evidence (pickup)", "Evidence (pickup)");
                if (SetupController.GetConfig().firearmDebugLogging && prCount == 0) {
                    var now = DateTime.UtcNow;
                    if ((now - lastFirearmPollDebugLog).TotalSeconds >= 15) {
                        lastFirearmPollDebugLog = now;
                        uint heldHash = NativeFunction.Natives.GET_SELECTED_PED_WEAPON<uint>(Main.Player, false);
                        string heldName = heldHash == 0u || heldHash == 0xA2719263u ? "none" : (GetWeaponDisplayNameFromHash(heldHash) ?? $"hash_{heldHash}");
                        Helper.Log($"[Firearm] Poll: PR returned 0 for player, holding {heldName}, fallback would run={!IsMeleeOrNonFirearm(heldHash, null)}", false, Helper.LogSeverity.Info);
                    }
                }
                // Fallback: PR often doesn't add pickup weapons to player search items. When holding a firearm, capture via game native so it at least shows in Recent.
                if (prCount == 0) TryCapturePlayerHeldWeaponFallback();

                // Try GetPickupSearchItems via reflection - PR WeaponItem supports (item, Pickup pickup, weaponModelId). Use Object type for Rage compatibility.
                Type searchApiType = Type.GetType("PolicingRedefined.API.SearchItemsAPI, PolicingRedefined")
                    ?? Type.GetType("PolicingRedefined.API.SearchItemAPI, PolicingRedefined")
                    ?? Type.GetType("PolicingRedefined.Interaction.Assets.SearchItemsAPI, PolicingRedefined");
                if (searchApiType == null) return;

                MethodInfo getPickupItems = searchApiType.GetMethod("GetPickupSearchItems", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Rage.Object) }, null);
                if (getPickupItems == null) return;

                try {
                    var getAllPickups = typeof(Rage.World).GetMethod("GetAllPickupObjects", BindingFlags.Public | BindingFlags.Static);
                    if (getAllPickups != null) {
                        object pickupsObj = getAllPickups.Invoke(null, null);
                        if (pickupsObj is System.Array pickups && pickups.Length > 0) {
                            foreach (object obj in pickups) {
                                if (obj == null) continue;
                                if (!(obj is Rage.Entity ent) || !ent.Exists()) continue;
                                if (Main.Player.DistanceTo(ent.Position) > 25f) continue;
                                lock (capturedPickupHandlesLock) {
                                    if (capturedPickupHandles.Contains(ent.Handle)) continue;
                                }
                                object result = null;
                                try { result = getPickupItems.Invoke(null, new object[] { obj }); } catch { continue; }
                                if (result == null) continue;
                                if (!(result is System.Collections.IEnumerable list)) continue;
                                var records = ExtractFirearmRecordsFromItemList(list, "Evidence (ground)", "Evidence (ground)");
                                if (records.Count > 0) {
                                    Database.SaveFirearmRecords(records);
                                    Helper.Log($"[Firearm] Saved {records.Count} firearm record(s) from pickup (Evidence ground)", false, Helper.LogSeverity.Info);
                                    lock (capturedPickupHandlesLock) {
                                        capturedPickupHandles.Add(ent.Handle);
                                        if (capturedPickupHandles.Count > 200) {
                                            foreach (var h in capturedPickupHandles.Take(100).ToList())
                                                capturedPickupHandles.Remove(h);
                                        }
                                    }
                                }
                            }
                        }
                    }
                } catch (Exception ex) {
                    Helper.Log($"TryCapturePickupAndPlayerFirearms pickup loop: {ex.Message}", false, Helper.LogSeverity.Warning);
                }
            } catch (Exception e) {
                Helper.Log($"TryCapturePickupAndPlayerFirearms failed: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>Accepts firearm check result from Dispatch or external system. Call when Dispatch returns serial/owner so it shows in MDT Firearms Check. Source will be "Dispatch".</summary>
        internal static bool SaveFirearmCheckResultFromDispatch(string serialNumber, string ownerName, string weaponType, string status = null, string weaponModelId = null) {
            if (string.IsNullOrWhiteSpace(ownerName)) return false;
            Helper.Log($"[Firearm] SaveFirearmCheckResultFromDispatch: serial={serialNumber ?? "(none)"}, owner={ownerName}, weapon={weaponType ?? weaponModelId ?? "Firearm"}, status={status ?? "—"}", false, Helper.LogSeverity.Info);
            string serial = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim();
            string weapon = (weaponType ?? weaponModelId ?? "Firearm").Trim();
            if (string.IsNullOrEmpty(weapon)) weapon = "Firearm";
            bool isStolen = !string.IsNullOrEmpty(status) && status.IndexOf("stolen", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isScratched = !string.IsNullOrEmpty(status) && status.IndexOf("scratched", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isScratched) serial = null;

            var record = new FirearmRecord {
                SerialNumber = serial,
                IsSerialScratched = isScratched,
                OwnerPedName = ownerName.Trim(),
                WeaponModelId = weaponModelId,
                WeaponDisplayName = weapon,
                WeaponModelHash = 0u,
                IsStolen = isStolen,
                Description = status,
                Source = "Dispatch",
                FirstSeenAt = DateTime.UtcNow.ToString("o"),
                LastSeenAt = DateTime.UtcNow.ToString("o")
            };
            Database.SaveFirearmRecords(new List<FirearmRecord> { record });
            return true;
        }

        /// <summary>Filters firearm records to actual guns only (excludes melee, knives, throwables) and drops empty/meaningless entries. Call before returning to Firearms Check UI.</summary>
        internal static List<FirearmRecord> FilterToActualFirearms(List<FirearmRecord> records, int? maxCount = null) {
            if (records == null) return new List<FirearmRecord>();
            var filtered = new List<FirearmRecord>();
            foreach (var r in records) {
                if (r == null) continue;
                if (IsMeleeOrNonFirearm(r.WeaponModelHash, r.WeaponModelId)) continue;
                string name = (r.WeaponDisplayName ?? r.Description ?? r.WeaponModelId ?? "").Trim();
                bool hasSerialOrScratched = r.IsSerialScratched || !string.IsNullOrWhiteSpace(r.SerialNumber);
                if (string.IsNullOrEmpty(name) && !hasSerialOrScratched) continue; // Empty "—" entries with nothing to show
                filtered.Add(r);
            }
            if (maxCount.HasValue && filtered.Count > maxCount.Value)
                return filtered.Take(maxCount.Value).ToList();
            return filtered;
        }

        /// <summary>True if the weapon is melee/throwable/non-firearm. Firearms Check should only show actual guns.</summary>
        private static bool IsMeleeOrNonFirearm(uint hash, string modelId) {
            if (hash != 0u && MeleeAndNonFirearmHashes.Contains(hash)) return true;
            if (string.IsNullOrWhiteSpace(modelId)) return false;
            string m = modelId.Trim().ToUpperInvariant();
            return m.Contains("KNIFE") || m.Contains("KNUCKLE") || m.Contains("NIGHTSTICK") || m.Contains("HAMMER")
                || m.Contains("CROWBAR") || m.Contains("GOLFCLUB") || m.Contains("DAGGER") || m.Contains("MACHETE")
                || m.Contains("SWITCHBLADE") || m.Contains("BATTLEAXE") || m.Contains("POOLCUE") || m.Contains("WRENCH")
                || m.Contains("FLASHLIGHT") || m.Contains("HATCHET") || m.Contains("BOTTLE") || m.Contains("UNARMED")
                || m.Contains("PETROLCAN") || m.Contains("JERRYCAN") || m.Contains("FIREEXTINGUISHER") || m.Contains("HAZARDCAN")
                || m.Contains("FERTILIZER") || m.Contains("PARACHUTE") || m.Contains("GRENADE") || m.Contains("MOLOTOV")
                || m.Contains("SNOWBALL") || m.Contains("BALL") || m.EndsWith("_BAT"); // WEAPON_BAT, not COMBATPISTOL
        }

        /// <summary>Extracts FirearmRecords from PR search item list. Shared by CaptureFirearmsFromPed and pickup/player capture.</summary>
        private static List<FirearmRecord> ExtractFirearmRecordsFromItemList(System.Collections.IEnumerable list, string ownerName, string source) {
            var records = new List<FirearmRecord>();
            string now = DateTime.UtcNow.ToString("o");
            foreach (object item in list) {
                if (item == null) continue;
                Type t = item.GetType();
                bool isWeapon = t.Name.Contains("WeaponItem") || t.Name.Contains("FirearmItem") || t.Name.Contains("Weapon") || t.Name.Contains("Firearm");
                if (!isWeapon) continue;
                uint hash = 0u;
                string modelId = null;
                bool isStolen = false;
                string description = null;
                string serial = null;
                bool isSerialScratched = false;
                foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    try {
                        object val = prop.GetValue(item);
                        string name = prop.Name;
                        if (name == "WeaponModelHash" || name == "ModelHash") {
                            if (val == null) continue;
                            if (val is int i) hash = unchecked((uint)i);
                            else if (val is uint u) hash = u;
                            else if (val is long l) hash = unchecked((uint)l);
                            else if (val is ulong ul) hash = (uint)Math.Min(ul, uint.MaxValue);
                            else { try { hash = Convert.ToUInt32(val); } catch { } }
                        } else if (name == "WeaponModelId" || name == "ModelId") modelId = val?.ToString();
                        else if (name == "IsStolen") { try { isStolen = Convert.ToBoolean(val); } catch { } }
                        else if (name == "Value" || name == "Description") description = description ?? val?.ToString();
                        else if ((name == "SerialNumber" || name == "Serial") && serial == null) serial = val?.ToString();
                        else if ((name == "State" || name == "FirearmState" || name == "SerialState") && val != null) {
                            string stateStr = val.ToString();
                            if (string.Equals(stateStr, "ScratchedSN", StringComparison.OrdinalIgnoreCase) || (int.TryParse(stateStr, out int si) && si == 1)) {
                                isSerialScratched = true;
                                serial = null;
                            }
                        }
                    } catch { }
                }
                if (hash == 0u) continue;
                if (IsMeleeOrNonFirearm(hash, modelId)) continue; // Knives, bats, etc. don't belong in Firearms Check
                if (isSerialScratched) serial = null;
                string displayName = GetWeaponDisplayNameFromHash(hash) ?? description ?? modelId;
                records.Add(new FirearmRecord {
                    SerialNumber = string.IsNullOrWhiteSpace(serial) ? null : serial.Trim(),
                    IsSerialScratched = isSerialScratched,
                    OwnerPedName = ownerName.Trim(),
                    WeaponModelId = modelId,
                    WeaponDisplayName = displayName,
                    WeaponModelHash = hash,
                    IsStolen = isStolen,
                    Description = description,
                    Source = source,
                    FirstSeenAt = now,
                    LastSeenAt = now
                });
            }
            return records;
        }

        /// <summary>Uses PR SearchItemsAPI.GetVehicleSearchItems to capture items and persist to vehicle_search_records. Also creates firearm_records for Weapon/FirearmItems so they appear in Firearms Check.</summary>
        internal static void CaptureVehicleSearchItems(Vehicle vehicle) {
            if (vehicle == null || !vehicle.Exists() || !Main.usePR) return;
            try {
                string plate = vehicle.LicensePlate?.Trim();
                if (string.IsNullOrEmpty(plate)) return;
                if (SetupController.GetConfig().firearmDebugLogging)
                    Helper.Log($"[Firearm] CaptureVehicleSearchItems called for plate {plate}", false, Helper.LogSeverity.Info);

                string ownerForFirearms = GetVehicleDriverNameForFirearmOwner(vehicle, plate);

                MethodInfo getItems = null;
                foreach (string typeName in new[] {
                    "PolicingRedefined.API.SearchItemsAPI, PolicingRedefined",
                    "PolicingRedefined.Interaction.Assets.SearchItemsAPI, PolicingRedefined"
                }) {
                    Type searchApiType = Type.GetType(typeName);
                    if (searchApiType == null) continue;
                    getItems = searchApiType.GetMethod("GetVehicleSearchItems",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase,
                        null, new[] { typeof(Rage.Vehicle) }, null);
                    if (getItems != null) break;
                }
                if (getItems == null) {
                    if (SetupController.GetConfig().firearmDebugLogging)
                        Helper.Log("[Firearm] CaptureVehicleSearchItems: GetVehicleSearchItems not found", false, Helper.LogSeverity.Info);
                    return;
                }

                object result = getItems.Invoke(null, new object[] { vehicle });
                if (result == null) {
                    if (SetupController.GetConfig().firearmDebugLogging)
                        Helper.Log($"[Firearm] CaptureVehicleSearchItems: GetVehicleSearchItems returned null for plate {plate}", false, Helper.LogSeverity.Info);
                    return;
                }
                var list = result as System.Collections.IEnumerable;
                if (list == null) return;

                var records = new List<VehicleSearchRecord>();
                var firearmRecords = new List<FirearmRecord>();
                string now = DateTime.UtcNow.ToString("o");

                foreach (object item in list) {
                    if (item == null) continue;
                    Type t = item.GetType();
                    string itemType = "Contraband";
                    string drugType = null;
                    string itemLocation = null;
                    string description = null;
                    uint weaponHash = 0u;
                    string weaponModelId = null;
                    string serial = null;
                    bool isStolen = false;
                    bool isSerialScratched = false;

                    if (t.Name.Contains("DrugItem") || t.Name.Contains("Drug")) {
                        itemType = "Drug";
                        string amountStr = null;
                        foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                            try {
                                object val = prop.GetValue(item);
                                if (val == null) continue;
                                string pn = prop.Name;
                                string vs = val?.ToString()?.Trim();
                                if (string.IsNullOrEmpty(vs)) continue;
                                if (pn == "DrugType" || pn == "SubstanceType" || pn == "Substance" || pn == "Name" || pn == "DrugName") drugType = drugType ?? vs;
                                else if (pn == "Value" || pn == "Description" || pn == "FlavorText") description = description ?? vs;
                                else if (pn == "Location" || pn == "ItemLocation") itemLocation = itemLocation ?? vs;
                                else if (pn == "Amount" || pn == "Quantity") amountStr = amountStr ?? vs;
                            } catch { }
                        }
                        if (string.IsNullOrEmpty(description) && !string.IsNullOrEmpty(amountStr))
                            description = string.IsNullOrEmpty(drugType) ? amountStr : $"{drugType} ({amountStr})";
                        if (string.IsNullOrEmpty(description)) description = drugType;
                    } else if (t.Name.Contains("WeaponItem") || t.Name.Contains("FirearmItem") || (t.Name.Contains("Weapon") && t.Name.Contains("Item"))) {
                        itemType = "Weapon";
                        string itemOwner = null;
                        foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                            try {
                                object val = prop.GetValue(item);
                                if (val == null) continue;
                                string pn = prop.Name;
                                if (pn == "WeaponModelHash" || pn == "ModelHash") weaponHash = Convert.ToUInt32(val);
                                else if (pn == "WeaponModelId" || pn == "ModelId") weaponModelId = val?.ToString();
                                else if (pn == "Value" || pn == "Description" || pn == "FlavorText") description = description ?? val?.ToString();
                                else if (pn == "Location" || pn == "ItemLocation") itemLocation = itemLocation ?? val?.ToString();
                                else if (pn == "SerialNumber" || pn == "Serial") serial = serial ?? val?.ToString();
                                else if (pn == "Owner" || pn == "RegisteredOwner" || pn == "OwnerName" || pn == "OwnerPedName")
                                    itemOwner = string.IsNullOrWhiteSpace(itemOwner) ? val?.ToString()?.Trim() : itemOwner;
                                else if (pn == "IsStolen") { try { isStolen = Convert.ToBoolean(val); } catch { } }
                                else if ((pn == "State" || pn == "FirearmState" || pn == "SerialState") && val != null) {
                                    string stateStr = val.ToString();
                                    if (string.Equals(stateStr, "ScratchedSN", StringComparison.OrdinalIgnoreCase) || (int.TryParse(stateStr, out int si) && si == 1))
                                        isSerialScratched = true;
                                }
                            } catch { }
                        }
                        string firearmOwner = !string.IsNullOrWhiteSpace(itemOwner) ? itemOwner : ownerForFirearms;
                        if (weaponHash != 0u && firearmOwner != null && !IsMeleeOrNonFirearm(weaponHash, weaponModelId)) {
                            string displayName = GetWeaponDisplayNameFromHash(weaponHash) ?? description ?? weaponModelId;
                            firearmRecords.Add(new FirearmRecord {
                                SerialNumber = isSerialScratched ? null : (string.IsNullOrWhiteSpace(serial) ? null : serial.Trim()),
                                IsSerialScratched = isSerialScratched,
                                OwnerPedName = firearmOwner,
                                WeaponModelId = weaponModelId,
                                WeaponDisplayName = displayName,
                                WeaponModelHash = weaponHash,
                                IsStolen = isStolen,
                                Description = description,
                                Source = "Vehicle search",
                                FirstSeenAt = now,
                                LastSeenAt = now
                            });
                        }
                    } else {
                        foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                            try {
                                object val = prop.GetValue(item);
                                if (val == null) continue;
                                string pn = prop.Name;
                                string vs = val?.ToString()?.Trim();
                                if (string.IsNullOrEmpty(vs)) continue;
                                if (pn == "Value" || pn == "Description" || pn == "FlavorText" || pn == "Name" || pn == "DisplayName" || pn == "Type")
                                    description = description ?? vs;
                                else if (pn == "Location" || pn == "ItemLocation") itemLocation = itemLocation ?? vs;
                            } catch { }
                        }
                    }
                    // Skip items with no useful info — PR often returns empty/placeholder slots that flood the list
                    if (string.IsNullOrEmpty(description) && string.IsNullOrEmpty(drugType)) continue;

                    records.Add(new VehicleSearchRecord {
                        LicensePlate = plate,
                        ItemType = itemType,
                        DrugType = drugType,
                        ItemLocation = itemLocation,
                        Description = description,
                        WeaponModelHash = weaponHash,
                        WeaponModelId = weaponModelId,
                        Source = "Vehicle search",
                        CapturedAt = now
                    });
                }
                if (records.Count > 0) {
                    // Replace previous records for this plate (prevents duplicate captures from poll + delayed + events)
                    Database.DeleteVehicleSearchRecordsByPlate(plate);
                    const int maxItemsPerCapture = 5;
                    var toSave = records.Count > maxItemsPerCapture ? records.Take(maxItemsPerCapture).ToList() : records;
                    Database.SaveVehicleSearchRecords(toSave);
                    lock (capturedVehicleSearchLock) {
                        capturedVehicleSearchPlates.Add(plate);
                        if (capturedVehicleSearchPlates.Count > MaxCapturedVehicleSearchPlates) {
                            var toRemove = capturedVehicleSearchPlates.Take(MaxCapturedVehicleSearchPlates / 2).ToList();
                            foreach (var p in toRemove) capturedVehicleSearchPlates.Remove(p);
                        }
                    }
                    Helper.Log($"Vehicle search captured {records.Count} item(s) for plate {plate}", false, Helper.LogSeverity.Info);
                }
                if (firearmRecords.Count > 0) {
                    Database.SaveFirearmRecords(firearmRecords);
                    Helper.Log($"[Firearm] Saved {firearmRecords.Count} firearm record(s) from vehicle search (plate {plate})", false, Helper.LogSeverity.Info);
                }
            } catch (Exception e) {
                Helper.Log($"CaptureVehicleSearchItems failed: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>Resolves driver/owner name for firearm owner when firearm is found in vehicle. Tries driver, then CDF vehicle owner, then "[Vehicle: PLATE]".</summary>
        private static string GetVehicleDriverNameForFirearmOwner(Vehicle vehicle, string plate) {
            if (vehicle == null || !vehicle.Exists() || string.IsNullOrEmpty(plate)) return null;
            try {
                Ped driver = vehicle.Driver;
                if (driver != null && driver.IsValid()) {
                    string name = (GetPedDataForPed(driver)?.Name ?? LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(driver)?.FullName)?.Trim();
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
                var vData = vehicle.GetVehicleData();
                if (vData?.Owner != null) {
                    string ownerName = vData.Owner.FullName?.Trim();
                    if (!string.IsNullOrWhiteSpace(ownerName)) return ownerName;
                }
            } catch { }
            return $"[Vehicle: {plate}]";
        }

        /// <summary>Gets in-game weapon display name from hash (matches what player sees). Uses GET_WEAPON_NAME_FROM_HASH + GetLabelText. Runs on game thread.</summary>
        private static string GetWeaponDisplayNameFromHash(uint weaponHash) {
            if (weaponHash == 0u) return null;
            try {
                string label = NativeFunction.Natives.GET_WEAPON_NAME_FROM_HASH<string>(weaponHash);
                if (string.IsNullOrWhiteSpace(label)) return null;
                return Game.GetLocalizedString(label);
            } catch {
                return null;
            }
        }

        /// <summary>Returns true if PR reports the ped's resistance action as something other than None (Flee, Attack, or Uncooperative). Only valid when PR is loaded.</summary>
        private static bool GetPedResistanceFromPR(Ped ped) {
            if (ped == null || !ped.IsValid() || !Main.usePR) return false;
            try {
                Type pedApiType = Type.GetType("PolicingRedefined.API.PedAPI, PolicingRedefined");
                if (pedApiType == null) return false;
                MethodInfo getResistance = pedApiType.GetMethod("GetPedResistanceAction", BindingFlags.Public | BindingFlags.Static);
                if (getResistance == null) return false;
                object result = getResistance.Invoke(null, new object[] { ped });
                if (result == null) return false;
                int value = Convert.ToInt32(result);
                return value != 0;
            } catch {
                return false;
            }
        }

        private static void PruneStaleEvidenceEntries() {
            DateTime threshold = DateTime.UtcNow.AddHours(-24);
            List<string> stale = pedEvidenceCache
                .Where(kvp => kvp.Value.CapturedAt < threshold)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (string key in stale) pedEvidenceCache.Remove(key);
            List<Rage.PoolHandle> staleHandles = fleeingPedHandles.Where(kvp => kvp.Value < threshold).Select(kvp => kvp.Key).ToList();
            foreach (Rage.PoolHandle h in staleHandles) fleeingPedHandles.Remove(h);
            staleHandles = damagedVehicleHandles.Where(kvp => kvp.Value < threshold).Select(kvp => kvp.Key).ToList();
            foreach (Rage.PoolHandle h in staleHandles) damagedVehicleHandles.Remove(h);
            staleHandles = assaultedPedHandles.Where(kvp => kvp.Value < threshold).Select(kvp => kvp.Key).ToList();
            foreach (Rage.PoolHandle h in staleHandles) assaultedPedHandles.Remove(h);
            staleHandles = hadWeaponHandles.Where(kvp => kvp.Value < threshold).Select(kvp => kvp.Key).ToList();
            foreach (Rage.PoolHandle h in staleHandles) hadWeaponHandles.Remove(h);
        }

        private static void BuildCourtCaseMetadata(CourtData courtData, string offenderPedName, Location reportLocation) {
            if (courtData == null) return;
            Config config = SetupController.GetConfig();

            string normalizedName = offenderPedName?.ToLower();
            MDTProPedData pedData = string.IsNullOrEmpty(normalizedName)
                ? null
                : pedDatabase.FirstOrDefault(p => p.Name?.ToLower() == normalizedName);

            courtData.PriorCitationCount = pedData?.Citations?.Count ?? 0;
            courtData.PriorArrestCount = pedData?.Arrests?.Count ?? 0;
            courtData.PriorConvictionCount = CountPriorConvictions(offenderPedName);

            int severity = GetSeverityScore(courtData);
            courtData.SeverityScore = severity;

            CourtDistrictProfile districtProfile = ResolveCourtDistrict(reportLocation);
            courtData.CourtDistrict = districtProfile.District;
            courtData.CourtName = districtProfile.CourtName;
            courtData.CourtType = districtProfile.CourtType;
            courtData.PolicyAdjustment = districtProfile.PolicyAdjustment;

            int repeatScore = GetRepeatOffenderScore(courtData, pedData, config);
            courtData.RepeatOffenderScore = repeatScore;
            courtData.EvidenceScore = GetEvidenceScore(courtData, config);
            courtData.DocketPressure = GetDocketPressure(districtProfile.District, config);

            bool hasLifeSentence = courtData.Charges.Any(c => c.Time == null);
            bool hasArrestableCharge = courtData.Charges?.Any(c => c.IsArrestable == true || (c.Time.HasValue && c.Time.Value > 0) || c.Time == null) == true;

            courtData.HasPublicDefender = pedData == null || courtData.RepeatOffenderScore < 7 || Helper.GetRandomInt(0, 100) < 70;
            courtData.ProsecutionStrength = GetProsecutionStrength(courtData, config);
            courtData.DefenseStrength = GetDefenseStrength(courtData, config);

            float outcomeMomentum = (courtData.ProsecutionStrength - courtData.DefenseStrength) / 100f;
            float repeatW = config.courtSentenceMultiplierRepeatWeight > 0 ? config.courtSentenceMultiplierRepeatWeight : 0.035f;
            float severityW = config.courtSentenceMultiplierSeverityWeight > 0 ? config.courtSentenceMultiplierSeverityWeight : 0.01f;
            float outcomeW = config.courtSentenceMultiplierOutcomeWeight > 0 ? config.courtSentenceMultiplierOutcomeWeight : 0.15f;
            float docketW = config.courtSentenceMultiplierDocketWeight > 0 ? config.courtSentenceMultiplierDocketWeight : 0.08f;
            float maxMult = config.courtSentenceMultiplierMax > 0 ? config.courtSentenceMultiplierMax : MaxSentenceMultiplier;
            courtData.SentenceMultiplier = Math.Min(
                maxMult,
                Math.Max(
                    1f,
                    1f
                    + (repeatScore * repeatW)
                    + (severity * severityW)
                    + (outcomeMomentum * outcomeW)
                    + (courtData.DocketPressure * docketW)
                    + courtData.PolicyAdjustment));

            int juryThreshold = config.courtJurySeverityThreshold > 0
                ? config.courtJurySeverityThreshold
                : DefaultJuryTrialSeverityThreshold;
            bool isSeriousCase = hasLifeSentence || severity >= juryThreshold;
            courtData.IsJuryTrial = hasArrestableCharge && isSeriousCase;
            courtData.JurySize = courtData.IsJuryTrial ? (severity >= 22 ? 12 : 6) : 0;

            int convictionChance = (int)Math.Round(
                40
                + (courtData.RepeatOffenderScore * 2.2f)
                + (severity * 1.8f)
                + (courtData.EvidenceScore * 0.35f)
                + (courtData.ProsecutionStrength * 0.4f)
                - (courtData.DefenseStrength * 0.3f)
                + (courtData.PolicyAdjustment * 100f));
            if (pedData?.IsWanted == true) convictionChance += 8;
            convictionChance = Math.Max(10, Math.Min(90, convictionChance));

            if (courtData.IsJuryTrial && courtData.JurySize > 0) {
                // Mean votes for conviction from conviction chance; add spread so we see more 9-3, 8-4 etc. instead of mostly 11-1
                float meanVotes = (convictionChance / 100f) * courtData.JurySize;
                int spread = Math.Max(1, courtData.JurySize / 3);
                int spreadAmount = Helper.GetRandomInt(-spread, spread);
                int votesForConviction = Math.Max(0, Math.Min(courtData.JurySize,
                    (int)Math.Round(meanVotes) + spreadAmount));
                courtData.JuryVotesForConviction = votesForConviction;
                courtData.JuryVotesForAcquittal = courtData.JurySize - votesForConviction;
            }
            courtData.ConvictionChance = convictionChance;

            courtData.Plea = courtData.RepeatOffenderScore >= 8
                ? "No Contest"
                : "Not Guilty";

            // Resolution time: 20 min (minor) to 5 hours (serious felony), scaled by severity
            float resolutionMinutes = Math.Max(
                config.courtCaseResolutionMinBase,
                Math.Min(
                    config.courtCaseResolutionMaxMinutes,
                    config.courtCaseResolutionMinBase + (severity * config.courtCaseResolutionSeverityScale)));
            // Guilty/No Contest plea fast-tracks — no contested trial needed
            if (courtData.Plea == "Guilty" || courtData.Plea == "No Contest")
                resolutionMinutes = Math.Max(config.courtCaseResolutionMinBase * 0.5f, resolutionMinutes * 0.4f);
            courtData.ResolveAtUtc = DateTime.UtcNow.AddMinutes(resolutionMinutes).ToString("o");

            courtData.JudgeName = SelectRotatingRosterMember(
                districtProfile.Judges,
                districtProfile.District,
                "judge",
                courtData.Number,
                courtData.SeverityScore);

            courtData.ProsecutorName = SelectRotatingRosterMember(
                districtProfile.Prosecutors,
                districtProfile.District,
                "prosecutor",
                courtData.Number,
                courtData.RepeatOffenderScore);

            courtData.DefenseAttorneyName = courtData.HasPublicDefender
                ? "Public Defender Office"
                : SelectRotatingRosterMember(
                    districtProfile.DefenseAttorneys.Skip(1).ToArray(),
                    districtProfile.District,
                    "defense",
                    courtData.Number,
                    courtData.RepeatOffenderScore + courtData.SeverityScore);

            courtData.CreatedAtUtc = DateTime.UtcNow.ToString("o");
            courtData.LastUpdatedUtc = courtData.CreatedAtUtc;
        }

        private static int GetRepeatOffenderScore(CourtData courtData, MDTProPedData pedData, Config config) {
            float score =
                (courtData.PriorCitationCount * config.courtPriorCitationWeight) +
                (courtData.PriorArrestCount * config.courtPriorArrestWeight) +
                (courtData.PriorConvictionCount * config.courtPriorConvictionWeight);

            if (pedData?.IsOnProbation == true) score += config.courtProbationWeight;
            if (pedData?.IsOnParole == true) score += config.courtParoleWeight;
            if (pedData?.IsWanted == true) score += config.courtWantedWeight;

            int recentConvictions = CountRecentConvictions(courtData.PedName, config.courtRecentConvictionWindowDays);
            score += recentConvictions * config.courtRecentConvictionBonusWeight;

            return Math.Max(0, (int)Math.Round(score));
        }

        /// <summary>Returns the report type for an attached report ID, or null if not found. Used for per-type evidence bonuses.</summary>
        private static string ResolveAttachedReportType(string reportId) {
            if (string.IsNullOrWhiteSpace(reportId)) return null;
            if (IncidentReports?.Any(r => r.Id == reportId) == true) return "incident";
            if (InjuryReports?.Any(r => r.Id == reportId) == true) return "injury";
            if (CitationReports?.Any(r => r.Id == reportId) == true) return "citation";
            if (TrafficIncidentReports?.Any(r => r.Id == reportId) == true) return "trafficIncident";
            if (ImpoundReports?.Any(r => r.Id == reportId) == true) return "impound";
            if (PropertyEvidenceReports?.Any(r => r.Id == reportId) == true) return "propertyEvidence";
            return null;
        }

        private static int GetEvidenceScore(CourtData courtData, Config config) {
            if (courtData?.Charges == null || courtData.Charges.Count == 0) return 0;

            float score = config.courtEvidenceBase;
            foreach (CourtData.Charge charge in courtData.Charges) {
                if (charge == null) continue;
                score += config.courtEvidencePerCharge;
                if (charge.IsArrestable == true) score += config.courtEvidenceArrestableBonus;
                if (charge.Time == null) score += config.courtEvidenceLifeSentenceBonus;
            }

            // Report-based evidence: relevant reports get full weight; other attached reports get a smaller bonus so tangential evidence (e.g. stolen firearm in a drug case) still counts but carries less weight
            if (courtData.AttachedReportIds != null && courtData.AttachedReportIds.Count > 0) {
                foreach (string reportId in courtData.AttachedReportIds) {
                    if (string.IsNullOrWhiteSpace(reportId)) continue;
                    string reportType = ResolveAttachedReportType(reportId);
                    if (string.IsNullOrEmpty(reportType)) continue;
                    bool relevant = IsAttachedReportRelevantToCase(courtData, reportId, reportType);
                    if (relevant) {
                        if (reportType == "incident" && config.courtEvidenceIncidentReportBonus > 0)
                            score += config.courtEvidenceIncidentReportBonus;
                        else if (reportType == "injury" && config.courtEvidenceInjuryReportBonus > 0)
                            score += config.courtEvidenceInjuryReportBonus;
                        else if (reportType == "citation" && config.courtEvidenceCitationReportBonus > 0)
                            score += config.courtEvidenceCitationReportBonus;
                        else if (reportType == "trafficIncident" && config.courtEvidenceTrafficIncidentReportBonus > 0)
                            score += config.courtEvidenceTrafficIncidentReportBonus;
                        else if (reportType == "impound" && config.courtEvidenceImpoundReportBonus > 0)
                            score += config.courtEvidenceImpoundReportBonus;
                        else if (reportType == "propertyEvidence") {
                            float seizureBonus = config.courtEvidenceSeizureReportBonus > 0 ? config.courtEvidenceSeizureReportBonus : config.courtEvidencePropertyEvidenceReportBonus;
                            if (seizureBonus > 0) score += seizureBonus;
                        }
                    } else if (config.courtEvidenceOtherAttachedReportBonus > 0) {
                        score += config.courtEvidenceOtherAttachedReportBonus;
                    }
                }
            }

            // Arrest report notes length bonus (primary report only)
            if (config.courtEvidenceReportNotesBonus > 0 && config.courtEvidenceReportNotesMinLength > 0 && !string.IsNullOrEmpty(courtData.ReportId)) {
                ArrestReport primaryArrest = arrestReports?.FirstOrDefault(r => r.Id == courtData.ReportId);
                if (primaryArrest != null && primaryArrest.Notes != null && primaryArrest.Notes.Length >= config.courtEvidenceReportNotesMinLength)
                    score += config.courtEvidenceReportNotesBonus;
            }

            if (!string.IsNullOrEmpty(courtData.PedName)) {
                ArrestReport primaryArrest = !string.IsNullOrEmpty(courtData.ReportId) ? arrestReports?.FirstOrDefault(r => r.Id == courtData.ReportId) : null;
                lock (pedEvidenceLock) {
                    if (pedEvidenceCache.TryGetValue(courtData.PedName, out PedEvidenceContext ctx)) {
                        if (ctx.HadWeapon) { score += config.courtEvidenceWeaponBonus; courtData.EvidenceHadWeapon = true; }
                        if (ctx.WasWanted) { score += config.courtEvidenceWantedBonus; courtData.EvidenceWasWanted = true; }
                        if (ctx.WasPatDown) { score += config.courtEvidencePatDownBonus; courtData.EvidenceWasPatDown = true; }
                        if (ctx.WasDrunk) { score += config.courtEvidenceDrunkBonus; courtData.EvidenceWasDrunk = true; }
                        if (ctx.WasFleeing) { score += config.courtEvidenceFleeingBonus; courtData.EvidenceWasFleeing = true; }
                        if (ctx.AssaultedPed) { score += config.courtEvidenceAssaultBonus; courtData.EvidenceAssaultedPed = true; }
                        if (ctx.DamagedVehicle) { score += config.courtEvidenceVehicleDamageBonus; courtData.EvidenceDamagedVehicle = true; }
                        if (ctx.HadIllegalWeapon) { score += config.courtEvidenceIllegalWeaponBonus; courtData.EvidenceIllegalWeapon = true; }
                        if (ctx.ViolatedSupervision) { score += config.courtEvidenceSupervisionViolationBonus; courtData.EvidenceViolatedSupervision = true; }
                        if (ctx.Resisted) { score += config.courtEvidenceResistedBonus; courtData.EvidenceResisted = true; }
                    }
                }
                if (courtData.EvidenceUseOfForce && config.courtEvidenceUseOfForceBonus > 0) {
                    score += config.courtEvidenceUseOfForceBonus;
                }
                // Infer Resisted from UseOfForce: tazer/baton/etc implies resistance when officer documented it
                if (courtData.EvidenceUseOfForce && !courtData.EvidenceResisted && config.courtEvidenceResistedBonus > 0) {
                    courtData.EvidenceResisted = true;
                    score += config.courtEvidenceResistedBonus;
                }
                // Infer Attempted to Flee from evading/pursuit charges when in-game capture missed (chase-then-stop clears fleeing state)
                if (!courtData.EvidenceWasFleeing && courtData.Charges != null && courtData.Charges.Any(c => c != null && IsEvadingOrFleeingChargeName(c.Name ?? ""))) {
                    courtData.EvidenceWasFleeing = true;
                    if (config.courtEvidenceFleeingBonus > 0) score += config.courtEvidenceFleeingBonus;
                }
                // Do NOT infer vehicle damage from charges: evading can be on foot, hit-and-run may lack collision evidence. Only use PedEvidenceContext or documented report evidence.
                // Drug evidence: charge-specific from seizure reports, else drug_records, else DocumentedDrugs (backward compat).
                // Add bonus once if ANY drug charge is satisfied by seizure OR drug_records OR DocumentedDrugs.
                if (config.courtEvidenceDrugsBonus > 0) {
                    var drugTypesFromSeizure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    bool satisfiedBySeizure = false;
                    if (courtData.AttachedReportIds != null && courtData.Charges != null) {
                        foreach (string rid in courtData.AttachedReportIds) {
                            if (string.IsNullOrWhiteSpace(rid)) continue;
                            var per = PropertyEvidenceReports?.FirstOrDefault(r => r.Id == rid);
                            if (per == null || per.SeizedDrugTypes == null || per.SeizedDrugTypes.Count == 0) continue;
                            if (!IsAttachedReportRelevantToCase(courtData, rid, "propertyEvidence")) continue;
                            foreach (CourtData.Charge ch in courtData.Charges) {
                                if (ch == null || !IsDrugRelatedChargeName(ch.Name ?? "")) continue;
                                if (SeizureEvidenceHelper.ChargeSatisfiedBySeizedDrugs(ch.Name, per.SeizedDrugTypes)) {
                                    satisfiedBySeizure = true;
                                    foreach (string dt in per.SeizedDrugTypes) if (!string.IsNullOrEmpty(dt)) drugTypesFromSeizure.Add(dt);
                                }
                            }
                        }
                    }
                    if (satisfiedBySeizure) {
                        score += config.courtEvidenceDrugsBonus;
                        courtData.EvidenceHadDrugs = true;
                        courtData.EvidenceDrugTypesBreakdown = drugTypesFromSeizure.Count > 0 ? drugTypesFromSeizure.ToList() : null;
                        // Quantity bonus: when drug evidence comes from seizure report with SeizedDrugs (drug + quantity), add extra based on quantity weight
                        if (config.courtEvidenceDrugQuantityBonus > 0 && courtData.AttachedReportIds != null) {
                            foreach (string rid in courtData.AttachedReportIds) {
                                if (string.IsNullOrWhiteSpace(rid)) continue;
                                var per = PropertyEvidenceReports?.FirstOrDefault(r => r.Id == rid);
                                if (per?.SeizedDrugs == null || per.SeizedDrugs.Count == 0) continue;
                                if (!IsAttachedReportRelevantToCase(courtData, rid, "propertyEvidence")) continue;
                                float qWeight = SeizureEvidenceHelper.GetTotalQuantityWeight(per.SeizedDrugs);
                                score += config.courtEvidenceDrugQuantityBonus * Math.Min(1f, qWeight);
                                break; // apply once per case
                            }
                        }
                    } else {
                        var drugs = Database.LoadDrugsByOwner(courtData.PedName, 1);
                        if (drugs != null && drugs.Count > 0) {
                            score += config.courtEvidenceDrugsBonus;
                            courtData.EvidenceHadDrugs = true;
                        } else if (primaryArrest != null && primaryArrest.DocumentedDrugs) {
                            score += config.courtEvidenceDrugsBonus;
                            courtData.EvidenceHadDrugs = true;
                        }
                    }
                }
                // Firearms: DocumentedFirearms (backward compat) first, else from attached seizure reports (must have firearm charge AND seizure lists at least one firearm)
                if (primaryArrest != null && primaryArrest.DocumentedFirearms) {
                    bool hadWeaponAlready = courtData.EvidenceHadWeapon;
                    bool hadIllegalAlready = courtData.EvidenceIllegalWeapon;
                    courtData.EvidenceHadWeapon = true;
                    courtData.EvidenceIllegalWeapon = true;
                    if (!hadWeaponAlready && config.courtEvidenceWeaponBonus > 0) score += config.courtEvidenceWeaponBonus;
                    if (!hadIllegalAlready && config.courtEvidenceIllegalWeaponBonus > 0) score += config.courtEvidenceIllegalWeaponBonus;
                } else if (!courtData.EvidenceHadWeapon && IsCaseFirearmRelated(courtData) && courtData.AttachedReportIds != null) {
                    foreach (string rid in courtData.AttachedReportIds) {
                        if (string.IsNullOrWhiteSpace(rid)) continue;
                        var per = PropertyEvidenceReports?.FirstOrDefault(r => r.Id == rid);
                        if (per == null || per.SeizedFirearmTypes == null || per.SeizedFirearmTypes.Count == 0) continue;
                        if (!IsAttachedReportRelevantToCase(courtData, rid, "propertyEvidence")) continue;
                        if (SeizureEvidenceHelper.IsFirearmChargeSatisfiedBySeizedFirearms(true, per.SeizedFirearmTypes)) {
                            courtData.EvidenceHadWeapon = true;
                            courtData.EvidenceIllegalWeapon = true;
                            courtData.EvidenceFirearmTypesBreakdown = per.SeizedFirearmTypes.Where(s => !string.IsNullOrEmpty(s)).ToList();
                            if (config.courtEvidenceWeaponBonus > 0) score += config.courtEvidenceWeaponBonus;
                            if (config.courtEvidenceIllegalWeaponBonus > 0) score += config.courtEvidenceIllegalWeaponBonus;
                            break;
                        }
                    }
                }
                // Fallback: MDT is authoritative for warrants; if cache didn't set EvidenceWasWanted, set from ped record (both pedDatabase and keepInPedDatabase)
                if (!courtData.EvidenceWasWanted) {
                    MDTProPedData pedData = GetPedDataByName(courtData.PedName);
                    if (pedData != null && (pedData.IsWanted || !string.IsNullOrWhiteSpace(pedData.WarrantText))) {
                        score += config.courtEvidenceWantedBonus;
                        courtData.EvidenceWasWanted = true;
                    }
                }
            }

            float max = config.courtEvidenceMax > 0 ? config.courtEvidenceMax : 95f;
            return (int)Math.Round(Math.Max(0f, Math.Min(max, score)));
        }

        private static float GetDocketPressure(string district, Config config) {
            int window = Math.Max(1, config.courtDocketWindowDays);
            DateTime now = DateTime.UtcNow;
            DateTime start = now.AddDays(-window);

            int recentCasesInDistrict = courtDatabase.Count(c =>
                c != null &&
                string.Equals(c.CourtDistrict, district, StringComparison.OrdinalIgnoreCase) &&
                TryGetCaseTimestamp(c, out DateTime ts) &&
                ts >= start &&
                ts <= now);

            float baseline = Math.Max(0f, config.courtDocketPressureBase);
            float scale = Math.Max(0f, config.courtDocketPressureScale);
            float normalized = Math.Min(1f, recentCasesInDistrict / 25f);
            return Math.Max(0f, Math.Min(1f, baseline + (normalized * scale)));
        }

        private static float GetProsecutionStrength(CourtData courtData, Config config) {
            float severityPart = courtData.SeverityScore * config.courtProsecutionSeverityWeight;
            float evidencePart = courtData.EvidenceScore * config.courtProsecutionEvidenceWeight;
            float recidivismPart = courtData.RepeatOffenderScore * config.courtProsecutionRecidivismWeight;
            return Math.Max(0f, Math.Min(100f, severityPart + evidencePart + recidivismPart));
        }

        private static float GetDefenseStrength(CourtData courtData, Config config) {
            float baseStrength = courtData.HasPublicDefender
                ? config.courtDefensePublicDefenderBonus
                : config.courtDefensePrivateCounselBonus;

            float pressureMitigation = (1f - courtData.DocketPressure) * 12f;
            float complexityMitigation = Math.Min(30f, courtData.SeverityScore * 1.2f);

            return Math.Max(0f, Math.Min(100f, baseStrength + pressureMitigation + complexityMitigation));
        }

        private static bool TryGetCaseTimestamp(CourtData courtCase, out DateTime timestamp) {
            timestamp = DateTime.MinValue;
            if (courtCase == null) return false;

            string value = courtCase.CreatedAtUtc;
            if (string.IsNullOrEmpty(value)) value = courtCase.LastUpdatedUtc;
            if (string.IsNullOrEmpty(value)) return false;

            return DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out timestamp);
        }

        private static int CountRecentConvictions(string pedName, float recentWindowDays) {
            if (string.IsNullOrEmpty(pedName) || recentWindowDays <= 0f) return 0;

            DateTime threshold = DateTime.UtcNow.AddDays(-recentWindowDays);
            string normalized = pedName.ToLower();

            return courtDatabase.Count(c =>
                c != null &&
                c.Status == 1 &&
                c.PedName?.ToLower() == normalized &&
                TryGetCaseTimestamp(c, out DateTime ts) &&
                ts >= threshold);
        }

        private static int GetEvidenceBand(CourtData courtData) {
            int score = courtData?.EvidenceScore ?? 0;
            if (score < 35) return 0; // low
            if (score < 60) return 1; // medium
            return 2; // high
        }

        private static bool HasChargeKeyword(CourtData courtData, string keyword) {
            if (courtData?.Charges == null) return false;
            string k = keyword.ToLowerInvariant();
            return courtData.Charges.Any(c => (c.Name ?? "").ToLowerInvariant().Contains(k));
        }

        /// <summary>Evidence tag names used to weight outcome templates. Must match the checks in CountMatchingEvidenceTags. Homicide/SexOffense/Kidnapping/Arson are charge-based.</summary>
        private static readonly string[] EvidenceTagNames = new[] { "Homicide", "SexOffense", "Kidnapping", "Arson", "Weapon", "Wanted", "PatDown", "Drunk", "Fleeing", "Assault", "VehicleDamage", "IllegalWeapon", "Supervision", "Resisted", "Drugs", "UseOfForce" };

        private static bool HasSexOffenseCharge(CourtData courtData) {
            if (courtData?.Charges == null) return false;
            return courtData.Charges.Any(c => {
                string n = (c?.Name ?? "").ToLowerInvariant();
                return n.Contains("rape") || n.Contains("sexual assault") || n.Contains("sexual battery")
                    || n.Contains("prostitut") || n.Contains("lewd conduct") || n.Contains("indecent exposure")
                    || n.Contains("failure to register as sex offender");
            });
        }

        private static bool HasKidnappingCharge(CourtData courtData) {
            return HasChargeKeyword(courtData, "kidnapping");
        }

        private static bool HasArsonCharge(CourtData courtData) {
            return HasChargeKeyword(courtData, "arson") || HasChargeKeyword(courtData, "unlawful burning") || HasChargeKeyword(courtData, "reckless burning");
        }

        /// <summary>True when drug/narcotic charges apply (avoids matching firearm/stolen "possession").</summary>
        private static bool HasDrugCrimeCharge(CourtData courtData) {
            if (courtData?.Charges == null) return false;
            return courtData.Charges.Any(c => {
                string n = (c?.Name ?? "").ToLowerInvariant();
                return n.Contains("paraphernalia") || n.Contains("controlled substance") || n.Contains("trafficking")
                    || n.Contains("manufacturing meth") || n.Contains("transport or sale of meth") || n.Contains("sale or transport of cannabis")
                    || n.Contains("cannabis over") || n.Contains("under influence of controlled")
                    || n.Contains("cocaine") || n.Contains("heroin") || n.Contains("fentanyl") || n.Contains("methamphetamine")
                    || (n.Contains("amphetamine") && !n.Contains("methamphetamine"))
                    || n.Contains("benzodiazepine") || n.Contains("hallucinogen") || n.Contains("ecstasy") || n.Contains("mdma") || n.Contains("pcp");
            });
        }

        private static int CountMatchingEvidenceTags(CourtData courtData, string[] tags) {
            if (courtData == null || tags == null || tags.Length == 0) return 0;
            int count = 0;
            foreach (string tag in tags) {
                if (tag == "Homicide" && (HasChargeKeyword(courtData, "murder") || HasChargeKeyword(courtData, "manslaughter"))) count++;
                else if (tag == "SexOffense" && HasSexOffenseCharge(courtData)) count++;
                else if (tag == "Kidnapping" && HasKidnappingCharge(courtData)) count++;
                else if (tag == "Arson" && HasArsonCharge(courtData)) count++;
                else if (tag == "Weapon" && courtData.EvidenceHadWeapon) count++;
                else if (tag == "Wanted" && courtData.EvidenceWasWanted) count++;
                else if (tag == "PatDown" && courtData.EvidenceWasPatDown) count++;
                else if (tag == "Drunk" && courtData.EvidenceWasDrunk) count++;
                else if (tag == "Fleeing" && courtData.EvidenceWasFleeing) count++;
                else if (tag == "Assault" && courtData.EvidenceAssaultedPed) count++;
                else if (tag == "VehicleDamage" && courtData.EvidenceDamagedVehicle) count++;
                else if (tag == "IllegalWeapon" && courtData.EvidenceIllegalWeapon) count++;
                else if (tag == "Supervision" && courtData.EvidenceViolatedSupervision) count++;
                else if (tag == "Resisted" && courtData.EvidenceResisted) count++;
                else if (tag == "Drugs" && courtData.EvidenceHadDrugs) count++;
                else if (tag == "UseOfForce" && courtData.EvidenceUseOfForce) count++;
            }
            return count;
        }

        /// <summary>True if template's evidence tags are all satisfied. Never use evidence-specific wording when that evidence is absent.</summary>
        private static bool TemplateEvidenceSatisfied(CourtData courtData, string[] tags) {
            if (tags == null || tags.Length == 0) return true;
            foreach (string tag in tags) {
                if (tag == "Homicide" && !(HasChargeKeyword(courtData, "murder") || HasChargeKeyword(courtData, "manslaughter"))) return false;
                if (tag == "SexOffense" && !HasSexOffenseCharge(courtData)) return false;
                if (tag == "Kidnapping" && !HasKidnappingCharge(courtData)) return false;
                if (tag == "Arson" && !HasArsonCharge(courtData)) return false;
                if (tag == "Weapon" && !courtData.EvidenceHadWeapon) return false;
                if (tag == "Wanted" && !courtData.EvidenceWasWanted) return false;
                if (tag == "PatDown" && !courtData.EvidenceWasPatDown) return false;
                if (tag == "Drunk" && !courtData.EvidenceWasDrunk) return false;
                if (tag == "Fleeing" && !courtData.EvidenceWasFleeing) return false;
                if (tag == "Assault" && !courtData.EvidenceAssaultedPed) return false;
                if (tag == "VehicleDamage" && !courtData.EvidenceDamagedVehicle) return false;
                if (tag == "IllegalWeapon" && !courtData.EvidenceIllegalWeapon) return false;
                if (tag == "Supervision" && !courtData.EvidenceViolatedSupervision) return false;
                if (tag == "Resisted" && !courtData.EvidenceResisted) return false;
                if (tag == "Drugs" && !courtData.EvidenceHadDrugs) return false;
                if (tag == "UseOfForce" && !courtData.EvidenceUseOfForce) return false;
            }
            return true;
        }

        private static string SelectWeightedOutcome(CourtData courtData, (string text, string[] evidenceTags)[] pool) {
            if (pool == null || pool.Length == 0) return "";
            var eligible = new List<(string text, string[] tags, int weight)>();
            foreach (var entry in pool) {
                if (!TemplateEvidenceSatisfied(courtData, entry.evidenceTags)) continue;
                int w = 1 + (CountMatchingEvidenceTags(courtData, entry.evidenceTags) * 18);
                eligible.Add((entry.text, entry.evidenceTags, Math.Max(1, w)));
            }
            if (eligible.Count == 0) {
                var generic = pool.FirstOrDefault(e => e.evidenceTags == null || e.evidenceTags.Length == 0);
                return generic.text ?? (pool.Length > 0 ? pool[0].text : "");
            }
            int total = eligible.Sum(e => e.weight);
            if (total <= 0) return eligible[0].text;
            int r = Helper.GetRandomInt(0, Math.Max(0, total - 1));
            foreach (var e in eligible) {
                r -= e.weight;
                if (r < 0) return e.text;
            }
            return eligible[eligible.Count - 1].text;
        }

        private static string BuildOutcomeReasoning(CourtData courtData, int convictionChance, int resolvedStatus) {
            if (courtData == null) return "";
            var b = new StringBuilder();
            string tribunal = courtData.IsJuryTrial ? "jury" : "court";
            int evidenceBand = GetEvidenceBand(courtData);
            string pleaNorm = string.IsNullOrWhiteSpace(courtData.Plea) ? "Not Guilty" : courtData.Plea.Trim();
            bool mismatchAcquittal = resolvedStatus == 2 && convictionChance >= 65;
            bool mismatchGuilty = resolvedStatus == 1
                && !string.Equals(pleaNorm, "Guilty", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pleaNorm, "No Contest", StringComparison.OrdinalIgnoreCase)
                && convictionChance <= 45 && evidenceBand == 0;

            if (resolvedStatus == 1) {
                if (string.Equals(pleaNorm, "Guilty", StringComparison.OrdinalIgnoreCase)) {
                    string[] guiltyPlea = new[] {
                        "The defendant entered a guilty plea. The court accepted the plea and proceeded directly to sentencing.",
                        "The defendant pleaded guilty to all charges. Sentencing followed immediately.",
                        "The defendant voluntarily entered a guilty plea. The court accepted the plea and proceeded to sentencing without trial.",
                        "The defendant entered a guilty plea as part of a negotiated agreement. The court accepted the plea and proceeded to sentencing.",
                        "Following plea negotiations, the defendant pleaded guilty. The court accepted the plea and imposed sentence.",
                        "The defendant entered a guilty plea. In accepting the plea, the court noted the defendant's acceptance of responsibility.",
                        "The defendant pleaded guilty and waived trial. The court accepted the waiver and proceeded directly to sentencing.",
                        "The defendant entered a guilty plea before trial. The court accepted the plea, finding it knowing and voluntary.",
                        "The defendant entered a guilty plea pursuant to a plea bargain. The court approved the agreement and imposed the agreed sentence.",
                        "The defendant voluntarily pleaded guilty, accepting responsibility for the charges. The court accepted the plea and proceeded to sentencing.",
                        "Having entered a guilty plea, the defendant waived the right to trial. The court accepted the plea and imposed sentence.",
                        "The defendant entered a guilty plea. The court, after conducting a plea colloquy, accepted the plea and sentenced the defendant.",
                        "The defendant pleaded guilty to the charged offences. The court noted the defendant's acceptance of responsibility and proceeded to sentencing.",
                        "Following counseled plea negotiations, the defendant entered a guilty plea. The court accepted the plea and imposed sentence.",
                        "The defendant entered a guilty plea. The court accepted the plea after establishing that it was made knowingly, voluntarily, and intelligently.",
                        "The defendant pleaded guilty and proceeded directly to sentencing without a trial. The court accepted the plea.",
                        "The defendant entered a guilty plea as part of a negotiated disposition. The court accepted the plea and sentenced accordingly.",
                        "The defendant waived trial and entered a guilty plea. The court accepted the waiver and proceeded to sentencing.",
                        "The defendant entered a guilty plea. The presiding judge accepted the plea, noting the defendant's acceptance of responsibility.",
                        "The defendant pleaded guilty to all counts. The court accepted the plea and imposed sentence without further proceedings.",
                        "The defendant entered a guilty plea after discussions with counsel. The court accepted the plea and proceeded to sentencing.",
                        "The defendant voluntarily pleaded guilty. The court accepted the plea, finding it supported by a factual basis.",
                        "The defendant entered a guilty plea pursuant to a plea agreement. The court accepted the agreement and imposed the negotiated sentence.",
                        "The defendant pleaded guilty and waived the right to a jury trial. The court accepted the plea and sentenced the defendant.",
                        "The defendant entered a guilty plea. The court, satisfied that the plea was knowing and voluntary, accepted it and imposed sentence.",
                        "The defendant entered a guilty plea. The court noted the defendant's allocution and acceptance of responsibility before imposing sentence.",
                    };
                    b.Append(guiltyPlea[Helper.GetRandomInt(0, guiltyPlea.Length - 1)]);
                } else if (string.Equals(pleaNorm, "No Contest", StringComparison.OrdinalIgnoreCase)) {
                    string[] noContestPlea = new[] {
                        "The defendant entered a no contest plea, neither admitting nor denying the charges. The court accepted the plea and returned a guilty verdict.",
                        "The court accepted the defendant's nolo contendere plea and entered a finding of guilty.",
                        "The defendant entered a no contest plea. The court treated the plea as an admission for sentencing purposes and returned a guilty verdict.",
                        "The defendant entered a no contest plea following discussions with counsel. The court accepted the plea and imposed sentence.",
                        "The defendant entered a plea of nolo contendere. The court accepted the plea and entered a finding of guilty for sentencing.",
                        "The defendant pleaded no contest, neither admitting nor denying guilt. The court accepted the plea and imposed sentence.",
                        "The defendant entered a no contest plea. The court treated the plea as a conviction for all purposes and proceeded to sentencing.",
                        "The defendant entered a nolo contendere plea. The court accepted the plea, entered a finding of guilty, and proceeded to sentencing.",
                        "The defendant entered a no contest plea. Neither admitting nor denying the charges, the defendant consented to a finding of guilty for sentencing.",
                        "The court accepted the defendant's no contest plea. The plea was treated as an admission for sentencing and a guilty verdict was entered.",
                        "The defendant entered a no contest plea. The court found the plea voluntary and entered a conviction for sentencing purposes.",
                        "The defendant pleaded nolo contendere to the charges. The court accepted the plea and returned a finding of guilty.",
                        "The defendant entered a no contest plea. The court, having accepted the plea, treated it as a guilty plea for sentencing and imposed sentence.",
                        "The defendant entered a no contest plea without admitting or denying the allegations. The court accepted the plea and entered a guilty verdict.",
                        "The defendant entered a plea of no contest. The court accepted the plea and, treating it as equivalent to a guilty plea for sentencing, imposed sentence.",
                    };
                    b.Append(noContestPlea[Helper.GetRandomInt(0, noContestPlea.Length - 1)]);
                } else if (mismatchGuilty) {
                    string[] lowEvidenceGuilty = new[] {
                        "Despite limited physical evidence, the {0} found the defendant guilty, relying heavily on witness testimony and the defendant's conduct at the scene.",
                        "In a case that hinged on credibility, the {0} found the defendant guilty based on the weight of officer testimony and circumstantial evidence.",
                        "The {0} returned a guilty verdict. Although the evidence was circumstantial, witness credibility and the defendant's statements supported the conviction.",
                        "The {0} found the defendant guilty. The prosecution's case, while not overwhelming, was sufficient to establish guilt beyond a reasonable doubt.",
                        "The {0} returned a guilty verdict. Circumstantial evidence and officer testimony, while limited, were deemed sufficient to establish guilt beyond a reasonable doubt.",
                        "Despite the lack of physical evidence, the {0} found the defendant guilty. Credibility determinations favoured the prosecution's witnesses.",
                        "The {0} found the defendant guilty based on circumstantial evidence and witness testimony. The evidence, though not abundant, was sufficient to meet the burden of proof.",
                        "In a case relying primarily on officer testimony, the {0} returned a guilty verdict. Credibility was resolved in favour of the prosecution.",
                        "The {0} returned a guilty verdict. Limited physical evidence was supplemented by credible witness testimony sufficient to establish guilt beyond a reasonable doubt.",
                        "The {0} found the defendant guilty. Circumstantial evidence and the defendant's own statements, together with officer testimony, supported the conviction.",
                        "Despite limited corroborating evidence, the {0} found the defendant guilty. Witness credibility and circumstantial indicators established guilt beyond a reasonable doubt.",
                        "The {0} returned a guilty verdict. Officer testimony and circumstantial evidence, though not overwhelming, were deemed sufficient to prove guilt beyond a reasonable doubt.",
                        "The {0} found the defendant guilty. The prosecution's case, resting largely on credibility determinations, met the burden of proof despite limited physical evidence.",
                        "In a case that turned on credibility, the {0} returned a guilty verdict. Circumstantial evidence and witness testimony were sufficient to establish guilt beyond a reasonable doubt.",
                        "The {0} returned a guilty verdict. Although physical evidence was sparse, the weight of officer testimony and circumstantial evidence supported the conviction.",
                        "The {0} found the defendant guilty. Credibility determinations favoured the prosecution's witnesses, and the circumstantial evidence was sufficient to establish guilt beyond a reasonable doubt.",
                    };
                    b.AppendFormat(lowEvidenceGuilty[Helper.GetRandomInt(0, lowEvidenceGuilty.Length - 1)], tribunal);
                } else {
                    var guiltyTrialPool = new (string text, string[] evidenceTags)[] {
                        ("The {0} returned a guilty verdict. The prosecution built an overwhelming case against the defendant.", null),
                        ("After deliberation, the {0} found the defendant guilty.", null),
                        ("In a closely contested case, the {0} ultimately returned a guilty verdict.", null),
                        ("The {0} found the defendant guilty. The weight of the evidence left no reasonable doubt.", null),
                        ("The {0} returned a guilty verdict based on the strength of the testimony and physical evidence.", null),
                        ("The {0} found the defendant guilty. Witness testimony and documentary evidence supported the charges.", null),
                        ("The {0} returned a guilty verdict. The prosecution met its burden of proof.", null),
                        ("After considering the evidence, the {0} found the defendant guilty on all counts.", null),
                        ("The {0} concluded that the defendant's guilt was established beyond a reasonable doubt.", null),
                        ("The {0} returned a guilty verdict. The defendant's own statements and the evidence presented were consistent with guilt.", null),
                        ("The {0} returned a guilty verdict. The prosecution presented testimony and physical evidence that convinced the {0}.", null),
                        ("The {0} found the defendant guilty. The evidence was overwhelming and left no room for reasonable doubt.", null),
                        ("The {0} returned a guilty verdict after weighing the totality of the evidence presented at trial.", null),
                        ("The {0} found the defendant guilty. The prosecution established each element of the offence beyond reasonable doubt.", null),
                        ("The {0} returned a guilty verdict. Officer testimony and corroborating evidence supported conviction.", null),
                        ("The {0} found the defendant guilty. The case against the defendant was compelling and well-supported.", null),
                        ("The {0} returned a guilty verdict. The defendant's conduct at the scene and subsequent admissions supported guilt.", null),
                        ("The {0} found the defendant guilty. Documentary evidence and witness accounts aligned with the prosecution's theory.", null),
                        ("The {0} returned a guilty verdict. The prosecution's case was thorough and left no reasonable doubt as to guilt.", null),
                        ("The {0} found the defendant guilty. Physical evidence and testimony established the defendant's involvement.", null),
                        ("The {0} returned a guilty verdict. The weight of the evidence clearly pointed to the defendant's guilt.", null),
                        ("The {0} found the defendant guilty. The prosecution met its burden; the defence failed to raise sufficient doubt.", null),
                        ("The {0} returned a guilty verdict. Expert testimony and physical evidence supported the charges.", null),
                        ("The {0} found the defendant guilty. The totality of circumstances established guilt beyond a reasonable doubt.", null),
                        ("The {0} returned a guilty verdict. Eyewitness identification and corroborating evidence were persuasive.", null),
                        ("The {0} found the defendant guilty. The prosecution proved each charge to the required standard.", null),
                        ("The {0} returned a guilty verdict. The defendant was caught in the act and the evidence was conclusive.", null),
                        ("The {0} found the defendant guilty. No reasonable doubt remained after consideration of all evidence.", null),
                        ("The {0} returned a guilty verdict. The defendant's presence at the scene and incriminating conduct supported conviction.", null),
                        ("The {0} found the defendant guilty. The prosecution presented a coherent and convincing case.", null),
                        ("The {0} returned a guilty verdict. The defendant was found guilty based on the strength of the prosecution's evidence.", null),
                        ("The {0} found the defendant guilty. Evidence that the defendant was armed at the time of arrest strongly supported the prosecution's case.", new[] { "Weapon" }),
                        ("The {0} returned a guilty verdict. The defendant was armed when taken into custody, a fact the {0} found significant.", new[] { "Weapon" }),
                        ("The {0} found the defendant guilty. Possession of a weapon at the time of arrest was cited as evidence of intent and capability.", new[] { "Weapon" }),
                        ("The {0} returned a guilty verdict. The defendant was carrying a weapon when apprehended; the {0} considered this probative of guilt.", new[] { "Weapon" }),
                        ("The {0} found the defendant guilty. Discovery of a weapon during arrest reinforced the prosecution's case and demonstrated dangerous conduct.", new[] { "Weapon" }),
                        ("The {0} returned a guilty verdict. The defendant's being armed at the scene was presented as strong evidence of intent.", new[] { "Weapon" }),
                        ("The {0} returned a guilty verdict. An active warrant was outstanding; the circumstances of arrest left little room for doubt.", new[] { "Wanted" }),
                        ("The {0} found the defendant guilty. The existence of an active warrant and the manner of apprehension supported the prosecution.", new[] { "Wanted" }),
                        ("The {0} returned a guilty verdict. The defendant was wanted on outstanding warrants; the {0} considered this in reaching its decision.", new[] { "Wanted" }),
                        ("The {0} found the defendant guilty. An active warrant demonstrated the defendant's fugitive status and supported the prosecution's narrative.", new[] { "Wanted" }),
                        ("The {0} returned a guilty verdict. The defendant's attempt to flee was cited as evidence of consciousness of guilt.", new[] { "Fleeing" }),
                        ("The {0} found the defendant guilty. Flight from law enforcement was presented as evidence of guilt and weighed by the {0}.", new[] { "Fleeing" }),
                        ("The {0} returned a guilty verdict. The defendant fled when approached by officers; the {0} inferred consciousness of guilt.", new[] { "Fleeing" }),
                        ("The {0} found the defendant guilty. Attempted flight was cited as demonstrative of the defendant's knowledge of wrongdoing.", new[] { "Fleeing" }),
                        ("The {0} returned a guilty verdict. The defendant resisted arrest; that conduct was considered alongside the underlying charges.", new[] { "Resisted" }),
                        ("The {0} found the defendant guilty. Resistance at the time of arrest was cited as supporting the prosecution's narrative.", new[] { "Resisted" }),
                        ("The {0} returned a guilty verdict. The defendant's resistance during apprehension was presented as evidence of guilt.", new[] { "Resisted" }),
                        ("The {0} found the defendant guilty. Resisting arrest demonstrated consciousness of guilt and was weighed by the {0}.", new[] { "Resisted" }),
                        ("The {0} returned a guilty verdict. The defendant physically resisted officers; this conduct supported the charges.", new[] { "Resisted" }),
                        ("The {0} returned a guilty verdict. Evidence of assault during the incident reinforced the charges.", new[] { "Assault" }),
                        ("The {0} found the defendant guilty. The defendant's assaultive conduct was a key factor in the verdict.", new[] { "Assault" }),
                        ("The {0} returned a guilty verdict. Assault during the incident was presented and the {0} found it established beyond reasonable doubt.", new[] { "Assault" }),
                        ("The {0} found the defendant guilty. The assaultive conduct toward the victim was compelling evidence of intent.", new[] { "Assault" }),
                        ("The {0} returned a guilty verdict. Visible intoxication at the time of the offence was a key factor.", new[] { "Drunk" }),
                        ("The {0} found the defendant guilty. The defendant was visibly intoxicated when apprehended, which the {0} considered in reaching its verdict.", new[] { "Drunk" }),
                        ("The {0} returned a guilty verdict. Signs of impairment were observed and documented; the {0} found this evidence persuasive.", new[] { "Drunk" }),
                        ("The {0} found the defendant guilty. The defendant's intoxicated state at the time of the offence supported the charges.", new[] { "Drunk" }),
                        ("The {0} returned a guilty verdict. Field sobriety and intoxication evidence were consistent with guilt.", new[] { "Drunk" }),
                        ("The {0} returned a guilty verdict. The defendant's violation of probation or parole conditions was taken into account.", new[] { "Supervision" }),
                        ("The {0} found the defendant guilty. Supervision violations were presented and weighed in the {0}'s decision.", new[] { "Supervision" }),
                        ("The {0} returned a guilty verdict. The defendant was on probation or parole at the time; the {0} considered this aggravating.", new[] { "Supervision" }),
                        ("The {0} found the defendant guilty. Breach of supervision conditions was cited as evidence of disregard for court orders.", new[] { "Supervision" }),
                        ("The {0} returned a guilty verdict. Evidence of vehicle damage was consistent with the charges and supported conviction.", new[] { "VehicleDamage" }),
                        ("The {0} found the defendant guilty. Damage to the vehicle was documented and presented as evidence of the offence.", new[] { "VehicleDamage" }),
                        ("The {0} returned a guilty verdict. Physical damage to the vehicle supported the prosecution's account of the incident.", new[] { "VehicleDamage" }),
                        ("The {0} found the defendant guilty. The possession of an illegal weapon significantly strengthened the prosecution's case.", new[] { "IllegalWeapon" }),
                        ("The {0} returned a guilty verdict. An illegal weapon was recovered; the {0} found this dispositive of the charges.", new[] { "IllegalWeapon" }),
                        ("The {0} found the defendant guilty. Recovery of an illegal weapon was central to the prosecution's case.", new[] { "IllegalWeapon" }),
                        ("The {0} returned a guilty verdict. The illegal weapon recovered at the scene was cited as conclusive evidence.", new[] { "IllegalWeapon" }),
                        ("The {0} returned a guilty verdict. Recovery of controlled substances during arrest supported the charges.", new[] { "Drugs" }),
                        ("The {0} found the defendant guilty. Drugs were seized during the arrest; the {0} found this evidence compelling.", new[] { "Drugs" }),
                        ("The {0} returned a guilty verdict. Controlled substances were found on the defendant's person; possession was established.", new[] { "Drugs" }),
                        ("The {0} found the defendant guilty. The seizure of narcotics during arrest strongly supported the prosecution.", new[] { "Drugs" }),
                        ("The {0} found the defendant guilty. A lawful pat-down and subsequent discovery of evidence was presented at trial.", new[] { "PatDown" }),
                        ("The {0} returned a guilty verdict. Evidence discovered during a lawful pat-down was admitted and supported conviction.", new[] { "PatDown" }),
                        ("The {0} found the defendant guilty. A valid pat-down search yielded incriminating evidence; the {0} found the search lawful.", new[] { "PatDown" }),
                        ("The {0} returned a guilty verdict. Documentation of the incident, including use of force, was found to support the prosecution.", new[] { "UseOfForce" }),
                        ("The {0} found the defendant guilty. Use of force reports and body-worn camera footage supported the prosecution's account.", new[] { "UseOfForce" }),
                        ("The {0} returned a guilty verdict. Documentation of force used during arrest was consistent with the charges and admissible.", new[] { "UseOfForce" }),
                        ("The {0} found the defendant guilty. Multiple factors—including the defendant's conduct at arrest and the evidence gathered—supported the verdict.", new[] { "Resisted", "Weapon" }),
                        ("The {0} returned a guilty verdict. The combination of an outstanding warrant and the defendant's conduct at arrest left no reasonable doubt.", new[] { "Wanted", "Fleeing" }),
                        ("The {0} found the defendant guilty. The murder charges were the defining element of the case.", new[] { "Homicide" }),
                        ("The {0} returned a guilty verdict. The gravity of the homicide charges dominated the proceeding.", new[] { "Homicide" }),
                        ("The {0} found the defendant guilty. The murder and manslaughter charges were central to the prosecution's case.", new[] { "Homicide" }),
                        ("The {0} returned a guilty verdict. The homicide-related charges were paramount in the {0}'s decision.", new[] { "Homicide" }),
                        ("The {0} found the defendant guilty. The murder charges warranted the most serious consideration by the court.", new[] { "Homicide" }),
                        ("The {0} returned a guilty verdict. The defendant's conviction on murder charges was the focal point of the case.", new[] { "Homicide" }),
                        ("The {0} found the defendant guilty. The sexual offence charges were central to the prosecution's case.", new[] { "SexOffense" }),
                        ("The {0} returned a guilty verdict. The gravity of the rape and sexual violence charges dominated the proceeding.", new[] { "SexOffense" }),
                        ("The {0} found the defendant guilty. Kidnapping and related charges were paramount in the {0}'s decision.", new[] { "Kidnapping" }),
                        ("The {0} returned a guilty verdict. The kidnapping charges warranted the most serious consideration.", new[] { "Kidnapping" }),
                        ("The {0} found the defendant guilty. The arson charges were a defining element of the case.", new[] { "Arson" }),
                        ("The {0} returned a guilty verdict. Fire-related offences and property destruction were central to the verdict.", new[] { "Arson" }),
                    };
                    string chosen = SelectWeightedOutcome(courtData, guiltyTrialPool);
                    b.AppendFormat(chosen, tribunal);
                }
                var factors = new List<string>();
                if (HasChargeKeyword(courtData, "murder") || HasChargeKeyword(courtData, "manslaughter"))
                    factors.Add("the defendant was convicted of murder or manslaughter");
                if (HasSexOffenseCharge(courtData))
                    factors.Add("the defendant was convicted of sexual or related offences");
                if (HasKidnappingCharge(courtData))
                    factors.Add("the defendant was convicted of kidnapping");
                if (HasArsonCharge(courtData))
                    factors.Add("the defendant was convicted of arson or unlawful burning");
                if (HasChargeKeyword(courtData, "robbery") || HasChargeKeyword(courtData, "burglary") || HasChargeKeyword(courtData, "carjacking") || HasChargeKeyword(courtData, "home invasion"))
                    factors.Add("serious property and robbery charges were proven");
                if (HasDrugCrimeCharge(courtData))
                    factors.Add("drug-related charges formed part of the case");
                if (HasChargeKeyword(courtData, "escape") && !HasChargeKeyword(courtData, "evad"))
                    factors.Add("escape or attempted escape from custody was established");
                if (HasChargeKeyword(courtData, "violation of probation") || HasChargeKeyword(courtData, "violation of parole") || HasChargeKeyword(courtData, "protective order"))
                    factors.Add("breach of court orders or supervision was established");
                if (courtData.EvidenceHadWeapon) factors.Add("the defendant was armed at the time of arrest");
                if (courtData.EvidenceWasWanted) factors.Add("an active warrant was outstanding");
                if (courtData.EvidenceViolatedSupervision) factors.Add("the defendant was on probation or parole");
                if (courtData.EvidenceWasDrunk) factors.Add("the defendant was visibly intoxicated");
                if (courtData.EvidenceWasFleeing) factors.Add("the defendant attempted to flee");
                if (courtData.EvidenceAssaultedPed) factors.Add("the defendant committed assault");
                if (courtData.EvidenceResisted) factors.Add("the defendant resisted arrest");
                if (courtData.EvidenceDamagedVehicle) factors.Add("the defendant caused vehicle damage");
                if (courtData.EvidenceIllegalWeapon) {
                    if (courtData.EvidenceFirearmTypesBreakdown != null && courtData.EvidenceFirearmTypesBreakdown.Count > 0)
                        factors.Add((courtData.EvidenceFirearmTypesBreakdown.Count == 1 ? courtData.EvidenceFirearmTypesBreakdown[0] + " was" : string.Join(" and ", courtData.EvidenceFirearmTypesBreakdown) + " were") + " recovered");
                    else factors.Add("an illegal weapon was recovered");
                }
                if (courtData.EvidenceHadDrugs) {
                    if (courtData.EvidenceDrugTypesBreakdown != null && courtData.EvidenceDrugTypesBreakdown.Count > 0)
                        factors.Add((courtData.EvidenceDrugTypesBreakdown.Count == 1 ? courtData.EvidenceDrugTypesBreakdown[0] + " was" : string.Join(" and ", courtData.EvidenceDrugTypesBreakdown) + " were") + " recovered");
                    else factors.Add("controlled substances were found");
                }
                if (factors.Count > 0) {
                    string[] factorLeadIns = new[] {
                        "Key factors: ",
                        "Notable factors included: ",
                        "The court noted: ",
                        "Aggravating factors included: ",
                        "Relevant considerations: ",
                        "Factors weighed by the court: ",
                        "Significant factors: ",
                        "The court cited: ",
                    };
                    b.Append(" " + factorLeadIns[Helper.GetRandomInt(0, factorLeadIns.Length - 1)] + string.Join("; ", factors) + ".");
                }
                if (courtData.RepeatOffenderScore >= 5) {
                    string[] repeatOffender = new[] {
                        "The defendant's prior criminal record weighed heavily in the verdict.",
                        "The defendant's extensive prior record was a significant factor.",
                        "The court took the defendant's criminal history into account.",
                        "Prior convictions influenced the verdict.",
                        "The defendant's repeat offender status factored in the decision.",
                        "The court noted the defendant's prior criminal record in reaching its verdict.",
                    };
                    b.Append(" " + repeatOffender[Helper.GetRandomInt(0, repeatOffender.Length - 1)]);
                }
                if (courtData.IsJuryTrial) {
                    string[] juryConviction = new[] {
                        $"The jury voted {courtData.JuryVotesForConviction}-{courtData.JuryVotesForAcquittal} in favour of conviction.",
                        $"The jury reached a {courtData.JuryVotesForConviction}-{courtData.JuryVotesForAcquittal} verdict.",
                        $"The jury split {courtData.JuryVotesForConviction}-{courtData.JuryVotesForAcquittal} in favour of guilt.",
                        $"The vote was {courtData.JuryVotesForConviction} to {courtData.JuryVotesForAcquittal}.",
                        $"The jury voted {courtData.JuryVotesForConviction} to {courtData.JuryVotesForAcquittal} for conviction.",
                    };
                    b.Append(" " + juryConviction[Helper.GetRandomInt(0, juryConviction.Length - 1)]);
                }
                if (courtData.DocketPressure > 0.6f) {
                    string[] docketConviction = new[] {
                        "The case was heard on an expedited basis due to court docket volume.",
                        "The case was fast-tracked amid a crowded court calendar.",
                        "Docket pressure led to an expedited hearing.",
                        "The case was resolved quickly due to court backlog.",
                        "Amid heavy docket volume, the case was heard on an expedited schedule.",
                    };
                    b.Append(" " + docketConviction[Helper.GetRandomInt(0, docketConviction.Length - 1)]);
                }
                AppendChargeDomainPhrase(b, courtData, resolvedStatus);
            } else if (resolvedStatus == 2) {
                if (mismatchAcquittal) {
                    string[] highEvidenceAcquittal = new[] {
                        "Despite strong prosecution evidence, the {0} returned a not guilty verdict. The defence successfully challenged key aspects of the case and raised sufficient reasonable doubt.",
                        "In an unexpected outcome, the {0} acquitted the defendant. Procedural issues and defence challenges to the evidence ultimately prevailed.",
                        "The {0} returned a not guilty verdict despite a robust prosecution case. The defence's challenge to witness identification and chain of custody created reasonable doubt.",
                        "Although the prosecution presented substantial evidence, the {0} found the defendant not guilty. Credibility issues and defence arguments raised sufficient doubt.",
                    };
                    b.AppendFormat(highEvidenceAcquittal[Helper.GetRandomInt(0, highEvidenceAcquittal.Length - 1)], tribunal);
                } else {
                var acquittalPool = new (string text, string[] evidenceTags)[] {
                    ("The {0} found the defendant not guilty. The prosecution failed to establish guilt beyond a reasonable doubt.", null),
                    ("The {0} returned a not guilty verdict. The defence successfully raised reasonable doubt.", null),
                    ("Despite a strong prosecution case, the {0} returned a not guilty verdict.", null),
                    ("The {0} acquitted the defendant. The evidence was deemed insufficient to support a conviction.", null),
                    ("The {0} found the defendant not guilty. The prosecution's case rested largely on circumstantial evidence.", null),
                    ("The {0} returned a not guilty verdict. Witness credibility and inconsistent testimony favoured the defence.", null),
                    ("The {0} acquitted the defendant. The defence raised sufficient doubt as to the defendant's involvement.", null),
                    ("The {0} found the defendant not guilty. Gaps in the chain of custody and procedural issues were cited.", null),
                    ("The {0} returned a not guilty verdict. The evidence did not sufficiently link the defendant to the alleged conduct.", null),
                    ("The {0} acquitted the defendant. Conflicting testimony and lack of physical evidence supported acquittal.", null),
                    ("The {0} found the defendant not guilty. The defence presented a plausible alternative account of the facts.", null),
                    ("The {0} returned a not guilty verdict. The prosecution could not overcome the presumption of innocence.", null),
                    ("The {0} acquitted the defendant. Reasonable doubt remained as to intent and identification.", null),
                    ("The {0} found the defendant not guilty. The weight of the evidence did not support conviction.", null),
                    ("The {0} returned a not guilty verdict. The defence's challenge to the arrest and search was sustained.", null),
                    ("The {0} acquitted the defendant. Insufficient evidence was presented to establish guilt.", null),
                    ("The {0} found the defendant not guilty. The prosecution failed to meet its burden of proof.", null),
                    ("The {0} returned a not guilty verdict. The defendant's account, together with the evidence, left reasonable doubt.", null),
                    ("The {0} acquitted the defendant. The prosecution failed to meet its burden; reasonable doubt existed.", null),
                    ("The {0} found the defendant not guilty. The defence raised doubt as to the reliability of the identification.", null),
                    ("The {0} returned a not guilty verdict. Identification issues and conflicting descriptions favoured acquittal.", null),
                    ("The {0} acquitted the defendant. The circumstantial evidence was insufficient to establish guilt beyond reasonable doubt.", null),
                    ("The {0} found the defendant not guilty. The prosecution's circumstantial case did not exclude reasonable alternatives.", null),
                    ("The {0} returned a not guilty verdict. Witness credibility was called into question; the defence prevailed.", null),
                    ("The {0} acquitted the defendant. Key witnesses were inconsistent; the {0} could not rely on their testimony.", null),
                    ("The {0} found the defendant not guilty. Problems with the chain of custody undermined the physical evidence.", null),
                    ("The {0} returned a not guilty verdict. Chain of custody gaps raised doubt about the integrity of the evidence.", null),
                    ("The {0} acquitted the defendant. Procedural irregularities and chain of custody issues favoured the defence.", null),
                    ("The {0} found the defendant not guilty. The prosecution presented insufficient evidence to sustain a conviction.", null),
                    ("The {0} returned a not guilty verdict. The evidence, taken as a whole, did not establish guilt to the required standard.", null),
                    ("The {0} acquitted the defendant. The defence successfully challenged the prosecution's key evidence.", null),
                    ("The {0} found the defendant not guilty. The prosecution could not prove each element of the offence.", null),
                    ("The {0} returned a not guilty verdict. Doubt remained as to whether the defendant committed the alleged acts.", null),
                    ("The {0} acquitted the defendant. The presumption of innocence was not overcome by the prosecution's case.", null),
                    ("The {0} found the defendant not guilty. The defence raised a reasonable alternative explanation for the evidence.", null),
                    ("The {0} returned a not guilty verdict. The prosecution's theory of the case was not sufficiently supported.", null),
                    ("The {0} acquitted the defendant. Lack of corroboration and weak identification supported acquittal.", null),
                    ("The {0} found the defendant not guilty. The prosecution relied on evidence that was inconclusive or unreliable.", null),
                    ("The {0} returned a not guilty verdict. The defendant was acquitted; the evidence did not rise to the standard of proof.", null),
                    ("The {0} acquitted the defendant. Reasonable doubt existed as to the defendant's guilt.", null),
                    ("The {0} found the defendant not guilty. The prosecution failed to prove guilt beyond a reasonable doubt.", null),
                    ("The {0} returned a not guilty verdict. The defence cast sufficient doubt on the prosecution's case.", null),
                    ("The {0} acquitted the defendant. Credibility problems with prosecution witnesses favoured the defence.", null),
                    ("The {0} found the defendant not guilty. The evidence was equivocal and did not support conviction.", null),
                    ("The {0} returned a not guilty verdict. The prosecution could not exclude reasonable doubt as to identity or intent.", null),
                    ("The {0} acquitted the defendant. The case against the defendant was not made out to the required standard.", null),
                    ("The {0} found the defendant not guilty. Material inconsistencies in the evidence favoured acquittal.", null),
                    ("The {0} returned a not guilty verdict. The defence raised reasonable doubt on critical elements of the charges.", null),
                    ("The {0} acquitted the defendant. The prosecution's evidence was insufficient to sustain a finding of guilt.", null),
                    ("The {0} found the defendant not guilty. The {0} was not persuaded that guilt had been established.", null),
                    ("The {0} returned a not guilty verdict. The prosecution did not present evidence sufficient to convict.", null),
                    ("The {0} acquitted the defendant. Reasonable doubt persisted after consideration of all evidence.", null),
                    ("The {0} found the defendant not guilty. The defence's arguments raised sufficient doubt to warrant acquittal.", null),
                    ("The {0} returned a not guilty verdict. The prosecution failed to establish a sufficient connection between the defendant and the offence.", null),
                    ("The {0} acquitted the defendant. The evidence presented fell short of the standard required for conviction.", null),
                };
                string chosen = SelectWeightedOutcome(courtData, acquittalPool);
                b.AppendFormat(chosen, tribunal);
                }
                if (!courtData.HasPublicDefender) {
                    string[] privateCounsel = new[] {
                        $"Private counsel {courtData.DefenseAttorneyName} mounted an effective defence.",
                        $"Defence attorney {courtData.DefenseAttorneyName} presented a strong case.",
                        $"{courtData.DefenseAttorneyName} provided effective representation for the defence.",
                        $"Private counsel {courtData.DefenseAttorneyName} argued persuasively on the defendant's behalf.",
                        $"{courtData.DefenseAttorneyName} successfully advocated for the defendant.",
                    };
                    b.Append(" " + privateCounsel[Helper.GetRandomInt(0, privateCounsel.Length - 1)]);
                }
                if (courtData.IsJuryTrial) {
                    string[] juryAcquittal = new[] {
                        $"The jury voted {courtData.JuryVotesForAcquittal}-{courtData.JuryVotesForConviction} in favour of acquittal.",
                        $"The jury reached a {courtData.JuryVotesForAcquittal}-{courtData.JuryVotesForConviction} verdict for acquittal.",
                        $"The jury split {courtData.JuryVotesForAcquittal}-{courtData.JuryVotesForConviction} in favour of not guilty.",
                        $"A {courtData.JuryVotesForAcquittal}-{courtData.JuryVotesForConviction} jury vote returned a not guilty verdict.",
                        $"The jury voted {courtData.JuryVotesForAcquittal} to {courtData.JuryVotesForConviction} for acquittal.",
                    };
                    b.Append(" " + juryAcquittal[Helper.GetRandomInt(0, juryAcquittal.Length - 1)]);
                }
                if (courtData.DocketPressure > 0.6f) {
                    string[] docketAcquittal = new[] {
                        "The case was resolved quickly amid a crowded court calendar.",
                        "The case was heard on an expedited basis due to docket volume.",
                        "Docket pressure contributed to a quick resolution.",
                        "The case was fast-tracked; resolution came amid a busy court schedule.",
                        "Amid court backlog, the case was resolved on an accelerated timeline.",
                    };
                    b.Append(" " + docketAcquittal[Helper.GetRandomInt(0, docketAcquittal.Length - 1)]);
                }
                AppendChargeDomainPhrase(b, courtData, resolvedStatus);
            } else if (resolvedStatus == 3) {
                string[] dismissedPool = new[] {
                    "Charges were dismissed. The case did not proceed to trial.",
                    "The charges were dismissed. The prosecution declined to proceed.",
                    "The case was dismissed. Insufficient evidence to proceed to trial.",
                    "Charges were dismissed on procedural grounds. The case did not go to trial.",
                    "The court dismissed the case. The matter was not tried on the merits.",
                    "The case was dismissed. The prosecution could not meet its burden at this stage.",
                    "Charges were dismissed without prejudice. The matter was not tried on the merits.",
                    "The charges were dismissed with prejudice. The case will not be refiled.",
                    "The court entered nolle prosequi. The prosecution elected not to proceed.",
                    "Charges were dismissed. The matter did not proceed to trial due to evidentiary issues.",
                    "The case was dismissed. Key witnesses were unavailable or evidence was suppressed.",
                    "The prosecution declined to proceed. Charges were dismissed.",
                    "Charges were dismissed on procedural grounds. The matter did not reach trial.",
                    "The case was dismissed. Procedural defects led to dismissal.",
                    "The charges were dismissed. The case did not reach trial.",
                    "The court dismissed the case. Insufficient evidence was cited.",
                    "Charges were dismissed. The prosecution elected to decline prosecution.",
                    "The case was dismissed. The matter was not tried on the merits.",
                    "The charges were dismissed. Did not proceed to trial.",
                    "The court dismissed the case. The prosecution declined to proceed.",
                    "Charges were dismissed. The prosecution withdrew the charges.",
                    "The case was dismissed. Evidentiary problems prevented the case from going forward.",
                    "The charges were dismissed. The case was resolved without a trial.",
                    "The court entered a dismissal. The matter was not tried on the merits.",
                    "Charges were dismissed. The prosecution determined it could not prevail.",
                };
                b.Append(dismissedPool[Helper.GetRandomInt(0, dismissedPool.Length - 1)]);
            }

            return b.ToString().Trim();
        }

        private static void AppendChargeDomainPhrase(StringBuilder b, CourtData courtData, int resolvedStatus) {
            if (b == null || courtData?.Charges == null || courtData.Charges.Count == 0) return;
            // Homicide/murder always mentioned when present—do not relegate to random pick
            if (HasChargeKeyword(courtData, "murder") || HasChargeKeyword(courtData, "manslaughter")) {
                string[] homicideConv = new[] {
                    "The murder charges were the focal point of the case.",
                    "The homicide charges were central to the verdict.",
                    "The murder and manslaughter charges were paramount in the proceeding.",
                    "The gravity of the murder charges dominated the case.",
                    "The court weighed the homicide-related charges heavily in reaching its decision.",
                    "The murder charges were central to the prosecution and the verdict.",
                };
                string[] homicideAcq = new[] {
                    "The defence challenged intent, identification, and the prosecution's theory of the homicide.",
                    "Reasonable doubt as to the defendant's involvement in the death was raised by the defence.",
                    "The defence contested the causation and intent elements of the homicide charges.",
                };
                b.Append(" " + (resolvedStatus == 1 ? homicideConv[Helper.GetRandomInt(0, homicideConv.Length - 1)] : homicideAcq[Helper.GetRandomInt(0, homicideAcq.Length - 1)]));
            }
            var phrases = new List<string>();
            if (HasChargeKeyword(courtData, "DUI") || HasChargeKeyword(courtData, "DWI") || HasChargeKeyword(courtData, "driving under")
                || HasChargeKeyword(courtData, "chemical test") || HasChargeKeyword(courtData, "field sobriety") || HasChargeKeyword(courtData, "dui causing")) {
                string[] duiConv = new[] {
                    "The impaired driving charge was central to the case.",
                    "The DUI charge was a focal point of the proceeding.",
                    "Impaired driving evidence strongly influenced the outcome.",
                    "The court gave significant weight to the intoxication evidence.",
                    "The driving-under-influence charge was central to the verdict.",
                    "Field sobriety and breath test results were key to the outcome.",
                };
                string[] duiAcq = new[] {
                    "The defence challenged the validity of the traffic stop and field sobriety procedures.",
                    "The defence contested the legality of the stop and the reliability of sobriety testing.",
                    "Chain of custody for breath samples and stop justification were disputed.",
                    "The defence raised issues with the traffic stop and testing protocol.",
                    "Challenges to the stop, testing procedures, and calibration records were central.",
                    "The defence contested the basis for the stop and the field sobriety assessment.",
                };
                phrases.Add(resolvedStatus == 1 ? duiConv[Helper.GetRandomInt(0, duiConv.Length - 1)] : duiAcq[Helper.GetRandomInt(0, duiAcq.Length - 1)]);
            }
            if (HasDrugCrimeCharge(courtData)) {
                string[] drugConv = new[] {
                    "Drug possession and related conduct were addressed in the verdict.",
                    "The drug charges were a central element of the case.",
                    "Narcotics evidence was weighed in the outcome.",
                    "The controlled substance charges factored heavily in the verdict.",
                    "Drug-related conduct was reflected in the court's decision.",
                    "The drug and paraphernalia charges were central to the prosecution's case.",
                };
                string[] drugAcq = new[] {
                    "Chain of custody and search legality were contested.",
                    "The defence challenged the search and chain of custody of the evidence.",
                    "Search warrant validity and evidence handling were in dispute.",
                    "The defence contested the lawfulness of the search and custody procedures.",
                    "Challenges to search legality and evidence integrity were raised.",
                    "The defence questioned the basis for the search and the handling of the evidence.",
                };
                phrases.Add(resolvedStatus == 1 ? drugConv[Helper.GetRandomInt(0, drugConv.Length - 1)] : drugAcq[Helper.GetRandomInt(0, drugAcq.Length - 1)]);
            }
            if (HasChargeKeyword(courtData, "assault") || HasChargeKeyword(courtData, "battery") || HasChargeKeyword(courtData, "violence")
                || HasChargeKeyword(courtData, "mayhem") || HasChargeKeyword(courtData, "malicious wounding") || HasChargeKeyword(courtData, "wounding")) {
                string[] assaultConv = new[] {
                    "The violent nature of the offence was reflected in the outcome.",
                    "The assault and battery charges were central to the verdict.",
                    "The violent conduct was a key factor in the decision.",
                    "Assault-related evidence strongly influenced the outcome.",
                    "The court weighed the violent nature of the offence.",
                    "The battery and assault charges factored heavily in the verdict.",
                };
                string[] assaultAcq = new[] {
                    "Self-defence and intent were central to the defence.",
                    "The defence argued self-defence and lack of intent.",
                    "Self-defence and provocation were key defence arguments.",
                    "The defence challenged intent and asserted justification.",
                    "Issues of self-defence and mental state were in dispute.",
                    "The defence raised self-defence and questioned intent.",
                };
                phrases.Add(resolvedStatus == 1 ? assaultConv[Helper.GetRandomInt(0, assaultConv.Length - 1)] : assaultAcq[Helper.GetRandomInt(0, assaultAcq.Length - 1)]);
            }
            if (HasChargeKeyword(courtData, "resisting") || HasChargeKeyword(courtData, "obstruction") || HasChargeKeyword(courtData, "refusing") || HasChargeKeyword(courtData, "failure to present")) {
                string[] resistConv = new[] {
                    "The defendant's conduct toward law enforcement was considered.",
                    "Resisting arrest was a factor in the outcome.",
                    "The obstruction charges were weighed in the verdict.",
                    "Conduct toward officers was central to the case.",
                    "The resisting and obstruction charges factored in the decision.",
                    "The defendant's resistance was cited in the outcome.",
                };
                string[] resistAcq = new[] {
                    "The lawfulness of the underlying detention was in dispute.",
                    "The defence contested the legality of the arrest and detention.",
                    "The validity of the underlying stop and arrest was challenged.",
                    "The defence argued the detention was unlawful.",
                    "Challenges to the arrest and detention legality were central.",
                    "The defence questioned whether the initial detention was justified.",
                };
                phrases.Add(resolvedStatus == 1 ? resistConv[Helper.GetRandomInt(0, resistConv.Length - 1)] : resistAcq[Helper.GetRandomInt(0, resistAcq.Length - 1)]);
            }
            if (HasKidnappingCharge(courtData)) {
                string[] kidnapConv = new[] {
                    "The kidnapping charges were central to the verdict.",
                    "Abduction-related offences were a focal point of the case.",
                    "The kidnapping counts factored heavily in the outcome.",
                };
                string[] kidnapAcq = new[] {
                    "Consent, intent, and movement of the victim were contested by the defence.",
                    "The defence challenged the elements of kidnapping and unlawful restraint.",
                };
                phrases.Add(resolvedStatus == 1 ? kidnapConv[Helper.GetRandomInt(0, kidnapConv.Length - 1)] : kidnapAcq[Helper.GetRandomInt(0, kidnapAcq.Length - 1)]);
            }
            if (HasSexOffenseCharge(courtData)) {
                string[] sexConv = new[] {
                    "The sexual offence charges were central to the verdict.",
                    "Sex crimes and related conduct were a focal point of the proceeding.",
                    "The court weighed the sexual violence charges heavily.",
                };
                string[] sexAcq = new[] {
                    "Consent and identification were central issues raised by the defence.",
                    "The defence challenged the prosecution's theory of the sexual offences.",
                };
                phrases.Add(resolvedStatus == 1 ? sexConv[Helper.GetRandomInt(0, sexConv.Length - 1)] : sexAcq[Helper.GetRandomInt(0, sexAcq.Length - 1)]);
            }
            if (HasArsonCharge(courtData)) {
                string[] arsonConv = new[] {
                    "The arson and fire-related charges were central to the verdict.",
                    "Property destruction by fire was a key element of the case.",
                    "The arson counts factored heavily in the outcome.",
                };
                string[] arsonAcq = new[] {
                    "Origin and cause of the fire were contested by the defence.",
                    "The defence challenged intent and whether the burning was wilful.",
                };
                phrases.Add(resolvedStatus == 1 ? arsonConv[Helper.GetRandomInt(0, arsonConv.Length - 1)] : arsonAcq[Helper.GetRandomInt(0, arsonAcq.Length - 1)]);
            }
            if (HasChargeKeyword(courtData, "vandalism") || HasChargeKeyword(courtData, "trespass") || HasChargeKeyword(courtData, "destruction of property") || HasChargeKeyword(courtData, "prowling")) {
                string[] propConv = new[] {
                    "Criminal damage and trespass charges were addressed in the verdict.",
                    "Property-related misdemeanours factored in the outcome.",
                    "Vandalism and trespass evidence was weighed by the court.",
                };
                string[] propAcq = new[] {
                    "The defence challenged damage valuation and lawful presence on the property.",
                    "Intent and extent of property damage were disputed.",
                };
                phrases.Add(resolvedStatus == 1 ? propConv[Helper.GetRandomInt(0, propConv.Length - 1)] : propAcq[Helper.GetRandomInt(0, propAcq.Length - 1)]);
            }
            if (HasChargeKeyword(courtData, "escape") && !HasChargeKeyword(courtData, "evad")) {
                string[] escConv = new[] {
                    "Escape from custody or confinement charges were central to the case.",
                    "The escape-related counts factored in the verdict.",
                };
                string[] escAcq = new[] {
                    "The defence challenged whether a lawful escape charge was made out.",
                };
                phrases.Add(resolvedStatus == 1 ? escConv[Helper.GetRandomInt(0, escConv.Length - 1)] : escAcq[Helper.GetRandomInt(0, escAcq.Length - 1)]);
            }
            if (HasChargeKeyword(courtData, "violation of probation") || HasChargeKeyword(courtData, "violation of parole") || HasChargeKeyword(courtData, "protective order") || HasChargeKeyword(courtData, "failure to register as sex offender")) {
                string[] courtOrdConv = new[] {
                    "Breach of probation, parole, or court orders was weighed in the verdict.",
                    "Supervision and registration violations were central to the case.",
                };
                string[] courtOrdAcq = new[] {
                    "The defence contested whether a breach of conditions was proven.",
                };
                phrases.Add(resolvedStatus == 1 ? courtOrdConv[Helper.GetRandomInt(0, courtOrdConv.Length - 1)] : courtOrdAcq[Helper.GetRandomInt(0, courtOrdAcq.Length - 1)]);
            }
            if (HasChargeKeyword(courtData, "riot") || HasChargeKeyword(courtData, "disorderly conduct") || HasChargeKeyword(courtData, "disturbing the peace") || HasChargeKeyword(courtData, "stalking")
                || HasChargeKeyword(courtData, "impersonating peace") || HasChargeKeyword(courtData, "false report") || HasChargeKeyword(courtData, "unlawful assembly")
                || HasChargeKeyword(courtData, "accessory after") || HasChargeKeyword(courtData, "accessory before") || HasChargeKeyword(courtData, "harassment by electronic")
                || HasChargeKeyword(courtData, "failure to disperse") || HasChargeKeyword(courtData, "wanton endangerment") || HasChargeKeyword(courtData, "reckless endangerment")
                || HasChargeKeyword(courtData, "present during a riot") || HasChargeKeyword(courtData, "public intoxication") || HasChargeKeyword(courtData, "911 abuse")) {
                string[] pubConv = new[] {
                    "Public order and disorderly conduct charges were addressed in the verdict.",
                    "The court weighed breaches of the peace and related offences.",
                };
                string[] pubAcq = new[] {
                    "First Amendment and assembly issues were raised by the defence.",
                };
                phrases.Add(resolvedStatus == 1 ? pubConv[Helper.GetRandomInt(0, pubConv.Length - 1)] : pubAcq[Helper.GetRandomInt(0, pubAcq.Length - 1)]);
            }
            if (HasChargeKeyword(courtData, "theft") || HasChargeKeyword(courtData, "burglary") || HasChargeKeyword(courtData, "robbery") || HasChargeKeyword(courtData, "larceny") || HasChargeKeyword(courtData, "counterfeit") || HasChargeKeyword(courtData, "stolen") || HasChargeKeyword(courtData, "credit card scanning")) {
                string[] theftConv = new[] {
                    "The theft-related charges were central to the verdict.",
                    "Property crime evidence factored heavily in the outcome.",
                    "The theft or burglary charges were key to the prosecution.",
                    "The court weighed the property offence evidence.",
                    "The theft charges were a focal point of the case.",
                    "Evidence of theft or unlawful taking influenced the verdict.",
                };
                string[] theftAcq = new[] {
                    "The defence challenged identification and proof of intent.",
                    "Intent and identification were contested by the defence.",
                    "The defence raised doubt as to identification and ownership.",
                    "Challenges to identification and unlawful intent were central.",
                    "The defence contested proof of possession and intent.",
                    "Identification of property and intent were in dispute.",
                };
                phrases.Add(resolvedStatus == 1 ? theftConv[Helper.GetRandomInt(0, theftConv.Length - 1)] : theftAcq[Helper.GetRandomInt(0, theftAcq.Length - 1)]);
            }
            if (HasChargeKeyword(courtData, "firearm") || HasChargeKeyword(courtData, "weapon") || HasChargeKeyword(courtData, "gun") || HasChargeKeyword(courtData, "armed")) {
                string[] firearmConv = new[] {
                    "The firearms charges were central to the case.",
                    "Weapon possession evidence strongly influenced the outcome.",
                    "The gun charges factored heavily in the verdict.",
                    "The court weighed the firearms-related evidence.",
                    "Armed conduct was a key factor in the decision.",
                    "The weapon charges were a focal point of the proceeding.",
                };
                string[] firearmAcq = new[] {
                    "The defence challenged possession and lawful authority.",
                    "The defence contested proof of possession and lawful purpose.",
                    "Possession and intent to use unlawfully were disputed.",
                    "The defence raised questions about lawful possession.",
                    "Challenges to constructive possession and intent were central.",
                    "The defence questioned whether the weapon was lawfully possessed.",
                };
                phrases.Add(resolvedStatus == 1 ? firearmConv[Helper.GetRandomInt(0, firearmConv.Length - 1)] : firearmAcq[Helper.GetRandomInt(0, firearmAcq.Length - 1)]);
            }
            // Traffic/evading: exclude arson charges that contain "reckless" (e.g. reckless burning)
            if (!HasArsonCharge(courtData) && (HasChargeKeyword(courtData, "traffic") || HasChargeKeyword(courtData, "speeding") || HasChargeKeyword(courtData, "evading")
                || HasChargeKeyword(courtData, "street racing") || HasChargeKeyword(courtData, "hit and run") || HasChargeKeyword(courtData, "wrong side")
                || HasChargeKeyword(courtData, "driving on suspended") || HasChargeKeyword(courtData, "driving without license") || HasChargeKeyword(courtData, "license expired")
                || HasChargeKeyword(courtData, "refusal to sign traffic") || HasChargeKeyword(courtData, "impeding traffic")
                || (HasChargeKeyword(courtData, "reckless") && !HasChargeKeyword(courtData, "burning")))) {
                string[] trafficConv = new[] {
                    "The traffic and driving charges were central to the verdict.",
                    "Reckless driving and related conduct factored in the outcome.",
                    "The traffic offence evidence was weighed by the court.",
                    "The driving charges were a key element of the case.",
                    "Traffic and evading evidence influenced the decision.",
                    "The reckless driving charge was central to the prosecution.",
                };
                string[] trafficAcq = new[] {
                    "The defence challenged the pursuit and identification of the vehicle.",
                    "Vehicle identification and pursuit justification were contested.",
                    "The defence raised issues with pursuit protocol and identification.",
                    "Challenges to the chase and driver identification were central.",
                    "The defence contested the basis for the pursuit.",
                    "Identification of the driver and necessity of pursuit were in dispute.",
                };
                phrases.Add(resolvedStatus == 1 ? trafficConv[Helper.GetRandomInt(0, trafficConv.Length - 1)] : trafficAcq[Helper.GetRandomInt(0, trafficAcq.Length - 1)]);
            }
            // Always append one charge-domain phrase when any domain matched (was 33% random—too often silent)
            if (phrases.Count > 0)
                b.Append(" " + phrases[Helper.GetRandomInt(0, phrases.Count - 1)]);
        }

        private static string BuildSentenceReasoning(CourtData courtData) {
            if (courtData == null) return "";
            var b = new StringBuilder();
            string judge = courtData.JudgeName ?? "the court";
            bool hasLife = courtData.Charges?.Any(c => c.Time == null) == true;

            if (courtData.RepeatOffenderScore >= 6) {
                string[] recidivism = new[] {
                    $"The court cited the defendant's extensive prior record and pattern of criminal conduct as significant aggravating factors.",
                    $"{judge} noted the defendant's repeated failures to comply with prior sanctions and the need for deterrence.",
                    "The defendant's criminal history and failure to rehabilitate weighed heavily in sentencing.",
                    $"{judge} found that recidivism and the defendant's prior sanctions warranted an elevated sentence.",
                    "The court emphasised the pattern of criminal conduct and escalating behaviour over time.",
                    $"Aggravating factors included {judge}'s finding that prior interventions had failed to deter the defendant.",
                    "The defendant's extensive prior record demonstrated a need for substantial deterrence.",
                    $"{judge} cited the repeated failures to comply with court orders and rehabilitative efforts.",
                    "The court considered the defendant's recidivism and lack of response to prior punishment.",
                    "Prior sanctions had proven insufficient; the court imposed a sentence reflecting that failure to rehabilitate.",
                    $"The defendant's escalating conduct and pattern of criminal behaviour were emphasised by {judge}.",
                    "The court weighed the defendant's prior convictions and the need to protect the public from further offending.",
                    $"{judge} noted that the defendant's criminal history warranted a sentence at the upper end of the range.",
                    "Aggravating factors included the defendant's extensive prior record and disregard for prior court orders.",
                    "The court found that the defendant's failure to rehabilitate despite prior sanctions justified a substantial term.",
                    $"{judge} cited the pattern of criminal conduct and the need for both punishment and deterrence.",
                    "The defendant's repeated failures to comply and recidivism were central to the sentencing decision.",
                    "The court emphasised the gravity of prior convictions and the defendant's escalating conduct.",
                    $"{judge} considered the defendant's prior sanctions and the evident need for a stronger deterrent.",
                    "The defendant's extensive prior record, failure to rehabilitate, and pattern of criminal conduct warranted an elevated sentence.",
                };
                b.Append(recidivism[Helper.GetRandomInt(0, recidivism.Length - 1)]);
            } else if (courtData.RepeatOffenderScore >= 3) {
                string[] prior = new[] {
                    $"The defendant's prior convictions were taken into account as an aggravating factor.",
                    $"{judge} considered the defendant's record in determining the appropriate sentence.",
                    $"The defendant's prior convictions were considered by {judge} as an aggravating factor in sentencing.",
                    "The court applied the sentencing guidelines having regard to the defendant's criminal history.",
                    $"{judge} noted the defendant's prior record when assessing the appropriate sentence.",
                    "The defendant's prior convictions weighed as an aggravating factor in the sentencing exercise.",
                    $"The court, having considered the defendant's record, applied the guidelines accordingly.",
                    $"{judge} took the defendant's prior convictions into account in fashioning the sentence.",
                    "The defendant's criminal history was considered as an aggravating factor under the guidelines.",
                    $"The sentencing guidelines were applied with due regard to {judge}'s assessment of the prior record.",
                    "The court considered the defendant's record and the need for proportionate punishment.",
                    $"{judge} found the defendant's prior convictions relevant to the sentencing decision.",
                    "The defendant's prior record was taken into account as part of the sentencing calculus.",
                    $"Aggravating factors considered by {judge} included the defendant's prior convictions.",
                    "The court balanced the defendant's prior convictions against the nature of the current offence.",
                };
                b.Append(prior[Helper.GetRandomInt(0, prior.Length - 1)]);
            }

            if (HasChargeKeyword(courtData, "murder") || HasChargeKeyword(courtData, "manslaughter")) {
                if (b.Length > 0) b.Append(" ");
                string[] homicideSent = new[] {
                    "The murder and manslaughter charges were central to the sentencing decision.",
                    "The court emphasised the gravity of the homicide charges in imposing sentence.",
                    "The homicide-related convictions warranted the most serious sentencing response.",
                    "The murder charges were the defining factor in the court's sentencing approach.",
                    "The gravity of the murder convictions dominated the sentencing considerations.",
                };
                b.Append(homicideSent[Helper.GetRandomInt(0, homicideSent.Length - 1)]);
            }
            if (HasSexOffenseCharge(courtData)) {
                if (b.Length > 0) b.Append(" ");
                string[] sexSent = new[] {
                    "The sexual offence convictions warranted a severe sentencing response.",
                    "The court emphasised the gravity of the sex crimes in fashioning sentence.",
                    "Protection of vulnerable persons and denunciation of sexual violence informed the sentence.",
                };
                b.Append(sexSent[Helper.GetRandomInt(0, sexSent.Length - 1)]);
            }
            if (HasKidnappingCharge(courtData)) {
                if (b.Length > 0) b.Append(" ");
                string[] kidnapSent = new[] {
                    "The kidnapping convictions were central to the sentencing decision.",
                    "The court treated abduction-related offences as highly aggravating.",
                };
                b.Append(kidnapSent[Helper.GetRandomInt(0, kidnapSent.Length - 1)]);
            }
            if (HasArsonCharge(courtData)) {
                if (b.Length > 0) b.Append(" ");
                string[] arsonSent = new[] {
                    "The arson convictions factored heavily into the sentence imposed.",
                    "Fire-related offences and risk to life and property were emphasised in sentencing.",
                };
                b.Append(arsonSent[Helper.GetRandomInt(0, arsonSent.Length - 1)]);
            }
            if (courtData.SeverityScore >= 15 || hasLife) {
                if (b.Length > 0) b.Append(" ");
                string[] severity = new[] {
                    "The seriousness of the offence warranted a substantial sentence.",
                    "Given the nature and gravity of the charges, the court imposed a sentence at the upper end of the guideline range.",
                    "The court found that the offences demonstrated a significant threat to public safety.",
                    $"The gravity of the charges led {judge} to impose a sentence reflecting the seriousness of the conduct.",
                    "The nature of the offences and the threat to public safety justified a substantial sentence.",
                    "The court imposed a sentence at the upper end of the guideline range given the seriousness of the offence.",
                    $"The seriousness of the offence and threat to public safety were central to {judge}'s sentencing decision.",
                    "The court found the offences sufficiently grave to warrant a substantial term of imprisonment.",
                    "The nature and gravity of the charges justified a sentence toward the top of the applicable range.",
                    $"Given the threat to public safety, {judge} imposed a substantial sentence.",
                    "The court emphasised the seriousness of the offence and the need to protect the public.",
                    "The offences demonstrated a significant threat to public safety, warranting a substantial sentence.",
                    $"The gravity of the charges and nature of the offences were emphasised by {judge} in sentencing.",
                    "The court imposed a substantial sentence reflecting the seriousness of the conduct and threat to public safety.",
                    "The nature of the offences warranted a sentence at the upper end of the guideline range.",
                    $"The seriousness of the offence and the threat posed to the public informed {judge}'s sentencing approach.",
                    "The court found that the gravity of the charges required a substantial custodial sentence.",
                    "The offences were of sufficient seriousness to justify a sentence toward the top of the range.",
                    $"The nature of the offences, their gravity, and the threat to public safety were cited by {judge}.",
                    "The court imposed a substantial sentence having regard to the seriousness of the offence and protection of the public.",
                    "The gravity of the charges and the substantial threat to public safety warranted an elevated sentence.",
                };
                b.Append(severity[Helper.GetRandomInt(0, severity.Length - 1)]);
            }

            if (courtData.IsJuryTrial && courtData.JurySize > 0) {
                int margin = courtData.JuryVotesForConviction - courtData.JuryVotesForAcquittal;
                if (b.Length > 0) b.Append(" ");
                if (margin >= courtData.JurySize - 1) {
                    string[] unanimous = new[] {
                        "The unanimous jury verdict supported a strong sentencing response.",
                        $"The unanimous verdict of the jury was noted by {judge} in imposing sentence.",
                        "The court took account of the jury's unanimous verdict in fashioning the sentence.",
                        "The jury's unanimous finding of guilt supported the court's sentencing approach.",
                        $"The unanimous jury verdict reinforced {judge}'s assessment of the seriousness of the conduct.",
                        "The court considered the unanimous jury verdict in determining the appropriate sentence.",
                        "The jury's unanimous conviction supported a substantial sentencing response.",
                        $"The unanimous verdict was accorded significant weight by {judge} in sentencing.",
                        "The court noted the jury's unanimous verdict and imposed sentence accordingly.",
                        "The unanimous jury finding of guilt warranted a strong sentencing response.",
                    };
                    b.Append(unanimous[Helper.GetRandomInt(0, unanimous.Length - 1)]);
                } else if (margin <= 2) {
                    string[] narrow = new[] {
                        "The narrow jury verdict was noted; the court balanced the split decision in imposing sentence.",
                        $"The narrow margin of the jury's verdict was considered by {judge} when fashioning the sentence.",
                        "The court took account of the jury's narrow verdict and balanced it in imposing sentence.",
                        $"Given the split jury decision, {judge} exercised caution in determining the sentence.",
                        "The court noted the narrow jury verdict and balanced the split decision accordingly.",
                        $"The jury's narrow verdict was weighed by {judge} in assessing the appropriate sentence.",
                        "The court considered the jury's divided verdict and imposed a sentence reflecting that split.",
                        $"The narrow jury margin was taken into account by {judge} in fashioning the sentence.",
                        "The court balanced the jury's split decision in determining the appropriate sentence.",
                        $"The narrow verdict was noted by {judge}; the sentence reflected the divided jury finding.",
                    };
                    b.Append(narrow[Helper.GetRandomInt(0, narrow.Length - 1)]);
                }
            }

            float policy = courtData.PolicyAdjustment;
            if (policy > 0.03f && b.Length > 0) {
                string[] policyHigh = new[] {
                    $" {judge} imposed a sentence consistent with this district's approach to similar offences.",
                    $" The sentence reflected {judge}'s application of this district's approach to such cases.",
                    $" {judge} applied this jurisdiction's sentencing practice for offences of this nature.",
                    $" The court imposed a sentence in line with local sentencing practice for similar matters.",
                    $" {judge} noted the district's approach to similar offences in determining the sentence.",
                    $" The sentence was consistent with {judge}'s application of local sentencing standards.",
                    $" {judge} imposed a sentence reflective of this district's approach to comparable cases.",
                    $" The court applied the district's established approach to offences of this kind.",
                };
                b.Append(policyHigh[Helper.GetRandomInt(0, policyHigh.Length - 1)]);
            } else if (policy < -0.02f && b.Length > 0) {
                string[] policyLow = new[] {
                    " Mitigating circumstances were considered in fashioning the sentence.",
                    $" {judge} considered mitigating circumstances in determining the appropriate sentence.",
                    " The court took account of mitigating factors in imposing sentence.",
                    $" Mitigating factors were weighed by {judge} in fashioning the sentence.",
                    " The sentence reflected the court's consideration of mitigating circumstances.",
                    $" {judge} applied a sentence that took mitigating circumstances into account.",
                    " The court considered mitigating factors in assessing the appropriate sentence.",
                    $" Mitigating circumstances informed {judge}'s sentencing approach.",
                };
                b.Append(policyLow[Helper.GetRandomInt(0, policyLow.Length - 1)]);
            }

            if (courtData.EvidenceAssaultedPed || courtData.EvidenceHadWeapon) {
                if (b.Length > 0) b.Append(" ");
                string[] violent = new[] {
                    "The violent or threatening conduct at the time of the offence was cited as an aggravating factor.",
                    $"The assaultive behaviour during the offence was noted by {judge} as an aggravating factor.",
                    "The court cited the defendant's violent conduct at the time of the offence as an aggravating factor.",
                    $"The use of a weapon was cited by {judge} as a significant aggravating factor.",
                    "The violent or threatening conduct was weighed as an aggravating factor in sentencing.",
                    $"The court found the assaultive behaviour and threat of violence to be aggravating factors.",
                    $"{judge} noted the defendant's violent or threatening conduct as an aggravating circumstance.",
                    "The defendant's violent conduct at the time of the offence was emphasised as an aggravating factor.",
                    $"The weapon used in the offence was cited by {judge} as an aggravating factor.",
                    "The court considered the violent or threatening nature of the conduct as an aggravating factor.",
                    $"The assaultive behaviour was taken into account by {judge} as an aggravating factor.",
                    "The violent conduct and threat to others were cited as significant aggravating factors.",
                };
                b.Append(violent[Helper.GetRandomInt(0, violent.Length - 1)]);
            }

            if (b.Length == 0) {
                string[] fallback = new[] {
                    $"{judge} considered the nature of the offence, the defendant's background, and the need for punishment and deterrence in imposing sentence.",
                    $"The court considered the nature of the offence, the defendant's background, and the need for punishment and deterrence.",
                    $"{judge} took into account the nature of the offence, rehabilitation prospects, and protection of the public.",
                    "The court considered the defendant's background, the nature of the offence, and the purposes of sentencing.",
                    $"{judge} weighed the nature of the offence, punishment, deterrence, and rehabilitation in imposing sentence.",
                    "The court considered the defendant's background and the need for punishment, deterrence, and protection of the public.",
                    $"{judge} applied the sentencing principles having regard to the nature of the offence and the defendant's circumstances.",
                    "The court considered the nature of the offence, the defendant's background, rehabilitation, and protection of the public.",
                    $"{judge} imposed sentence having considered the nature of the offence and the need for punishment and deterrence.",
                    "The court weighed the defendant's background, the nature of the offence, and the objectives of sentencing.",
                    $"{judge} considered the nature of the offence, punishment, deterrence, and rehabilitation in fashioning the sentence.",
                    "The court took into account the defendant's background, the nature of the offence, and the need for proportionate punishment.",
                    $"{judge} considered the defendant's background, the seriousness of the offence, and protection of the public.",
                    "The court applied the sentencing principles, considering the nature of the offence and the defendant's circumstances.",
                    $"{judge} weighed the nature of the offence, the defendant's background, and the need for punishment, deterrence, and rehabilitation.",
                };
                b.Append(fallback[Helper.GetRandomInt(0, fallback.Length - 1)]);
            }

            return b.ToString().Trim();
        }

        internal static void CheckAndResolvePendingCases() {
            try {
                DateTime now = DateTime.UtcNow;
                List<CourtData> due = courtDatabase
                    .Where(c => c != null
                        && c.Status == 0
                        && !string.IsNullOrEmpty(c.ResolveAtUtc)
                        && DateTime.TryParse(c.ResolveAtUtc, null, DateTimeStyles.RoundtripKind, out DateTime resolveAt)
                        && now >= resolveAt)
                    .ToList();
                foreach (CourtData courtCase in due) {
                    ResolveCaseAuto(courtCase);
                }
            } catch (Exception e) {
                Helper.Log($"Court auto-resolution check failed: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>Force-resolve a pending court case now (run trial/verdict logic immediately). Returns true if the case was found and was pending.</summary>
        /// <param name="caseNumber">Court case number.</param>
        /// <param name="plea">Optional plea to apply before resolving (e.g. from UI selection).</param>
        /// <param name="outcomeNotes">Optional outcome notes to apply before resolving.</param>
        internal static bool ForceResolveCourtCase(string caseNumber, string plea = null, string outcomeNotes = null) {
            if (string.IsNullOrWhiteSpace(caseNumber)) return false;
            CourtData courtCase = courtDatabase.Find(x => x.Number == caseNumber);
            if (courtCase == null || courtCase.Status != 0) return false;
            if (!string.IsNullOrWhiteSpace(plea)) courtCase.Plea = plea;
            if (outcomeNotes != null) courtCase.OutcomeNotes = outcomeNotes;
            ResolveCaseAuto(courtCase);
            return true;
        }

        private static void ResolveCaseAuto(CourtData courtCase) {
            if (courtCase == null) return;
            try {
                string plea = string.IsNullOrWhiteSpace(courtCase.Plea) ? "Not Guilty" : courtCase.Plea.Trim();
                courtCase.Plea = plea;

                bool pleaGuiltyOrNoContest = string.Equals(plea, "Guilty", StringComparison.OrdinalIgnoreCase) || string.Equals(plea, "No Contest", StringComparison.OrdinalIgnoreCase);

                if (courtCase.Charges != null) {
                    foreach (CourtData.Charge charge in courtCase.Charges) {
                        if (pleaGuiltyOrNoContest) {
                            charge.Outcome = 1; // Convicted
                            charge.ConvictionChance = null;
                            charge.SentenceDaysServed = charge.Time;
                        } else {
                            int chance = GetPerChargeConvictionChance(courtCase, charge);
                            charge.ConvictionChance = chance;
                            int roll = Helper.GetRandomInt(1, 100);
                            charge.Outcome = roll <= chance ? 1 : 2; // 1 Convicted, 2 Acquitted
                            if (charge.Outcome == 1) charge.SentenceDaysServed = charge.Time;
                        }
                    }
                }

                int newStatus = (courtCase.Charges != null && courtCase.Charges.Any(c => c.Outcome == 1)) ? 1 : 2;
                courtCase.Status = newStatus;
                courtCase.OutcomeReasoning = BuildOutcomeReasoning(courtCase, courtCase.ConvictionChance, newStatus);
                if (newStatus == 1) {
                    courtCase.SentenceReasoning = BuildSentenceReasoning(courtCase);
                    courtCase.LicenseRevocations = ComputeLicenseRevocations(courtCase);
                    if (courtCase.LicenseRevocations != null && courtCase.LicenseRevocations.Count > 0) {
                        courtCase.OutcomeReasoning += " The court further ordered: " + string.Join("; ", courtCase.LicenseRevocations) + ".";
                    }
                } else {
                    courtCase.SentenceReasoning = null;
                }
                courtCase.LastUpdatedUtc = DateTime.UtcNow.ToString("o");

                if (!string.IsNullOrEmpty(courtCase.PedName)) {
                    string normalized = courtCase.PedName.ToLower();
                    int pedIndex = pedDatabase.FindIndex(p => p.Name?.ToLower() == normalized);
                    if (pedIndex == -1) {
                        MDTProPedData persistent = keepInPedDatabase.FirstOrDefault(p => p.Name?.ToLower() == normalized);
                        if (persistent != null) {
                            pedDatabase.Add(persistent);
                            pedIndex = pedDatabase.Count - 1;
                        }
                    }
                    if (pedIndex >= 0) {
                        MDTProPedData pedData = pedDatabase[pedIndex];
                        Config config = SetupController.GetConfig();
                        if (newStatus == 1) {
                            UpdatePedIncarcerationFromCourtData(pedData, courtCase, config);
                            ApplyLicenseRevocationsToPed(pedData, courtCase.LicenseRevocations);
                        }
                        pedData.IsWanted = false;
                        pedData.WarrantText = null;
                        SyncSinglePedToCDF(pedData);
                        KeepPedInDatabase(pedData);
                        pedDatabase[pedIndex] = pedData;
                        Database.SavePed(pedData);
                    }
                }

                Database.SaveCourtCase(courtCase);
                int convictedCount = courtCase.Charges?.Count(c => c.Outcome == 1) ?? 0;
                Helper.Log($"Court case {courtCase.Number} auto-resolved: {(newStatus == 1 ? "Convicted" : "Acquitted")} ({convictedCount}/{courtCase.Charges?.Count ?? 0} charges) (plea: {courtCase.Plea})", false, Helper.LogSeverity.Info);

                string defendantName = !string.IsNullOrWhiteSpace(courtCase.PedName) ? courtCase.PedName.Trim() : "Unknown";
                string msg = string.Format(Setup.SetupController.GetLanguage().court.trialHeardNotification, courtCase.Number ?? "?", defendantName);
                Utility.RageNotification.Show(msg, Utility.RageNotification.NotificationType.Info);
            } catch (Exception e) {
                Helper.Log($"Auto-resolution failed for case {courtCase?.Number}: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        private static int GetSeverityScore(CourtData courtData) {
            if (courtData?.Charges == null) return 0;

            int score = 0;
            foreach (CourtData.Charge charge in courtData.Charges) {
                score += 1;
                if (charge.IsArrestable == true) score += 3;

                if (charge.Time == null) {
                    score += 15;
                } else if (charge.Time > 0) {
                    score += Math.Min(12, (charge.Time.Value / 30) + 1);
                }

                if (charge.Fine >= 20000) score += 4;
                else if (charge.Fine >= 10000) score += 3;
                else if (charge.Fine >= 5000) score += 2;
            }

            return score;
        }

        private static CourtDistrictProfile ResolveCourtDistrict(Location reportLocation) {
            string county = reportLocation?.County?.ToLower() ?? string.Empty;
            string area = reportLocation?.Area?.ToLower() ?? string.Empty;

            if (county.Contains("blaine")) return BlaineDistrict;
            if (area.Contains("cayo") || area.Contains("north yankton")) return IslandDistrict;
            return LosSantosDistrict;
        }

        private static string SelectRotatingRosterMember(
            string[] roster,
            string district,
            string role,
            string caseNumber,
            int caseWeight = 0) {
            if (roster == null || roster.Length == 0) return null;

            int window = GetCourtRosterWindow(DateTime.UtcNow);
            string key = $"{district}|{role}|{window}|{caseNumber}|{caseWeight}";
            int hash = GetStableHash(key);
            int index = Math.Abs(hash % roster.Length);
            return roster[index];
        }

        private static int GetCourtRosterWindow(DateTime utcNow) {
            DateTime epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int days = (int)Math.Max(0, (utcNow - epoch).TotalDays);
            int rotationDays = SetupController.GetConfig().courtRosterRotationDays;
            if (rotationDays <= 0) rotationDays = DefaultCourtRosterRotationDays;
            return days / rotationDays;
        }

        private static int GetStableHash(string value) {
            unchecked {
                const int fnvOffset = (int)2166136261;
                const int fnvPrime = 16777619;

                int hash = fnvOffset;
                if (string.IsNullOrEmpty(value)) return hash;

                for (int i = 0; i < value.Length; i++) {
                    hash ^= value[i];
                    hash *= fnvPrime;
                }

                return hash;
            }
        }

        private static int CountPriorConvictions(string pedName) {
            if (string.IsNullOrEmpty(pedName)) return 0;
            string normalized = pedName.ToLower();
            return courtDatabase.Count(c => c.PedName?.ToLower() == normalized && c.Status == 1);
        }

        private static void ApplyRepeatOffenderSentencing(CourtData courtData) {
            if (courtData?.Charges == null || courtData.Charges.Count == 0) return;
            if (courtData.SentenceMultiplier <= 1f) return;

            foreach (CourtData.Charge charge in courtData.Charges) {
                charge.Fine = Math.Max(0, (int)Math.Round(charge.Fine * courtData.SentenceMultiplier));

                if (charge.Time.HasValue && charge.Time.Value > 0) {
                    int adjustedDays = (int)Math.Round(charge.Time.Value * courtData.SentenceMultiplier);
                    charge.Time = Math.Max(charge.Time.Value, adjustedDays);
                }
            }
        }

        private static OfficerInformationData GetOfficerInformation() {
            LSPD_First_Response.Engine.Scripting.Entities.Persona persona = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(Main.Player);

            OfficerInformationData result = new OfficerInformationData {
                agency = Helper.GetAgencyNameFromScriptName(LSPD_First_Response.Mod.API.Functions.GetCurrentAgencyScriptName()) ?? LSPD_First_Response.Mod.API.Functions.GetCurrentAgencyScriptName(),
                firstName = persona.Forename,
                lastName = persona.Surname,
                callSign = DependencyCheck.IsIPTCommonAvailable() ? Helper.GetCallSignFromIPTCommon() : null
            };

            return result;
        }

        internal static void SetOfficerInformation() {
            OfficerInformation = GetOfficerInformation();
        }

        private static void UpdatePlayerLocation() {
            if (!Main.Player.IsValid()) return;
            PlayerLocation = new Location(Main.Player.Position);
            PlayerCoords = new PlayerCoords(Main.Player.Position, Main.Player.Heading);
        }
    }
}
