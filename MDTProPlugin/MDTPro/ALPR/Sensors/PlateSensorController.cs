using Rage;
using Rage.Native;
using System.Collections.Generic;

namespace MDTPro.ALPR.Sensors {
    /// <summary>Evaluates virtual sensors against nearby vehicles (MDT-owned geometry and LOS checks).</summary>
    internal static class PlateSensorController {
        private static readonly PlateSensorConfig[] Sensors = PlateSensorConfig.BuildDefaultQuad(
            AlprDefaults.SensorHalfFovDegrees,
            AlprDefaults.SensorMinRangeMeters,
            AlprDefaults.SensorMaxRangeMeters,
            AlprDefaults.SensorMaxPlateAngleDegrees);

        /// <summary>Good reads only: at most one vehicle per sensor per tick, closest vehicles first.</summary>
        internal static List<PlateReadResult> EvaluateTick(Vehicle cruiser, int maxNearbyVehicles, float weatherRangeMultiplier) {
            var good = new List<PlateReadResult>();
            if (cruiser == null || !cruiser.Exists()) return good;
            PlateSensorConfig[] sensors = Sensors;
            var claimedSensors = new HashSet<string>();
            int cap = System.Math.Min(16, System.Math.Max(1, maxNearbyVehicles));
            Vehicle[] nearby = null;
            try {
                nearby = Game.LocalPlayer.Character.GetNearbyVehicles(cap);
            } catch {
                return good;
            }
            if (nearby == null || nearby.Length == 0) return good;

            var ordered = new List<Vehicle>();
            foreach (Vehicle v in nearby) {
                if (v != null && v.Exists() && v != cruiser) ordered.Add(v);
            }
            ordered.Sort((a, b) => cruiser.DistanceTo(a).CompareTo(cruiser.DistanceTo(b)));

            foreach (Vehicle target in ordered) {
                try {
                    if (target.LicensePlateType == LicensePlateType.None) continue;
                } catch { /* ignore */ }

                foreach (PlateSensorConfig sensor in sensors) {
                    if (claimedSensors.Contains(sensor.Id)) continue;
                    PlateReadResult one = EvaluateOne(cruiser, target, sensor, weatherRangeMultiplier);
                    if (one.IsGood) {
                        good.Add(one);
                        claimedSensors.Add(sensor.Id);
                    }
                }
            }

            return good;
        }

        private static PlateReadResult EvaluateOne(Vehicle cruiser, Vehicle target, PlateSensorConfig sensor, float weatherMul) {
            Vector3 sensorPos = AlprPlateGeometry.OffsetFromVehicle(cruiser, sensor.OffsetRight, sensor.OffsetForward, sensor.OffsetUp);
            Vector3 fwd = AlprPlateGeometry.RotateForwardByYawDeg(cruiser.ForwardVector, sensor.YawOffsetDegrees);
            if (fwd.LengthSquared() < 0.0001f)
                fwd = Vector3.Normalize(cruiser.ForwardVector);

            Vector3 plateWorld = AlprPlateGeometry.GetPlatePositionFacingObserver(target, sensorPos);
            float dist = sensorPos.DistanceTo(plateWorld);
            float maxR = sensor.MaxRangeMeters * System.Math.Max(0.5f, weatherMul);
            if (dist < sensor.MinRangeMeters || dist > maxR)
                return new PlateReadResult(sensor, target, plateWorld, PlateReadStatus.OutOfRange);

            float headingDiff = System.Math.Abs(WrapAngle180(cruiser.Heading - target.Heading));
            if (headingDiff > sensor.MaxPlateAngleDegrees && headingDiff < 180f - sensor.MaxPlateAngleDegrees)
                return new PlateReadResult(sensor, target, plateWorld, PlateReadStatus.BadPlateAngle);

            Vector3 toPlate = plateWorld - sensorPos;
            toPlate.Z = 0f;
            if (toPlate.LengthSquared() < 0.0001f)
                return new PlateReadResult(sensor, target, plateWorld, PlateReadStatus.OutOfFieldOfView);
            toPlate = Vector3.Normalize(toPlate);
            float halfAngle = AlprPlateGeometry.HalfAngleDegBetween(fwd, toPlate);
            if (halfAngle > sensor.HalfFieldOfViewDegrees)
                return new PlateReadResult(sensor, target, plateWorld, PlateReadStatus.OutOfFieldOfView);

            if (!HasClearLos(cruiser, target))
                return new PlateReadResult(sensor, target, plateWorld, PlateReadStatus.NoLineOfSight);

            return new PlateReadResult(sensor, target, plateWorld, PlateReadStatus.GoodRead);
        }

        private static bool HasClearLos(Vehicle cruiser, Vehicle target) {
            try {
                return NativeFunction.Natives.HAS_ENTITY_CLEAR_LOS_TO_ENTITY<bool>(cruiser, target, 17);
            } catch {
                return distFallback(cruiser, target);
            }
        }

        private static bool distFallback(Vehicle a, Vehicle b) {
            try {
                return a.DistanceTo(b) < 22f;
            } catch {
                return true;
            }
        }

        private static float WrapAngle180(float deg) {
            while (deg > 180f) deg -= 360f;
            while (deg < -180f) deg += 360f;
            return deg;
        }
    }
}
