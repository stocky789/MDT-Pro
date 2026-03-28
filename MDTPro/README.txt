MDT Pro - Installation
======================

REQUIREMENTS
  • LSPDFR
  • CommonDataFramework (CDF) — ALWAYS required (including if you use StopThePed + Ultimate Backup).
  • CalloutInterfaceAPI (in game root or plugins/LSPDFR/)
  • CalloutInterface — required (Active Call page uses it for live callout details).

  Stops / backup / citations — use ONE path (do not mix):
  • Policing Redefined — install PR; use MDT Pro Mod integration for PR-based stops, citations, backup.
  • StopThePed + Ultimate Backup — supported. Install StopThePed and Ultimate Backup; set Mod integration
    in the MDT (Customization → Config) to match. IMPORTANT: do NOT install Policing Redefined on this
    setup — PR + StopThePed + Ultimate Backup together is unsupported and can break behavior.
    You still NEED CommonDataFramework.

INSTALL
  1. Extract the full mod ZIP into your GTA V folder (the one with GTA5.exe).
  2. Keep the folder structure: you should see "plugins" and "MDTPro" in the same place as GTA5.exe.

USE
  1. Start GTA V and go on duty with LSPDFR.
  2. MDT Pro will show on-screen addresses (e.g. http://127.0.0.1:9000).
  3. Open that address in any browser (Chrome/Brave recommended).

  If you can't connect from another device (phone, tablet, etc.): this is usually a Windows
  Firewall issue (on your game PC, not your router). Add an inbound rule for port 9000
  (or your chosen port in config) in Windows Firewall.

UPDATING
  Overwrite the existing plugin files and the MDTPro folder. Your MDTPro/data/ and config.json are kept.
