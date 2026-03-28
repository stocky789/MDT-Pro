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
        /// <summary>Serializes case-number allocation and all in-memory <see cref="courtDatabase"/> list mutations. Person Search + supervision backstory run on the HTTP thread while arrest close runs there too; concurrent List enumeration was throwing and aborting arrest → court creation.</summary>
        private static readonly object _courtDatabaseLock = new object();
        private const float MaxSentenceMultiplier = 2.5f;
        private const int DefaultJuryTrialSeverityThreshold = 15;
        private const int DefaultCourtRosterRotationDays = 14;

        private class LawFirmRoster {
            public string Name;
            public string[] Lawyers;
        }

        private class CourtDistrictProfile {
            public string District;
            public string CourtName;
            public string CourtType;
            public string[] Judges;
            public string ProsecutionOffice;
            public string[] ProsecutionLawyers;
            public LawFirmRoster[] DefenseFirms; // First is Public Defender, rest are private firms
            public float PolicyAdjustment;
        }

        // Lore-friendly GTA law firms: Slaughter SS&S (GTA V), Hammerstein & Faust (GTA V), Delio and Furax (Vice City),
        // Goldberg Ligner & Shyster (Liberty City), Rakin and Ponzer (GTA III)
        private static readonly CourtDistrictProfile LosSantosDistrict = new CourtDistrictProfile {
            District = "Los Santos Judicial District",
            CourtName = "Los Santos Superior Court",
            CourtType = "Superior Court",
            Judges = new[] { "Hon. Hugh Harrison", "Hon. K. Martinez", "Hon. S. Alvarez", "Hon. D. Whitaker", "Hon. T. Ellison", "Hon. M. Chen", "Hon. L. Torres", "Hon. F. Okonkwo" },
            ProsecutionOffice = "Los Santos County District Attorney's Office",
            ProsecutionLawyers = new[] { "A. Mercer", "L. O'Neil", "M. Reeves", "T. Walsh", "J. Santos" },
            DefenseFirms = new[] {
                new LawFirmRoster { Name = "Public Defender Office", Lawyers = new[] { "M. Chen", "R. Foster", "D. Okonkwo", "K. Hayes" } },
                new LawFirmRoster { Name = "Slaughter, Slaughter & Slaughter", Lawyers = new[] { "C. Price", "V. Slaughter", "R. Slaughter", "D. Mercer", "S. Torres" } },
                new LawFirmRoster { Name = "Hammerstein & Faust", Lawyers = new[] { "N. Hammerstein", "F. Faust", "K. Lindell", "P. Reed" } },
                new LawFirmRoster { Name = "Delio and Furax", Lawyers = new[] { "M. Delio", "J. Furax", "R. Vance", "L. Cruz" } },
            },
            PolicyAdjustment = 0.02f,
        };

        private static readonly CourtDistrictProfile BlaineDistrict = new CourtDistrictProfile {
            District = "Blaine County Circuit",
            CourtName = "Blaine County Courthouse",
            CourtType = "Circuit Court",
            Judges = new[] { "Hon. R. Bennett", "Hon. J. Monroe", "Hon. P. Gaines", "Hon. V. Cross" },
            ProsecutionOffice = "Blaine County District Attorney's Office",
            ProsecutionLawyers = new[] { "T. Caldwell", "J. Holloway", "D. Pritchard", "B. Ellis", "M. Tate" },
            DefenseFirms = new[] {
                new LawFirmRoster { Name = "Public Defender Office", Lawyers = new[] { "N. Harper", "M. Lott", "C. Webb", "J. Riley" } },
                new LawFirmRoster { Name = "Slaughter, Slaughter & Slaughter", Lawyers = new[] { "C. Price", "N. Harper", "M. Lott", "S. Torres" } },
                new LawFirmRoster { Name = "Hammerstein & Faust", Lawyers = new[] { "P. Gaines", "L. Monroe", "K. Lindell" } },
                new LawFirmRoster { Name = "Rakin and Ponzer", Lawyers = new[] { "A. Rakin", "J. Ponzer", "T. Mills" } },
            },
            PolicyAdjustment = 0.05f,
        };

        private static readonly CourtDistrictProfile IslandDistrict = new CourtDistrictProfile {
            District = "Special Territory Docket",
            CourtName = "San Andreas Territorial Tribunal",
            CourtType = "Special Jurisdiction Court",
            Judges = new[] { "Hon. I. Navarro", "Hon. V. Reese", "Hon. Grady", "Hon. Barry Griffin" },
            ProsecutionOffice = "San Andreas Territorial Prosecutor's Office",
            ProsecutionLawyers = new[] { "S. DeLuca", "E. Rowan", "B. Torres", "F. Maddox" },
            DefenseFirms = new[] {
                new LawFirmRoster { Name = "Public Defender Office", Lawyers = new[] { "B. Donovan", "F. Maddox", "G. Santos", "H. Velez" } },
                new LawFirmRoster { Name = "Goldberg, Ligner & Shyster", Lawyers = new[] { "P. Ligner", "D. Shyster", "L. Ligner", "K. Jenkins" } },
                new LawFirmRoster { Name = "Slaughter, Slaughter & Slaughter", Lawyers = new[] { "B. Donovan", "F. Maddox", "C. Price" } },
                new LawFirmRoster { Name = "Rakin and Ponzer", Lawyers = new[] { "A. Rakin", "J. Ponzer", "D. Grossman" } },
            },
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

        /// <summary>Format one candidate docket id; caller must hold <see cref="_courtDatabaseLock"/> if substituting index.</summary>
        private static string FormatCourtCaseNumberCandidate(Config cfg, int index) {
            string number = cfg.courtCaseNumberFormat ?? "{shortYear}-{index}";
            int pad = cfg.courtCaseNumberIndexPad > 0 ? cfg.courtCaseNumberIndexPad : 1;
            number = number.Replace("{shortYear}", DateTime.Now.ToString("yy", CultureInfo.InvariantCulture));
            number = number.Replace("{year}", DateTime.Now.ToString("yyyy", CultureInfo.InvariantCulture));
            number = number.Replace("{month}", DateTime.Now.ToString("MM", CultureInfo.InvariantCulture));
            number = number.Replace("{day}", DateTime.Now.ToString("dd", CultureInfo.InvariantCulture));
            number = number.Replace("{index}", index.ToString().PadLeft(pad, '0'));
            return number;
        }

        /// <summary>Next unused docket id; caller must hold <see cref="_courtDatabaseLock"/>.</summary>
        /// <remarks>
        /// Do not use (1 + count of cases for the year) as the index: gaps exist (deleted rows, synthetic cases, manual DB edits),
        /// so that value can equal an existing <see cref="CourtData.Number"/> (e.g. 42 rows for &apos;26 but docket 26-000043 already taken).
        /// </remarks>
        private static string AllocateCourtCaseNumberUnderLock() {
            Config cfg = SetupController.GetConfig();
            int yy = int.Parse(DateTime.Now.ToString("yy", CultureInfo.InvariantCulture));
            int seedIndex = 1;
            foreach (CourtData caseData in courtDatabase) {
                if (caseData != null && caseData.ShortYear == yy) seedIndex++;
            }
            const int maxAttempts = 500_000;
            string template = cfg.courtCaseNumberFormat ?? "{shortYear}-{index}";
            bool hasIndexToken = template.IndexOf("{index}", StringComparison.Ordinal) >= 0;
            for (int i = 0; i < maxAttempts; i++) {
                string candidate = FormatCourtCaseNumberCandidate(cfg, seedIndex + i);
                if (!courtDatabase.Any(x => x != null && string.Equals(x.Number, candidate, StringComparison.Ordinal)))
                    return candidate;
                if (!hasIndexToken)
                    break;
            }
            Helper.Log(
                "[MDTPro] AllocateCourtCaseNumber: could not find free docket after max attempts; using time-based suffix.",
                true,
                Helper.LogSeverity.Warning);
            string fallback = FormatCourtCaseNumberCandidate(cfg, seedIndex + (int)(DateTime.UtcNow.Ticks % 900_000));
            if (courtDatabase.Any(x => x != null && string.Equals(x.Number, fallback, StringComparison.Ordinal)))
                fallback = $"{DateTime.Now:yy}-{(DateTime.UtcNow.Ticks & 0xFFFFFF):x6}";
            return fallback;
        }

        internal static string AllocateCourtCaseNumber() {
            lock (_courtDatabaseLock) {
                return AllocateCourtCaseNumberUnderLock();
            }
        }

        internal static CourtData FindCourtCaseByNumber(string number) {
            if (string.IsNullOrEmpty(number)) return null;
            lock (_courtDatabaseLock) {
                return courtDatabase.Find(x => x.Number == number);
            }
        }

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
                ourPed.IsOnProbation = cdf.IsOnProbation;
                ourPed.IsOnParole = cdf.IsOnParole;
                EnsureSupervisionCourtBackstory(ourPed);
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
                    pedData.Citations = new List<CitationGroup.Charge>();
                    pedData.Arrests = new List<ArrestGroup.Charge>();
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
            foreach (MDTProPedData data in fileContent) {
                if (data == null || data.Name == null) continue;
                EnsureSupervisionCourtBackstory(data);
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
                EnsureSupervisionCourtBackstory(mdtProPedData);
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

                        pedDataToAdd.Citations ??= new List<CitationGroup.Charge>();
                        pedDataToAdd.Citations.AddRange((citationReport.Charges ?? Enumerable.Empty<CitationReport.Charge>()).Where(x => x != null && !x.addedByReportInEdit));

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
                    var chargesToHand = (citationReport.Charges ?? Enumerable.Empty<CitationReport.Charge>())
                        .Where(charge => charge != null)
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
                Helper.LogArrestCourtVerbose(
                    $"AddReport(arrest): Id={arrestReport.Id}, Status={(int)arrestReport.Status} ({arrestReport.Status}), Offender={arrestReport.OffenderPedName ?? "?"}, Charges={(arrestReport.Charges?.Count ?? 0)}, ExistingCourtCaseNo={arrestReport.CourtCaseNumber ?? "(none)"}");

                if (!string.IsNullOrEmpty(arrestReport.OffenderPedName)) {
                    MDTProPedData pedDataToAdd = GetPedDataByName(arrestReport.OffenderPedName);
                    if (pedDataToAdd != null) {
                        pedDataToAdd.Arrests ??= new List<ArrestGroup.Charge>();
                        pedDataToAdd.Arrests.AddRange((arrestReport.Charges ?? Enumerable.Empty<ArrestReport.Charge>()).Where(x => x != null && !x.addedByReportInEdit));

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
                    Helper.LogArrestCourtVerbose($"AddReport(arrest): entering Closed branch for report {arrestReport.Id}");
                    lock (_courtDatabaseLock) {
                        string courtCaseNumber = arrestReport.CourtCaseNumber ?? AllocateCourtCaseNumberUnderLock();
                        // Web UI can send a stale CourtCaseNumber (e.g. recycled DOM / wrong dataset). If that # is already another docket, we used to skip Add entirely — no new case, arrest still "closed for court".
                        CourtData holder = courtDatabase.FirstOrDefault(x => x != null && string.Equals(x.Number, courtCaseNumber, StringComparison.Ordinal));
                        if (holder != null && !string.Equals(holder.ReportId, arrestReport.Id, StringComparison.Ordinal)) {
                            Helper.Log(
                                $"[MDTPro] Arrest {arrestReport.Id}: case # {courtCaseNumber} is already docket for report {holder.ReportId} (defendant {holder.PedName ?? "?"}); allocating a new number.",
                                false,
                                Helper.LogSeverity.Warning);
                            courtCaseNumber = AllocateCourtCaseNumberUnderLock();
                        }
                        arrestReport.CourtCaseNumber = courtCaseNumber;
                        Helper.LogArrestCourtVerbose($"AddReport(arrest): court case number={courtCaseNumber}, in-memory court count={courtDatabase.Count}");

                        CourtData courtData = new CourtData(
                            arrestReport.OffenderPedName,
                            courtCaseNumber,
                            arrestReport.Id,
                            int.Parse(DateTime.Now.ToString("yy"))
                        );

                        if (arrestReport.AttachedReportIds != null && arrestReport.AttachedReportIds.Count > 0) {
                            courtData.AttachedReportIds.AddRange(arrestReport.AttachedReportIds);
                        }

                        foreach (ArrestReport.Charge charge in arrestReport.Charges ?? Enumerable.Empty<ArrestReport.Charge>()) {
                            if (charge == null) continue;
                            int minDays = charge.minDays;
                            int? maxDays = charge.maxDays;
                            int? time;
                            int rangeMin;
                            int? rangeMax;
                            if (maxDays == null) {
                                // Life sentence charge: either life or a range (minDays to minDays*2)
                                if (Helper.GetRandomInt(0, 1) == 0) {
                                    time = Helper.GetRandomInt(minDays, Math.Max(minDays, minDays * 2));
                                    rangeMin = minDays;
                                    rangeMax = Math.Max(minDays, minDays * 2);
                                } else {
                                    time = null;
                                    rangeMin = 0;
                                    rangeMax = null; // Life
                                }
                            } else {
                                // Store base min for severity; roll at resolution
                                time = minDays;
                                rangeMin = minDays;
                                rangeMax = maxDays;
                            }
                            courtData.AddCharge(
                                new CourtData.Charge(
                                    charge.name,
                                    Helper.GetRandomInt(charge.minFine, charge.maxFine),
                                    time,
                                    charge.isArrestable,
                                    rangeMin,
                                    rangeMax
                                )
                            );
                        }

                        courtData.EvidenceUseOfForce = arrestReport.UseOfForce != null && !string.IsNullOrEmpty(arrestReport.UseOfForce.Type);
                        BuildCourtCaseMetadata(courtData, arrestReport.OffenderPedName, arrestReport.Location);
                        ApplyRepeatOffenderSentencing(courtData);

                        if (!courtDatabase.Any(x => x.Number == courtCaseNumber)) {
                            if (courtDatabase.Count > SetupController.GetConfig().courtDatabaseMaxEntries) {
                                string evicted = courtDatabase[0]?.Number;
                                Database.DeleteCourtCase(courtDatabase[0].Number);
                                courtDatabase.RemoveAt(0);
                                Helper.LogArrestCourtVerbose($"AddReport(arrest): evicted oldest case {evicted} (max entries reached)");
                            }
                            courtDatabase.Add(courtData);
                            Helper.LogArrestCourtVerbose($"AddReport(arrest): added case {courtCaseNumber} to list; calling Database.SaveCourtCase (defendant={courtData.PedName}, reportId={courtData.ReportId})");
                            // Persist here so a later Find/Save in the HTTP handler is not the only path (avoids lost cases if lookup races).
                            Database.SaveCourtCase(courtData);
                            Helper.Log(
                                $"[MDTPro] Court case {courtCaseNumber} created for arrest {arrestReport.Id} (defendant {arrestReport.OffenderPedName ?? "?"}).",
                                false,
                                Helper.LogSeverity.Info);
                            Helper.LogArrestCourtVerbose($"AddReport(arrest): Database.SaveCourtCase finished for {courtCaseNumber}");
                        } else {
                            Helper.LogArrestCourtVerbose($"AddReport(arrest): SKIPPED add — case number {courtCaseNumber} already in courtDatabase (re-save or duplicate?)");
                        }
                    }
                } else {
                    Helper.LogArrestCourtVerbose($"AddReport(arrest): not Closed (status={(int)arrestReport.Status}); no court case branch");
                }

                // Do not take _pedDbLock while holding _courtDatabaseLock (deadlock with game thread: ped lock → EnsureSupervision → court lock).
                if (arrestReport.Status == ReportStatus.Closed
                    && !string.IsNullOrEmpty(arrestReport.CourtCaseNumber)
                    && !string.IsNullOrEmpty(arrestReport.OffenderPedName)) {
                    CourtData linkedCase = FindCourtCaseByNumber(arrestReport.CourtCaseNumber);
                    if (linkedCase != null) {
                        Helper.LogArrestCourtVerbose($"AddReport(arrest): linked case {arrestReport.CourtCaseNumber} for ped incarceration sync ({arrestReport.OffenderPedName})");
                        lock (_pedDbLock) {
                            int pedIndex = pedDatabase.FindIndex(pedData => pedData.Name?.ToLower() == arrestReport.OffenderPedName.ToLower());
                            if (pedIndex != -1) {
                                UpdatePedIncarcerationFromCourtData(pedDatabase[pedIndex], linkedCase);
                            }
                        }
                        MDTProPedData pedRef = GetPedDataByName(arrestReport.OffenderPedName);
                        if (pedRef != null) KeepPedInDatabase(pedRef);
                    } else {
                        Helper.LogArrestCourtVerbose($"AddReport(arrest): WARNING Closed with CourtCaseNumber={arrestReport.CourtCaseNumber} but FindCourtCaseByNumber returned null (ped sync skipped)");
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

            string liveName = currentPedData.Name;
            currentPedData.ApplyPersistentRecordPreservingLiveIdentity(persistentMatch);
            currentPedData.TimesStopped = Math.Max(currentPedData.TimesStopped, persistentMatch.TimesStopped + 1);
            currentPedData.TryParseNameIntoFirstLast();

            if (currentPedData.CDFPedData != null) {
                currentPedData.CDFPedData.Wanted = currentPedData.IsWanted;
                currentPedData.CDFPedData.IsOnProbation = currentPedData.IsOnProbation;
                currentPedData.CDFPedData.IsOnParole = currentPedData.IsOnParole;
                currentPedData.CDFPedData.Citations = currentPedData.Citations?.Count ?? 0;
                currentPedData.CDFPedData.TimesStopped = currentPedData.TimesStopped;
                currentPedData.TrySyncCDFPersonaToPersistentIdentity();
                SyncSinglePedToCDF(currentPedData);
            }

            EnsureSupervisionCourtBackstory(currentPedData);
            KeepPedInDatabase(currentPedData);
            Helper.Log($"Re-encounter merged prior record (model + name match); live identity kept: {liveName}", false, Helper.LogSeverity.Info);
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
                    // Supervision must track live CDF too; otherwise DB stays false while the game shows probation/parole and synthetic court never runs
                    existingPed.IsOnProbation = mdtProPedData.IsOnProbation;
                    existingPed.IsOnParole = mdtProPedData.IsOnParole;
                    // Always refresh portrait model from the live ped (was incorrectly gated on CDFPedData == null, leaving stale ModelName for most peds)
                    if (mdtProPedData.ModelHash != 0) existingPed.ModelHash = mdtProPedData.ModelHash;
                    if (!string.IsNullOrEmpty(mdtProPedData.ModelName)) existingPed.ModelName = mdtProPedData.ModelName;
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
                        existingPed.TryParseNameIntoFirstLast();
                    }
                }
            }
            if (existingPed != null) {
                EnsureSupervisionCourtBackstory(existingPed);
                KeepPedInDatabase(existingPed);
                Database.SavePed(existingPed);
                SetContextPed(existingPed);
                return;
            }

            TryApplyReEncounterProfile(mdtProPedData);
            EnsureSupervisionCourtBackstory(mdtProPedData);
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
                            existing.IsOnProbation = updated.IsOnProbation;
                            existing.IsOnParole = updated.IsOnParole;
                            existing.TryParseNameIntoFirstLast();
                        }
                        EnsureSupervisionCourtBackstory(existing);
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

        /// <summary>True if the world ped's identity (CDF or LSPDFR persona) matches the MDT record name.</summary>
        internal static bool LivePedIdentityMatchesRecord(Ped p, MDTProPedData pedData) {
            if (p == null || !p.IsValid() || pedData == null || string.IsNullOrWhiteSpace(pedData.Name)) return false;
            string live = null;
            try { live = p.GetPedData()?.FullName; } catch { }
            if (string.IsNullOrWhiteSpace(live)) {
                try {
                    var persona = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(p);
                    if (persona != null && !string.IsNullOrEmpty(persona.FullName)) live = persona.FullName;
                } catch { }
            }
            if (string.IsNullOrWhiteSpace(live)) return false;
            return string.Equals(live.Trim(), pedData.Name.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Refresh ModelHash/ModelName from a live ped (Holder or recently ID'd handle) so Person Search ID photos match the entity in the world. CDF does not supply portrait data; we use GTA model name against public ped image CDNs.</summary>
        internal static bool TryRefreshPedModelFromLiveWorld(MDTProPedData pedData, string searchName, string reversedSearchName) {
            if (pedData == null) return false;
            try {
                if (pedData.Holder != null && pedData.Holder.IsValid()) {
                    uint h = (uint)pedData.Holder.Model.Hash;
                    string n = pedData.Holder.Model.Name;
                    if (h != 0 && !string.IsNullOrEmpty(n)) {
                        pedData.ModelHash = h;
                        pedData.ModelName = n;
                        return true;
                    }
                }
                foreach (string key in new[] { searchName, reversedSearchName, pedData.Name }) {
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    var handleOpt = GetRecentlyIdentifiedPedHandle(key.Trim());
                    if (!handleOpt.HasValue) continue;
                    try {
                        Ped live = World.GetEntityByHandle<Ped>(handleOpt.Value);
                        if (live == null || !live.IsValid()) continue;
                        if (!LivePedIdentityMatchesRecord(live, pedData)) continue;
                        uint mh = (uint)live.Model.Hash;
                        string mn = live.Model.Name;
                        if (mh == 0 || string.IsNullOrEmpty(mn)) continue;
                        pedData.ModelHash = mh;
                        pedData.ModelName = mn;
                        return true;
                    } catch { }
                }
            } catch { }
            return false;
        }

        /// <summary>Re-read probation/parole from a live ped (holder or recent ID handle) so SQLite-backed records catch up with CDF; then ensure synthetic supervision court case exists.</summary>
        internal static bool TryRefreshSupervisionFromLiveWorld(MDTProPedData pedData, string searchName, string reversedSearchName) {
            if (pedData == null || string.IsNullOrWhiteSpace(pedData.Name)) return false;
            try {
                Ped live = null;
                if (pedData.Holder != null && pedData.Holder.IsValid())
                    live = pedData.Holder;
                if (live == null) {
                    foreach (string key in new[] { searchName, reversedSearchName, pedData.Name }) {
                        if (string.IsNullOrWhiteSpace(key)) continue;
                        var handleOpt = GetRecentlyIdentifiedPedHandle(key.Trim());
                        if (!handleOpt.HasValue) continue;
                        try {
                            Ped p = World.GetEntityByHandle<Ped>(handleOpt.Value);
                            if (p == null || !p.IsValid()) continue;
                            if (!LivePedIdentityMatchesRecord(p, pedData)) continue;
                            live = p;
                            break;
                        } catch { }
                    }
                }
                if (live == null || !live.IsValid()) return false;
                PedData cdf = null;
                try { cdf = live.GetPedData(); } catch { }
                if (cdf == null) return false;
                bool prob = cdf.IsOnProbation;
                bool par = cdf.IsOnParole;
                bool changed = pedData.IsOnProbation != prob || pedData.IsOnParole != par;
                pedData.IsOnProbation = prob;
                pedData.IsOnParole = par;
                EnsureSupervisionCourtBackstory(pedData);
                if (changed) {
                    KeepPedInDatabase(pedData);
                    Database.SavePed(pedData);
                }
                return true;
            } catch { }
            return false;
        }

        /// <summary>GTA peds share models; only merge persistent history when names clearly match so we do not graft one person's record onto another (e.g. Stan Pierce vs Stan Bank).</summary>
        private static bool PedNamesConsistentForModelReEncounter(MDTProPedData persisted, MDTProPedData live) {
            if (persisted == null || live == null) return false;
            string pn = (persisted.Name ?? "").Trim();
            string ln = (live.Name ?? "").Trim();
            if (pn.Length > 0 && ln.Length > 0 && string.Equals(pn, ln, StringComparison.OrdinalIgnoreCase))
                return true;
            string pLast = (persisted.LastName ?? "").Trim();
            string lLast = (live.LastName ?? "").Trim();
            if (pLast.Length == 0 || lLast.Length == 0) return false;
            if (!string.Equals(pLast, lLast, StringComparison.OrdinalIgnoreCase)) return false;
            string pFirst = (persisted.FirstName ?? "").Trim();
            string lFirst = (live.FirstName ?? "").Trim();
            if (pFirst.Length == 0 || lFirst.Length == 0) return true;
            return string.Equals(pFirst, lFirst, StringComparison.OrdinalIgnoreCase);
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
                    .Where(ped => PedNamesConsistentForModelReEncounter(ped, currentPedData))
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

        /// <summary>
        /// Ensures any ped on probation/parole has prior arrest charges and a closed synthetic court case explaining the disposition.
        /// Charges are sampled from arrestOptions.json (coherent multi-count). Idempotent per ped via <see cref="CourtData.SyntheticSupervisionReportId"/>.
        /// </summary>
        internal static void EnsureSupervisionCourtBackstory(MDTProPedData ped) {
            if (ped == null || string.IsNullOrWhiteSpace(ped.Name)) return;
            if (!ped.IsOnProbation && !ped.IsOnParole) return;

            lock (_courtDatabaseLock) {
                CourtData existing = courtDatabase.FirstOrDefault(c =>
                    c != null
                    && string.Equals(c.PedName, ped.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.ReportId, CourtData.SyntheticSupervisionReportId, StringComparison.Ordinal));

                if (existing != null) {
                    if (ped.Arrests == null || ped.Arrests.Count == 0) {
                        ped.Arrests = (existing.Charges ?? new List<CourtData.Charge>())
                            .Where(ch => ch != null && !string.IsNullOrWhiteSpace(ch.Name))
                            .Select(ch => SupervisionBackstoryHelper.FindArrestChargeTemplate(ch.Name))
                            .ToList();
                        Database.SavePed(ped);
                    }
                    return;
                }

                var coherent = SupervisionBackstoryHelper.BuildCoherentSupervisionCharges(ped.IsOnParole);
                if (coherent == null || coherent.Count == 0) {
                    if (ped.Arrests != null && ped.Arrests.Count > 0) {
                        coherent = ped.Arrests
                            .Where(ch => ch != null && !string.IsNullOrWhiteSpace(ch.name))
                            .Select(SupervisionBackstoryHelper.CloneCharge)
                            .ToList();
                    }
                }
                if (coherent == null || coherent.Count == 0) return;

                ped.Arrests = coherent;
                string synthNumber = AllocateCourtCaseNumberUnderLock();
                CourtData courtCase = BuildSyntheticSupervisionCourtCase(ped, coherent, synthNumber);
                if (courtCase == null || string.IsNullOrEmpty(courtCase.Number)) return;

                if (!courtDatabase.Any(x => x.Number == courtCase.Number)) {
                    if (courtDatabase.Count >= SetupController.GetConfig().courtDatabaseMaxEntries) {
                        Database.DeleteCourtCase(courtDatabase[0].Number);
                        courtDatabase.RemoveAt(0);
                    }
                    courtDatabase.Add(courtCase);
                }

                Database.SaveCourtCase(courtCase);
                Database.SavePed(ped);
            }
        }

        /// <summary>Court cases for a defendant, newest hearing/resolution first (Person Search API).</summary>
        internal static List<CourtData> GetCourtCasesForPedName(string pedName) {
            if (string.IsNullOrWhiteSpace(pedName)) return new List<CourtData>();
            string n = pedName.Trim();
            lock (_courtDatabaseLock) {
                return courtDatabase
                    .Where(c => c != null && string.Equals(c.PedName, n, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(ParseCourtSortDateForPedCases)
                    .ToList();
            }
        }

        private static DateTime ParseCourtSortDateForPedCases(CourtData c) {
            if (c == null) return DateTime.MinValue;
            if (!string.IsNullOrEmpty(c.HearingDateUtc) && DateTime.TryParse(c.HearingDateUtc, null, DateTimeStyles.RoundtripKind, out DateTime h))
                return h;
            if (!string.IsNullOrEmpty(c.ResolveAtUtc) && DateTime.TryParse(c.ResolveAtUtc, null, DateTimeStyles.RoundtripKind, out DateTime r))
                return r;
            if (!string.IsNullOrEmpty(c.LastUpdatedUtc) && DateTime.TryParse(c.LastUpdatedUtc, null, DateTimeStyles.RoundtripKind, out DateTime u))
                return u;
            return DateTime.MinValue;
        }

        private static CourtData BuildSyntheticSupervisionCourtCase(MDTProPedData ped, List<ArrestGroup.Charge> arrestCharges, string caseNumber) {
            if (ped == null || string.IsNullOrWhiteSpace(ped.Name) || arrestCharges == null || arrestCharges.Count == 0) return null;
            if (string.IsNullOrWhiteSpace(caseNumber)) return null;
            var hearingUtc = DateTime.UtcNow.AddDays(-Helper.GetRandomInt(400, 1100));
            int shortYear = int.Parse(hearingUtc.ToString("yy", CultureInfo.InvariantCulture));

            string lastName = !string.IsNullOrWhiteSpace(ped.LastName)
                ? ped.LastName.Trim()
                : (ped.Name?.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "Defendant");

            string[] docketOpeners = {
                "Minutes reflect a prior bench disposition",
                "The criminal docket reflects a concluded matter",
                "Register shows a final judgment",
                "Historical docket entries document a prior resolution",
            };
            string opener = docketOpeners[Helper.GetRandomInt(0, docketOpeners.Length - 1)];

            var courtCase = new CourtData(ped.Name.Trim(), caseNumber, CourtData.SyntheticSupervisionReportId, shortYear) {
                Status = 1,
                IsJuryTrial = false,
                JurySize = 0,
                JuryVotesForConviction = 0,
                JuryVotesForAcquittal = 0,
                PriorCitationCount = 0,
                PriorArrestCount = 0,
                PriorConvictionCount = 0,
                Plea = "No Contest",
                HearingDateUtc = hearingUtc.ToString("o"),
                ResolveAtUtc = hearingUtc.AddDays(Helper.GetRandomInt(14, 120)).ToString("o"),
                SentenceMultiplier = 1f,
                ProsecutionStrength = 55f,
                DefenseStrength = 40f,
                DocketPressure = 0.35f,
                ConvictionChance = 88,
                RepeatOffenderScore = 0,
                HasPublicDefender = true,
                OutcomeNotes = $"{opener} in People v. {lastName}. This entry was reconstructed so the defendant's CDF supervision status (probation/parole) matches a plausible charging history on file. No contemporaneous patrol arrest report is attached in MDT. Case closed.",
            };

            CourtDistrictProfile district = ResolveSyntheticSupervisionDistrict(ped);
            courtCase.CourtDistrict = district.District;
            courtCase.CourtName = district.CourtName;
            courtCase.CourtType = district.CourtType;
            courtCase.PolicyAdjustment = district.PolicyAdjustment;

            courtCase.JudgeName = SelectRotatingRosterMember(district.Judges, district.District, "synth-judge", caseNumber, courtCase.SeverityScore);
            courtCase.ProsecutorName = SelectProsecutor(district, caseNumber, 3);
            courtCase.DefenseAttorneyName = SelectDefenseAttorney(district, courtCase.HasPublicDefender, caseNumber, 2);

            foreach (ArrestGroup.Charge ac in arrestCharges) {
                if (ac == null || string.IsNullOrWhiteSpace(ac.name)) continue;
                int minDays = ac.minDays;
                int? maxDays = ac.maxDays;
                int? time;
                int rangeMin;
                int? rangeMax;
                if (maxDays == null) {
                    int hi = Math.Max(minDays, minDays * 2);
                    time = minDays > 0 ? Helper.GetRandomInt(minDays, hi) : 0;
                    rangeMin = minDays;
                    rangeMax = hi;
                } else {
                    time = minDays;
                    rangeMin = minDays;
                    rangeMax = maxDays;
                }
                courtCase.AddCharge(new CourtData.Charge(
                    ac.name,
                    Helper.GetRandomInt(ac.minFine, Math.Max(ac.minFine, ac.maxFine)),
                    time,
                    ac.isArrestable,
                    rangeMin,
                    rangeMax));
            }

            ApplySyntheticSupervisionEvidenceTags(courtCase);

            courtCase.SeverityScore = GetSeverityScore(courtCase);
            Config cfg = SetupController.GetConfig();
            courtCase.EvidenceScore = GetEvidenceScore(courtCase, cfg);
            courtCase.EvidenceBand = GetEvidenceBand(courtCase);
            courtCase.OfficerTestimonySummary = BuildOfficerTestimonySummary(courtCase);

            foreach (CourtData.Charge ch in courtCase.Charges ?? Enumerable.Empty<CourtData.Charge>()) {
                if (ch == null) continue;
                ch.Outcome = 1;
                ch.ConvictionChance = courtCase.ConvictionChance;
                int minD = ch.MinDays;
                int maxD = ch.MaxDays ?? ch.MinDays;
                if (maxD < minD) maxD = minD;
                if (minD <= 0 && maxD <= 0 && ch.Time.HasValue && ch.Time.Value > 0) {
                    minD = ch.Time.Value;
                    maxD = ch.Time.Value;
                }
                if (ch.Time == null && (!ch.MaxDays.HasValue || ch.MaxDays.Value <= 0)) {
                    ch.SentenceDaysServed = null;
                } else if (minD > 0 || maxD > 0) {
                    ch.SentenceDaysServed = Helper.GetRandomInt(minD, maxD);
                } else {
                    ch.SentenceDaysServed = 0;
                }
            }

            // Same verdict and sentencing narrative engines as live arrest-driven cases (plea type, charges, evidence flags, severity, district policy, etc.).
            courtCase.OutcomeReasoning = BuildOutcomeReasoning(courtCase, courtCase.ConvictionChance, 1);
            courtCase.SentenceReasoning = BuildSentenceReasoning(courtCase);

            courtCase.LastUpdatedUtc = DateTime.UtcNow.ToString("o");
            return courtCase;
        }

        private static CourtDistrictProfile ResolveSyntheticSupervisionDistrict(MDTProPedData ped) {
            string a = (ped?.Address ?? "").ToLowerInvariant();
            if (a.Contains("sandy") || a.Contains("paleto") || a.Contains("grapeseed")
                || a.Contains("blaine") || a.Contains("harmony") || a.Contains("desert")) {
                return BlaineDistrict;
            }
            if (a.Contains("cayo") || a.Contains("yankton")) return IslandDistrict;
            return LosSantosDistrict;
        }

        private static void ApplySyntheticSupervisionEvidenceTags(CourtData courtCase) {
            if (courtCase?.Charges == null) return;
            foreach (CourtData.Charge ch in courtCase.Charges) {
                if (ch == null || string.IsNullOrEmpty(ch.Name)) continue;
                string n = ch.Name.ToLowerInvariant();
                if (n.Contains("weapon") || n.Contains("firearm") || n.Contains("knife") || n.Contains("deadly weapon"))
                    courtCase.EvidenceHadWeapon = true;
                if (n.Contains("drug") || n.Contains("cannabis") || n.Contains("cocaine") || n.Contains("heroin")
                    || n.Contains("meth") || n.Contains("controlled substance") || n.Contains("narcotic") || n.Contains("paraphernalia"))
                    courtCase.EvidenceHadDrugs = true;
                if (n.Contains("elud") || n.Contains("evad") || n.Contains("flee") || n.Contains("pursuit"))
                    courtCase.EvidenceWasFleeing = true;
                if (n.Contains("dui") || n.Contains("drunk") || n.Contains("under the influence") || n.Contains("intoxicat"))
                    courtCase.EvidenceWasDrunk = true;
                if (n.Contains("resist")) courtCase.EvidenceResisted = true;
                if (n.Contains("assault") || n.Contains("battery")) courtCase.EvidenceAssaultedPed = true;
                if (n.Contains("vehicle") && (n.Contains("reckless") || n.Contains("hit and run") || n.Contains("collision")))
                    courtCase.EvidenceDamagedVehicle = true;
            }
        }

        private static void UpdatePedIncarcerationFromCourtData(MDTProPedData pedData, CourtData courtData, Config config = null) {
            if (pedData == null || courtData?.Charges == null) return;
            if (string.Equals(courtData.ReportId, CourtData.SyntheticSupervisionReportId, StringComparison.Ordinal))
                return;

            int totalDays = 0;
            bool hasLifeSentence = false;

            foreach (CourtData.Charge charge in courtData.Charges) {
                if (charge == null) continue;
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
                if (charge == null) continue;
                if (charge.Outcome != 1) continue; // Only convicted charges
                if (string.IsNullOrEmpty(charge.Name)) continue;
                string name = charge.Name.Trim();

                // Driver's license: use canRevokeLicense from arrest options (CA: DUI, reckless driving, hit-and-run, evading, etc.)
                if (!driversLicenseRevoked && chargeLookup.TryGetValue(name, out var arrestCharge) && arrestCharge.canRevokeLicense) {
                    driversLicenseRevoked = true;
                }

                // Firearms: under California rules — felonies = lifetime; domestic violence / protective order = lifetime; violent misdemeanors = 10 years
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
                || n.Contains("carjacking") || n.Contains("driving on suspended") || n.Contains("driving on revoked") || n.Contains("revoked license")
                || n.Contains("driving without license") || n.Contains("driving without valid license") || n.Contains("driving with license expired")
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
                || n.Contains("manufacturing meth") || n.Contains("possession of cannabis") || n.Contains("possession of marijuana") || n.Contains("possession of cocaine")
                || n.Contains("possession of methamphetamine") || n.Contains("possession of heroin") || n.Contains("possession of pcp")
                || n.Contains("possession of lsd") || n.Contains("hallucinogen") || n.Contains("possession of ecstasy") || n.Contains("mdma")
                || n.Contains("possession of fentanyl") || n.Contains("ritalin") || n.Contains("hydrocodone")
                || n.Contains("prescription") && n.Contains("narcotic");
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

        /// <summary>Judge name -> leniency (-1 lenient to 1 strict). Loaded from judgeProfiles.json.</summary>
        private static Dictionary<string, float> judgeLeniencyMap = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Load judge profiles from defaults/judgeProfiles.json. All roster judges must have profiles.</summary>
        internal static void LoadJudgeProfiles() {
            judgeLeniencyMap.Clear();
            try {
                string path = Setup.SetupController.JudgeProfilesDefaultsPath;
                if (!System.IO.File.Exists(path)) {
                    Utility.Helper.Log("judgeProfiles.json not found; all judges will use neutral leniency.", false, Utility.Helper.LogSeverity.Warning);
                    return;
                }
                var data = Utility.Helper.ReadFromJsonFile<JudgeProfilesData>(path);
                if (data?.judges == null) return;
                foreach (var j in data.judges) {
                    if (string.IsNullOrWhiteSpace(j?.name)) continue;
                    float len = j.leniency;
                    if (len < -1f) len = -1f;
                    if (len > 1f) len = 1f;
                    judgeLeniencyMap[j.name.Trim()] = len;
                }
                ValidateAllRosterJudgesHaveProfiles();
            } catch (System.Exception ex) {
                Utility.Helper.Log($"LoadJudgeProfiles: {ex.Message}", false, Utility.Helper.LogSeverity.Warning);
            }
        }

        private static void ValidateAllRosterJudgesHaveProfiles() {
            var rosterJudges = new[] { LosSantosDistrict, BlaineDistrict, IslandDistrict }
                .SelectMany(p => p?.Judges ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var missing = rosterJudges.Where(name => !judgeLeniencyMap.ContainsKey(name)).ToList();
            if (missing.Count > 0) {
                Utility.Helper.Log($"Judges without profiles in judgeProfiles.json: {string.Join(", ", missing)}. Add profiles for these judges.", true, Utility.Helper.LogSeverity.Warning);
            }
        }

        /// <summary>Returns judge leniency (-1 to 1). All roster judges must have profiles in judgeProfiles.json; missing judges return 0 and log a warning.</summary>
        private static float GetJudgeLeniencyFromName(string judgeName) {
            if (string.IsNullOrWhiteSpace(judgeName)) return 0f;
            string key = judgeName.Trim();
            if (judgeLeniencyMap.TryGetValue(key, out float leniency)) return leniency;
            Utility.Helper.Log($"Judge '{key}' has no profile in judgeProfiles.json; using neutral (0). Add a profile for this judge.", false, Utility.Helper.LogSeverity.Warning);
            return 0f;
        }

        private static int GetProsecutorStrengthModifier(string prosecutorName) {
            if (string.IsNullOrWhiteSpace(prosecutorName)) return 0;
            int h = Math.Abs(GetStableHash(prosecutorName));
            return (h % 17) - 8; // -8 to +8
        }

        private static int GetDefenseStrengthModifier(string defenseAttorneyName) {
            if (string.IsNullOrWhiteSpace(defenseAttorneyName)) return 0;
            int h = Math.Abs(GetStableHash(defenseAttorneyName));
            return (h % 17) - 8; // -8 to +8 (positive = stronger defense = lower conviction)
        }

        /// <summary>Per-charge conviction chance (0-100). Case chance + tier modifier + variance + judge/prosecutor/defense modifiers, then skewed. Evidence cap applies.</summary>
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

            // Judge/prosecutor/defense traits affect conviction (bench trials: judge; jury: prosecutor/defense affect underlying chance)
            if (!courtCase.IsJuryTrial && !string.IsNullOrWhiteSpace(courtCase.JudgeName)) {
                float leniency = GetJudgeLeniencyFromName(courtCase.JudgeName);
                tierMod += (int)Math.Round(leniency * 8); // Lenient judge: -8; strict: +8
            }
            tierMod += GetProsecutorStrengthModifier(courtCase.ProsecutorName);
            tierMod -= GetDefenseStrengthModifier(courtCase.DefenseAttorneyName); // Strong defense reduces chance

            int chance = Math.Max(15, Math.Min(85, baseChance + variance + tierMod));

            // Homicide without death report: cap conviction chance so documentation matters
            if (isHomicide && !hasDeathReport && config.courtConvictionHomicideNoDeathReportCap > 0)
                chance = Math.Min(chance, config.courtConvictionHomicideNoDeathReportCap);

            // Evidence cap: weak evidence cannot over-convict; strong evidence floors at 30%
            int evidenceScore = courtCase.EvidenceScore;
            if (evidenceScore < 20) chance = Math.Min(chance, 40);
            if (evidenceScore > 70) chance = Math.Max(chance, 30);

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
                    // Same charge-aware dismissal wording as auto-resolution would use (evidence band, burden of proof, charge-specific lines).
                    courtCase.OutcomeReasoning = BuildOutcomeReasoning(courtCase, courtCase.ConvictionChance, 3);
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
                if (persona == null || string.IsNullOrWhiteSpace(persona.FullName)) return;

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
                if (string.IsNullOrWhiteSpace(cacheKey)) return;
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
                    Database.UpsertPedEvidenceEntry(new PedEvidenceCacheEntry {
                        PedName = cacheKey,
                        CapturedAt = ctx.CapturedAt,
                        HadWeapon = ctx.HadWeapon,
                        WasWanted = ctx.WasWanted,
                        WasPatDown = ctx.WasPatDown,
                        WasDrunk = ctx.WasDrunk,
                        WasFleeing = ctx.WasFleeing,
                        AssaultedPed = ctx.AssaultedPed,
                        DamagedVehicle = ctx.DamagedVehicle,
                        HadIllegalWeapon = ctx.HadIllegalWeapon,
                        ViolatedSupervision = ctx.ViolatedSupervision,
                        Resisted = ctx.Resisted,
                    });
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
                    if (persona != null && !string.IsNullOrWhiteSpace(persona.FullName)) {
                        string cacheKey = GetPedDataForPed(ped)?.Name ?? persona.FullName;
                        if (string.IsNullOrWhiteSpace(cacheKey)) return;
                        if (!pedEvidenceCache.TryGetValue(cacheKey, out PedEvidenceContext ctx)) {
                            ctx = new PedEvidenceContext();
                            pedEvidenceCache[cacheKey] = ctx;
                        }
                        ctx.WasFleeing = true;
                        if (damagedVehicle) ctx.DamagedVehicle = true;
                        if (assaultedPlayer) ctx.AssaultedPed = true;
                        if (hadWeapon) ctx.HadWeapon = true;
                        ctx.CapturedAt = now;
                        Database.UpsertPedEvidenceEntry(new PedEvidenceCacheEntry {
                            PedName = cacheKey,
                            CapturedAt = ctx.CapturedAt,
                            HadWeapon = ctx.HadWeapon,
                            WasWanted = ctx.WasWanted,
                            WasPatDown = ctx.WasPatDown,
                            WasDrunk = ctx.WasDrunk,
                            WasFleeing = ctx.WasFleeing,
                            AssaultedPed = ctx.AssaultedPed,
                            DamagedVehicle = ctx.DamagedVehicle,
                            HadIllegalWeapon = ctx.HadIllegalWeapon,
                            ViolatedSupervision = ctx.ViolatedSupervision,
                            Resisted = ctx.Resisted,
                        });
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
                if (persona == null || string.IsNullOrWhiteSpace(persona.FullName)) return;
                string cacheKey = GetPedDataForPed(ped)?.Name ?? persona.FullName;
                if (string.IsNullOrWhiteSpace(cacheKey)) return;
                lock (pedEvidenceLock) {
                    if (!pedEvidenceCache.TryGetValue(cacheKey, out PedEvidenceContext ctx)) {
                        ctx = new PedEvidenceContext();
                        pedEvidenceCache[cacheKey] = ctx;
                    }
                    ctx.WasPatDown = true;
                    if (ctx.CapturedAt == default(DateTime)) ctx.CapturedAt = DateTime.UtcNow;
                    Database.UpsertPedEvidenceEntry(new PedEvidenceCacheEntry {
                        PedName = cacheKey,
                        CapturedAt = ctx.CapturedAt,
                        HadWeapon = ctx.HadWeapon,
                        WasWanted = ctx.WasWanted,
                        WasPatDown = ctx.WasPatDown,
                        WasDrunk = ctx.WasDrunk,
                        WasFleeing = ctx.WasFleeing,
                        AssaultedPed = ctx.AssaultedPed,
                        DamagedVehicle = ctx.DamagedVehicle,
                        HadIllegalWeapon = ctx.HadIllegalWeapon,
                        ViolatedSupervision = ctx.ViolatedSupervision,
                        Resisted = ctx.Resisted,
                    });
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
                        if (SetupController.GetConfig().firearmDebugLogging)
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
                if (SetupController.GetConfig().firearmDebugLogging)
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
                                    if (SetupController.GetConfig().firearmDebugLogging)
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
            if (SetupController.GetConfig().firearmDebugLogging)
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
                    if (SetupController.GetConfig().firearmDebugLogging)
                        Helper.Log($"Vehicle search captured {records.Count} item(s) for plate {plate}", false, Helper.LogSeverity.Info);
                }
                if (firearmRecords.Count > 0) {
                    Database.SaveFirearmRecords(firearmRecords);
                    if (SetupController.GetConfig().firearmDebugLogging)
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

        /// <summary>Loads ped evidence cache from DB into memory. Prunes entries older than 24h. Case-insensitive merge.</summary>
        internal static void LoadPedEvidenceFromDatabase() {
            var entries = Database.LoadPedEvidenceCache(maxAgeHours: 24);
            if (entries == null || entries.Count == 0) return;
            lock (pedEvidenceLock) {
                foreach (var e in entries) {
                    if (string.IsNullOrWhiteSpace(e.PedName)) continue;
                    var ctx = new PedEvidenceContext {
                        HadWeapon = e.HadWeapon,
                        WasWanted = e.WasWanted,
                        WasPatDown = e.WasPatDown,
                        WasDrunk = e.WasDrunk,
                        WasFleeing = e.WasFleeing,
                        AssaultedPed = e.AssaultedPed,
                        DamagedVehicle = e.DamagedVehicle,
                        HadIllegalWeapon = e.HadIllegalWeapon,
                        ViolatedSupervision = e.ViolatedSupervision,
                        Resisted = e.Resisted,
                        CapturedAt = e.CapturedAt,
                    };
                    pedEvidenceCache[e.PedName] = ctx;
                }
            }
        }

        private static void PruneStaleEvidenceEntries() {
            DateTime threshold = DateTime.UtcNow.AddHours(-24);
            Database.PrunePedEvidenceCache(24);
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
            courtData.EvidenceBand = GetEvidenceBand(courtData);
            courtData.DocketPressure = GetDocketPressure(districtProfile.District, config);
            courtData.OfficerTestimonySummary = BuildOfficerTestimonySummary(courtData);

            bool hasLifeSentence = courtData.Charges?.Any(c => c.Time == null) == true;
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

            // Evidence cap: weak evidence cannot over-convict; strong evidence floors at 30%
            if (courtData.EvidenceScore < 20) convictionChance = Math.Min(convictionChance, 40);
            if (courtData.EvidenceScore > 70) convictionChance = Math.Max(convictionChance, 30);

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

            courtData.ProsecutorName = SelectProsecutor(districtProfile, courtData.Number, courtData.RepeatOffenderScore);
            courtData.DefenseAttorneyName = SelectDefenseAttorney(districtProfile, courtData.HasPublicDefender, courtData.Number, courtData.RepeatOffenderScore + courtData.SeverityScore);

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

        /// <summary>Recalculates EvidenceScore and Evidence* flags for a court case. Call after attach/detach of reports. Preserves EvidenceUseOfForce from primary arrest report.
        /// Concurrency: No locking. If two requests recalc the same case concurrently, both may run; the last SaveCourtCase wins. Acceptable: recalc is idempotent and SQLite serializes DB writes.</summary>
        internal static void RecalculateCourtCaseEvidence(CourtData courtData) {
            if (courtData == null) return;
            Config config = SetupController.GetConfig();

            // Reset all evidence flags so GetEvidenceScore can set them fresh (it only sets true, never false)
            courtData.EvidenceHadWeapon = false;
            courtData.EvidenceWasWanted = false;
            courtData.EvidenceWasPatDown = false;
            courtData.EvidenceWasDrunk = false;
            courtData.EvidenceWasFleeing = false;
            courtData.EvidenceAssaultedPed = false;
            courtData.EvidenceDamagedVehicle = false;
            courtData.EvidenceIllegalWeapon = false;
            courtData.EvidenceViolatedSupervision = false;
            courtData.EvidenceResisted = false;
            courtData.EvidenceHadDrugs = false;
            courtData.EvidenceDrugTypesBreakdown = null;
            courtData.EvidenceFirearmTypesBreakdown = null;
            // EvidenceUseOfForce comes from arrest report; preserve before recalc
            if (!string.IsNullOrEmpty(courtData.ReportId)) {
                var primaryArrest = arrestReports?.FirstOrDefault(r => r.Id == courtData.ReportId);
                courtData.EvidenceUseOfForce = primaryArrest?.UseOfForce != null && !string.IsNullOrEmpty(primaryArrest.UseOfForce.Type);
            }

            courtData.EvidenceScore = GetEvidenceScore(courtData, config);
            courtData.EvidenceBand = GetEvidenceBand(courtData);
            courtData.OfficerTestimonySummary = BuildOfficerTestimonySummary(courtData);
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

        /// <summary>Builds a short one-paragraph officer testimony summary from evidence flags and charges. For exhibit display.</summary>
        private static string BuildOfficerTestimonySummary(CourtData courtData) {
            if (courtData == null) return null;
            var clauses = new List<string>();
            if (courtData.EvidenceHadWeapon) {
                string weapon = (courtData.EvidenceFirearmTypesBreakdown != null && courtData.EvidenceFirearmTypesBreakdown.Count > 0)
                    ? $" ({string.Join(", ", courtData.EvidenceFirearmTypesBreakdown.Where(s => !string.IsNullOrEmpty(s)))})"
                    : "";
                clauses.Add($"Defendant was armed{weapon}");
            }
            if (courtData.EvidenceWasWanted) clauses.Add("defendant was wanted on warrant");
            if (courtData.EvidenceResisted) clauses.Add("defendant resisted arrest");
            if (courtData.EvidenceHadDrugs) {
                string drugs = (courtData.EvidenceDrugTypesBreakdown != null && courtData.EvidenceDrugTypesBreakdown.Count > 0)
                    ? string.Join(", ", courtData.EvidenceDrugTypesBreakdown.Where(s => !string.IsNullOrEmpty(s)))
                    : "controlled substances";
                clauses.Add($"defendant was found in possession of {drugs}");
            }
            if (courtData.EvidenceAssaultedPed) clauses.Add("defendant assaulted the officer");
            if (courtData.EvidenceDamagedVehicle) clauses.Add("defendant caused vehicle damage during the incident");
            if (courtData.EvidenceWasFleeing) clauses.Add("defendant fled from the officer");
            if (courtData.EvidenceIllegalWeapon && !courtData.EvidenceHadWeapon) clauses.Add("defendant possessed an illegal weapon");
            if (courtData.EvidenceViolatedSupervision) clauses.Add("defendant was in violation of supervision");
            if (courtData.EvidenceUseOfForce) clauses.Add("use of force was required");
            if (courtData.Charges != null && courtData.Charges.Count > 0) {
                var chargeNames = courtData.Charges.Take(5).Select(c => c.Name).Where(s => !string.IsNullOrEmpty(s)).ToList();
                if (chargeNames.Count > 0)
                    clauses.Add($"Charges included: {string.Join(", ", chargeNames)}{(courtData.Charges.Count > 5 ? " and others" : "")}");
            }
            if (clauses.Count == 0) return null;
            return "Officer responded to call. " + string.Join(", ", clauses) + ".";
        }

        private static bool HasChargeKeyword(CourtData courtData, string keyword) {
            if (courtData?.Charges == null) return false;
            string k = keyword.ToLowerInvariant();
            // "Shoplifting …" does not contain the substring "theft"; treat shoplifting as theft for verdict/rationale keyword matching (legacy cases + citations).
            if (k == "theft") {
                return courtData.Charges.Any(c => {
                    string n = (c.Name ?? "").ToLowerInvariant();
                    return n.Contains("theft") || n.Contains("shoplift");
                });
            }
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
            return HasChargeKeyword(courtData, "kidnapping") || HasChargeKeyword(courtData, "false imprisonment");
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
                    || n.Contains("cannabis over") || n.Contains("cannabis") || n.Contains("marijuana") || n.Contains("under influence of controlled")
                    || n.Contains("cocaine") || n.Contains("heroin") || n.Contains("fentanyl") || n.Contains("methamphetamine")
                    || (n.Contains("amphetamine") && !n.Contains("methamphetamine"))
                    || n.Contains("benzodiazepine") || n.Contains("hallucinogen") || n.Contains("ecstasy") || n.Contains("mdma") || n.Contains("pcp")
                    || n.Contains("dmt") || n.Contains("ghb") || n.Contains("ketamine") || n.Contains("steroids") || n.Contains("bath salt")
                    || n.Contains("synthetic cannabinoid") || n.Contains("k2") || n.Contains("spice") || n.Contains("peyote") || n.Contains("psilocybin")
                    || n.Contains("lysergic") || n.Contains("oxycontin") || n.Contains("percocet") || n.Contains("vicodin") || n.Contains("hydrocodone") || n.Contains("ritalin") || n.Contains("codeine")
                    || n.Contains("promethazine") || n.Contains("demerol") || n.Contains("morphine") || n.Contains("methadone") || n.Contains("opium")
                    || n.Contains("hydromorph") || n.Contains("roxicodone") || n.Contains("roofies") || n.Contains("ativan") || n.Contains("valium")
                    || n.Contains("xanax") || n.Contains("soma") || n.Contains("tramadol") || n.Contains("darvocet") || n.Contains("darvon")
                    || n.Contains("cultivation") || n.Contains("intent to manufacture") || n.Contains("intent to distribute")
                    || n.Contains("schedule i") || n.Contains("schedule ii") || n.Contains("schedule iii") || n.Contains("schedule iv");
            });
        }

        private static bool HasGangCharge(CourtData courtData) {
            return HasChargeKeyword(courtData, "gang");
        }

        private static bool HasFederalCharge(CourtData courtData) {
            if (courtData?.Charges == null) return false;
            return courtData.Charges.Any(c => {
                string n = (c?.Name ?? "").ToLowerInvariant();
                return n.Contains("federal") || n.Contains("assassination") || n.Contains("espionage") || n.Contains("cyberterrorism")
                    || n.Contains("bank fraud") || n.Contains("wire fraud") || n.Contains("securities fraud") || n.Contains("human trafficking")
                    || n.Contains("alien smuggl") || n.Contains("drug trafficking across");
            });
        }

        private static bool HasICECharge(CourtData courtData) {
            if (courtData?.Charges == null) return false;
            return courtData.Charges.Any(c => {
                string n = (c?.Name ?? "").ToLowerInvariant();
                return n.Contains("immigration") || n.Contains("illegal entry") || n.Contains("illegal re-entry")
                    || n.Contains("harboring illegal") || n.Contains("deportation") || n.Contains("work visa");
            });
        }

        private static bool HasRICOCharge(CourtData courtData) {
            return HasChargeKeyword(courtData, "rico") || HasChargeKeyword(courtData, "racketeering");
        }

        private static bool HasFraudCharge(CourtData courtData) {
            if (courtData?.Charges == null) return false;
            return courtData.Charges.Any(c => {
                string n = (c?.Name ?? "").ToLowerInvariant();
                return n.Contains("fraud") || n.Contains("embezzlement") || n.Contains("forgery") || n.Contains("money laundering")
                    || n.Contains("ponzi") || n.Contains("mortgage fraud") || n.Contains("insurance fraud") || n.Contains("corporate fraud")
                    || n.Contains("tax fraud") || n.Contains("identity theft");
            });
        }

        private static bool HasWildlifeCharge(CourtData courtData) {
            if (courtData?.Charges == null) return false;
            return courtData.Charges.Any(c => {
                string n = (c?.Name ?? "").ToLowerInvariant();
                return n.Contains("poaching") || n.Contains("cruelty to animal") || n.Contains("dog fighting");
            });
        }

        private static bool HasUnlicensedCharge(CourtData courtData) {
            return HasChargeKeyword(courtData, "unlicensed") || HasChargeKeyword(courtData, "unauthorized practice");
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

        /// <summary>True if courtData has any charge whose name contains any of the keywords (case-insensitive).</summary>
        /// <remarks>Sentinel values avoid false positives from substring matches (e.g. "possession" on firearm charges, "reckless" on arson).</remarks>
        private static bool CaseMatchesChargeKeywords(CourtData courtData, string[] keywords) {
            if (keywords == null || keywords.Length == 0) return true;
            foreach (var k in keywords) {
                if (k == "__drug_crime__") { if (HasDrugCrimeCharge(courtData)) return true; continue; }
                if (k == "__dui_charge__") { if (HasDUIVerdictCharge(courtData)) return true; continue; }
                if (k == "__traffic_no_arson__") { if (HasTrafficVerdictChargeExcludingArson(courtData)) return true; continue; }
                if (k == "__trafficking_drug__") { if (HasDrugTraffickingOrSaleCharge(courtData)) return true; continue; }
                if (HasChargeKeyword(courtData, k)) return true;
            }
            return false;
        }

        /// <summary>DUI/DWI and closely related charges (matches "Driving Under The Influence" etc., not generic drug influence).</summary>
        private static bool HasDUIVerdictCharge(CourtData courtData) {
            if (courtData?.Charges == null) return false;
            return courtData.Charges.Any(c => {
                string n = (c?.Name ?? "").ToLowerInvariant();
                return n.Contains("dui") || n.Contains("dwi") || n.Contains("driving under")
                    || n.Contains("field sobriety") || n.Contains("chemical test") || n.Contains("sobriety testing")
                    || n.Contains("dui causing");
            });
        }

        /// <summary>Traffic/driving charges for verdict wording; excludes arson "reckless burning" and firearm "reckless discharge of firearm" from reckless catch-all.</summary>
        private static bool HasTrafficVerdictChargeExcludingArson(CourtData courtData) {
            if (courtData?.Charges == null) return false;
            if (HasArsonCharge(courtData)) return false;
            return HasChargeKeyword(courtData, "traffic") || HasChargeKeyword(courtData, "speeding") || HasChargeKeyword(courtData, "unlawful speed")
                || HasChargeKeyword(courtData, "evading") || HasChargeKeyword(courtData, "elude") || HasChargeKeyword(courtData, "street racing") || HasChargeKeyword(courtData, "hit and run")
                || HasChargeKeyword(courtData, "wrong side") || HasChargeKeyword(courtData, "driving on suspended") || HasChargeKeyword(courtData, "driving without license")
                || HasChargeKeyword(courtData, "license expired") || HasChargeKeyword(courtData, "refusal to sign traffic") || HasChargeKeyword(courtData, "impeding traffic")
                || HasChargeKeyword(courtData, "reckless driving") || HasChargeKeyword(courtData, "careless driving")
                || (HasChargeKeyword(courtData, "reckless") && !HasChargeKeyword(courtData, "burning") && !HasChargeKeyword(courtData, "firearm"));
        }

        /// <summary>Drug trafficking / intent to distribute / sale-for-drug charges only (avoids "Unlawful Sale of Firearms").</summary>
        private static bool HasDrugTraffickingOrSaleCharge(CourtData courtData) {
            if (courtData?.Charges == null) return false;
            return courtData.Charges.Any(c => {
                string n = (c?.Name ?? "").ToLowerInvariant();
                var one = new CourtData { Charges = new List<CourtData.Charge> { c } };
                if (!HasDrugCrimeCharge(one)) return false;
                return n.Contains("trafficking") || n.Contains("intent to distribute") || n.Contains("for sale") || n.Contains("manufacturing meth")
                    || n.Contains("transport or sale") || n.Contains("sale or transport") || n.Contains("cultivation");
            });
        }

        /// <summary>Selects a verdict phrase, preferring charge-specific wording when the case matches. Generic (null/empty chargeKeywords) always eligible.</summary>
        private static string SelectChargeAwarePhrase(CourtData courtData, (string text, string[] chargeKeywords)[] pool) {
            if (pool == null || pool.Length == 0) return "";
            var eligible = new List<(string text, int weight)>();
            foreach (var entry in pool) {
                if (!CaseMatchesChargeKeywords(courtData, entry.chargeKeywords)) continue;
                int w = (entry.chargeKeywords == null || entry.chargeKeywords.Length == 0) ? 1 : 8;
                eligible.Add((entry.text, w));
            }
            if (eligible.Count == 0) return "";
            int total = eligible.Sum(e => e.weight);
            int r = Helper.GetRandomInt(0, Math.Max(0, total - 1));
            foreach (var e in eligible) {
                r -= e.weight;
                if (r < 0) return e.text;
            }
            return eligible[eligible.Count - 1].text;
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
                        "The defendant entered a guilty plea. The court accepted the plea and proceeded to sentencing without further proceedings.",
                        "The defendant pleaded guilty at arraignment. The court accepted the plea and scheduled sentencing.",
                        "The defendant entered a guilty plea. The court accepted the plea and ordered a presentence investigation before imposing sentence.",
                        "The defendant entered a guilty plea. The court accepted the plea, found a factual basis sufficient to support it, and proceeded to sentencing.",
                        "The defendant pleaded guilty. The court accepted the plea after advising the defendant of the rights being waived and imposed sentence.",
                        "The defendant entered a guilty plea. The court accepted the plea and continued the matter for sentencing.",
                        "The defendant pleaded guilty to the charges as filed. The court accepted the plea and pronounced sentence.",
                        "The defendant entered a guilty plea. The court accepted the plea, entered a finding of guilty, and set a sentencing date.",
                        "The defendant pleaded guilty. The court accepted the plea and proceeded to imposition of sentence without delay.",
                        "The defendant entered a guilty plea. The court accepted the plea after confirming the defendant understood the consequences and proceeded accordingly.",
                        "The defendant pleaded guilty to all counts. The court accepted the plea and sentenced the defendant in accordance with the agreement.",
                        "The defendant entered a guilty plea. The court accepted the plea and ordered the defendant committed pending sentencing.",
                        "The defendant pleaded guilty. The court accepted the plea, noted the defendant's waiver of trial, and imposed sentence.",
                        "The defendant entered a guilty plea. The court accepted the plea and remanded the defendant for sentencing.",
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
                        "The defendant entered a no contest plea. The court accepted the plea, entered a conviction, and scheduled sentencing.",
                        "The defendant pleaded nolo contendere. The court accepted the plea and proceeded to sentencing as if a guilty verdict had been returned.",
                        "The defendant entered a no contest plea. The court found the plea knowing and voluntary, accepted it, and imposed sentence.",
                        "The defendant entered a nolo contendere plea. The court accepted the plea, entered a finding of guilty, and continued for sentencing.",
                        "The defendant entered a no contest plea. The court treated the plea as an admission of the factual basis and proceeded to sentencing.",
                        "The defendant pleaded no contest to the charges. The court accepted the plea and entered a judgment of conviction for sentencing purposes.",
                        "The defendant entered a no contest plea. The court accepted the plea, finding it made voluntarily and with full knowledge of the consequences.",
                    };
                    b.Append(noContestPlea[Helper.GetRandomInt(0, noContestPlea.Length - 1)]);
                } else if (mismatchGuilty) {
                    var lowEvidenceGuilty = new (string text, string[] chargeKeywords)[] {
                        ("Despite limited physical evidence, the {0} found the defendant guilty, relying heavily on witness testimony and the defendant's conduct at the scene.", null),
                        ("In a case that hinged on credibility, the {0} found the defendant guilty based on the weight of officer testimony and circumstantial evidence.", null),
                        ("The {0} returned a guilty verdict. Although the evidence was circumstantial, witness credibility and the defendant's statements supported the conviction.", null),
                        ("The {0} found the defendant guilty. The prosecution's case, while not overwhelming, was sufficient to establish guilt beyond a reasonable doubt.", null),
                        ("The {0} returned a guilty verdict. Circumstantial evidence and officer testimony, while limited, were deemed sufficient to establish guilt beyond a reasonable doubt.", null),
                        ("Despite the lack of physical evidence, the {0} found the defendant guilty. Credibility determinations favoured the prosecution's witnesses.", null),
                        ("The {0} found the defendant guilty based on circumstantial evidence and witness testimony. The evidence, though not abundant, was sufficient to meet the burden of proof.", null),
                        ("In a case relying primarily on officer testimony, the {0} returned a guilty verdict. Credibility was resolved in favour of the prosecution.", null),
                        ("The {0} returned a guilty verdict. Limited physical evidence was supplemented by credible witness testimony sufficient to establish guilt beyond a reasonable doubt.", null),
                        ("The {0} found the defendant guilty. Circumstantial evidence and the defendant's own statements, together with officer testimony, supported the conviction.", null),
                        ("Despite limited corroborating evidence, the {0} found the defendant guilty. Witness credibility and circumstantial indicators established guilt beyond a reasonable doubt.", null),
                        ("The {0} returned a guilty verdict. Officer testimony and circumstantial evidence, though not overwhelming, were deemed sufficient to prove guilt beyond a reasonable doubt.", null),
                        ("The {0} found the defendant guilty. The prosecution's case, resting largely on credibility determinations, met the burden of proof despite limited physical evidence.", null),
                        ("In a case that turned on credibility, the {0} returned a guilty verdict. Circumstantial evidence and witness testimony were sufficient to establish guilt beyond a reasonable doubt.", null),
                        ("The {0} returned a guilty verdict. Although physical evidence was sparse, the weight of officer testimony and circumstantial evidence supported the conviction.", null),
                        ("The {0} found the defendant guilty. Credibility determinations favoured the prosecution's witnesses, and the circumstantial evidence was sufficient to establish guilt beyond a reasonable doubt.", null),
                        ("Despite limited physical evidence, the {0} returned a guilty verdict. The defendant's own admissions and circumstantial evidence were sufficient to meet the burden of proof.", null),
                        ("The {0} found the defendant guilty. Although the evidence was not overwhelming, the totality of circumstances and witness credibility supported conviction.", null),
                        ("Despite the absence of forensic evidence, the {0} returned a guilty verdict based on officer testimony and the defendant's conduct at the scene.", null),
                        ("The {0} found the defendant guilty. Limited documentary evidence was supplemented by credible testimony sufficient to establish guilt beyond a reasonable doubt.", null),
                        ("In a case with sparse physical evidence, the {0} returned a guilty verdict. Circumstantial evidence and the defendant's behaviour were deemed probative of guilt.", null),
                        ("The {0} found the defendant guilty despite limited corroboration. Witness credibility and circumstantial indicators met the prosecution's burden of proof.", null),
                        ("Despite limited tangible evidence, the {0} returned a guilty verdict. Officer testimony and the defendant's inconsistent statements supported the conviction.", null),
                        ("The {0} found the defendant guilty. Though physical evidence was minimal, witness identification and circumstantial evidence established guilt beyond a reasonable doubt.", null),
                        ("Despite limited BAC evidence, the {0} found the defendant guilty on DUI charges based on officer observations and field sobriety testimony.", new[] { "__dui_charge__" }),
                        ("The {0} returned a guilty verdict on impaired driving charges. Limited chemical test evidence was supplemented by officer testimony about impairment.", new[] { "__dui_charge__" }),
                        ("DUI conviction despite sparse evidence. The {0} relied on the defendant's conduct and officer credibility to meet the burden of proof.", new[] { "__dui_charge__" }),
                        ("Despite limited drug evidence, the {0} found the defendant guilty. Officer testimony and circumstantial indicators established possession.", new[] { "__drug_crime__" }),
                        ("The {0} returned a guilty verdict on drug charges. Chain of custody was imperfect but the tribunal found the evidence sufficient.", new[] { "__drug_crime__" }),
                        ("Drug possession conviction with limited physical evidence. The {0} relied on officer testimony and the defendant's conduct.", new[] { "__drug_crime__" }),
                        ("Despite limited assault evidence, the {0} found the defendant guilty. Witness credibility and the defendant's conduct supported the verdict.", new[] { "assault", "battery" }),
                        ("The {0} returned a guilty verdict on battery charges. Circumstantial evidence and victim testimony, though limited, were deemed sufficient.", new[] { "battery" }),
                        ("Theft conviction despite sparse evidence. The {0} relied on witness identification and circumstantial indicators of intent.", new[] { "theft", "larceny" }),
                        ("The {0} found the defendant guilty on burglary charges. Limited forensic evidence was supplemented by witness testimony.", new[] { "burglary" }),
                        ("Despite limited weapon evidence, the {0} returned a guilty verdict. Officer testimony established possession and intent.", new[] { "firearm", "weapon", "gun" }),
                        ("The {0} found the defendant guilty on evading charges. Driver identification was contested but the tribunal credited officer testimony.", new[] { "evad", "fleeing", "elude" }),
                        ("Evading conviction despite limited pursuit evidence. The {0} relied on vehicle identification and officer credibility.", new[] { "evad", "evading", "elude" }),
                        ("The {0} returned a guilty verdict on traffic charges. Limited speed measurement evidence was supplemented by officer observations.", new[] { "__traffic_no_arson__" }),
                        ("Despite limited resisting evidence, the {0} found the defendant guilty. Officer testimony established the lawfulness of the arrest.", new[] { "resisting", "obstruction" }),
                    };
                    var chosen = SelectChargeAwarePhrase(courtData, lowEvidenceGuilty);
                    b.AppendFormat(string.IsNullOrEmpty(chosen) ? "Despite limited physical evidence, the {0} found the defendant guilty based on witness testimony and circumstantial evidence." : chosen, tribunal);
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
                        ("The {0} returned a guilty verdict. Overwhelming evidence presented at trial left no reasonable doubt as to the defendant's guilt.", null),
                        ("The {0} found the defendant guilty. The prosecution's case was built on compelling witness testimony that the {0} found credible.", null),
                        ("The {0} returned a guilty verdict. Physical evidence recovered at the scene conclusively linked the defendant to the offence.", null),
                        ("The {0} found the defendant guilty. Circumstantial evidence, when viewed in its totality, established guilt beyond a reasonable doubt.", null),
                        ("The {0} returned a guilty verdict. The defendant's conduct before, during, and after the incident supported the prosecution's theory.", null),
                        ("The {0} found the defendant guilty. Documentary evidence, including records and reports, corroborated the charges and supported conviction.", null),
                        ("The {0} returned a guilty verdict. Expert testimony provided by the prosecution established key elements of the offence.", null),
                        ("The {0} found the defendant guilty. The defendant's confession, admitted into evidence, was central to the verdict.", null),
                        ("The {0} returned a guilty verdict. The defendant was caught in the act; eyewitness and officer testimony confirmed the defendant's involvement.", null),
                        ("The {0} found the defendant guilty. The chain of custody for physical evidence was properly established and the evidence was admitted.", null),
                        ("The {0} returned a guilty verdict. Corroboration between witnesses, physical evidence, and the defendant's own statements established guilt.", null),
                        ("The {0} found the defendant guilty. The prosecution met its burden of proof beyond a reasonable doubt on each element of the offence.", null),
                        ("The {0} returned a guilty verdict. Testimony from multiple witnesses converged on the defendant's guilt.", null),
                        ("The {0} found the defendant guilty. Forensic evidence presented at trial supported the prosecution's case and excluded alternative explanations.", null),
                        ("The {0} returned a guilty verdict. The defendant's presence at the scene and incriminating conduct were established by both testimony and physical evidence.", null),
                        ("The {0} found the defendant guilty. Video or photographic evidence presented at trial was consistent with the charges and supported conviction.", null),
                        ("The {0} returned a guilty verdict. The prosecution presented a coherent narrative supported by witness testimony, documents, and physical evidence.", null),
                        ("The {0} found the defendant guilty. Eyewitness identification, though challenged, was found reliable and sufficient to support conviction.", null),
                        ("The {0} returned a guilty verdict. The defendant's admissions, whether formal or informal, were weighed with other evidence and supported guilt.", null),
                        ("The {0} found the defendant guilty. The defence failed to raise a reasonable doubt; the prosecution's evidence was sufficient and convincing.", null),
                        ("The {0} returned a guilty verdict. Overwhelming testimony from officers and civilians established the defendant's participation in the offence.", null),
                        ("The {0} found the defendant guilty. Physical evidence, including items recovered from the defendant, linked the defendant to the crime.", null),
                        ("The {0} returned a guilty verdict. Circumstantial evidence, though indirect, was sufficient to infer guilt beyond a reasonable doubt.", null),
                        ("The {0} found the defendant guilty. The defendant's statements to officers, admitted into evidence, were inconsistent with innocence.", null),
                        ("The {0} returned a guilty verdict. Documentary evidence, such as records and logs, supported the prosecution's timeline and theory.", null),
                        ("The {0} found the defendant guilty. Expert witnesses testified to matters within their expertise; the {0} found their testimony persuasive.", null),
                        ("The {0} returned a guilty verdict. The defendant was observed committing the offence; identification and conduct were not in serious dispute.", null),
                        ("The {0} found the defendant guilty. The prosecution established a strong chain of evidence linking the defendant to the offence.", null),
                        ("The {0} returned a guilty verdict. Multiple strands of evidence—testimony, physical evidence, and documentary proof—converged on guilt.", null),
                        ("The {0} found the defendant guilty. The burden of proof was met; the prosecution presented sufficient evidence to eliminate reasonable doubt.", null),
                        ("The {0} returned a guilty verdict. Witness testimony was credible, consistent, and sufficient to establish the defendant's guilt.", null),
                        ("The {0} found the defendant guilty. Physical evidence recovered from the defendant or the scene supported the charges.", null),
                        ("The {0} returned a guilty verdict. Circumstantial evidence, when combined with the defendant's conduct, established guilt beyond a reasonable doubt.", null),
                        ("The {0} found the defendant guilty. The defendant's behaviour at the scene and subsequent statements supported the prosecution's case.", null),
                        ("The {0} returned a guilty verdict. Documentary and physical evidence corroborated witness accounts and established the defendant's involvement.", null),
                        ("The {0} found the defendant guilty. Expert testimony addressed technical or scientific issues and supported the prosecution's theory.", null),
                        ("The {0} returned a guilty verdict. The defendant's confession, corroborated by other evidence, was deemed voluntary and reliable.", null),
                        ("The {0} found the defendant guilty. The defendant was apprehended at or near the scene; circumstances and evidence pointed to guilt.", null),
                        ("The {0} returned a guilty verdict. The chain of custody for key evidence was established; the evidence was properly admitted and weighed.", null),
                        ("The {0} found the defendant guilty. Cross-correspondence between witnesses, documents, and physical evidence supported conviction.", null),
                        ("The {0} returned a guilty verdict. The prosecution proved each essential element of the offence; no reasonable doubt remained.", null),
                        ("The {0} found the defendant guilty. Testimony from the victim and other witnesses was consistent and sufficient to establish guilt.", null),
                        ("The {0} returned a guilty verdict. Forensic analysis of evidence presented at trial supported the prosecution and implicated the defendant.", null),
                        ("The {0} found the defendant guilty. The defendant's location, conduct, and admissions at the time of arrest supported the verdict.", null),
                        ("The {0} returned a guilty verdict. Audio or visual recordings admitted into evidence supported the charges and the prosecution's narrative.", null),
                        ("The {0} found the defendant guilty. The prosecution presented a logically coherent case supported by admissible evidence.", null),
                        ("The {0} returned a guilty verdict. Identification evidence, supported by corroboration, was sufficient to establish the defendant's guilt.", null),
                        ("The {0} found the defendant guilty. The defendant's words and actions, as testified to and documented, were indicative of guilt.", null),
                        ("The {0} returned a guilty verdict. The defence presented no credible alternative; the prosecution's evidence was sufficient and persuasive.", null),
                        ("The {0} found the defendant guilty. Physical evidence and testimony combined to establish a compelling case against the defendant.", null),
                        ("The {0} returned a guilty verdict. Circumstantial evidence, though not direct, was sufficient to satisfy the burden of proof.", null),
                        ("The {0} found the defendant guilty. Documentary records supported the chronology and facts underlying the charges.", null),
                        ("The {0} returned a guilty verdict. Expert evidence addressed disputed issues and supported the prosecution's theory of the case.", null),
                        ("The {0} found the defendant guilty. The defendant was taken into custody at the scene; evidence gathered supported the charges.", null),
                        ("The {0} returned a guilty verdict. Properly authenticated evidence was admitted and established the defendant's guilt beyond a reasonable doubt.", null),
                        ("The {0} found the defendant guilty. Multiple sources of evidence—witnesses, documents, and physical items—supported the verdict.", null),
                        ("The {0} returned a guilty verdict. The prosecution proved the defendant's guilt; the evidence was overwhelming and left no reasonable doubt.", null),
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
                        ("The {0} found the defendant guilty. The defendant was in possession of controlled substances and resisted arrest; both factors supported the verdict.", new[] { "Drugs", "Resisted" }),
                        ("The {0} returned a guilty verdict. Flight from officers and recovery of a weapon at arrest demonstrated consciousness of guilt and dangerous conduct.", new[] { "Fleeing", "Weapon" }),
                        ("The {0} found the defendant guilty. The defendant fled and was armed; the {0} considered both as evidence of intent and guilt.", new[] { "Fleeing", "Weapon" }),
                        ("The {0} returned a guilty verdict. An outstanding warrant and the defendant's resistance during arrest left no reasonable doubt.", new[] { "Wanted", "Resisted" }),
                        ("The {0} found the defendant guilty. Drugs were recovered and the defendant was armed; the combination strengthened the prosecution's case.", new[] { "Drugs", "Weapon" }),
                        ("The {0} returned a guilty verdict. The defendant was intoxicated and resisted arrest; both were cited as supporting the charges.", new[] { "Drunk", "Resisted" }),
                        ("The {0} found the defendant guilty. Assault during the incident and resistance at arrest were presented as evidence of culpability.", new[] { "Assault", "Resisted" }),
                        ("The {0} returned a guilty verdict. The defendant was wanted, armed, and fled; the {0} weighed these factors in reaching its verdict.", new[] { "Wanted", "Weapon", "Fleeing" }),
                        ("The {0} found the defendant guilty. Drugs and an illegal weapon were recovered; the prosecution's case was compelling.", new[] { "Drugs", "IllegalWeapon" }),
                        ("The {0} returned a guilty verdict. The defendant was on supervision and possessed drugs; violation and possession supported conviction.", new[] { "Supervision", "Drugs" }),
                        ("The {0} found the defendant guilty. Evidence discovered during a lawful pat-down and the defendant's resistance supported the charges.", new[] { "PatDown", "Resisted" }),
                        ("The {0} returned a guilty verdict. Vehicle damage and assault during the incident established the defendant's involvement.", new[] { "VehicleDamage", "Assault" }),
                        ("The {0} found the defendant guilty. The defendant fled and assaulted an officer; flight and assault were cited as evidence of guilt.", new[] { "Fleeing", "Assault" }),
                        ("The {0} returned a guilty verdict. Use of force documentation and resistance during arrest corroborated the prosecution's account.", new[] { "UseOfForce", "Resisted" }),
                        ("The {0} found the defendant guilty. The defendant was intoxicated and fled; impairment and flight supported the verdict.", new[] { "Drunk", "Fleeing" }),
                        ("The {0} returned a guilty verdict. An outstanding warrant and weapon possession demonstrated the defendant's fugitive status and dangerousness.", new[] { "Wanted", "Weapon" }),
                        ("The {0} found the defendant guilty. The murder charges were the defining element of the case.", new[] { "Homicide" }),
                        ("The {0} returned a guilty verdict. The gravity of the homicide charges dominated the proceeding.", new[] { "Homicide" }),
                        ("The {0} found the defendant guilty. The murder and manslaughter charges were central to the prosecution's case.", new[] { "Homicide" }),
                        ("The {0} returned a guilty verdict. The homicide-related charges were paramount in the {0}'s decision.", new[] { "Homicide" }),
                        ("The {0} found the defendant guilty. The murder charges warranted the most serious consideration by the court.", new[] { "Homicide" }),
                        ("The {0} returned a guilty verdict. The defendant's conviction on murder charges was the focal point of the case.", new[] { "Homicide" }),
                        ("The {0} found the defendant guilty. The homicide charges were proven beyond a reasonable doubt; the evidence was compelling.", new[] { "Homicide" }),
                        ("The {0} returned a guilty verdict. Murder or manslaughter was established through testimony, forensic evidence, and the defendant's conduct.", new[] { "Homicide" }),
                        ("The {0} found the defendant guilty. The seriousness of the homicide charges demanded, and received, thorough consideration by the {0}.", new[] { "Homicide" }),
                        ("The {0} returned a guilty verdict. The defendant was convicted of homicide-related offences; the gravity of the charges was reflected in the verdict.", new[] { "Homicide" }),
                        ("The {0} found the defendant guilty. Evidence presented at trial established the defendant's guilt on the murder or manslaughter charges.", new[] { "Homicide" }),
                        ("The {0} found the defendant guilty. The sexual offence charges were central to the prosecution's case.", new[] { "SexOffense" }),
                        ("The {0} returned a guilty verdict. The gravity of the rape and sexual violence charges dominated the proceeding.", new[] { "SexOffense" }),
                        ("The {0} found the defendant guilty. The sexual offence charges were proven beyond a reasonable doubt through victim testimony and corroborating evidence.", new[] { "SexOffense" }),
                        ("The {0} returned a guilty verdict. Evidence of sexual assault or related offences was compelling; the {0} found the defendant guilty.", new[] { "SexOffense" }),
                        ("The {0} found the defendant guilty. The sexual offence charges warranted the most serious consideration; the prosecution met its burden.", new[] { "SexOffense" }),
                        ("The {0} returned a guilty verdict. Testimony, forensic evidence, and corroboration established the defendant's guilt on the sexual offence charges.", new[] { "SexOffense" }),
                        ("The {0} found the defendant guilty. The defendant was convicted of sexual offences; the charges were central to the case and supported by the evidence.", new[] { "SexOffense" }),
                        ("The {0} returned a guilty verdict. Sexual offence charges dominated the trial; the {0} found the evidence sufficient and the defendant guilty.", new[] { "SexOffense" }),
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
                if (HasGangCharge(courtData)) factors.Add("gang-related charges formed part of the case");
                if (HasFederalCharge(courtData)) factors.Add("federal charges were proven");
                if (HasICECharge(courtData)) factors.Add("immigration-related charges were established");
                if (HasRICOCharge(courtData)) factors.Add("RICO or racketeering charges were proven");
                if (HasFraudCharge(courtData)) factors.Add("fraud or financial crime charges were established");
                if (HasWildlifeCharge(courtData)) factors.Add("wildlife or animal cruelty charges were proven");
                if (HasUnlicensedCharge(courtData)) factors.Add("unlicensed or unauthorised practice was established");
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
                        "The defendant's pattern of recidivism was considered by the court.",
                        "Prior offences were cited as aggravating circumstances.",
                        "The court weighed the defendant's history of similar conduct.",
                        "Repeat offender status was reflected in the sentencing considerations.",
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
                        $"A {courtData.JuryVotesForConviction}-{courtData.JuryVotesForAcquittal} jury verdict found the defendant guilty.",
                        $"The jury returned a guilty verdict by a margin of {courtData.JuryVotesForConviction} to {courtData.JuryVotesForAcquittal}.",
                        $"Jurors voted {courtData.JuryVotesForConviction}-{courtData.JuryVotesForAcquittal} to convict.",
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
                        "Court congestion contributed to a compressed trial schedule.",
                        "The case was prioritized due to docket pressure and resolved accordingly.",
                    };
                    b.Append(" " + docketConviction[Helper.GetRandomInt(0, docketConviction.Length - 1)]);
                }
                AppendChargeDomainPhrase(b, courtData, resolvedStatus);
            } else if (resolvedStatus == 2) {
                if (mismatchAcquittal) {
                    var highEvidenceAcquittal = new (string text, string[] chargeKeywords)[] {
                        ("Despite strong prosecution evidence, the {0} returned a not guilty verdict. The defence successfully challenged key aspects of the case and raised sufficient reasonable doubt.", null),
                        ("In an unexpected outcome, the {0} acquitted the defendant. Procedural issues and defence challenges to the evidence ultimately prevailed.", null),
                        ("The {0} returned a not guilty verdict despite a robust prosecution case. The defence's challenge to witness identification and chain of custody created reasonable doubt.", null),
                        ("Although the prosecution presented substantial evidence, the {0} found the defendant not guilty. Credibility issues and defence arguments raised sufficient doubt.", null),
                        ("Despite compelling prosecution evidence, the {0} acquitted the defendant. Defence counsel successfully attacked the legality of the search and seizure.", null),
                        ("The {0} returned a not guilty verdict despite strong evidence. Miranda violations and inadmissible statements undermined the prosecution's case.", null),
                        ("In a surprising outcome, the {0} found the defendant not guilty. The defence's alibi evidence and conflicting testimony created reasonable doubt.", null),
                        ("Although the prosecution presented a strong case, the {0} acquitted the defendant. Problems with identification procedures and mistaken identity concerns prevailed.", null),
                        ("Despite substantial physical evidence, the {0} returned a not guilty verdict. Chain of custody breaches and forensic reliability challenges favoured the defence.", null),
                        ("The {0} acquitted the defendant despite a robust prosecution. Defence arguments on lack of intent and self-defence raised sufficient reasonable doubt.", null),
                        ("In an unexpected verdict, the {0} found the defendant not guilty. Procedural defects and exclusion of key evidence tipped the balance toward acquittal.", null),
                        ("Despite significant evidence against the defendant, the {0} returned a not guilty verdict. Witness credibility issues and conflicting testimony doomed the prosecution's case.", null),
                        ("The {0} acquitted the defendant despite strong circumstantial evidence. The defence successfully argued mistaken identity and lack of corroboration.", null),
                        ("Although the prosecution marshalled substantial proof, the {0} found the defendant not guilty. Search illegality and Fourth Amendment violations led to acquittal.", null),
                        ("The {0} acquitted the defendant despite compelling evidence. The defence successfully challenged the traffic stop and field sobriety procedures.", new[] { "__dui_charge__" }),
                        ("Despite strong DUI evidence, the {0} returned a not guilty verdict. Breath test calibration and chain of custody were successfully challenged.", new[] { "__dui_charge__" }),
                        ("The {0} acquitted the defendant on DUI charges despite substantial evidence. The defence attacked the legality of the stop and testing protocol.", new[] { "__dui_charge__" }),
                        ("The {0} acquitted the defendant on DUI charges. The defence successfully challenged the prosecution's impairment evidence and raised reasonable doubt.", new[] { "__dui_charge__" }),
                        ("Despite drug evidence, the {0} acquitted the defendant. The defence successfully challenged the search and chain of custody.", new[] { "__drug_crime__" }),
                        ("The {0} returned a not guilty verdict on drug charges. Search warrant defects and evidence handling were successfully challenged.", new[] { "__drug_crime__" }),
                        ("The {0} acquitted the defendant on drug possession charges. The defence raised sufficient doubt about lawful possession and intent.", new[] { "__drug_crime__" }),
                        ("Despite assault evidence, the {0} acquitted the defendant. Self-defence and lack of intent were successfully argued.", new[] { "assault", "battery" }),
                        ("The {0} acquitted the defendant on assault charges. The defence successfully challenged identification and raised reasonable doubt.", new[] { "assault" }),
                        ("Battery charges resulted in acquittal. The defence argued self-defence and provocation; the {0} had reasonable doubt.", new[] { "battery" }),
                        ("Despite theft evidence, the {0} returned a not guilty verdict. The defence challenged ownership and intent successfully.", new[] { "theft", "larceny", "burglary" }),
                        ("The {0} acquitted the defendant on burglary charges. The defence successfully challenged proof of unlawful entry.", new[] { "burglary" }),
                        ("The {0} acquitted the defendant on robbery charges. The defence challenged identification and the force-or-fear element; reasonable doubt remained.", new[] { "robbery" }),
                        ("Despite firearm evidence, the {0} acquitted the defendant. Search legality and constructive possession were successfully challenged.", new[] { "firearm", "weapon", "gun" }),
                        ("The {0} returned a not guilty verdict on weapon charges. The defence raised sufficient doubt about possession and unlawful intent.", new[] { "weapon", "firearm" }),
                        ("Despite evading evidence, the {0} acquitted the defendant. Driver identification and pursuit justification were successfully challenged.", new[] { "evad", "fleeing", "elude" }),
                        ("The {0} acquitted the defendant on evading charges. The defence challenged whether the defendant was the driver and whether pursuit was lawful.", new[] { "evad", "evading", "elude" }),
                        ("The {0} acquitted the defendant on traffic charges despite strong evidence. The defence successfully challenged speed measurement and vehicle identification.", new[] { "__traffic_no_arson__" }),
                        ("Despite resisting arrest evidence, the {0} acquitted the defendant. The defence successfully challenged the lawfulness of the underlying detention.", new[] { "resisting", "obstruction" }),
                        ("The {0} acquitted the defendant on murder charges. The defence challenged causation, intent, and identification; reasonable doubt prevailed.", new[] { "murder" }),
                        ("The {0} acquitted the defendant on manslaughter charges. The defence successfully argued lack of recklessness and causation.", new[] { "manslaughter" }),
                        ("Despite homicide evidence, the {0} returned a not guilty verdict. The defence challenged forensic evidence and raised reasonable doubt.", new[] { "murder", "manslaughter" }),
                        ("The {0} acquitted the defendant on sexual assault charges. Consent and identification were successfully challenged.", new[] { "rape", "sexual assault", "sexual" }),
                        ("The {0} acquitted the defendant on sex offence charges. The defence raised sufficient doubt about the prosecution's narrative.", new[] { "rape", "sexual" }),
                        ("Despite kidnapping evidence, the {0} acquitted the defendant. Consent and unlawful restraint were successfully challenged.", new[] { "kidnapping" }),
                        ("The {0} returned a not guilty verdict on arson charges. Origin and cause were successfully challenged by the defence.", new[] { "arson" }),
                        ("Despite strong prosecution evidence, the {0} acquitted the defendant. The defence successfully excluded key evidence on procedural grounds.", null),
                        ("The {0} acquitted the defendant. Expert testimony was successfully impeached; reasonable doubt remained.", null),
                        ("In a surprising verdict, the {0} found the defendant not guilty. The defence's challenge to eyewitness identification prevailed.", null),
                        ("The {0} acquitted the defendant despite a strong case. Discovery violations and withheld evidence favoured the defence.", null),
                        ("Despite overwhelming evidence, the {0} returned a not guilty verdict. Juror nullification or credibility determinations may have factored.", null),
                        ("The {0} acquitted the defendant. The defence successfully argued that the prosecution's theory did not fit the evidence.", null),
                    };
                    var chosen = SelectChargeAwarePhrase(courtData, highEvidenceAcquittal);
                    b.AppendFormat(string.IsNullOrEmpty(chosen) ? "Despite strong prosecution evidence, the {0} returned a not guilty verdict. The defence successfully challenged key aspects of the case." : chosen, tribunal);
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
                    ("The {0} found the defendant not guilty. Chain of custody issues and handling errors undermined the physical evidence.", null),
                    ("The {0} acquitted the defendant. The prosecution could not prove the defendant's identity beyond reasonable doubt.", null),
                    ("The {0} returned a not guilty verdict. Identification procedures were flawed; the defendant was acquitted.", null),
                    ("The {0} found the defendant not guilty. Procedural defects and evidentiary rulings favoured the defence.", null),
                    ("The {0} acquitted the defendant. The search that yielded evidence was ruled unlawful.", null),
                    ("The {0} returned a not guilty verdict. Search legality was successfully challenged; key evidence was excluded.", null),
                    ("The {0} found the defendant not guilty. Miranda violations led to exclusion of incriminating statements.", null),
                    ("The {0} acquitted the defendant. Statements were suppressed due to procedural violations.", null),
                    ("The {0} returned a not guilty verdict. The defendant's alibi was not disproven beyond reasonable doubt.", null),
                    ("The {0} found the defendant not guilty. Alibi evidence raised sufficient doubt as to the defendant's presence.", null),
                    ("The {0} acquitted the defendant. Mistaken identity was a reasonable possibility given the evidence.", null),
                    ("The {0} returned a not guilty verdict. The defence established a credible mistaken identity theory.", null),
                    ("The {0} found the defendant not guilty. Conflicting testimony among witnesses created insurmountable doubt.", null),
                    ("The {0} acquitted the defendant. Inconsistent and contradictory testimony favoured acquittal.", null),
                    ("The {0} returned a not guilty verdict. The prosecution failed to prove intent beyond reasonable doubt.", null),
                    ("The {0} found the defendant not guilty. Lack of intent was established; the requisite mental element was not proven.", null),
                    ("The {0} acquitted the defendant. The defendant's claim of self-defence raised reasonable doubt.", null),
                    ("The {0} returned a not guilty verdict. Self-defence could not be ruled out; the defendant was acquitted.", null),
                    ("The {0} found the defendant not guilty. Witness credibility was fatally undermined on cross-examination.", null),
                    ("The {0} acquitted the defendant. The prosecution's witnesses were deemed unreliable by the {0}.", null),
                    ("The {0} returned a not guilty verdict. Insufficient evidence linked the defendant to the offence.", null),
                    ("The {0} found the defendant not guilty. The circumstantial evidence did not establish guilt to the required degree.", null),
                    ("The {0} acquitted the defendant. The prosecution's circumstantial case left room for reasonable alternative inferences.", null),
                    ("The {0} returned a not guilty verdict. Procedural irregularities tainted the evidence and favoured acquittal.", null),
                    ("The {0} found the defendant not guilty. Evidentiary gaps and unanswered questions supported acquittal.", null),
                    ("The {0} acquitted the defendant. The prosecution could not corroborate its key allegations.", null),
                    ("The {0} returned a not guilty verdict. Corroborating evidence was lacking; reasonable doubt prevailed.", null),
                    ("The {0} found the defendant not guilty. The defence's challenge to the arrest procedure was sustained.", null),
                    ("The {0} acquitted the defendant. Arrest legality and Fourth Amendment concerns favoured the defence.", null),
                    ("The {0} returned a not guilty verdict. Forensic evidence was inconclusive or unreliable.", null),
                    ("The {0} found the defendant not guilty. Expert testimony failed to establish guilt beyond reasonable doubt.", null),
                    ("The {0} acquitted the defendant. The {0} was not satisfied that the prosecution had met its burden.", null),
                    ("The {0} returned a not guilty verdict. The evidence, viewed as a whole, left reasonable doubt.", null),
                    ("The {0} found the defendant not guilty. Timeline inconsistencies and conflicting accounts favoured acquittal.", null),
                    ("The {0} acquitted the defendant. The prosecution's narrative did not comport with the evidence.", null),
                    ("The {0} returned a not guilty verdict. Prior inconsistent statements by witnesses undermined the case.", null),
                    ("The {0} found the defendant not guilty. Bias or motive to fabricate affected prosecution witnesses.", null),
                    ("The {0} acquitted the defendant. The defendant's version of events was plausible and not disproven.", null),
                    ("The {0} returned a not guilty verdict. Lack of physical evidence tying the defendant to the crime favoured acquittal.", null),
                    ("The {0} found the defendant not guilty. The prosecution relied on uncorroborated testimony; the {0} had reasonable doubt.", null),
                    ("The {0} acquitted the defendant. Suppression of evidence due to constitutional violations weakened the prosecution.", null),
                    ("The {0} returned a not guilty verdict. The defendant was acquitted on the strength of defence evidence and reasonable doubt.", null),
                    ("The {0} found the defendant not guilty. The prosecution failed to rebut the defence's theory of the case.", null),
                    ("The {0} acquitted the defendant. Witness identification was unreliable; mistaken identity remained a real possibility.", null),
                    ("The {0} returned a not guilty verdict. The elements of the offence were not proven beyond reasonable doubt.", null),
                    ("The {0} found the defendant not guilty. The burden of proof was not met; the presumption of innocence prevailed.", null),
                    ("The {0} acquitted the defendant. The {0} concluded the evidence was insufficient to sustain a conviction.", null),
                    ("The {0} returned a not guilty verdict. Conflicting expert opinions left reasonable doubt as to guilt.", null),
                    ("The {0} found the defendant not guilty. The prosecution's reliance on a single uncorroborated witness was insufficient.", null),
                    ("The {0} acquitted the defendant. The evidence was more consistent with innocence than guilt.", null),
                    ("The {0} returned a not guilty verdict. Police conduct and investigation flaws raised doubt about the case.", null),
                    ("The {0} found the defendant not guilty. The defence successfully challenged the reliability of the evidence.", null),
                    ("The {0} acquitted the defendant. Exculpatory evidence and reasonable doubt led to acquittal.", null),
                    ("The {0} returned a not guilty verdict. The prosecution could not establish the defendant's involvement to the required standard.", null),
                    ("The {0} found the defendant not guilty. Motive, opportunity, and means were not sufficiently proven.", null),
                    ("The {0} acquitted the defendant. The prosecution's case rested on speculation rather than proof.", null),
                    ("The {0} returned a not guilty verdict. Discovery violations and withheld evidence contributed to acquittal.", null),
                    ("The {0} found the defendant not guilty. The defendant was acquitted; the evidence did not eliminate reasonable doubt.", null),
                    ("The {0} acquitted the defendant. Insufficient evidence; the prosecution could not meet its burden.", null),
                    ("The {0} returned a not guilty verdict. The prosecution could not establish a prima facie case.", null),
                    ("The {0} found the defendant not guilty. Corroboration was lacking; the prosecution's case was thin.", null),
                    ("The {0} acquitted the defendant. The evidence was inconclusive as to the defendant's role.", null),
                    ("The {0} returned a not guilty verdict. The defence's motion for acquittal was granted.", null),
                    ("The {0} found the defendant not guilty. Hearsay and inadmissible evidence weakened the prosecution.", null),
                    ("The {0} acquitted the defendant. The prosecution's timeline did not hold up under scrutiny.", null),
                    ("The {0} returned a not guilty verdict. Physical evidence was inconclusive or absent.", null),
                    ("The {0} found the defendant not guilty. The prosecution could not explain away exculpatory evidence.", null),
                    ("The {0} acquitted the defendant. The evidence did not support the prosecution's theory of the case.", null),
                    ("The {0} returned a not guilty verdict. Lack of motive evidence favoured the defence.", null),
                    ("The {0} found the defendant not guilty. The prosecution's case was built on inference rather than proof.", null),
                    ("The {0} acquitted the defendant. Eyewitness testimony was impeached; identification failed.", null),
                    ("The {0} returned a not guilty verdict. The prosecution could not establish the defendant's presence at the scene.", null),
                    ("The {0} found the defendant not guilty. Documentary evidence was ambiguous or exculpatory.", null),
                    ("The {0} acquitted the defendant. The prosecution failed to prove the corpus delicti.", null),
                    ("The {0} returned a not guilty verdict. Reasonable alternative explanations were not ruled out.", null),
                    ("The {0} found the defendant not guilty. The prosecution's circumstantial case had too many gaps.", null),
                    ("The {0} acquitted the defendant. The burden of proof was not sustained on any count.", null),
                    ("The {0} returned a not guilty verdict. The evidence did not establish guilt to a moral certainty.", null),
                    ("The {0} found the defendant not guilty. The prosecution could not overcome the defendant's presumption of innocence.", null),
                    ("The {0} acquitted the defendant. The prosecution's evidence was speculative and insufficient.", null),
                    ("The {0} returned a not guilty verdict. The defence raised doubt on every essential element.", null),
                    ("The {0} found the defendant not guilty. The prosecution could not prove the defendant's knowledge or intent.", null),
                    ("The {0} acquitted the defendant. The evidence was as consistent with innocence as with guilt.", null),
                    ("The {0} returned a not guilty verdict. The prosecution failed to establish a complete chain of evidence.", null),
                    ("The {0} found the defendant not guilty. The prosecution could not exclude the possibility of third-party culpability.", null),
                    ("The {0} acquitted the defendant. The prosecution's witnesses had credibility issues.", null),
                    ("The {0} returned a not guilty verdict. The prosecution could not prove the defendant acted unlawfully.", null),
                    ("The {0} found the defendant not guilty. The prosecution's case depended on testimony that was not credible.", null),
                    ("The {0} acquitted the defendant. The prosecution could not establish the requisite mens rea.", null),
                    ("The {0} returned a not guilty verdict. The prosecution's evidence was circumstantial and insufficient.", null),
                    ("The {0} found the defendant not guilty. The prosecution could not prove the actus reus.", null),
                    ("The {0} acquitted the defendant. The prosecution's theory was not supported by the weight of the evidence.", null),
                    ("The {0} returned a not guilty verdict. The prosecution failed to present sufficient evidence on any element.", null),
                    ("The {0} found the defendant not guilty. The prosecution could not establish causation.", null),
                    ("The {0} acquitted the defendant. The prosecution's case was undermined by its own witnesses.", null),
                    ("The {0} returned a not guilty verdict. The prosecution could not prove the defendant committed the act.", null),
                    ("The {0} found the defendant not guilty. The prosecution's evidence was insufficient as a matter of law.", null),
                    ("The {0} acquitted the defendant. The prosecution could not establish a connection between the defendant and the crime.", null),
                    ("The {0} returned a not guilty verdict. The prosecution's case was based on conjecture.", null),
                    ("The {0} found the defendant not guilty. The prosecution could not prove the defendant had the requisite intent.", null),
                    ("The {0} acquitted the defendant. The prosecution's evidence was contradicted by physical evidence.", null),
                    ("The {0} returned a not guilty verdict. The prosecution failed to meet its burden on identification.", null),
                    ("The {0} found the defendant not guilty. The prosecution could not establish the defendant's guilt.", null),
                    ("The {0} acquitted the defendant. The prosecution's case lacked the necessary evidentiary foundation.", null),
                    ("The {0} returned a not guilty verdict. The prosecution could not prove the defendant's involvement.", null),
                    ("The {0} found the defendant not guilty. The prosecution's witnesses were not believed.", null),
                    ("The {0} acquitted the defendant. The prosecution could not sustain its burden of proof.", null),
                    ("The {0} returned a not guilty verdict. The prosecution failed to prove guilt beyond a reasonable doubt on any charge.", null),
                    ("The {0} found the defendant not guilty. The prosecution's evidence was insufficient to support a conviction.", null),
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
                        $"Defence counsel {courtData.DefenseAttorneyName} secured an acquittal through skilled representation.",
                        $"{courtData.DefenseAttorneyName} effectively challenged the prosecution's case and secured acquittal.",
                        $"Private counsel {courtData.DefenseAttorneyName} raised sufficient reasonable doubt to secure a not guilty verdict.",
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
                        $"The jury returned a not guilty verdict by a margin of {courtData.JuryVotesForAcquittal} to {courtData.JuryVotesForConviction}.",
                        $"A {courtData.JuryVotesForAcquittal}-{courtData.JuryVotesForConviction} vote in favour of acquittal was recorded.",
                        $"Jurors voted {courtData.JuryVotesForAcquittal}-{courtData.JuryVotesForConviction} for acquittal.",
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
                        "Court congestion led to an expedited disposition of the matter.",
                        "The case was resolved on a compressed schedule due to docket pressure.",
                    };
                    b.Append(" " + docketAcquittal[Helper.GetRandomInt(0, docketAcquittal.Length - 1)]);
                }
                AppendChargeDomainPhrase(b, courtData, resolvedStatus);
            } else if (resolvedStatus == 3) {
                // Evidence-band-aware: when EvidenceBand is Low, prefer insufficient-evidence / burden wording. Charge-specific phrasing favoured.
                var lowEvidenceDismissals = new (string text, string[] chargeKeywords)[] {
                    ("The case was dismissed. Insufficient evidence to proceed to trial.", null),
                    ("Charges were dismissed. The prosecution could not meet its burden of proof.", null),
                    ("The court dismissed the case. Insufficient evidence was cited.", null),
                    ("The case was dismissed. The prosecution could not meet its burden at this stage.", null),
                    ("Charges were dismissed. The evidence was insufficient to support prosecution.", null),
                    ("The court entered a dismissal. The prosecution could not establish a prima facie case.", null),
                    ("The case was dismissed. Evidence did not rise to the level required for trial.", null),
                    ("Charges were dismissed. The prosecution could not sustain its burden of proof.", null),
                    ("The court dismissed the case. Insufficient evidence to support the charges.", null),
                    ("Charges were dismissed. Lack of corroborating evidence prevented prosecution.", null),
                    ("The case was dismissed. The prosecution could not demonstrate probable cause.", null),
                    ("Charges were dismissed. The evidence was insufficient to establish the elements of the offence.", null),
                    ("The court entered a dismissal. The prosecution failed to meet its evidentiary burden.", null),
                    ("Charges were dismissed. Insufficient evidence; the prosecution could not proceed.", null),
                    ("The case was dismissed. The evidence fell short of what was required to go to trial.", null),
                    ("Charges were dismissed. Burden of proof could not be met at this stage.", null),
                    ("The court dismissed the case. The prosecution could not adduce sufficient evidence.", null),
                    ("Charges were dismissed. Evidence was inadequate to support a conviction.", null),
                    ("The case was dismissed. The prosecution's evidence was deemed insufficient.", null),
                    ("Charges were dismissed. Could not establish a prima facie case.", null),
                    ("DUI charges dismissed. Insufficient evidence to support prosecution—lack of reliable BAC documentation or calibration records.", new[] { "__dui_charge__" }),
                    ("Impaired driving charges were dismissed. The prosecution could not meet its burden of proof; breath test validity was contested.", new[] { "__dui_charge__" }),
                    ("DUI charges dismissed. Evidence of impairment was insufficient; field sobriety procedures were not properly documented.", new[] { "__dui_charge__" }),
                    ("The court dismissed the DUI charges. The prosecution could not establish impairment beyond a reasonable doubt.", new[] { "__dui_charge__" }),
                    ("DWI charges were dismissed. Chain of custody for breath samples and calibration records could not be established.", new[] { "__dui_charge__" }),
                    ("DUI charges dismissed. Insufficient evidence—traffic stop justification and testing protocol were challenged.", new[] { "__dui_charge__" }),
                    ("Chemical test refusal charges dismissed. The prosecution could not meet its burden; procedural defects cited.", new[] { "__dui_charge__" }),
                    ("Drug possession charges dismissed. Insufficient evidence—chain of custody and search legality could not be established.", new[] { "__drug_crime__" }),
                    ("Narcotics charges were dismissed. The prosecution could not meet its burden of proof; the search was deemed unlawful.", new[] { "__drug_crime__" }),
                    ("Drug charges dismissed. Insufficient evidence to establish possession; evidence handling was challenged.", new[] { "__drug_crime__" }),
                    ("Controlled substance charges were dismissed. The prosecution could not establish chain of custody.", new[] { "__drug_crime__" }),
                    ("Trafficking charges dismissed. Insufficient evidence; the prosecution could not meet its burden for intent to distribute.", new[] { "__trafficking_drug__" }),
                    ("Assault charges dismissed. Insufficient evidence—identification and intent could not be established.", new[] { "assault" }),
                    ("Battery charges were dismissed. The prosecution could not meet its burden of proof; witness credibility was lacking.", new[] { "battery" }),
                    ("Assault and battery charges dismissed. Insufficient evidence; the victim declined to cooperate.", new[] { "assault", "battery" }),
                    ("Domestic violence charges were dismissed. The prosecution could not establish the elements; insufficient corroboration.", new[] { "domestic", "battery", "assault" }),
                    ("Theft charges dismissed. Insufficient evidence to establish ownership and intent.", new[] { "theft", "larceny" }),
                    ("Burglary charges were dismissed. The prosecution could not meet its burden; no evidence of unlawful entry.", new[] { "burglary" }),
                    ("Robbery charges dismissed. Insufficient evidence—identification and force or fear could not be proven.", new[] { "robbery" }),
                    ("Grand theft charges were dismissed. The prosecution could not establish value or intent.", new[] { "grand theft", "theft" }),
                    ("Stolen property charges dismissed. Insufficient evidence of knowing possession.", new[] { "stolen" }),
                    ("Firearm charges were dismissed. The prosecution could not establish possession or unlawful intent.", new[] { "firearm", "weapon", "gun" }),
                    ("Weapon possession charges dismissed. Insufficient evidence; search legality was successfully challenged.", new[] { "weapon", "firearm", "gun" }),
                    ("Carrying concealed weapon charges were dismissed. The prosecution could not meet its burden of proof.", new[] { "concealed", "weapon" }),
                    ("Evading charges dismissed. Insufficient evidence to establish identity of the driver or necessity of pursuit.", new[] { "evad", "fleeing", "elude" }),
                    ("Evading peace officer charges were dismissed. The prosecution could not establish the elements.", new[] { "evad", "evading", "elude" }),
                    ("Reckless evading charges dismissed. Insufficient evidence—pursuit protocol and identification were contested.", new[] { "reckless evad", "evading", "elude" }),
                    ("Traffic charges dismissed. Insufficient evidence; the prosecution could not establish the elements.", new[] { "__traffic_no_arson__" }),
                    ("Reckless driving charges were dismissed. The prosecution could not meet its burden of proof.", new[] { "reckless driving" }),
                    ("Hit and run charges dismissed. Insufficient evidence to establish the defendant was the driver.", new[] { "hit and run", "leaving the scene" }),
                    ("Driving on suspended license charges were dismissed. Documentation of suspension could not be established.", new[] { "suspended", "license" }),
                    ("Resisting arrest charges dismissed. The prosecution could not establish the lawfulness of the underlying detention.", new[] { "resisting", "obstruction" }),
                    ("Obstruction charges were dismissed. Insufficient evidence; the underlying arrest was challenged.", new[] { "obstruction", "resisting" }),
                    ("Murder charges dismissed. Insufficient evidence—the prosecution could not establish causation or intent.", new[] { "murder" }),
                    ("Manslaughter charges were dismissed. The prosecution could not meet its burden of proof.", new[] { "manslaughter" }),
                    ("Homicide charges were dismissed. Evidence was insufficient to proceed; key forensic evidence was unavailable.", new[] { "murder", "manslaughter" }),
                    ("Sexual assault charges dismissed. The prosecution could not establish the elements; insufficient corroboration.", new[] { "rape", "sexual assault", "sexual battery" }),
                    ("Sex offence charges were dismissed. The prosecution could not meet its burden; consent and identification were at issue.", new[] { "rape", "sexual" }),
                    ("Kidnapping charges dismissed. Insufficient evidence to establish unlawful restraint or movement.", new[] { "kidnapping" }),
                    ("Arson charges were dismissed. Origin and cause could not be established; insufficient evidence.", new[] { "arson" }),
                    ("Arson charges were dismissed. The prosecution could not meet its burden of proof.", new[] { "arson", "burning" }),
                    ("Vandalism charges dismissed. Insufficient evidence of damage valuation and intent.", new[] { "vandalism" }),
                    ("Criminal trespass charges were dismissed. The prosecution could not establish unlawful entry.", new[] { "trespass" }),
                    ("Escape charges were dismissed. Insufficient evidence that the defendant was in lawful custody.", new[] { "escape" }),
                    ("Probation violation charges dismissed. The prosecution could not establish the breach.", new[] { "probation", "parole" }),
                    ("Disorderly conduct charges were dismissed. The prosecution could not meet its burden of proof.", new[] { "disorderly", "disturbing" }),
                    ("Fraud charges were dismissed. Insufficient evidence to establish intent to defraud.", new[] { "fraud", "embezzlement" }),
                };
                var dismissedPool = new (string text, string[] chargeKeywords)[] {
                    ("Charges were dismissed. The case did not proceed to trial.", null),
                    ("The charges were dismissed. The prosecution declined to proceed.", null),
                    ("The case was dismissed. Insufficient evidence to proceed to trial.", null),
                    ("Charges were dismissed on procedural grounds. The case did not go to trial.", null),
                    ("The court dismissed the case. The matter was not tried on the merits.", null),
                    ("The case was dismissed. The prosecution could not meet its burden at this stage.", null),
                    ("Charges were dismissed without prejudice. The matter was not tried on the merits.", null),
                    ("The charges were dismissed with prejudice. The case will not be refiled.", null),
                    ("The court entered nolle prosequi. The prosecution elected not to proceed.", null),
                    ("Charges were dismissed. The matter did not proceed to trial due to evidentiary issues.", null),
                    ("The case was dismissed. Key witnesses were unavailable or evidence was suppressed.", null),
                    ("The prosecution declined to proceed. Charges were dismissed.", null),
                    ("Charges were dismissed on procedural grounds. The matter did not reach trial.", null),
                    ("The case was dismissed. Procedural defects led to dismissal.", null),
                    ("The charges were dismissed. The case did not reach trial.", null),
                    ("The court dismissed the case. Insufficient evidence was cited.", null),
                    ("Charges were dismissed. The prosecution elected to decline prosecution.", null),
                    ("The case was dismissed. The matter was not tried on the merits.", null),
                    ("The charges were dismissed. Did not proceed to trial.", null),
                    ("The court dismissed the case. The prosecution declined to proceed.", null),
                    ("Charges were dismissed. The prosecution withdrew the charges.", null),
                    ("The case was dismissed. Evidentiary problems prevented the case from going forward.", null),
                    ("The charges were dismissed. The case was resolved without a trial.", null),
                    ("The court entered a dismissal. The matter was not tried on the merits.", null),
                    ("Charges were dismissed. The prosecution determined it could not prevail.", null),
                    ("Charges were dismissed. Witnesses failed to appear or were unwilling to testify.", null),
                    ("The case was dismissed. A key witness recanted or became unavailable.", null),
                    ("Charges were dismissed. Chain of custody problems made the evidence inadmissible.", null),
                    ("The case was dismissed. Evidence was suppressed following a motion to suppress.", null),
                    ("Charges were dismissed. Procedural deadlines were missed by the prosecution.", null),
                    ("The case was dismissed. The defendant's speedy trial rights would have been violated.", null),
                    ("Charges were dismissed. Identification evidence was deemed unreliable.", null),
                    ("Charges were dismissed. The prosecution elected to focus resources elsewhere.", null),
                    ("Charges were dismissed. Plea negotiations with co-defendants led to dismissal.", null),
                    ("Charges were dismissed. New exculpatory evidence emerged before trial.", null),
                    ("The case was dismissed. The victim declined to cooperate with prosecution.", null),
                    ("Charges were dismissed. Jurisdictional or venue issues prevented trial.", null),
                    ("The case was dismissed. The statute of limitations had expired.", null),
                    ("Charges were dismissed. Double jeopardy or collateral estoppel applied.", null),
                    ("The case was dismissed. The charges were duplicative of another pending case.", null),
                    ("DUI charges were dismissed. The victim of the alleged impairment incident declined to testify.", new[] { "__dui_charge__" }),
                    ("Drug charges were dismissed. A key witness was unavailable; the prosecution could not proceed.", new[] { "__drug_crime__" }),
                    ("Assault charges were dismissed. The alleged victim recanted before trial.", new[] { "assault", "battery" }),
                    ("Theft charges were dismissed. The complaining witness failed to appear.", new[] { "theft", "larceny", "burglary" }),
                    ("Firearm charges were dismissed. Evidence was suppressed; the search was ruled unconstitutional.", new[] { "firearm", "weapon" }),
                    ("Evading charges were dismissed. The pursuing officer was unavailable to testify.", new[] { "evad", "fleeing", "elude" }),
                    ("Traffic charges were dismissed. Calibration records for the speed measurement device were lost.", new[] { "speeding", "traffic" }),
                };
                if (evidenceBand == 0 && Helper.GetRandomInt(0, 99) < 65) {
                    var lowResult = SelectChargeAwarePhrase(courtData, lowEvidenceDismissals);
                    b.Append(string.IsNullOrEmpty(lowResult) ? "The case was dismissed. Insufficient evidence to proceed to trial." : lowResult);
                } else {
                    var dismResult = SelectChargeAwarePhrase(courtData, dismissedPool);
                    b.Append(string.IsNullOrEmpty(dismResult) ? "Charges were dismissed. The case did not proceed to trial." : dismResult);
                }
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
            // Traffic/evading: exclude arson (reckless burning) and firearm (reckless discharge of firearm) from "reckless" catch-all
            if (!HasArsonCharge(courtData) && (HasChargeKeyword(courtData, "traffic") || HasChargeKeyword(courtData, "speeding") || HasChargeKeyword(courtData, "evading")
                || HasChargeKeyword(courtData, "street racing") || HasChargeKeyword(courtData, "hit and run") || HasChargeKeyword(courtData, "wrong side")
                || HasChargeKeyword(courtData, "driving on suspended") || HasChargeKeyword(courtData, "driving without license") || HasChargeKeyword(courtData, "license expired")
                || HasChargeKeyword(courtData, "refusal to sign traffic") || HasChargeKeyword(courtData, "impeding traffic")
                || (HasChargeKeyword(courtData, "reckless") && !HasChargeKeyword(courtData, "burning") && !HasChargeKeyword(courtData, "firearm")))) {
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
            bool pleaGuiltyOrNoContest = string.Equals(courtData.Plea, "Guilty", StringComparison.OrdinalIgnoreCase) || string.Equals(courtData.Plea, "No Contest", StringComparison.OrdinalIgnoreCase);

            if (pleaGuiltyOrNoContest) {
                string[] pleaPhrases = new[] {
                    "The defendant's guilty plea was taken into account as a mitigating factor in sentencing.",
                    "Having regard to the defendant's acceptance of responsibility, the court imposed a reduced sentence.",
                    "The court applied a sentencing discount in light of the defendant's guilty plea.",
                    $"{judge} noted the defendant's guilty plea when determining the appropriate sentence.",
                    "The defendant's early acceptance of guilt was considered as a mitigating factor.",
                };
                b.Append(pleaPhrases[Helper.GetRandomInt(0, pleaPhrases.Length - 1)]);
            }

            if (courtData.RepeatOffenderScore >= 6) {
                if (b.Length > 0) b.Append(" ");
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
                    $"{judge} cited the defendant's entrenched recidivism and disregard for the law as grounds for a substantial sentence.",
                    "The court found that the defendant's repeat offending pattern left no alternative but a lengthy custodial term.",
                    $"{judge} emphasised that the defendant's persistent criminality warranted a sentence that would protect the public.",
                    "The defendant's history of recidivism demonstrated that lesser sanctions had been exhausted; the court imposed accordingly.",
                    $"{judge} noted that the defendant's repeated return to criminal conduct justified a sentence at the upper end of the range.",
                    "The court considered the defendant's longstanding pattern of reoffending and the need for incapacitation.",
                    $"{judge} found that the defendant's recidivist behaviour and failure to reform warranted an elevated sentence.",
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
                    $"{judge} applied the sentencing guidelines in light of the defendant's prior convictions and the current offence.",
                    "The defendant's prior record was given due weight in determining the appropriate custodial term.",
                    $"The court, having regard to the prior convictions, fashioned a sentence that reflected both the offence and the defendant's history.",
                    $"{judge} took the defendant's prior convictions into consideration when selecting the sentence within the range.",
                    "The defendant's criminal history was applied as an aggravating factor under the sentencing framework.",
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
                    $"{judge} cited the taking of human life as the paramount factor in determining the appropriate sentence.",
                    "The court found that the homicide convictions demanded the most severe sentencing response available.",
                    $"The loss of life and circumstances of the offence were emphasised by {judge} in imposing sentence.",
                    "The murder and manslaughter convictions warranted a sentence reflecting the utmost gravity of the conduct.",
                    $"{judge} considered the irreparable harm caused by the homicide and imposed sentence accordingly.",
                };
                b.Append(homicideSent[Helper.GetRandomInt(0, homicideSent.Length - 1)]);
            }
            if (HasSexOffenseCharge(courtData)) {
                if (b.Length > 0) b.Append(" ");
                string[] sexSent = new[] {
                    "The sexual offence convictions warranted a severe sentencing response.",
                    "The court emphasised the gravity of the sex crimes in fashioning sentence.",
                    "Protection of vulnerable persons and denunciation of sexual violence informed the sentence.",
                    $"{judge} cited the sexual offences as among the most serious in the criminal calendar.",
                    "The court found that the sexual offence convictions warranted a substantial custodial sentence.",
                    $"The need to protect the public and denounce sexual violence was central to {judge}'s sentencing approach.",
                    "The sexual offence convictions were weighed heavily in determining the appropriate sentence.",
                };
                b.Append(sexSent[Helper.GetRandomInt(0, sexSent.Length - 1)]);
            }
            if (HasKidnappingCharge(courtData)) {
                if (b.Length > 0) b.Append(" ");
                string[] kidnapSent = new[] {
                    "The kidnapping convictions were central to the sentencing decision.",
                    "The court treated abduction-related offences as highly aggravating.",
                    $"{judge} emphasised the serious nature of the kidnapping charges in imposing sentence.",
                    "The deprivation of liberty and risk to the victim weighed heavily in the sentencing decision.",
                    $"The kidnapping convictions warranted a substantial sentence, as noted by {judge}.",
                };
                b.Append(kidnapSent[Helper.GetRandomInt(0, kidnapSent.Length - 1)]);
            }
            if (HasArsonCharge(courtData)) {
                if (b.Length > 0) b.Append(" ");
                string[] arsonSent = new[] {
                    "The arson convictions factored heavily into the sentence imposed.",
                    "Fire-related offences and risk to life and property were emphasised in sentencing.",
                    $"{judge} cited the danger posed by the arson and the risk to life and property as aggravating factors.",
                    "The court found that arson offences warranted a substantial sentence given the potential for harm.",
                    $"The arson convictions were treated as highly serious by {judge} in fashioning the sentence.",
                };
                b.Append(arsonSent[Helper.GetRandomInt(0, arsonSent.Length - 1)]);
            }
            if (HasChargeKeyword(courtData, "robbery") || HasChargeKeyword(courtData, "burglary") || HasChargeKeyword(courtData, "carjacking") || HasChargeKeyword(courtData, "home invasion")) {
                if (b.Length > 0) b.Append(" ");
                string[] robberySent = new[] {
                    "The robbery and burglary convictions were central to the sentencing decision.",
                    $"{judge} emphasised the seriousness of the property crime and the threat to victims in imposing sentence.",
                    "The court found that the robbery or burglary charges warranted a substantial custodial sentence.",
                    "The invasion of property and threat of violence were weighed heavily in the sentencing decision.",
                    $"{judge} cited the robbery or burglary convictions as warranting a sentence reflecting the gravity of the conduct.",
                };
                b.Append(robberySent[Helper.GetRandomInt(0, robberySent.Length - 1)]);
            }
            if (HasDrugCrimeCharge(courtData)) {
                if (b.Length > 0) b.Append(" ");
                string[] drugSent = new[] {
                    "The drug offence convictions were central to the sentencing decision.",
                    $"{judge} considered the nature and quantity of the controlled substances in fashioning the sentence.",
                    "The court weighed the drug-related charges and the need for deterrence in imposing sentence.",
                    "The drug convictions warranted a sentence reflecting the seriousness of narcotics offences.",
                    $"{judge} cited the drug offences as aggravating the need for both punishment and rehabilitation.",
                };
                b.Append(drugSent[Helper.GetRandomInt(0, drugSent.Length - 1)]);
            }
            if (!HasArsonCharge(courtData) && (HasChargeKeyword(courtData, "traffic") || HasChargeKeyword(courtData, "speeding") || HasChargeKeyword(courtData, "evading")
                || HasChargeKeyword(courtData, "street racing") || HasChargeKeyword(courtData, "hit and run") || HasChargeKeyword(courtData, "DUI") || HasChargeKeyword(courtData, "DWI")
                || HasChargeKeyword(courtData, "driving under") || HasChargeKeyword(courtData, "chemical test") || HasChargeKeyword(courtData, "field sobriety") || HasChargeKeyword(courtData, "driving on suspended")
                || HasChargeKeyword(courtData, "driving without license") || HasChargeKeyword(courtData, "license expired") || (HasChargeKeyword(courtData, "reckless") && !HasChargeKeyword(courtData, "burning") && !HasChargeKeyword(courtData, "firearm")))) {
                if (b.Length > 0) b.Append(" ");
                string[] trafficSent = new[] {
                    "The traffic and driving-related convictions were considered in imposing sentence.",
                    $"{judge} weighed the traffic offence or DUI convictions in fashioning the sentence.",
                    "The court found that the driving charges warranted a sentence reflecting the risk to public safety.",
                    "The traffic or DUI convictions were central to the sentencing decision.",
                    $"{judge} cited the driving-related conduct and risk to others as factors in determining the sentence.",
                };
                b.Append(trafficSent[Helper.GetRandomInt(0, trafficSent.Length - 1)]);
            }
            if (HasFraudCharge(courtData)) {
                if (b.Length > 0) b.Append(" ");
                string[] fraudSent = new[] {
                    "The fraud and financial crime convictions were central to the sentencing decision.",
                    $"{judge} considered the extent of the financial harm and breach of trust in imposing sentence.",
                    "The court found that the fraud convictions warranted a sentence reflecting the seriousness of the conduct.",
                    $"{judge} cited the calculated nature of the fraud and the harm to victims as aggravating factors.",
                    "The court weighed the breach of trust and financial loss to victims in imposing sentence.",
                };
                b.Append(fraudSent[Helper.GetRandomInt(0, fraudSent.Length - 1)]);
            }
            if (HasChargeKeyword(courtData, "firearm") || HasChargeKeyword(courtData, "weapon") || HasChargeKeyword(courtData, "gun") || HasChargeKeyword(courtData, "armed")) {
                if (b.Length > 0) b.Append(" ");
                string[] firearmSent = new[] {
                    "The firearms charges were central to the sentencing decision.",
                    $"{judge} emphasised the danger posed by unlawful weapon possession in fashioning the sentence.",
                    "The court found that the firearms convictions warranted a substantial custodial sentence.",
                    $"{judge} cited the weapon charges as an aggravating factor in determining the appropriate sentence.",
                    "The court weighed the unlawful possession of firearms and the risk to public safety in sentencing.",
                };
                b.Append(firearmSent[Helper.GetRandomInt(0, firearmSent.Length - 1)]);
            }
            if (HasChargeKeyword(courtData, "resisting") || HasChargeKeyword(courtData, "obstruction") || HasChargeKeyword(courtData, "refusing") || HasChargeKeyword(courtData, "failure to present")) {
                if (b.Length > 0) b.Append(" ");
                string[] resistingSent = new[] {
                    "The resisting or obstruction convictions were considered as aggravating factors in sentencing.",
                    $"{judge} noted the defendant's refusal to comply with lawful authority in fashioning the sentence.",
                    "The court weighed the resisting arrest or obstruction charges in imposing sentence.",
                    $"The defendant's resistance to lawful arrest was cited by {judge} as an aggravating factor.",
                    "The court considered the obstruction of justice and refusal to comply as factors in the sentence.",
                };
                b.Append(resistingSent[Helper.GetRandomInt(0, resistingSent.Length - 1)]);
            }
            if (HasChargeKeyword(courtData, "violation of probation") || HasChargeKeyword(courtData, "violation of parole") || HasChargeKeyword(courtData, "protective order") || HasChargeKeyword(courtData, "failure to register as sex offender") || HasChargeKeyword(courtData, "warrant")) {
                if (b.Length > 0) b.Append(" ");
                string[] warrantSent = new[] {
                    "The probation, parole, or warrant-related convictions were central to the sentencing decision.",
                    $"{judge} considered the defendant's failure to comply with prior court orders in imposing sentence.",
                    "The court found that the supervision violation warranted a consecutive or enhanced sentence.",
                    $"{judge} cited the defendant's disregard for court orders and outstanding warrants as aggravating factors.",
                    "The court weighed the defendant's breach of supervision and outstanding warrant status in imposing sentence.",
                };
                b.Append(warrantSent[Helper.GetRandomInt(0, warrantSent.Length - 1)]);
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
                    $"The court found the offences to be of such seriousness that {judge} imposed a sentence at the upper end of the range.",
                    "The threat to public safety and the gravity of the conduct justified a substantial custodial term.",
                    $"{judge} emphasised the need to protect the community given the serious nature of the offences.",
                    "The offences were of sufficient gravity to warrant a sentence reflecting the highest level of culpability.",
                    $"The court considered the serious threat posed to the public and imposed sentence accordingly.",
                    "The nature and circumstances of the offences demanded a substantial sentence for the protection of the public.",
                    $"{judge} found that the seriousness of the conduct and the risk to public safety warranted an elevated sentence.",
                    "The court imposed a substantial sentence having regard to the grave nature of the offences and threat to public safety.",
                    $"The offences demonstrated such a threat to public safety that {judge} imposed a sentence toward the top of the range.",
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
                        $"The jury's unanimous verdict left no doubt as to guilt; {judge} imposed sentence accordingly.",
                        "The court accorded significant weight to the unanimous jury verdict in determining the sentence.",
                        $"The unanimous jury verdict was cited by {judge} as supporting a substantial sentencing response.",
                        "The jury's unanimous finding of guilt reinforced the court's assessment of the seriousness of the conduct.",
                        $"The unanimous verdict of the jury informed {judge}'s determination of the appropriate sentence.",
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
                        "The court took the jury's split decision into account when fashioning the sentence.",
                        $"Given the close jury vote, {judge} balanced the verdict with appropriate sentencing considerations.",
                        "The narrow margin of the jury's decision was reflected in the court's sentencing approach.",
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
                    $" {judge} imposed a sentence reflective of this jurisdiction's practice for similar offences.",
                    " The sentence was fashioned in accordance with local sentencing norms for comparable cases.",
                    $" The court applied {judge}'s assessment of how similar matters are typically disposed in this district.",
                    $" The sentence reflected the district's sentencing practice for offences of this nature.",
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
                    " The court reduced the sentence having regard to mitigating factors presented.",
                    $" {judge} gave weight to mitigating circumstances in selecting a sentence below the guideline midpoint.",
                    " The sentence was moderated in light of the mitigating factors in the case.",
                    $" Mitigating circumstances led {judge} to impose a sentence that reflected leniency where appropriate.",
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
                    $"The defendant's use of force and threat of violence were emphasised by {judge} in imposing sentence.",
                    "The court found that the assaultive conduct and weapon use significantly aggravated the offence.",
                    $"The weapon or violent conduct at the time of the offence was cited by {judge} as warranting an elevated sentence.",
                    "The defendant's violent behaviour and disregard for the safety of others weighed heavily in sentencing.",
                    $"The court considered the defendant's assaultive conduct and armed status as significant aggravating factors.",
                    "The violent or armed nature of the offence justified a sentence toward the upper end of the range.",
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

                // Plea bargain: 10-25% sentence discount when Guilty/No Contest
                float pleaDiscount = pleaGuiltyOrNoContest ? (1f - (Helper.GetRandomInt(10, 25) / 100f)) : 1f;

                if (courtCase.Charges != null) {
                    bool juryConvicted = false;
                    if (courtCase.IsJuryTrial && courtCase.JurySize > 0) {
                        juryConvicted = courtCase.JuryVotesForConviction > (courtCase.JurySize / 2);
                    }

                    foreach (CourtData.Charge charge in courtCase.Charges) {
                        if (charge == null) continue;
                        if (pleaGuiltyOrNoContest) {
                            charge.Outcome = 1; // Convicted
                            charge.ConvictionChance = null;
                        } else if (courtCase.IsJuryTrial && courtCase.JurySize > 0) {
                            charge.Outcome = juryConvicted ? 1 : 2;
                            charge.ConvictionChance = null;
                        } else {
                            int chance = GetPerChargeConvictionChance(courtCase, charge);
                            charge.ConvictionChance = chance;
                            int roll = Helper.GetRandomInt(1, 100);
                            charge.Outcome = roll <= chance ? 1 : 2; // 1 Convicted, 2 Acquitted
                        }

                        if (charge.Outcome == 1) {
                            charge.SentenceDaysServed = RollSentenceForCharge(courtCase, charge, pleaDiscount);
                        }
                    }
                }

                int newStatus = (courtCase.Charges != null && courtCase.Charges.Any(c => c != null && c.Outcome == 1)) ? 1 : 2;
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
                DateTime nowUtc = DateTime.UtcNow;
                courtCase.LastUpdatedUtc = nowUtc.ToString("o");
                // If resolved before the scheduled trial/court time (e.g. Force Resolve), store actual
                // resolution time so the case timeline and docket date are not still in the future.
                if (!string.IsNullOrEmpty(courtCase.ResolveAtUtc)
                    && DateTime.TryParse(courtCase.ResolveAtUtc, null, DateTimeStyles.RoundtripKind, out DateTime scheduledResolve)
                    && nowUtc < scheduledResolve) {
                    courtCase.ResolveAtUtc = nowUtc.ToString("o");
                }

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
                int convictedCount = courtCase.Charges?.Count(c => c != null && c.Outcome == 1) ?? 0;
                Helper.Log($"Court case {courtCase.Number} auto-resolved: {(newStatus == 1 ? "Convicted" : "Acquitted")} ({convictedCount}/{courtCase.Charges?.Count ?? 0} charges) (plea: {courtCase.Plea})", false, Helper.LogSeverity.Info);

                string defendantName = !string.IsNullOrWhiteSpace(courtCase.PedName) ? courtCase.PedName.Trim() : "Unknown";
                string msg = string.Format(Setup.SetupController.GetLanguage().court.trialHeardNotification, courtCase.Number ?? "?", defendantName);
                Utility.RageNotification.Show(msg, Utility.RageNotification.NotificationType.Info);
            } catch (Exception e) {
                Helper.Log($"Auto-resolution failed for case {courtCase?.Number}: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>Rolls sentence days for a convicted charge. Life sentence returns null. Applies range roll, SentenceMultiplier, plea discount, and judge leniency.</summary>
        private static int? RollSentenceForCharge(CourtData courtCase, CourtData.Charge charge, float pleaDiscount) {
            if (charge.Time == null && (!charge.MaxDays.HasValue || charge.MaxDays.Value <= 0)) return null; // Life
            int minD = charge.MinDays;
            int maxD = charge.MaxDays ?? minD;
            if (maxD < minD) maxD = minD;
            // Legacy charges (no MinDays/MaxDays): use Time as fixed base
            if (minD <= 0 && maxD <= 0 && charge.Time.HasValue && charge.Time.Value > 0) {
                minD = charge.Time.Value;
                maxD = charge.Time.Value;
            }
            if (minD <= 0 && maxD <= 0) return 0;

            float t = Helper.GetRandomInt(0, 100) / 100f;
            float leniency = GetJudgeLeniencyFromName(courtCase.JudgeName);
            if (leniency != 0) t = Math.Max(0, Math.Min(1, t + leniency * 0.35f));
            int rolled = minD + (int)Math.Round(t * (maxD - minD));
            rolled = Math.Max(minD, Math.Min(maxD, rolled));

            float mult = courtCase.SentenceMultiplier > 0 ? courtCase.SentenceMultiplier : 1f;
            int sentence = Math.Max(0, (int)Math.Round(rolled * mult * pleaDiscount));
            return sentence;
        }

        private static int GetSeverityScore(CourtData courtData) {
            if (courtData?.Charges == null) return 0;

            int score = 0;
            foreach (CourtData.Charge charge in courtData.Charges) {
                if (charge == null) continue;
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

        /// <summary>Selects prosecutor from district's prosecution office. Returns "Office — Lawyer".</summary>
        private static string SelectProsecutor(CourtDistrictProfile district, string caseNumber, int caseWeight) {
            if (district == null || string.IsNullOrEmpty(district.ProsecutionOffice) || district.ProsecutionLawyers == null || district.ProsecutionLawyers.Length == 0)
                return null;
            string lawyer = SelectRotatingRosterMember(district.ProsecutionLawyers, district.District, "prosecutor", caseNumber, caseWeight);
            return string.IsNullOrEmpty(lawyer) ? district.ProsecutionOffice : $"{district.ProsecutionOffice} — {lawyer}";
        }

        /// <summary>Selects defense attorney. If public defender: from first firm. Else: from private firms. Returns "Firm — Lawyer".</summary>
        private static string SelectDefenseAttorney(CourtDistrictProfile district, bool hasPublicDefender, string caseNumber, int caseWeight) {
            if (district?.DefenseFirms == null || district.DefenseFirms.Length == 0) return "Public Defender Office";
            LawFirmRoster firm;
            if (hasPublicDefender || district.DefenseFirms.Length <= 1) {
                firm = district.DefenseFirms[0];
            } else {
                int idx = 1 + (Math.Abs(GetStableHash($"{district.District}|defense|{caseNumber}|{caseWeight}")) % (district.DefenseFirms.Length - 1));
                firm = district.DefenseFirms[idx];
            }
            if (firm == null || string.IsNullOrEmpty(firm.Name)) return "Public Defender Office";
            if (firm.Lawyers == null || firm.Lawyers.Length == 0) return firm.Name;
            string lawyer = SelectRotatingRosterMember(firm.Lawyers, district.District, $"defense|{firm.Name}", caseNumber, caseWeight);
            return string.IsNullOrEmpty(lawyer) ? firm.Name : $"{firm.Name} — {lawyer}";
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
                if (charge == null) continue;
                charge.Fine = Math.Max(0, (int)Math.Round(charge.Fine * courtData.SentenceMultiplier));
                // Do NOT apply SentenceMultiplier to charge.Time; sentencing range roll happens at resolution
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
