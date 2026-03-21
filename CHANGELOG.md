# Changelog

All notable changes to MDT Pro are documented here.

---

## [0.9.6.0] — 2026-03-21

### Property and Evidence Receipt (Seizure Reports)

- **New report type** — Create **Property and Evidence Receipt** reports to document seized drugs and firearms in detail. Uses realistic police terminology (property and evidence receipt). **Multiple subjects** supported via Recent IDs (same as Person Search) and manual add. **Drugs seized** and **Firearms seized** use an add-button pattern (like charges): select type, click Add; each entry in a list with delete. **Drug quantities** (Baggie, Bundle, Grams, Ounce, Pill, etc.) align with Policing Redefined and street terminology. Optional other contraband notes. Attach to arrests for court evidence.
- **Charge-specific evidence** — Court evidence now requires matching seizure types: e.g. a Possession of Heroin charge only gets drug evidence if Heroin is listed in the attached seizure report. Possession of Fentanyl requires Fentanyl documented. Generic charges (Possession Of Controlled Substance For Sale, Trafficking, Paraphernalia, etc.) accept any drug type. Firearm charges require at least one firearm type listed in the seizure report.
- **Evidence breakdown in court** — Court evidence view shows specific types when documented: "Drugs found (Heroin, Cocaine)" or "Firearms seized (Pistol)" instead of generic labels. Config: `courtEvidenceSeizureReportBonus`, `courtEvidenceDrugQuantityBonus` (higher quantities—bundle, kilo, etc.—add evidence).
- **Arrest integration** — In the arrest report's Attached reports section, use **Create Property and Evidence Receipt** to document contraband; subject pre-fills from the offender and the report auto-attaches on save. The **Evidence seized** section audits all attached PER reports and displays a simple list of seized items (drugs with quantity, firearms, other contraband).
- **Removed arrest checkboxes** — The drugs/firearms tickboxes on arrest reports are removed. Use Property and Evidence Receipt reports instead. Existing arrests with DocumentedDrugs/DocumentedFirearms remain supported via court fallback (generic evidence).
- **Verdict rationale** — Court sentencing text now reflects specific types when available (e.g. "Heroin and Cocaine were recovered" instead of "controlled substances were found").

### Person Search

- **ID photo** — Person Search now shows a photo of the person's in-game character model in the Basic Information section. For vanilla GTA V peds, the MDT displays their PED mugshot from the game. If no photo is available (e.g. addon character packs or peds without a stored model), a "No photo available" placeholder is shown instead.

### Firearms Check

- **Recent weapons (serials + names)** — Firearms Check now shows **Recent weapons**: the actual guns you've seen on pat-downs and body searches, with their serial numbers and weapon names. No more "Recent IDs" with person names—just click a weapon (e.g. "ABC123 — Pistol") to look it up.
- **Lookups actually work** — Firearm lookups were failing a lot, especially when a gun was found in a car. That's fixed: searching by serial or owner from Person Search or Firearms Check should work every time now.
- **Guns in vehicle search** — When you find a weapon during a vehicle search, that weapon line is now clickable. Click it to jump straight into Firearms Check and run a search for that gun.

### Vehicle Search

- **Search Results (Contraband)** — Drugs, weapons, and contraband from vehicle searches now show up reliably in the MDT. Previously they often didn't appear.

### Court

- **Verdict & sentencing phrasing** — 200+ wording variants for verdict reasoning, outcome reasoning, and sentencing rationale. Jury vote phrasing no longer repeats "returned a guilty verdict."
- **Evidence accuracy** — Outcome reasoning only mentions evidence that was actually documented. Vehicle damage is no longer inferred from evading charges (suspects can flee on foot). Verdict templates for vehicle damage, weapons, drugs, etc. are excluded unless that evidence was captured or documented in reports.

### Charges

