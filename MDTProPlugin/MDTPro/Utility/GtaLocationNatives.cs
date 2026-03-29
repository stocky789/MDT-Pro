using Rage;
using Rage.Native;
using System;
using System.Runtime.InteropServices;

namespace MDTPro.Utility {
    /// <summary>
    /// Street/zone via GTA natives (NativeDB: GET_STREET_NAME_AT_COORD, GET_STREET_NAME_FROM_HASH_KEY, GET_NAME_OF_ZONE, GET_LABEL_TEXT via <see cref="Game.GetLocalizedString"/>).
    /// Uses ground-snapped coords (pathfind expects surface Z) and IntPtr for zone key — RPH cannot use <c>CallByName&lt;string&gt;</c> for pointer returns.
    /// </summary>
    internal static class GtaLocationNatives {
        internal static Vector3 GroundSnapForPathfind(Vector3 pos) {
            try {
                float? gz = World.GetGroundZ(pos, true, false);
                if (gz.HasValue) return new Vector3(pos.X, pos.Y, gz.Value + 0.45f);
            } catch {
                /* ignore */
            }
            return pos;
        }

        internal static string TryGetStreetDisplayName(Vector3 pos) {
            Vector3 p = GroundSnapForPathfind(pos);
            try {
                uint streetHash = 0u, crossingHash = 0u;
                try {
                    NativeFunction.Natives.GetStreetNameAtCoord<bool>(p.X, p.Y, p.Z, out streetHash, out crossingHash);
                } catch {
                    /* RPH build without this wrapper — fall through */
                }
                if (streetHash != 0u) {
                    string s = World.GetStreetName(streetHash);
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                }
            } catch {
                /* ignore */
            }
            try {
                uint h = World.GetStreetHash(p);
                if (h != 0u) {
                    string s = World.GetStreetName(h);
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                }
            } catch {
                /* ignore */
            }
            try {
                string s = World.GetStreetName(p);
                return string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
            } catch {
                return "";
            }
        }

        /// <summary>Returns a human-readable zone/area name from game labels, or empty.</summary>
        internal static string TryGetLocalizedZoneName(Vector3 pos) {
            Vector3 p = GroundSnapForPathfind(pos);
            string raw = TryGetNameOfZoneKey(p);
            if (string.IsNullOrWhiteSpace(raw)) return "";
            raw = raw.Trim('\0', ' ', '\t');
            try {
                string loc = Game.GetLocalizedString(raw);
                if (!string.IsNullOrWhiteSpace(loc)) {
                    string t = loc.Trim();
                    if (!t.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return t;
                }
            } catch {
                /* ignore */
            }
            return raw;
        }

        private static string TryGetNameOfZoneKey(Vector3 pos) {
            try {
                IntPtr ip = NativeFunction.CallByName<IntPtr>("GET_NAME_OF_ZONE",
                    new NativeArgument(pos.X),
                    new NativeArgument(pos.Y),
                    new NativeArgument(pos.Z));
                if (ip != IntPtr.Zero) {
                    string s = Marshal.PtrToStringAnsi(ip);
                    if (!string.IsNullOrEmpty(s)) return s.Trim('\0', ' ', '\t');
                }
            } catch {
                /* ignore */
            }
            return null;
        }
    }
}
