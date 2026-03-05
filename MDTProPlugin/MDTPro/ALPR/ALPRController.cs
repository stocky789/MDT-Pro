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
using static MDTPro.Main;

namespace MDTPro.ALPR {
    /// <summary>
    /// Coordinates ALPR scanning, flag detection, and broadcasting to HUD/WebSocket.
    /// </summary>
    internal static class ALPRController {
        private static GameFiber _scanFiber;
        private static readonly HashSet<string> AlertedPlates = new HashSet<string>();
        private static readonly Dictionary<string, DateTime> PlateCooldown = new Dictionary<string, DateTime>();
        private static readonly object Lock = new object();

        internal static ALPRHit CurrentHit { get; private set; }
        internal static bool IsRunning { get; private set; }

        private static readonly object StartLock = new object();

        internal static void Start() {
            lock (StartLock) {
                if (IsRunning) return;
                var cfg = GetConfig();
                if (!cfg.alprEnabled) return;
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
                    intervalMs = Math.Max(500, Math.Min(10000, cfg.alprScanIntervalMs));
                    int cooldownSec = Math.Max(10, Math.Min(300, cfg.alprCooldownSeconds));
                    int maxVehicles = Math.Max(5, cfg.maxNumberOfNearbyPedsOrVehicles);

                    if (Player == null || !Player.Exists()) {
                        GameFiber.Sleep(intervalMs);
                        continue;
                    }

                    Vehicle playerVehicle = Player.CurrentVehicle;
                    Vehicle[] nearby = Player.GetNearbyVehicles(maxVehicles);
                    if (nearby == null || nearby.Length == 0) {
                        GameFiber.Sleep(intervalMs);
                        continue;
                    }

                    if (loopCount > 0 && loopCount % 30 == 0)
                        PruneExpiredCooldowns(cooldownSec);

                    foreach (Vehicle v in nearby) {
                        if (v == null || !v.Exists()) continue;
                        if (playerVehicle != null && v == playerVehicle) continue;
                        string plate = v.LicensePlate?.Trim();
                        if (string.IsNullOrEmpty(plate)) continue;
                        string plateKey = plate.ToUpperInvariant();

                        lock (Lock) {
                            if (PlateCooldown.TryGetValue(plateKey, out DateTime until) && DateTime.UtcNow < until)
                                continue;
                        }

                        try {
                            MDTProVehicleData vd = new MDTProVehicleData(v);
                            if (vd.CDFVehicleData?.Owner == null) continue;

                            List<string> flags = BuildFlags(vd);
                            if (flags.Count == 0) continue;

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
                                if (cfg.alprShowInGameNotification) {
                                    string msg = $"{hit.Plate} – {string.Join(", ", flags)}";
                                    RageNotification.Show(msg, RageNotification.NotificationType.Info, "ALPR");
                                }
                                if (cfg.alprPlaySoundOnHit) {
                                    // RPH doesn't have built-in sound; could play a native or skip. Skip for now.
                                }
                                var hitCopy = hit;
                                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                                    ServerAPI.WebSocketHandler.BroadcastALPRHit(hitCopy));
                            }
                        } catch (Exception ex) {
                            Log($"ALPR scan skip vehicle {plate}: {ex.Message}", false, LogSeverity.Warning);
                        }
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
