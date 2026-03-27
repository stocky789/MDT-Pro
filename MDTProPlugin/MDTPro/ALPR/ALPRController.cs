using CommonDataFramework.Modules.VehicleDatabase;
using MDTPro.Data;
using MDTPro.Setup;
using MDTPro.Utility;
using Rage;
using System;
using System.Collections.Generic;
using System.Linq;
using static MDTPro.Setup.SetupController;
using static MDTPro.Utility.Helper;

namespace MDTPro.ALPR {
    /// <summary>
    /// Coordinates ALPR scanning, flag detection, and broadcasting to HUD/WebSocket.
    /// Registration/insurance flags follow live CDF vehicle documents only (same source as Callout Interface), not null/empty string heuristics or SQLite snapshots.
    /// </summary>
    internal static class ALPRController {
        private static GameFiber _scanFiber;
        private static readonly HashSet<string> AlertedPlates = new HashSet<string>();
        private static readonly Dictionary<string, DateTime> PlateCooldown = new Dictionary<string, DateTime>();
        private static readonly object Lock = new object();
        private static readonly TimeSpan CurrentHitHoldDuration = TimeSpan.FromSeconds(30);
        private static DateTime _currentHitHoldUntilUtc = DateTime.MinValue;
        /// <summary>Reused each scan to avoid per-frame allocations (scan fiber only).</summary>
        private static readonly List<(Vehicle vehicle, float distance)> InConeAndRangeBuffer = new List<(Vehicle, float)>(16);

        private const int ScanIntervalMs = 2000;
        private const int CooldownSeconds = 90;
        private const float ScanRangeMeters = 50f;
        private const float ReadRangeMeters = 40f;
        private const float ConeAngleDegrees = 22f;

        internal static ALPRHit CurrentHit { get; private set; }
        internal static bool IsRunning { get; private set; }
        /// <summary>Cached by the scan loop so the HUD can read it from RawFrameRender without calling natives.</summary>
        internal static bool IsInPoliceVehicleCached { get; private set; }

        private static readonly object StartLock = new object();

        /// <summary>Start ALPR. Called from Main when player goes on duty. Only runs if alprEnabled in settings.
        /// Scanning and HUD require: enabled in settings + on duty (Start/Stop) + in police vehicle.</summary>
        internal static void Start() {
            lock (StartLock) {
                if (IsRunning) return;
                var cfg = GetConfig();
                if (cfg == null || !cfg.alprEnabled) return;
                IsRunning = true;
            }
            lock (Lock) {
                AlertedPlates.Clear();
                PlateCooldown.Clear();
            }

            _scanFiber = GameFiber.StartNew(ScanLoop);
            ALPRHUD.Start();
            Log("ALPR started", false, LogSeverity.Info);
        }

        internal static void Stop() {
            IsRunning = false;
            IsInPoliceVehicleCached = false;
            _scanFiber?.Abort();
            _scanFiber = null;
            ALPRHUD.Stop();
            CurrentHit = null;
            _currentHitHoldUntilUtc = DateTime.MinValue;
            lock (Lock) {
                AlertedPlates.Clear();
                PlateCooldown.Clear();
            }
            Log("ALPR stopped", false, LogSeverity.Info);
        }

        internal static void Clear() {
            CurrentHit = null;
            _currentHitHoldUntilUtc = DateTime.MinValue;
            lock (Lock) {
                AlertedPlates.Clear();
                PlateCooldown.Clear();
            }
        }

