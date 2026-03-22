using MDTPro.Data;
using MDTPro.Data.Reports;
using MDTPro.Utility;
using Newtonsoft.Json;
using Rage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MDTPro.Setup {
    internal class SetupController {
        private static string _mdtProPathRoot;

        /// <summary>Resolves MDTPro folder from plugin location (GTA/plugins/LSPDFR -> GTA/MDTPro) so it works regardless of process current directory.</summary>
        internal static string MDTProPath {
            get {
                if (_mdtProPathRoot != null) return _mdtProPathRoot;
                try {
                    string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (!string.IsNullOrEmpty(pluginDir)) {
                        string gameRoot = Path.GetFullPath(Path.Combine(pluginDir, "..", ".."));
                        string mdt = Path.Combine(gameRoot, "MDTPro");
                        if (Directory.Exists(mdt)) {
                            _mdtProPathRoot = mdt;
                            return _mdtProPathRoot;
                        }
                    }
                } catch { }
                _mdtProPathRoot = "MDTPro";
                return _mdtProPathRoot;
            }
        }

        internal static string DataPath => Path.Combine(MDTProPath, "data");
        internal static string ReportsDataPath => Path.Combine(DataPath, "reports");
        internal static string DefaultsPath => Path.Combine(MDTProPath, "defaults");
        internal static string ConfigPath => Path.Combine(MDTProPath, "config.json");
        internal static string LanguagePath => Path.Combine(MDTProPath, "language.json");
        internal static string CitationOptionsPath => Path.Combine(MDTProPath, "citationOptions.json");
        internal static string ArrestOptionsPath => Path.Combine(MDTProPath, "arrestOptions.json");
        internal static string CitationOptionsDefaultsPath => Path.Combine(DefaultsPath, "citationOptions.json");
        internal static string ArrestOptionsDefaultsPath => Path.Combine(DefaultsPath, "arrestOptions.json");
        internal static string SeizureOptionsDefaultsPath => Path.Combine(DefaultsPath, "seizureOptions.json");
        internal static string PedDataPath => Path.Combine(DataPath, "peds.json");
        internal static string VehicleDataPath => Path.Combine(DataPath, "vehicles.json");
        internal static string CourtDataPath => Path.Combine(DataPath, "court.json");
        internal static string ShiftHistoryDataPath => Path.Combine(DataPath, "shiftHistory.json");
        internal static string OfficerInformationDataPath => Path.Combine(DataPath, "officerInformation.json");
        internal static string LogFilePath => Path.Combine(MDTProPath, "MDTPro.log");
        internal static string ImgDefaultsDirPath => Path.Combine(MDTProPath, "imgDefaults");
        internal static string ImgDirPath => Path.Combine(MDTProPath, "img");
        internal static string IncidentReportsPath => Path.Combine(ReportsDataPath, "incidentReports.json");
        internal static string CitationReportsPath => Path.Combine(ReportsDataPath, "citationReports.json");
        internal static string ArrestReportsPath => Path.Combine(ReportsDataPath, "arrestReports.json");
        internal static string IpAddressesPath => Path.Combine(MDTProPath, "ipAddresses.txt");
        internal static string PluginsPath => Path.Combine(MDTProPath, "plugins");

        internal static void SetupDirectory() {
            if (!Directory.Exists(DataPath)) {
                Directory.CreateDirectory(DataPath);
            }

            if (!Directory.Exists(ReportsDataPath)) {
                Directory.CreateDirectory(ReportsDataPath);
            }

            if (!File.Exists(CitationOptionsPath)) {
                File.WriteAllBytes(CitationOptionsPath, File.ReadAllBytes(CitationOptionsDefaultsPath));
            }

            if (!File.Exists(ArrestOptionsPath)) {
                File.WriteAllBytes(ArrestOptionsPath, File.ReadAllBytes(ArrestOptionsDefaultsPath));
            }

            if (!Directory.Exists(PluginsPath)) {
                Directory.CreateDirectory(PluginsPath);
            }

            Database.Initialize();

            DataController.OfficerInformationData = Database.LoadOfficerInformation() ?? new OfficerInformationData();
            DataController.courtDatabase = Database.LoadCourtCases() ?? new List<CourtData>();
            DataController.shiftHistoryData = Database.LoadShifts() ?? new List<ShiftData>();
            DataController.incidentReports = Database.LoadIncidentReports() ?? new List<IncidentReport>();
            DataController.citationReports = Database.LoadCitationReports() ?? new List<CitationReport>();
            DataController.arrestReports = Database.LoadArrestReports() ?? new List<ArrestReport>();
            DataController.impoundReports = Database.LoadImpoundReports() ?? new List<ImpoundReport>();
            DataController.trafficIncidentReports = Database.LoadTrafficIncidentReports() ?? new List<TrafficIncidentReport>();
            DataController.injuryReports = Database.LoadInjuryReports() ?? new List<InjuryReport>();
            DataController.propertyEvidenceReports = Database.LoadPropertyEvidenceReceiptReports() ?? new List<PropertyEvidenceReceiptReport>();

            DataController.LoadPedDatabaseFromFile();
            DataController.LoadVehicleDatabaseFromFile();
            DataController.SetOfficerInformation();

            try {
                DataController.ActivePostalCodeSet = JsonConvert.SerializeObject(CommonDataFramework.Modules.Postals.PostalCodeController.ActivePostalCodeSet);
            } catch (Exception e) {
                Helper.Log($"Could not read PostalCodeController.ActivePostalCodeSet: {e.Message}", false, Helper.LogSeverity.Warning);
            }

            if (!File.Exists(ConfigPath)) {
                Helper.WriteToJsonFile(ConfigPath, new Config());
            }

            if (!File.Exists(LanguagePath)) {
                Helper.WriteToJsonFile(LanguagePath, new Language());
            }

            GameFiber.StartNew(() => {
                while (Server.RunServer) {
                    DataController.SetDatabases();
                    DataController.CheckAndResolvePendingCases();
                    DataController.TryCaptureVehicleSearches();
                    GameFiber.Wait(GetConfig().databaseUpdateInterval);
                }
            }, "data-update-interval");

            GameFiber.StartNew(() => {
                while (Server.RunServer) {
                    DataController.TryCapturePickupAndPlayerFirearms();
                    GameFiber.Wait(500);
                }
            }, "firearm-capture-interval");

            GameFiber.StartNew(() => {
                while (Server.RunServer) {
                    DataController.SetDynamicData();
                    GameFiber.Wait(GetConfig().webSocketUpdateInterval);
                }
            }, "dynamic-data-update-interval");

            string[] imgDefaultsDir = Directory.GetFiles(ImgDefaultsDirPath).Select(item => item.Split('\\')[item.Split('\\').Length - 1]).ToArray();
            if (!Directory.Exists(ImgDirPath)) Directory.CreateDirectory(ImgDirPath);
            foreach (string imgNameInDefaultDir in imgDefaultsDir) {
                if (File.Exists($"{ImgDirPath}/{imgNameInDefaultDir}")) continue;
                File.WriteAllBytes($"{ImgDirPath}/{imgNameInDefaultDir}", File.ReadAllBytes($"{ImgDefaultsDirPath}/{imgNameInDefaultDir}"));
            }

            Helper.ClearLog();
            Helper.Log($"Version: {Main.Version}");
            Helper.Log($"Log path: {Path.GetFullPath(LogFilePath)}");

            Config config = GetConfig();
            Helper.Log($"Config:\n{JsonConvert.SerializeObject(config, Formatting.Indented)}");
            if (config.firearmDebugLogging)
                Helper.Log("[Firearm] Debug logging ENABLED – firearm capture flow will be logged to this file.", false, Helper.LogSeverity.Info);

            string[] MDTProDirectoryFiles = Directory.GetFiles(MDTProPath).Select(item => $"[File] {Path.GetFileName(item)}").ToArray();
            string[] MDTProDirectoryDirs = Directory.GetDirectories(MDTProPath).Select(item => $"[Directory] {Path.GetFileName(item)}").ToArray();
            string[] MDTProDirectoryFilesAndDirs = MDTProDirectoryFiles.Concat(MDTProDirectoryDirs).ToArray();
            Helper.Log($"MDTPro Directory:\n  {string.Join("\n  ", MDTProDirectoryFilesAndDirs)}");
        }

        internal static void ClearCache() {
            cachedConfig = null;
            cachedLanguage = null;
            cachedCitationOptions = null;
            cachedArrestOptions = null;
        }

        private static Config cachedConfig;
        internal static Config GetConfig() {
            if (cachedConfig == null) {
                var def = new Config();
                cachedConfig = Helper.ReadFromJsonFile<Config>(ConfigPath) ?? def;
                EnsureALPRDefaults(cachedConfig, def);
                EnsureCitationArrestOptionsFromDefaults(cachedConfig, def);
                Helper.WriteToJsonFile(ConfigPath, cachedConfig);
            }
            return cachedConfig;
        }

        /// <summary>Ensures ALPR config values are sensible. Only enable, popup duration, and HUD position are in config; tuning is hardcoded.</summary>
        private static void EnsureALPRDefaults(Config cfg, Config def) {
            if (string.IsNullOrEmpty(cfg.alprHudAnchor)) cfg.alprHudAnchor = def.alprHudAnchor ?? "TopRight";
        }

        /// <summary>One-time migration: overwrite citation and arrest options from defaults so upgraders get updated charges (no VC/PC/HS in names, Stolen Possession, Evading, etc.). ALPR behavior is already hardcoded; this only updates the charge lists.</summary>
        private static void EnsureCitationArrestOptionsFromDefaults(Config cfg, Config def) {
            const int currentCitationArrestOptionsVersion = 2;
            if (cfg.citationArrestOptionsVersion >= currentCitationArrestOptionsVersion) return;
            try {
                if (File.Exists(CitationOptionsDefaultsPath)) {
                    File.WriteAllBytes(CitationOptionsPath, File.ReadAllBytes(CitationOptionsDefaultsPath));
                    cachedCitationOptions = null;
                }
                if (File.Exists(ArrestOptionsDefaultsPath)) {
                    File.WriteAllBytes(ArrestOptionsPath, File.ReadAllBytes(ArrestOptionsDefaultsPath));
                    cachedArrestOptions = null;
                }
                cfg.citationArrestOptionsVersion = currentCitationArrestOptionsVersion;
                Helper.Log("Citation and arrest options updated from defaults (version 2: plain charge names, Stolen Possession, Evading).", true, Helper.LogSeverity.Info);
            } catch (Exception ex) {
                Helper.Log($"Could not update citation/arrest options from defaults: {ex.Message}", true, Helper.LogSeverity.Warning);
            }
        }

        internal static void ResetConfig() {
            cachedConfig = null;
        }

        private static Language cachedLanguage;
        internal static Language GetLanguage() {
            if (cachedLanguage == null) {
                cachedLanguage = Helper.ReadFromJsonFile<Language>(LanguagePath) ?? new Language();
                EnsureIdTypeMapDefaults(cachedLanguage);
                Helper.WriteToJsonFile(LanguagePath, cachedLanguage);
            }
            return cachedLanguage;
        }

        /// <summary>Ensures IdTypeMap has prefixes for all report types (handles language.json from before impound/trafficIncident/injury were added).</summary>
        private static void EnsureIdTypeMapDefaults(Language lang) {
            if (lang?.reports?.idTypeMap == null) return;
            var map = lang.reports.idTypeMap;
            if (string.IsNullOrEmpty(map.impound)) map.impound = "IMP";
            if (string.IsNullOrEmpty(map.trafficIncident)) map.trafficIncident = "TIR";
            if (string.IsNullOrEmpty(map.injury)) map.injury = "INJ";
            if (string.IsNullOrEmpty(map.propertyEvidence)) map.propertyEvidence = "PER";
        }

        private static List<CitationGroup> cachedCitationOptions;
        internal static List<CitationGroup> GetCitationOptions() {
            cachedCitationOptions ??= Helper.ReadFromJsonFile<List<CitationGroup>>(CitationOptionsPath);
            return cachedCitationOptions;
        }

        private static List<ArrestGroup> cachedArrestOptions;
        internal static List<ArrestGroup> GetArrestOptions() {
            cachedArrestOptions ??= Helper.ReadFromJsonFile<List<ArrestGroup>>(ArrestOptionsPath);
            return cachedArrestOptions;
        }

        internal static SeizureOptions GetSeizureOptions() {
            return Helper.ReadFromJsonFile<SeizureOptions>(SeizureOptionsDefaultsPath) ?? new SeizureOptions();
        }

        internal static List<MDTProPedData> GetMDTProPedData() {
            return Database.LoadPeds() ?? new List<MDTProPedData>();
        }

        internal static List<MDTProVehicleData> GetMDTProVehicleData() {
            return Database.LoadVehicles() ?? new List<MDTProVehicleData>();
        }
    }
}
