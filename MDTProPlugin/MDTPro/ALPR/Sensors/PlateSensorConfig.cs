namespace MDTPro.ALPR.Sensors {
    /// <summary>One virtual plate-reading sensor relative to the police vehicle (MDT-owned parameters).</summary>
    internal sealed class PlateSensorConfig {
        internal PlateSensorConfig(string id, float offsetRight, float offsetForward, float offsetUp, float yawOffsetDegrees,
            float halfFieldOfViewDegrees, float minRangeMeters, float maxRangeMeters, float maxPlateAngleDegrees) {
            Id = id;
            OffsetRight = offsetRight;
            OffsetForward = offsetForward;
            OffsetUp = offsetUp;
            YawOffsetDegrees = yawOffsetDegrees;
            HalfFieldOfViewDegrees = halfFieldOfViewDegrees;
            MinRangeMeters = minRangeMeters;
            MaxRangeMeters = maxRangeMeters;
            MaxPlateAngleDegrees = maxPlateAngleDegrees;
        }

        internal string Id { get; }
        internal float OffsetRight { get; }
        internal float OffsetForward { get; }
        internal float OffsetUp { get; }
        internal float YawOffsetDegrees { get; }
        internal float HalfFieldOfViewDegrees { get; }
        internal float MinRangeMeters { get; }
        internal float MaxRangeMeters { get; }
        internal float MaxPlateAngleDegrees { get; }

        /// <summary>Default four-corner cruiser layout (tunable via <see cref="Setup.Config"/>).</summary>
        internal static PlateSensorConfig[] BuildDefaultQuad(float halfFovDeg, float minR, float maxR, float maxPlateAngle) {
            return new[] {
                new PlateSensorConfig("FL", -0.52f, 0.32f, 0.85f, 45f, halfFovDeg, minR, maxR, maxPlateAngle),
                new PlateSensorConfig("FR", 0.52f, 0.32f, 0.85f, -45f, halfFovDeg, minR, maxR, maxPlateAngle),
                new PlateSensorConfig("RL", -0.52f, -0.95f, 0.85f, 135f, halfFovDeg, minR, maxR, maxPlateAngle),
                new PlateSensorConfig("RR", 0.52f, -0.95f, 0.85f, -135f, halfFovDeg, minR, maxR, maxPlateAngle),
            };
        }
    }
}
