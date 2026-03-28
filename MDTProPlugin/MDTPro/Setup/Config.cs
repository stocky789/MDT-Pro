// Ignore Spelling: Taskbar

namespace MDTPro.Setup {
    internal class Config {
        public int port = 9000;
        /// <summary>When true, show the in-game notification with MDT URLs (local IP and PC name) when the HTTP server starts. Disable for streaming to avoid exposing host/network info on screen; URLs remain in MDTPro/ipAddresses.txt and the log.</summary>
        public bool showListeningAddressNotification = true;
        /// <summary>After a StopThePed citation handoff attempt (when Policing Redefined is not handling citations), play a short vanilla GTA clipboard idle on the player for a paperwork moment. Uses amb@world_human_clipboard clips, not StopThePed internals.</summary>
        public bool stpCitationPaperworkAnimation = true;
        /// <summary>Max distance (meters) from the suspect to open the citation handoff menu or play the paperwork animation (StopThePed path).</summary>
        public float stpCitationHandoffMaxDistance = 4f;
        /// <summary>Minutes until a queued StopThePed-path citation handoff expires (0 = never).</summary>
        public int stpCitationHandoffPendingExpireMinutes = 45;
        /// <summary>When true, StopThePed-path citation notifications include a line with your MDT browser link. When false, only the short in-game message is shown.</summary>
        public bool citationStpAppendMdtBrowserLink = true;
        /// <summary>After handing a citation, show a random suspect line (subtitle) matched to charge keywords.</summary>
        public bool citationPedReactionEnabled = true;
        /// <summary>How long the suspect subtitle stays on screen (ms).</summary>
        public int citationPedReactionDurationMs = 7500;
        /// <summary>When false, only lines in each pool's "clean" list are used (no "mature" profanity lines).</summary>
        public bool citationPedReactionAllowProfanity = true;
        /// <summary>After citation handoff, small chance the suspect attacks the officer (TASK_COMBAT_PED; firearm if equipped). Works on foot or in a vehicle (ped is tasked to exit first when inside one).</summary>
        public bool citationPostHandoffViolenceEnabled = true;
        /// <summary>Base probability (0–1) before fines and charge modifiers. Typical range ~0.05–0.08.</summary>
        public float citationPostHandoffViolenceBaseChance = 0.055f;
        /// <summary>Hard cap on final roll probability (0 = no cap).</summary>
        public float citationPostHandoffViolenceMaxChance = 0.12f;
        /// <summary>Added to probability per dollar of total citation fines (e.g. 0.0000025 = +2.5% at $1000).</summary>
        public float citationPostHandoffViolenceFinePerDollar = 0.0000025f;
        /// <summary>Max extra probability from fines alone (0 = unlimited).</summary>
        public float citationPostHandoffViolenceFineBonusCap = 0.045f;
        /// <summary>Extra probability when any charge is arrestable.</summary>
        public float citationPostHandoffViolenceArrestableBonus = 0.025f;
        /// <summary>Extra probability for assault/resist/disorderly-style charge names.</summary>
        public float citationPostHandoffViolenceHostileChargeBonus = 0.018f;
        /// <summary>Extra probability when the ped is drunk (native check).</summary>
        public float citationPostHandoffViolenceDrunkBonus = 0.012f;
        /// <summary>If the ped has a firearm, probability (0–1) that the combat task equips it (else unarmed/melee).</summary>
        public float citationPostHandoffViolenceShootWhenArmedChance = 0.5f;
        /// <summary>Minimum milliseconds between violence rolls after any citation (global cooldown).</summary>
        public int citationPostHandoffViolenceCooldownMs = 45000;
        /// <summary>When true, only male suspects can roll post-citation violence. Uses CDF gender from person data when present, else the ped model&apos;s male flag.</summary>
        public bool citationPostHandoffViolenceMaleOnly = true;
        /// <summary>When resolving a firearm for the shoot roll, try CommonDataFramework <c>PedData</c> first (reflection: weapon collections / weapon hash fields). CDF versions differ; if nothing matches, native inventory is used.</summary>
        public bool citationPostHandoffViolenceTryCdfWeapon = true;
        /// <summary>When true, use Policing Redefined <c>GetPedSearchItems</c> (frisk results) before native inventory. Often empty until the ped is searched.</summary>
        public bool citationPostHandoffViolenceTryPedSearchItemsWeapon = false;
        public int maxNumberOfNearbyPedsOrVehicles = 15;
        public int databaseLimitMultiplier = 10;
        /// <summary>Milliseconds between WebSocket pushes for time, location, and map coords. Higher = less CPU; 1000 is smooth for taskbar/map.</summary>
        public int webSocketUpdateInterval = 1000;
        public int databaseUpdateInterval = 10000;
        public bool updateDomWithLanguageOnLoad = false;
        public bool useInGameTime = false;
        public bool showSecondsInTaskbarClock = false;
        public int initialWindowWidth = 600;
        public int initialWindowHeight = 400;
        // US stats: ~28% have prior citations, ~30% prior arrests, ~15% of those have warrants. Bumped for game variety.
        public float hasPriorCitationsProbability = 0.40f;
        public float hasPriorArrestsProbability = 0.40f;
        public float hasPriorArrestsWithWarrantProbability = 0.28f;
        public float reEncounterChance = 0.08f;
        /// <summary>Base chance (0-1) for vehicle re-encounter when driver is unknown. Uses reEncounterChance if &lt;= 0.</summary>
        public float reEncounterVehicleChance = 0.08f;
        /// <summary>Chance (0-1) for vehicle re-encounter when driver is a known/persistent ped (same person, same car).</summary>
        public float reEncounterVehicleChanceWhenPedKnown = 0.85f;
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
        public int courtDatabaseMaxEntries = 200;
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
        public float courtEvidenceResistedBonus = 15f;
        public float courtEvidenceDrugsBonus = 12f;
        /// <summary>Additional evidence when drug quantity is documented in seizure report. Max bonus = this value × quantity weight (0–1). Higher quantities (bundle, kilo, etc.) increase conviction likelihood.</summary>
        public float courtEvidenceDrugQuantityBonus = 6f;
        public float courtEvidenceUseOfForceBonus = 10f;
        /// <summary>Per attached incident report (evidence for court).</summary>
        public float courtEvidenceIncidentReportBonus = 10f;
        /// <summary>Per attached injury report (evidence for court).</summary>
        public float courtEvidenceInjuryReportBonus = 8f;
        /// <summary>Per attached citation report, same ped (evidence for court).</summary>
        public float courtEvidenceCitationReportBonus = 3f;
        /// <summary>Per attached traffic incident report (e.g. DUI/collision cases).</summary>
        public float courtEvidenceTrafficIncidentReportBonus = 6f;
        /// <summary>Per attached impound report (e.g. stolen recovery, evidence).</summary>
        public float courtEvidenceImpoundReportBonus = 5f;
        /// <summary>Per attached Property and Evidence Receipt (seized contraband documentation). Charge-specific drug/firearm matching handled in court integration.</summary>
        public float courtEvidencePropertyEvidenceReportBonus = 8f;
        /// <summary>Per attached seizure report (Property and Evidence Receipt). Optional bonus similar to incident/citation. Uses courtEvidencePropertyEvidenceReportBonus when 0.</summary>
        public float courtEvidenceSeizureReportBonus = 8f;
        /// <summary>Per attached report that does not meet relevance (e.g. impound on a drug case, incident that doesn't name defendant). Still counts so tangential evidence (e.g. stolen firearm in a drug case) is not ignored; just carries less weight than directly relevant reports.</summary>
        public float courtEvidenceOtherAttachedReportBonus = 3f;
        /// <summary>Bonus when primary arrest report Notes length exceeds courtEvidenceReportNotesMinLength.</summary>
        public float courtEvidenceReportNotesBonus = 8f;
        /// <summary>Minimum Notes length (chars) on arrest report to get courtEvidenceReportNotesBonus.</summary>
        public int courtEvidenceReportNotesMinLength = 100;
        /// <summary>Max conviction chance (%) for homicide charge when no death/fatal injury report is attached. 0 = no cap.</summary>
        public int courtConvictionHomicideNoDeathReportCap = 25;
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

