# Changelog

## [0.9.5.0] — 2026-03-10

### Major Features

- **Court outcome overhaul** — Verdict and sentencing narratives are much deeper. Dozens of outcome reasons that correlate with evidence (weapon, warrant, fleeing, resistance, assault, intoxication, drugs, supervision, etc.). **Sentencing Rationale** (new field) explains aggravating/mitigating factors, recidivism, jury verdict margin, and district policy. Low-evidence guilty and high-evidence acquittal get special “mismatch” narratives. Charge-type vocabulary (DUI, drugs, assault, resisting) and docket pressure add variety. Convicted cases show a separate “Sentencing Rationale” section on the Court page. Database schema 22 adds `SentenceReasoning`; existing cases get it on next resolution.
- **Quick Actions Bar** — A small floating bar at the bottom-right of the MDT with one-tap buttons for Panic, Backup, and Clear ALPR. You can turn it on or off in Config → Quick Actions Bar. Backup options require Policing Redefined.
- **Request Backup from the MDT** — Request backup (patrol, traffic stop, transport, tow, etc.) directly from the MDT. Works with Policing Redefined when installed.
- **Active Call** — The Active Call page now shows each call’s status (Pending, Accepted, En Route, Finished), a short timeline, and expandable cards. You can set a GPS waypoint to a call and use Accept / En Route when your callout system supports it.
- **Create BOLO from the noticeboard** — You can add a BOLO from the BOLO Noticeboard without needing the vehicle in front of you. Enter plate, optional model, reason, and how long it should last. You can also remove BOLOs from the list.
- **Use of Force on arrest reports** — Arrest reports now have a Use of Force section: type (e.g. Taser, Baton, Firearm), justification, whether anyone was injured, and witnesses. This is saved and shown in court as “Use of Force Documented,” with an evidence bonus you can set in Config → Court.
- **Prefill when creating reports** — If you tap “New Injury Report” from Person Search or “Create Impound Report” from Vehicle Search, the report form opens with the person or vehicle details already filled in.
- **Impound Report** — New report type for impounded vehicles: plate, model, owner, VIN, reason, tow company (dropdown: Camel Towing, Davis Towing — GTA 5 lore), and impound lot (randomly assigned to one of the two LSPD Auto Impound locations: Mission Row or Davis). Available under Reports, and from Vehicle Search via “Create Impound Report” (vehicle info is prefilled).
- **Traffic Incident Report** — New report type for collisions and multi-vehicle incidents: drivers, passengers, pedestrians, vehicles (plates and models), injury yes/no and details, and collision type. Available under Reports.
- **Injury Report** — New report type: injured party, injury type, severity, treatment, incident context, and optional link to another report. Available under Reports, and from Person Search via “New Injury Report” (name prefilled). **Import from game** fills injury type, severity, and treatment from in-game data: use [DamageTrackerFramework](https://github.com/Variapolis/DamageTrackerFramework) (optional) for detailed damage (weapon, body region, amount) or current ped health/armor as fallback when the injured party is nearby.
### Minor Features

- **Court: Disposition UI** — Verdict and outcome reasoning appear under “Verdict & Outcome Reasoning”; convicted cases also show “Sentencing Rationale” in a separate block. Evidence breakdown lists all 12 evidence flags (e.g. intoxicated, fleeing, supervision violation, pat-down, illegal weapon). New language keys for sentence reasoning and evidence labels.
- **Court: Use of Force** — When an arrest report includes use of force, the court case shows "Use of Force Documented" in the evidence and gets the bonus you set in config.
- **Court: Fleeing** — Suspects who surrender after a chase are now correctly marked as having fled, so court gets the right evidence.
- **Court: Drunk** — Suspects doing drunk movements or animations are now correctly flagged for court even when the game's own drunk check doesn't fire.
- **ALPR** — Scan and read range increased so plates are easier to pick up at distance.
- **In-game notifications** — Update checker and other in-game messages now use a police-style icon instead of the LS Customs icon.
- **Browser tab** — The MDT Pro logo now appears in the browser tab when the MDT is open.
- **OpenIV installers** — You can install with an .oiv package and uninstall with the included uninstaller; both point to LCPDFR and GitHub. For a full removal, delete the MDTPro folder from your GTA V directory after uninstalling.
- **Backup response code** — The Quick Actions backup menu lets you choose Code 1, Code 2, or Code 3 for patrol, EMS, traffic stop, and transport requests.

### Bug Fixes

- **Court: edge cases** — Outcome and sentencing builders are null-safe. Plea comparison is case-insensitive (e.g. “guilty” from UI works). Acquitted or dismissed cases clear `SentenceReasoning` so the UI does not show sentencing. Empty or null plea is treated as “Not Guilty” when resolving. Empty outcome pool and zero total weight in weighted selection are handled without throwing. License revocations append only when the list is non-null and non-empty. Database loader uses `ReaderOptionalString` for `SentenceReasoning` so older DBs or missing columns do not crash.
- **ALPR vehicle color** — The in-game ALPR HUD now shows color names (e.g. "Red", "Black / White") instead of raw numbers like "255-0-0".
- **State ID and "Ask for all docs"** — Showing a State ID on a foot stop or using "Ask for all documents" on a vehicle stop now correctly adds the person to Person Search.
- **VIN Tampering and license revocation** — Convictions for "VIN Tampering / Altered Or Defaced VIN" now result in driver's license revocation at sentencing, consistent with other VIN-related charges.

## [0.9.3.0] — 2026-03-07

### Major Features

- **New: Firearms Check** — New page to search firearms by serial number or owner name. Uses data from pat-downs and dead-body searches (Policing Redefined `GetPedSearchItems`).
- **New: Registered Firearms** — Person Search now shows a "Registered Firearms" section listing weapons linked to the searched person.
- **Drug records** — Pat-down and dead-body search now capture `DrugItem` from PR `GetPedSearchItems`. Stored in `drug_records` table. Person Search shows "Substance History" when drugs are found.
- **Vehicle search records** — Poll-based capture of vehicle search items (weapons, drugs, contraband) when PR reports a vehicle as searched. Stored in `vehicle_search_records`. Vehicle Search shows "Search Results (Contraband)".
- **Vehicle schema** — VIN Status (Valid/Scratched), Make, Model, PrimaryColor, SecondaryColor added to vehicles table and Person/Vehicle search UI.
- **BOLO management** — New REST endpoints: `POST /post/addBOLO` and `POST /post/removeBOLO`. Requires vehicle to be in-world.
- **Settings & Officer Profile** — Officer information, shift controls, and career stats moved to a separate "Officer Profile" taskbar button. Settings panel now focuses on Plugins & Config only.
- **Config page improvements** — Plain-English labels and tooltips for all config options; grouped sections; preset dropdowns with optional custom values; filter search; collapsible sections; Revert button to discard unsaved changes; success/error feedback on save.
- **New: Department Styling plugin** — Choose an MDT theme based on your in-game department. Options: San Andreas Government (default), LSPD, SAFD, BCSO, LSSD, SAHP, FIB. Each theme applies department-specific colors, badge imagery, and animated backgrounds to the MDT. Enable in Config → Plugins; select theme in Settings → Department Theme.
- **New: Court license revocations** — Upon conviction, the court now orders license revocations based on California law. Driver's license, firearms permit (CCW), and sport fishing privileges can be revoked depending on the charges. Revocations are stored in the court outcome and applied to the offender's ped record.
- **License revocation rules (CA-based):** Driver's license revoked for charges with `canRevokeLicense` (DUI, reckless driving, hit-and-run, evading, etc.). Firearms permit: lifetime for felonies and domestic violence; 10 years for violent misdemeanors (assault, battery, brandishing). Sport fishing revoked for fish/wildlife code violations.

### Minor Features

- **ALPR behavior** — Tuning (read range, cone, plate position) is now hardcoded with realistic values; only Enable and HUD position remain in config. Sound plays when a flagged vehicle is read. Full vehicle details shown only when the vehicle has alert flags; "Scanning" otherwise. Vehicle color shown on ALPR card. HUD uses RawFrameRender to prevent flicker when in a police vehicle. Config save merges with existing config so internal keys (e.g. ALPR) are not lost when saving from the Config page.
- **ALPR MDT notifications** — Notifications auto-dismiss after 2 minutes with a short fade; maximum of 8 popups shown at once.
- **Clarification: Calendar and bundled plugins** — Calendar, Vehicle Search, and other bundled plugins only appear in the sidebar when enabled in Config → Plugins. If they were not visible, they were disabled—not missing due to a bug.
- **Firearm data:** Weapon names come from the game native (`GET_WEAPON_NAME_FROM_HASH`) so model names match what players see in-game.
- **Firearm records** stored in SQLite (`firearm_records` table, schema 15). Upsert by owner + serial + weapon hash.
- **Scratched serial numbers** — Firearms with PR `EFirearmState.ScratchedSN` are stored with `SerialNumber = null` and cannot be searched by serial; they appear in owner/Person Search as "Serial: Scratched". Schema 17 adds `IsSerialScratched`.
- **Build script:** The `Dependencies` folder (including SQLite DLLs) is now copied into the Release folder so the full mod package includes all required files.
- **UI:** Firearms Check menu icon updated to a pistol/sidearm icon.
- **PR events** — Subscribed to `OnFootTrafficStopEnded`, `OnPedAskedToExitVehicle`, `OnDriverAskedToTurnOffEngine`. Procedural actions (e.g. "Asked to exit vehicle") added to Identification History.
- **CDF removal events** — Subscribes to `OnPedDataRemoved` and `OnVehicleDataRemoved` to prune in-memory lists when entities despawn (keeps SQLite/keepIn* intact).
- **CDF sync** — Citations count and TimesStopped now sync to CDF when updating ped from MDT.
- **Court evidence** — Drug possession adds `courtEvidenceDrugsBonus` and "Drugs Found on Person" in court case evidence breakdown.
- **Schema 16** — Database migration for new tables and columns.
- **Officer status bar** — Header now shows a compact officer info strip to the right of the badge: First Name, Last Name, Badge Number, Rank, Call Sign, Department. Fills from Officer Profile; updates when you save.
- **Department Styling: Animated backgrounds** — Replaced static wallpaper images with a canvas-based particle animation. Floating particles in the department accent color drift slowly across the desktop for a calm, modern look.
- **Department Styling: Config page theming** — The Config and Plugins (customization) page now follows the selected department theme.
- **Firearms Check icon** — Updated to a rifle/sidearm icon (from SVG Repo) for clearer identification.
- **Sidebar icon consistency** — All desktop menu icons (Person Search, Vehicle Search, Firearms Check, Reports, etc.) now use the same color.
- **Court UI** — Convicted cases display a "License Revocations Ordered" section listing all court-ordered revocations in the disposition.
- **Ped persistence** — Revoked status is saved to SQLite and reflected in Person Search (License Status, Weapon Permit Status).
- **CDF/PR sync** — Full bidirectional sync so Policing Redefined and Common Data Framework stay aligned with MDT data. Driver's license, weapon permit, fishing permit, and hunting permit status now sync from MDT to CDF on ped update, court conviction, and re-encounter.
- **Re-encounter parity** — When a previously convicted person is re-encountered, their revoked licenses/permits are applied from our SQLite record to CDF/PR so traffic stops and ID checks show the correct status with no conflicts.
- **Schema 18** — `LicenseRevocations` column added to `court_cases` table (JSON array of revocation strings).

### Bug Fixes

- **Fixed: Wanted status in Person Search** — Wanted status from PR dispatch now syncs to MDT (refresh on `OnPedRanThroughDispatch` and re-encounter). Clears correctly on arrest report and court resolution; synced to CDF so MDT and PR stay aligned.
- **Fixed: Court plea type reverting** — Plea selection (Guilty/Not Guilty/No Contest) was not being saved for pending cases. Added a "Save Plea & Notes" button so changes persist when closing the window. Force Resolve now also sends the current plea and outcome notes from the UI, so the selected plea is used when resolving.
- **Fixed:** Bug fixes for ALPR and Policing Redefined event integration.
- **Fixed: ALPR proximity and front/rear plate support** — ALPR previously required you to be almost touching vehicles and only reliably read front plates. Root causes: (1) missing config keys deserialized to 0, forcing read range down to 2 m; (2) geometry used vehicle center instead of plate position, favoring front-facing scenarios. Fixes: config migration ensures ALPR defaults when keys are missing; read range minimum raised from 2 m to 10 m (max 50 m); scan range min 15 m; cone min 15°; plate-position logic now uses the plate that faces the cruiser (rear when behind, front when head-on) via bone positions (`numberplate`, `bumper_r`, `bumper_f`), so both front and rear plates are readable at realistic distances.
- **OnPedReleased safety** — Avoids dereferencing ped when PR reports released ped may no longer exist.
- **RageHook compliance** — Cross-checked Ped/Vehicle usage against Rage Plugin Hook docs: `Vehicle.Driver` null-checked before use; `Exists()` used for pre-persist validation (handles null safely per Rage); all native calls (e.g. `GET_WEAPON_NAME_FROM_HASH`) run in GameFiber/game thread context; PR event handlers validate handleables before access.
- **Fixed: Overlay click targeting** — Window controls (close, minimize, maximize) and the Settings department dropdown were not clickable; clicks were hitting the sidebar badge instead. Fixed by ensuring the overlay stacks above main content.

## [0.9.2.0] — 2026-03-07

- **Fixed:** Registration and insurance status now sync correctly with Policing Redefined. Previously, re-encounter logic could overwrite PR’s revoked/expired status with stale “Valid” from the database; CDF/PR is now treated as authoritative at stop time.
- **Fixed:** SQLite DLL placement corrected. `System.Data.SQLite.dll` and `x64\SQLite.Interop.dll` must go in the **GTA V root** (same folder as `GTA5.exe`), not in `plugins\LSPDFR`. The native loader uses the application directory. Install instructions updated.

## [0.9.1.0] — 2026-03-07

- **Fixed:** SQLite dependencies (`System.Data.SQLite.dll` and `x64\SQLite.Interop.dll`) are now included in the release package. This fixes a crash on going on duty for users who didn't have these files from another mod.
- **Config default:** WebSocket update interval (`webSocketUpdateInterval`) default is now **1000** ms (was 100 ms). Reduces CPU use; taskbar and map stay smooth. Existing installs keep their current value until you change it: open **Customization → Config**, set `webSocketUpdateInterval` to `1000`, and Save—or edit `MDTPro/config.json` and set `"webSocketUpdateInterval": 1000`.
- Changed citation workflow so citation reports no longer create or update court cases.
- Kept citation fine randomness intact (fine is still randomized per charge between configured min/max).
- Arrest reports still create court cases as before.

## [0.9.0.0] — 2025-03-06 (beta – initial release)

- **Initial public beta.** Police MDT with reports, peds, citations, arrest/charge options, court cases, and shift tracking.
- Web UI served locally; data stored in `MDTPro/data/` (SQLite).
