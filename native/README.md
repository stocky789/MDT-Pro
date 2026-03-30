# MDT Pro — Native desktop client (WPF)

This folder is a **separate .NET solution** that implements a **native Windows** shell for the same MDT Pro plugin endpoints used by the **browser MDT**. **Reports** are **native WPF** structured forms only (same `/post` save routes as the web UI). **WebView2** is used only where explicitly listed below (e.g. settings customization), not for reports.

## Requirements

- Windows with .NET **10** SDK and WPF workload.
- MDT Pro plugin running in-game (default port **9000**).
- **[WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)** for Settings → embedded customization only (not for reports or core searches).

## Build

```powershell
cd native
dotnet build MDTProNative.sln -c Release
```

Run the WPF app:

```powershell
dotnet run --project src/MDTProNative.Wpf -c Release
```

## Feature parity (vs browser MDT)

| Area | Native behavior |
|------|-----------------|
| Reports | Native structured forms per report type; same `/post` create/save APIs as the web MDT (no WebView). |
| Shift history | Native list + **Court** native docket (`Shift/Court` tab). |
| Court (cases, resolve, attach reports) | Native `NativeCourtView` (pending-case actions). |
| Person / vehicle / firearms / BOLO | Native WPF views (same `/data` + `/post` as web). |
| Map | Native tactical position readout (same data the plugin exposes to the web map). |
| Active Call | Native Dashboard (WebSocket callouts + GPS post). |
| Config & plugins | Settings → embedded `page/customization` |
| Officer profile, shift start/end | Native WPF (same `/post` routes as web) |
| Panic, backup menu, ALPR clear | Native quick actions |
| Callouts list + GPS from overview | Native WebSocket + `setGpsWaypoint` |

**Plugin-only or extra windows:** use **Settings → About → Open web home (full desktop) in browser** (`page/index`). That is the same desktop as opening the MDT in Chrome/Edge, including `/plugin/.../page/...` windows.

## Projects

| Project | Role |
|---------|------|
| `MDTProNative.Core` | Shared types (`MdtServerEndpoint`). |
| `MDTProNative.Client` | HTTP + WebSocket client for the plugin. |
| `MDTProNative.Wpf` | WPF UI + `MdtWebPageView` host. |

## Optional next work

- Real-time push for lists without polling — plugin WebSocket fan-out.
- **macOS**: .NET MAUI or SwiftUI using the same HTTP/WS contract.