        /// <summary>Sentence multiplier: weight for repeat offender score (prior arrests, probation, etc.). 0 = use built-in default. Rebalanced so 2.5x is rare (career criminal + serious charges).</summary>
        public float courtSentenceMultiplierRepeatWeight = 0.035f;
        /// <summary>Sentence multiplier: weight for case severity. 0 = use built-in default.</summary>
        public float courtSentenceMultiplierSeverityWeight = 0.01f;
        /// <summary>Sentence multiplier: weight for prosecution vs defense outcome. 0 = use built-in default.</summary>
        public float courtSentenceMultiplierOutcomeWeight = 0.15f;
        /// <summary>Sentence multiplier: weight for docket pressure. 0 = use built-in default.</summary>
        public float courtSentenceMultiplierDocketWeight = 0.08f;
        /// <summary>Maximum sentence multiplier cap (e.g. 2.5 = 250% of base). 0 = use built-in default (2.5).</summary>
        public float courtSentenceMultiplierMax = 2.5f;

        public int mapPlayerIconSize = 30;
        public float mapTurnPenaltySecondsPerRadian = 1.0f;
        public bool mapDrawPostalCodeSet = true;
        public bool mapUseBurgerUnits = false;
        public bool mapSmoothPlayerIcon = false;

        public bool showAgencyInCalloutInfo = true;
        /// <summary>When true, names mentioned in callout additional messages (e.g. "associated with Joe Thomas") are added as stub person records so they appear in Person Search. Callout packs that use CDF to register suspects do not need this.</summary>
        public bool addCalloutSuspectNamesFromMessages = true;

