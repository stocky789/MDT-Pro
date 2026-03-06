using CommonDataFramework.Modules;
using CommonDataFramework.Modules.PedDatabase;
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
            Judges = new[] { "Hon. K. Matthews", "Hon. S. Alvarez", "Hon. D. Whitaker", "Hon. T. Ellison" },
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

        // Evidence trigger reliability (for UI: only show reliably tracked items in court breakdown):
        // Reliable: HadWeapon (native at arrest), WasWanted (LSPDFR persona), AssaultedPed (damage native + player check), DamagedVehicle (damage native).
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

        internal static Location PlayerLocation = new Location();
        internal static string CurrentTime = World.TimeOfDay.ToString();
        internal static PlayerCoords PlayerCoords = new PlayerCoords();

        internal static string ActivePostalCodeSet;

        internal static void SetDatabases() {
            SetPedDatabase();
            SetVehicleDatabase();
        }

        internal static void SetDynamicData() {
            UpdatePlayerLocation();
            CurrentTime = World.TimeOfDay.ToString();
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

        internal static void SyncPedDatabaseWithCDF() {
            foreach (MDTProPedData databasePed in PedDatabase) {
                if (databasePed?.CDFPedData == null) continue;
                try {
                    databasePed.CDFPedData.Wanted = databasePed.IsWanted;
                    databasePed.CDFPedData.IsOnProbation = databasePed.IsOnProbation;
                    databasePed.CDFPedData.IsOnParole = databasePed.IsOnParole;
                    if (Enum.TryParse(databasePed.LicenseStatus, out ELicenseState licenseStatusValue)) {
                        databasePed.CDFPedData.DriversLicenseState = licenseStatusValue;
                    }
                } catch (Exception ex) {
                    Helper.Log($"SyncPedDatabaseWithCDF skip ped: {ex.Message}", false, Helper.LogSeverity.Warning);
                }
            }
        }

        internal static void SyncVehicleDatabaseWithCDF() {
            foreach (MDTProVehicleData databaseVehicle in VehicleDatabase) {
                SyncSingleVehicleToCDF(databaseVehicle);
            }
        }

        /// <summary>Push MDT vehicle data to CDF VehicleData so PR and other mods see IsStolen, Registration, Insurance.</summary>
        private static void SyncSingleVehicleToCDF(MDTProVehicleData databaseVehicle) {
            if (databaseVehicle?.CDFVehicleData == null) return;
            try {
                databaseVehicle.CDFVehicleData.IsStolen = databaseVehicle.IsStolen;
                if (databaseVehicle.CDFVehicleData.Registration != null
                    && !string.IsNullOrEmpty(databaseVehicle.RegistrationStatus)
                    && Enum.TryParse(databaseVehicle.RegistrationStatus, out EDocumentStatus registrationStatusValue)) {
                    databaseVehicle.CDFVehicleData.Registration.Status = registrationStatusValue;
                }
                if (databaseVehicle.CDFVehicleData.Insurance != null
                    && !string.IsNullOrEmpty(databaseVehicle.InsuranceStatus)
                    && Enum.TryParse(databaseVehicle.InsuranceStatus, out EDocumentStatus insuranceStatusValue)) {
                    databaseVehicle.CDFVehicleData.Insurance.Status = insuranceStatusValue;
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

            MDTProPedData pedData;
            lock (_pedDbLock) {
                pedData = pedDatabase.FirstOrDefault(x => x.Name?.Equals(pedName, StringComparison.OrdinalIgnoreCase) == true)
                    ?? keepInPedDatabase.FirstOrDefault(x => x.Name?.Equals(pedName, StringComparison.OrdinalIgnoreCase) == true)
                    ?? pedDatabase.FirstOrDefault(x => x.Holder != null && x.Holder.IsValid() && x.Holder.Handle == ped.Handle);
                if (pedData == null) {
                    pedData = new MDTProPedData { Name = pedName };
                    pedData.IdentificationHistory = new List<MDTProPedData.IdentificationEntry>();
                    if (!pedDatabase.Any(x => x.Name == pedName)) pedDatabase.Add(pedData);
                } else {
                    if (!pedDatabase.Any(x => x.Name == pedData.Name)) pedDatabase.Add(pedData);
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

        internal static void UpdatePedData(MDTProPedData pedData) {
            lock (_pedDbLock) {
                int index = pedDatabase.FindIndex(x => x.Name == pedData.Name);
                if (index == -1) {
                    Helper.Log("Failed to update Ped database!", false, Helper.LogSeverity.Warning);
                    return;
                }
                pedDatabase[index] = pedData;
            }
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

        internal static void UpdateVehicleData(MDTProVehicleData vehicleData) {
            lock (_vehicleDbLock) {
                int index = vehicleDatabase.FindIndex(x => x.LicensePlate == vehicleData.LicensePlate);
                if (index == -1) {
                    Helper.Log("Failed to update Vehicle database!", false, Helper.LogSeverity.Warning);
                    return;
                }
                vehicleDatabase[index] = vehicleData;
            }
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
                if (index != -1) {
                    citationReports[index] = citationReport;
                } else {
                    citationReports.Add(citationReport);
                }
                // Notify Policing Redefined so "Give Citation" shows in the ped menu (new or updated citation saved as closed)
                if (Main.usePR && citationReport.Status == ReportStatus.Closed) {
                    var chargesToHand = citationReport.Charges
                        .Select(charge => new PRHelper.CitationHandoutCharge {
                            Name = charge.name,
                            Fine = Helper.GetRandomInt(charge.minFine, charge.maxFine),
                            IsArrestable = charge.isArrestable,
                        })
                        .ToList();

                    PRHelper.GiveCitation(citationReport.OffenderPedName, chargesToHand);
                }
            } else if (report is ArrestReport arrestReport) {
                if (!string.IsNullOrEmpty(arrestReport.OffenderPedName)) {
                    int pedIndex = pedDatabase.FindIndex(pedData => pedData.Name?.ToLower() == arrestReport.OffenderPedName.ToLower());
                    if (pedIndex != -1) {
                        MDTProPedData pedDataToAdd = pedDatabase[pedIndex];

                        pedDataToAdd.Arrests.AddRange(arrestReport.Charges.Where(x => !x.addedByReportInEdit));

                        KeepPedInDatabase(pedDataToAdd);
                        pedDatabase[pedIndex] = pedDataToAdd;
                    }
                }

                if (!string.IsNullOrEmpty(arrestReport.OffenderVehicleLicensePlate)) {
                    MDTProVehicleData vehicleDataToAdd;
                    lock (_vehicleDbLock) {
                        vehicleDataToAdd = vehicleDatabase.FirstOrDefault(vehicleData => vehicleData.LicensePlate?.ToLower() == arrestReport.OffenderVehicleLicensePlate.ToLower());
                    }
                    if (vehicleDataToAdd != null) KeepVehicleInDatabase(vehicleDataToAdd);
                }

                string courtCaseNumber = arrestReport.CourtCaseNumber ?? Helper.GetCourtCaseNumber();

                arrestReport.CourtCaseNumber = courtCaseNumber;

                CourtData courtData = new CourtData(
                    arrestReport.OffenderPedName,
                    courtCaseNumber,
                    arrestReport.Id,
                    int.Parse(DateTime.Now.ToString("yy"))
                    );

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

            MDTProPedData existingPed;
            lock (_pedDbLock) {
                existingPed = pedDatabase.FirstOrDefault(x => x.Name == mdtProPedData.Name);
                if (existingPed != null && existingPed.CDFPedData == null) {
                    existingPed.LicenseStatus = mdtProPedData.LicenseStatus;
                    existingPed.LicenseExpiration = mdtProPedData.LicenseExpiration;
                    existingPed.WeaponPermitStatus = mdtProPedData.WeaponPermitStatus;
                    existingPed.WeaponPermitExpiration = mdtProPedData.WeaponPermitExpiration;
                    existingPed.WeaponPermitType = mdtProPedData.WeaponPermitType;
                    existingPed.FishingPermitStatus = mdtProPedData.FishingPermitStatus;
                    existingPed.FishingPermitExpiration = mdtProPedData.FishingPermitExpiration;
                    existingPed.HuntingPermitStatus = mdtProPedData.HuntingPermitStatus;
                    existingPed.HuntingPermitExpiration = mdtProPedData.HuntingPermitExpiration;
                }
            }
            if (existingPed != null) {
                if (existingPed.CDFPedData == null) Database.SavePed(existingPed);
                SetContextPed(existingPed);
                return;
            }

            TryApplyReEncounterProfile(mdtProPedData);
            lock (_pedDbLock) {
                if (pedDatabase.Any(x => x.Name == mdtProPedData.Name)) return;
                pedDatabase.Add(mdtProPedData);
            }
            Database.SavePed(mdtProPedData);
            SetContextPed(mdtProPedData);
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
                if (charge.Time == null) {
                    hasLifeSentence = true;
                    continue;
                }
                if (charge.Time > 0) totalDays += charge.Time.Value;
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
            courtCase.LastUpdatedUtc = DateTime.UtcNow.ToString("o");

            if (!string.IsNullOrEmpty(courtCase.PedName)) {
                int pedIndex = pedDatabase.FindIndex(pedData => pedData.Name?.ToLower() == courtCase.PedName?.ToLower());
                if (pedIndex != -1) {
                    MDTProPedData pedData = pedDatabase[pedIndex];

                    if (status == 1) {
                        UpdatePedIncarcerationFromCourtData(pedData, courtCase);
                        pedData.IsOnProbation = true;
                        pedData.IsWanted = false;
                    } else if (status == 2 || status == 3) {
                        pedData.IsWanted = false;
                    }

                    KeepPedInDatabase(pedData);
                    pedDatabase[pedIndex] = pedData;
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
                bool wasWanted = persona.Wanted;
                bool wasDrunk = NativeFunction.Natives.IS_PED_DRUNK<bool>(ped);
                bool wasFleeing = NativeFunction.Natives.IS_PED_FLEEING<bool>(ped);

                // clearAfterRead: false so we don't clear damage state on first check (improves reliability)
                Ped[] nearbyPeds = ped.GetNearbyPeds(50);
                bool assaultedPed = false;
                Ped playerPed = Main.Player;
                if (playerPed != null && playerPed.IsValid() && playerPed != ped &&
                    NativeFunction.Natives.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY<bool>(playerPed, ped, false))
                    assaultedPed = true;
                if (!assaultedPed && nearbyPeds != null) {
                    assaultedPed = nearbyPeds.Any(victim =>
                        victim != ped && victim.IsValid() &&
                        NativeFunction.Natives.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY<bool>(victim, ped, false));
                }

                Vehicle[] nearbyVehicles = ped.GetNearbyVehicles(20);
                bool damagedVehicle = nearbyVehicles != null && nearbyVehicles.Any(v =>
                    v.IsValid() &&
                    NativeFunction.Natives.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY<bool>(v, ped, false));

                // Illegal weapon carry: armed but weapon permit status is not valid (requires CDF data)
                // Also check probation/parole violation. Use Holder fallback for re-encounters.
                bool hadIllegalWeapon = false;
                bool violatedSupervision = false;
                MDTProPedData dbPed = GetPedDataForPed(ped);
                if (dbPed != null) {
                    if (hadWeapon && !string.IsNullOrEmpty(dbPed.WeaponPermitStatus)) {
                        hadIllegalWeapon = !dbPed.WeaponPermitStatus.Equals("Valid", StringComparison.OrdinalIgnoreCase);
                    }
                    if (dbPed.IsOnProbation || dbPed.IsOnParole) {
                        violatedSupervision = true;
                    }
                }

                bool resisted = GetPedResistanceFromPR(ped);

                string cacheKey = dbPed?.Name ?? persona.FullName;
                lock (pedEvidenceLock) {
                    PruneStaleEvidenceEntries();
                    if (!pedEvidenceCache.TryGetValue(cacheKey, out PedEvidenceContext ctx)) {
                        ctx = new PedEvidenceContext();
                        pedEvidenceCache[cacheKey] = ctx;
                    }
                    ctx.HadWeapon = hadWeapon;
                    ctx.WasWanted = wasWanted;
                    ctx.WasDrunk = wasDrunk;
                    ctx.WasFleeing = ctx.WasFleeing || wasFleeing;
                    ctx.AssaultedPed = assaultedPed;
                    ctx.DamagedVehicle = damagedVehicle;
                    ctx.HadIllegalWeapon = hadIllegalWeapon;
                    ctx.ViolatedSupervision = violatedSupervision;
                    ctx.Resisted = resisted;
                    ctx.CapturedAt = DateTime.UtcNow;
                }
            } catch (Exception e) {
                Helper.Log($"Evidence capture failed: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        internal static void MarkPedFleeing(Ped ped) {
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
                    ctx.WasFleeing = true;
                    ctx.CapturedAt = DateTime.UtcNow;
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
            } catch (Exception e) {
                Helper.Log($"PatDown capture failed: {e.Message}", false, Helper.LogSeverity.Warning);
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
            courtData.SentenceMultiplier = Math.Min(
                MaxSentenceMultiplier,
                Math.Max(
                    1f,
                    1f
                    + (repeatScore * 0.08f)
                    + (severity * 0.02f)
                    + (outcomeMomentum * 0.35f)
                    + (courtData.DocketPressure * 0.2f)
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
                int votesForConviction = 0;
                for (int i = 0; i < courtData.JurySize; i++) {
                    if (Helper.GetRandomInt(1, 100) <= convictionChance) votesForConviction++;
                }
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

        private static int GetEvidenceScore(CourtData courtData, Config config) {
            if (courtData?.Charges == null || courtData.Charges.Count == 0) return 0;

            float score = config.courtEvidenceBase;
            foreach (CourtData.Charge charge in courtData.Charges) {
                score += config.courtEvidencePerCharge;
                if (charge.IsArrestable == true) score += config.courtEvidenceArrestableBonus;
                if (charge.Time == null) score += config.courtEvidenceLifeSentenceBonus;
            }

            if (!string.IsNullOrEmpty(courtData.PedName)) {
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

        private static string BuildOutcomeReasoning(CourtData courtData, int convictionChance, int resolvedStatus) {
            var b = new StringBuilder();
            string tribunal = courtData.IsJuryTrial ? "jury" : "court";

            if (resolvedStatus == 1) {
                if (courtData.Plea == "Guilty") {
                    b.Append("The defendant entered a guilty plea. The court accepted the plea and proceeded directly to sentencing.");
                } else if (courtData.Plea == "No Contest") {
                    b.Append("The defendant entered a no contest plea, neither admitting nor denying the charges. The court accepted the plea and returned a guilty verdict.");
                } else if (convictionChance >= 70) {
                    b.Append($"The {tribunal} returned a guilty verdict. The prosecution built an overwhelming case against the defendant.");
                } else if (convictionChance >= 50) {
                    b.Append($"After deliberation, the {tribunal} found the defendant guilty.");
                } else {
                    b.Append($"In a closely contested case, the {tribunal} ultimately returned a guilty verdict.");
                }
                var factors = new List<string>();
                if (courtData.EvidenceHadWeapon) factors.Add("the defendant was armed at the time of arrest");
                if (courtData.EvidenceWasWanted) factors.Add("an active warrant was outstanding");
                if (courtData.EvidenceViolatedSupervision) factors.Add("the defendant was on probation or parole");
                if (courtData.EvidenceWasDrunk) factors.Add("the defendant was visibly intoxicated");
                if (courtData.EvidenceWasFleeing) factors.Add("the defendant attempted to flee");
                if (courtData.EvidenceAssaultedPed) factors.Add("the defendant committed assault");
                if (courtData.EvidenceResisted) factors.Add("the defendant resisted arrest");
                if (factors.Count > 0)
                    b.Append($" Key factors: {string.Join("; ", factors)}.");
                if (courtData.RepeatOffenderScore >= 5)
                    b.Append(" The defendant's prior criminal record weighed heavily in the verdict.");
                if (courtData.IsJuryTrial)
                    b.Append($" The jury voted {courtData.JuryVotesForConviction}-{courtData.JuryVotesForAcquittal} in favour of conviction.");
            } else if (resolvedStatus == 2) {
                if (convictionChance <= 35) {
                    b.Append($"The {tribunal} found the defendant not guilty. The prosecution failed to establish guilt beyond a reasonable doubt.");
                } else if (convictionChance <= 55) {
                    b.Append($"The {tribunal} returned a not guilty verdict. The defence successfully raised reasonable doubt.");
                } else {
                    b.Append($"Despite a strong prosecution case, the {tribunal} returned a not guilty verdict.");
                }
                if (!courtData.HasPublicDefender)
                    b.Append($" Private counsel {courtData.DefenseAttorneyName} mounted an effective defence.");
                if (courtData.IsJuryTrial)
                    b.Append($" The jury voted {courtData.JuryVotesForAcquittal}-{courtData.JuryVotesForConviction} in favour of acquittal.");
            } else if (resolvedStatus == 3) {
                b.Append("Charges were dismissed. The case did not proceed to trial.");
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
        internal static bool ForceResolveCourtCase(string caseNumber) {
            if (string.IsNullOrWhiteSpace(caseNumber)) return false;
            CourtData courtCase = courtDatabase.Find(x => x.Number == caseNumber);
            if (courtCase == null || courtCase.Status != 0) return false;
            ResolveCaseAuto(courtCase);
            return true;
        }

        private static void ResolveCaseAuto(CourtData courtCase) {
            try {
                int newStatus;
                if (courtCase.Plea == "Guilty" || courtCase.Plea == "No Contest") {
                    newStatus = 1;
                } else {
                    int roll = Helper.GetRandomInt(1, 100);
                    newStatus = roll <= courtCase.ConvictionChance ? 1 : 2;
                }

                courtCase.Status = newStatus;
                courtCase.OutcomeReasoning = BuildOutcomeReasoning(courtCase, courtCase.ConvictionChance, newStatus);
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
                        }
                        pedData.IsWanted = false;
                        KeepPedInDatabase(pedData);
                        pedDatabase[pedIndex] = pedData;
                    }
                }

                Database.SaveCourtCase(courtCase);
                Helper.Log($"Court case {courtCase.Number} auto-resolved: {(newStatus == 1 ? "Convicted" : "Acquitted")} (plea: {courtCase.Plea}, chance: {courtCase.ConvictionChance}%)", false, Helper.LogSeverity.Info);
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
            DateTime epoch = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
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
