# Changelog

All notable changes to MDT Pro are documented here.

---

## [0.9.9.2] — 2026-04-21

### Improvements

- **ALPR** — Flagged plates (including “not in database”) only show on the reader **occasionally**, so traffic-heavy sessions aren’t one long alert list. Stolen / BOLO / wanted alerts are still spaced out so you’re not bombarded with sounds or popups.

- **Reports** — Saved report lists (**native desktop MDT** and **in-game / browser MDT**) show the **newest** report first (by time, then report number), instead of oldest at the top.

### Bug Fixes

- Fixed occasional frame drops and stutter while the MDT is open in your browser or native desktop app.

## [0.9.9.1] — 2026-04-15

### Major Features

- **ALPR has been reimplemented** — In-car plate reader with an on-screen terminal. The **browser MDT** and **desktop MDT** use the same reader for popups (toggle ALPR in **F7**).
- **ALPR** — Hold **Left Alt** to drag the panel or the **SIZE** corner to resize; **F7** still has position options.

### Bug Fixes

- **ALPR** — Owner license warnings now match **Person Search**.
- **Dashboard** — **Start / End shift** in the browser MDT is more reliable.
- **Reports** — Status filter buttons behave correctly, with an **All statuses** option.
- **Reports** — **Create impound report** from **Vehicle Search** opens the right impound draft with your vehicle filled in.
- **Reports** — Fix **Open / Closed / …** status you pick on a form - previously no highlight was shown on the status.

---

## [0.9.9.0] — 2026-04-14

### Major Features

- **Native Windows MDT** — Optional **Windows desktop app** that talks to the same MDT session as the in-game/browser MDT (dashboard, person and vehicle search, firearms, BOLO, shift/court, map, officer profile, **full report writing** in proper forms—handy on a second monitor). It ships **with the same release** as the plugin: the download zip (for example **MDTPro v0.9.9.0-beta**) includes the **Native Release** output **alongside** the rest of MDT Pro, so you still **copy the whole package** into your **MDT Pro** folder like always. The **OIV installer** also installs the full package, native app included—everything ends up under your **MDT Pro** folder together, not as a separate product you install somewhere else.
- **Reports overhaul** — Reports got a major refresh, especially **property/evidence** and related types: **document-style** layout, headers/branding, easier editing (tables and labeled fields instead of big text blocks), **recent IDs** helpers, and **print / export** where the native app supports it. The in-browser MDT gets the same document look and tools where it applies.

### Minor Features

- **Callouts (dashboard)** — Callout details use a clearer, dispatch-style layout (call, location, narrative, and so on). Callout behaviour overall is still a work in progress; see **Other** below.
- **Native person editor** — License state and permit fields use **dropdowns** where it makes sense so editing a person matches the rest of the MDT.
- **Local PED Images** — PED Model images are now loaded from a local database rather than online - providing better support for offline play and better optimization.

### Bug Fixes

- **Person Search — ID photos** — Portraits load from **bundled images** shipped with the resource (no outside image site). The MDT tries harder to pick the **right face/hair look** when that data exists, and portrait refresh is **more reliable** when the game is paused or you’re alt-tabbed. **Note:** Wrong or missing photos can still happen; see **Other** below.

### Other

- **Callouts — known issue** — Callouts are **still buggy**. **Accepting a callout from the MDT still does not work** in this version; I am actively working on a solution to this but it is holding up development at this stage so unfortunately we need to release, yet again no support for it. 

- **PED portraits — known issue** — ID photos can **still be missing or wrong**. Matching portraits to the right ped isn’t fully reliable yet. Still narrowing down if this is a bug with the persistence / SQL system or something else.

---

## [0.9.8.3] — 2026-04-02

### Bug Fixes

- **Person Search — ID photo (requires further testing)** — Wrong portraits have been reported for a long time and past fixes haven’t nailed it for everyone. This build **tries another approach**: when the MDT has **date of birth**, it uses that to tell same-name records apart, and when you search it **may** prefer pedestrians **near you** or at a **recent traffic stop** so the picture isn’t only driven by a stale or shared name.

Note: PED variations still aren't possible at this stage. I haven't thought of any good ways of circumventing this other than compiling and building a separate database for variations. This is a lot of work that I do not have time for currently.

---

## [0.9.8.2] — 2026-03-30

### Bug Fixes

- **Vehicle Search — nearby plates** — The nearby-plates list updates when you open or refresh Vehicle Search, picks up plates more reliably, and recent searches no longer lists the same plate as separate entries over and over.

