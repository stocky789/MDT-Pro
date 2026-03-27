# Person Search, CDF, Policing Redefined, and ID photos

This note explains how MDT Pro resolves a person record and why the **ID photo** is (and is not) tied to **Policing Redefined (PR)** and the **Common Data Framework (CDF)**.

## External references

| Topic | Where to read more |
|--------|-------------------|
| Policing Redefined (user-facing) | [Policing Redefined docs](https://policing-redefined.netlify.app/docs/user-docs/intro/) |
| LSPDFR plugin / API examples | [LMSDev/LSPDFR-API](https://github.com/LMSDev/LSPDFR-API) |
| Common Data Framework (NuGet package used by PR and many plugins) | [CommonDataFramework on NuGet](https://www.nuget.org/packages/CommonDataFramework) |

CDF is a **shared in-game data layer** (peds, vehicles, documents). It does **not** publish a public HTTP API; plugins call into the CDF **.NET API** from inside the game process (e.g. `ped.GetPedData()` in RPH).

## What CDF gives MDT Pro

- **Identity and paperwork**: `PedData` on a live `Rage.Ped` typically includes name, DOB, gender, address, license and permit status, wanted/probation/parole flags, advisory text, etc.
- **No mugshot texture**: CDF does not expose a stable “DMV photo” URL or image bytes for the MDT browser. The game renders peds with drawable variations; there is no single canonical portrait per NPC in the data we consume.

So the MDT **cannot** show a true biometric photo of *that* ped. It shows a **reference image for the ped model** (e.g. `s_m_y_cop_01`), the same approach used by many external tools.

## How `ModelName` / `ModelHash` are set

MDT stores:

- `ModelHash` — `Rage.Ped.Model.Hash` (unsigned)
- `ModelName` — `Rage.Ped.Model.Name` (internal GTA model name, usually lowercase)

They are updated when:

- The ped is **identified** (PR/LSPDFR ID flow) — see `DataController.AddIdentificationEvent`.
- The ped is **resolved on encounter** — `DataController.ResolvePedForReEncounter` (must refresh the live model whenever we merge with an existing record; previously the model could stay stale when CDF data was present).

Before returning **Person Search** results, the server also calls `TryRefreshPedModelFromLiveWorld`: if the SQLite row has no live `Holder`, it tries the **recently identified** ped handle (same name, within a short TTL) so the photo matches who you just ran.

## `/data/specificPed` resolution order (browser → plugin)

1. If the POST body is a normal name, prefer the **context ped** when its CDF/LSPDFR name matches the query (or reversed “Last First”).
2. Else `GetPedDataByName` / reversed name on **active + kept** ped lists.
3. Else first case-insensitive match in the in-memory ped database.
4. Special keywords: `context`, `%context`, `current` map to the context ped.

So wrong photos are usually **wrong or stale `ModelName`**, not a wrong JSON field from CDF.

## How the browser loads the image

`pedSearch.js` tries, in order:

1. `https://docs.fivem.net/peds/{model}.webp`
2. `https://docs.fivem.net/peds/{model}.png`

**Limitations**

- **Addon / EUP** models often have **no** entry on that CDN → placeholder (“No photo available”).
- **Freemode** (`mp_m_freemode_01` / `mp_f_freemode_01`) shows a generic model shot, not hair/clothing/face blend for that NPC.

## PR-specific behaviour

PR drives traffic/ped stops and dispatch; it relies on CDF for documents and ped records. MDT listens to PR events (see `PREvents.cs`) to refresh warrants and related state. Anything that fixes **live** `Ped` ↔ **CDF `PedData`** alignment in-game also helps MDT; the MDT web UI only sees what the plugin serializes after that.

## Future improvements (if you need real mugshots)

Would require **in-game capture** (e.g. render ped headshot to PNG via natives, save under `img/`, serve via `/image/...`) and storage keyed by persistent identity. That is outside what CDF/PR document today.
