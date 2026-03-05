# ALPR Integration Plan for MDT Pro

This document outlines a complete, working ALPR (Automatic License Plate Recognition) integration for MDT Pro: in-game scanning, lookup, in-game HUD, MDT web popup, and settings in both game and MDT. It follows Rage Plugin Hook conventions and draws on concepts from ReportsPlus (Java desktop mod) for data model and UX—adapted to MDT Pro’s C#/LSPDFR + web stack.

---

## 1. Overview

- **In-game**: MDT Pro (LSPDFR plugin) scans nearby vehicles’ license plates on a timer, looks them up against CDF/MDT data, detects flags (no insurance, no/expired registration, stolen, owner wanted). When flags exist, it shows an in-game HUD panel and can push a hit to the MDT browser.
- **In-game HUD**: One panel (position configurable) showing current/last ALPR hit: plate, owner, model, flags. Drawn with Rage `Graphics` APIs.
- **MDT (browser)**: Optional popup when a flagged hit occurs (configurable), with “Open vehicle lookup” for full details.
- **Settings**: In-game options (enable/disable, HUD position, optional sound) and MDT config (enable popup, which flags to alert on, etc.).

---

## 2. Rage Plugin Hook – In-Game Display

### 2.1 API to Use

- **Namespace**: `Rage` (already used by MDT Pro).
- **Drawing**: Use `Rage.Graphics` in a **game fiber** (e.g. `GameFiber.StartNew` or a per-frame tick) so all drawing runs on the main game thread.
- **Text**: `Graphics.DrawText(string text, string fontName, float fontSize, PointF position, Color color)`.
  - `PointF` is in **screen coordinates** (documentation suggests normalized 0–1 in some contexts; verify in RPH version: if 0–1, scale by screen width/height for layout).
- **Rectangles**: `Graphics.DrawRectangle(RectangleF rect, Color color)` for panel background/borders.
- **Resolution**: Use `Game.Resolution.Width` and `Game.Resolution.Height` (or equivalent) to scale layout so the ALPR panel scales across resolutions.

### 2.2 Positioning (Standards-Compliant)

- Store position in **normalized coordinates** (e.g. 0.0–1.0) or in **pixel offset from a corner** (e.g. “top-left”, “top-right”) so behavior is consistent across resolutions.
- **Config fields** (in-game or MDT):
  - `alprPositionAnchor`: e.g. `"TopLeft"`, `"TopRight"`, `"BottomLeft"`, `"BottomRight"`.
  - `alprPositionOffsetX`, `alprPositionOffsetY`: offset in pixels from that anchor (or normalized 0–1).
- In the render loop: resolve anchor (e.g. top-left = 0,0), add offset, then draw panel and text so the panel does not go off-screen. Clamp to safe margins.

### 2.3 Render Loop

- Run a single **GameFiber** that:
  - Only runs when `alprEnabled` and on duty.
  - Every frame (or every N ms to limit CPU): if there is a “current ALPR hit” to show, call `Graphics.DrawRectangle` for the panel and `Graphics.DrawText` for plate, owner, model, and each flag line.
- Keep drawing calls minimal (one background rect + a few text lines) to avoid impacting FPS.

---

## 3. Data Model (Inspired by ReportsPlus, Adapted to MDT)

### 3.1 ALPR Hit (C#)

- **Plate** (string)
- **Owner** (string) – from CDF/MDT
- **Model display name** (string)
- **Flags** (list of strings), e.g.:
  - `"No insurance"` / `"Insurance expired"`
  - `"No registration"` / `"Registration expired"`
  - `"Stolen"`
  - `"Owner wanted"`
- **Time scanned** (DateTime)
- Optional (later): speed, distance, “scanner” name – can be added without changing core flow.

ReportsPlus uses a similar model (licenseplate, plateType, speed, distance, scanner, flags, timescanned); we map CDF/MDT registration/insurance/wanted/stolen into our flags.

### 3.2 Flag Derivation from CDF/MDT

- From `MDTProVehicleData` / CDF `VehicleData`:
  - Registration: `RegistrationStatus` / expiration → “No registration” / “Registration expired”.
  - Insurance: `InsuranceStatus` / expiration → “No insurance” / “Insurance expired”.
  - `IsStolen` → “Stolen”.
  - Owner’s wanted status (from CDF ped) → “Owner wanted”.
- Build `List<string> flags` and only treat as a “hit” when `flags.Count > 0` (or when config says to show clean plates too).

