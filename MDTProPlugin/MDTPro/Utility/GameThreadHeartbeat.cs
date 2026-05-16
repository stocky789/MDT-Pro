using System;
using System.Threading;

namespace MDTPro.Utility {
    /// <summary>
    /// Last time the <see cref="GameWorkScheduler"/> game fiber made progress. Used to tell programmatic
    /// <c>Game.IsPaused</c> (LemonUI menus still tick) from a truly frozen script context (Steam overlay / alt-tab).
    /// Updated only from that fiber — no extra thread or fiber, one <see cref="Volatile"/> write per scheduler wake.
    /// </summary>
    internal static class GameThreadHeartbeat {
        static long _lastTickUtcTicks = DateTime.UtcNow.Ticks;

        internal static void RecordTick() {
            Volatile.Write(ref _lastTickUtcTicks, DateTime.UtcNow.Ticks);
        }

        internal static long MillisecondsSinceLastTick {
            get {
                long ticks = Volatile.Read(ref _lastTickUtcTicks);
                return (long)(DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalMilliseconds;
            }
        }

        /// <summary>
        /// True when the game-work scheduler fiber has not recorded a tick in <paramref name="thresholdMs"/> ms
        /// (overlay, alt-tab, or hard hitch). Not tripped by <c>Game.IsPaused = true</c> while LemonUI keeps yielding.
        /// </summary>
        internal static bool IsGameThreadFrozen(int thresholdMs = 750) {
            return MillisecondsSinceLastTick > thresholdMs;
        }
    }
}
