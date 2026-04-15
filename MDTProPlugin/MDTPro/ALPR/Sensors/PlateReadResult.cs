using Rage;

namespace MDTPro.ALPR.Sensors {
    internal enum PlateReadStatus {
        GoodRead,
        OutOfRange,
        BadPlateAngle,
        OutOfFieldOfView,
        NoLineOfSight,
        NoPlateBone,
        Skipped
    }

    internal sealed class PlateReadResult {
        internal PlateReadResult(PlateSensorConfig sensor, Vehicle target, Vector3 plateWorld, PlateReadStatus status) {
            Sensor = sensor;
            Target = target;
            PlateWorld = plateWorld;
            Status = status;
        }

        internal PlateSensorConfig Sensor { get; }
        internal Vehicle Target { get; }
        internal Vector3 PlateWorld { get; }
        internal PlateReadStatus Status { get; }

        internal bool IsGood => Status == PlateReadStatus.GoodRead;
    }
}