### 3.3 Deduplication and Alerts (ReportsPlus-Style)

- **Per-plate cooldown**: Don’t re-alert the same plate for a configurable time (e.g. 60–120 seconds) so the same car driving by again doesn’t spam.
- **Alerted plates**: Track plates that have already triggered an in-game notification (and/or MDT popup) this session; optionally reset when going off duty or via “Clear ALPR” in MDT.

---

## 4. Scanning and Lookup (In-Game)

- **Scan interval**: Configurable (e.g. every 1–3 seconds) in a GameFiber.
- **Source of plates**: Use `Main.Player.GetNearbyVehicles(n)` (or Rage equivalent); iterate and read `Vehicle.LicensePlate` (and ensure vehicle exists and plate is non-empty).
- **Lookup**: For each plate, get or create `MDTProVehicleData` (same as vehicle search): use `DataController.VehicleDatabase` / CDF so that registration, insurance, stolen, owner are available. If not in DB, optionally trigger CDF to populate (e.g. create `VehicleData` from vehicle) then build flags.
- **Flags**: Build the list from that vehicle data; if any flag is set, create an `ALPRHit` and push to:
  - In-game HUD (current hit),
  - Optional in-game notification (e.g. `RageNotification.ShowWarning(...)`),
  - Optional WebSocket broadcast to MDT for popup.

---

## 5. In-Game Settings (Rage / LSPDFR)

These affect behavior that only the game process can control: HUD visibility, position, and optional sound.

Suggested **in-game** options (e.g. via a simple menu or keybound config; exact UI can be minimal at first):

- **Enable ALPR** (bool): Master switch for scanning and HUD.
- **ALPR HUD position**:
  - Anchor: TopLeft / TopRight / BottomLeft / BottomRight.
  - Offset X, Y (pixels or normalized).
- **ALPR scan interval** (seconds): How often to scan nearby vehicles.
- **ALPR alert sound** (bool): Play a short sound on flagged hit (e.g. reuse or add a small wav in MDTPro folder).
- **Show in-game notification on hit** (bool): Besides HUD, show a Rage notification (e.g. “ALPR: [plate] – flags”).

Storage: either a small JSON in the MDT Pro folder (e.g. `alprGameSettings.json`) or extend `config.json` with an `alpr` section that the game reads; avoid the game writing the main MDT config too often (the browser may overwrite). Prefer a separate file for “game-only” ALPR settings so the MDT config stays the single source for browser-related options.

---

## 6. MDT Settings (Config + Customization Page)

These control MDT (browser) behavior and can be edited in the existing Settings/Config flow.

Add to **Config** (e.g. `Setup/Config.cs` and `/config`, `POST /post/updateConfig`):

- **alprEnabled** (bool): Mirror or master for “ALPR on” (game can also have its own override).
- **alprShowPopupInMDT** (bool): When a flagged hit occurs, push to browser and show popup.
- **alprPopupDuration** (int, seconds): Auto-close popup after N seconds (0 = don’t auto-close).
- **alprFlagsToAlert** (list or comma-separated string): Which flags trigger the MDT popup (e.g. “Stolen,Owner wanted,No insurance”). Empty = all flags.

Optional:

- **alprInGamePositionAnchor** / **alprInGamePositionOffsetX/Y**: If you prefer to edit HUD position from MDT (e.g. on the customization page), the game can read these on next tick or when config is reloaded.

Expose these in the **MDT customization/config UI** (same place as other options) so the player can turn the popup on/off, choose duration, and which flags to alert on.

---

## 7. In-Game HUD Implementation Sketch

- **ALPRPanel** (or similar) class:
  - Holds “current hit” to display (or last N hits; start with one).
  - Method `void Draw(Graphics g)` that:
    - Reads position from config (anchor + offset), resolves to screen X,Y.
    - Draws a semi-transparent rectangle (e.g. `Graphics.DrawRectangle`) for the panel.
    - Draws lines of text: plate, owner, model, then each flag (e.g. different color for “Stolen” vs “Expired” – ReportsPlus uses red for stolen/no, orange for expired).
- **GameFiber** (started when going on duty if ALPR enabled):
  - Loop: sleep scan interval → get nearby vehicles → for each plate do lookup → if flags, update current hit and (if options set) notification + WebSocket.
  - In the same fiber or a second one: every frame, if current hit exists and HUD enabled, call `ALPRPanel.Draw(Graphics)`.

