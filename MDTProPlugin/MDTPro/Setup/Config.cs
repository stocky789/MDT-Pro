// Ignore Spelling: Taskbar

namespace MDTPro.Setup {
    internal class Config {
        public int port = 8080;
        public int maxNumberOfNearbyPedsOrVehicles = 15;
        public int databaseLimitMultiplier = 10;
        public int webSocketUpdateInterval = 100;
        public int databaseUpdateInterval = 15000;
        public bool updateDomWithLanguageOnLoad = false;
        public bool useInGameTime = false;
        public bool showSecondsInTaskbarClock = false;
        public int initialWindowWidth = 600;
        public int initialWindowHeight = 400;
        public float hasPriorCitationsProbability = 0.8f;
        public float hasPriorArrestsProbability = 0.2f;
        public float hasPriorArrestsWithWarrantProbability = 0.8f;
        public float reEncounterChance = 0.08f;
        public int maxNumberOfPriorCitations = 5;
        public int maxNumberOfPriorArrests = 3;
        public int maxNumberOfPriorArrestsWithWarrant = 8;

        // available: type (reportId only), year, shortYear, month, day, index
        // reportIds/courtCaseNumbers must be unique, to achieve this year/shortYear and index must be included
        public string reportIdFormat = "{type}-{shortYear}-{index}"; 
        public int reportIdIndexPad = 6;
        public string courtCaseNumberFormat = "{shortYear}-{index}";
        public int courtCaseNumberIndexPad = 6;

        public bool displayCurrencySymbolBeforeNumber = true;
        public int courtDatabaseMaxEntries = 100;
        public int courtRosterRotationDays = 14;
        public int courtJurySeverityThreshold = 15;
        public float courtPriorCitationWeight = 1f;
        public float courtPriorArrestWeight = 2f;
        public float courtPriorConvictionWeight = 3f;
        public float courtProbationWeight = 2f;
        public float courtParoleWeight = 2f;
        public float courtWantedWeight = 1f;
        public float courtRecentConvictionWindowDays = 180f;
        public float courtRecentConvictionBonusWeight = 1.5f;
        public float courtEvidenceBase = 15f;
        public float courtEvidencePerCharge = 4f;
        public float courtEvidenceArrestableBonus = 5f;
        public float courtEvidenceLifeSentenceBonus = 15f;
        public float courtEvidenceMax = 95f;
        public float courtEvidenceWeaponBonus = 25f;
        public float courtEvidenceWantedBonus = 20f;
        public float courtEvidencePatDownBonus = 10f;
        public float courtEvidenceDrunkBonus = 12f;
        public float courtEvidenceFleeingBonus = 15f;
        public float courtEvidenceAssaultBonus = 20f;
        public float courtEvidenceVehicleDamageBonus = 8f;
        public float courtEvidenceIllegalWeaponBonus = 18f;
        public float courtEvidenceSupervisionViolationBonus = 22f;
        public float courtCaseResolutionMinBase = 20f;
        public float courtCaseResolutionMaxMinutes = 300f;
        public float courtCaseResolutionSeverityScale = 12f;
        public float courtParoleThresholdRealDays = 14f;
        public float courtParoleReleaseFraction = 0.6f;
        public int courtDocketWindowDays = 14;
        public float courtDocketPressureBase = 0.2f;
        public float courtDocketPressureScale = 0.6f;
        public float courtProsecutionSeverityWeight = 0.45f;
        public float courtProsecutionEvidenceWeight = 0.4f;
        public float courtProsecutionRecidivismWeight = 0.15f;
        public float courtDefensePublicDefenderBonus = 8f;
        public float courtDefensePrivateCounselBonus = 14f;

        public int mapPlayerIconSize = 30;
        public float mapTurnPenaltySecondsPerRadian = 1.0f;
        public bool mapDrawPostalCodeSet = true;
        public bool mapUseBurgerUnits = false;
        public bool mapSmoothPlayerIcon = false;

        public bool showAgencyInCalloutInfo = true;

        /// <summary>
        /// Check for updates on load via GitHub Releases. Set to false to disable.
        /// </summary>
        public bool checkForUpdates = true;

        /// <summary>
        /// GitHub repo in "owner/repo" format. Leave empty to skip update check.
        /// </summary>
        public string githubReleasesRepo = "stocky789/MDT-Pro";
    }
}