        private static void ScanLoop() {
            int loopCount = 0;

            int intervalMs = 2000;
            while (IsRunning && Server.RunServer) {
                try {
                    ExpireDisplayedHitIfNeeded();
                    var cfg = GetConfig();
                    if (cfg == null || !cfg.alprEnabled) {
                        GameFiber.Sleep(1000);
                        continue;
                    }
                    intervalMs = ScanIntervalMs;
                    int cooldownSec = CooldownSeconds;
                    const int maxReadPerCycle = 3;
                    int maxVehicles = Math.Min(16, Math.Max(5, cfg.maxNumberOfNearbyPedsOrVehicles + 2));
                    float scanRangeMeters = ScanRangeMeters;
                    float readRangeMeters = ReadRangeMeters;
                    float coneAngleDeg = ConeAngleDegrees;

                    // ALPR only works when: (1) enabled in settings, (2) on duty (Start/Stop in Main), (3) in police vehicle
                    if (Main.Player == null || !Main.Player.Exists()) {
                        CurrentHit = null;
                        _currentHitHoldUntilUtc = DateTime.MinValue;
                        GameFiber.Sleep(intervalMs);
                        continue;
                    }

                    Vehicle playerVehicle = Main.Player.CurrentVehicle;
                    bool inPoliceVehicle = playerVehicle != null && playerVehicle.Exists() && playerVehicle.IsPoliceVehicle;
                    IsInPoliceVehicleCached = inPoliceVehicle;
                    if (!inPoliceVehicle) {
                        CurrentHit = null;
                        _currentHitHoldUntilUtc = DateTime.MinValue;
                        GameFiber.Sleep(intervalMs);
                        continue;
                    }

                    Vehicle[] nearby = Main.Player.GetNearbyVehicles(maxVehicles);
                    if (nearby == null || nearby.Length == 0) {
                        GameFiber.Sleep(intervalMs);
                        continue;
                    }

                    if (loopCount > 0 && loopCount % 30 == 0)
                        PruneExpiredCooldowns(cooldownSec);

                    // Vehicles in cone up to scanRangeMeters; we will process up to 3 within read range (closest first)
                    InConeAndRangeBuffer.Clear();
                    Vector3 cruiserPos = playerVehicle.Position;
                    Vector3 forward = playerVehicle.ForwardVector;
                    forward.Z = 0f;
                    if (forward.LengthSquared() < 0.0001f) forward = new Vector3(0f, 1f, 0f);
                    else forward = Vector3.Normalize(forward);

                    foreach (Vehicle v in nearby) {
                        if (v == null || !v.Exists()) continue;
                        if (v == playerVehicle) continue;
                        Vector3 platePos = GetPlatePositionFacingCruiser(v, cruiserPos);
                        float dist = cruiserPos.DistanceTo(platePos);
                        if (dist > scanRangeMeters) continue;
                        if (!IsInCone(cruiserPos, forward, platePos, coneAngleDeg)) continue;
                        string p = v.LicensePlate?.Trim();
                        if (string.IsNullOrEmpty(p)) continue;
                        InConeAndRangeBuffer.Add((v, dist));
                    }

                    // Sort by distance and take up to 3 vehicles within read range
                    SortBufferByDistance(InConeAndRangeBuffer);
                    int processed = 0;

                    for (int i = 0; i < InConeAndRangeBuffer.Count && processed < maxReadPerCycle; i++) {
                        var (toScan, dist) = InConeAndRangeBuffer[i];
                        if (dist > readRangeMeters) continue;

                        string plate = toScan.LicensePlate?.Trim();
                        if (string.IsNullOrEmpty(plate)) continue;

                        string plateKey = plate.ToUpperInvariant();
                        try {
                            MDTProVehicleData vd = new MDTProVehicleData(toScan);
                            MDTProVehicleData dbVehicle = DataController.GetVehicleByLicensePlate(plate);
                            List<string> flags;
                            string owner;
                            string modelDisplayName;

                            if (vd.CDFVehicleData?.Owner != null) {
                                flags = BuildFlagsFromLiveCdfVehicle(vd, dbVehicle);
                                modelDisplayName = vd.ModelDisplayName ?? vd.ModelName ?? "";
                                // Prefer MDT DB owner over CDF for re-encounters: CDF assigns a fresh persona per spawn,
                                // but our DB has the persistent identity (correct owner) from prior stops.
                                owner = (dbVehicle != null && !string.IsNullOrEmpty(dbVehicle.Owner))
                                    ? dbVehicle.Owner
                                    : (vd.Owner ?? "");
                            } else {
                                // No CDF data: use MDT database (stolen/expired from prior stops or saved data)
                                if (dbVehicle != null) {
                                    flags = BuildFlagsPersistedVehicleOnly(dbVehicle);
                                    owner = dbVehicle.Owner ?? "—";
                                    modelDisplayName = dbVehicle.ModelDisplayName ?? dbVehicle.ModelName ?? "";
                                } else {
                                    flags = new List<string> { "Not in database" };
                                    owner = "—";
                                    modelDisplayName = "";
                                }
                                if (string.IsNullOrEmpty(modelDisplayName))
                                    modelDisplayName = GetModelDisplayName(toScan);
                            }

                            if (string.IsNullOrEmpty(modelDisplayName))
                                modelDisplayName = GetModelDisplayName(toScan);

                            string vehicleColor = GetVehicleColorDisplay(vd, dbVehicle);

                            bool shouldAlert;
                            lock (Lock) {
                                shouldAlert = !AlertedPlates.Contains(plateKey);
                                if (shouldAlert) {
                                    AlertedPlates.Add(plateKey);
                                    PlateCooldown[plateKey] = DateTime.UtcNow.AddSeconds(cooldownSec);
                                }
                            }

                            var hit = new ALPRHit {
                                Plate = vd.LicensePlate ?? plate,
                                Owner = owner,
                                ModelDisplayName = modelDisplayName,
                                VehicleColor = vehicleColor,
                                Flags = flags,
                                TimeScanned = DateTime.Now
                            };

                            // Only show full vehicle info when there is at least one alert flag (stolen, no insurance, etc.). Otherwise HUD shows "Scanning".
                            if (HasAlertFlags(hit) && ShouldReplaceDisplayedHit(hit)) {
                                SetDisplayedHit(hit);
                                if (shouldAlert)
                                    PlayAlprHitSound();
                            }
                            processed++;

                            if (shouldAlert) {
                                var hitCopy = hit;
                                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                                    ServerAPI.WebSocketHandler.BroadcastALPRHit(hitCopy));
                            }
                        } catch (Exception ex) {
                            Log($"ALPR scan skip vehicle {plate}: {ex.Message}", false, LogSeverity.Warning);
                        }
                    }
                } catch (Exception ex) {
                    Log($"ALPR scan error: {ex.Message}", false, LogSeverity.Error);
                }

                loopCount++;
                GameFiber.Sleep(intervalMs);
            }
        }