- **Firearms Check (StopThePed)** — After a weapon or serial check in StopThePed, the MDT saves the correct serial instead of the wrong one or a blank. (Needs more testing)

### Improvements

- **Person Search** — Person records stay consistent when you identify or meet someone again; court outcomes (for example a revoked license or permit) still show correctly when they apply.

- **Same person, another stop** — If you interact with the same pedestrian again (for example another StopThePed stop), permits and court-related status stay up to date instead of being skipped.

- **Vehicle Search — seen before** — When you run into a vehicle you’ve looked up before, registration-style details refresh so the MDT matches what you see in-game.

---

## [0.9.8.1] — 2026-03-29

### Bug Fixes

- **Citation handoff key STP only (F10)** — After the in-game message told you to press **F10**, the key sometimes did nothing when you used **StopThePed**. That’s fixed. The same underlying issue could affect the **Settings menu** hotkey and **ALPR**; those should behave reliably again right after you go on duty.

- **Reloading the plugin** — If you reload MDT Pro (or related plugins) without restarting the game, crashes were more likely after using **StopThePed** or other integrations. MDT Pro now cleans up its hooks properly on unload so reloads are safer.

- **Address & location on the MDT** — The **taskbar location** line and **new report** address fields could stay empty or show junk (like the word “null” or only a comma), especially with **StopThePed**. They should now fill in with a real street and area, match your traffic stop better when you’re using STP, and show **Los Santos**-style county names correctly (with a space). If something still looks wrong, you can turn on **location debugging** in **config.json** (`locationDebugLogging`) and check **MDTPro.log** for details.

### Minor Features

- **Safer citation handoff** — When handing off a citation in-game, the person in front of you must match the offender name on the citation / MDT record when the game can read their full name. If it doesn’t match, handoff is blocked and you’ll see a clear message (new default text: identity mismatch).

### Other

- **Callouts** — Starting a new shift no longer stacks duplicate callout listeners over time; that keeps callout behaviour stable for long play sessions.

---

## [0.9.8.0] — 2026-03-29

### Major Features

- **StopThePed + Ultimate Backup** — Works with **StopThePed** for stops / traffic events and **Ultimate Backup** for Quick Actions backup when **Mod integration → Backup** is **Ultimate Backup** or **Auto** (with PR absent, Auto uses UB). **CDF still required.** Don’t run **Policing Redefined** together with StopThePed + UB; it’s unsupported. README / release **README.txt** / **Config** list PR vs STP+UB requirements.

- **Citation handoff menu** — You can hand a citation to a PED using the Citation Menu (F10 by default) - this menu is only when using STP. It is disabled in PR mode.

### Minor Features

- **StopThePed** — Optional MDT URL line on citation notifications; optional post-handoff clipboard animation (**Config** → Citations — StopThePed & handoff).

- **Suspect lines** — Subtitle after handoff; lines come from `citationPedReactions.json` (version in file; defaults sync when bundled version is newer). Toggle / profanity in **Config** → Citations — suspect lines.

- **Citation handed notification** — In-game message: name + total fine, before suspect line / hostility roll.

- **Post-citation hostility** — Optional rare combat after citation; config under **Citations — rare hostile suspect**.

- **StopThePed stop integration** — **Mod integration** → traffic stops / events: STP or Auto aligns ped/vehicle data with stops where possible.

### Bug Fixes

- **HttpListener** — `Server.Stop()` is synchronous; stop also runs **off duty**. Avoids port still bound (`Listening on Server failed`) and listener threads stacking across duty toggles / reloads.

- **citationPedReactions sync** — Version read from file head (regex); full JSON not parsed on game thread; upgrade copy can run on thread pool.

---

## [0.9.7.2] — 2026-03-28

### Minor improvements

- **Probation and parole** — People on probation or parole now get a **sensible prior record**: charges that fit, a **closed court case** that matches, and an **arrest history** that isn’t empty. The MDT labels these as **reconstructed** where there isn’t a real patrol arrest report behind them, so you know what you’re looking at.

- **Person Search** — When someone has court cases on file, you’ll see a **Court & disposition** section with the basics and a button to **open the right case in Court**. If they’re on supervision and that ties to the reconstructed prior case, a short note appears under **History** explaining how it fits together.

- **Court** — Those reconstructed supervision cases show a **Prior** badge and a clear banner at the top of the case details so they’re easy to pick out on the docket.

