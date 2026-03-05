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
    /// </summary>
    internal static class ALPRController {
        private static GameFiber _scanFiber;
        private static readonly HashSet<string> AlertedPlates = new HashSet<string>();
        private static readonly Dictionary<string, DateTime> PlateCooldown = new Dictionary<string, DateTime>();
        private static readonly object Lock = new object();
        /// <summary>Reused each scan to avoid per-frame allocations (scan fiber only).</summary>
        private static readonly List<(Vehicle vehicle, float distance)> InConeAndRangeBuffer = new List<(Vehicle, float)>(16);

        internal static ALPRHit CurrentHit { get; private set; }
        internal static bool IsRunning { get; private set; }

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
            Log("ALPR started", true, LogSeverity.Info);
        }

        internal static void Stop() {
            IsRunning = false;
            _scanFiber?.Abort();
            _scanFiber = null;
            ALPRHUD.Stop();
            CurrentHit = null;
            lock (Lock) {
                AlertedPlates.Clear();
                PlateCooldown.Clear();
            }
            Log("ALPR stopped", true, LogSeverity.Info);
        }

        internal static void Clear() {
            CurrentHit = null;
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
                    var cfg = GetConfig();
                    if (cfg == null || !cfg.alprEnabled) {
                        GameFiber.Sleep(1000);
                        continue;
                    }
                    // Minimum 1500 ms between scans to avoid FPS impact (max ~0.67 scans/sec)
                    intervalMs = Math.Max(1500, Math.Min(10000, cfg.alprScanIntervalMs));
                    int cooldownSec = Math.Max(10, Math.Min(300, cfg.alprCooldownSeconds));
                    // Cap vehicles considered for ALPR to keep iteration cheap (max 10)
                    int maxVehicles = Math.Min(10, Math.Max(3, cfg.maxNumberOfNearbyPedsOrVehicles));
                    float scanRangeMeters = Math.Max(10f, Math.Min(100f, cfg.alprScanRangeMeters));
                    float readRangeMeters = Math.Max(2f, Math.Min(15f, cfg.alprReadRangeMeters));
                    float coneAngleDeg = Math.Max(10f, Math.Min(60f, cfg.alprConeAngleDegrees));

                    // ALPR only works when: (1) enabled in settings, (2) on duty (Start/Stop in Main), (3) in police vehicle
                    if (Main.Player == null || !Main.Player.Exists()) {
                        CurrentHit = null;
                        GameFiber.Sleep(intervalMs);
                        continue;
                    }

                    Vehicle playerVehicle = Main.Player.CurrentVehicle;
                    bool inPoliceVehicle = playerVehicle != null && playerVehicle.Exists() && playerVehicle.IsPoliceVehicle;
                    if (!inPoliceVehicle) {
                        CurrentHit = null;
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

                    // Realistic ALPR: only read vehicles in front (cone) and within read range; process closest one per cycle
                    InConeAndRangeBuffer.Clear();
                    Vector3 cruiserPos = playerVehicle.Position; // playerVehicle is police vehicle (checked above)
                    Vector3 forward = playerVehicle.ForwardVector;
                    forward.Z = 0f;
                    if (forward.LengthSquared() < 0.0001f) forward = new Vector3(0f, 1f, 0f);
                    else forward = Vector3.Normalize(forward);

                    // Consider vehicles in cone up to scanRangeMeters; only process (read) the closest if within readRangeMeters
                    foreach (Vehicle v in nearby) {
                        if (v == null || !v.Exists()) continue;
                        if (v == playerVehicle) continue;
                        float dist = cruiserPos.DistanceTo(v.Position);
                        if (dist > scanRangeMeters) continue;
                        if (!IsInCone(cruiserPos, forward, v.Position, coneAngleDeg)) continue;
                        string plate = v.LicensePlate?.Trim();
                        if (string.IsNullOrEmpty(plate)) continue;
                        InConeAndRangeBuffer.Add((v, dist));
                    }

                    // Process only the single closest vehicle this cycle, and only if within read range (realistic ALPR read distance)
                    Vehicle toScan = GetClosestVehicleInBuffer(InConeAndRangeBuffer);
                    if (toScan != null) {
                        if (cruiserPos.DistanceTo(toScan.Position) > readRangeMeters) {
                            CurrentHit = null;
                        } else {
                        string plate = toScan.LicensePlate?.Trim();
                        if (string.IsNullOrEmpty(plate)) {
                            CurrentHit = null;
                        } else {
                        string plateKey = plate.ToUpperInvariant();
                        try {
                            MDTProVehicleData vd = new MDTProVehicleData(toScan);
                            if (vd.CDFVehicleData?.Owner != null) {
                                List<string> flags = BuildFlags(vd);
                                if (flags.Count > 0) {
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
                                        Owner = vd.Owner ?? "",
                                        ModelDisplayName = vd.ModelDisplayName ?? vd.ModelName ?? "",
                                        Flags = flags,
                                        TimeScanned = DateTime.Now
                                    };

                                    CurrentHit = hit;

                                    if (shouldAlert) {
                                        if (cfg.alprPlaySoundOnHit) {
                                            // RPH doesn't have built-in sound; could play a native or skip. Skip for now.
                                        }
                                        var hitCopy = hit;
                                        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                                            ServerAPI.WebSocketHandler.BroadcastALPRHit(hitCopy));
                                    }
                                } else {
                                    CurrentHit = null;
                                }
                            } else {
                                CurrentHit = null;
                            }
                        } catch (Exception ex) {
                            Log($"ALPR scan skip vehicle {plate}: {ex.Message}", false, LogSeverity.Warning);
                            CurrentHit = null;
                        }
                        }
                        }
                    } else {
                        CurrentHit = null;
                    }
                } catch (Exception ex) {
                    Log($"ALPR scan error: {ex.Message}", true, LogSeverity.Error);
                }

                loopCount++;
                GameFiber.Sleep(intervalMs);
            }
        }

        private static List<string> BuildFlags(MDTProVehicleData vd) {
            var flags = new List<string>();

            if (vd.IsStolen)
                flags.Add("Stolen");

            string reg = vd.RegistrationStatus ?? "";
            if (reg.Equals("Expired", StringComparison.OrdinalIgnoreCase))
                flags.Add("Registration expired");
            else if (reg.Equals("Suspended", StringComparison.OrdinalIgnoreCase) ||
                     reg.Equals("Revoked", StringComparison.OrdinalIgnoreCase) ||
                     reg.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                     string.IsNullOrWhiteSpace(reg))
                flags.Add("No registration");

            string ins = vd.InsuranceStatus ?? "";
            if (ins.Equals("Expired", StringComparison.OrdinalIgnoreCase))
                flags.Add("Insurance expired");
            else if (ins.Equals("Suspended", StringComparison.OrdinalIgnoreCase) ||
                     ins.Equals("Revoked", StringComparison.OrdinalIgnoreCase) ||
                     ins.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                     string.IsNullOrWhiteSpace(ins))
                flags.Add("No insurance");

            if (vd.CDFVehicleData?.Owner?.Wanted == true)
                flags.Add("Owner wanted");

            return flags;
        }

        /// <summary>Returns the closest vehicle from the buffer (by distance). No LINQ/allocations.</summary>
        private static Vehicle GetClosestVehicleInBuffer(List<(Vehicle vehicle, float distance)> buffer) {
            if (buffer == null || buffer.Count == 0) return null;
            Vehicle closest = buffer[0].vehicle;
            float minDist = buffer[0].distance;
            for (int i = 1; i < buffer.Count; i++) {
                if (buffer[i].distance < minDist) {
                    minDist = buffer[i].distance;
                    closest = buffer[i].vehicle;
                }
            }
            return closest;
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
    }
}
