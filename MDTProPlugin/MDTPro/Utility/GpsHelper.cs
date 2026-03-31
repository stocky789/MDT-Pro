using Rage;
using Rage.Native;

namespace MDTPro.Utility {
    /// <summary>Helpers for in-game GPS/waypoint using GTA natives. SET_NEW_WAYPOINT (HUD) takes float x, float y per alloc8or/GTA5 Native DB.</summary>
    internal static class GpsHelper {
        /// <summary>Sets the in-game map waypoint at (x, y). Uses HUD::SET_NEW_WAYPOINT. Runs on game fiber.</summary>
        internal static void SetWaypoint(float x, float y) {
            GameFiberHttpBridge.EnqueueFireAndForget(() => {
                try {
                    NativeFunction.Natives.SET_NEW_WAYPOINT(x, y);
                } catch (System.Exception ex) {
                    Game.LogTrivial($"[MDTPro] GpsHelper.SetWaypoint failed: {ex.Message}");
                }
            });
        }
    }
}
