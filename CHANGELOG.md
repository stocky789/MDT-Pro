# Changelog

## [Unreleased]

- Nothing yet.

## [0.9.5.0] — 2026-03-10

### Court & evidence

- **Evidence from the reports you attach** — Attaching Incident, Injury, Citation, Traffic Incident, or Impound reports to an arrest (or court case) now adds to the case’s evidence. Each type contributes a different amount; you can tune these in Config → Court. Longer arrest report notes also strengthen the case.
- **Homicide and death documentation** — For murder or manslaughter, conviction is harder when there’s no attached report that documents a death. If you attach an Injury report with severity like “Fatal” or treatment like “DOA” or “Pronounced deceased,” the case is treated as having documented death. Injury reports now include a “Fatal” severity option.
- **Traffic Incident and Impound as evidence** — You can attach Traffic Incident and Impound reports to arrests and court cases the same way as Incident, Injury, and Citation. They count toward evidence, especially when the charges are vehicle-related (DUI, GTA, evading, hit-and-run, etc.).
- **Clearer “Attached reports” on arrest and court** — The Attached reports section now briefly explains that only attached reports count as evidence and that you can attach Incident, Injury, Citation, Traffic Incident, or Impound by report ID.
- **Relevant vs other evidence** — Reports that clearly match the case (e.g. incident or citation naming the defendant, or traffic/impound when the charges are vehicle-related) count for more. Other attached reports still add a smaller amount so nothing is wasted (e.g. a stolen firearm mentioned on an incident still helps a drug case a bit).
- **Evidence seized on the arrest report** — Arrest reports now have an **Evidence seized** section with checkboxes: **Drugs found / documented** and **Firearm(s) found / documented**. Checking these feeds into court evidence even when the game or Policing Redefined didn’t log the find, so you can record what was seized.
- **Charge types and court** — The court and evidence logic now correctly treats all charge types (felony, violent misdemeanor, vehicle-related, drug-related, firearm-related, homicide) for things like license revocations and how much weight different evidence has. Every arrest and citation charge type is covered.

### Major features

- **Court evidence from reports and the arrest workflow** — Conviction evidence comes from the reports you file. New arrests start as **Pending**; you can save the arrest and keep attaching reports. When ready, **Close arrest (submit for court)** creates the court case. You can also attach reports to the case before the hearing. After the case is resolved, each charge shows Convicted, Acquitted, or Dismissed, and fines and jail time only count convicted charges. After saving an arrest, a reminder suggests attaching relevant reports.
- **Richer court outcomes** — Verdict and sentencing text now reflect evidence (weapon, warrant, fleeing, resistance, assault, intoxication, drugs, supervision, etc.). **Sentencing Rationale** explains aggravating and mitigating factors, recidivism, jury verdict, and district policy. Unusual outcomes (e.g. guilty with little evidence) get special wording. Convicted cases show a separate “Sentencing Rationale” section on the Court page.
- **Quick Actions Bar** — Small floating bar at the bottom-right with one-tap Panic, Backup, and Clear ALPR. Can be turned on or off in Config. Backup options need Policing Redefined.
- **Request Backup from the MDT** — Request backup (patrol, traffic stop, transport, tow, etc.) from the MDT. Works with Policing Redefined.
- **Active Call** — Active Call page shows each call’s status (Pending, Accepted, En Route, Finished), a short timeline, and expandable cards. You can set a GPS waypoint and use Accept / En Route when your callout system supports it.
- **Create BOLO from the noticeboard** — Add a BOLO from the BOLO Noticeboard without needing the vehicle in front of you. Enter plate, optional model, reason, and duration. You can also remove BOLOs from the list.
- **BOLO system and CDF** — BOLOs are plate-based. When a vehicle with a BOLO’d plate is seen or stopped, the BOLO is applied and synced to Common Data Framework so Policing Redefined and other CDF plugins see it. Vehicle Search and ALPR show BOLOs for in-world and noticeboard-only plates.
- **Use of Force on arrest reports** — Arrest reports have a Use of Force section: type (e.g. Taser, Baton, Firearm), justification, injury yes/no, and witnesses. This is shown in court as “Use of Force Documented” and adds to evidence; the amount is configurable in Config → Court.
- **Prefill when creating reports** — “New Injury Report” from Person Search or “Create Impound Report” from Vehicle Search opens the report with that person or vehicle already filled in.
- **Impound Report** — New report type: plate, model, owner, VIN, reason, tow company (Camel Towing, Davis Towing), and impound lot (Mission Row or Davis). Available under Reports and from Vehicle Search via “Create Impound Report” with vehicle prefilled.
- **Traffic Incident Report** — New report type for collisions and multi-vehicle incidents: drivers, passengers, pedestrians, vehicles (plates and models), injury details, and collision type. Available under Reports.
- **Injury Report** — New report type: injured party, injury type, severity, treatment, and incident context. Available under Reports and from Person Search via “New Injury Report” with name prefilled. Optional import from game (DamageTrackerFramework or ped health/armor) when the injured person is nearby.

