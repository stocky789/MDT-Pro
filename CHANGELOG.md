# Changelog

## [0.9.3.0] — 2026-03-07

- **Fixed: Wanted status in Person Search** — Wanted status from PR dispatch now syncs to MDT (refresh on `OnPedRanThroughDispatch` and re-encounter). Clears correctly on arrest report and court resolution; synced to CDF so MDT and PR stay aligned.
- **Fixed: Court plea type reverting** — Plea selection (Guilty/Not Guilty/No Contest) was not being saved for pending cases. Added a "Save Plea & Notes" button so changes persist when closing the window. Force Resolve now also sends the current plea and outcome notes from the UI, so the selected plea is used when resolving.
- **Clarification: Calendar and bundled plugins** — Calendar, Vehicle Search, and other bundled plugins only appear in the sidebar when enabled in Config → Plugins. If they were not visible, they were disabled—not missing due to a bug.
- **New: Firearms Check** — New page to search firearms by serial number or owner name. Uses data from pat-downs and dead-body searches (Policing Redefined `GetPedSearchItems`).
- **New: Registered Firearms** — Person Search now shows a "Registered Firearms" section listing weapons linked to the searched person.
- **Firearm data:** Weapon names come from the game native (`GET_WEAPON_NAME_FROM_HASH`) so model names match what players see in-game.
- **Firearm records** stored in SQLite (`firearm_records` table, schema 15). Upsert by owner + serial + weapon hash.
- **Scratched serial numbers** — Firearms with PR `EFirearmState.ScratchedSN` are stored with `SerialNumber = null` and cannot be searched by serial; they appear in owner/Person Search as "Serial: Scratched". Schema 17 adds `IsSerialScratched`.
- **Build script:** The `Dependencies` folder (including SQLite DLLs) is now copied into the Release folder so the full mod package includes all required files.
- **UI:** Firearms Check menu icon updated to a pistol/sidearm icon.
- **Fixed:** Bug fixes for ALPR and Policing Redefined event integration.
- **Drug records** — Pat-down and dead-body search now capture `DrugItem` from PR `GetPedSearchItems`. Stored in `drug_records` table. Person Search shows "Substance History" when drugs are found.
- **Vehicle search records** — Poll-based capture of vehicle search items (weapons, drugs, contraband) when PR reports a vehicle as searched. Stored in `vehicle_search_records`. Vehicle Search shows "Search Results (Contraband)".
- **PR events** — Subscribed to `OnFootTrafficStopEnded`, `OnPedAskedToExitVehicle`, `OnDriverAskedToTurnOffEngine`. Procedural actions (e.g. "Asked to exit vehicle") added to Identification History.
- **OnPedReleased safety** — Avoids dereferencing ped when PR reports released ped may no longer exist.
- **CDF removal events** — Subscribes to `OnPedDataRemoved` and `OnVehicleDataRemoved` to prune in-memory lists when entities despawn (keeps SQLite/keepIn* intact).
- **CDF sync** — Citations count and TimesStopped now sync to CDF when updating ped from MDT.
- **Vehicle schema** — VIN Status (Valid/Scratched), Make, Model, PrimaryColor, SecondaryColor added to vehicles table and Person/Vehicle search UI.
- **BOLO management** — New REST endpoints: `POST /post/addBOLO` and `POST /post/removeBOLO`. Requires vehicle to be in-world.
- **Court evidence** — Drug possession adds `courtEvidenceDrugsBonus` and "Drugs Found on Person" in court case evidence breakdown.
- **Schema 16** — Database migration for new tables and columns.
- **Settings & Officer Profile** — Officer information, shift controls, and career stats moved to a separate "Officer Profile" taskbar button. Settings panel now focuses on Plugins & Config only.
- **Officer status bar** — Header now shows a compact officer info strip to the right of the badge: First Name, Last Name, Badge Number, Rank, Call Sign, Department. Fills from Officer Profile; updates when you save.
- **Config page improvements** — Plain-English labels and tooltips for all config options; grouped sections; preset dropdowns with optional custom values; filter search; collapsible sections; Revert button to discard unsaved changes; success/error feedback on save.
- **RageHook compliance** — Cross-checked Ped/Vehicle usage against Rage Plugin Hook docs: `Vehicle.Driver` null-checked before use; `Exists()` used for pre-persist validation (handles null safely per Rage); all native calls (e.g. `GET_WEAPON_NAME_FROM_HASH`) run in GameFiber/game thread context; PR event handlers validate handleables before access.
- **New: Department Styling plugin** — Choose an MDT theme based on your in-game department. Options: San Andreas Government (default), LSPD, SAFD, BCSO, LSSD, SAHP, FIB. Each theme applies department-specific colors, badge imagery, and animated backgrounds to the MDT. Enable in Config → Plugins; select theme in Settings → Department Theme.
- **Department Styling: Animated backgrounds** — Replaced static wallpaper images with a canvas-based particle animation. Floating particles in the department accent color drift slowly across the desktop for a calm, modern look.
- **Department Styling: Config page theming** — The Config and Plugins (customization) page now follows the selected department theme.
- **Fixed: Overlay click targeting** — Window controls (close, minimize, maximize) and the Settings department dropdown were not clickable; clicks were hitting the sidebar badge instead. Fixed by ensuring the overlay stacks above main content.
- **Firearms Check icon** — Updated to a rifle/sidearm icon (from SVG Repo) for clearer identification.
- **Sidebar icon consistency** — All desktop menu icons (Person Search, Vehicle Search, Firearms Check, Reports, etc.) now use the same color.
- **New: Court license revocations** — Upon conviction, the court now orders license revocations based on California law. Driver's license, firearms permit (CCW), and sport fishing privileges can be revoked depending on the charges. Revocations are stored in the court outcome and applied to the offender's ped record.
- **License revocation rules (CA-based):** Driver's license revoked for charges with `canRevokeLicense` (DUI, reckless driving, hit-and-run, evading, etc.). Firearms permit: lifetime for felonies and domestic violence; 10 years for violent misdemeanors (assault, battery, brandishing). Sport fishing revoked for fish/wildlife code violations.
- **Court UI** — Convicted cases display a "License Revocations Ordered" section listing all court-ordered revocations in the disposition.
- **Ped persistence** — Revoked status is saved to SQLite and reflected in Person Search (License Status, Weapon Permit Status).
- **CDF/PR sync** — Full bidirectional sync so Policing Redefined and Common Data Framework stay aligned with MDT data. Driver's license, weapon permit, fishing permit, and hunting permit status now sync from MDT to CDF on ped update, court conviction, and re-encounter.
- **Re-encounter parity** — When a previously convicted person is re-encountered, their revoked licenses/permits are applied from our SQLite record to CDF/PR so traffic stops and ID checks show the correct status with no conflicts.
- **Schema 18** — `LicenseRevocations` column added to `court_cases` table (JSON array of revocation strings).

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