- **Drug charges aligned with Policing Redefined** — Arrest charges and seizure options cover every drug type from PR's API: **Possession Of Amphetamine**, **Possession Of Benzodiazepine**, **Possession Of Controlled Substance** (generic). Seizure options: Amphetamine, Benzodiazepine, Mescaline, Psilocybin. Court evidence mapping matches PR drug types.
- **Property and ID charges (California law)** — Arrest and citation charges: **Possession Of Burglary Tools** (PC § 466), **Possession Of Credit Card Scanning Device** (PC § 502.6), **Possession Of Counterfeit Items** (PC § 475), **Possession Of Stolen Debit / Credit Card**, **Refusing To Provide Identification**, **Failure To Present Drivers License Upon Demand** (VC 12951). Court verdict phrasing reflects these (theft/property, resisting/obstruction, vehicle-related) where applicable.

---

## [0.9.5.1] — 2026-03-17

### Hotfixes

- **"Failed to save report" fixed** — Saving any report (Incident, Citation, Arrest, Injury, Traffic Incident, Impound) could fail with a generic error. This was caused by the database missing columns added in 0.9.5.0. The game now adds those columns automatically when you go on duty, so existing installs are fixed without losing data. New installs get the correct database from the start.
- **Clearer errors when a save fails** — If a report still fails to save, the MDT now shows the real error message from the game instead of only "Failed to save report." Full details are also written to the log file for troubleshooting.
- **Log file (MDTPro.log)** — The log in your GTA V folder (`MDTPro\MDTPro.log`) is now created reliably on first run and when report saves fail the full error and stack trace are written there. Helpful for tracking down issues and when reporting bugs.
- **WebSocket handler leak** — Callout and shift-history event handlers are now unsubscribed when an MDT client disconnects (in a `finally` block). Previously, each client that opened the callout or map page added a handler that was only removed when the handler ran and saw the socket closed. Handlers could accumulate over time; when a callout fired, every handler ran on the LSPDFR thread and blocked with `SendData(...).Wait()`, leading to freezes and other plugins crashing after extended play.
- **Nearby vehicles and game thread** — The `/data/nearbyVehicles` API no longer touches game entities (e.g. `Main.Player`, vehicle `Holder`) from the HTTP thread. Nearby vehicles with distance are now computed on the game thread in `SetDynamicData()` and cached; the API returns the cached list. This avoids cross-thread RAGE API use that could cause instability when Vehicle Search was open with 1-second auto-refresh.
- **Real-time refresh interval** — Person Search and Vehicle Search auto-refresh interval increased from 1 second to 3 seconds to reduce server and game load while keeping Recent IDs, Nearby Vehicles, and search history up to date.

---

## [0.9.5.0] — 2026-03-15

### Court & evidence

- **Evidence from attached reports** — Attach Incident, Injury, Citation, Traffic Incident, or Impound reports to an arrest or court case to add evidence. Each report type contributes to the case; amounts are tunable in Config → Court. Longer arrest notes also strengthen the case.
- **Report relevance** — Reports that match the case (e.g. incident naming the defendant, or traffic/impound for vehicle-related charges) count for more. Other attached reports still add a smaller amount so nothing is wasted.
- **Homicide and death documentation** — For murder or manslaughter, conviction is capped when no attached report documents a death. Attach an Injury report with severity "Fatal" or treatment "DOA" / "Pronounced deceased" so the case is treated as having documented death. Injury reports now include a "Fatal" severity option.
- **Evidence seized** — Arrest reports have an **Evidence seized** section: check **Drugs found / documented** and **Firearm(s) found / documented** so court evidence reflects what you actually seized, even when the game or Policing Redefined didn't log it.
- **Charge types and license revocations** — Court logic correctly handles all charge types (felony, violent misdemeanor, vehicle-related, drug-related, firearm-related, homicide) for license revocations and evidence weight. Every arrest and citation charge is covered.
- **Evidence captured during pursuit** — Attempted to flee, resisted arrest, vehicle damage, assault on the officer, and "had weapon" are now recorded when the suspect surrenders or is seen fleeing, not only at the moment of cuffing. This fixes cases where the suspect was no longer fleeing or armed by the time you arrested them.
- **Resisted arrest** — Resistance is now also inferred from assault on the officer, so "Resisted Arrest" appears in court even without Policing Redefined.
- **Arrest workflow** — New arrests start as **Pending**; save and keep attaching reports, then **Close arrest (submit for court)** to create the case. You can attach reports to the case until the hearing. Attached reports show a short summary (type, date, context) on the arrest and court pages.
- **Court outcomes** — Verdict and sentencing text reflect all evidence (weapon, warrant, fleeing, resistance, assault, intoxication, drugs, supervision, etc.). **Sentencing Rationale** explains aggravating and mitigating factors, recidivism, and district policy. Convicted cases show a dedicated "Sentencing Rationale" section.
- **Use of Force** — Arrest reports have a Use of Force section (type, justification, injury, witnesses). When filled in, the court case shows "Use of Force Documented" and receives the configured evidence bonus.

