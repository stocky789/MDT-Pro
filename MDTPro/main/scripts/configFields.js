/**
 * Friendly labels and tooltips for config fields. Keys must match Config.cs exactly.
 */
const CONFIG_SECTIONS = [
  {
    title: 'General',
    keys: [
      'port',
      'webSocketUpdateInterval',
      'databaseUpdateInterval',
      'initialWindowWidth',
      'initialWindowHeight',
    ],
  },
  {
    title: 'Display & Time',
    keys: [
      'useInGameTime',
      'showSecondsInTaskbarClock',
      'updateDomWithLanguageOnLoad',
      'displayCurrencySymbolBeforeNumber',
    ],
  },
  {
    title: 'Person Data Limits',
    keys: [
      'maxNumberOfNearbyPedsOrVehicles',
      'databaseLimitMultiplier',
    ],
  },
  {
    title: 'Random Person Data',
    keys: [
      'hasPriorCitationsProbability',
      'hasPriorArrestsProbability',
      'hasPriorArrestsWithWarrantProbability',
      'reEncounterChance',
      'reEncounterVehicleChance',
      'reEncounterVehicleChanceWhenPedKnown',
      'maxNumberOfPriorCitations',
      'maxNumberOfPriorArrests',
      'maxNumberOfPriorArrestsWithWarrant',
    ],
  },
  {
    title: 'Reports & Court Case IDs',
    keys: [
      'reportIdFormat',
      'reportIdIndexPad',
      'courtCaseNumberFormat',
      'courtCaseNumberIndexPad',
    ],
  },
  {
    title: 'Court — General',
    keys: [
      'courtDatabaseMaxEntries',
      'courtRosterRotationDays',
      'courtJurySeverityThreshold',
    ],
  },
  {
    title: 'Court — Repeat Offender Scoring',
    keys: [
      'courtPriorCitationWeight',
      'courtPriorArrestWeight',
      'courtPriorConvictionWeight',
      'courtProbationWeight',
      'courtParoleWeight',
      'courtWantedWeight',
      'courtRecentConvictionWindowDays',
      'courtRecentConvictionBonusWeight',
    ],
  },
  {
    title: 'Court — Evidence Scoring',
    keys: [
      'courtEvidenceBase',
      'courtEvidencePerCharge',
      'courtEvidenceArrestableBonus',
      'courtEvidenceLifeSentenceBonus',
      'courtEvidenceMax',
      'courtEvidenceWeaponBonus',
      'courtEvidenceWantedBonus',
      'courtEvidencePatDownBonus',
      'courtEvidenceDrunkBonus',
      'courtEvidenceFleeingBonus',
      'courtEvidenceAssaultBonus',
      'courtEvidenceVehicleDamageBonus',
      'courtEvidenceIllegalWeaponBonus',
      'courtEvidenceSupervisionViolationBonus',
      'courtEvidenceResistedBonus',
      'courtEvidenceDrugsBonus',
    ],
  },
  {
    title: 'Court — Case Resolution & Docket',
    keys: [
      'courtCaseResolutionMinBase',
      'courtCaseResolutionMaxMinutes',
      'courtCaseResolutionSeverityScale',
      'courtParoleThresholdRealDays',
      'courtParoleReleaseFraction',
      'courtDocketWindowDays',
      'courtDocketPressureBase',
      'courtDocketPressureScale',
    ],
  },
  {
    title: 'Court — Prosecutor & Defense',
    keys: [
      'courtProsecutionSeverityWeight',
      'courtProsecutionEvidenceWeight',
      'courtProsecutionRecidivismWeight',
      'courtDefensePublicDefenderBonus',
      'courtDefensePrivateCounselBonus',
    ],
  },
  {
    title: 'Map & GPS',
    keys: [
      'mapPlayerIconSize',
      'mapTurnPenaltySecondsPerRadian',
      'mapDrawPostalCodeSet',
      'mapUseBurgerUnits',
      'mapSmoothPlayerIcon',
    ],
  },
  {
    title: 'Active Call',
    keys: [
      'showAgencyInCalloutInfo',
      'addCalloutSuspectNamesFromMessages',
    ],
  },
  {
    title: 'Quick Actions Bar',
    keys: ['quickActionsBarEnabled'],
  },
  {
    title: 'Updates',
    keys: [
      'checkForUpdates',
      'githubReleasesRepo',
    ],
  },
]

