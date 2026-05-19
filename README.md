# MDT Pro

[Read the MDT Pro user guide](https://stocky789.github.io/MDT-Pro/)

MDT Pro is a police computer for LSPDFR. When you go on duty, it starts a local web MDT that you can
open in a browser, Steam overlay, another device on your network, or the optional native Windows client.

![MDT Pro overview](Screenshots/Overview.png)

## Start Here

1. Install the external LSPDFR mods listed below.
2. Pick one local integration stack: **Policing Redefined** or **StopThePed + Ultimate Backup**.
3. Install MDT Pro with the OpenIV package or by copying the release files into your GTA V folder.
4. Go on duty in LSPDFR.
5. Open the MDT address shown in-game, usually `http://127.0.0.1:9000`.

## Requirements

Install these separately:

- **LSPDFR** and RagePluginHook.
- **CommonDataFramework (CDF)**. This is always required, even if you use StopThePed and Ultimate Backup
  instead of Policing Redefined.

The MDT Pro release installs the plugin and its support files at the same time:

- `plugins/LSPDFR/MDTPro.dll`
- `plugins/LSPDFR/CalloutInterfaceAPI.dll`
- `plugins/LSPDFR/Newtonsoft.Json.dll`
- `plugins/LSPDFR/LemonUI.RagePluginHook.dll`
- `System.Data.SQLite.dll` in the GTA V root folder
- `x64/SQLite.Interop.dll`
- `MDTPro/`
- `MDTProNative/` if the release includes the native Windows client

LemonUI is required for the in-game MDT Pro menus. The release includes it, but the direct download is
[LemonUI v2.2](https://github.com/LemonUIbyLemon/LemonUI/releases/download/v2.2/LemonUI.zip) if you ever
need to replace the DLL yourself.

Optional integration:

- **Callout Interface** adds live Active Call data. MDT Pro still opens without it, but the Active Call
  page will not receive live callout details.

### Pick One Local Mod Stack

MDT Pro's local browser MDT supports two common setups. Choose one and keep it clean.

| Stack | Install | What MDT Pro uses it for |
| --- | --- | --- |
| **Policing Redefined** | Policing Redefined | Stops, arrests, citations, and backup when PR is selected or Auto detects PR. |
| **StopThePed + Ultimate Backup** | StopThePed and Ultimate Backup | Stops and citation handoff use StopThePed. Quick Actions backup uses Ultimate Backup when selected or when Auto finds UB without PR. |

Do not run Policing Redefined together with StopThePed + Ultimate Backup. That mix can cause duplicate
or conflicting behavior.

In the MDT, open **Settings** -> **Config and Plugins** and set:

- **Traffic stops & events** to Auto, Policing Redefined, or StopThePed.
- **Backup (Quick Actions)** to Auto, Policing Redefined, or Ultimate Backup.

### MDT Cloud Requirements

MDT Cloud has a stricter support rule than the local MDT.

To use MDT Cloud login, register an account at [mdt.stockhosting.com.au](https://mdt.stockhosting.com.au)
and run **Policing Redefined**. MDT Cloud currently supports **Policing Redefined only**. Do not use the
Cloud login with StopThePed + Ultimate Backup.

## Install

Use the OpenIV installer from a release when you can. It puts the plugin, web UI, native app, SQLite
files, LemonUI, and support DLLs in the expected folders.

For a manual install, extract the release ZIP into your GTA V folder, the one with `GTA5.exe`.

After install, your game folder should include:

- `plugins/LSPDFR/MDTPro.dll`
- `plugins/LSPDFR/CalloutInterfaceAPI.dll`
- `plugins/LSPDFR/Newtonsoft.Json.dll`
- `plugins/LSPDFR/LemonUI.RagePluginHook.dll`
- `System.Data.SQLite.dll` in the GTA V root
- `x64/SQLite.Interop.dll`
- `MDTPro/`
- `MDTProNative/` if you install the native Windows client

SQLite has one strict rule: `System.Data.SQLite.dll` belongs in the GTA V root folder, not only under
`plugins/LSPDFR`. If MDT Pro says it cannot load SQLite, check that file and `x64/SQLite.Interop.dll`
first.

## Update Or Reset

To update, overwrite the plugin files, the `MDTPro/` folder, and the native app folder with the new
release. Keep `MDTPro/data/` and `MDTPro/config.json` if you want to keep your records and settings.

To reset local MDT data, close the game and clear `MDTPro/data/`. Deleting `MDTPro/config.json` resets
settings to defaults the next time the plugin loads.

## Use MDT Pro

Go on duty with LSPDFR. MDT Pro starts its local server and shows one or more addresses in-game. If you
miss the notification, check `MDTPro/ipAddresses.txt`.

The default port is `9000`. You can change it from **Settings** -> **Config and Plugins**.

Chromium browsers such as Chrome, Edge, and Brave tend to work best. You can also point the Steam overlay
browser at `http://127.0.0.1:9000`.

### Use Another Device

Phones, tablets, and other PCs can connect to the MDT if they are on the same network and Windows allows the connection.

If another device cannot reach the MDT, add an inbound Windows Firewall rule on the game PC for the MDT
port, usually `9000`. This is usually a Windows Firewall issue, not a router issue.

## What You Can Do

- **Control Panel**: edit officer info, start or end shifts, view career totals, and open settings.
- **Reports**: create incident, injury, traffic, impound, citation, arrest, and property/evidence reports.
  Drafts save in the browser for up to 24 hours.
- **Person, vehicle, and firearm lookups**: search records collected through MDT Pro and supported integrations.
- **Citations**: with Policing Redefined, citations can hand off through PR. With StopThePed, use the
  MDT Pro in-game menu and **Hand pending citation** near the suspect.
- **BOLO and backup**: add BOLOs, request backup, panic, request tow or transport, and clear ALPR hits
  from Quick Actions.
- **ALPR**: enable the in-game plate reader and the browser popups from Config and Plugins.
- **Shift History and Court**: review completed shifts, linked reports, and court cases from arrest reports.
- **Map and GPS**: view your current location and route to selected points while the game is running.
- **Active Call**: view callout status, location, priority, timeline, and GPS actions when Callout
  Interface is installed.
- **MDT Cloud**: register at [mdt.stockhosting.com.au](https://mdt.stockhosting.com.au), then sign in
  from the in-game menu. Cloud currently supports Policing Redefined only. See
  [cloud/README.md](cloud/README.md) for the backend and portal.

## Customization

Open **Settings** -> **Config and Plugins** from the Control Panel or browse to `/page/customization`.

From there you can change the port, clock mode, game time behavior, map options, window sizing, court
settings, evidence weights, ALPR behavior, and plugin state. Settings are stored in `MDTPro/config.json`.

### Custom Wallpaper

To change the MDT desktop background:

1. Open **Settings** -> **Config and Plugins**.
2. Enable the **Custom Wallpaper** plugin.
3. In the **Desktop wallpaper** section, choose a PNG or JPG image.
4. Click **Apply**.
5. Refresh or reopen the main MDT window if it was already open.

Use **Use default** in the wallpaper section to remove your custom wallpaper.

## Plugins

Plugins live in `MDTPro/plugins/`. Enable or disable them from **Config and Plugins**.

A plugin folder usually looks like this:

```text
PluginName/
  info.json
  pages/
  scripts/
  styles/
  images/
```

The folder name becomes the plugin URL id. For example, `MDTPro/plugins/ALPR/` is served under `/plugin/ALPR/...`.

Useful paths:

- Pages: `/plugin/<pluginId>/page/<fileName>`
- Scripts: `/plugin/<pluginId>/script/<fileName>`
- Styles: `/plugin/<pluginId>/style/<fileName>`
- Images: `/plugin/<pluginId>/image/<fileName>`

Plugin scripts can use `API` from `MDTPro/main/scripts/pluginAPI.js`. Open that file in an editor for
the current helper list.

## Build From Source

Restore and build from the repo root:

```powershell
dotnet restore MDTProPlugin\MDTPro.sln
dotnet build MDTProPlugin\MDTPro.sln -c Release
```

Visual Studio works too. Open `MDTProPlugin/MDTPro.sln`, restore packages, and build **Release**.

Create a `References/` folder in the repo root and copy these compile-time files from your GTA V install:

- `LSPD First Response.dll`
- `IPT.Common.dll`

`CalloutInterfaceAPI.dll` is referenced from
`MDTProPlugin/lib/CalloutInterfaceAPI/CalloutInterfaceAPI.dll`. Add it there yourself, for example by
extracting it from the [CalloutInterfaceAPI NuGet package](https://www.nuget.org/packages/CalloutInterfaceAPI).

Other dependencies come from NuGet. `References/` is ignored by git so each developer can use their own game install.

For a full clean build, run:

```powershell
.\build.ps1
```

To build and copy into your GTA V folder:

```powershell
.\build.ps1 -Deploy -GamePath "D:\Games\GTA V"
```

Use `-SkipWindowsApp` only when you intentionally want to leave the native Windows client out of the release output.

### StopThePed API Check

If you update StopThePed, place `StopThePed.dll` under `STP/` and run:

```powershell
powershell -NoProfile -File scripts/verify-stp-api.ps1
```

To inspect the reflected API names:

```powershell
powershell -NoProfile -File scripts/dump-stp-api.ps1
```

That script prints to the console. Redirect it yourself if you want a text snapshot.

## Troubleshooting

- MDT Pro log: `MDTPro/MDTPro.log`.
- Game/plugin load log: `RAGEPluginHook.log` in the GTA V folder.
- Addresses written by MDT Pro: `MDTPro/ipAddresses.txt`.
- In-game menu key: `SettingsMenuKey` in `MDTPro/MDTPro.ini`, default `F10`.

For crashes, search `RAGEPluginHook.log` for `UNHANDLED` or `Exception`. If the stack mentions MDT Pro,
check `MDTPro.log` next. If it only names LSPDFR or another plugin, the issue is usually game build,
mod load order, or another plugin calling game functions too early.

If the settings menu crashes on `F10`, try another free key once. If two different keys crash, the keybind
probably is not the cause.

## License

MDT Pro is licensed under the [Eclipse Public License 2.0](LICENSE).

These default data/config files are excluded and licensed under the [MIT License](MIT%20LICENSE):
`MDTPro/arrestOptions.json`, `MDTPro/citationOptions.json`, `MDTPro/language.json`, and
`MDTPro/config.json`.
