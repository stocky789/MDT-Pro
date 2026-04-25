# MDT Pro

A Police Computer Plugin for LSPDFR. MDT Pro runs a local web server when you go on duty, so you can use the MDT (Mobile Data Terminal) from any browser—on the same machine or over your network.

![MDT Pro overview](Screenshots/Overview.png)

## Requirements

- **LSPDFR**
- **CommonDataFramework (CDF)** — **always required.** The plugin will not load without it. This applies to every setup, including if you use StopThePed and Ultimate Backup instead of Policing Redefined.
- **CalloutInterfaceAPI.dll** — **required** for the plugin to start. It must be in the GTA V **root** (next to `GTA5.exe`) or in `plugins/LSPDFR/` (same rule as other LSPDFR dependencies).
- **Newtonsoft.Json.dll** — **required** for the plugin to start. Same search locations as above (release builds place it beside `MDTPro.dll` under `plugins/LSPDFR/`).
- **Callout Interface** (the plugin) — **optional** for loading MDT Pro. If it is not installed, MDT Pro still runs; the **Active Call** page has no live callout feed (location, priority, messages, etc.) from Callout Interface.

### Stops, backup, and citations — choose **one** integration path

MDT Pro is built to work with **either** Policing Redefined **or** the StopThePed + Ultimate Backup combination. **Do not run Policing Redefined together with StopThePed + Ultimate Backup** — keep PR **uninstalled** (or disabled) on that setup. Mixing them is unsupported and can cause conflicting or duplicate behavior.

| Path | What to install | Notes |
|------|-----------------|--------|
| **Policing Redefined** | **Policing Redefined** | Stops, arrests, citations, and backup (when you choose PR in MDT Pro) use PR’s APIs. |
| **StopThePed + Ultimate Backup** | **StopThePed**, **Ultimate Backup** | Stops and citation handoff use StopThePed; **Quick Actions → Backup** uses **Ultimate Backup** when that integration is selected (or **Auto** when Policing Redefined is not present). Configure under **Settings (gear) → Config and Plugins** (or `/page/customization`). **CDF is still required.** **Do not install Policing Redefined** on this profile. |

On **Config and Plugins**, set **Traffic stops & events** to match your stop/traffic mod (**Auto**, **Policing Redefined**, or **StopThePed**) and **Backup (Quick Actions)** to match your backup mod (**Auto**, **Policing Redefined**, or **Ultimate Backup**). StopThePed is not a backup provider—only the two rows above apply.

## Building (for developers)

To build the plugin from source:

1. **Restore packages**  
   From repo root:  
   `dotnet restore MDTProPlugin\MDTPro.sln`  
   (Or open the solution in Visual Studio; it restores automatically.)