        /// <summary>True if the hit has at least one real alert flag (stolen, no insurance, etc.), not just "Not in database".</summary>
        private static bool HasAlertFlags(ALPRHit hit) {
            if (hit?.Flags == null || hit.Flags.Count == 0) return false;
            foreach (string f in hit.Flags) {
                if (string.IsNullOrEmpty(f)) continue;
                if (string.Equals(f.Trim(), "Not in database", StringComparison.OrdinalIgnoreCase))
                    continue;
                return true;
            }
            return false;
        }

        private static void PlayAlprHitSound() {
            try {
                Rage.Native.NativeFunction.Natives.PLAY_SOUND_FRONTEND(-1, "SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET", false);
            } catch {
                // Ignore if native fails (e.g. different RPH version)
            }
        }

        /// <summary>Builds a short color string for the ALPR HUD (e.g. "Black / White") from vehicle or DB data.</summary>
        private static string GetVehicleColorDisplay(MDTProVehicleData vd, MDTProVehicleData dbVehicle) {
            string primary = null;
            string secondary = null;
            if (vd != null && (vd.PrimaryColor != null || vd.SecondaryColor != null)) {
                primary = vd.PrimaryColor?.Trim();
                secondary = vd.SecondaryColor?.Trim();
            }
            if ((primary == null || secondary == null) && dbVehicle != null) {
                if (string.IsNullOrEmpty(primary)) primary = dbVehicle.PrimaryColor?.Trim();
                if (string.IsNullOrEmpty(secondary)) secondary = dbVehicle.SecondaryColor?.Trim();
            }
            if (!string.IsNullOrEmpty(primary) && !string.IsNullOrEmpty(secondary))
                return primary + " / " + secondary;
            if (!string.IsNullOrEmpty(primary)) return primary;
            if (!string.IsNullOrEmpty(secondary)) return secondary;
            if (vd?.Color != null && !string.IsNullOrWhiteSpace(vd.Color)) return vd.Color.Trim();
            if (dbVehicle?.Color != null && !string.IsNullOrWhiteSpace(dbVehicle.Color)) return dbVehicle.Color.Trim();
            return null;
        }