### Other improvements

- **Arrest and court UI** — Arrest report shows “Attached reports (evidence for court)” with attach/detach by report ID and **Close arrest (submit for court)** when pending. Court case shows “Attached reports” and attach/detach while the case is pending. Resolved cases show per-charge outcome and total fine and jail time for convicted charges only.
- **Court disposition** — Verdict and outcome reasoning in one section; convicted cases also show “Sentencing Rationale.” Evidence breakdown lists all evidence flags (e.g. intoxicated, fleeing, pat-down, illegal weapon).
- **Court: Use of Force** — If the arrest report has use of force filled in, the court case shows “Use of Force Documented” in evidence and gets the configured bonus.
- **Court: Fleeing** — Suspects who surrender after a chase are now correctly treated as having fled for court evidence.
- **Court: Drunk** — Suspects doing drunk movements or animations are now flagged for court even when the game’s drunk check doesn’t fire.
- **ALPR** — Scan and read range increased so plates are easier to read at distance.
- **In-game notifications** — Update checker and other messages use a police-style icon instead of the LS Customs icon.
- **Browser tab** — MDT Pro logo appears in the browser tab when the MDT is open.
- **OpenIV installers** — Install with the .oiv package and uninstall with the included uninstaller. For full removal, delete the MDTPro folder from your GTA V directory after uninstalling.
- **Backup response code** — Quick Actions backup menu lets you choose Code 1, 2, or 3 for patrol, EMS, traffic stop, and transport.
- **BOLO noticeboard** — Only active (non-expired) BOLOs are shown.
- **ALPR BOLO flag** — When ALPR reads a plate with an active BOLO, the hit is flagged as “BOLO” and triggers an alert.
- **BOLO on Vehicle Search** — Plate search for a vehicle in world now shows any BOLO for that plate and syncs it to CDF.

### Bug fixes

- **Court** — Plea is now saved correctly (e.g. “Guilty” from the UI). Empty or missing plea is treated as Not Guilty. Acquitted or dismissed cases no longer show sentencing. License revocations are applied correctly. Various edge cases in outcome and sentencing text are handled without errors.
- **ALPR vehicle color** — In-game ALPR HUD shows color names (e.g. “Red”, “Black / White”) instead of numeric values.
- **State ID and “Ask for all docs”** — Showing a State ID on a foot stop or using “Ask for all documents” on a vehicle stop now correctly adds the person to Person Search.
- **VIN Tampering** — Convictions for VIN tampering or defaced VIN now result in driver’s license revocation at sentencing.
- **BOLO: noticeboard then in-world** — Creating a BOLO for a plate from the noticeboard (no car nearby), then later having a car with that plate appear in world, now correctly shows the BOLO on that vehicle and syncs to CDF.
- **BOLO: Vehicle Search** — Searching for a plate in Vehicle Search when the vehicle was in world could miss a BOLO that existed only from the noticeboard. BOLOs are now merged and shown (and synced to CDF) correctly.