Use `Game.FiberIsCurrent` when calling Rage APIs; ensure the render loop runs on the game fiber.

---

## 8. MDT Web Popup

- **WebSocket**: When a flagged ALPR hit occurs and `alprShowPopupInMDT` is true, the server sends a WebSocket message to all connected clients, e.g. `{ "event": "alprHit", "data": { "plate", "owner", "model", "flags", "timeScanned" } }`.
- **Front-end**: In the main MDT script (or a small ALPR module), listen for `alprHit`. When received, show a small modal/overlay with plate, owner, model, flags, and a button “Open vehicle lookup” that opens vehicle search with that plate (same as ReportsPlus “Search D.M.V.” pre-filling the plate).
- **Auto-close**: If `alprPopupDuration` > 0, set a timer to close the popup.
- **Clear**: Optionally add “Clear ALPR” in MDT that sends a POST (e.g. `POST /post/alprClear`) so the server clears “last hit” and optionally the alerted-plates list; in-game HUD can hide until the next hit.

---

## 9. API Additions (Server)

- **POST /post/alprHit** (optional): If you ever want an external LSPDFR plugin to push ALPR hits to MDT, accept JSON `{ plate, owner, model, flags[], timeScanned }`, validate, then broadcast via WebSocket and update in-memory “current hit” for the HUD. For the built-in implementation, the game code can call an internal method instead of HTTP.
- **WebSocket**: New message type `alprHit` with the same payload.
- **POST /post/alprClear**: Clears current ALPR hit and optionally alerted-plates; respond 200 OK.

---

## 10. ReportsPlus Ideas Used (Reference Only)

- **Vehicle model**: Plate, flags list, time scanned; optional speed/distance/scanner (we can add later).
- **Flag colors**: Red for stolen/no, orange for expired (in HUD and optionally in MDT popup).
- **Deduplication**: Alerted-plates set and per-plate cooldown to avoid spam.
- **“Search DMV”**: In MDT, “Open vehicle lookup” with plate pre-filled.
- **Clear**: Clear list and reset state; we add “Clear ALPR” in MDT and optionally in-game.
- **Position config**: ReportsPlus stores ALPR window position (x, y); we store HUD anchor + offset for resolution-independent layout.
- **ALPR-specific settings**: Separate section (alprSettings / alpr in config) for enable, popup, duration, which flags to alert, and optionally image/presentation options later.

---

## 11. File / Code Structure (Suggested)

- **Config**: `Setup/Config.cs` – add ALPR fields. Optional: `alprGameSettings.json` for in-game-only options (position, sound, scan interval) if you don’t want to mix with main config.
- **Data**: `Data/ALPRHit.cs` – small class (Plate, Owner, Model, Flags, TimeScanned).
- **Logic**: `ALPR/ALPRScanner.cs` – scan loop, lookup, flag building, update current hit, trigger notification and WebSocket.
- **HUD**: `ALPR/ALPRHUD.cs` – current hit, position resolution, `Draw(Graphics)` using Rage APIs.
- **Server**: `ServerAPI/WebSocketHandler.cs` – handle `alprHit` broadcast; `PostAPIResponse.cs` – `alprHit` (if needed), `alprClear`.
- **MDT**: New strings in `Setup/Language.cs` for ALPR; customization page and main JS: ALPR options + popup listener + “Open vehicle lookup” + “Clear ALPR”.
- **Main.cs**: On duty, if ALPR enabled, start ALPR scanner and HUD fibers; on cleanup, stop them.

---

## 12. Testing Checklist

- Enable/disable ALPR from in-game and from MDT; both reflect correctly.
- HUD position: all four anchors; offset; different resolutions; no off-screen drawing.
- Scan: nearby vehicle with no insurance/expired reg/stolen/wanted owner produces flags and hit.
- In-game: HUD shows hit; notification (if on) and sound (if on) fire once per plate per cooldown.
- MDT: Popup appears when `alprShowPopupInMDT` is true; “Open vehicle lookup” opens search with plate; Clear ALPR clears state.
- No duplicate alerts for same plate within cooldown; no crashes when CDF data is missing or vehicle is invalid.

---

This plan gives a complete path to a working ALPR: Rage-based in-game HUD with standard positioning, settings split between in-game and MDT, and ReportsPlus-inspired data model and UX, adapted to MDT Pro’s architecture.
