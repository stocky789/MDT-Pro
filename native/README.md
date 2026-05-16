# MDT Pro Native

This folder contains the native Windows desktop client for MDT Pro. It talks to the same local MDT Pro
plugin server as the browser MDT, usually `http://127.0.0.1:9000`.

Use it when you want the MDT on a second monitor, in a normal Windows window, or outside the browser.

## Requirements

- Windows
- .NET 10 SDK with WPF workload
- MDT Pro running in-game
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) for the embedded Settings page

Reports, lookups, court, dashboard, quick actions, and most day-to-day views are native WPF. WebView2 is
only used for pages that still come from the browser MDT, such as Settings -> Config and Plugins.

## Build And Run

From this folder:

```powershell
dotnet build MDTProNative.sln -c Release
dotnet run --project src/MDTProNative.Wpf -c Release
```

Start GTA V, go on duty, then connect the app to the same host and port shown by MDT Pro.

## Current Coverage

| Area | Native client behavior |
| --- | --- |
| Reports | Native structured forms using the same `/post` save routes as the browser MDT. |
| Shift history and court | Native lists, docket view, case actions, evidence, and dispositions. |
| Person, vehicle, firearms, BOLO | Native views using the same `/data` and `/post` routes as the browser MDT. |
| Map | Native tactical position readout from the plugin map data. |
| Active Call | Native dashboard with WebSocket callouts and GPS actions. |
| Settings and plugins | Embedded browser page for `page/customization`. |
| Officer profile and shifts | Native profile, start shift, and end shift actions. |
| Quick actions | Panic, backup menu, tow/transport, and ALPR clear. |

For plugin-only windows or the full browser desktop, open **Settings** -> **About** -> **Open web home**.
That launches the same `page/index` experience you get in Chrome or Edge.

## Projects

| Project | Role |
| --- | --- |
| `MDTProNative.Core` | Shared types such as `MdtServerEndpoint`. |
| `MDTProNative.Client` | HTTP and WebSocket client for the plugin server. |
| `MDTProNative.Wpf` | WPF shell, views, and embedded web host. |

## Future Ideas

- Push updates for list views without polling.
- A macOS client using the same HTTP/WebSocket contract.