- **Reconstructed case wording** — Verdict and sentencing blurbs on those prior cases use the **same text generators** as real resolved cases (plea, charges, evidence bands, and the rest), so they read with the same variety as patrol-driven docket entries—not a small set of repeated templates.

- **Property and Evidence** — Vials are now a quantity option when doing a seizure report.

---

## [0.9.7.1] — 2026-03-27

### Minor Features

- **Cleaner logs** — Startup writes a short config summary to **MDTPro.log** instead of the full settings dump and folder listing (turn on **verboseFileLogging** in config if you need the old detail). Optional **log file size cap** with automatic trim so the file doesn’t grow forever. **WebSocket** and **ALPR** lines are still written to **MDTPro.log** as before; they are **no longer duplicated** into the **RPH in-game console** (so routine connects/disconnects and ALPR start/stop don’t spam the plugin console).

- **Easier to share logs** — If an error gets written to **MDTPro.log**, those lines no longer include long **C:\…** folder paths, so snippets are shorter and more comfortable to post on the forum.

- **Streaming / privacy** — **Customization → Config → General**: **Show MDT address in-game** (on by default). Turn it off to skip the in-game pop-up that shows your local IP and PC name when the MDT server starts. URLs are still written to **MDTPro/ipAddresses.txt** and logged; the setting applies the next time you go on duty (when the server starts).

- **Drug charges & seizures** — Filter narcotics by **schedule** (I–V and other wording) when picking **arrest** charges. On **Property and Evidence** reports, choose a **schedule**, then the drug type. New seizure options for **Ritalin** and **Hydrocodone**, plus new arrest charges for both. Overlapping arrest charges removed (**petty theft** kept instead of duplicate shoplifting; one **controlled substance** line instead of a second prescription copy). Existing installs get the updated arrest and citation lists automatically the next time you load the mod.

### Bug Fixes

- **Probation / parole** — People on probation or parole now usually show at least one **prior arrest** on their record so it doesn’t look empty by mistake.
- **Person Search** — Saved and recent people resolve more reliably; **photos** mix up less often, including when you search a new name before the last search has finished.
- **ALPR** — Registration and insurance flags use **live game data** for that vehicle (same idea as Callout Interface), not empty fields or old saved-only records when there’s no live owner. Plate scan timing is unchanged from before.
- **Court** — Drug evidence from **Property and Evidence** still matches **possession / controlled substance** charges after the charge list change. Verdict wording that looks for **theft** also matches older charges that still say **shoplifting**.

---

## [0.9.7.0] — 2026-03-23

### Major Features

- **Court system overhaul** — Jury trials use the actual jury vote; judges choose sentences within a range based on leniency profile; weak evidence caps conviction chance; plea deals give a 10–25% sentence discount. Court dates, hearing dates, and legal team (judge, prosecutor, defense attorney) shown on each case. Docket-style layout. Judge personality profiles affect conviction and sentencing—8 Los Santos, 4 Blaine County, 4 Territorial judges including lore judges (Hugh Harrison, Barry Griffin, Judge Grady). Lore-friendly law firms: Los Santos/Blaine County DA's Office, Territorial Prosecutor, Slaughter Slaughter & Slaughter, Hammerstein & Faust, Delio and Furax, Goldberg Ligner & Shyster, Rakin and Ponzer, Public Defender. Evidence strength (Low/Medium/Strong) at a glance; numbered exhibits; auto-generated officer testimony summary.
- **Smarter verdict wording** — **300+ variations** across verdict types. Dismissals cite "Insufficient evidence" or "Prosecution could not meet burden of proof." Acquittals mention when the defense challenged key evidence. Charge-specific wording: DUI ("lack of reliable BAC documentation"), drugs ("chain of custody and search legality"), assault ("self-defence and lack of intent"), evading ("driver identification and pursuit justification"), murder/arson/theft/firearm/traffic/resisting. Sentence reasoning covers recidivism, prior convictions, jury verdicts, charge type, and aggravating/mitigating factors.
- **Hundreds of new charges and expanded citations** — Arson, Kidnapping, Racketeering (RICO), Federal crimes (espionage, wire fraud, cyberterrorism), Immigration/ICE, Wildlife (poaching, cruelty, dog fighting), expanded drug charges (Schedule I–IV, steroids, Xanax, Fentanyl, cultivation), fraud (embezzlement, forgery, money laundering, Ponzi schemes). Citations: Federal, Legal Compliance, Motorcycle/ATV, Pets & Wildlife, Accident, Alcohol, Documents, Equipment, Yielding, Parking, Speeding, Written Warnings. Every charge fully integrated; verdicts and sentencing reference correct offense types and license revocations. Existing installs receive updates on upgrade (version 3 migration).