        /// <summary>ALPR when CDF has an owner: stolen/BOLO/wanted plus registration/insurance from CDF document objects only.</summary>
        private static List<string> BuildFlagsFromLiveCdfVehicle(MDTProVehicleData vd, MDTProVehicleData dbVehicle) {
            var flags = new List<string>();
            if (vd != null && vd.IsStolen)
                flags.Add("Stolen");
            if (DataController.HasActiveBOLOs(vd) || (dbVehicle != null && DataController.HasActiveBOLOs(dbVehicle)))
                flags.Add("BOLO");

            VehicleData cdf = vd?.CDFVehicleData;
            if (cdf != null) {
                AppendCdfRegistrationInsuranceFlags(flags, cdf);
                if (cdf.Owner?.Wanted == true)
                    flags.Add("Owner wanted");
            }

            return flags;
        }

        /// <summary>No live CDF owner: stolen/BOLO from MDT persistence only — do not show reg/insurance (would not match Callout Interface).</summary>
        private static List<string> BuildFlagsPersistedVehicleOnly(MDTProVehicleData dbVehicle) {
            var flags = new List<string>();
            if (dbVehicle == null) return flags;
            if (dbVehicle.IsStolen)
                flags.Add("Stolen");
            if (DataController.HasActiveBOLOs(dbVehicle))
                flags.Add("BOLO");
            return flags;
        }

        /// <summary>Uses CDF Registration/Insurance entities when present; never infers violations from missing or blank strings on MDTProVehicleData.</summary>
        private static void AppendCdfRegistrationInsuranceFlags(List<string> flags, VehicleData cdf) {
            if (cdf == null) return;
            try {
                if (cdf.Registration != null)
                    AppendOneCdfDocumentFlags(flags, cdf.Registration, "Registration expired", "No registration");
            } catch { /* CDF version differences */ }
            try {
                if (cdf.Insurance != null)
                    AppendOneCdfDocumentFlags(flags, cdf.Insurance, "Insurance expired", "No insurance");
            } catch { }
        }

        private static void AppendOneCdfDocumentFlags(List<string> flags, object document, string expiredLabel, string invalidLabel) {
            if (document == null || flags == null) return;
            string statusStr = null;
            DateTime? expiration = null;
            try {
                var stProp = document.GetType().GetProperty("Status");
                if (stProp != null)
                    statusStr = stProp.GetValue(document)?.ToString();
                var expProp = document.GetType().GetProperty("ExpirationDate");
                if (expProp != null) {
                    object ev = expProp.GetValue(document);
                    if (ev is DateTime dt) expiration = dt;
                }
            } catch {
                return;
            }

            if (DocumentStatusImpliesExpired(statusStr, expiration)) {
                flags.Add(expiredLabel);
                return;
            }
            if (IsSuspendedRevokedOrNoneStatus(statusStr))
                flags.Add(invalidLabel);
        }

