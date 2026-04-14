The CalloutInterfaceAPI.dll should go in the root Grand Theft Auto V folder (or plugins/LSPDFR per your setup).

Please visit https://github.com/Immersive-Plugins-Team/CalloutInterfaceAPI for more details.

Building this project (from the MDT-Pro repo): copy into the repo’s References folder (next to CalloutInterfaceAPI):
  • CalloutInterface.dll
  • LSPD First Response.dll
StopThePed is optional at compile time; vehicle document helpers resolve StopThePed at runtime via reflection when STP is loaded in-game.

Public entry points (use these from other plugins — do not reference CalloutInterface.dll directly):
- CalloutInterfaceAPI.Functions.IsCalloutInterfaceAvailable
- GetColorName, GetDateTime
- SendMessage(callout, message), SendVehicle(vehicle)
- GetCalloutFromHandle(LHandle) — resolves the LSPDFR Callout for a handle when Callout Interface is installed
- PublishCadUnitStatus(string) — pushes a CAD / unit line to in-game CI when a compatible CI build exposes SetStatus(string) (or similar)

CalloutInterfaceAPI.CalloutInterfaceAttribute — decorate callouts for CI metadata (name, agency, priority, description).