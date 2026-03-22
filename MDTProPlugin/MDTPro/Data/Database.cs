using MDTPro.Data.Reports;
using MDTPro.Setup;
using MDTPro.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MDTPro.Data {
    internal static class Database {
        internal static readonly string DatabasePath = $"{SetupController.DataPath}/mdtpro.db";

        private static SQLiteConnection connection;
        private static readonly object dbLock = new object();

        private const int CurrentSchemaVersion = 29;

        /// <summary>Reads an INTEGER column from SQLite as uint. SQLite returns INTEGER as Int64; values outside uint range are clamped to 0.</summary>
        private static uint ReadUInt32FromReader(object value) {
            if (value == null || value is DBNull) return 0;
            try {
                long l = Convert.ToInt64(value);
                if (l < 0 || l > uint.MaxValue) return 0;
                return (uint)l;
            } catch {
                return 0;
            }
        }

        internal static void Initialize() {
            lock (dbLock) {
                try {
                    string connectionString = $"Data Source={DatabasePath};Version=3;Journal Mode=WAL;Foreign Keys=True;";
                    connection = new SQLiteConnection(connectionString);
                    connection.Open();

                    CreateSchema();

                    int version = GetSchemaVersion();
                    if (version == 0) {
                        SetSchemaVersion(CurrentSchemaVersion);

                        if (HasLegacyJsonFiles()) {
                            MigrateFromJson();
                        }
                    } else if (version < CurrentSchemaVersion) {
                        MigrateSchema(version);
                    }
                    EnsureAllMissingSchemaColumns();
                } catch (Exception e) {
                    Helper.Log($"Database initialization failed: {e.Message}", true, Helper.LogSeverity.Error);
                    connection?.Dispose();
                    connection = null;
                    throw;
                }
            }

            Helper.Log($"Database initialized at {Path.GetFullPath(DatabasePath)}");
        }

        internal static void Close() {
            lock (dbLock) {
                connection?.Close();
                connection?.Dispose();
                connection = null;
            }
        }

        #region Schema

        private static void CreateSchema() {
            string sql = @"
                CREATE TABLE IF NOT EXISTS schema_version (
                    version INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS peds (
                    Name                    TEXT PRIMARY KEY,
                    FirstName               TEXT,
                    LastName                TEXT,
                    ModelHash               INTEGER NOT NULL DEFAULT 0,
                    ModelName               TEXT,
                    Birthday                TEXT,
                    Gender                  TEXT,
                    Address                 TEXT,
                    IsInGang                INTEGER NOT NULL DEFAULT 0,
                    AdvisoryText            TEXT,
                    TimesStopped            INTEGER NOT NULL DEFAULT 0,
                    IsWanted                INTEGER NOT NULL DEFAULT 0,
                    WarrantText             TEXT,
                    IsOnProbation           INTEGER NOT NULL DEFAULT 0,
                    IsOnParole              INTEGER NOT NULL DEFAULT 0,
                    LicenseStatus           TEXT,
                    LicenseExpiration       TEXT,
                    WeaponPermitStatus      TEXT,
                    WeaponPermitExpiration  TEXT,
                    WeaponPermitType        TEXT,
                    FishingPermitStatus     TEXT,
                    FishingPermitExpiration TEXT,
                    HuntingPermitStatus     TEXT,
                    HuntingPermitExpiration TEXT,
                    IncarceratedUntil       TEXT,
                    IdentificationHistory   TEXT,
                    IsDeceased              INTEGER NOT NULL DEFAULT 0,
                    DeceasedAt              TEXT
                );

                CREATE TABLE IF NOT EXISTS damage_cache (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    VictimName          TEXT NOT NULL,
                    Damage              INTEGER NOT NULL DEFAULT 0,
                    ArmourDamage        INTEGER NOT NULL DEFAULT 0,
                    WeaponType          TEXT,
                    WeaponGroup         TEXT,
                    BodyRegion          TEXT,
                    VictimAlive         INTEGER NOT NULL DEFAULT 1,
                    AtUtc               TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_damage_cache_victim ON damage_cache(VictimName);
                CREATE INDEX IF NOT EXISTS idx_damage_cache_at ON damage_cache(AtUtc);

                CREATE TABLE IF NOT EXISTS ped_citations (
                    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    PedName          TEXT NOT NULL REFERENCES peds(Name) ON DELETE CASCADE,
                    name             TEXT,
                    minFine          INTEGER NOT NULL DEFAULT 0,
                    maxFine          INTEGER NOT NULL DEFAULT 0,
                    canRevokeLicense INTEGER NOT NULL DEFAULT 0,
                    isArrestable     INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_ped_citations_ped ON ped_citations(PedName);

                CREATE TABLE IF NOT EXISTS ped_arrests (
                    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    PedName          TEXT NOT NULL REFERENCES peds(Name) ON DELETE CASCADE,
                    name             TEXT,
                    minFine          INTEGER NOT NULL DEFAULT 0,
                    maxFine          INTEGER NOT NULL DEFAULT 0,
                    canRevokeLicense INTEGER NOT NULL DEFAULT 0,
                    isArrestable     INTEGER NOT NULL DEFAULT 1,
                    minDays          INTEGER NOT NULL DEFAULT 0,
                    maxDays          INTEGER,
                    probation        REAL NOT NULL DEFAULT 0,
                    canBeWarrant     INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_ped_arrests_ped ON ped_arrests(PedName);

                CREATE TABLE IF NOT EXISTS vehicles (
                    LicensePlate                TEXT PRIMARY KEY,
                    ModelName                   TEXT,
                    ModelDisplayName            TEXT,
                    IsStolen                    INTEGER NOT NULL DEFAULT 0,
                    Owner                       TEXT,
                    Color                       TEXT,
                    VehicleIdentificationNumber TEXT,
                    VinStatus                   TEXT,
                    Make                        TEXT,
                    Model                       TEXT,
                    PrimaryColor                TEXT,
                    SecondaryColor              TEXT,
                    RegistrationStatus          TEXT,
                    RegistrationExpiration      TEXT,
                    InsuranceStatus             TEXT,
                    InsuranceExpiration          TEXT,
                    BOLOs                       TEXT
                );

                CREATE TABLE IF NOT EXISTS court_cases (
                    Number                  TEXT PRIMARY KEY,
                    PedName                 TEXT,
                    ReportId                TEXT,
                    ShortYear               INTEGER NOT NULL,
                    Status                  INTEGER NOT NULL DEFAULT 0,
                    IsJuryTrial             INTEGER NOT NULL DEFAULT 0,
                    JurySize                INTEGER NOT NULL DEFAULT 0,
                    JuryVotesForConviction  INTEGER NOT NULL DEFAULT 0,
                    JuryVotesForAcquittal   INTEGER NOT NULL DEFAULT 0,
                    PriorCitationCount      INTEGER NOT NULL DEFAULT 0,
                    PriorArrestCount        INTEGER NOT NULL DEFAULT 0,
                    PriorConvictionCount    INTEGER NOT NULL DEFAULT 0,
                    SeverityScore           INTEGER NOT NULL DEFAULT 0,
                    EvidenceScore           INTEGER NOT NULL DEFAULT 0,
                    EvidenceHadWeapon       INTEGER NOT NULL DEFAULT 0,
                    EvidenceWasWanted       INTEGER NOT NULL DEFAULT 0,
                    EvidenceWasPatDown      INTEGER NOT NULL DEFAULT 0,
                    EvidenceWasDrunk        INTEGER NOT NULL DEFAULT 0,
                    EvidenceWasFleeing      INTEGER NOT NULL DEFAULT 0,
                    EvidenceAssaultedPed    INTEGER NOT NULL DEFAULT 0,
                    EvidenceDamagedVehicle  INTEGER NOT NULL DEFAULT 0,
                    EvidenceIllegalWeapon   INTEGER NOT NULL DEFAULT 0,
                    EvidenceViolatedSupervision INTEGER NOT NULL DEFAULT 0,
                    EvidenceResisted         INTEGER NOT NULL DEFAULT 0,
                    EvidenceHadDrugs         INTEGER NOT NULL DEFAULT 0,
                    ConvictionChance        INTEGER NOT NULL DEFAULT 0,
                    ResolveAtUtc            TEXT,
                    RepeatOffenderScore     INTEGER NOT NULL DEFAULT 0,
                    SentenceMultiplier      REAL NOT NULL DEFAULT 1.0,
                    ProsecutionStrength     REAL NOT NULL DEFAULT 0,
                    DefenseStrength         REAL NOT NULL DEFAULT 0,
                    DocketPressure          REAL NOT NULL DEFAULT 0,
                    PolicyAdjustment        REAL NOT NULL DEFAULT 0,
                    CourtDistrict           TEXT,
                    CourtName               TEXT,
                    CourtType               TEXT,
                    HasPublicDefender       INTEGER NOT NULL DEFAULT 1,
                    Plea                    TEXT,
                    JudgeName               TEXT,
                    ProsecutorName          TEXT,
                    DefenseAttorneyName     TEXT,
                    HearingDateUtc          TEXT,
                    CreatedAtUtc            TEXT,
                    LastUpdatedUtc          TEXT,
                    OutcomeNotes            TEXT,
                    OutcomeReasoning        TEXT,
                    SentenceReasoning       TEXT,
                    LicenseRevocations      TEXT,
                    EvidenceUseOfForce      INTEGER NOT NULL DEFAULT 0,
                    AttachedReportIds       TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_court_cases_ped ON court_cases(PedName);

                CREATE TABLE IF NOT EXISTS court_charges (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    CaseNumber   TEXT NOT NULL REFERENCES court_cases(Number) ON DELETE CASCADE,
                    Name         TEXT,
                    Fine         INTEGER NOT NULL DEFAULT 0,
                    Time         INTEGER,
                    IsArrestable INTEGER,
                    Outcome      INTEGER NOT NULL DEFAULT 0,
                    ConvictionChance INTEGER,
                    SentenceDaysServed INTEGER
                );
                CREATE INDEX IF NOT EXISTS idx_court_charges_case ON court_charges(CaseNumber);

                CREATE TABLE IF NOT EXISTS officer_information (
                    Id          INTEGER PRIMARY KEY CHECK (Id = 1),
                    firstName   TEXT,
                    lastName    TEXT,
                    rank        TEXT,
                    callSign    TEXT,
                    agency      TEXT,
                    badgeNumber INTEGER
                );

                CREATE TABLE IF NOT EXISTS shifts (
                    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    startTime TEXT,
                    endTime   TEXT
                );

                CREATE TABLE IF NOT EXISTS shift_reports (
                    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
                    ShiftId  INTEGER NOT NULL REFERENCES shifts(Id) ON DELETE CASCADE,
                    ReportId TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_shift_reports_shift ON shift_reports(ShiftId);

                CREATE TABLE IF NOT EXISTS incident_reports (
                    Id                 TEXT PRIMARY KEY,
                    ShortYear          INTEGER NOT NULL,
                    OfficerFirstName   TEXT,
                    OfficerLastName    TEXT,
                    OfficerRank        TEXT,
                    OfficerCallSign    TEXT,
                    OfficerAgency      TEXT,
                    OfficerBadgeNumber INTEGER,
                    LocationArea       TEXT,
                    LocationStreet     TEXT,
                    LocationCounty     TEXT,
                    LocationPostal     TEXT,
                    TimeStamp          TEXT NOT NULL,
                    Status             INTEGER NOT NULL DEFAULT 1,
                    Notes              TEXT
                );

                CREATE TABLE IF NOT EXISTS incident_report_offenders (
                    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReportId TEXT NOT NULL REFERENCES incident_reports(Id) ON DELETE CASCADE,
                    PedName  TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_incident_offenders_report ON incident_report_offenders(ReportId);

                CREATE TABLE IF NOT EXISTS incident_report_witnesses (
                    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReportId TEXT NOT NULL REFERENCES incident_reports(Id) ON DELETE CASCADE,
                    PedName  TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_incident_witnesses_report ON incident_report_witnesses(ReportId);

                CREATE TABLE IF NOT EXISTS citation_reports (
                    Id                          TEXT PRIMARY KEY,
                    ShortYear                   INTEGER NOT NULL,
                    OfficerFirstName            TEXT,
                    OfficerLastName             TEXT,
                    OfficerRank                 TEXT,
                    OfficerCallSign             TEXT,
                    OfficerAgency               TEXT,
                    OfficerBadgeNumber          INTEGER,
                    LocationArea                TEXT,
                    LocationStreet              TEXT,
                    LocationCounty              TEXT,
                    LocationPostal              TEXT,
                    TimeStamp                   TEXT NOT NULL,
                    Status                      INTEGER NOT NULL DEFAULT 1,
                    Notes                       TEXT,
                    OffenderPedName             TEXT,
                    OffenderVehicleLicensePlate TEXT,
                    CourtCaseNumber             TEXT,
                    FinalAmount                 INTEGER
                );

                CREATE TABLE IF NOT EXISTS citation_report_charges (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReportId            TEXT NOT NULL REFERENCES citation_reports(Id) ON DELETE CASCADE,
                    name                TEXT,
                    minFine             INTEGER NOT NULL DEFAULT 0,
                    maxFine             INTEGER NOT NULL DEFAULT 0,
                    canRevokeLicense    INTEGER NOT NULL DEFAULT 0,
                    isArrestable        INTEGER NOT NULL DEFAULT 0,
                    addedByReportInEdit INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_citation_charges_report ON citation_report_charges(ReportId);

                CREATE TABLE IF NOT EXISTS arrest_reports (
                    Id                          TEXT PRIMARY KEY,
                    ShortYear                   INTEGER NOT NULL,
                    OfficerFirstName            TEXT,
                    OfficerLastName             TEXT,
                    OfficerRank                 TEXT,
                    OfficerCallSign             TEXT,
                    OfficerAgency               TEXT,
                    OfficerBadgeNumber          INTEGER,
                    LocationArea                TEXT,
                    LocationStreet              TEXT,
                    LocationCounty              TEXT,
                    LocationPostal              TEXT,
                    TimeStamp                   TEXT NOT NULL,
                    Status                      INTEGER NOT NULL DEFAULT 1,
                    Notes                       TEXT,
                    OffenderPedName             TEXT,
                    OffenderVehicleLicensePlate TEXT,
                    CourtCaseNumber             TEXT,
                    UseOfForceType              TEXT,
                    UseOfForceTypeOther         TEXT,
                    UseOfForceJustification     TEXT,
                    UseOfForceInjurySuspect     INTEGER NOT NULL DEFAULT 0,
                    UseOfForceInjuryOfficer     INTEGER NOT NULL DEFAULT 0,
                    UseOfForceWitnesses         TEXT,
                    AttachedReportIds           TEXT,
                    DocumentedDrugs             INTEGER NOT NULL DEFAULT 0,
                    DocumentedFirearms          INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS arrest_report_charges (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReportId            TEXT NOT NULL REFERENCES arrest_reports(Id) ON DELETE CASCADE,
                    name                TEXT,
                    minFine             INTEGER NOT NULL DEFAULT 0,
                    maxFine             INTEGER NOT NULL DEFAULT 0,
                    canRevokeLicense    INTEGER NOT NULL DEFAULT 0,
                    isArrestable        INTEGER NOT NULL DEFAULT 1,
                    minDays             INTEGER NOT NULL DEFAULT 0,
                    maxDays             INTEGER,
                    probation           REAL NOT NULL DEFAULT 0,
                    canBeWarrant        INTEGER NOT NULL DEFAULT 0,
                    addedByReportInEdit INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_arrest_charges_report ON arrest_report_charges(ReportId);

                CREATE TABLE IF NOT EXISTS search_history (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    SearchType  TEXT NOT NULL,
                    SearchQuery TEXT NOT NULL,
                    ResultName  TEXT,
                    Timestamp   TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_search_history_type ON search_history(SearchType);
                CREATE INDEX IF NOT EXISTS idx_search_history_timestamp ON search_history(Timestamp);

                CREATE TABLE IF NOT EXISTS firearm_records (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    SerialNumber        TEXT,
                    IsSerialScratched   INTEGER NOT NULL DEFAULT 0,
                    OwnerPedName        TEXT NOT NULL,
                    WeaponModelId       TEXT,
                    WeaponDisplayName   TEXT,
                    WeaponModelHash     INTEGER NOT NULL DEFAULT 0,
                    IsStolen            INTEGER NOT NULL DEFAULT 0,
                    Description         TEXT,
                    Source              TEXT,
                    FirstSeenAt         TEXT NOT NULL,
                    LastSeenAt          TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_firearm_records_owner ON firearm_records(OwnerPedName);
                CREATE INDEX IF NOT EXISTS idx_firearm_records_serial ON firearm_records(SerialNumber);

                CREATE TABLE IF NOT EXISTS drug_records (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    OwnerPedName        TEXT NOT NULL,
                    DrugType            TEXT,
                    DrugCategory        TEXT,
                    Description         TEXT,
                    Source              TEXT,
                    FirstSeenAt         TEXT NOT NULL,
                    LastSeenAt          TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_drug_records_owner ON drug_records(OwnerPedName);
                CREATE INDEX IF NOT EXISTS idx_drug_records_drugtype ON drug_records(DrugType);

                CREATE TABLE IF NOT EXISTS traffic_incident_reports (
                    Id TEXT PRIMARY KEY, ShortYear INTEGER NOT NULL, OfficerFirstName TEXT, OfficerLastName TEXT,
                    OfficerRank TEXT, OfficerCallSign TEXT, OfficerAgency TEXT, OfficerBadgeNumber INTEGER,
                    LocationArea TEXT, LocationStreet TEXT, LocationCounty TEXT, LocationPostal TEXT,
                    TimeStamp TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Notes TEXT,
                    DriverNames TEXT, PassengerNames TEXT, PedestrianNames TEXT,
                    VehiclePlates TEXT, VehicleModels TEXT, InjuryReported INTEGER NOT NULL DEFAULT 0,
                    InjuryDetails TEXT, CollisionType TEXT
                );
                CREATE TABLE IF NOT EXISTS injury_reports (
                    Id TEXT PRIMARY KEY, ShortYear INTEGER NOT NULL, OfficerFirstName TEXT, OfficerLastName TEXT,
                    OfficerRank TEXT, OfficerCallSign TEXT, OfficerAgency TEXT, OfficerBadgeNumber INTEGER,
                    LocationArea TEXT, LocationStreet TEXT, LocationCounty TEXT, LocationPostal TEXT,
                    TimeStamp TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Notes TEXT,
                    InjuredPartyName TEXT, InjuryType TEXT, Severity TEXT, Treatment TEXT,
                    IncidentContext TEXT, LinkedReportId TEXT
                );
                CREATE TABLE IF NOT EXISTS impound_reports (
                    Id TEXT PRIMARY KEY, ShortYear INTEGER NOT NULL, OfficerFirstName TEXT, OfficerLastName TEXT,
                    OfficerRank TEXT, OfficerCallSign TEXT, OfficerAgency TEXT, OfficerBadgeNumber INTEGER,
                    LocationArea TEXT, LocationStreet TEXT, LocationCounty TEXT, LocationPostal TEXT,
                    TimeStamp TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Notes TEXT,
                    LicensePlate TEXT, VehicleModel TEXT, Owner TEXT, Vin TEXT, ImpoundReason TEXT,
                    TowCompany TEXT, ImpoundLot TEXT
                );
                CREATE TABLE IF NOT EXISTS property_evidence_reports (
                    Id TEXT PRIMARY KEY, ShortYear INTEGER NOT NULL, OfficerFirstName TEXT, OfficerLastName TEXT,
                    OfficerRank TEXT, OfficerCallSign TEXT, OfficerAgency TEXT, OfficerBadgeNumber INTEGER,
                    LocationArea TEXT, LocationStreet TEXT, LocationCounty TEXT, LocationPostal TEXT,
                    TimeStamp TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Notes TEXT,
                    SubjectPedName TEXT, SeizedDrugTypes TEXT, SeizedFirearmTypes TEXT, OtherContrabandNotes TEXT
                );

                CREATE TABLE IF NOT EXISTS vehicle_search_records (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    LicensePlate        TEXT NOT NULL,
                    ItemType            TEXT,
                    DrugType            TEXT,
                    ItemLocation        TEXT,
                    Description         TEXT,
                    WeaponModelHash     INTEGER NOT NULL DEFAULT 0,
                    WeaponModelId       TEXT,
                    Source              TEXT,
                    CapturedAt          TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_vehicle_search_records_plate ON vehicle_search_records(LicensePlate);
            ";

            using (var cmd = new SQLiteCommand(sql, connection)) {
                cmd.ExecuteNonQuery();
            }
        }

        private static bool GetBooleanFromReader(SQLiteDataReader reader, string columnName) {
            try {
                int ordinal = reader.GetOrdinal(columnName);
                return !reader.IsDBNull(ordinal) && Convert.ToBoolean(reader[ordinal]);
            } catch {
                return false;
            }
        }

        private static string ReaderOptionalString(SQLiteDataReader reader, string columnName) {
            try {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? null : (reader[ordinal] as string);
            } catch {
                return null;
            }
        }

        private static List<string> ParseLicenseRevocations(string json) {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try {
                var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(json);
                return list ?? new List<string>();
            } catch {
                return new List<string>();
            }
        }

        private static List<string> ParseAttachedReportIds(string json) {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try {
                var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(json);
                return list ?? new List<string>();
            } catch {
                return new List<string>();
            }
        }

        /// <summary>Parses JSON array to List. Returns null if empty or invalid (for optional breakdown fields).</summary>
        private static List<string> ParseListOrNull(string json) {
            var list = ParseAttachedReportIds(json);
            return list != null && list.Count > 0 ? list : null;
        }

        private static int GetSchemaVersion() {
            using (var cmd = new SQLiteCommand("SELECT version FROM schema_version LIMIT 1", connection)) {
                object result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 0;
            }
        }

        private static void SetSchemaVersion(int version) {
            using (var cmd = new SQLiteCommand("DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES (@v)", connection)) {
                cmd.Parameters.AddWithValue("@v", version);
                cmd.ExecuteNonQuery();
            }
        }

        private static void MigrateSchema(int fromVersion) {
            if (fromVersion < 2) {
                using (var cmd = new SQLiteCommand(@"
                    CREATE TABLE IF NOT EXISTS search_history (
                        Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        SearchType  TEXT NOT NULL,
                        SearchQuery TEXT NOT NULL,
                        ResultName  TEXT,
                        Timestamp   TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_search_history_type ON search_history(SearchType);
                    CREATE INDEX IF NOT EXISTS idx_search_history_timestamp ON search_history(Timestamp);
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 2 (search_history table)");
            }

            if (fromVersion < 3) {
                using (var cmd = new SQLiteCommand(@"
                    ALTER TABLE court_cases ADD COLUMN Status INTEGER NOT NULL DEFAULT 0;
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 3 (court_cases Status column)");
            }

            if (fromVersion < 4) {
                using (var cmd = new SQLiteCommand(@"
                    ALTER TABLE peds ADD COLUMN ModelHash INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE peds ADD COLUMN ModelName TEXT;
                    ALTER TABLE peds ADD COLUMN IncarceratedUntil TEXT;
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 4 (ped model + incarceration columns)");
            }

            if (fromVersion < 5) {
                using (var cmd = new SQLiteCommand(@"
                    ALTER TABLE court_cases ADD COLUMN IsJuryTrial INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN JurySize INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN JuryVotesForConviction INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN JuryVotesForAcquittal INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN PriorCitationCount INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN PriorArrestCount INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN PriorConvictionCount INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN RepeatOffenderScore INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN SentenceMultiplier REAL NOT NULL DEFAULT 1.0;
                    ALTER TABLE court_cases ADD COLUMN HasPublicDefender INTEGER NOT NULL DEFAULT 1;
                    ALTER TABLE court_cases ADD COLUMN Plea TEXT;
                    ALTER TABLE court_cases ADD COLUMN JudgeName TEXT;
                    ALTER TABLE court_cases ADD COLUMN ProsecutorName TEXT;
                    ALTER TABLE court_cases ADD COLUMN DefenseAttorneyName TEXT;
                    ALTER TABLE court_cases ADD COLUMN HearingDateUtc TEXT;
                    ALTER TABLE court_cases ADD COLUMN CreatedAtUtc TEXT;
                    ALTER TABLE court_cases ADD COLUMN LastUpdatedUtc TEXT;
                    ALTER TABLE court_cases ADD COLUMN OutcomeNotes TEXT;
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 5 (court realism metadata)");
            }

            if (fromVersion < 6) {
                using (var cmd = new SQLiteCommand(@"
                    ALTER TABLE court_cases ADD COLUMN SeverityScore INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN CourtDistrict TEXT;
                    ALTER TABLE court_cases ADD COLUMN CourtName TEXT;
                    ALTER TABLE court_cases ADD COLUMN CourtType TEXT;
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 6 (court district + severity metadata)");
            }

            if (fromVersion < 7) {
                using (var cmd = new SQLiteCommand(@"
                    ALTER TABLE court_cases ADD COLUMN EvidenceScore INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN ProsecutionStrength REAL NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN DefenseStrength REAL NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN DocketPressure REAL NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN PolicyAdjustment REAL NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN OutcomeReasoning TEXT;
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 7 (court intelligence metadata)");
            }

            if (fromVersion < 8) {
                using (var cmd = new SQLiteCommand(@"
                    ALTER TABLE court_cases ADD COLUMN EvidenceHadWeapon INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN EvidenceWasWanted INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN EvidenceWasPatDown INTEGER NOT NULL DEFAULT 0;
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 8 (real in-game evidence flags)");
            }

            if (fromVersion < 9) {
                using (var cmd = new SQLiteCommand(@"
                    ALTER TABLE court_cases ADD COLUMN EvidenceWasDrunk INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN EvidenceWasFleeing INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN EvidenceAssaultedPed INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN EvidenceDamagedVehicle INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN EvidenceIllegalWeapon INTEGER NOT NULL DEFAULT 0;
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 9 (extended evidence flags)");
            }

            if (fromVersion < 10) {
                using (var cmd = new SQLiteCommand(@"
                    ALTER TABLE court_cases ADD COLUMN EvidenceViolatedSupervision INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN ConvictionChance INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE court_cases ADD COLUMN ResolveAtUtc TEXT;
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 10 (auto-resolution and supervision violation)");
            }

            if (fromVersion < 11) {
                using (var cmd = new SQLiteCommand(@"
                    ALTER TABLE peds ADD COLUMN IdentificationHistory TEXT;
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 11 (identification history)");
            }

            if (fromVersion < 12) {
                using (var cmd = new SQLiteCommand(@"
                    ALTER TABLE court_cases ADD COLUMN EvidenceResisted INTEGER NOT NULL DEFAULT 0;
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 12 (evidence resisted from PR)");
            }

            if (fromVersion < 13) {
                using (var cmd = new SQLiteCommand(@"
                    ALTER TABLE citation_reports ADD COLUMN FinalAmount INTEGER;
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 13 (citation final amount)");
            }

            if (fromVersion < 14) {
                using (var cmd = new SQLiteCommand(@"
                    CREATE TABLE IF NOT EXISTS firearm_records (
                        Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                        SerialNumber        TEXT,
                        OwnerPedName        TEXT NOT NULL,
                        WeaponModelId       TEXT,
                        WeaponDisplayName   TEXT,
                        WeaponModelHash     INTEGER NOT NULL DEFAULT 0,
                        IsStolen            INTEGER NOT NULL DEFAULT 0,
                        Description         TEXT,
                        Source              TEXT,
                        FirstSeenAt         TEXT NOT NULL,
                        LastSeenAt          TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_firearm_records_owner ON firearm_records(OwnerPedName);
                    CREATE INDEX IF NOT EXISTS idx_firearm_records_serial ON firearm_records(SerialNumber);
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                Helper.Log("Database migrated to schema version 14 (firearm records)");
            }

            if (fromVersion < 15) {
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE firearm_records ADD COLUMN WeaponDisplayName TEXT", connection)) {
                        cmd.ExecuteNonQuery();
                    }
                    Helper.Log("Database migrated to schema version 15 (firearm WeaponDisplayName)");
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) {
                    // Column already exists
                }
            }

            if (fromVersion < 16) {
                using (var cmd = new SQLiteCommand(@"
                    CREATE TABLE IF NOT EXISTS drug_records (
                        Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                        OwnerPedName        TEXT NOT NULL,
                        DrugType            TEXT,
                        DrugCategory        TEXT,
                        Description         TEXT,
                        Source              TEXT,
                        FirstSeenAt         TEXT NOT NULL,
                        LastSeenAt          TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_drug_records_owner ON drug_records(OwnerPedName);
                    CREATE INDEX IF NOT EXISTS idx_drug_records_drugtype ON drug_records(DrugType);

                    CREATE TABLE IF NOT EXISTS vehicle_search_records (
                        Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                        LicensePlate        TEXT NOT NULL,
                        ItemType            TEXT,
                        DrugType            TEXT,
                        ItemLocation        TEXT,
                        Description         TEXT,
                        WeaponModelHash     INTEGER NOT NULL DEFAULT 0,
                        WeaponModelId       TEXT,
                        Source              TEXT,
                        CapturedAt          TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_vehicle_search_records_plate ON vehicle_search_records(LicensePlate);
                ", connection)) {
                    cmd.ExecuteNonQuery();
                }
                foreach (string col in new[] { "VinStatus", "Make", "Model", "PrimaryColor", "SecondaryColor" }) {
                    try {
                        using (var cmd = new SQLiteCommand($"ALTER TABLE vehicles ADD COLUMN {col} TEXT", connection)) {
                            cmd.ExecuteNonQuery();
                        }
                    } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
                }
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN EvidenceHadDrugs INTEGER NOT NULL DEFAULT 0", connection)) {
                        cmd.ExecuteNonQuery();
                    }
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
                Helper.Log("Database migrated to schema version 16 (drug records, vehicle search, vehicle schema, EvidenceHadDrugs)");
            }

            if (fromVersion < 17) {
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE firearm_records ADD COLUMN IsSerialScratched INTEGER NOT NULL DEFAULT 0", connection)) {
                        cmd.ExecuteNonQuery();
                    }
                    Helper.Log("Database migrated to schema version 17 (firearm IsSerialScratched)");
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }

            if (fromVersion < 18) {
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN LicenseRevocations TEXT", connection)) {
                        cmd.ExecuteNonQuery();
                    }
                    Helper.Log("Database migrated to schema version 18 (court LicenseRevocations)");
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }

            if (fromVersion < 19) {
                foreach (string col in new[] { "UseOfForceType", "UseOfForceTypeOther", "UseOfForceJustification", "UseOfForceWitnesses" }) {
                    try {
                        using (var cmd = new SQLiteCommand($"ALTER TABLE arrest_reports ADD COLUMN {col} TEXT", connection)) {
                            cmd.ExecuteNonQuery();
                        }
                    } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
                }
                foreach (string col in new[] { "UseOfForceInjurySuspect", "UseOfForceInjuryOfficer" }) {
                    try {
                        using (var cmd = new SQLiteCommand($"ALTER TABLE arrest_reports ADD COLUMN {col} INTEGER NOT NULL DEFAULT 0", connection)) {
                            cmd.ExecuteNonQuery();
                        }
                    } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
                }
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN EvidenceUseOfForce INTEGER NOT NULL DEFAULT 0", connection)) {
                        cmd.ExecuteNonQuery();
                    }
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
                try {
                    using (var cmd = new SQLiteCommand(@"
                        CREATE TABLE IF NOT EXISTS impound_reports (
                            Id TEXT PRIMARY KEY, ShortYear INTEGER NOT NULL, OfficerFirstName TEXT, OfficerLastName TEXT,
                            OfficerRank TEXT, OfficerCallSign TEXT, OfficerAgency TEXT, OfficerBadgeNumber INTEGER,
                            LocationArea TEXT, LocationStreet TEXT, LocationCounty TEXT, LocationPostal TEXT,
                            TimeStamp TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Notes TEXT,
                            LicensePlate TEXT, VehicleModel TEXT, Owner TEXT, Vin TEXT, ImpoundReason TEXT,
                            TowCompany TEXT, ImpoundLot TEXT
                        )", connection)) {
                        cmd.ExecuteNonQuery();
                    }
                } catch { }
                Helper.Log("Database migrated to schema version 19 (Use of Force, EvidenceUseOfForce, impound_reports)");
            }

            if (fromVersion < 20) {
                try {
                    using (var cmd = new SQLiteCommand(@"
                        CREATE TABLE IF NOT EXISTS traffic_incident_reports (
                            Id TEXT PRIMARY KEY, ShortYear INTEGER NOT NULL, OfficerFirstName TEXT, OfficerLastName TEXT,
                            OfficerRank TEXT, OfficerCallSign TEXT, OfficerAgency TEXT, OfficerBadgeNumber INTEGER,
                            LocationArea TEXT, LocationStreet TEXT, LocationCounty TEXT, LocationPostal TEXT,
                            TimeStamp TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Notes TEXT,
                            DriverNames TEXT, PassengerNames TEXT, PedestrianNames TEXT,
                            VehiclePlates TEXT, VehicleModels TEXT, InjuryReported INTEGER NOT NULL DEFAULT 0,
                            InjuryDetails TEXT, CollisionType TEXT
                        );
                        CREATE TABLE IF NOT EXISTS injury_reports (
                            Id TEXT PRIMARY KEY, ShortYear INTEGER NOT NULL, OfficerFirstName TEXT, OfficerLastName TEXT,
                            OfficerRank TEXT, OfficerCallSign TEXT, OfficerAgency TEXT, OfficerBadgeNumber INTEGER,
                            LocationArea TEXT, LocationStreet TEXT, LocationCounty TEXT, LocationPostal TEXT,
                            TimeStamp TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Notes TEXT,
                            InjuredPartyName TEXT, InjuryType TEXT, Severity TEXT, Treatment TEXT,
                            IncidentContext TEXT, LinkedReportId TEXT
                        )", connection)) {
                        cmd.ExecuteNonQuery();
                    }
                    Helper.Log("Database migrated to schema version 20 (traffic_incident_reports, injury_reports)");
                } catch { }
            }
            if (fromVersion < 21) {
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE injury_reports ADD COLUMN GameInjurySnapshot TEXT", connection)) {
                        cmd.ExecuteNonQuery();
                    }
                    Helper.Log("Database migrated to schema version 21 (injury_reports GameInjurySnapshot)");
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }
            if (fromVersion < 22) {
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN SentenceReasoning TEXT", connection)) {
                        cmd.ExecuteNonQuery();
                    }
                    Helper.Log("Database migrated to schema version 22 (court_cases SentenceReasoning)");
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }
            if (fromVersion < 23) {
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE peds ADD COLUMN IsDeceased INTEGER NOT NULL DEFAULT 0", connection)) {
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = new SQLiteCommand("ALTER TABLE peds ADD COLUMN DeceasedAt TEXT", connection)) {
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = new SQLiteCommand(@"
                        CREATE TABLE IF NOT EXISTS damage_cache (
                            Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                            VictimName          TEXT NOT NULL,
                            Damage              INTEGER NOT NULL DEFAULT 0,
                            ArmourDamage        INTEGER NOT NULL DEFAULT 0,
                            WeaponType          TEXT,
                            WeaponGroup         TEXT,
                            BodyRegion          TEXT,
                            VictimAlive         INTEGER NOT NULL DEFAULT 1,
                            AtUtc               TEXT NOT NULL
                        );
                        CREATE INDEX IF NOT EXISTS idx_damage_cache_victim ON damage_cache(VictimName);
                        CREATE INDEX IF NOT EXISTS idx_damage_cache_at ON damage_cache(AtUtc);
                    ", connection)) {
                        cmd.ExecuteNonQuery();
                    }
                    Helper.Log("Database migrated to schema version 23 (peds IsDeceased/DeceasedAt, damage_cache)");
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true || ex.Message?.Contains("already exists") == true) { }
            }
            if (fromVersion < 24) {
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE court_charges ADD COLUMN Outcome INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); }
                    using (var cmd = new SQLiteCommand("ALTER TABLE court_charges ADD COLUMN ConvictionChance INTEGER", connection)) { cmd.ExecuteNonQuery(); }
                    using (var cmd = new SQLiteCommand("ALTER TABLE court_charges ADD COLUMN SentenceDaysServed INTEGER", connection)) { cmd.ExecuteNonQuery(); }
                    using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN AttachedReportIds TEXT", connection)) { cmd.ExecuteNonQuery(); }
                    using (var cmd = new SQLiteCommand("ALTER TABLE arrest_reports ADD COLUMN AttachedReportIds TEXT", connection)) { cmd.ExecuteNonQuery(); }
                    Helper.Log("Database migrated to schema version 24 (per-charge Outcome, court/arrest AttachedReportIds)");
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }
            if (fromVersion < 25) {
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE arrest_reports ADD COLUMN DocumentedDrugs INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); }
                    using (var cmd = new SQLiteCommand("ALTER TABLE arrest_reports ADD COLUMN DocumentedFirearms INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); }
                    Helper.Log("Database migrated to schema version 25 (arrest Evidence seized: DocumentedDrugs, DocumentedFirearms)");
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }
            if (fromVersion < 26) {
                try {
                    using (var cmd = new SQLiteCommand(@"
                        CREATE TABLE IF NOT EXISTS property_evidence_reports (
                            Id TEXT PRIMARY KEY, ShortYear INTEGER NOT NULL, OfficerFirstName TEXT, OfficerLastName TEXT,
                            OfficerRank TEXT, OfficerCallSign TEXT, OfficerAgency TEXT, OfficerBadgeNumber INTEGER,
                            LocationArea TEXT, LocationStreet TEXT, LocationCounty TEXT, LocationPostal TEXT,
                            TimeStamp TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Notes TEXT,
                            SubjectPedName TEXT, SeizedDrugTypes TEXT, SeizedFirearmTypes TEXT, OtherContrabandNotes TEXT
                        )", connection)) { cmd.ExecuteNonQuery(); }
                    Helper.Log("Database migrated to schema version 26 (property_evidence_reports)");
                } catch (Exception ex) when (ex.Message?.Contains("already exists") == true) { }
            }
            if (fromVersion < 27) {
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN EvidenceDrugTypesBreakdown TEXT", connection)) { cmd.ExecuteNonQuery(); }
                    using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN EvidenceFirearmTypesBreakdown TEXT", connection)) { cmd.ExecuteNonQuery(); }
                    Helper.Log("Database migrated to schema version 27 (EvidenceDrugTypesBreakdown, EvidenceFirearmTypesBreakdown)");
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }
            if (fromVersion < 28) {
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE property_evidence_reports ADD COLUMN SubjectPedNames TEXT", connection)) { cmd.ExecuteNonQuery(); }
                    using (var cmd = new SQLiteCommand("ALTER TABLE property_evidence_reports ADD COLUMN SeizedDrugs TEXT", connection)) { cmd.ExecuteNonQuery(); }
                    Helper.Log("Database migrated to schema version 28 (SubjectPedNames, SeizedDrugs with quantity)");
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }
            if (fromVersion < 29) {
                try {
                    using (var cmd = new SQLiteCommand("ALTER TABLE impound_reports ADD COLUMN PersonAtFaultName TEXT", connection)) { cmd.ExecuteNonQuery(); }
                    Helper.Log("Database migrated to schema version 29 (impound_reports PersonAtFaultName)");
                } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }

            SetSchemaVersion(CurrentSchemaVersion);
        }

        /// <summary>Runs every migration's ALTER/CREATE so DBs that had version=25 but old CreateSchema get all columns/tables. Safe to run every startup (duplicate column/table is ignored).</summary>
        private static void EnsureAllMissingSchemaColumns() {
            if (connection == null) return;
            try { using (var cmd = new SQLiteCommand("CREATE TABLE IF NOT EXISTS search_history (Id INTEGER PRIMARY KEY AUTOINCREMENT, SearchType TEXT NOT NULL, SearchQuery TEXT NOT NULL, ResultName TEXT, Timestamp TEXT NOT NULL); CREATE INDEX IF NOT EXISTS idx_search_history_type ON search_history(SearchType); CREATE INDEX IF NOT EXISTS idx_search_history_timestamp ON search_history(Timestamp);", connection)) { cmd.ExecuteNonQuery(); } } catch { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN Status INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE peds ADD COLUMN ModelHash INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE peds ADD COLUMN ModelName TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE peds ADD COLUMN IncarceratedUntil TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN IsJuryTrial INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN JurySize INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN JuryVotesForConviction INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN JuryVotesForAcquittal INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN PriorCitationCount INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN PriorArrestCount INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN PriorConvictionCount INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN RepeatOffenderScore INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN SentenceMultiplier REAL NOT NULL DEFAULT 1.0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN HasPublicDefender INTEGER NOT NULL DEFAULT 1", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN Plea TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN JudgeName TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN ProsecutorName TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN DefenseAttorneyName TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN HearingDateUtc TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN CreatedAtUtc TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN LastUpdatedUtc TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN OutcomeNotes TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN SeverityScore INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN CourtDistrict TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN CourtName TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN CourtType TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            foreach (string col in new[] { "EvidenceScore", "ProsecutionStrength", "DefenseStrength", "DocketPressure", "PolicyAdjustment" }) {
                try { using (var cmd = new SQLiteCommand($"ALTER TABLE court_cases ADD COLUMN {col} {(col == "EvidenceScore" ? "INTEGER NOT NULL DEFAULT 0" : "REAL NOT NULL DEFAULT 0")}", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN OutcomeReasoning TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            foreach (string col in new[] { "EvidenceHadWeapon", "EvidenceWasWanted", "EvidenceWasPatDown", "EvidenceWasDrunk", "EvidenceWasFleeing", "EvidenceAssaultedPed", "EvidenceDamagedVehicle", "EvidenceIllegalWeapon", "EvidenceViolatedSupervision", "EvidenceResisted", "EvidenceHadDrugs", "EvidenceUseOfForce" }) {
                try { using (var cmd = new SQLiteCommand($"ALTER TABLE court_cases ADD COLUMN {col} INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN ConvictionChance INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN ResolveAtUtc TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN LicenseRevocations TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN SentenceReasoning TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN AttachedReportIds TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE peds ADD COLUMN IdentificationHistory TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE citation_reports ADD COLUMN FinalAmount INTEGER", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("CREATE TABLE IF NOT EXISTS firearm_records (Id INTEGER PRIMARY KEY AUTOINCREMENT, SerialNumber TEXT, OwnerPedName TEXT NOT NULL, WeaponModelId TEXT, WeaponModelHash INTEGER NOT NULL DEFAULT 0, IsStolen INTEGER NOT NULL DEFAULT 0, Description TEXT, Source TEXT, FirstSeenAt TEXT NOT NULL, LastSeenAt TEXT NOT NULL); CREATE INDEX IF NOT EXISTS idx_firearm_records_owner ON firearm_records(OwnerPedName); CREATE INDEX IF NOT EXISTS idx_firearm_records_serial ON firearm_records(SerialNumber);", connection)) { cmd.ExecuteNonQuery(); } } catch { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE firearm_records ADD COLUMN WeaponDisplayName TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE firearm_records ADD COLUMN IsSerialScratched INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("CREATE TABLE IF NOT EXISTS drug_records (Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerPedName TEXT NOT NULL, DrugType TEXT, DrugCategory TEXT, Description TEXT, Source TEXT, FirstSeenAt TEXT NOT NULL, LastSeenAt TEXT NOT NULL); CREATE INDEX IF NOT EXISTS idx_drug_records_owner ON drug_records(OwnerPedName); CREATE INDEX IF NOT EXISTS idx_drug_records_drugtype ON drug_records(DrugType);", connection)) { cmd.ExecuteNonQuery(); } } catch { }
            try { using (var cmd = new SQLiteCommand("CREATE TABLE IF NOT EXISTS vehicle_search_records (Id INTEGER PRIMARY KEY AUTOINCREMENT, LicensePlate TEXT NOT NULL, ItemType TEXT, DrugType TEXT, ItemLocation TEXT, Description TEXT, WeaponModelHash INTEGER NOT NULL DEFAULT 0, WeaponModelId TEXT, Source TEXT, CapturedAt TEXT NOT NULL); CREATE INDEX IF NOT EXISTS idx_vehicle_search_records_plate ON vehicle_search_records(LicensePlate);", connection)) { cmd.ExecuteNonQuery(); } } catch { }
            foreach (string col in new[] { "VinStatus", "Make", "Model", "PrimaryColor", "SecondaryColor" }) {
                try { using (var cmd = new SQLiteCommand($"ALTER TABLE vehicles ADD COLUMN {col} TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }
            foreach (string col in new[] { "UseOfForceType", "UseOfForceTypeOther", "UseOfForceJustification", "UseOfForceWitnesses" }) {
                try { using (var cmd = new SQLiteCommand($"ALTER TABLE arrest_reports ADD COLUMN {col} TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }
            foreach (string col in new[] { "UseOfForceInjurySuspect", "UseOfForceInjuryOfficer" }) {
                try { using (var cmd = new SQLiteCommand($"ALTER TABLE arrest_reports ADD COLUMN {col} INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            }
            try { using (var cmd = new SQLiteCommand("CREATE TABLE IF NOT EXISTS impound_reports (Id TEXT PRIMARY KEY, ShortYear INTEGER NOT NULL, OfficerFirstName TEXT, OfficerLastName TEXT, OfficerRank TEXT, OfficerCallSign TEXT, OfficerAgency TEXT, OfficerBadgeNumber INTEGER, LocationArea TEXT, LocationStreet TEXT, LocationCounty TEXT, LocationPostal TEXT, TimeStamp TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Notes TEXT, LicensePlate TEXT, VehicleModel TEXT, Owner TEXT, Vin TEXT, ImpoundReason TEXT, TowCompany TEXT, ImpoundLot TEXT)", connection)) { cmd.ExecuteNonQuery(); } } catch { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE impound_reports ADD COLUMN PersonAtFaultName TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("CREATE TABLE IF NOT EXISTS traffic_incident_reports (Id TEXT PRIMARY KEY, ShortYear INTEGER NOT NULL, OfficerFirstName TEXT, OfficerLastName TEXT, OfficerRank TEXT, OfficerCallSign TEXT, OfficerAgency TEXT, OfficerBadgeNumber INTEGER, LocationArea TEXT, LocationStreet TEXT, LocationCounty TEXT, LocationPostal TEXT, TimeStamp TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Notes TEXT, DriverNames TEXT, PassengerNames TEXT, PedestrianNames TEXT, VehiclePlates TEXT, VehicleModels TEXT, InjuryReported INTEGER NOT NULL DEFAULT 0, InjuryDetails TEXT, CollisionType TEXT)", connection)) { cmd.ExecuteNonQuery(); } } catch { }
            try { using (var cmd = new SQLiteCommand("CREATE TABLE IF NOT EXISTS injury_reports (Id TEXT PRIMARY KEY, ShortYear INTEGER NOT NULL, OfficerFirstName TEXT, OfficerLastName TEXT, OfficerRank TEXT, OfficerCallSign TEXT, OfficerAgency TEXT, OfficerBadgeNumber INTEGER, LocationArea TEXT, LocationStreet TEXT, LocationCounty TEXT, LocationPostal TEXT, TimeStamp TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Notes TEXT, InjuredPartyName TEXT, InjuryType TEXT, Severity TEXT, Treatment TEXT, IncidentContext TEXT, LinkedReportId TEXT)", connection)) { cmd.ExecuteNonQuery(); } } catch { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE injury_reports ADD COLUMN GameInjurySnapshot TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE peds ADD COLUMN IsDeceased INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE peds ADD COLUMN DeceasedAt TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("CREATE TABLE IF NOT EXISTS damage_cache (Id INTEGER PRIMARY KEY AUTOINCREMENT, VictimName TEXT NOT NULL, Damage INTEGER NOT NULL DEFAULT 0, ArmourDamage INTEGER NOT NULL DEFAULT 0, WeaponType TEXT, WeaponGroup TEXT, BodyRegion TEXT, VictimAlive INTEGER NOT NULL DEFAULT 1, AtUtc TEXT NOT NULL); CREATE INDEX IF NOT EXISTS idx_damage_cache_victim ON damage_cache(VictimName); CREATE INDEX IF NOT EXISTS idx_damage_cache_at ON damage_cache(AtUtc);", connection)) { cmd.ExecuteNonQuery(); } } catch { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_charges ADD COLUMN Outcome INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_charges ADD COLUMN ConvictionChance INTEGER", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_charges ADD COLUMN SentenceDaysServed INTEGER", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE arrest_reports ADD COLUMN AttachedReportIds TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE arrest_reports ADD COLUMN DocumentedDrugs INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE arrest_reports ADD COLUMN DocumentedFirearms INTEGER NOT NULL DEFAULT 0", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("CREATE TABLE IF NOT EXISTS property_evidence_reports (Id TEXT PRIMARY KEY, ShortYear INTEGER NOT NULL, OfficerFirstName TEXT, OfficerLastName TEXT, OfficerRank TEXT, OfficerCallSign TEXT, OfficerAgency TEXT, OfficerBadgeNumber INTEGER, LocationArea TEXT, LocationStreet TEXT, LocationCounty TEXT, LocationPostal TEXT, TimeStamp TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Notes TEXT, SubjectPedName TEXT, SeizedDrugTypes TEXT, SeizedFirearmTypes TEXT, OtherContrabandNotes TEXT)", connection)) { cmd.ExecuteNonQuery(); } } catch { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN EvidenceDrugTypesBreakdown TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE court_cases ADD COLUMN EvidenceFirearmTypesBreakdown TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE property_evidence_reports ADD COLUMN SubjectPedNames TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
            try { using (var cmd = new SQLiteCommand("ALTER TABLE property_evidence_reports ADD COLUMN SeizedDrugs TEXT", connection)) { cmd.ExecuteNonQuery(); } } catch (Exception ex) when (ex.Message?.Contains("duplicate column") == true) { }
        }

        #endregion

        #region Load Methods

        internal static List<MDTProPedData> LoadPeds() {
            lock (dbLock) {
                if (connection == null) return null;

                var peds = new List<MDTProPedData>();

                using (var cmd = new SQLiteCommand("SELECT * FROM peds", connection)) {
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            peds.Add(ReadPedFromRow(reader));
                        }
                    }
                }

                foreach (var ped in peds) {
                    ped.Citations = LoadPedCitations(ped.Name);
                    ped.Arrests = LoadPedArrests(ped.Name);
                }

                return peds;
            }
        }

        private static MDTProPedData ReadPedFromRow(SQLiteDataReader reader) {
            return new MDTProPedData {
                Name = reader["Name"] as string,
                FirstName = reader["FirstName"] as string,
                LastName = reader["LastName"] as string,
                ModelHash = reader["ModelHash"] is DBNull ? 0 : (uint)Convert.ToInt64(reader["ModelHash"]),
                ModelName = reader["ModelName"] as string,
                Birthday = reader["Birthday"] as string,
                Gender = reader["Gender"] as string,
                Address = reader["Address"] as string,
                IsInGang = Convert.ToBoolean(reader["IsInGang"]),
                AdvisoryText = reader["AdvisoryText"] as string,
                TimesStopped = Convert.ToInt32(reader["TimesStopped"]),
                IsWanted = Convert.ToBoolean(reader["IsWanted"]),
                WarrantText = reader["WarrantText"] as string,
                IsOnProbation = Convert.ToBoolean(reader["IsOnProbation"]),
                IsOnParole = Convert.ToBoolean(reader["IsOnParole"]),
                LicenseStatus = reader["LicenseStatus"] as string,
                LicenseExpiration = reader["LicenseExpiration"] as string,
                WeaponPermitStatus = reader["WeaponPermitStatus"] as string,
                WeaponPermitExpiration = reader["WeaponPermitExpiration"] as string,
                WeaponPermitType = reader["WeaponPermitType"] as string,
                FishingPermitStatus = reader["FishingPermitStatus"] as string,
                FishingPermitExpiration = reader["FishingPermitExpiration"] as string,
                HuntingPermitStatus = reader["HuntingPermitStatus"] as string,
                HuntingPermitExpiration = reader["HuntingPermitExpiration"] as string,
                IncarceratedUntil = reader["IncarceratedUntil"] as string,
                IdentificationHistory = reader["IdentificationHistory"] is string idJson && !string.IsNullOrEmpty(idJson)
                    ? JsonConvert.DeserializeObject<List<MDTProPedData.IdentificationEntry>>(idJson)
                    : new List<MDTProPedData.IdentificationEntry>(),
                IsDeceased = reader["IsDeceased"] is DBNull || reader["IsDeceased"] == null ? false : Convert.ToBoolean(reader["IsDeceased"]),
                DeceasedAt = reader["DeceasedAt"] as string
            };
        }

        private static List<CitationGroup.Charge> LoadPedCitations(string pedName) {
            var charges = new List<CitationGroup.Charge>();

            using (var cmd = new SQLiteCommand("SELECT * FROM ped_citations WHERE PedName = @name", connection)) {
                cmd.Parameters.AddWithValue("@name", pedName);

                using (var reader = cmd.ExecuteReader()) {
                    while (reader.Read()) {
                        charges.Add(new CitationGroup.Charge {
                            name = reader["name"] as string,
                            minFine = Convert.ToInt32(reader["minFine"]),
                            maxFine = Convert.ToInt32(reader["maxFine"]),
                            canRevokeLicense = Convert.ToBoolean(reader["canRevokeLicense"]),
                            isArrestable = Convert.ToBoolean(reader["isArrestable"])
                        });
                    }
                }
            }

            return charges;
        }

        private static List<ArrestGroup.Charge> LoadPedArrests(string pedName) {
            var charges = new List<ArrestGroup.Charge>();

            using (var cmd = new SQLiteCommand("SELECT * FROM ped_arrests WHERE PedName = @name", connection)) {
                cmd.Parameters.AddWithValue("@name", pedName);

                using (var reader = cmd.ExecuteReader()) {
                    while (reader.Read()) {
                        charges.Add(new ArrestGroup.Charge {
                            name = reader["name"] as string,
                            minFine = Convert.ToInt32(reader["minFine"]),
                            maxFine = Convert.ToInt32(reader["maxFine"]),
                            canRevokeLicense = Convert.ToBoolean(reader["canRevokeLicense"]),
                            isArrestable = Convert.ToBoolean(reader["isArrestable"]),
                            minDays = Convert.ToInt32(reader["minDays"]),
                            maxDays = reader["maxDays"] is DBNull ? (int?)null : Convert.ToInt32(reader["maxDays"]),
                            probation = Convert.ToSingle(reader["probation"]),
                            canBeWarrant = Convert.ToBoolean(reader["canBeWarrant"])
                        });
                    }
                }
            }

            return charges;
        }

        internal static List<MDTProVehicleData> LoadVehicles() {
            lock (dbLock) {
                if (connection == null) return null;

                var vehicles = new List<MDTProVehicleData>();

                using (var cmd = new SQLiteCommand("SELECT * FROM vehicles", connection)) {
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            var vehicle = new MDTProVehicleData {
                                LicensePlate = reader["LicensePlate"] as string,
                                ModelName = reader["ModelName"] as string,
                                ModelDisplayName = reader["ModelDisplayName"] as string,
                                IsStolen = Convert.ToBoolean(reader["IsStolen"]),
                                Owner = reader["Owner"] as string,
                                Color = reader["Color"] as string,
                                VehicleIdentificationNumber = reader["VehicleIdentificationNumber"] as string,
                                VinStatus = reader["VinStatus"] as string,
                                Make = reader["Make"] as string,
                                Model = reader["Model"] as string,
                                PrimaryColor = reader["PrimaryColor"] as string,
                                SecondaryColor = reader["SecondaryColor"] as string,
                                RegistrationStatus = reader["RegistrationStatus"] as string,
                                RegistrationExpiration = reader["RegistrationExpiration"] as string,
                                InsuranceStatus = reader["InsuranceStatus"] as string,
                                InsuranceExpiration = reader["InsuranceExpiration"] as string
                            };

                            string bolosJson = reader["BOLOs"] as string;
                            if (!string.IsNullOrEmpty(bolosJson)) {
                                try {
                                    vehicle.BOLOs = JsonConvert.DeserializeObject<CommonDataFramework.Modules.VehicleDatabase.VehicleBOLO[]>(bolosJson);
                                } catch {
                                    vehicle.BOLOs = null;
                                }
                            }

                            vehicles.Add(vehicle);
                        }
                    }
                }

                return vehicles;
            }
        }

        internal static List<CourtData> LoadCourtCases() {
            lock (dbLock) {
                if (connection == null) return null;

                var cases = new List<CourtData>();

                using (var cmd = new SQLiteCommand("SELECT * FROM court_cases", connection)) {
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            var courtCase = new CourtData {
                                Number = reader["Number"] as string,
                                PedName = reader["PedName"] as string,
                                ReportId = reader["ReportId"] as string,
                                ShortYear = Convert.ToInt32(reader["ShortYear"]),
                                Status = Convert.ToInt32(reader["Status"]),
                                IsJuryTrial = Convert.ToBoolean(reader["IsJuryTrial"]),
                                JurySize = Convert.ToInt32(reader["JurySize"]),
                                JuryVotesForConviction = Convert.ToInt32(reader["JuryVotesForConviction"]),
                                JuryVotesForAcquittal = Convert.ToInt32(reader["JuryVotesForAcquittal"]),
                                PriorCitationCount = Convert.ToInt32(reader["PriorCitationCount"]),
                                PriorArrestCount = Convert.ToInt32(reader["PriorArrestCount"]),
                                PriorConvictionCount = Convert.ToInt32(reader["PriorConvictionCount"]),
                                SeverityScore = Convert.ToInt32(reader["SeverityScore"]),
                                EvidenceScore = reader["EvidenceScore"] is DBNull ? 0 : Convert.ToInt32(reader["EvidenceScore"]),
                                EvidenceHadWeapon = reader["EvidenceHadWeapon"] is DBNull ? false : Convert.ToBoolean(reader["EvidenceHadWeapon"]),
                                EvidenceWasWanted = reader["EvidenceWasWanted"] is DBNull ? false : Convert.ToBoolean(reader["EvidenceWasWanted"]),
                                EvidenceWasPatDown = reader["EvidenceWasPatDown"] is DBNull ? false : Convert.ToBoolean(reader["EvidenceWasPatDown"]),
                                EvidenceWasDrunk = reader["EvidenceWasDrunk"] is DBNull ? false : Convert.ToBoolean(reader["EvidenceWasDrunk"]),
                                EvidenceWasFleeing = reader["EvidenceWasFleeing"] is DBNull ? false : Convert.ToBoolean(reader["EvidenceWasFleeing"]),
                                EvidenceAssaultedPed = reader["EvidenceAssaultedPed"] is DBNull ? false : Convert.ToBoolean(reader["EvidenceAssaultedPed"]),
                                EvidenceDamagedVehicle = reader["EvidenceDamagedVehicle"] is DBNull ? false : Convert.ToBoolean(reader["EvidenceDamagedVehicle"]),
                                EvidenceIllegalWeapon = reader["EvidenceIllegalWeapon"] is DBNull ? false : Convert.ToBoolean(reader["EvidenceIllegalWeapon"]),
                                EvidenceViolatedSupervision = reader["EvidenceViolatedSupervision"] is DBNull ? false : Convert.ToBoolean(reader["EvidenceViolatedSupervision"]),
                                EvidenceResisted = reader["EvidenceResisted"] is DBNull ? false : Convert.ToBoolean(reader["EvidenceResisted"]),
                                EvidenceHadDrugs = GetBooleanFromReader(reader, "EvidenceHadDrugs"),
                                EvidenceUseOfForce = GetBooleanFromReader(reader, "EvidenceUseOfForce"),
                                EvidenceDrugTypesBreakdown = ParseListOrNull(ReaderOptionalString(reader, "EvidenceDrugTypesBreakdown")),
                                EvidenceFirearmTypesBreakdown = ParseListOrNull(ReaderOptionalString(reader, "EvidenceFirearmTypesBreakdown")),
                                ConvictionChance = reader["ConvictionChance"] is DBNull ? 0 : Convert.ToInt32(reader["ConvictionChance"]),
                                ResolveAtUtc = reader["ResolveAtUtc"] as string,
                                RepeatOffenderScore = Convert.ToInt32(reader["RepeatOffenderScore"]),
                                SentenceMultiplier = reader["SentenceMultiplier"] is DBNull ? 1f : Convert.ToSingle(reader["SentenceMultiplier"]),
                                ProsecutionStrength = reader["ProsecutionStrength"] is DBNull ? 0f : Convert.ToSingle(reader["ProsecutionStrength"]),
                                DefenseStrength = reader["DefenseStrength"] is DBNull ? 0f : Convert.ToSingle(reader["DefenseStrength"]),
                                DocketPressure = reader["DocketPressure"] is DBNull ? 0f : Convert.ToSingle(reader["DocketPressure"]),
                                PolicyAdjustment = reader["PolicyAdjustment"] is DBNull ? 0f : Convert.ToSingle(reader["PolicyAdjustment"]),
                                CourtDistrict = reader["CourtDistrict"] as string,
                                CourtName = reader["CourtName"] as string,
                                CourtType = reader["CourtType"] as string,
                                HasPublicDefender = reader["HasPublicDefender"] is DBNull || Convert.ToBoolean(reader["HasPublicDefender"]),
                                Plea = reader["Plea"] as string,
                                JudgeName = reader["JudgeName"] as string,
                                ProsecutorName = reader["ProsecutorName"] as string,
                                DefenseAttorneyName = reader["DefenseAttorneyName"] as string,
                                HearingDateUtc = reader["HearingDateUtc"] as string,
                                CreatedAtUtc = reader["CreatedAtUtc"] as string,
                                LastUpdatedUtc = reader["LastUpdatedUtc"] as string,
                                OutcomeNotes = reader["OutcomeNotes"] as string,
                                OutcomeReasoning = reader["OutcomeReasoning"] as string,
                                SentenceReasoning = ReaderOptionalString(reader, "SentenceReasoning"),
                                LicenseRevocations = ParseLicenseRevocations(reader["LicenseRevocations"] as string),
                                AttachedReportIds = ParseAttachedReportIds(ReaderOptionalString(reader, "AttachedReportIds"))
                            };

                            courtCase.Charges = LoadCourtCharges(courtCase.Number);
                            cases.Add(courtCase);
                        }
                    }
                }

                return cases;
            }
        }

        private static List<CourtData.Charge> LoadCourtCharges(string caseNumber) {
            var charges = new List<CourtData.Charge>();

            using (var cmd = new SQLiteCommand("SELECT * FROM court_charges WHERE CaseNumber = @num", connection)) {
                cmd.Parameters.AddWithValue("@num", caseNumber);

                using (var reader = cmd.ExecuteReader()) {
                    while (reader.Read()) {
                        charges.Add(new CourtData.Charge {
                            Name = reader["Name"] as string,
                            Fine = Convert.ToInt32(reader["Fine"]),
                            Time = reader["Time"] is DBNull ? (int?)null : Convert.ToInt32(reader["Time"]),
                            IsArrestable = reader["IsArrestable"] is DBNull ? (bool?)null : Convert.ToBoolean(reader["IsArrestable"]),
                            Outcome = ReaderOptionalInt(reader, "Outcome", 0),
                            ConvictionChance = ReaderOptionalIntNull(reader, "ConvictionChance"),
                            SentenceDaysServed = ReaderOptionalIntNull(reader, "SentenceDaysServed")
                        });
                    }
                }
            }

            return charges;
        }

        private static int ReaderOptionalInt(SQLiteDataReader reader, string columnName, int defaultValue) {
            try {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? defaultValue : Convert.ToInt32(reader[ordinal]);
            } catch { return defaultValue; }
        }

        private static int? ReaderOptionalIntNull(SQLiteDataReader reader, string columnName) {
            try {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? (int?)null : Convert.ToInt32(reader[ordinal]);
            } catch { return null; }
        }

        internal static OfficerInformationData LoadOfficerInformation() {
            lock (dbLock) {
                if (connection == null) return null;

                using (var cmd = new SQLiteCommand("SELECT * FROM officer_information WHERE Id = 1", connection)) {
                    using (var reader = cmd.ExecuteReader()) {
                        if (reader.Read()) {
                            return new OfficerInformationData {
                                firstName = reader["firstName"] as string,
                                lastName = reader["lastName"] as string,
                                rank = reader["rank"] as string,
                                callSign = reader["callSign"] as string,
                                agency = reader["agency"] as string,
                                badgeNumber = reader["badgeNumber"] is DBNull ? (int?)null : Convert.ToInt32(reader["badgeNumber"])
                            };
                        }
                    }
                }

                return null;
            }
        }

        internal static List<ShiftData> LoadShifts() {
            lock (dbLock) {
                if (connection == null) return null;

                var shifts = new List<ShiftData>();

                using (var cmd = new SQLiteCommand("SELECT * FROM shifts ORDER BY Id", connection)) {
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            int shiftId = Convert.ToInt32(reader["Id"]);

                            var shift = new ShiftData {
                                startTime = reader["startTime"] is DBNull ? (DateTime?)null : DateTime.Parse((string)reader["startTime"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                                endTime = reader["endTime"] is DBNull ? (DateTime?)null : DateTime.Parse((string)reader["endTime"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                            };

                            shift.reports = LoadShiftReports(shiftId);
                            shifts.Add(shift);
                        }
                    }
                }

                return shifts;
            }
        }

        private static List<string> LoadShiftReports(int shiftId) {
            var reports = new List<string>();

            using (var cmd = new SQLiteCommand("SELECT ReportId FROM shift_reports WHERE ShiftId = @id", connection)) {
                cmd.Parameters.AddWithValue("@id", shiftId);

                using (var reader = cmd.ExecuteReader()) {
                    while (reader.Read()) {
                        reports.Add(reader["ReportId"] as string);
                    }
                }
            }

            return reports;
        }

        internal static List<IncidentReport> LoadIncidentReports() {
            lock (dbLock) {
                if (connection == null) return null;

                var reports = new List<IncidentReport>();

                using (var cmd = new SQLiteCommand("SELECT * FROM incident_reports", connection)) {
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            var report = new IncidentReport {
                                Id = reader["Id"] as string,
                                ShortYear = Convert.ToInt32(reader["ShortYear"]),
                                OfficerInformation = ReadOfficerFromRow(reader),
                                Location = ReadLocationFromRow(reader),
                                TimeStamp = DateTime.Parse((string)reader["TimeStamp"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                                Status = (ReportStatus)Convert.ToInt32(reader["Status"]),
                                Notes = reader["Notes"] as string
                            };

                            report.OffenderPedsNames = LoadIncidentPedNames(report.Id, "incident_report_offenders");
                            report.WitnessPedsNames = LoadIncidentPedNames(report.Id, "incident_report_witnesses");
                            reports.Add(report);
                        }
                    }
                }

                return reports;
            }
        }

        private static string[] LoadIncidentPedNames(string reportId, string tableName) {
            var names = new List<string>();

            using (var cmd = new SQLiteCommand($"SELECT PedName FROM {tableName} WHERE ReportId = @id", connection)) {
                cmd.Parameters.AddWithValue("@id", reportId);

                using (var reader = cmd.ExecuteReader()) {
                    while (reader.Read()) {
                        names.Add(reader["PedName"] as string);
                    }
                }
            }

            return names.ToArray();
        }

        internal static List<CitationReport> LoadCitationReports() {
            lock (dbLock) {
                if (connection == null) return null;

                var reports = new List<CitationReport>();

                using (var cmd = new SQLiteCommand("SELECT * FROM citation_reports", connection)) {
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            var report = new CitationReport {
                                Id = reader["Id"] as string,
                                ShortYear = Convert.ToInt32(reader["ShortYear"]),
                                OfficerInformation = ReadOfficerFromRow(reader),
                                Location = ReadLocationFromRow(reader),
                                TimeStamp = DateTime.Parse((string)reader["TimeStamp"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                                Status = (ReportStatus)Convert.ToInt32(reader["Status"]),
                                Notes = reader["Notes"] as string,
                                OffenderPedName = reader["OffenderPedName"] as string,
                                OffenderVehicleLicensePlate = reader["OffenderVehicleLicensePlate"] as string,
                                CourtCaseNumber = reader["CourtCaseNumber"] as string,
                                FinalAmount = reader["FinalAmount"] is DBNull || reader["FinalAmount"] == null ? (int?)null : Convert.ToInt32(reader["FinalAmount"])
                            };

                            report.Charges = LoadCitationReportCharges(report.Id);
                            reports.Add(report);
                        }
                    }
                }

                return reports;
            }
        }

        private static List<CitationReport.Charge> LoadCitationReportCharges(string reportId) {
            var charges = new List<CitationReport.Charge>();

            using (var cmd = new SQLiteCommand("SELECT * FROM citation_report_charges WHERE ReportId = @id", connection)) {
                cmd.Parameters.AddWithValue("@id", reportId);

                using (var reader = cmd.ExecuteReader()) {
                    while (reader.Read()) {
                        charges.Add(new CitationReport.Charge {
                            name = reader["name"] as string,
                            minFine = Convert.ToInt32(reader["minFine"]),
                            maxFine = Convert.ToInt32(reader["maxFine"]),
                            canRevokeLicense = Convert.ToBoolean(reader["canRevokeLicense"]),
                            isArrestable = Convert.ToBoolean(reader["isArrestable"]),
                            addedByReportInEdit = Convert.ToBoolean(reader["addedByReportInEdit"])
                        });
                    }
                }
            }

            return charges;
        }

        internal static List<ArrestReport> LoadArrestReports() {
            lock (dbLock) {
                if (connection == null) return null;

                var reports = new List<ArrestReport>();

                using (var cmd = new SQLiteCommand("SELECT * FROM arrest_reports", connection)) {
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            var report = new ArrestReport {
                                Id = reader["Id"] as string,
                                ShortYear = Convert.ToInt32(reader["ShortYear"]),
                                OfficerInformation = ReadOfficerFromRow(reader),
                                Location = ReadLocationFromRow(reader),
                                TimeStamp = DateTime.Parse((string)reader["TimeStamp"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                                Status = (ReportStatus)Convert.ToInt32(reader["Status"]),
                                Notes = reader["Notes"] as string,
                                OffenderPedName = reader["OffenderPedName"] as string,
                                OffenderVehicleLicensePlate = reader["OffenderVehicleLicensePlate"] as string,
                                CourtCaseNumber = reader["CourtCaseNumber"] as string,
                                AttachedReportIds = ParseAttachedReportIds(ReaderOptionalString(reader, "AttachedReportIds"))
                            };
                            try {
                                string uofType = reader["UseOfForceType"] as string;
                                if (!string.IsNullOrEmpty(uofType)) {
                                    report.UseOfForce = new ArrestReport.UseOfForceData {
                                        Type = uofType,
                                        TypeOther = reader["UseOfForceTypeOther"] as string,
                                        Justification = reader["UseOfForceJustification"] as string,
                                        InjuryToSuspect = GetBooleanFromReader(reader, "UseOfForceInjurySuspect"),
                                        InjuryToOfficer = GetBooleanFromReader(reader, "UseOfForceInjuryOfficer"),
                                        Witnesses = reader["UseOfForceWitnesses"] as string
                                    };
                                }
                            } catch { /* columns may not exist on older schema */ }
                            try {
                                report.DocumentedDrugs = GetBooleanFromReader(reader, "DocumentedDrugs");
                                report.DocumentedFirearms = GetBooleanFromReader(reader, "DocumentedFirearms");
                            } catch { /* schema < 25 */ }

                            report.Charges = LoadArrestReportCharges(report.Id);
                            reports.Add(report);
                        }
                    }
                }

                return reports;
            }
        }

        internal static List<ImpoundReport> LoadImpoundReports() {
            lock (dbLock) {
                if (connection == null) return null;

                var reports = new List<ImpoundReport>();

                try {
                    using (var cmd = new SQLiteCommand("SELECT * FROM impound_reports", connection)) {
                        using (var reader = cmd.ExecuteReader()) {
                            while (reader.Read()) {
                                var report = new ImpoundReport {
                                    Id = reader["Id"] as string,
                                    ShortYear = Convert.ToInt32(reader["ShortYear"]),
                                    OfficerInformation = ReadOfficerFromRow(reader),
                                    Location = ReadLocationFromRow(reader),
                                    TimeStamp = DateTime.Parse((string)reader["TimeStamp"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                                    Status = (ReportStatus)Convert.ToInt32(reader["Status"]),
                                    Notes = reader["Notes"] as string,
                                    LicensePlate = reader["LicensePlate"] as string,
                                    VehicleModel = reader["VehicleModel"] as string,
                                    Owner = reader["Owner"] as string,
                                    PersonAtFaultName = ReaderOptionalString(reader, "PersonAtFaultName"),
                                    Vin = reader["Vin"] as string,
                                    ImpoundReason = reader["ImpoundReason"] as string,
                                    TowCompany = reader["TowCompany"] as string,
                                    ImpoundLot = reader["ImpoundLot"] as string
                                };
                                reports.Add(report);
                            }
                        }
                    }
                } catch { }

                return reports;
            }
        }

        internal static List<TrafficIncidentReport> LoadTrafficIncidentReports() {
            lock (dbLock) {
                if (connection == null) return null;
                var reports = new List<TrafficIncidentReport>();
                try {
                    using (var cmd = new SQLiteCommand("SELECT * FROM traffic_incident_reports", connection)) {
                        using (var reader = cmd.ExecuteReader()) {
                            while (reader.Read()) {
                                var r = new TrafficIncidentReport {
                                    Id = reader["Id"] as string,
                                    ShortYear = Convert.ToInt32(reader["ShortYear"]),
                                    OfficerInformation = ReadOfficerFromRow(reader),
                                    Location = ReadLocationFromRow(reader),
                                    TimeStamp = DateTime.Parse((string)reader["TimeStamp"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                                    Status = (ReportStatus)Convert.ToInt32(reader["Status"]),
                                    Notes = reader["Notes"] as string,
                                    DriverNames = ParseStringArray(reader["DriverNames"] as string),
                                    PassengerNames = ParseStringArray(reader["PassengerNames"] as string),
                                    PedestrianNames = ParseStringArray(reader["PedestrianNames"] as string),
                                    VehiclePlates = ParseStringArray(reader["VehiclePlates"] as string),
                                    VehicleModels = ParseStringArray(reader["VehicleModels"] as string),
                                    InjuryReported = GetBooleanFromReader(reader, "InjuryReported"),
                                    InjuryDetails = reader["InjuryDetails"] as string,
                                    CollisionType = reader["CollisionType"] as string
                                };
                                reports.Add(r);
                            }
                        }
                    }
                } catch { }
                return reports;
            }
        }

        internal static List<InjuryReport> LoadInjuryReports() {
            lock (dbLock) {
                if (connection == null) return null;
                var reports = new List<InjuryReport>();
                try {
                    using (var cmd = new SQLiteCommand("SELECT * FROM injury_reports", connection)) {
                        using (var reader = cmd.ExecuteReader()) {
                            while (reader.Read()) {
                                var r = new InjuryReport {
                                    Id = reader["Id"] as string,
                                    ShortYear = Convert.ToInt32(reader["ShortYear"]),
                                    OfficerInformation = ReadOfficerFromRow(reader),
                                    Location = ReadLocationFromRow(reader),
                                    TimeStamp = DateTime.Parse((string)reader["TimeStamp"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                                    Status = (ReportStatus)Convert.ToInt32(reader["Status"]),
                                    Notes = reader["Notes"] as string,
                                    InjuredPartyName = reader["InjuredPartyName"] as string,
                                    InjuryType = reader["InjuryType"] as string,
                                    Severity = reader["Severity"] as string,
                                    Treatment = reader["Treatment"] as string,
                                    IncidentContext = reader["IncidentContext"] as string,
                                    LinkedReportId = reader["LinkedReportId"] as string,
                                    GameInjurySnapshot = ReaderOptionalString(reader, "GameInjurySnapshot")
                                };
                                reports.Add(r);
                            }
                        }
                    }
                } catch { }
                return reports;
            }
        }

        private static List<PropertyEvidenceReceiptReport.SeizedDrugEntry> ParseSeizedDrugs(string json) {
            if (string.IsNullOrWhiteSpace(json)) return new List<PropertyEvidenceReceiptReport.SeizedDrugEntry>();
            try {
                var list = JsonConvert.DeserializeObject<List<PropertyEvidenceReceiptReport.SeizedDrugEntry>>(json);
                return list ?? new List<PropertyEvidenceReceiptReport.SeizedDrugEntry>();
            } catch { return new List<PropertyEvidenceReceiptReport.SeizedDrugEntry>(); }
        }

        internal static List<PropertyEvidenceReceiptReport> LoadPropertyEvidenceReceiptReports() {
            lock (dbLock) {
                if (connection == null) return null;
                var reports = new List<PropertyEvidenceReceiptReport>();
                try {
                    using (var cmd = new SQLiteCommand("SELECT * FROM property_evidence_reports", connection)) {
                        using (var reader = cmd.ExecuteReader()) {
                            while (reader.Read()) {
                                var subjectNames = ParseAttachedReportIds(ReaderOptionalString(reader, "SubjectPedNames"));
                                if (subjectNames == null || subjectNames.Count == 0) {
                                    var legacy = ReaderOptionalString(reader, "SubjectPedName");
                                    subjectNames = !string.IsNullOrWhiteSpace(legacy) ? new List<string> { legacy } : new List<string>();
                                }
                                var seizedDrugs = ParseSeizedDrugs(ReaderOptionalString(reader, "SeizedDrugs"));
                                if (seizedDrugs == null || seizedDrugs.Count == 0) {
                                    var legacyTypes = ParseAttachedReportIds(ReaderOptionalString(reader, "SeizedDrugTypes"));
                                    if (legacyTypes != null && legacyTypes.Count > 0) {
                                        seizedDrugs = legacyTypes.ConvertAll(t => new PropertyEvidenceReceiptReport.SeizedDrugEntry { DrugType = t, Quantity = "" });
                                    }
                                }
                                var r = new PropertyEvidenceReceiptReport {
                                    Id = reader["Id"] as string,
                                    ShortYear = Convert.ToInt32(reader["ShortYear"]),
                                    OfficerInformation = ReadOfficerFromRow(reader),
                                    Location = ReadLocationFromRow(reader),
                                    TimeStamp = DateTime.Parse((string)reader["TimeStamp"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                                    Status = (ReportStatus)Convert.ToInt32(reader["Status"]),
                                    Notes = reader["Notes"] as string,
                                    SubjectPedNames = subjectNames ?? new List<string>(),
                                    SeizedDrugs = seizedDrugs ?? new List<PropertyEvidenceReceiptReport.SeizedDrugEntry>(),
                                    SeizedFirearmTypes = ParseAttachedReportIds(ReaderOptionalString(reader, "SeizedFirearmTypes")) ?? new List<string>(),
                                    OtherContrabandNotes = ReaderOptionalString(reader, "OtherContrabandNotes")
                                };
                                reports.Add(r);
                            }
                        }
                    }
                } catch { }
                return reports;
            }
        }

        private static string[] ParseStringArray(string json) {
            if (string.IsNullOrWhiteSpace(json)) return new string[0];
            try {
                var list = JsonConvert.DeserializeObject<List<string>>(json);
                return list?.ToArray() ?? new string[0];
            } catch { return new string[0]; }
        }

        private static List<ArrestReport.Charge> LoadArrestReportCharges(string reportId) {
            var charges = new List<ArrestReport.Charge>();

            using (var cmd = new SQLiteCommand("SELECT * FROM arrest_report_charges WHERE ReportId = @id", connection)) {
                cmd.Parameters.AddWithValue("@id", reportId);

                using (var reader = cmd.ExecuteReader()) {
                    while (reader.Read()) {
                        charges.Add(new ArrestReport.Charge {
                            name = reader["name"] as string,
                            minFine = Convert.ToInt32(reader["minFine"]),
                            maxFine = Convert.ToInt32(reader["maxFine"]),
                            canRevokeLicense = Convert.ToBoolean(reader["canRevokeLicense"]),
                            isArrestable = Convert.ToBoolean(reader["isArrestable"]),
                            minDays = Convert.ToInt32(reader["minDays"]),
                            maxDays = reader["maxDays"] is DBNull ? (int?)null : Convert.ToInt32(reader["maxDays"]),
                            probation = Convert.ToSingle(reader["probation"]),
                            canBeWarrant = Convert.ToBoolean(reader["canBeWarrant"]),
                            addedByReportInEdit = Convert.ToBoolean(reader["addedByReportInEdit"])
                        });
                    }
                }
            }

            return charges;
        }

        private static OfficerInformationData ReadOfficerFromRow(SQLiteDataReader reader) {
            return new OfficerInformationData {
                firstName = reader["OfficerFirstName"] as string,
                lastName = reader["OfficerLastName"] as string,
                rank = reader["OfficerRank"] as string,
                callSign = reader["OfficerCallSign"] as string,
                agency = reader["OfficerAgency"] as string,
                badgeNumber = reader["OfficerBadgeNumber"] is DBNull ? (int?)null : Convert.ToInt32(reader["OfficerBadgeNumber"])
            };
        }

        private static Location ReadLocationFromRow(SQLiteDataReader reader) {
            return new Location {
                Area = reader["LocationArea"] as string,
                Street = reader["LocationStreet"] as string,
                County = reader["LocationCounty"] as string,
                Postal = reader["LocationPostal"] as string
            };
        }

        #endregion

        #region Save Methods

        internal static void SavePed(MDTProPedData ped) {
            if (ped?.Name == null) return;
            if (MDTProPedData.IsMinimalIdentity(ped)) {
                Utility.Helper.Log($"[MDTPro] Skipping save of minimal-identity ped (would show N/A in Person Search): {ped.Name}", false, Utility.Helper.LogSeverity.Info);
                return;
            }

            lock (dbLock) {
                if (connection == null) return;

                using (var transaction = connection.BeginTransaction()) {
                    SavePedInternal(ped, transaction);
                    transaction.Commit();
                }
            }
        }

        internal static void SavePeds(List<MDTProPedData> peds) {
            if (peds == null || peds.Count == 0) return;

            lock (dbLock) {
                if (connection == null) return;

                using (var transaction = connection.BeginTransaction()) {
                    foreach (var ped in peds) {
                        if (ped?.Name == null) continue;
                        SavePedInternal(ped, transaction);
                    }
                    transaction.Commit();
                }
            }
        }

        private static void SavePedInternal(MDTProPedData ped, SQLiteTransaction transaction) {
            using (var cmd = new SQLiteCommand(@"
                INSERT OR REPLACE INTO peds (
                    Name, FirstName, LastName, ModelHash, ModelName, Birthday, Gender, Address,
                    IsInGang, AdvisoryText, TimesStopped, IsWanted, WarrantText,
                    IsOnProbation, IsOnParole, LicenseStatus, LicenseExpiration,
                    WeaponPermitStatus, WeaponPermitExpiration, WeaponPermitType,
                    FishingPermitStatus, FishingPermitExpiration,
                    HuntingPermitStatus, HuntingPermitExpiration, IncarceratedUntil,
                    IdentificationHistory, IsDeceased, DeceasedAt
                ) VALUES (
                    @Name, @FirstName, @LastName, @ModelHash, @ModelName, @Birthday, @Gender, @Address,
                    @IsInGang, @AdvisoryText, @TimesStopped, @IsWanted, @WarrantText,
                    @IsOnProbation, @IsOnParole, @LicenseStatus, @LicenseExpiration,
                    @WeaponPermitStatus, @WeaponPermitExpiration, @WeaponPermitType,
                    @FishingPermitStatus, @FishingPermitExpiration,
                    @HuntingPermitStatus, @HuntingPermitExpiration, @IncarceratedUntil,
                    @IdentificationHistory, @IsDeceased, @DeceasedAt
                )", connection, transaction)) {
                cmd.Parameters.AddWithValue("@Name", (object)ped.Name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FirstName", (object)ped.FirstName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LastName", (object)ped.LastName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ModelHash", ped.ModelHash);
                cmd.Parameters.AddWithValue("@ModelName", (object)ped.ModelName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Birthday", (object)ped.Birthday ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Gender", (object)ped.Gender ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Address", (object)ped.Address ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsInGang", ped.IsInGang ? 1 : 0);
                cmd.Parameters.AddWithValue("@AdvisoryText", (object)ped.AdvisoryText ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TimesStopped", ped.TimesStopped);
                cmd.Parameters.AddWithValue("@IsWanted", ped.IsWanted ? 1 : 0);
                cmd.Parameters.AddWithValue("@WarrantText", (object)ped.WarrantText ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsOnProbation", ped.IsOnProbation ? 1 : 0);
                cmd.Parameters.AddWithValue("@IsOnParole", ped.IsOnParole ? 1 : 0);
                cmd.Parameters.AddWithValue("@LicenseStatus", (object)ped.LicenseStatus ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LicenseExpiration", (object)ped.LicenseExpiration ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@WeaponPermitStatus", (object)ped.WeaponPermitStatus ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@WeaponPermitExpiration", (object)ped.WeaponPermitExpiration ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@WeaponPermitType", (object)ped.WeaponPermitType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FishingPermitStatus", (object)ped.FishingPermitStatus ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FishingPermitExpiration", (object)ped.FishingPermitExpiration ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@HuntingPermitStatus", (object)ped.HuntingPermitStatus ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@HuntingPermitExpiration", (object)ped.HuntingPermitExpiration ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IncarceratedUntil", (object)ped.IncarceratedUntil ?? DBNull.Value);
                string idHistoryJson = ped.IdentificationHistory != null && ped.IdentificationHistory.Count > 0
                    ? JsonConvert.SerializeObject(ped.IdentificationHistory)
                    : null;
                cmd.Parameters.AddWithValue("@IdentificationHistory", (object)idHistoryJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsDeceased", ped.IsDeceased ? 1 : 0);
                cmd.Parameters.AddWithValue("@DeceasedAt", (object)ped.DeceasedAt ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand("DELETE FROM ped_citations WHERE PedName = @name", connection, transaction)) {
                cmd.Parameters.AddWithValue("@name", ped.Name);
                cmd.ExecuteNonQuery();
            }

            if (ped.Citations != null) {
                foreach (var charge in ped.Citations) {
                    using (var cmd = new SQLiteCommand(@"
                        INSERT INTO ped_citations (PedName, name, minFine, maxFine, canRevokeLicense, isArrestable)
                        VALUES (@PedName, @name, @minFine, @maxFine, @canRevokeLicense, @isArrestable)",
                        connection, transaction)) {
                        cmd.Parameters.AddWithValue("@PedName", ped.Name);
                        cmd.Parameters.AddWithValue("@name", (object)charge.name ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@minFine", charge.minFine);
                        cmd.Parameters.AddWithValue("@maxFine", charge.maxFine);
                        cmd.Parameters.AddWithValue("@canRevokeLicense", charge.canRevokeLicense ? 1 : 0);
                        cmd.Parameters.AddWithValue("@isArrestable", charge.isArrestable ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            using (var cmd = new SQLiteCommand("DELETE FROM ped_arrests WHERE PedName = @name", connection, transaction)) {
                cmd.Parameters.AddWithValue("@name", ped.Name);
                cmd.ExecuteNonQuery();
            }

            if (ped.Arrests != null) {
                foreach (var charge in ped.Arrests) {
                    using (var cmd = new SQLiteCommand(@"
                        INSERT INTO ped_arrests (PedName, name, minFine, maxFine, canRevokeLicense, isArrestable, minDays, maxDays, probation, canBeWarrant)
                        VALUES (@PedName, @name, @minFine, @maxFine, @canRevokeLicense, @isArrestable, @minDays, @maxDays, @probation, @canBeWarrant)",
                        connection, transaction)) {
                        cmd.Parameters.AddWithValue("@PedName", ped.Name);
                        cmd.Parameters.AddWithValue("@name", (object)charge.name ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@minFine", charge.minFine);
                        cmd.Parameters.AddWithValue("@maxFine", charge.maxFine);
                        cmd.Parameters.AddWithValue("@canRevokeLicense", charge.canRevokeLicense ? 1 : 0);
                        cmd.Parameters.AddWithValue("@isArrestable", charge.isArrestable ? 1 : 0);
                        cmd.Parameters.AddWithValue("@minDays", charge.minDays);
                        cmd.Parameters.AddWithValue("@maxDays", charge.maxDays.HasValue ? (object)charge.maxDays.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@probation", charge.probation);
                        cmd.Parameters.AddWithValue("@canBeWarrant", charge.canBeWarrant ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        /// <summary>Marks a ped as deceased. Persists to DB and updates in-memory ped if present.</summary>
        internal static void MarkPedDeceased(string pedName, string deceasedAtUtc = null) {
            if (string.IsNullOrWhiteSpace(pedName)) return;
            if (string.IsNullOrWhiteSpace(deceasedAtUtc)) deceasedAtUtc = DateTime.UtcNow.ToString("o");

            lock (dbLock) {
                if (connection == null) return;
                using (var cmd = new SQLiteCommand("UPDATE peds SET IsDeceased = 1, DeceasedAt = @at WHERE Name = @name", connection)) {
                    cmd.Parameters.AddWithValue("@at", deceasedAtUtc);
                    cmd.Parameters.AddWithValue("@name", pedName);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Saves a damage cache entry to SQL for permanent injury report lookup.</summary>
        internal static void SaveDamageCacheEntry(string victimName, int damage, int armourDamage, string weaponType, string weaponGroup, string bodyRegion, bool victimAlive, DateTime atUtc) {
            if (string.IsNullOrWhiteSpace(victimName)) return;

            lock (dbLock) {
                if (connection == null) return;
                using (var cmd = new SQLiteCommand(@"
                    INSERT INTO damage_cache (VictimName, Damage, ArmourDamage, WeaponType, WeaponGroup, BodyRegion, VictimAlive, AtUtc)
                    VALUES (@VictimName, @Damage, @ArmourDamage, @WeaponType, @WeaponGroup, @BodyRegion, @VictimAlive, @AtUtc)", connection)) {
                    cmd.Parameters.AddWithValue("@VictimName", victimName);
                    cmd.Parameters.AddWithValue("@Damage", damage);
                    cmd.Parameters.AddWithValue("@ArmourDamage", armourDamage);
                    cmd.Parameters.AddWithValue("@WeaponType", (object)weaponType ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@WeaponGroup", (object)weaponGroup ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@BodyRegion", (object)bodyRegion ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@VictimAlive", victimAlive ? 1 : 0);
                    cmd.Parameters.AddWithValue("@AtUtc", atUtc.ToString("o"));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Loads the most recent damage cache entry for a victim (permanent, from SQL). Returns null if none.</summary>
        internal static (int Damage, int ArmourDamage, string WeaponType, string WeaponGroup, string BodyRegion, bool VictimAlive, DateTime At)? LoadDamageCacheByVictimName(string victimName) {
            if (string.IsNullOrWhiteSpace(victimName)) return null;

            lock (dbLock) {
                if (connection == null) return null;
                using (var cmd = new SQLiteCommand(@"
                    SELECT Damage, ArmourDamage, WeaponType, WeaponGroup, BodyRegion, VictimAlive, AtUtc 
                    FROM damage_cache 
                    WHERE VictimName = @name 
                    ORDER BY 
                        CASE WHEN UPPER(COALESCE(WeaponGroup,'')) LIKE '%BULLET%' 
                                  OR UPPER(COALESCE(WeaponType,'')) LIKE '%PISTOL%' 
                                  OR UPPER(COALESCE(WeaponType,'')) LIKE '%RIFLE%'
                                  OR UPPER(COALESCE(WeaponType,'')) LIKE '%GUN%'
                                  OR UPPER(COALESCE(WeaponType,'')) LIKE '%SHOTGUN%'
                             THEN 0 ELSE 1 END,
                        AtUtc DESC 
                    LIMIT 1", connection)) {
                    cmd.Parameters.AddWithValue("@name", victimName);
                    using (var reader = cmd.ExecuteReader()) {
                        if (!reader.Read()) return null;
                        return (
                            Convert.ToInt32(reader["Damage"]),
                            Convert.ToInt32(reader["ArmourDamage"]),
                            reader["WeaponType"] as string,
                            reader["WeaponGroup"] as string,
                            reader["BodyRegion"] as string,
                            Convert.ToBoolean(reader["VictimAlive"]),
                            DateTime.Parse(reader["AtUtc"] as string ?? "", null, System.Globalization.DateTimeStyles.RoundtripKind)
                        );
                    }
                }
            }
        }

        internal static void SaveVehicle(MDTProVehicleData vehicle) {
            if (vehicle?.LicensePlate == null) return;

            lock (dbLock) {
                if (connection == null) return;

                using (var transaction = connection.BeginTransaction()) {
                    SaveVehicleInternal(vehicle, transaction);
                    transaction.Commit();
                }
            }
        }

        internal static void SaveVehicles(List<MDTProVehicleData> vehicles) {
            if (vehicles == null || vehicles.Count == 0) return;

            lock (dbLock) {
                if (connection == null) return;

                using (var transaction = connection.BeginTransaction()) {
                    foreach (var vehicle in vehicles) {
                        if (vehicle?.LicensePlate == null) continue;
                        SaveVehicleInternal(vehicle, transaction);
                    }
                    transaction.Commit();
                }
            }
        }

        private static void SaveVehicleInternal(MDTProVehicleData vehicle, SQLiteTransaction transaction) {
            string bolosJson = vehicle.BOLOs != null ? JsonConvert.SerializeObject(vehicle.BOLOs) : null;

            using (var cmd = new SQLiteCommand(@"
                INSERT OR REPLACE INTO vehicles (
                    LicensePlate, ModelName, ModelDisplayName, IsStolen, Owner, Color,
                    VehicleIdentificationNumber, VinStatus, Make, Model, PrimaryColor, SecondaryColor,
                    RegistrationStatus, RegistrationExpiration,
                    InsuranceStatus, InsuranceExpiration, BOLOs
                ) VALUES (
                    @LicensePlate, @ModelName, @ModelDisplayName, @IsStolen, @Owner, @Color,
                    @VIN, @VinStatus, @Make, @Model, @PrimaryColor, @SecondaryColor,
                    @RegStatus, @RegExpiration, @InsStatus, @InsExpiration, @BOLOs
                )", connection, transaction)) {
                cmd.Parameters.AddWithValue("@LicensePlate", (object)vehicle.LicensePlate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ModelName", (object)vehicle.ModelName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ModelDisplayName", (object)vehicle.ModelDisplayName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsStolen", vehicle.IsStolen ? 1 : 0);
                cmd.Parameters.AddWithValue("@Owner", (object)vehicle.Owner ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Color", (object)vehicle.Color ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@VIN", (object)vehicle.VehicleIdentificationNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@VinStatus", (object)vehicle.VinStatus ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Make", (object)vehicle.Make ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Model", (object)vehicle.Model ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PrimaryColor", (object)vehicle.PrimaryColor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SecondaryColor", (object)vehicle.SecondaryColor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RegStatus", (object)vehicle.RegistrationStatus ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RegExpiration", (object)vehicle.RegistrationExpiration ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@InsStatus", (object)vehicle.InsuranceStatus ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@InsExpiration", (object)vehicle.InsuranceExpiration ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@BOLOs", (object)bolosJson ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        internal static void SaveCourtCase(CourtData courtCase) {
            if (courtCase?.Number == null) return;

            lock (dbLock) {
                if (connection == null) return;

                using (var transaction = connection.BeginTransaction()) {
                    SaveCourtCaseInternal(courtCase, transaction);
                    transaction.Commit();
                }
            }
        }

        private static void SaveCourtCaseInternal(CourtData courtCase, SQLiteTransaction transaction) {
            if (string.IsNullOrEmpty(courtCase.CreatedAtUtc)) {
                courtCase.CreatedAtUtc = DateTime.UtcNow.ToString("o");
            }
            courtCase.LastUpdatedUtc = DateTime.UtcNow.ToString("o");

            using (var cmd = new SQLiteCommand(@"
                INSERT OR REPLACE INTO court_cases (
                    Number, PedName, ReportId, ShortYear, Status,
                    IsJuryTrial, JurySize, JuryVotesForConviction, JuryVotesForAcquittal,
                    PriorCitationCount, PriorArrestCount, PriorConvictionCount, SeverityScore, EvidenceScore,
                    EvidenceHadWeapon, EvidenceWasWanted, EvidenceWasPatDown,
                    EvidenceWasDrunk, EvidenceWasFleeing, EvidenceAssaultedPed, EvidenceDamagedVehicle, EvidenceIllegalWeapon,
                    EvidenceViolatedSupervision, EvidenceResisted, EvidenceHadDrugs, EvidenceUseOfForce, ConvictionChance, ResolveAtUtc,
                    RepeatOffenderScore,
                    SentenceMultiplier, ProsecutionStrength, DefenseStrength, DocketPressure, PolicyAdjustment,
                    CourtDistrict, CourtName, CourtType, HasPublicDefender, Plea,
                    JudgeName, ProsecutorName, DefenseAttorneyName,
                    HearingDateUtc, CreatedAtUtc, LastUpdatedUtc, OutcomeNotes, OutcomeReasoning, SentenceReasoning, LicenseRevocations, AttachedReportIds,
                    EvidenceDrugTypesBreakdown, EvidenceFirearmTypesBreakdown
                ) VALUES (
                    @Number, @PedName, @ReportId, @ShortYear, @Status,
                    @IsJuryTrial, @JurySize, @JuryVotesForConviction, @JuryVotesForAcquittal,
                    @PriorCitationCount, @PriorArrestCount, @PriorConvictionCount, @SeverityScore, @EvidenceScore,
                    @EvidenceHadWeapon, @EvidenceWasWanted, @EvidenceWasPatDown,
                    @EvidenceWasDrunk, @EvidenceWasFleeing, @EvidenceAssaultedPed, @EvidenceDamagedVehicle, @EvidenceIllegalWeapon,
                    @EvidenceViolatedSupervision, @EvidenceResisted, @EvidenceHadDrugs, @EvidenceUseOfForce, @ConvictionChance, @ResolveAtUtc,
                    @RepeatOffenderScore,
                    @SentenceMultiplier, @ProsecutionStrength, @DefenseStrength, @DocketPressure, @PolicyAdjustment,
                    @CourtDistrict, @CourtName, @CourtType, @HasPublicDefender, @Plea,
                    @JudgeName, @ProsecutorName, @DefenseAttorneyName,
                    @HearingDateUtc, @CreatedAtUtc, @LastUpdatedUtc, @OutcomeNotes, @OutcomeReasoning, @SentenceReasoning, @LicenseRevocations, @AttachedReportIds,
                    @EvidenceDrugTypesBreakdown, @EvidenceFirearmTypesBreakdown
                )",
                connection, transaction)) {
                cmd.Parameters.AddWithValue("@Number", (object)courtCase.Number ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PedName", (object)courtCase.PedName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ReportId", (object)courtCase.ReportId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ShortYear", courtCase.ShortYear);
                cmd.Parameters.AddWithValue("@Status", courtCase.Status);
                cmd.Parameters.AddWithValue("@IsJuryTrial", courtCase.IsJuryTrial ? 1 : 0);
                cmd.Parameters.AddWithValue("@JurySize", courtCase.JurySize);
                cmd.Parameters.AddWithValue("@JuryVotesForConviction", courtCase.JuryVotesForConviction);
                cmd.Parameters.AddWithValue("@JuryVotesForAcquittal", courtCase.JuryVotesForAcquittal);
                cmd.Parameters.AddWithValue("@PriorCitationCount", courtCase.PriorCitationCount);
                cmd.Parameters.AddWithValue("@PriorArrestCount", courtCase.PriorArrestCount);
                cmd.Parameters.AddWithValue("@PriorConvictionCount", courtCase.PriorConvictionCount);
                cmd.Parameters.AddWithValue("@SeverityScore", courtCase.SeverityScore);
                cmd.Parameters.AddWithValue("@EvidenceScore", courtCase.EvidenceScore);
                cmd.Parameters.AddWithValue("@EvidenceHadWeapon", courtCase.EvidenceHadWeapon ? 1 : 0);
                cmd.Parameters.AddWithValue("@EvidenceWasWanted", courtCase.EvidenceWasWanted ? 1 : 0);
                cmd.Parameters.AddWithValue("@EvidenceWasPatDown", courtCase.EvidenceWasPatDown ? 1 : 0);
                cmd.Parameters.AddWithValue("@EvidenceWasDrunk", courtCase.EvidenceWasDrunk ? 1 : 0);
                cmd.Parameters.AddWithValue("@EvidenceWasFleeing", courtCase.EvidenceWasFleeing ? 1 : 0);
                cmd.Parameters.AddWithValue("@EvidenceAssaultedPed", courtCase.EvidenceAssaultedPed ? 1 : 0);
                cmd.Parameters.AddWithValue("@EvidenceDamagedVehicle", courtCase.EvidenceDamagedVehicle ? 1 : 0);
                cmd.Parameters.AddWithValue("@EvidenceIllegalWeapon", courtCase.EvidenceIllegalWeapon ? 1 : 0);
                cmd.Parameters.AddWithValue("@EvidenceViolatedSupervision", courtCase.EvidenceViolatedSupervision ? 1 : 0);
                cmd.Parameters.AddWithValue("@EvidenceResisted", courtCase.EvidenceResisted ? 1 : 0);
                cmd.Parameters.AddWithValue("@EvidenceHadDrugs", courtCase.EvidenceHadDrugs ? 1 : 0);
                cmd.Parameters.AddWithValue("@EvidenceUseOfForce", courtCase.EvidenceUseOfForce ? 1 : 0);
                cmd.Parameters.AddWithValue("@ConvictionChance", courtCase.ConvictionChance);
                cmd.Parameters.AddWithValue("@ResolveAtUtc", (object)courtCase.ResolveAtUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RepeatOffenderScore", courtCase.RepeatOffenderScore);
                cmd.Parameters.AddWithValue("@SentenceMultiplier", courtCase.SentenceMultiplier <= 0f ? 1f : courtCase.SentenceMultiplier);
                cmd.Parameters.AddWithValue("@ProsecutionStrength", courtCase.ProsecutionStrength);
                cmd.Parameters.AddWithValue("@DefenseStrength", courtCase.DefenseStrength);
                cmd.Parameters.AddWithValue("@DocketPressure", courtCase.DocketPressure);
                cmd.Parameters.AddWithValue("@PolicyAdjustment", courtCase.PolicyAdjustment);
                cmd.Parameters.AddWithValue("@CourtDistrict", (object)courtCase.CourtDistrict ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CourtName", (object)courtCase.CourtName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CourtType", (object)courtCase.CourtType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@HasPublicDefender", courtCase.HasPublicDefender ? 1 : 0);
                cmd.Parameters.AddWithValue("@Plea", (object)courtCase.Plea ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@JudgeName", (object)courtCase.JudgeName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ProsecutorName", (object)courtCase.ProsecutorName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DefenseAttorneyName", (object)courtCase.DefenseAttorneyName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@HearingDateUtc", (object)courtCase.HearingDateUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CreatedAtUtc", (object)courtCase.CreatedAtUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LastUpdatedUtc", (object)courtCase.LastUpdatedUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@OutcomeNotes", (object)courtCase.OutcomeNotes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@OutcomeReasoning", (object)courtCase.OutcomeReasoning ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SentenceReasoning", (object)courtCase.SentenceReasoning ?? DBNull.Value);
                string revocationsJson = courtCase.LicenseRevocations != null && courtCase.LicenseRevocations.Count > 0
                    ? Newtonsoft.Json.JsonConvert.SerializeObject(courtCase.LicenseRevocations) : null;
                cmd.Parameters.AddWithValue("@LicenseRevocations", (object)revocationsJson ?? DBNull.Value);
                string attachedJson = courtCase.AttachedReportIds != null && courtCase.AttachedReportIds.Count > 0
                    ? Newtonsoft.Json.JsonConvert.SerializeObject(courtCase.AttachedReportIds) : null;
                cmd.Parameters.AddWithValue("@AttachedReportIds", (object)attachedJson ?? DBNull.Value);
                string drugBreakdownJson = courtCase.EvidenceDrugTypesBreakdown != null && courtCase.EvidenceDrugTypesBreakdown.Count > 0
                    ? Newtonsoft.Json.JsonConvert.SerializeObject(courtCase.EvidenceDrugTypesBreakdown) : null;
                cmd.Parameters.AddWithValue("@EvidenceDrugTypesBreakdown", (object)drugBreakdownJson ?? DBNull.Value);
                string firearmBreakdownJson = courtCase.EvidenceFirearmTypesBreakdown != null && courtCase.EvidenceFirearmTypesBreakdown.Count > 0
                    ? Newtonsoft.Json.JsonConvert.SerializeObject(courtCase.EvidenceFirearmTypesBreakdown) : null;
                cmd.Parameters.AddWithValue("@EvidenceFirearmTypesBreakdown", (object)firearmBreakdownJson ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand("DELETE FROM court_charges WHERE CaseNumber = @num", connection, transaction)) {
                cmd.Parameters.AddWithValue("@num", courtCase.Number);
                cmd.ExecuteNonQuery();
            }

            if (courtCase.Charges != null) {
                foreach (var charge in courtCase.Charges) {
                    using (var cmd = new SQLiteCommand(@"
                        INSERT INTO court_charges (CaseNumber, Name, Fine, Time, IsArrestable, Outcome, ConvictionChance, SentenceDaysServed)
                        VALUES (@CaseNumber, @Name, @Fine, @Time, @IsArrestable, @Outcome, @ConvictionChance, @SentenceDaysServed)",
                        connection, transaction)) {
                        cmd.Parameters.AddWithValue("@CaseNumber", courtCase.Number);
                        cmd.Parameters.AddWithValue("@Name", (object)charge.Name ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Fine", charge.Fine);
                        cmd.Parameters.AddWithValue("@Time", charge.Time.HasValue ? (object)charge.Time.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@IsArrestable", charge.IsArrestable.HasValue ? (object)(charge.IsArrestable.Value ? 1 : 0) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@Outcome", charge.Outcome);
                        cmd.Parameters.AddWithValue("@ConvictionChance", charge.ConvictionChance.HasValue ? (object)charge.ConvictionChance.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@SentenceDaysServed", charge.SentenceDaysServed.HasValue ? (object)charge.SentenceDaysServed.Value : DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        internal static void DeleteCourtCase(string caseNumber) {
            if (string.IsNullOrEmpty(caseNumber)) return;

            lock (dbLock) {
                if (connection == null) return;

                using (var cmd = new SQLiteCommand("DELETE FROM court_cases WHERE Number = @num", connection)) {
                    cmd.Parameters.AddWithValue("@num", caseNumber);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        internal static void SaveOfficerInformation(OfficerInformationData data) {
            if (data == null) return;

            lock (dbLock) {
                if (connection == null) return;

                using (var cmd = new SQLiteCommand(@"
                    INSERT OR REPLACE INTO officer_information (Id, firstName, lastName, rank, callSign, agency, badgeNumber)
                    VALUES (1, @firstName, @lastName, @rank, @callSign, @agency, @badgeNumber)",
                    connection)) {
                    cmd.Parameters.AddWithValue("@firstName", (object)data.firstName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@lastName", (object)data.lastName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@rank", (object)data.rank ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@callSign", (object)data.callSign ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@agency", (object)data.agency ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@badgeNumber", data.badgeNumber.HasValue ? (object)data.badgeNumber.Value : DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        internal static void SaveShift(ShiftData shift) {
            if (shift == null) return;

            lock (dbLock) {
                if (connection == null) return;

                using (var transaction = connection.BeginTransaction()) {
                    SaveShiftInternal(shift, transaction);
                    transaction.Commit();
                }
            }
        }

        internal static void SaveShifts(List<ShiftData> shifts) {
            if (shifts == null) return;

            lock (dbLock) {
                if (connection == null) return;

                using (var transaction = connection.BeginTransaction()) {
                    using (var cmd = new SQLiteCommand("DELETE FROM shifts", connection, transaction)) {
                        cmd.ExecuteNonQuery();
                    }

                    foreach (var shift in shifts) {
                        if (shift == null) continue;
                        SaveShiftInternal(shift, transaction);
                    }
                    transaction.Commit();
                }
            }
        }

        private static void SaveShiftInternal(ShiftData shift, SQLiteTransaction transaction) {
            using (var cmd = new SQLiteCommand(@"
                INSERT INTO shifts (startTime, endTime)
                VALUES (@startTime, @endTime);
                SELECT last_insert_rowid();",
                connection, transaction)) {
                cmd.Parameters.AddWithValue("@startTime", shift.startTime.HasValue ? (object)shift.startTime.Value.ToString("o") : DBNull.Value);
                cmd.Parameters.AddWithValue("@endTime", shift.endTime.HasValue ? (object)shift.endTime.Value.ToString("o") : DBNull.Value);
                long shiftId = (long)cmd.ExecuteScalar();

                if (shift.reports != null) {
                    foreach (string reportId in shift.reports) {
                        using (var rptCmd = new SQLiteCommand(@"
                            INSERT INTO shift_reports (ShiftId, ReportId) VALUES (@shiftId, @reportId)",
                            connection, transaction)) {
                            rptCmd.Parameters.AddWithValue("@shiftId", shiftId);
                            rptCmd.Parameters.AddWithValue("@reportId", reportId);
                            rptCmd.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        internal static void SaveIncidentReport(IncidentReport report) {
            if (report?.Id == null) return;

            lock (dbLock) {
                if (connection == null) return;

                using (var transaction = connection.BeginTransaction()) {
                    WriteReportBase("incident_reports", report, transaction);

                    using (var cmd = new SQLiteCommand("DELETE FROM incident_report_offenders WHERE ReportId = @id", connection, transaction)) {
                        cmd.Parameters.AddWithValue("@id", report.Id);
                        cmd.ExecuteNonQuery();
                    }

                    if (report.OffenderPedsNames != null) {
                        foreach (string name in report.OffenderPedsNames) {
                            if (string.IsNullOrEmpty(name)) continue;
                            using (var cmd = new SQLiteCommand("INSERT INTO incident_report_offenders (ReportId, PedName) VALUES (@id, @name)", connection, transaction)) {
                                cmd.Parameters.AddWithValue("@id", report.Id);
                                cmd.Parameters.AddWithValue("@name", name);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }

                    using (var cmd = new SQLiteCommand("DELETE FROM incident_report_witnesses WHERE ReportId = @id", connection, transaction)) {
                        cmd.Parameters.AddWithValue("@id", report.Id);
                        cmd.ExecuteNonQuery();
                    }

                    if (report.WitnessPedsNames != null) {
                        foreach (string name in report.WitnessPedsNames) {
                            if (string.IsNullOrEmpty(name)) continue;
                            using (var cmd = new SQLiteCommand("INSERT INTO incident_report_witnesses (ReportId, PedName) VALUES (@id, @name)", connection, transaction)) {
                                cmd.Parameters.AddWithValue("@id", report.Id);
                                cmd.Parameters.AddWithValue("@name", name);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }

                    transaction.Commit();
                }
            }
        }

        internal static void SaveCitationReport(CitationReport report) {
            if (report?.Id == null) return;

            lock (dbLock) {
                if (connection == null) return;

                using (var transaction = connection.BeginTransaction()) {
                    using (var cmd = new SQLiteCommand(@"
                        INSERT OR REPLACE INTO citation_reports (
                            Id, ShortYear, OfficerFirstName, OfficerLastName, OfficerRank,
                            OfficerCallSign, OfficerAgency, OfficerBadgeNumber,
                            LocationArea, LocationStreet, LocationCounty, LocationPostal,
                            TimeStamp, Status, Notes, OffenderPedName,
                            OffenderVehicleLicensePlate, CourtCaseNumber, FinalAmount
                        ) VALUES (
                            @Id, @ShortYear, @OfficerFirstName, @OfficerLastName, @OfficerRank,
                            @OfficerCallSign, @OfficerAgency, @OfficerBadgeNumber,
                            @LocationArea, @LocationStreet, @LocationCounty, @LocationPostal,
                            @TimeStamp, @Status, @Notes, @OffenderPedName,
                            @OffenderVehicleLicensePlate, @CourtCaseNumber, @FinalAmount
                        )", connection, transaction)) {
                        AddReportBaseParams(cmd, report);
                        cmd.Parameters.AddWithValue("@OffenderPedName", (object)report.OffenderPedName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@OffenderVehicleLicensePlate", (object)report.OffenderVehicleLicensePlate ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@CourtCaseNumber", (object)report.CourtCaseNumber ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@FinalAmount", report.FinalAmount.HasValue ? (object)report.FinalAmount.Value : DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd = new SQLiteCommand("DELETE FROM citation_report_charges WHERE ReportId = @id", connection, transaction)) {
                        cmd.Parameters.AddWithValue("@id", report.Id);
                        cmd.ExecuteNonQuery();
                    }

                    if (report.Charges != null) {
                        foreach (var charge in report.Charges) {
                            using (var cmd = new SQLiteCommand(@"
                                INSERT INTO citation_report_charges (ReportId, name, minFine, maxFine, canRevokeLicense, isArrestable, addedByReportInEdit)
                                VALUES (@ReportId, @name, @minFine, @maxFine, @canRevokeLicense, @isArrestable, @addedByReportInEdit)",
                                connection, transaction)) {
                                cmd.Parameters.AddWithValue("@ReportId", report.Id);
                                cmd.Parameters.AddWithValue("@name", (object)charge.name ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@minFine", charge.minFine);
                                cmd.Parameters.AddWithValue("@maxFine", charge.maxFine);
                                cmd.Parameters.AddWithValue("@canRevokeLicense", charge.canRevokeLicense ? 1 : 0);
                                cmd.Parameters.AddWithValue("@isArrestable", charge.isArrestable ? 1 : 0);
                                cmd.Parameters.AddWithValue("@addedByReportInEdit", charge.addedByReportInEdit ? 1 : 0);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }

                    transaction.Commit();
                }
            }
        }

        internal static void SaveArrestReport(ArrestReport report) {
            if (report?.Id == null) return;

            lock (dbLock) {
                if (connection == null) return;

                using (var transaction = connection.BeginTransaction()) {
                    using (var cmd = new SQLiteCommand(@"
                        INSERT OR REPLACE INTO arrest_reports (
                            Id, ShortYear, OfficerFirstName, OfficerLastName, OfficerRank,
                            OfficerCallSign, OfficerAgency, OfficerBadgeNumber,
                            LocationArea, LocationStreet, LocationCounty, LocationPostal,
                            TimeStamp, Status, Notes, OffenderPedName,
                            OffenderVehicleLicensePlate, CourtCaseNumber,
                            UseOfForceType, UseOfForceTypeOther, UseOfForceJustification,
                            UseOfForceInjurySuspect, UseOfForceInjuryOfficer, UseOfForceWitnesses,
                            DocumentedDrugs, DocumentedFirearms, AttachedReportIds
                        ) VALUES (
                            @Id, @ShortYear, @OfficerFirstName, @OfficerLastName, @OfficerRank,
                            @OfficerCallSign, @OfficerAgency, @OfficerBadgeNumber,
                            @LocationArea, @LocationStreet, @LocationCounty, @LocationPostal,
                            @TimeStamp, @Status, @Notes, @OffenderPedName,
                            @OffenderVehicleLicensePlate, @CourtCaseNumber,
                            @UseOfForceType, @UseOfForceTypeOther, @UseOfForceJustification,
                            @UseOfForceInjurySuspect, @UseOfForceInjuryOfficer, @UseOfForceWitnesses,
                            @DocumentedDrugs, @DocumentedFirearms, @AttachedReportIds
                        )", connection, transaction)) {
                        AddReportBaseParams(cmd, report);
                        cmd.Parameters.AddWithValue("@OffenderPedName", (object)report.OffenderPedName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@OffenderVehicleLicensePlate", (object)report.OffenderVehicleLicensePlate ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@CourtCaseNumber", (object)report.CourtCaseNumber ?? DBNull.Value);
                        string arrestAttachedJson = report.AttachedReportIds != null && report.AttachedReportIds.Count > 0
                            ? Newtonsoft.Json.JsonConvert.SerializeObject(report.AttachedReportIds) : null;
                        cmd.Parameters.AddWithValue("@AttachedReportIds", (object)arrestAttachedJson ?? DBNull.Value);
                        var uof = report.UseOfForce;
                        cmd.Parameters.AddWithValue("@UseOfForceType", (object)uof?.Type ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@UseOfForceTypeOther", (object)uof?.TypeOther ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@UseOfForceJustification", (object)uof?.Justification ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@UseOfForceInjurySuspect", uof?.InjuryToSuspect == true ? 1 : 0);
                        cmd.Parameters.AddWithValue("@UseOfForceInjuryOfficer", uof?.InjuryToOfficer == true ? 1 : 0);
                        cmd.Parameters.AddWithValue("@UseOfForceWitnesses", (object)uof?.Witnesses ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DocumentedDrugs", report.DocumentedDrugs ? 1 : 0);
                        cmd.Parameters.AddWithValue("@DocumentedFirearms", report.DocumentedFirearms ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd = new SQLiteCommand("DELETE FROM arrest_report_charges WHERE ReportId = @id", connection, transaction)) {
                        cmd.Parameters.AddWithValue("@id", report.Id);
                        cmd.ExecuteNonQuery();
                    }

                    if (report.Charges != null) {
                        foreach (var charge in report.Charges) {
                            using (var cmd = new SQLiteCommand(@"
                                INSERT INTO arrest_report_charges (
                                    ReportId, name, minFine, maxFine, canRevokeLicense, isArrestable,
                                    minDays, maxDays, probation, canBeWarrant, addedByReportInEdit
                                ) VALUES (
                                    @ReportId, @name, @minFine, @maxFine, @canRevokeLicense, @isArrestable,
                                    @minDays, @maxDays, @probation, @canBeWarrant, @addedByReportInEdit
                                )", connection, transaction)) {
                                cmd.Parameters.AddWithValue("@ReportId", report.Id);
                                cmd.Parameters.AddWithValue("@name", (object)charge.name ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@minFine", charge.minFine);
                                cmd.Parameters.AddWithValue("@maxFine", charge.maxFine);
                                cmd.Parameters.AddWithValue("@canRevokeLicense", charge.canRevokeLicense ? 1 : 0);
                                cmd.Parameters.AddWithValue("@isArrestable", charge.isArrestable ? 1 : 0);
                                cmd.Parameters.AddWithValue("@minDays", charge.minDays);
                                cmd.Parameters.AddWithValue("@maxDays", charge.maxDays.HasValue ? (object)charge.maxDays.Value : DBNull.Value);
                                cmd.Parameters.AddWithValue("@probation", charge.probation);
                                cmd.Parameters.AddWithValue("@canBeWarrant", charge.canBeWarrant ? 1 : 0);
                                cmd.Parameters.AddWithValue("@addedByReportInEdit", charge.addedByReportInEdit ? 1 : 0);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }

                    transaction.Commit();
                }
            }
        }

        internal static void SaveImpoundReport(ImpoundReport report) {
            if (report?.Id == null) return;

            lock (dbLock) {
                if (connection == null) return;

                using (var transaction = connection.BeginTransaction()) {
                    using (var cmd = new SQLiteCommand(@"
                        INSERT OR REPLACE INTO impound_reports (
                            Id, ShortYear, OfficerFirstName, OfficerLastName, OfficerRank,
                            OfficerCallSign, OfficerAgency, OfficerBadgeNumber,
                            LocationArea, LocationStreet, LocationCounty, LocationPostal,
                            TimeStamp, Status, Notes,
                            LicensePlate, VehicleModel, Owner, PersonAtFaultName, Vin, ImpoundReason, TowCompany, ImpoundLot
                        ) VALUES (
                            @Id, @ShortYear, @OfficerFirstName, @OfficerLastName, @OfficerRank,
                            @OfficerCallSign, @OfficerAgency, @OfficerBadgeNumber,
                            @LocationArea, @LocationStreet, @LocationCounty, @LocationPostal,
                            @TimeStamp, @Status, @Notes,
                            @LicensePlate, @VehicleModel, @Owner, @PersonAtFaultName, @Vin, @ImpoundReason, @TowCompany, @ImpoundLot
                        )", connection, transaction)) {
                        AddReportBaseParams(cmd, report);
                        cmd.Parameters.AddWithValue("@LicensePlate", (object)report.LicensePlate ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@VehicleModel", (object)report.VehicleModel ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Owner", (object)report.Owner ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@PersonAtFaultName", (object)report.PersonAtFaultName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Vin", (object)report.Vin ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ImpoundReason", (object)report.ImpoundReason ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@TowCompany", (object)report.TowCompany ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ImpoundLot", (object)report.ImpoundLot ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        internal static void SaveTrafficIncidentReport(TrafficIncidentReport report) {
            if (report?.Id == null) return;
            lock (dbLock) {
                if (connection == null) return;
                using (var transaction = connection.BeginTransaction()) {
                    using (var cmd = new SQLiteCommand(@"
                        INSERT OR REPLACE INTO traffic_incident_reports (
                            Id, ShortYear, OfficerFirstName, OfficerLastName, OfficerRank,
                            OfficerCallSign, OfficerAgency, OfficerBadgeNumber,
                            LocationArea, LocationStreet, LocationCounty, LocationPostal,
                            TimeStamp, Status, Notes, DriverNames, PassengerNames, PedestrianNames,
                            VehiclePlates, VehicleModels, InjuryReported, InjuryDetails, CollisionType
                        ) VALUES (
                            @Id, @ShortYear, @OfficerFirstName, @OfficerLastName, @OfficerRank,
                            @OfficerCallSign, @OfficerAgency, @OfficerBadgeNumber,
                            @LocationArea, @LocationStreet, @LocationCounty, @LocationPostal,
                            @TimeStamp, @Status, @Notes, @DriverNames, @PassengerNames, @PedestrianNames,
                            @VehiclePlates, @VehicleModels, @InjuryReported, @InjuryDetails, @CollisionType
                        )", connection, transaction)) {
                        AddReportBaseParams(cmd, report);
                        cmd.Parameters.AddWithValue("@DriverNames", report.DriverNames != null ? JsonConvert.SerializeObject(report.DriverNames) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@PassengerNames", report.PassengerNames != null ? JsonConvert.SerializeObject(report.PassengerNames) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@PedestrianNames", report.PedestrianNames != null ? JsonConvert.SerializeObject(report.PedestrianNames) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@VehiclePlates", report.VehiclePlates != null ? JsonConvert.SerializeObject(report.VehiclePlates) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@VehicleModels", report.VehicleModels != null ? JsonConvert.SerializeObject(report.VehicleModels) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@InjuryReported", report.InjuryReported ? 1 : 0);
                        cmd.Parameters.AddWithValue("@InjuryDetails", (object)report.InjuryDetails ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@CollisionType", (object)report.CollisionType ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        internal static void SaveInjuryReport(InjuryReport report) {
            if (report?.Id == null) return;
            lock (dbLock) {
                if (connection == null) return;
                using (var transaction = connection.BeginTransaction()) {
                    using (var cmd = new SQLiteCommand(@"
                        INSERT OR REPLACE INTO injury_reports (
                            Id, ShortYear, OfficerFirstName, OfficerLastName, OfficerRank,
                            OfficerCallSign, OfficerAgency, OfficerBadgeNumber,
                            LocationArea, LocationStreet, LocationCounty, LocationPostal,
                            TimeStamp, Status, Notes, InjuredPartyName, InjuryType, Severity,
                            Treatment, IncidentContext, LinkedReportId, GameInjurySnapshot
                        ) VALUES (
                            @Id, @ShortYear, @OfficerFirstName, @OfficerLastName, @OfficerRank,
                            @OfficerCallSign, @OfficerAgency, @OfficerBadgeNumber,
                            @LocationArea, @LocationStreet, @LocationCounty, @LocationPostal,
                            @TimeStamp, @Status, @Notes, @InjuredPartyName, @InjuryType, @Severity,
                            @Treatment, @IncidentContext, @LinkedReportId, @GameInjurySnapshot
                        )", connection, transaction)) {
                        AddReportBaseParams(cmd, report);
                        cmd.Parameters.AddWithValue("@InjuredPartyName", (object)report.InjuredPartyName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@InjuryType", (object)report.InjuryType ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Severity", (object)report.Severity ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Treatment", (object)report.Treatment ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@IncidentContext", (object)report.IncidentContext ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@LinkedReportId", (object)report.LinkedReportId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@GameInjurySnapshot", (object)report.GameInjurySnapshot ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        internal static void SavePropertyEvidenceReceiptReport(PropertyEvidenceReceiptReport report) {
            if (report?.Id == null) return;
            lock (dbLock) {
                if (connection == null) return;
                using (var transaction = connection.BeginTransaction()) {
                    using (var cmd = new SQLiteCommand(@"
                        INSERT OR REPLACE INTO property_evidence_reports (
                            Id, ShortYear, OfficerFirstName, OfficerLastName, OfficerRank,
                            OfficerCallSign, OfficerAgency, OfficerBadgeNumber,
                            LocationArea, LocationStreet, LocationCounty, LocationPostal,
                            TimeStamp, Status, Notes, SubjectPedName, SubjectPedNames, SeizedDrugTypes, SeizedDrugs, SeizedFirearmTypes, OtherContrabandNotes
                        ) VALUES (
                            @Id, @ShortYear, @OfficerFirstName, @OfficerLastName, @OfficerRank,
                            @OfficerCallSign, @OfficerAgency, @OfficerBadgeNumber,
                            @LocationArea, @LocationStreet, @LocationCounty, @LocationPostal,
                            @TimeStamp, @Status, @Notes, @SubjectPedName, @SubjectPedNames, @SeizedDrugTypes, @SeizedDrugs, @SeizedFirearmTypes, @OtherContrabandNotes
                        )", connection, transaction)) {
                        AddReportBaseParams(cmd, report);
                        cmd.Parameters.AddWithValue("@SubjectPedName", (object)report.SubjectPedName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@SubjectPedNames", report.SubjectPedNames != null && report.SubjectPedNames.Count > 0 ? JsonConvert.SerializeObject(report.SubjectPedNames) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@SeizedDrugTypes", report.SeizedDrugTypes != null && report.SeizedDrugTypes.Count > 0 ? JsonConvert.SerializeObject(report.SeizedDrugTypes) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@SeizedDrugs", report.SeizedDrugs != null && report.SeizedDrugs.Count > 0 ? JsonConvert.SerializeObject(report.SeizedDrugs) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@SeizedFirearmTypes", report.SeizedFirearmTypes != null && report.SeizedFirearmTypes.Count > 0 ? JsonConvert.SerializeObject(report.SeizedFirearmTypes) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@OtherContrabandNotes", (object)report.OtherContrabandNotes ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        private static void WriteReportBase(string tableName, Report report, SQLiteTransaction transaction) {
            using (var cmd = new SQLiteCommand($@"
                INSERT OR REPLACE INTO {tableName} (
                    Id, ShortYear, OfficerFirstName, OfficerLastName, OfficerRank,
                    OfficerCallSign, OfficerAgency, OfficerBadgeNumber,
                    LocationArea, LocationStreet, LocationCounty, LocationPostal,
                    TimeStamp, Status, Notes
                ) VALUES (
                    @Id, @ShortYear, @OfficerFirstName, @OfficerLastName, @OfficerRank,
                    @OfficerCallSign, @OfficerAgency, @OfficerBadgeNumber,
                    @LocationArea, @LocationStreet, @LocationCounty, @LocationPostal,
                    @TimeStamp, @Status, @Notes
                )", connection, transaction)) {
                AddReportBaseParams(cmd, report);
                cmd.ExecuteNonQuery();
            }
        }

        private static void AddReportBaseParams(SQLiteCommand cmd, Report report) {
            cmd.Parameters.AddWithValue("@Id", report.Id);
            cmd.Parameters.AddWithValue("@ShortYear", report.ShortYear);
            cmd.Parameters.AddWithValue("@OfficerFirstName", (object)report.OfficerInformation?.firstName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OfficerLastName", (object)report.OfficerInformation?.lastName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OfficerRank", (object)report.OfficerInformation?.rank ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OfficerCallSign", (object)report.OfficerInformation?.callSign ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OfficerAgency", (object)report.OfficerInformation?.agency ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OfficerBadgeNumber", report.OfficerInformation?.badgeNumber.HasValue == true ? (object)report.OfficerInformation.badgeNumber.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationArea", (object)report.Location?.Area ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationStreet", (object)report.Location?.Street ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationCounty", (object)report.Location?.County ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationPostal", (object)report.Location?.Postal ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TimeStamp", report.TimeStamp.ToString("o"));
            cmd.Parameters.AddWithValue("@Status", (int)report.Status);
            cmd.Parameters.AddWithValue("@Notes", (object)report.Notes ?? DBNull.Value);
        }

        #endregion

        #region Search History

        internal static void SaveSearchHistoryEntry(string searchType, string query, string resultName) {
            lock (dbLock) {
                if (connection == null) return;

                using (var cmd = new SQLiteCommand(@"
                    INSERT INTO search_history (SearchType, SearchQuery, ResultName, Timestamp)
                    VALUES (@type, @query, @result, @ts)", connection)) {
                    cmd.Parameters.AddWithValue("@type", searchType);
                    cmd.Parameters.AddWithValue("@query", query);
                    cmd.Parameters.AddWithValue("@result", (object)resultName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        internal static List<SearchHistoryEntry> LoadSearchHistory(string searchType, int limit = 10) {
            lock (dbLock) {
                if (connection == null) return new List<SearchHistoryEntry>();

                var entries = new List<SearchHistoryEntry>();

                using (var cmd = new SQLiteCommand(@"
                    SELECT ResultName, Timestamp AS LastSearched
                    FROM search_history
                    WHERE SearchType = @type AND ResultName IS NOT NULL
                    ORDER BY Timestamp DESC
                    LIMIT @limit", connection)) {
                    cmd.Parameters.AddWithValue("@type", searchType);
                    cmd.Parameters.AddWithValue("@limit", limit);

                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            entries.Add(new SearchHistoryEntry {
                                ResultName = reader["ResultName"] as string,
                                LastSearched = reader["LastSearched"] as string,
                                SearchCount = 1
                            });
                        }
                    }
                }

                return entries;
            }
        }

        internal static void ClearSearchHistory(string searchType) {
            lock (dbLock) {
                if (connection == null) return;
                using (var cmd = new SQLiteCommand("DELETE FROM search_history WHERE SearchType = @type", connection)) {
                    cmd.Parameters.AddWithValue("@type", searchType);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        #endregion

        #region Firearm Records

        internal static void SaveFirearmRecords(List<FirearmRecord> records) {
            if (records == null || records.Count == 0) return;
            foreach (var r in records) UpsertFirearmRecord(r);
        }

        /// <summary>Upserts a single firearm. Matches existing by OwnerPedName + SerialNumber/empty + WeaponModelHash; updates LastSeenAt or inserts new.</summary>
        internal static void UpsertFirearmRecord(FirearmRecord r) {
            if (r == null || string.IsNullOrWhiteSpace(r.OwnerPedName)) return;
            lock (dbLock) {
                if (connection == null) return;
                string now = DateTime.UtcNow.ToString("o");
                r.FirstSeenAt = r.FirstSeenAt ?? now;
                r.LastSeenAt = r.LastSeenAt ?? now;
                string serial = r.SerialNumber ?? "";
                int? existingId = null;
                using (var sel = new SQLiteCommand(@"
                    SELECT Id FROM firearm_records WHERE OwnerPedName = @owner AND (SerialNumber = @serial OR (SerialNumber IS NULL AND @serial = '')) AND WeaponModelHash = @hash LIMIT 1
                ", connection)) {
                    sel.Parameters.AddWithValue("@owner", r.OwnerPedName);
                    sel.Parameters.AddWithValue("@serial", serial);
                    sel.Parameters.AddWithValue("@hash", (int)r.WeaponModelHash);
                    object o = sel.ExecuteScalar();
                    if (o != null) existingId = Convert.ToInt32(o);
                }
                if (existingId.HasValue) {
                    using (var cmd = new SQLiteCommand("UPDATE firearm_records SET IsStolen = @stolen, IsSerialScratched = @scratch, Description = COALESCE(@desc, Description), WeaponDisplayName = COALESCE(@displayName, WeaponDisplayName), LastSeenAt = @last WHERE Id = @id", connection)) {
                        cmd.Parameters.AddWithValue("@stolen", r.IsStolen ? 1 : 0);
                        cmd.Parameters.AddWithValue("@scratch", r.IsSerialScratched ? 1 : 0);
                        cmd.Parameters.AddWithValue("@desc", (object)r.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@displayName", (object)r.WeaponDisplayName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@last", r.LastSeenAt);
                        cmd.Parameters.AddWithValue("@id", existingId.Value);
                        cmd.ExecuteNonQuery();
                    }
                } else {
                    using (var cmd = new SQLiteCommand(@"
                        INSERT INTO firearm_records (SerialNumber, IsSerialScratched, OwnerPedName, WeaponModelId, WeaponDisplayName, WeaponModelHash, IsStolen, Description, Source, FirstSeenAt, LastSeenAt)
                        VALUES (@serial, @scratch, @owner, @modelId, @displayName, @modelHash, @stolen, @desc, @src, @first, @last)
                    ", connection)) {
                        cmd.Parameters.AddWithValue("@serial", string.IsNullOrEmpty(serial) ? DBNull.Value : (object)serial);
                        cmd.Parameters.AddWithValue("@scratch", r.IsSerialScratched ? 1 : 0);
                        cmd.Parameters.AddWithValue("@owner", r.OwnerPedName);
                        cmd.Parameters.AddWithValue("@modelId", (object)r.WeaponModelId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@displayName", (object)r.WeaponDisplayName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@modelHash", (int)r.WeaponModelHash);
                        cmd.Parameters.AddWithValue("@stolen", r.IsStolen ? 1 : 0);
                        cmd.Parameters.AddWithValue("@desc", (object)r.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@src", (object)r.Source ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@first", r.FirstSeenAt);
                        cmd.Parameters.AddWithValue("@last", r.LastSeenAt);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        /// <summary>Loads firearms by owner name. Uses case-insensitive matching so "John Doe" matches "john doe".</summary>
        internal static List<FirearmRecord> LoadFirearmsByOwner(string ownerPedName, int limit = 50) {
            lock (dbLock) {
                if (connection == null) return new List<FirearmRecord>();
                string owner = (ownerPedName ?? "").Trim();
                if (string.IsNullOrEmpty(owner)) return new List<FirearmRecord>();
                var list = new List<FirearmRecord>();
                using (var cmd = new SQLiteCommand(@"
                    SELECT Id, SerialNumber, IsSerialScratched, OwnerPedName, WeaponModelId, WeaponDisplayName, WeaponModelHash, IsStolen, Description, Source, FirstSeenAt, LastSeenAt
                    FROM firearm_records WHERE OwnerPedName IS NOT NULL AND OwnerPedName != '' AND LOWER(TRIM(OwnerPedName)) = LOWER(@owner) ORDER BY LastSeenAt DESC LIMIT @limit
                ", connection)) {
                    cmd.Parameters.AddWithValue("@owner", owner);
                    cmd.Parameters.AddWithValue("@limit", Math.Max(1, Math.Min(limit, 100)));
                    using (var rdr = cmd.ExecuteReader()) {
                        while (rdr.Read())
                            list.Add(ReadFirearmFromReader(rdr));
                    }
                }
                return list;
            }
        }

        /// <summary>Loads a firearm by serial number. Returns null for scratched-serial firearms (cannot be looked up).</summary>
        internal static FirearmRecord LoadFirearmBySerial(string serialNumber) {
            if (string.IsNullOrWhiteSpace(serialNumber)) return null;
            lock (dbLock) {
                if (connection == null) return null;
                using (var cmd = new SQLiteCommand(@"
                    SELECT Id, SerialNumber, IsSerialScratched, OwnerPedName, WeaponModelId, WeaponDisplayName, WeaponModelHash, IsStolen, Description, Source, FirstSeenAt, LastSeenAt
                    FROM firearm_records WHERE SerialNumber IS NOT NULL AND SerialNumber = @serial ORDER BY LastSeenAt DESC LIMIT 1
                ", connection)) {
                    cmd.Parameters.AddWithValue("@serial", serialNumber.Trim());
                    using (var rdr = cmd.ExecuteReader()) {
                        if (rdr.Read()) return ReadFirearmFromReader(rdr);
                    }
                }
                return null;
            }
        }

        /// <summary>Updates LastSeenAt to now for all firearm records with this owner. Call when owner's firearms are viewed (Person Search or Firearms Search) so they appear in Recent IDs.</summary>
        internal static void TouchFirearmRecordsByOwner(string ownerPedName) {
            if (string.IsNullOrWhiteSpace(ownerPedName)) return;
            lock (dbLock) {
                if (connection == null) return;
                string now = DateTime.UtcNow.ToString("o");
                using (var cmd = new SQLiteCommand(@"
                    UPDATE firearm_records SET LastSeenAt = @now
                    WHERE OwnerPedName IS NOT NULL AND OwnerPedName != '' AND LOWER(TRIM(OwnerPedName)) = LOWER(TRIM(@owner))
                ", connection)) {
                    cmd.Parameters.AddWithValue("@now", now);
                    cmd.Parameters.AddWithValue("@owner", ownerPedName.Trim());
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Returns recent owners (distinct OwnerPedName) ordered by latest LastSeenAt.</summary>
        internal static List<string> LoadRecentFirearmOwnerNames(int limit = 12) {
            lock (dbLock) {
                if (connection == null) return new List<string>();
                var list = new List<string>();
                using (var cmd = new SQLiteCommand(@"
                    SELECT OwnerPedName FROM firearm_records
                    WHERE OwnerPedName IS NOT NULL AND OwnerPedName != ''
                    GROUP BY OwnerPedName ORDER BY MAX(LastSeenAt) DESC LIMIT @limit
                ", connection)) {
                    cmd.Parameters.AddWithValue("@limit", limit);
                    using (var rdr = cmd.ExecuteReader()) {
                        while (rdr.Read()) {
                            string n = rdr["OwnerPedName"] as string;
                            if (!string.IsNullOrEmpty(n)) list.Add(n);
                        }
                    }
                }
                return list;
            }
        }

        /// <summary>Returns recent firearm records (CDF/PR) ordered by LastSeenAt for Firearms Check "Recent weapons" — serials and weapon names for lookup.</summary>
        internal static List<FirearmRecord> LoadRecentFirearms(int limit = 12) {
            lock (dbLock) {
                if (connection == null) return new List<FirearmRecord>();
                var list = new List<FirearmRecord>();
                using (var cmd = new SQLiteCommand(@"
                    SELECT Id, SerialNumber, IsSerialScratched, OwnerPedName, WeaponModelId, WeaponDisplayName, WeaponModelHash, IsStolen, Description, Source, FirstSeenAt, LastSeenAt
                    FROM firearm_records ORDER BY LastSeenAt DESC LIMIT @limit
                ", connection)) {
                    cmd.Parameters.AddWithValue("@limit", limit);
                    using (var rdr = cmd.ExecuteReader()) {
                        while (rdr.Read()) list.Add(ReadFirearmFromReader(rdr));
                    }
                }
                return list;
            }
        }

        private static FirearmRecord ReadFirearmFromReader(SQLiteDataReader rdr) {
            return new FirearmRecord {
                Id = rdr.GetInt32(rdr.GetOrdinal("Id")),
                SerialNumber = rdr["SerialNumber"] as string,
                IsSerialScratched = Convert.ToInt32(rdr["IsSerialScratched"] ?? 0) != 0,
                OwnerPedName = rdr["OwnerPedName"] as string ?? "",
                WeaponModelId = rdr["WeaponModelId"] as string,
                WeaponDisplayName = rdr["WeaponDisplayName"] as string,
                WeaponModelHash = ReadUInt32FromReader(rdr["WeaponModelHash"]),
                IsStolen = Convert.ToInt32(rdr["IsStolen"] ?? 0) != 0,
                Description = rdr["Description"] as string,
                Source = rdr["Source"] as string,
                FirstSeenAt = rdr["FirstSeenAt"] as string,
                LastSeenAt = rdr["LastSeenAt"] as string
            };
        }

        #endregion

        #region Drug Records

        internal static void SaveDrugRecords(List<DrugRecord> records) {
            if (records == null || records.Count == 0) return;
            foreach (var r in records) UpsertDrugRecord(r);
        }

        private static void UpsertDrugRecord(DrugRecord r) {
            if (r == null || string.IsNullOrWhiteSpace(r.OwnerPedName)) return;
            lock (dbLock) {
                if (connection == null) return;
                string now = DateTime.UtcNow.ToString("o");
                r.FirstSeenAt = r.FirstSeenAt ?? now;
                r.LastSeenAt = r.LastSeenAt ?? now;
                int? existingId = null;
                using (var sel = new SQLiteCommand(@"
                    SELECT Id FROM drug_records WHERE OwnerPedName = @owner AND DrugType = @drugType AND Source = @src LIMIT 1
                ", connection)) {
                    sel.Parameters.AddWithValue("@owner", r.OwnerPedName);
                    sel.Parameters.AddWithValue("@drugType", (object)r.DrugType ?? DBNull.Value);
                    sel.Parameters.AddWithValue("@src", (object)r.Source ?? DBNull.Value);
                    object o = sel.ExecuteScalar();
                    if (o != null) existingId = Convert.ToInt32(o);
                }
                if (existingId.HasValue) {
                    using (var cmd = new SQLiteCommand("UPDATE drug_records SET LastSeenAt = @last WHERE Id = @id", connection)) {
                        cmd.Parameters.AddWithValue("@last", r.LastSeenAt);
                        cmd.Parameters.AddWithValue("@id", existingId.Value);
                        cmd.ExecuteNonQuery();
                    }
                } else {
                    using (var cmd = new SQLiteCommand(@"
                        INSERT INTO drug_records (OwnerPedName, DrugType, DrugCategory, Description, Source, FirstSeenAt, LastSeenAt)
                        VALUES (@owner, @drugType, @category, @desc, @src, @first, @last)
                    ", connection)) {
                        cmd.Parameters.AddWithValue("@owner", r.OwnerPedName);
                        cmd.Parameters.AddWithValue("@drugType", (object)r.DrugType ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@category", (object)r.DrugCategory ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@desc", (object)r.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@src", (object)r.Source ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@first", r.FirstSeenAt);
                        cmd.Parameters.AddWithValue("@last", r.LastSeenAt);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        internal static List<DrugRecord> LoadDrugsByOwner(string ownerPedName, int limit = 50) {
            if (string.IsNullOrWhiteSpace(ownerPedName)) return new List<DrugRecord>();
            lock (dbLock) {
                if (connection == null) return new List<DrugRecord>();
                var list = new List<DrugRecord>();
                string owner = ownerPedName.Trim();
                using (var cmd = new SQLiteCommand(@"
                    SELECT Id, OwnerPedName, DrugType, DrugCategory, Description, Source, FirstSeenAt, LastSeenAt
                    FROM drug_records WHERE LOWER(OwnerPedName) = LOWER(@owner) ORDER BY LastSeenAt DESC LIMIT @limit
                ", connection)) {
                    cmd.Parameters.AddWithValue("@owner", owner);
                    cmd.Parameters.AddWithValue("@limit", limit);
                    using (var rdr = cmd.ExecuteReader()) {
                        while (rdr.Read())
                            list.Add(new DrugRecord {
                                Id = rdr.GetInt32(rdr.GetOrdinal("Id")),
                                OwnerPedName = rdr["OwnerPedName"] as string ?? "",
                                DrugType = rdr["DrugType"] as string,
                                DrugCategory = rdr["DrugCategory"] as string,
                                Description = rdr["Description"] as string,
                                Source = rdr["Source"] as string,
                                FirstSeenAt = rdr["FirstSeenAt"] as string,
                                LastSeenAt = rdr["LastSeenAt"] as string
                            });
                    }
                }
                return list;
            }
        }

        #endregion

        #region Vehicle Search Records

        internal static void DeleteVehicleSearchRecordsByPlate(string licensePlate) {
            if (string.IsNullOrWhiteSpace(licensePlate)) return;
            lock (dbLock) {
                if (connection == null) return;
                try {
                    using (var cmd = new SQLiteCommand("DELETE FROM vehicle_search_records WHERE LOWER(TRIM(LicensePlate)) = LOWER(@plate)", connection)) {
                        cmd.Parameters.AddWithValue("@plate", licensePlate.Trim());
                        cmd.ExecuteNonQuery();
                    }
                } catch { }
            }
        }

        internal static void SaveVehicleSearchRecords(List<VehicleSearchRecord> records) {
            if (records == null || records.Count == 0) return;
            lock (dbLock) {
                if (connection == null) return;
                foreach (var r in records) {
                    if (string.IsNullOrWhiteSpace(r.LicensePlate)) continue;
                    using (var cmd = new SQLiteCommand(@"
                        INSERT INTO vehicle_search_records (LicensePlate, ItemType, DrugType, ItemLocation, Description, WeaponModelHash, WeaponModelId, Source, CapturedAt)
                        VALUES (@plate, @itemType, @drugType, @location, @desc, @hash, @modelId, @src, @captured)
                    ", connection)) {
                        cmd.Parameters.AddWithValue("@plate", (object)r.LicensePlate ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@itemType", (object)r.ItemType ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@drugType", (object)r.DrugType ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@location", (object)r.ItemLocation ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@desc", (object)r.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@hash", (int)r.WeaponModelHash);
                        cmd.Parameters.AddWithValue("@modelId", (object)r.WeaponModelId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@src", (object)r.Source ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@captured", (object)r.CapturedAt ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        internal static List<VehicleSearchRecord> LoadVehicleSearchRecordsByPlate(string licensePlate, int limit = 100) {
            if (string.IsNullOrWhiteSpace(licensePlate)) return new List<VehicleSearchRecord>();
            string plateTrim = licensePlate.Trim();
            if (string.IsNullOrEmpty(plateTrim)) return new List<VehicleSearchRecord>();
            lock (dbLock) {
                if (connection == null) return new List<VehicleSearchRecord>();
                var list = new List<VehicleSearchRecord>();
                using (var cmd = new SQLiteCommand(@"
                    SELECT Id, LicensePlate, ItemType, DrugType, ItemLocation, Description, WeaponModelHash, WeaponModelId, Source, CapturedAt
                    FROM vehicle_search_records WHERE LOWER(TRIM(LicensePlate)) = LOWER(@plate) ORDER BY CapturedAt DESC LIMIT @limit
                ", connection)) {
                    cmd.Parameters.AddWithValue("@plate", plateTrim);
                    cmd.Parameters.AddWithValue("@limit", limit);
                    using (var rdr = cmd.ExecuteReader()) {
                        while (rdr.Read()) {
                            list.Add(new VehicleSearchRecord {
                                Id = rdr.GetInt32(rdr.GetOrdinal("Id")),
                                LicensePlate = rdr["LicensePlate"] as string ?? "",
                                ItemType = rdr["ItemType"] as string,
                                DrugType = rdr["DrugType"] as string,
                                ItemLocation = rdr["ItemLocation"] as string,
                                Description = rdr["Description"] as string,
                                WeaponModelHash = ReadUInt32FromReader(rdr["WeaponModelHash"]),
                                WeaponModelId = rdr["WeaponModelId"] as string,
                                Source = rdr["Source"] as string,
                                CapturedAt = rdr["CapturedAt"] as string
                            });
                        }
                    }
                }
                return list;
            }
        }

        #endregion

        #region Migration

        private static bool HasLegacyJsonFiles() {
            return File.Exists(SetupController.PedDataPath)
                || File.Exists(SetupController.VehicleDataPath)
                || File.Exists(SetupController.CourtDataPath)
                || File.Exists(SetupController.ShiftHistoryDataPath)
                || File.Exists(SetupController.OfficerInformationDataPath)
                || File.Exists(SetupController.IncidentReportsPath)
                || File.Exists(SetupController.CitationReportsPath)
                || File.Exists(SetupController.ArrestReportsPath);
        }

        private static void MigrateFromJson() {
            Helper.Log("Starting JSON to SQLite migration...");

            using (var transaction = connection.BeginTransaction()) {
                try {
                    MigrateFile(SetupController.PedDataPath, (List<MDTProPedData> peds) => {
                        foreach (var ped in peds) {
                            if (ped?.Name == null) continue;
                            SavePedInternal(ped, transaction);
                        }
                    });

                    MigrateFile(SetupController.VehicleDataPath, (List<MDTProVehicleData> vehicles) => {
                        foreach (var vehicle in vehicles) {
                            if (vehicle?.LicensePlate == null) continue;
                            SaveVehicleInternal(vehicle, transaction);
                        }
                    });

                    MigrateFile(SetupController.CourtDataPath, (List<CourtData> cases) => {
                        foreach (var courtCase in cases) {
                            if (courtCase?.Number == null) continue;
                            SaveCourtCaseInternal(courtCase, transaction);
                        }
                    });

                    MigrateFile(SetupController.OfficerInformationDataPath, (OfficerInformationData officer) => {
                        if (officer == null) return;
                        using (var cmd = new SQLiteCommand(@"
                            INSERT OR REPLACE INTO officer_information (Id, firstName, lastName, rank, callSign, agency, badgeNumber)
                            VALUES (1, @firstName, @lastName, @rank, @callSign, @agency, @badgeNumber)",
                            connection, transaction)) {
                            cmd.Parameters.AddWithValue("@firstName", (object)officer.firstName ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@lastName", (object)officer.lastName ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@rank", (object)officer.rank ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@callSign", (object)officer.callSign ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@agency", (object)officer.agency ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@badgeNumber", officer.badgeNumber.HasValue ? (object)officer.badgeNumber.Value : DBNull.Value);
                            cmd.ExecuteNonQuery();
                        }
                    });

                    MigrateFile(SetupController.ShiftHistoryDataPath, (List<ShiftData> shifts) => {
                        foreach (var shift in shifts) {
                            if (shift == null) continue;
                            SaveShiftInternal(shift, transaction);
                        }
                    });

                    MigrateFile(SetupController.IncidentReportsPath, (List<IncidentReport> reports) => {
                        foreach (var report in reports) {
                            if (report?.Id == null) continue;

                            WriteReportBase("incident_reports", report, transaction);

                            if (report.OffenderPedsNames != null) {
                                foreach (string name in report.OffenderPedsNames) {
                                    if (string.IsNullOrEmpty(name)) continue;
                                    using (var cmd = new SQLiteCommand("INSERT INTO incident_report_offenders (ReportId, PedName) VALUES (@id, @name)", connection, transaction)) {
                                        cmd.Parameters.AddWithValue("@id", report.Id);
                                        cmd.Parameters.AddWithValue("@name", name);
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                            }

                            if (report.WitnessPedsNames != null) {
                                foreach (string name in report.WitnessPedsNames) {
                                    if (string.IsNullOrEmpty(name)) continue;
                                    using (var cmd = new SQLiteCommand("INSERT INTO incident_report_witnesses (ReportId, PedName) VALUES (@id, @name)", connection, transaction)) {
                                        cmd.Parameters.AddWithValue("@id", report.Id);
                                        cmd.Parameters.AddWithValue("@name", name);
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                            }
                        }
                    });

                    MigrateFile(SetupController.CitationReportsPath, (List<CitationReport> reports) => {
                        foreach (var report in reports) {
                            if (report?.Id == null) continue;

                            using (var cmd = new SQLiteCommand(@"
                                INSERT OR REPLACE INTO citation_reports (
                                    Id, ShortYear, OfficerFirstName, OfficerLastName, OfficerRank,
                                    OfficerCallSign, OfficerAgency, OfficerBadgeNumber,
                                    LocationArea, LocationStreet, LocationCounty, LocationPostal,
                                    TimeStamp, Status, Notes, OffenderPedName,
                                    OffenderVehicleLicensePlate, CourtCaseNumber, FinalAmount
                                ) VALUES (
                                    @Id, @ShortYear, @OfficerFirstName, @OfficerLastName, @OfficerRank,
                                    @OfficerCallSign, @OfficerAgency, @OfficerBadgeNumber,
                                    @LocationArea, @LocationStreet, @LocationCounty, @LocationPostal,
                                    @TimeStamp, @Status, @Notes, @OffenderPedName,
                                    @OffenderVehicleLicensePlate, @CourtCaseNumber, @FinalAmount
                                )", connection, transaction)) {
                                AddReportBaseParams(cmd, report);
                                cmd.Parameters.AddWithValue("@OffenderPedName", (object)report.OffenderPedName ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@OffenderVehicleLicensePlate", (object)report.OffenderVehicleLicensePlate ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@CourtCaseNumber", (object)report.CourtCaseNumber ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@FinalAmount", report.FinalAmount.HasValue ? (object)report.FinalAmount.Value : DBNull.Value);
                                cmd.ExecuteNonQuery();
                            }

                            if (report.Charges != null) {
                                foreach (var charge in report.Charges) {
                                    using (var cmd = new SQLiteCommand(@"
                                        INSERT INTO citation_report_charges (ReportId, name, minFine, maxFine, canRevokeLicense, isArrestable, addedByReportInEdit)
                                        VALUES (@ReportId, @name, @minFine, @maxFine, @canRevokeLicense, @isArrestable, @addedByReportInEdit)",
                                        connection, transaction)) {
                                        cmd.Parameters.AddWithValue("@ReportId", report.Id);
                                        cmd.Parameters.AddWithValue("@name", (object)charge.name ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@minFine", charge.minFine);
                                        cmd.Parameters.AddWithValue("@maxFine", charge.maxFine);
                                        cmd.Parameters.AddWithValue("@canRevokeLicense", charge.canRevokeLicense ? 1 : 0);
                                        cmd.Parameters.AddWithValue("@isArrestable", charge.isArrestable ? 1 : 0);
                                        cmd.Parameters.AddWithValue("@addedByReportInEdit", charge.addedByReportInEdit ? 1 : 0);
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                            }
                        }
                    });

                    MigrateFile(SetupController.ArrestReportsPath, (List<ArrestReport> reports) => {
                        foreach (var report in reports) {
                            if (report?.Id == null) continue;

                            using (var cmd = new SQLiteCommand(@"
                                INSERT OR REPLACE INTO arrest_reports (
                                    Id, ShortYear, OfficerFirstName, OfficerLastName, OfficerRank,
                                    OfficerCallSign, OfficerAgency, OfficerBadgeNumber,
                                    LocationArea, LocationStreet, LocationCounty, LocationPostal,
                                    TimeStamp, Status, Notes, OffenderPedName,
                                    OffenderVehicleLicensePlate, CourtCaseNumber
                                ) VALUES (
                                    @Id, @ShortYear, @OfficerFirstName, @OfficerLastName, @OfficerRank,
                                    @OfficerCallSign, @OfficerAgency, @OfficerBadgeNumber,
                                    @LocationArea, @LocationStreet, @LocationCounty, @LocationPostal,
                                    @TimeStamp, @Status, @Notes, @OffenderPedName,
                                    @OffenderVehicleLicensePlate, @CourtCaseNumber
                                )", connection, transaction)) {
                                AddReportBaseParams(cmd, report);
                                cmd.Parameters.AddWithValue("@OffenderPedName", (object)report.OffenderPedName ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@OffenderVehicleLicensePlate", (object)report.OffenderVehicleLicensePlate ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@CourtCaseNumber", (object)report.CourtCaseNumber ?? DBNull.Value);
                                cmd.ExecuteNonQuery();
                            }

                            if (report.Charges != null) {
                                foreach (var charge in report.Charges) {
                                    using (var cmd = new SQLiteCommand(@"
                                        INSERT INTO arrest_report_charges (
                                            ReportId, name, minFine, maxFine, canRevokeLicense, isArrestable,
                                            minDays, maxDays, probation, canBeWarrant, addedByReportInEdit
                                        ) VALUES (
                                            @ReportId, @name, @minFine, @maxFine, @canRevokeLicense, @isArrestable,
                                            @minDays, @maxDays, @probation, @canBeWarrant, @addedByReportInEdit
                                        )", connection, transaction)) {
                                        cmd.Parameters.AddWithValue("@ReportId", report.Id);
                                        cmd.Parameters.AddWithValue("@name", (object)charge.name ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@minFine", charge.minFine);
                                        cmd.Parameters.AddWithValue("@maxFine", charge.maxFine);
                                        cmd.Parameters.AddWithValue("@canRevokeLicense", charge.canRevokeLicense ? 1 : 0);
                                        cmd.Parameters.AddWithValue("@isArrestable", charge.isArrestable ? 1 : 0);
                                        cmd.Parameters.AddWithValue("@minDays", charge.minDays);
                                        cmd.Parameters.AddWithValue("@maxDays", charge.maxDays.HasValue ? (object)charge.maxDays.Value : DBNull.Value);
                                        cmd.Parameters.AddWithValue("@probation", charge.probation);
                                        cmd.Parameters.AddWithValue("@canBeWarrant", charge.canBeWarrant ? 1 : 0);
                                        cmd.Parameters.AddWithValue("@addedByReportInEdit", charge.addedByReportInEdit ? 1 : 0);
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                            }
                        }
                    });

                    transaction.Commit();
                    Helper.Log("JSON to SQLite migration completed successfully");
                } catch (Exception e) {
                    transaction.Rollback();
                    Helper.Log($"JSON to SQLite migration failed: {e.Message}", true, Helper.LogSeverity.Error);
                    return;
                }
            }

            RenameJsonFile(SetupController.PedDataPath);
            RenameJsonFile(SetupController.VehicleDataPath);
            RenameJsonFile(SetupController.CourtDataPath);
            RenameJsonFile(SetupController.ShiftHistoryDataPath);
            RenameJsonFile(SetupController.OfficerInformationDataPath);
            RenameJsonFile(SetupController.IncidentReportsPath);
            RenameJsonFile(SetupController.CitationReportsPath);
            RenameJsonFile(SetupController.ArrestReportsPath);
        }

        private static void MigrateFile<T>(string path, Action<T> importAction) where T : new() {
            if (!File.Exists(path)) return;

            T data;
            try {
                data = Helper.ReadFromJsonFile<T>(path);
            } catch (Exception e) {
                Helper.Log($"Failed to read {Path.GetFileName(path)} for migration: {e.Message}", false, Helper.LogSeverity.Warning);
                return;
            }

            if (data != null) {
                importAction(data);
            }
            Helper.Log($"Migrated {Path.GetFileName(path)}");
        }

        private static void RenameJsonFile(string path) {
            if (!File.Exists(path)) return;

            try {
                string backupPath = path + ".bak";
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Move(path, backupPath);
            } catch (Exception e) {
                Helper.Log($"Failed to rename {Path.GetFileName(path)}: {e.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        #endregion
    }
}