        private static bool DocumentStatusImpliesExpired(string status, DateTime? expirationDate) {
            if (!string.IsNullOrEmpty(status) && string.Equals(status, "Expired", StringComparison.OrdinalIgnoreCase))
                return true;
            if (expirationDate.HasValue && expirationDate.Value.Date < DateTime.UtcNow.Date) {
                if (string.IsNullOrEmpty(status) || string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsSuspendedRevokedOrNoneStatus(string status) {
            if (string.IsNullOrEmpty(status)) return false;
            return string.Equals(status, "Suspended", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Revoked", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "None", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Sorts the buffer in place by distance (closest first).</summary>
        private static void SortBufferByDistance(List<(Vehicle vehicle, float distance)> buffer) {
            if (buffer == null || buffer.Count <= 1) return;
            for (int i = 0; i < buffer.Count - 1; i++) {
                int minIdx = i;
                float minDist = buffer[i].distance;
                for (int j = i + 1; j < buffer.Count; j++) {
                    if (buffer[j].distance < minDist) {
                        minDist = buffer[j].distance;
                        minIdx = j;
                    }
                }
                if (minIdx != i) {
                    var t = buffer[i];
                    buffer[i] = buffer[minIdx];
                    buffer[minIdx] = t;
                }
            }
        }

        /// <summary>Gets localized display name for the vehicle model (used when CDF has no owner data).</summary>
        private static string GetModelDisplayName(Vehicle v) {
            if (v == null || !v.Exists()) return "";
            try {
                string raw = Rage.Native.NativeFunction.Natives.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL<string>(v.Model.Hash);
                return string.IsNullOrEmpty(raw) ? "" : Game.GetLocalizedString(raw);
            } catch {
                return v.Model.Name ?? "";
            }
        }

        /// <summary>Returns true if target position is within the given cone (horizontal angle) in front of the cruiser.</summary>
        private static bool IsInCone(Vector3 cruiserPos, Vector3 forwardHorizontal, Vector3 targetPos, float coneAngleDegrees) {
            Vector3 toTarget = targetPos - cruiserPos;
            toTarget.Z = 0f;
            if (toTarget.LengthSquared() < 0.0001f) return true;
            toTarget = Vector3.Normalize(toTarget);
            float dot = Vector3.Dot(forwardHorizontal, toTarget);
            float angleDeg = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, dot))) * (180.0 / Math.PI));
            return angleDeg <= coneAngleDegrees;
        }

        /// <summary>Gets the world position of the license plate facing the cruiser (front or rear). Uses bone positions when available; otherwise approximates from vehicle center and facing. This ensures both front and rear plates can be read.</summary>
        private static Vector3 GetPlatePositionFacingCruiser(Vehicle vehicle, Vector3 cruiserPos) {
            try {
                Vector3 vPos = vehicle.Position;
                Vector3 vFwd = vehicle.ForwardVector;
                vFwd.Z = 0f;
                if (vFwd.LengthSquared() < 0.0001f) return vPos;
                vFwd = Vector3.Normalize(vFwd);

                Vector3 toCruiser = cruiserPos - vPos;
                toCruiser.Z = 0f;
                if (toCruiser.LengthSquared() < 0.0001f) return vPos;
                toCruiser = Vector3.Normalize(toCruiser);

                // Rear plate faces backward (-vFwd); we see it when we're behind (toCruiser aligns with +vFwd)
                bool cruiserBehind = Vector3.Dot(toCruiser, vFwd) > 0.2f;
                // Front plate faces forward (+vFwd); we see it when we're in front (toCruiser aligns with -vFwd)
                bool cruiserInFront = Vector3.Dot(toCruiser, vFwd) < -0.2f;

                float offset = 2f;
                if (cruiserBehind) {
                    int rearIdx = vehicle.GetBoneIndex("numberplate");
                    if (rearIdx < 0) rearIdx = vehicle.GetBoneIndex("bumper_r");
                    return rearIdx >= 0 ? vehicle.GetBonePosition(rearIdx) : vPos + vFwd * offset;
                }
                if (cruiserInFront) {
                    int frontIdx = vehicle.GetBoneIndex("bumper_f");
                    return frontIdx >= 0 ? vehicle.GetBonePosition(frontIdx) : vPos - vFwd * offset;
                }
                return vPos;
            } catch {
                return vehicle.Position;
            }
        }

        private static void PruneExpiredCooldowns(int cooldownSec) {
            lock (Lock) {
                var now = DateTime.UtcNow;
                var expired = PlateCooldown
                    .Where(kv => now >= kv.Value)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (string key in expired) {
                    PlateCooldown.Remove(key);
                    AlertedPlates.Remove(key);
                }
            }
        }

        private static bool IsDisplayedHitHeld() {
            return CurrentHit != null && DateTime.UtcNow < _currentHitHoldUntilUtc;
        }

        private static bool ShouldReplaceDisplayedHit(ALPRHit candidate) {
            if (candidate == null) return false;
            if (CurrentHit == null) return true;
            if (!IsDisplayedHitHeld()) return true;

            // While locked, only replace a non-flagged display with a flagged one.
            // This preserves a stable HUD but still promotes important hits immediately.
            bool currentHasFlags = CurrentHit.HasFlags;
            bool candidateHasFlags = candidate.HasFlags;
            return !currentHasFlags && candidateHasFlags;
        }

        private static void SetDisplayedHit(ALPRHit hit) {
            CurrentHit = hit;
            _currentHitHoldUntilUtc = DateTime.UtcNow.Add(CurrentHitHoldDuration);
        }

        private static void ExpireDisplayedHitIfNeeded() {
            if (CurrentHit == null) return;
            if (DateTime.UtcNow >= _currentHitHoldUntilUtc) {
                CurrentHit = null;
                _currentHitHoldUntilUtc = DateTime.MinValue;
            }
        }

        internal static int GetDisplayedHitSecondsRemaining() {
            if (CurrentHit == null) return 0;
            var remaining = _currentHitHoldUntilUtc - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0) return 0;
            return (int)Math.Ceiling(remaining.TotalSeconds);
        }
    }
}
