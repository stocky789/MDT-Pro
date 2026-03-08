using MDTPro.Data;
using MDTPro.Data.Reports;
using MDTPro.Utility;
using Newtonsoft.Json;
using Rage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MDTPro.Setup {
    internal class SetupController {
        internal static readonly string MDTProPath = "MDTPro";
        internal static readonly string DataPath = $"{MDTProPath}/data";
        internal static readonly string ReportsDataPath = $"{DataPath}/reports";
        internal static readonly string DefaultsPath = $"{MDTProPath}/defaults";
        internal static readonly string ConfigPath = $"{MDTProPath}/config.json";
        internal static readonly string LanguagePath = $"{MDTProPath}/language.json";
        internal static readonly string CitationOptionsPath = $"{MDTProPath}/citationOptions.json";
        internal static readonly string ArrestOptionsPath = $"{MDTProPath}/arrestOptions.json";
        internal static readonly string CitationOptionsDefaultsPath = $"{DefaultsPath}/citationOptions.json";
        internal static readonly string ArrestOptionsDefaultsPath = $"{DefaultsPath}/arrestOptions.json";
        internal static readonly string PedDataPath = $"{DataPath}/peds.json";
        internal static readonly string VehicleDataPath = $"{DataPath}/vehicles.json";
        internal static readonly string CourtDataPath = $"{DataPath}/court.json";
        internal static readonly string ShiftHistoryDataPath = $"{DataPath}/shiftHistory.json";
        internal static readonly string OfficerInformationDataPath = $"{DataPath}/officerInformation.json";
        internal static readonly string LogFilePath = $"{MDTProPath}/MDTPro.log";
        internal static readonly string ImgDefaultsDirPath = $"{MDTProPath}/imgDefaults";
        internal static readonly string ImgDirPath = $"{MDTProPath}/img";
        internal static readonly string IncidentReportsPath = $"{ReportsDataPath}/incidentReports.json";
        internal static readonly string CitationReportsPath = $"{ReportsDataPath}/citationReports.json";
        internal static readonly string ArrestReportsPath = $"{ReportsDataPath}/arrestReports.json";
        internal static readonly string IpAddressesPath = $"{MDTProPath}/ipAddresses.txt";
        internal static readonly string PluginsPath = $"{MDTProPath}/plugins";

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
                    DataController.TryCapturePickupAndPlayerFirearms();
                    GameFiber.Wait(GetConfig().databaseUpdateInterval);
                }
            }, "data-update-interval");

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

            string[] MDTProDirectoryFiles = Directory.GetFiles(MDTProPath).Select(item => $"[File] {item.Split('\\')[1]}").ToArray();
            string[] MDTProDirectoryDirs = Directory.GetDirectories(MDTProPath).Select(item => $"[Directory] {item.Split('\\')[1]}").ToArray();
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
                cachedConfig = Helper.ReadFromJsonFile<Config>(ConfigPath) ?? new Config();
                Helper.WriteToJsonFile(ConfigPath, cachedConfig);
            }
            return cachedConfig;
        }

        internal static void ResetConfig() {
            cachedConfig = null;
        }

        private static Language cachedLanguage;
        internal static Language GetLanguage() {
            if (cachedLanguage == null) {
                cachedLanguage = Helper.ReadFromJsonFile<Language>(LanguagePath) ?? new Language();
                Helper.WriteToJsonFile(LanguagePath, cachedLanguage);
            }
            return cachedLanguage;
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

        internal static List<MDTProPedData> GetMDTProPedData() {
            return Database.LoadPeds() ?? new List<MDTProPedData>();
        }

        internal static List<MDTProVehicleData> GetMDTProVehicleData() {
            return Database.LoadVehicles() ?? new List<MDTProVehicleData>();
        }
    }
}