### Minor Features

- **Evidence updates when you add reports** — Attach or detach a report from a case (or arrest with a court case) and the evidence strength recalculates immediately.

### Bug Fixes

- **Citation/arrest options now update on upgrade** — Bumped `citationArrestOptionsVersion` to 3 so existing installations receive the new charge and citation defaults. Previously the version stayed at 2, so users who already had the 0.9.6.0 files never got the 0.9.7.0 additions.
- **Correct sentence totals** — Resolved cases now show the actual total jail time imposed, not the statutory maximum.
- **Court display** — Fixed display glitches when attached reports couldn't be loaded.
- **Charge data robustness** — Court display and case saving now handle missing or invalid charge entries without errors.
- **Policing Redefined — citation handoff** — Closing a citation in the MDT no longer calls `SetPedAsStopped` on occupants still inside a vehicle, which was putting PR into the wrong interaction state and hiding **Dismiss** (and other options) on the Traffic Stop / Ped Stop menus.

---

## [0.9.6.0] — 2026-03-22

### Major Features

- **Property and Evidence Receipt (Seizure Reports)** — New report type to document seized drugs and firearms. Supports multiple subjects via Recent IDs or manual add. Add drugs and firearms with quantity types (Baggie, Bundle, Grams, Ounce, Pill, etc.). Attach to arrests for court evidence. Charge-specific evidence: court now matches what you seized (e.g. Possession of Heroin only counts if Heroin is in the seizure report). Court shows specific types when documented ("Drugs found: Heroin, Cocaine" or "Firearms seized: Pistol"). The drugs/firearms tickboxes on arrest reports are removed — use Property and Evidence Receipt reports instead. Create from the arrest's Attached reports section; subject pre-fills and the report auto-attaches.
- **Court rebalanced** — Sentence multiplier was hitting the max too often. Now tuned so harsher sentences are reserved for career criminals, serious felonies, and strong prosecution. Routine arrests stay closer to baseline. Many new wording variants for verdicts and sentencing. Outcome reasoning only mentions evidence you actually documented. Murder and manslaughter are now prominently mentioned. Broader charge coverage for sexual offences, kidnapping, property damage, escape, probation breaches, traffic, DUI, and more. Adjust in Config → Court if needed.
- **Firearms Check improvements** — Recent weapons from pat-downs and body searches. Serial and owner appear when the game provides them. Faster capture, melee filtered, lookups improved. Weapon lines from vehicle searches are clickable — click to jump into Firearms Check.

### Minor Features

- **Person Search** — Injury reports and impound reports (person at fault or owner) in Associated Reports; ID photo in Basic Information (placeholder if unavailable).
- **Arrest Reports** — **Import recent reports** button attaches reports from the last 60 minutes that involve the arrested person (incident, injury, citation, traffic, impound, property/evidence). Works before first save (draft mode). **Save and close (submit for court)** — label clarifies it saves first; success notification only shows when save actually succeeds.
- **Impound Reports** — **Person at fault** field with Recent IDs to tie impounds to a person for import filtering and person search.
- **Vehicle Search** — Search Results (Contraband) shows drugs, weapons, and contraband when the game provides them (after running the plate and searching the vehicle).
- **Charges** — New drug charges: Amphetamine, Benzodiazepine, Mescaline, Psilocybin, generic Possession Of Controlled Substance. New property/ID charges: Possession Of Burglary Tools, Possession Of Credit Card Scanning Device, Possession Of Counterfeit Items, Possession Of Stolen Debit/Credit Card, Refusing To Provide Identification, Failure To Present Drivers License Upon Demand.

### Bug Fixes

- **Evidence capture** — No longer fails when suspects despawn or are transported mid-capture.
- **Person Search** — Fixed errors when searching for someone who had already left the area.
- **Import recent reports** — Now works when useInGameTime is enabled (uses real creation time for the 60-minute window).

### Misc

- **Firearms Check — what works and what doesn't:** Weapons from pat-downs and body searches show up. So do guns found when you search a vehicle (after running the plate through dispatch) and guns you're holding. Weapons you put in your trunk and then run a serial check on from the trunk menu often won't appear — yet to figure out a method to make this work.
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