2. **Reference DLLs from game**  
   Create a `References` folder in the **repo root** and copy these into it (names must match; compile-time only; not committed). Typical sources are your GTA V directory:
   - `PolicingRedefined.dll` (e.g. from `plugins\LSPDFR\` — needed to **compile** the plugin; your **runtime** install can still use only StopThePed + Ultimate Backup if you prefer)
   - `LSPD First Response.dll` (e.g. from `plugins\`)
   - `IPT.Common.dll` (from the game root, same folder as `GTA5.exe`)

   **CalloutInterfaceAPI:** the project references `MDTProPlugin\lib\CalloutInterfaceAPI\CalloutInterfaceAPI.dll` (see `MDTPro.csproj`). That folder is **not** in the repository — add `CalloutInterfaceAPI.dll` there yourself (e.g. extract it from the [CalloutInterfaceAPI](https://www.nuget.org/packages/CalloutInterfaceAPI) package). You do not need a copy in `References`. At **runtime** you must deploy `CalloutInterfaceAPI.dll` to the game (see **Installation**). The **Callout Interface** plugin (separate download) is optional and only needed for a full **Active Call** experience.

   Other dependencies (CommonDataFramework, Newtonsoft.Json, System.Data.SQLite, etc.) come from NuGet. `References` is in `.gitignore` (each dev uses their own game copy).

   **StopThePed API check (optional):** The repo can include `STP/StopThePed.dll` (gitignored; same file the mod ships). After you upgrade StopThePed, run `powershell -NoProfile -File scripts/verify-stp-api.ps1` to confirm `STPEvents.cs` still matches the event names on `StopThePed.API.Events` in the DLL. Run `powershell -NoProfile -File scripts/dump-stp-api.ps1` and **redirect output** if you want a text snapshot of `StopThePed.API` types, methods, and events (the script prints to the console; it does not write a file in the repo).

3. **Build**  
   From repo root:  
   `dotnet build MDTProPlugin\MDTPro.sln -c Release`  
   Or open `MDTProPlugin\MDTPro.sln` in Visual Studio and build **Release**.  
   Output is in `Release\plugins\lspdfr\MDTPro.dll` and `Release\MDTPro\` (web UI is copied automatically).

   Optional: run `.\build.ps1` for a clean full build (includes the **native Windows MDT** under `Release\MDTProNative\`). Use `.\build.ps1 -Deploy` to build and copy into your GTA V folder (pass `-GamePath "D:\Games\GTA V"` if your install is elsewhere). Only use `.\build.ps1 -SkipWindowsApp` if you intentionally want to omit the native desktop app from the output.

## Installation

- **OpenIV:** Install or uninstall with the `MDTPro-*.oiv` / `MDTPro-*-Uninstall.oiv` packages from a release build. For full removal, delete the `MDTPro` folder after uninstalling if anything remains.
- **Manual:** Extract all files and folders from the ZIP into the GTA V main directory (the folder containing `GTA5.exe`).

Your GTA V folder should have:
- `plugins/LSPDFR/MDTPro.dll`
- `plugins/LSPDFR/Newtonsoft.Json.dll` (or a copy in the game root; the loader checks both)
- `CalloutInterfaceAPI.dll` (in the game root or `plugins/LSPDFR/`)
- `System.Data.SQLite.dll` (in the GTA V root, same folder as `GTA5.exe`)
- `x64/SQLite.Interop.dll` (in the `x64` folder inside the GTA V root)
- `MDTPro/` folder (web UI)

**SQLite placement:** The native loader looks in the application directory (GTA V root), not in `plugins/LSPDFR`. If you see "Could not load file or assembly 'System.Data.SQLite'" or `DllNotFoundException`, ensure both SQLite files are in the root (and `x64\` for the Interop).

## Updating

- Overwrite the plugin files with the new version (replace the contents of `plugins/LSPDFR/` and the `MDTPro` folder with the updated files, or merge so that new files are added and existing ones updated). Your existing `MDTPro/data/` and `MDTPro/config.json` are preserved if you don’t delete them.
- **Config:** Existing installs keep the values already in `MDTPro/config.json`. To use a **new default** (e.g. a lower-CPU WebSocket update rate), either:
  - Open the MDT in your browser → **Settings** (gear) → **Config and Plugins** → **Config** tab, change the setting (e.g. `webSocketUpdateInterval` to `1000`), then **Save**, or
  - Edit `MDTPro/config.json` in a text editor and set the value (e.g. `"webSocketUpdateInterval": 1000`), then save the file.
- No need to wipe data or config unless you want a full reset (see [Resetting data](#resetting-data-optional)).

### Resetting data (optional)

- To clear saved MDT data (SQLite DB, shifts, etc.), delete or empty files under `MDTPro/data/` while the game is closed, or use the `ClearMDTProData` utility in the `ClearMDTProData/` folder (run it from your GTA V directory so paths resolve). Deleting `MDTPro/config.json` resets config to defaults on next load.

### Logs and troubleshooting

- **Log file:** `MDTPro/MDTPro.log` inside your GTA V folder (same folder as `GTA5.exe`). The plugin writes startup info, errors, and (when a report save fails) the full exception and stack trace.
- **RAGE/LSPDFR:** In-game notifications and `RAGEPluginHook.log` in the GTA V folder can also show plugin load or runtime errors.

## Setup

- Go on duty with LSPDFR. MDT Pro will start its web server and show in-game notifications with the addresses to open the MDT.
- If you miss the notifications, addresses are also written to `MDTPro/ipAddresses.txt`.
- Open the MDT in any browser. Chromium-based browsers (e.g. Chrome, Brave) work best. Use one of the shown addresses—if one fails (e.g. firewall), try another.
- The default port is **9000**. You can change it (and other options) on **Config and Plugins** (see [Customization](#customization)).

### Connecting from another device

If you can't reach the MDT from another device (phone, tablet, another PC on your network), it may be a **Windows Firewall** issue—this is the firewall on your game PC, not your router. Add an inbound rule in Windows Firewall to allow the port MDT Pro uses (9000 by default, or whatever you set in **Config and Plugins**). Without this rule, Windows may block incoming connections from other devices on your network.

### Setup using Steam overlay

- In Steam: **Steam → Settings → In-Game**.
- Enable *Enable the Steam Overlay while in-game*.
- Set *Overlay shortcut key(s)* to the key you want for opening the overlay (and thus the MDT).
- Set *Web browser home page* to `http://127.0.0.1:9000` (or the address and port shown by MDT Pro).

## UI Usage

### Desktop and Control Panel

- The **taskbar** shows the badge, current location, a **Control Panel** entry (gear icon; default label is “Control Panel” in `language.json`), and the clock. Click it to open the **Control Panel** overlay.
- In the Control Panel you can:
  - Enter and save **Officer Information** (name, badge number, rank, call sign, department). Use *Fill from Game* to pull your character details from the game when supported.
  - **Start** or **End** your current shift.
  - View **Career Statistics** (totals from completed shifts and reports).
- The **Config and Plugins** link in the Control Panel opens the customization page in a new tab (same as `/page/customization`).

### Reports

- All reports include **general**, **officer**, and **location** sections. These are auto-filled from your officer info and current location but can be edited.
- The **report ID** is generated automatically and cannot be changed.
- Use the **status** filter in the reports list (e.g. active, completed, canceled). Canceled reports are treated as deleted but remain viewable.
- Reports created **while a shift is active** (after you have clicked *Start Shift* in the Control Panel) are **attached to that shift** and appear in Shift History.
- A **notes** section is available for incident description or extra details.
- Report drafts are auto-saved in the browser; if you leave and return to the create page within 24 hours, you may be prompted to restore the draft.

#### Report types

- **Incident** — General reporting; offender, witness, and victim names optional.
- **Injury** — Injured party, severity, treatment. Create from Reports or Person Search (name prefilled).
- **Traffic Incident** — Collisions and multi-vehicle incidents. Available under Reports.
- **Impound** — Plate, owner, reason, tow company. Create from Reports or Vehicle Search (vehicle prefilled).
- **Property & evidence** — Subjects and seized items (e.g. drugs, firearms). Create from **Reports** like the other main types.

#### Citation and arrest reports

- The **charges** you add are stored with the report and, if an offender is set, are added to that person’s record for future lookups.
- **Issuing citations in-game:** With **Policing Redefined**, citations can be delivered from the PR ped menu when you close the citation in the MDT. With **StopThePed** (and **without** Policing Redefined), use the MDT citation handoff flow (e.g. in-game key set in `MDTPro.ini`) so StopThePed receives the ticket. Do not run PR and StopThePed together for this—pick one path (see [Requirements](#requirements)).

### Person Lookup (Person Search on the desktop)

- Search by name to see information about that person (from MDT Pro and, when available, CDF). Create an Injury Report with name prefilled via "New Injury Report".
- The **history** section lists citations and arrests. Click a citation or arrest entry to **create a new report** for that ped (pre-filled where applicable).
- **Callout suspects:** Person records normally come from CDF (e.g. peds you stop, vehicle owners). Some callout packs generate a suspect name from evidence (e.g. “mobile phone associated with Joe Thomas”) but do *not* register that person with CDF, so they would not appear in Person Search. MDT Pro tries to add name-only “stub” records when it sees phrases like “associated with …”, “sightings of …”, etc. in the **Active Call** message or additional messages. You can turn this off in config with `addCalloutSuspectNamesFromMessages: false`. For full integration, callout authors can register suspects with CDF so they appear across all CDF-using plugins.

### Vehicle Lookup

- Search by **license plate** or **VIN** to see vehicle and related information. Create an Impound Report with vehicle prefilled via "Create Impound Report".
- Click the **owner** in the basic information area to open Person Lookup for the vehicle’s registered owner.

### Firearms Check

- Lookup by serial or other fields where supported. Listed on the desktop alongside Person Search and Vehicle Search.

### BOLO & Backup

- **BOLO Noticeboard** — Add or remove BOLOs (plate, reason, duration) without the vehicle in front of you. BOLOs are stored on vehicles in **CDF / MDT**; the in-game **ALPR** scanner can flag the same plate as a BOLO when you read it.
- **Quick Actions** — Panic, Backup (patrol, traffic stop, transport, tow, etc.), and Clear ALPR from a **bottom-center** floating bar (fixed position; the whole bar can be turned off in **Config and Plugins**). Backup is sent through **Policing Redefined** or **Ultimate Backup** via **Backup (Quick Actions)** in config (**Auto** uses PR when that mod is available, otherwise Ultimate Backup when available).

### Shift History

- View all past shifts and the **reports linked to each shift** (reports created while that shift was active).

### Court

- View and manage **court cases** derived from arrest reports. Arrests start as Pending; attach reports (Incident, Injury, Citation, Traffic Incident, Impound, Property & evidence) as evidence, then close the arrest to create the case.
- Filter and sort by status, case number, ped name, or report ID.
- Cases can be updated (e.g. status, resolution); the system supports docket management, sentencing, and related options configurable in `config.json`.

### Map (GPS)

- Shows your **current position** on a map (updated via WebSocket while the game is running).
- **Route** from your position to a chosen point; turn-by-turn instructions and map display are provided. Uses in-game road data and configurable routing options.

### Active Call

- Shows details of the **current callout** when **CalloutInterface** is installed: status (Pending, Accepted, En Route, Finished), location (postal, street, area, county), priority, message, timeline, and expandable cards. Set a GPS waypoint and use Accept/En Route when your callout system supports it.
- Without CalloutInterface, the page opens but does not receive callout data.
- When callout messages mention a suspect by name (e.g. “associated with Joe Thomas”), MDT Pro can add that name to Person Search so you can look them up; see [Person Lookup](#person-lookup-ped-search).

## Customization

The **Config and Plugins** page (linked from the Control Panel, or open `/page/customization` in a new tab) lets you:

- **Change configuration** — e.g. HTTP port, in-game time vs real time, taskbar clock, window size, map options, court and evidence weights, and more. Settings are stored in `MDTPro/config.json`.
- **Manage plugins** — enable or disable MDT Pro plugins (see [Plugins](#plugins)).

### ALPR (Automatic License Plate Recognition)

- ALPR is an **optional** in-game feature. Enable it in **Config and Plugins** (browser) or via the **in-game Settings menu** (default key **F7**; set in `MDTPro/MDTPro.ini`).
- When enabled and you are **on duty** and in a **police vehicle**, the game scans nearby vehicles and flags plates (e.g. stolen, expired registration/insurance, owner wanted). Flagged hits can show an in-game HUD panel and optional sound or notification.
- **Where flags come from:** Stolen/expired/wanted are read only from **CDF** and the **MDT database** (vehicles you’ve run or marked in the MDT). Scan distance and angle are tuned in the plugin code (`AlprDefaults`; not a `config.json` setting). If you rarely see hits, vehicles may be outside range or not facing the sensors; building from source is the way to change those limits.
- The **ALPR plugin** (in `MDTPro/plugins/ALPR/`) adds **popups in the MDT** when the in-game scanner gets a hit, so you can see details in the browser. Enable the plugin on **Config and Plugins**.

## Plugins

Plugins extend MDT Pro by injecting JavaScript and CSS and optionally adding new pages.

### Using a plugin

- Place the plugin folder inside `MDTPro/plugins`.
- Enable the plugin on **Config and Plugins**.

### Creating a plugin

The plugin’s URL id is the **folder name** under `MDTPro/plugins` (e.g. `ALPR` → `/plugin/ALPR/...`); the server sets `id` from the directory even if you add an `id` field in `info.json`.

#### Plugin folder structure

```
Plugin Name
    │   info.json
    │
    ├───pages
    │       page.html
    │
    ├───scripts
    │       script 1.js
    │       script 2.js
    │
    ├───styles
    │       style 1.css
    │       style 2.css
    │
    └───images (optional)
            badge.png
```

Multiple pages, scripts, and styles are supported per plugin.

- HTML in `pages` is served at `/plugin/<pluginId>/page/<fileName>`
- JS in `scripts` is served at `/plugin/<pluginId>/script/<fileName>`
- CSS in `styles` is served at `/plugin/<pluginId>/style/<fileName>`
- Images in `images` (optional) at `/plugin/<pluginId>/image/<fileName>` (png, jpg, svg)
- In JavaScript, get the plugin ID with: `const pluginId = document.currentScript.dataset.pluginId`
- Scripts and styles are loaded on the main index page when the plugin is enabled on **Config and Plugins**.

#### info.json example

```json
{
  "name": "Plugin Name",
  "description": "An MDT Pro plugin",
  "author": "Your Name",
  "version": "2.0.1"
}
```

#### Plugin API

Use the functions in `MDTPro/main/scripts/pluginAPI.js`. Open that file in an editor for IntelliSense. All API functions are on the `API` object.

#### Example: add a new page

```js
const pageSVG =
  '<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 1 1" xml:space="preserve"><!-- valid SVG --></svg>'

API.createNewPage('test', 'Test Page', pageSVG, initTestPage) // requires test.html in pages folder

function initTestPage(contentWindow) {
  contentWindow.document.body.innerHTML = 'Test page loaded'
}
```

## License

MDT Pro is licensed under the [Eclipse Public License - v 2.0](LICENSE).

The following files/folders are excluded and licensed under the [MIT License](MIT%20LICENSE): `MDTPro/arrestOptions.json`, `MDTPro/citationOptions.json`, `MDTPro/language.json`, `MDTPro/config.json`