const PRESET_CUSTOM = { label: 'Custom...', value: '__custom__' }

const CONFIG_FIELD_META = {
  port: {
    label: 'HTTP port',
    tooltip: 'The port the MDT web interface runs on (e.g. 8080). You may need to allow this port in your firewall.',
    presets: [
      { label: '8080 (default)', value: 8080 },
      { label: '3010', value: 3010 },
      { label: '9000', value: 9000 },
      PRESET_CUSTOM,
    ],
  },
  webSocketUpdateInterval: {
    label: 'Taskbar & map update interval (ms)',
    tooltip: 'How often the taskbar clock, location, and map position refresh (in milliseconds). Higher values (e.g. 1000) use less CPU; lower values feel snappier but may impact FPS.',
    presets: [
      { label: '500 ms (snappy)', value: 500 },
      { label: '1000 ms (recommended)', value: 1000 },
      { label: '2000 ms (low CPU)', value: 2000 },
      { label: '5000 ms', value: 5000 },
      PRESET_CUSTOM,
    ],
  },
  databaseUpdateInterval: {
    label: 'Database sync interval (ms)',
    tooltip: 'How often the MDT syncs data with the game database. Higher values reduce CPU usage.',
    presets: [
      { label: '5000 ms', value: 5000 },
      { label: '10000 ms (default)', value: 10000 },
      { label: '15000 ms', value: 15000 },
      { label: '30000 ms', value: 30000 },
      PRESET_CUSTOM,
    ],
  },
  initialWindowWidth: {
    label: 'Default window width (px)',
    tooltip: 'Width in pixels when you first open an MDT window (e.g. Person Search, Vehicle Search).',
    presets: [
      { label: '600 px', value: 600 },
      { label: '720 px', value: 720 },
      { label: '900 px', value: 900 },
      { label: '1024 px', value: 1024 },
      PRESET_CUSTOM,
    ],
  },
  initialWindowHeight: {
    label: 'Default window height (px)',
    tooltip: 'Height in pixels when you first open an MDT window.',
    presets: [
      { label: '400 px', value: 400 },
      { label: '500 px', value: 500 },
      { label: '600 px', value: 600 },
      { label: '720 px', value: 720 },
      PRESET_CUSTOM,
    ],
  },
  useInGameTime: {
    label: 'Use in-game time',
    tooltip: 'When enabled, the taskbar clock and report timestamps use GTA\'s in-game time instead of your real system time. Good for immersion.',
  },
  showSecondsInTaskbarClock: {
    label: 'Show seconds in taskbar clock',
    tooltip: 'Display seconds in the taskbar clock (e.g. 14:32:05 instead of 14:32).',
  },
  updateDomWithLanguageOnLoad: {
    label: 'Apply language file on load',
    tooltip: 'When enabled, UI text is replaced from the language file when each page loads. Useful if you\'ve customized MDTPro/language.json.',
  },
  displayCurrencySymbolBeforeNumber: {
    label: 'Currency before amount',
    tooltip: 'Show fines as "$100" when enabled, or "100 $" when disabled. Affects citation fines and court displays.',
  },
  maxNumberOfNearbyPedsOrVehicles: {
    label: 'Max nearby people/vehicles shown',
    tooltip: 'Maximum number of people or vehicles to show in "Recent IDs" / "Nearby Vehicles" type lists.',
  },
  databaseLimitMultiplier: {
    label: 'Database limit multiplier',
    tooltip: 'Multiplier applied to various database query limits. Increase if you want larger result sets (e.g. more shift history).',
  },
  hasPriorCitationsProbability: {
    label: 'Chance person has prior citations',
    tooltip: 'Probability (0–1) that a randomly generated person has prior citations in their record. Affects CDF/person generation.',
  },
  hasPriorArrestsProbability: {
    label: 'Chance person has prior arrests',
    tooltip: 'Probability (0–1) that a generated person has prior arrests.',
  },
  hasPriorArrestsWithWarrantProbability: {
    label: 'Chance prior arrests include warrants',
    tooltip: 'Among people with prior arrests, the probability (0–1) that some of those arrests have active warrants.',
  },
  reEncounterChance: {
    label: 'Re-encounter same person chance',
    tooltip: 'Probability (0–1) of meeting the same person again in a later traffic stop or encounter.',
  },
  reEncounterVehicleChance: {
    label: 'Re-encounter same vehicle (unknown driver)',
    tooltip: 'Probability (0–1) of seeing the same vehicle again when the driver isn\'t a known person.',
  },
  reEncounterVehicleChanceWhenPedKnown: {
    label: 'Re-encounter same vehicle (known driver)',
    tooltip: 'Probability (0–1) of re-encountering a vehicle when the driver is already a known/persistent person. Typically higher than unknown-driver chance.',
  },
  maxNumberOfPriorCitations: {
    label: 'Max prior citations per person',
    tooltip: 'Maximum number of prior citations that can appear on a generated person\'s record.',
  },
  maxNumberOfPriorArrests: {
    label: 'Max prior arrests per person',
    tooltip: 'Maximum number of prior arrests on a generated person.',
  },
  maxNumberOfPriorArrestsWithWarrant: {
    label: 'Max prior arrests with warrant',
    tooltip: 'Maximum number of prior arrests that can include active warrants.',
  },
  reportIdFormat: {
    label: 'Report ID format',
    tooltip: 'Template for citation, arrest, and incident IDs. Placeholders: {type}, {year}, {shortYear}, {month}, {day}, {index}. Example: "{type}-{shortYear}-{index}" gives C-26-000001.',
    presets: [
      { label: '{type}-{shortYear}-{index} (default)', value: '{type}-{shortYear}-{index}' },
      { label: '{type}-{year}-{index}', value: '{type}-{year}-{index}' },
      { label: '{type}-{index}', value: '{type}-{index}' },
      PRESET_CUSTOM,
    ],
  },
  reportIdIndexPad: {
    label: 'Report ID index padding',
    tooltip: 'Number of digits to pad the report index (e.g. 6 gives 000001, 000002, …).',
  },
  courtCaseNumberFormat: {
    label: 'Court case number format',
    tooltip: 'Template for court case numbers. Placeholders: {shortYear}, {index}, {year}, {month}, {day}.',
    presets: [
      { label: '{shortYear}-{index} (default)', value: '{shortYear}-{index}' },
      { label: '{year}-{index}', value: '{year}-{index}' },
      PRESET_CUSTOM,
    ],
  },
  courtCaseNumberIndexPad: {
    label: 'Court case index padding',
    tooltip: 'Number of digits to pad the court case index.',
  },
  courtDatabaseMaxEntries: {
    label: 'Max court cases kept',
    tooltip: 'Maximum number of court cases to keep in the database. Older cases may be pruned.',
  },
  courtRosterRotationDays: {
    label: 'Court roster rotation (days)',
    tooltip: 'How often (in days) judges and prosecutors rotate in the court system.',
  },
  courtJurySeverityThreshold: {
    label: 'Jury trial severity threshold',
    tooltip: 'Case severity score above which a jury trial may be offered. 0 = disable jury trials.',
  },
  courtPriorCitationWeight: {
    label: 'Prior citation weight (repeat offender)',
    tooltip: 'Points added to repeat-offender score per prior citation. Affects conviction likelihood.',
  },
  courtPriorArrestWeight: {
    label: 'Prior arrest weight',
    tooltip: 'Points per prior arrest in repeat-offender scoring.',
  },
  courtPriorConvictionWeight: {
    label: 'Prior conviction weight',
    tooltip: 'Points per prior conviction.',
  },
  courtProbationWeight: {
    label: 'Probation weight',
    tooltip: 'Extra points if the person is on probation.',
  },
  courtParoleWeight: {
    label: 'Parole weight',
    tooltip: 'Extra points if the person is on parole.',
  },
  courtWantedWeight: {
    label: 'Wanted status weight',
    tooltip: 'Extra points if the person was wanted at arrest.',
  },
  courtRecentConvictionWindowDays: {
    label: 'Recent conviction window (days)',
    tooltip: 'Convictions within this many days count as "recent" and get a bonus weight.',
  },
  courtRecentConvictionBonusWeight: {
    label: 'Recent conviction bonus weight',
    tooltip: 'Extra points per recent conviction.',
  },
  courtEvidenceBase: {
    label: 'Evidence base score',
    tooltip: 'Starting evidence score for every court case before adding charge/context bonuses.',
  },
  courtEvidencePerCharge: {
    label: 'Evidence per charge',
    tooltip: 'Points added to evidence score per charge in the case.',
  },
  courtEvidenceArrestableBonus: {
    label: 'Arrestable charge bonus',
    tooltip: 'Extra evidence points when a charge is arrestable (vs citation-only).',
  },
  courtEvidenceLifeSentenceBonus: {
    label: 'Life sentence charge bonus',
    tooltip: 'Extra evidence points when a charge carries life in prison.',
  },
  courtEvidenceMax: {
    label: 'Evidence score cap',
    tooltip: 'Maximum evidence score (0 = no cap, e.g. 95 limits to 95).',
  },
  courtEvidenceWeaponBonus: {
    label: 'Weapon found bonus',
    tooltip: 'Evidence points when a weapon was found on the person.',
  },
  courtEvidenceWantedBonus: {
    label: 'Wanted person bonus',
    tooltip: 'Evidence points when the person was wanted at arrest.',
  },
  courtEvidencePatDownBonus: {
    label: 'Pat-down conducted bonus',
    tooltip: 'Evidence points when a pat-down was performed.',
  },
  courtEvidenceDrunkBonus: {
    label: 'Intoxication evidence bonus',
    tooltip: 'Evidence points when the person was drunk or under influence.',
  },
  courtEvidenceFleeingBonus: {
    label: 'Fleeing evidence bonus',
    tooltip: 'Evidence points when the person fled or attempted to flee.',
  },
  courtEvidenceAssaultBonus: {
    label: 'Assault evidence bonus',
    tooltip: 'Evidence points when the person assaulted an officer or another person.',
  },
  courtEvidenceVehicleDamageBonus: {
    label: 'Vehicle/property damage bonus',
    tooltip: 'Evidence points when the person damaged a vehicle or property.',
  },
  courtEvidenceIllegalWeaponBonus: {
    label: 'Illegal weapon bonus',
    tooltip: 'Evidence points when an illegal weapon was found.',
  },
  courtEvidenceSupervisionViolationBonus: {
    label: 'Probation/parole violation bonus',
    tooltip: 'Evidence points when the person violated probation or parole.',
  },
  courtEvidenceResistedBonus: {
    label: 'Resisted arrest bonus',
    tooltip: 'Evidence points when the person resisted arrest.',
  },
  courtEvidenceDrugsBonus: {
    label: 'Drugs found bonus',
    tooltip: 'Evidence points when drugs were found on the person.',
  },
  courtCaseResolutionMinBase: {
    label: 'Min resolution time (minutes)',
    tooltip: 'Minimum base minutes until a court case is resolved.',
  },
  courtCaseResolutionMaxMinutes: {
    label: 'Max resolution time (minutes)',
    tooltip: 'Maximum minutes before a case is resolved.',
  },
  courtCaseResolutionSeverityScale: {
    label: 'Severity-to-time scale',
    tooltip: 'How much case severity increases resolution time.',
  },
  courtParoleThresholdRealDays: {
    label: 'Parole eligibility (real days)',
    tooltip: 'Real-world days served before parole can be considered.',
  },
  courtParoleReleaseFraction: {
    label: 'Parole release fraction',
    tooltip: 'Fraction of remaining sentence that may be granted on parole (0–1).',
  },
  courtDocketWindowDays: {
    label: 'Docket pressure window (days)',
    tooltip: 'Days of case volume used to compute court docket pressure (affects case outcomes).',
  },
  courtDocketPressureBase: {
    label: 'Docket pressure base',
    tooltip: 'Base docket pressure value when court is busy.',
  },
  courtDocketPressureScale: {
    label: 'Docket pressure scale',
    tooltip: 'How strongly docket volume affects outcomes.',
  },
  courtProsecutionSeverityWeight: {
    label: 'Prosecution: severity weight',
    tooltip: 'How much case severity affects prosecution strength (0–1).',
  },
  courtProsecutionEvidenceWeight: {
    label: 'Prosecution: evidence weight',
    tooltip: 'How much evidence score affects prosecution strength (0–1).',
  },
  courtProsecutionRecidivismWeight: {
    label: 'Prosecution: repeat offender weight',
    tooltip: 'How much repeat-offender score affects prosecution strength (0–1).',
  },
  courtDefensePublicDefenderBonus: {
    label: 'Defense: public defender bonus',
    tooltip: 'Strength bonus when the defendant uses a public defender.',
  },
  courtDefensePrivateCounselBonus: {
    label: 'Defense: private counsel bonus',
    tooltip: 'Strength bonus when the defendant has private counsel.',
  },
  mapPlayerIconSize: {
    label: 'Map player icon size (px)',
    tooltip: 'Size in pixels of your icon on the GPS map.',
  },
  mapTurnPenaltySecondsPerRadian: {
    label: 'Map turn penalty',
    tooltip: 'Penalty (in seconds per radian) for turns in GPS routing. Higher = prefers straighter routes.',
  },
  mapDrawPostalCodeSet: {
    label: 'Show postal codes on map',
    tooltip: 'Draw postal code boundaries on the GPS map.',
  },
  mapUseBurgerUnits: {
    label: 'Use Burger Shot units for distance',
    tooltip: 'Display distance in "Burger Shot" units (GTA joke) instead of meters/feet.',
  },
  mapSmoothPlayerIcon: {
    label: 'Smooth player icon movement',
    tooltip: 'Animate the player icon smoothly on the map instead of jumping. Uses the taskbar update interval for animation.',
  },
  showAgencyInCalloutInfo: {
    label: 'Show agency in callout info',
    tooltip: 'Display the responding agency in the Active Call page.',
  },
  addCalloutSuspectNamesFromMessages: {
    label: 'Add suspect names from callout messages',
    tooltip: 'When callout messages mention names (e.g. "associated with Joe Thomas"), add them as person records so they appear in Person Search. Disable if your callout pack registers suspects with CDF directly.',
  },
  quickActionsBarEnabled: {
    label: 'Show Quick Actions bar',
    tooltip: 'Show the floating Quick Actions bar (bottom-center) with one-click buttons for Panic, Backup, and Clear ALPR. Requires Policing Redefined for backup actions.',
  },
  checkForUpdates: {
    label: 'Check for updates on load',
    tooltip: 'Query GitHub for new MDT Pro releases when you start the game. Disable to skip the check.',
  },
  githubReleasesRepo: {
    label: 'GitHub repo for updates',
    tooltip: 'Repository in "owner/repo" format (e.g. stocky789/MDT-Pro). Leave empty to skip update checks.',
  },
}

function getConfigFieldMeta(key) {
  const meta = CONFIG_FIELD_META[key]
  if (meta) {
    const tooltip = (meta.tooltip && String(meta.tooltip).trim()) ? meta.tooltip : `Setting: ${key}.`
    return { ...meta, tooltip }
  }
  const label = key.replace(/([A-Z])/g, ' $1').replace(/^./, (s) => s.toUpperCase()).trim()
  return {
    label,
    tooltip: `This option controls "${label}". See the documentation for details.`,
  }
}
