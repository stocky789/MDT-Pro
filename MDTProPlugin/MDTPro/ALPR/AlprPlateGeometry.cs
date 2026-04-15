using Rage;
using System;

namespace MDTPro.ALPR {
    /// <summary>World-space plate positions for ALPR geometry (MDT-owned math).</summary>
    internal static class AlprPlateGeometry {
        /// <summary>Plate bone or bumper point on <paramref name="vehicle"/> that faces <paramref name="observerWorld"/> (rear vs front).</summary>
        internal static Vector3 GetPlatePositionFacingObserver(Vehicle vehicle, Vector3 observerWorld) {
            if (vehicle == null || !vehicle.Exists()) return observerWorld;
            try {
                Vector3 vPos = vehicle.Position;
                Vector3 vFwd = vehicle.ForwardVector;
                vFwd.Z = 0f;
                if (vFwd.LengthSquared() < 0.0001f) return vPos;
                vFwd = Vector3.Normalize(vFwd);

                Vector3 toObserver = observerWorld - vPos;
                toObserver.Z = 0f;
                if (toObserver.LengthSquared() < 0.0001f) return vPos;
                toObserver = Vector3.Normalize(toObserver);

                // toObserver = unit vector from vehicle toward observer; vFwd = toward hood. Dot > 0 = observer ahead, dot < 0 = observer behind.
                bool observerBehind = Vector3.Dot(toObserver, vFwd) < -0.2f;
                bool observerInFront = Vector3.Dot(toObserver, vFwd) > 0.2f;
                float offset = 2f;
                if (observerBehind) {
                    int rearIdx = vehicle.GetBoneIndex("numberplate");
                    if (rearIdx < 0) rearIdx = vehicle.GetBoneIndex("bumper_r");
                    return rearIdx >= 0 ? vehicle.GetBonePosition(rearIdx) : vPos - vFwd * offset;
                }
                if (observerInFront) {
                    int frontIdx = vehicle.GetBoneIndex("bumper_f");
                    return frontIdx >= 0 ? vehicle.GetBonePosition(frontIdx) : vPos + vFwd * offset;
                }
                return vPos;
            } catch {
                return vehicle.Position;
            }
        }

        /// <summary>Horizontal angle in degrees between vehicle forward and vector from vehicle center to <paramref name="worldPoint"/> (0–180).</summary>
        internal static float HorizontalApproachAngleDeg(Vehicle vehicle, Vector3 worldPoint) {
            if (vehicle == null || !vehicle.Exists()) return 0f;
            Vector3 vFwd = vehicle.ForwardVector;
            vFwd.Z = 0f;
            if (vFwd.LengthSquared() < 0.0001f) return 0f;
            vFwd = Vector3.Normalize(vFwd);
            Vector3 to = worldPoint - vehicle.Position;
            to.Z = 0f;
            if (to.LengthSquared() < 0.0001f) return 0f;
            to = Vector3.Normalize(to);
            float dot = Vector3.Dot(vFwd, to);
            return (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, dot))) * (180.0 / Math.PI));
        }

        internal static Vector3 OffsetFromVehicle(Vehicle vehicle, float offsetRight, float offsetForward, float offsetUp) {
            if (vehicle == null || !vehicle.Exists()) return Vector3.Zero;
            Vector3 f = vehicle.ForwardVector;
            Vector3 r = vehicle.RightVector;
            Vector3 u = vehicle.UpVector;
            return vehicle.Position + r * offsetRight + f * offsetForward + u * offsetUp;
        }

        internal static Vector3 RotateForwardByYawDeg(Vector3 forward, float yawDeg) {
            forward.Z = 0f;
            if (forward.LengthSquared() < 0.0001f) forward = new Vector3(0f, 1f, 0f);
            forward = Vector3.Normalize(forward);
            float rad = yawDeg * (float)(Math.PI / 180.0);
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            return new Vector3(forward.X * cos - forward.Y * sin, forward.X * sin + forward.Y * cos, 0f);
        }

        internal static float HalfAngleDegBetween(Vector3 fromUnit, Vector3 toUnit) {
            float dot = Vector3.Dot(fromUnit, toUnit);
            return (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, dot))) * (180.0 / Math.PI));
        }
    }
}
