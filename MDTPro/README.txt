MDT Pro - quick install
=======================

MDT Pro starts a local browser MDT when you go on duty with LSPDFR.

Requirements
------------
Install these external mods:

  - LSPDFR and RagePluginHook
  - CommonDataFramework (CDF), required for every setup

The MDT Pro release comes with the below prepackaged:

  - plugins\LSPDFR\MDTPro.dll
  - plugins\LSPDFR\CalloutInterfaceAPI.dll
  - plugins\LSPDFR\Newtonsoft.Json.dll
  - plugins\LSPDFR\LemonUI.RagePluginHook.dll
  - System.Data.SQLite.dll in the GTA V root
  - x64\SQLite.Interop.dll
  - MDTPro\
  - MDTProNative\ when included in the release

LemonUI is required. The release includes it, but you can get it here if you ever need to replace it:
https://github.com/LemonUIbyLemon/LemonUI/releases/download/v2.2/LemonUI.zip

Callout Interface is optional for loading, but needed for live Active Call details.

Pick one local integration stack:

  - Policing Redefined
  - StopThePed + Ultimate Backup

Do not run Policing Redefined together with StopThePed + Ultimate Backup. CDF is still required either
way.

MDT Cloud login:

  - Register an account at https://mdt.stockhosting.com.au
  - Use Policing Redefined only
  - StopThePed + Ultimate Backup is local-MDT only and is not supported for Cloud login right now

Install
-------
Copy the release into your GTA V folder, the one with GTA5.exe.

Keep the folder layout intact. You should end up with:

  - plugins\LSPDFR\MDTPro.dll
  - plugins\LSPDFR\CalloutInterfaceAPI.dll
  - plugins\LSPDFR\Newtonsoft.Json.dll
  - plugins\LSPDFR\LemonUI.RagePluginHook.dll
  - MDTPro\
  - System.Data.SQLite.dll in the GTA V root
  - x64\SQLite.Interop.dll

Use
---
1. Start GTA V and go on duty with LSPDFR.
2. MDT Pro shows one or more addresses, usually http://127.0.0.1:9000.
3. Open that address in Chrome, Edge, Brave, Steam overlay, or the native Windows MDT.

If another device on your network cannot connect, add a Windows Firewall inbound rule on the game PC for
the MDT port, usually 9000.

Update
------
Overwrite the plugin files and the MDTPro folder with the new release.

Keep MDTPro\data\ and MDTPro\config.json if you want to keep your records and settings.

Troubleshooting
---------------
  - MDT Pro log: MDTPro\MDTPro.log
  - Game/plugin load log: RAGEPluginHook.log
  - Saved MDT addresses: MDTPro\ipAddresses.txt
  - Default in-game MDT Pro menu key: F10
