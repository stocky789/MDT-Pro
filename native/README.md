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

## Current scope (vertical slice)

- CAD-style **dark theme** (`Themes/CadResources.xaml`): status strip, multi-panel layout, monospace message log.
- **HTTP**: `data/currentTime`, `config`, `data/officerInformation` (for unit line).
- **WebSocket**: three connections (same as the web client) — `interval/time`, `interval/playerLocation`, `calloutEvent`.

## Next work (not implemented yet)

- Broader `/data` and `/post` coverage, BOLO and reports screens.
- **Real-time collaboration** requires **plugin-side** event fan-out and likely **auth**; see `docs/` in this folder.
- **macOS**: separate shell (e.g. .NET MAUI or SwiftUI) reusing `MDTProNative.Client` patterns.

## Projects

| Project | Role |
|---------|------|
| `MDTProNative.Core` | Shared types (`MdtServerEndpoint`). |
| `MDTProNative.Client` | HTTP + WebSocket client for the plugin. |
| `MDTProNative.Wpf` | WPF UI. |
