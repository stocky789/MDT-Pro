# CDF Sync — Implementation Review

Review of MDT-Pro's bidirectional sync with Common Data Framework (CDF) used by Policing Redefined. All facts below are derived from the codebase and implementation plan; no assumptions about undocumented CDF API.

---

## 1. Current Sync Flow

### 1.1 When Sync Runs

| Trigger | Method | Scope |
|---------|--------|-------|
| POST `/post/updatePedData` | `SyncPedDatabaseWithCDF()` | After `UpdatePedData`, before `SavePed` |
| POST `/post/updateVehicleData` | `SyncVehicleDatabaseWithCDF()` | After `UpdateVehicleData`, before `SaveVehicle` |

**Note:** Sync only runs for entities that are **in world** (in `pedDatabase` / `vehicleDatabase`). Peds/vehicles in `keepInPedDatabase` / `keepInVehicleDatabase` only (persisted, despawned) are never synced — CDF data for despawned entities may be invalid.

### 1.2 MDT → CDF (What We Write)

**SyncPedDatabaseWithCDF:**
- `CDFPedData.Wanted` ← `databasePed.IsWanted`
- `CDFPedData.IsOnProbation` ← `databasePed.IsOnProbation`
- `CDFPedData.IsOnParole` ← `databasePed.IsOnParole`
- `CDFPedData.Citations` ← `databasePed.Citations?.Count ?? 0`
- `CDFPedData.TimesStopped` ← `databasePed.TimesStopped`
- `CDFPedData.DriversLicenseState` ← parsed from `databasePed.LicenseStatus` (if valid `ELicenseState`)
- `CDFPedData.AdvisoryText` ← `databasePed.AdvisoryText` (try/catch — CDF may not expose setter)
- `CDFPedData.DriversLicenseExpiration` ← parsed from `databasePed.LicenseExpiration` (try/catch)

**SyncSingleVehicleToCDF:**
- `CDFVehicleData.IsStolen` ← `databaseVehicle.IsStolen`
- `CDFVehicleData.Registration.Status` ← parsed from `databaseVehicle.RegistrationStatus` (if `EDocumentStatus`)
- `CDFVehicleData.Registration.ExpirationDate` ← parsed from `databaseVehicle.RegistrationExpiration` (try/catch)
- `CDFVehicleData.Insurance.Status` ← parsed from `databaseVehicle.InsuranceStatus` (if `EDocumentStatus`)
- `CDFVehicleData.Insurance.ExpirationDate` ← parsed from `databaseVehicle.InsuranceExpiration` (try/catch)

### 1.3 CDF → MDT (What We Read)

**From CDF PedData** (PopulateParameters, re-encounters):
- FullName, Firstname, Lastname, Birthday, Gender, Address
- Wanted, IsOnProbation, IsOnParole
- DriversLicenseState, DriversLicenseExpiration
- WeaponPermit, FishingPermit, HuntingPermit
- AdvisoryText

**From CDF VehicleData** (PopulateParameters):
- IsStolen, Owner, Registration (Status, ExpirationDate), Insurance (Status, ExpirationDate)
- Vin (Number, Status), Make, Model, PrimaryColor, SecondaryColor
- BOLOs (via `GetAllBOLOs()`)

---

## 2. Edge Cases Addressed

| Issue | Fix |
|-------|-----|
| Null `pedData` / `vehicleData` from deserialization | Guard in PostAPIResponse; return 400 |
| Update fails (ped/vehicle not in world) | UpdatePedData/UpdateVehicleData return bool; return 404, skip Sync+Save |
| Case-sensitive name/plate match on update | Use `StringComparison.OrdinalIgnoreCase` in FindIndex |
| Null databasePed/databaseVehicle in foreach | Explicit `if (databasePed == null) continue` |
| Empty LicenseStatus passed to Enum.TryParse | Guard with `!string.IsNullOrEmpty`; use `ignoreCase: true` |
| Registration/Insurance.Status parse failure | Existing null checks; Enum.TryParse fails silently |

---

## 3. Optional Properties (Defensive Try/Catch)

The following are attempted; each is wrapped in try/catch so the sync continues if CDF does not expose a setter:

- **AdvisoryText** — MDT advisory text → CDF (PR can display)
- **DriversLicenseExpiration** — License expiry date → CDF
- **Registration.ExpirationDate** — Reg expiry → CDF
- **Insurance.ExpirationDate** — Insurance expiry → CDF

---

## 4. CDF Events (Bidirectional)

We subscribe to:
- `OnPedDataRemoved` — Prune `pedDatabase` when CDF removes ped data (entity despawned)
- `OnVehicleDataRemoved` — Prune `vehicleDatabase` when CDF removes vehicle data

We do **not** remove from `keepInPedDatabase` / `keepInVehicleDatabase` or SQLite; those persist.

---

## 5. BOLO Sync

BOLOs are CDF-owned. We:
- **Read:** `CDFVehicleData.GetAllBOLOs()` for display
- **Write:** `AddBOLO` / `RemoveBOLO` via our REST API when vehicle is in world
- **Persist:** BOLOs are stored in our SQLite `vehicles.BOLOs` when we save the vehicle

---

## 6. References

- Implementation plan: `docs/PR-CDF-IMPLEMENTATION-PLAN.md`
- CDF NuGet: CommonDataFramework 1.0.0.8
- Policing Redefined: References CDF for ped/vehicle data
