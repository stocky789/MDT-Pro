# Changelog

## [0.9.3.0] — 2026-03-07

- **New: Firearms Check** — New page to search firearms by serial number or owner name. Uses data from pat-downs and dead-body searches (Policing Redefined `GetPedSearchItems`).
- **New: Registered Firearms** — Person Search now shows a "Registered Firearms" section listing weapons linked to the searched person.
- **Firearm data:** Weapon names come from the game native (`GET_WEAPON_NAME_FROM_HASH`) so model names match what players see in-game.
- **Firearm records** stored in SQLite (`firearm_records` table, schema 15). Upsert by owner + serial + weapon hash.
- **Build script:** The `Dependencies` folder (including SQLite DLLs) is now copied into the Release folder so the full mod package includes all required files.
- **UI:** Firearms Check menu icon updated to a pistol/sidearm icon.
- **Fixed:** Bug fixes for ALPR and Policing Redefined event integration.

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