### Reports

- **Injury Report** — New report type: injured party, injury type, severity, treatment, and context. Available under Reports and from Person Search via "New Injury Report" with name prefilled.
- **Traffic Incident Report** — New report type for collisions and multi-vehicle incidents: drivers, passengers, pedestrians, vehicles, injury details, and collision type. Available under Reports.
- **Impound Report** — New report type: plate, model, owner, VIN, reason, tow company, and impound lot. Available under Reports and from Vehicle Search via "Create Impound Report" with vehicle prefilled.
- **Prefill** — Creating an Injury report from Person Search or an Impound report from Vehicle Search opens the form with that person or vehicle already filled in. "Nearby vehicles" on the impound form now correctly fills plate and details when you select a vehicle.

### BOLO & backup

- **BOLO from the noticeboard** — Add or remove BOLOs from the BOLO Noticeboard without needing the vehicle in front of you (plate, optional model, reason, duration). Only active (non-expired) BOLOs are shown.
- **BOLO and CDF** — When a vehicle with a BOLO'd plate is seen or stopped, the BOLO is synced to Common Data Framework. Vehicle Search and ALPR show BOLOs for in-world and noticeboard-only plates; ALPR flags a hit as "BOLO" and alerts.
- **Request backup from the MDT** — Request backup (patrol, traffic stop, transport, tow, etc.) from the MDT. Works with Policing Redefined. Quick Actions bar (bottom-right) offers one-tap Panic, Backup, and Clear ALPR; backup can use Code 1, 2, or 3.

### UI & workflow

- **Active Call** — Active Call page shows status (Pending, Accepted, En Route, Finished), a short timeline, and expandable cards. Set a GPS waypoint and use Accept / En Route when your callout system supports it.
- **Arrest status** — Arrest reports no longer show a separate "Open" status; use **Pending** until you close for court. You can attach and detach reports while the arrest is Pending.
- **Court case view** — Resolved cases show per-charge outcome (Convicted, Acquitted, Dismissed) and total fine and jail time for convicted charges only. Evidence breakdown lists all flags (e.g. intoxicated, fleeing, pat-down, illegal weapon).
- **In-game notifications** — Update checker and other messages use a police-style icon. MDT Pro logo appears in the browser tab when the MDT is open. Court trial heard: when a trial is resolved, an in-game notification appears ("Trial X for Firstname Lastname has been heard - to see the outcome check the MDT").
- **Person Search & Vehicle Search auto-refresh** — Recent IDs, Nearby Vehicles, and Recent Searches refresh every 1 second while the window is open. Freshly gathered IDs and vehicles appear without closing and reopening.
- **Copy Report ID** — A Copy button next to the Report ID in the General Information section copies the report ID to the clipboard for easy pasting elsewhere.
- **OpenIV** — Install or uninstall with the .oiv package. For full removal, delete the MDTPro folder from your GTA V directory after uninstalling.

### Bug fixes

- **Court** — Plea is saved correctly (e.g. "Guilty"). Empty or missing plea is treated as Not Guilty. Acquitted or dismissed cases no longer show sentencing. License revocations apply correctly.
- **Impound prefill** — Clicking a nearby vehicle when creating an Impound report now correctly populates plate and vehicle fields.
- **Arrest: attach reports after save** — You can attach and detach reports on an arrest after saving, as long as the arrest is still Pending (not yet closed for court).
- **ALPR** — In-game ALPR HUD shows vehicle color names (e.g. "Red", "Black / White") instead of numeric values. Scan and read range increased for easier reading at distance.
- **State ID and "Ask for all docs"** — Showing a State ID on a foot stop or "Ask for all documents" on a vehicle stop now correctly adds the person to Person Search.
- **VIN and license revocations** — Convictions for VIN tampering or defaced VIN now result in driver's license revocation at sentencing.
- **BOLO** — Creating a BOLO from the noticeboard and later encountering that plate in world now correctly shows the BOLO on Vehicle Search and syncs to CDF. Vehicle Search no longer misses noticeboard-only BOLOs when the vehicle is in world.
- **Recent IDs** — Patting down a suspect now adds the person to Recent IDs in Person Search. Previously only asking for a driver's license (or other ID-given events) triggered this.

