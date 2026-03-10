# Changelog

## [0.9.5.0] — 2026-03-09

### Major Features

- **Quick Actions Bar** — New floating action bar (bottom-right) with one-click buttons: Panic (request panic backup), Backup (request local patrol), Clear ALPR. Requires Policing Redefined for backup actions. Toggle on/off in Config → Quick Actions Bar.
- **Request Backup from MDT** — Request Policing Redefined backup (panic, local patrol, traffic stop, transport, tow) via `POST /post/requestBackup` with `{ "action": "panic" | "localPatrol" | "trafficStop" | "transport" | "tow" }`.
- **Active Call enhancements** — Status badge (Pending, Accepted, En Route, Finished) and timeline (Displayed • Accepted • Finished) on the Active Call page. Expandable callout list: cards show name, status, address; click header to expand/collapse. Pending callouts show Accept button; Accepted show En Route button. Set GPS per callout. Accept/En Route buttons call `POST /post/calloutAction`; CalloutInterface does not expose programmatic accept/status, so use in-game interface; buttons kept for future API support.
- **BOLO system improvements** — Create BOLO without vehicle nearby via new "Create BOLO" button on BOLO Noticeboard (plate, optional model, reason, expiry). BOLOs sync to CDF on re-encounter. Remove BOLO works for stub records. `POST /post/addBOLO` accepts optional `ModelDisplayName`.

### Minor Features

- **Court evidence: WasFleeing** — Fleeing detection improved. Suspects who surrender after a chase are now correctly flagged as having fled. Chase-then-stop scenarios are captured for court even when the game no longer reports them as fleeing at arrest.
- **Court evidence: WasDrunk** — Drunk detection improved. Suspects doing drunk movements or animations are now correctly flagged for court, even when the game's built-in drunk check doesn't trigger.
- **ALPR range** — Scan and read range doubled for better plate detection at distance.
- **In-game notifications** — Update checker and other in-game messages now use a police-style icon instead of the LS Customs icon.
- **Web favicon** — MDT Pro logo now shows in the browser tab when using the MDT.
- **OpenIV packages** — Install with MDTPro-x.x.x.oiv; uninstall with MDTPro-x.x.x-Uninstall.oiv. Both include links to LCPDFR and GitHub. The uninstaller removes the plugin and all MDTPro files. For a complete removal, manually delete the MDTPro folder from your GTA V directory after uninstalling.
- **Backup response code selector** — Quick Actions Bar backup menu now includes Code 1, Code 2, and Code 3 options. Selected code is sent with patrol, EMS, traffic stop, and transport requests.
- **GpsHelper** — Uses GTA native `SET_NEW_WAYPOINT` for in-game waypoint (used by `setGpsWaypoint` API).
- **BackupHelper** — Wraps Policing Redefined Backup API via reflection so the plugin loads without PR; backup actions no-op when PR is not installed.

### API Verification (cross-checked)

- **Policing Redefined Backup API** — Method signatures match docs: RequestPanicBackup(bool, bool), RequestBackup(EBackupUnit, EBackupResponseCode, bool×3), RequestTrafficStopBackup, RequestPoliceTransport, RequestTowServiceBackup(). EBackupUnit.LocalPatrol confirmed.
- **GTA 5 Native** — SET_NEW_WAYPOINT (HUD, hash 0xFE4333F2) takes float x, float y. RagePluginHook Natives.SET_NEW_WAYPOINT(x, y) invoked from GameFiber.
- **CDF** — No new CDF usage in this feature set; setGpsWaypoint uses CalloutEvents.CalloutInfo.Coords from LSPDFR callout position.

### Bug Fixes

- **Fixed: ALPR vehicle color display** — In-game ALPR HUD previously showed raw RGB values (e.g. "255-0-0") instead of readable color names. Vehicle colors now display as names (e.g. "Red", "Black / White") using CalloutInterfaceAPI's color conversion.
- **Fixed: State ID / "Ask for all docs" not populating Person Search** — Identification events (OnIdentificationGiven, OnDriverIdentificationGiven, OnOccupantIdentificationGiven) now call ResolvePedForReEncounter before AddIdentificationEvent, so foot traffic stop State ID and vehicle "ask for all documents" correctly add the ped to Person Search. Resilient identification enum mapping: unknown EGivenIdentification values fall back to "Identification" instead of skipping.
- **Fixed: VIN Tampering license revocation** — "VIN Tampering / Altered Or Defaced VIN" now has `canRevokeLicense: true` (was false), consistent with "Possession Of Defaced VIN Plate." Convictions on this charge will now result in driver's license revocation during court sentencing.

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
