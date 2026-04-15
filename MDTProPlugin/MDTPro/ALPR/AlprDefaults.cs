namespace MDTPro.ALPR {
    /// <summary>Built-in ALPR tuning (not exposed in F7 settings — only enable + HUD position are user-facing).</summary>
    internal static class AlprDefaults {
        /// <summary>Slower tick = fewer scans per second (CDF tags many nearby cars with paperwork flags).</summary>
        internal const int AdvancedTickMs = 120;
        internal const int TerminalMaxRows = 7;
        internal const int TerminalHistoryMinutes = 10;
        internal const float SensorHalfFovDegrees = 22.5f;
        internal const float SensorMinRangeMeters = 5f;
        /// <summary>Tighter read range so the pool of vehicles evaluated per tick stays smaller.</summary>
        internal const float SensorMaxRangeMeters = 18f;
        internal const float SensorMaxPlateAngleDegrees = 45f;
        /// <summary>Hard cap on nearby vehicles passed to the sensor pass (below LSPDFR pool size).</summary>
        internal const int MaxNearbyVehiclesScanCap = 8;
        /// <summary>
        /// Minimum wall-clock gap between promoted alerts (sound + HUD hold + web toast) for stolen/BOLO/owner wanted.
        /// 300s caps you at roughly two such hits per ten minutes even in heavy traffic; raise for rarer callout-style pacing.
        /// </summary>
        internal const int MinSecondsBetweenSeverePromotions = 300;
        /// <summary>
        /// Any non–NO-DATA hit (stolen, BOLO, wanted, reg/ins/DL, etc.): each scan draws a pass threshold uniformly in this range,
        /// then a second roll decides whether that read surfaces in ALPR at all for this tick. Expected pass rate is ~(min+max)/2
        /// (many nearby flagged vehicles each roll independently, so keep this low to avoid constant hits).
        /// </summary>
        internal const float TerminalAlertFlagRollChanceMin = 0.004f;
        internal const float TerminalAlertFlagRollChanceMax = 0.012f;
        internal const bool PlaySoundOnFlaggedHit = true;
        /// <summary>Not const so promotion code can stay wired when toggled during development.</summary>
        internal static readonly bool BlipOnFlaggedHit = false;
    }
}