## [0.9.3.0] — 2026-03-07

### Major features

- **Firearms Check** — New page to search firearms by serial number or owner. Uses data from pat-downs and body searches (Policing Redefined).
- **Registered Firearms** — Person Search shows a "Registered Firearms" section for weapons linked to the person.
- **Drug records** — Pat-down and body search capture drugs from Policing Redefined. Person Search shows "Substance History" when drugs were found.
- **Vehicle search records** — Vehicle search results (weapons, drugs, contraband) from Policing Redefined are stored and shown on Vehicle Search as "Search Results (Contraband)."
- **Vehicle details** — VIN status, make, model, and colors added to vehicles and shown on Person/Vehicle Search.
- **BOLO management** — Add and remove BOLOs via the MDT (vehicle must be in world).
- **Officer Profile** — Officer info, shift controls, and career stats moved to a separate "Officer Profile" button. Config focuses on plugins and settings.
- **Config page** — Plain-English labels and tooltips, grouped sections, presets with optional custom values, search, collapsible sections, Revert button, and save feedback.
- **Department Styling** — Choose an MDT theme (LSPD, SAFD, BCSO, LSSD, SAHP, FIB, etc.) with department colors, badge imagery, and backgrounds. Enable in Config → Plugins; select in Settings → Department Theme.
- **Court license revocations** — On conviction, the court can revoke driver's license, firearms permit, and sport fishing based on the charges (California-style rules). Revocations are stored and applied to the person's record.

### Other improvements

- **ALPR** — Tuning and behavior improvements; sound when a flagged vehicle is read; vehicle color on ALPR card.
- **Calendar and plugins** — Calendar and other bundled plugins only appear in the sidebar when enabled in Config → Plugins.
- **Firearms** — Weapon names from the game; scratched serials shown as "Serial: Scratched" and excluded from serial search.
- **Officer status bar** — Header shows officer name, badge, rank, call sign, department.
- **Court UI** — Convicted cases show "License Revocations Ordered."
- **Person Search** — Revoked license and weapon permit status from court are saved and shown.
- **CDF/PR sync** — Driver's license, weapon permit, and fishing/hunting status sync between MDT and Policing Redefined / Common Data Framework. Re-encountered people get the correct status from the MDT.

### Bug fixes

- **Wanted status** — Wanted status from dispatch now syncs to the MDT and clears correctly on arrest and court resolution.
- **Court plea** — Plea selection was not saved for pending cases. "Save Plea & Notes" added; Force Resolve uses the selected plea.
- **ALPR** — Proximity and front/rear plate reading improved; config defaults fixed so read range is correct.
- **Overlay clicks** — Window controls and Settings dropdown were sometimes not clickable; fixed.

## [0.9.2.0] — 2026-03-07

- **Fixed:** Registration and insurance status now sync correctly with Policing Redefined. Re-encounter no longer overwrites revoked/expired status with stale "Valid."
- **Fixed:** SQLite DLLs must be in the GTA V root (same folder as GTA5.exe), not in plugins. Install instructions updated.

## [0.9.1.0] — 2026-03-07

- **Fixed:** SQLite dependencies are now included in the release so the game no longer crashes on going on duty if you didn't have them from another mod.
- **Config:** WebSocket update interval default is now 1000 ms to reduce CPU use. Change it in Config or edit config.json if you want a different value.
- Citations no longer create or update court cases. Arrest reports still create court cases as before. Citation fines still vary at random within the configured range.

## [0.9.0.0] — 2025-03-06 (beta)

- **Initial public beta.** Police MDT with reports, Person Search, Vehicle Search, citations, arrests, court cases, and shift tracking. Web UI served locally; data stored in MDTPro/data (SQLite).
