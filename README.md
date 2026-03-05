# MDT Pro

A Police Computer Plugin for LSPDFR.

## Building (for developers)

To build the plugin from source:

1. **Restore NuGet packages**  
   From repo root:  
   `nuget restore MDTProPlugin\MDTPro.sln`  
   (Or open the solution in Visual Studio and build; it will restore automatically.)

2. **Provide reference DLLs**  
   Create a `References` folder in the repo root and copy these from your GTA V install:
   - `plugins/LSPDFR/CalloutInterface.dll`
   - `plugins/LSPDFR/CalloutInterfaceAPI.dll` (or from game root)
   - `plugins/LSPDFR/CommonDataFramework.dll`
   - `plugins/LSPDFR/PolicingRedefined.dll`
   - `plugins/LSPDFR/LSPD First Response.dll` (from `plugins/`)
   - `IPT.Common.dll` (game root)
   - `Newtonsoft.Json.dll` (game root, or comes from NuGet)
   - `System.Data.SQLite.dll` (from `plugins/LSPDFR/`; or ensure NuGet package has the DLL in `packages/.../lib/net46/`)

   `References` is in `.gitignore` (each dev uses their own game copy).

3. **Build**  
   Open `MDTProPlugin/MDTPro.sln` in Visual Studio and build **Release**, or run  
   `.\build-and-deploy.ps1`  
   to build and deploy to a GTA V path (edit `$GameRoot` in the script for your install).

## Installation

- Extract all files and folders from the ZIP into the GTA main directory

## Setup

- When going on duty using LSPDFR, MDT Pro will display notifications in-game containing the addresses used to access the MDT
- If the in-game notifications were missed, addresses are also written to `MDTPro/ipAddresses.txt`
- Access MDT Pro from any browser. Chromium-based browsers (e.g. Chrome, Brave) work best. Enter one of the displayed addresses—if one fails, try another.

### Setup using Steam overlay

- In Steam go to Steam <a>&rarr;</a> Settings <a>&rarr;</a> In Game
- Make sure _Enable the Steam Overlay while in-game_ is enabled
- Set _Overlay shortcut key(s)_ to the key you prefer for opening MDT Pro
- Set _Web browser home page_ to `http://127.0.0.1:8080` (or the address shown by MDT Pro)

## UI Usage

### Desktop

- The taskbar shows an MDT Pro icon in the center. Click it to open the _Control Panel_
- Enter officer information and start or end shifts from the Control Panel
- The [customization page](#customization) is also accessible from here

### Reports

- All reports include general, officer, and location sections. These are auto-filled but can be edited.
- The report ID cannot be changed
- Use the status field in the reports list to filter. Canceled reports are treated as deleted but remain viewable.
- Reports created while on duty (via the shift menu in the Control Panel) are attached to the current shift
- A notes section is available to describe the incident or provide additional details

#### Incident reports

- Incident reports handle any reporting that does not fit the other categories
- Offender, witness, and victim names are optional

#### Citation and arrest reports

- The given charges will be added to the offender, if the offender exists, when creating the report
- With PolicingRedefined installed, citations can be issued to offenders directly from the ped menu

### Ped Lookup

- Entering and searching for a person's name will show various information about that person
- Click the citation or arrest entry in the history section to create a new report for that ped

### Vehicle Lookup

- Entering and searching for a vehicle's license plate or VIN will show various information about that vehicle
- Clicking on the owner area in the basic information area will open the ped search for the vehicle's registered owner

### Shift History

- View all prior shifts and their linked reports
- Reports created during a shift are linked

## Customization

The customization page allows activating plugins and changing configuration.

## Plugins

Plugins extend MDT Pro's functionality by injecting JavaScript and CSS.

### Using a plugin

Place the plugin folder inside `MDTPro/plugins`. The plugin can be activated on the customization page.

### Creating a plugin

#### Plugin folder structure

```
Plugin Name
    │   info.json
    │
    ├───pages
    │       page.html
    │
    ├───scripts
    │       script 1.js
    │       script 2.js
    │
    └───styles
            style 1.css
            style 2.css
```

Multiple pages, scripts, and styles are supported per plugin.

- HTML files in `pages` are served at `/plugin/<pluginId>/page/<fileName>`
- JS files in `scripts` are served at `/plugin/<pluginId>/script/<fileName>`
- CSS files in `styles` are served at `/plugin/<pluginId>/style/<fileName>`
- In JavaScript, retrieve the plugin ID with: `const pluginId = document.currentScript.dataset.pluginId`
Scripts and styles are loaded onto the index page when activated using the customization page.

#### info.json example

```json
{
  "name": "Plugin Name",
  "description": "An MDT Pro plugin",
  "author": "Your Name",
  "version": "2.0.1"
}
```

#### Plugin API

Plugin developers can use the functions in `MDTPro/main/scripts/pluginAPI.js`. Open that file in another editor tab for IntelliSense. All API functions are exposed under the `API` object.

#### Example plugin to create a new page

```js
const pageSVG =
  '<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 1 1" xml:space="preserve"><!-- some valid svg stuff --></svg>'

API.createNewPage('test', 'Test Page', pageSVG, initTestPage) // requires test.html in pages folder

function initTestPage(contentWindow) {
  contentWindow.document.body.innerHTML = 'Test page loaded'
}
```

## License

MDT Pro is licensed under the [Eclipse Public License - v 2.0](LICENSE)

The following files / folders are excluded and licensed under the [MIT License](MIT%20LICENSE): `MDTPro/arrestOptions.json`, `MDTPro/citationOptions.json`, `MDTPro/language.json`, `MDTPro/config.json`