        /// <summary>
        /// Check for updates on load via GitHub Releases. Set to false to disable.
        /// </summary>
        public bool checkForUpdates = true;

        /// <summary>
        /// GitHub repo in "owner/repo" format. Leave empty to skip update check.
        /// </summary>
        public string githubReleasesRepo = "stocky789/MDT-Pro";

        /// <summary>Citation/arrest options schema version. When &lt; current, citationOptions.json and arrestOptions.json are overwritten from defaults on load so upgraders get updated charges. Bump SetupController.currentCitationArrestOptionsVersion when adding charges. Do not edit.</summary>
        public int citationArrestOptionsVersion = 2;

        // ---- ALPR ----
        /// <summary>Enable ALPR scanning and HUD in-game. Only active when in a police cruiser and on duty. Registration/insurance use live CDF documents only (aligned with Callout Interface).</summary>
        public bool alprEnabled = false;
        /// <summary>ALPR popup auto-close in MDT (seconds). 0 = no auto-close. Used by ALPR web plugin.</summary>
        public int alprPopupDuration = 0;
        /// <summary>HUD anchor: TopLeft, TopRight, BottomLeft, BottomRight.</summary>
        public string alprHudAnchor = "TopRight";
        /// <summary>HUD offset X in pixels from anchor.</summary>
        public int alprHudOffsetX = 20;
        /// <summary>HUD offset Y in pixels from anchor.</summary>
        public int alprHudOffsetY = 150;
        /// <summary>Scale factor for the ALPR HUD panel size (1.0 = default). Clamped 0.75–2.0 in code.</summary>
        public float alprHudScale = 1.0f;

        /// <summary>Show the Quick Actions bar (backup, panic, set GPS, clear ALPR) on the desktop.</summary>
        public bool quickActionsBarEnabled = true;

        /// <summary>When true, log detailed firearm capture flow (PR API results, fallback, event triggers) to MDT Pro log. Use for debugging Firearms Check.</summary>
        public bool firearmDebugLogging = false;

        /// <summary>Backup Quick Actions: Auto (Policing Redefined BackupAPI if present, else Ultimate Backup), PolicingRedefined, or UltimateBackup.</summary>
        public string integrationBackupProvider = "Auto";

        /// <summary>Traffic/stop/event integration: Auto (PR events if Policing Redefined loaded, else StopThePed), PolicingRedefined, or StopThePed.</summary>
        public string integrationStopEvents = "Auto";

        /// <summary>When true, log each step of POST /post/createArrestReport and in-memory/SQLite court case creation (game fiber). Turn on while testing arrest→court issues; turn off afterward.</summary>
        public bool verboseArrestCourtLogging = false;

        /// <summary>When true, startup writes the full config JSON and MDT folder file list to MDTPro.log. Default off keeps the log short.</summary>
        public bool verboseFileLogging = false;
        /// <summary>When the log file exceeds this size (KB), it is shortened automatically (newest half kept). 0 = no limit.</summary>
        public int logFileMaxSizeKb = 5120;
    }
}
