# MDT Pro — Native desktop client (WPF)

This folder is a **separate .NET solution** that implements a **native Windows** front end for the same MDT Pro plugin endpoints used by the **existing browser MDT**. The web UI and plugin layout under `MDTPro/` are **not replaced**; dispatchers can run this app against `http://<officer-pc>:<port>/` while officers keep the browser MDT.

## Requirements

- Windows with .NET **10** SDK (template default) and WPF workload.
- MDT Pro plugin running in-game (default port **9000**).

## Build

```powershell
cd native
dotnet build MDTProNative.sln -c Release
```

Run the WPF app:

```powershell
dotnet run --project src/MDTProNative.Wpf -c Release
```

## Current scope

- **Navigation**: Dashboard, Person search, Vehicle search, Firearms, BOLO (add/remove + auto-refresh), Reports (all major list endpoints), Shift/Court, **Map** (WebView2 loads the same `page/map.html` as the web MDT), Officer profile (load/save).
- **CAD-style** theme: status strip (time, location, unit, callout count), message log, monospace readouts.
- **WebSocket** (same protocol as browser): `interval/time`, `interval/playerLocation`, `calloutEvent`.
- **HTTP**: wraps the same `/data/*` and `/post/*` routes the web UI uses (POST bodies match browser MDT where required).

### Not in this client (use web MDT)

- Full **report composer** (all sections / drafts / autosave) — large; keep using the browser for creating/editing complex reports unless we add a dedicated native form later.
- **Plugins** (Calendar, ALPR popups, Department Styling) — web-only for now.
- **True multi-user live co-editing** — still requires additive plugin work; see `docs/COLLABORATION_ROADMAP.md`.

### Requirements

- **[WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)** for the Map screen.

## Next work (optional)

- **Real-time push** for lists (reports, BOLO) without polling timers — plugin WebSocket fan-out.
- **macOS**: .NET MAUI or SwiftUI using the same HTTP/WS contract.

## Projects

| Project | Role |
|---------|------|
| `MDTProNative.Core` | Shared types (`MdtServerEndpoint`). |
| `MDTProNative.Client` | HTTP + WebSocket client for the plugin. |
| `MDTProNative.Wpf` | WPF UI. |
