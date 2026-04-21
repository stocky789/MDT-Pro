namespace MDTPro.ALPR {
    /// <summary>Built-in ALPR tuning (not exposed in F7 settings — only enable + HUD position are user-facing).</summary>
    internal static class AlprDefaults {
        /// <summary>Slower tick = fewer scans per second (CDF tags many nearby cars with paperwork flags).</summary>
        internal const int AdvancedTickMs = 160;
        internal const int TerminalMaxRows = 7;
        internal const int TerminalHistoryMinutes = 10;
        internal const float SensorHalfFovDegrees = 22.5f;
        internal const float SensorMinRangeMeters = 5f;
        /// <summary>Tighter read range so the pool of vehicles evaluated per tick stays smaller.</summary>
        internal const float SensorMaxRangeMeters = 16f;
        internal const float SensorMaxPlateAngleDegrees = 45f;
        /// <summary>Hard cap on nearby vehicles passed to the sensor pass (below LSPDFR pool size).</summary>
        internal const int MaxNearbyVehiclesScanCap = 6;
        /// <summary>
        /// Minimum wall-clock gap between promoted alerts (sound + HUD hold + web toast) for stolen/BOLO/owner wanted.
        /// 420s caps severe toasts roughly once per seven minutes of scanning even in heavy traffic.
        /// </summary>
        internal const int MinSecondsBetweenSeverePromotions = 420;
        /// <summary>
        /// Once per ALPR scan tick, if any sensor has a flagged vehicle in view, one roll uses this band; on success at most
        /// one of those vehicles is shown (expected pass rate per tick ~(min+max)/2, here about 7.5%). Same roll drives in-game HUD and browser/native popups.
        /// </summary>
        internal const float TerminalFlaggedReadShowChanceMin = 0.05f;
        internal const float TerminalFlaggedReadShowChanceMax = 0.10f;
        /// <summary>
        /// Minimum seconds between attempts to surface any flagged hit (pool may have many cars; without this, each scan tick
        /// would get its own 5–10% roll). Keeps driving past dense flagged traffic closer to one roll every few seconds.
        /// </summary>
        internal const int MinSecondsBetweenFlaggedPoolRollAttempts = 6;
        /// <summary>After a paperwork-only row shows for a plate, suppress re-surfacing that plate in the terminal for this many seconds (same hit echo).</summary>
        internal const int PaperworkPlateReshowCooldownSeconds = 90;
        internal const bool PlaySoundOnFlaggedHit = true;
        /// <summary>Not const so promotion code can stay wired when toggled during development.</summary>
        internal static readonly bool BlipOnFlaggedHit = false;
    }
}