## [0.9.3.0] — 2026-03-07

### Major features

- **Firearms Check** — New page to search firearms by serial number or owner. Uses data from pat-downs and body searches (Policing Redefined).
- **Registered Firearms** — Person Search shows a “Registered Firearms” section for weapons linked to the person.
- **Drug records** — Pat-down and body search capture drugs from Policing Redefined. Person Search shows “Substance History” when drugs were found.
- **Vehicle search records** — Vehicle search results (weapons, drugs, contraband) from Policing Redefined are stored and shown on Vehicle Search as “Search Results (Contraband).”
- **Vehicle details** — VIN status, make, model, and colors added to vehicles and shown on Person/Vehicle Search.
- **BOLO management** — Add and remove BOLOs via the MDT (vehicle must be in world).
- **Officer Profile** — Officer info, shift controls, and career stats moved to a separate “Officer Profile” button. Config focuses on plugins and settings.
- **Config page** — Plain-English labels and tooltips, grouped sections, presets with optional custom values, search, collapsible sections, Revert button, and save feedback.
- **Department Styling** — Choose an MDT theme (LSPD, SAFD, BCSO, LSSD, SAHP, FIB, etc.) with department colors, badge imagery, and backgrounds. Enable in Config → Plugins; select in Settings → Department Theme.
- **Court license revocations** — On conviction, the court can revoke driver’s license, firearms permit, and sport fishing based on the charges (California-style rules). Revocations are stored and applied to the person’s record.

### Other improvements

- **ALPR** — Tuning and behavior improvements; sound when a flagged vehicle is read; vehicle color on ALPR card.
- **Calendar and plugins** — Calendar and other bundled plugins only appear in the sidebar when enabled in Config → Plugins.
- **Firearms** — Weapon names from the game; scratched serials shown as “Serial: Scratched” and excluded from serial search.
- **Officer status bar** — Header shows officer name, badge, rank, call sign, department.
- **Court UI** — Convicted cases show “License Revocations Ordered.”
- **Person Search** — Revoked license and weapon permit status from court are saved and shown.
- **CDF/PR sync** — Driver’s license, weapon permit, and fishing/hunting status sync between MDT and Policing Redefined / Common Data Framework. Re-encountered people get the correct status from the MDT.

### Bug fixes

- **Wanted status** — Wanted status from dispatch now syncs to the MDT and clears correctly on arrest and court resolution.
- **Court plea** — Plea selection was not saved for pending cases. “Save Plea & Notes” added; Force Resolve uses the selected plea.
- **ALPR** — Proximity and front/rear plate reading improved; config defaults fixed so read range is correct.
- **Overlay clicks** — Window controls and Settings dropdown were sometimes not clickable; fixed.

## [0.9.2.0] — 2026-03-07

- **Fixed:** Registration and insurance status now sync correctly with Policing Redefined. Re-encounter no longer overwrites revoked/expired status with stale “Valid.”
- **Fixed:** SQLite DLLs must be in the GTA V root (same folder as GTA5.exe), not in plugins. Install instructions updated.

## [0.9.1.0] — 2026-03-07

- **Fixed:** SQLite dependencies are now included in the release so the game no longer crashes on going on duty if you didn’t have them from another mod.
- **Config:** WebSocket update interval default is now 1000 ms to reduce CPU use. Change it in Config or edit config.json if you want a different value.
- Citations no longer create or update court cases. Arrest reports still create court cases as before. Citation fines still vary at random within the configured range.

## [0.9.0.0] — 2025-03-06 (beta)

- **Initial public beta.** Police MDT with reports, Person Search, Vehicle Search, citations, arrests, court cases, and shift tracking. Web UI served locally; data stored in MDTPro/data (SQLite).
