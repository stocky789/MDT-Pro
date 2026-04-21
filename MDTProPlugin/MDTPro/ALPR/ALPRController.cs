using CommonDataFramework.Modules.VehicleDatabase;
using MDTPro.Data;
using MDTPro.ServerAPI;
using MDTPro.Setup;
using MDTPro.Utility;
using MDTPro.ALPR.Sensors;
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
    /// Owner driver-license alerts use CDF on the vehicle owner (same backing as Person Search), not LSPDFR Persona.
    /// Flagged reads (NO DATA, paperwork, stolen, etc.): at most one roll every <see cref="AlprDefaults.MinSecondsBetweenFlaggedPoolRollAttempts"/>s
    /// while flagged vehicles are in view; each roll uses <see cref="AlprDefaults.TerminalFlaggedReadShowChanceMin"/>–max (then at most one car). Same path for HUD and browser/native.
    /// </summary>
    internal static class ALPRController {
        private static GameFiber _scanFiber;
        private static readonly HashSet<string> AlertedPlates = new HashSet<string>();
        private static readonly Dictionary<string, DateTime> PlateCooldown = new Dictionary<string, DateTime>();
        /// <summary>Per-plate quiet window so paperwork-only hits do not re-bubble the same plate every tick.</summary>
        private static readonly Dictionary<string, DateTime> PaperworkPlateQuietUntilUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Lock = new object();
        /// <summary>Wall-clock hold for stolen/BOLO/wanted detail promotion.</summary>
        private const int DisplayedHitHoldSecondsSevere = 45;
        /// <summary>Longer hold for registration/insurance/DL hits so the operator can read the detail pane.</summary>
        private const int DisplayedHitHoldSecondsPaperwork = 60;
        private static DateTime _currentHitHoldUntilUtc = DateTime.MinValue;
        private static int _displayedHitHoldTotalSeconds = DisplayedHitHoldSecondsSevere;
        private static readonly AlprSessionState TerminalSession = new AlprSessionState();
        private static Blip _flagBlip;
        private static string _lastSensorFlashId = "";
        private static DateTime _lastSensorFlashUtc = DateTime.MinValue;
        private static int _scanLoopCount;
        private static DateTime _lastSeverePromotionUtc = DateTime.MinValue;
        private static DateTime _nextFlaggedPoolRollUtc = DateTime.MinValue;
        private static readonly Random AlertFlagRollRng = new Random();

        internal static ALPRHit CurrentHit { get; private set; }
        internal static bool IsRunning { get; private set; }
        /// <summary>Cached by the scan loop so the HUD can read it from RawFrameRender without calling natives.</summary>
        internal static bool IsInPoliceVehicleCached { get; private set; }

        internal static AlprSessionState TerminalState => TerminalSession;

        internal static IReadOnlyList<AlprTerminalRow> GetTerminalRowsSnapshot() {
            int mx = AlprDefaults.TerminalMaxRows;
            if (mx <= 0) mx = 7;
            return TerminalSession.GetRowsSnapshot(mx);
        }

        internal static bool IsTerminalSensorFlashActive(string sensorId) {
            if (string.IsNullOrEmpty(sensorId)) return false;
            return string.Equals(sensorId, _lastSensorFlashId, StringComparison.OrdinalIgnoreCase)
                && (DateTime.UtcNow - _lastSensorFlashUtc).TotalMilliseconds < 220;
        }

        private static readonly object StartLock = new object();

        /// <summary>Web ALPR toast dedupe window for the built-in scanner. Reads config each time so file edits apply without restart.</summary>
        private static int GetConfiguredWebToastPlateCooldownSeconds() {
            var cfg = GetConfig();
            int sec = cfg?.alprWebToastPlateCooldownSeconds ?? 0;
            if (sec <= 0) sec = 90;
            if (sec < 15) sec = 15;
            if (sec > 600) sec = 600;
            return sec;
        }

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
                PaperworkPlateQuietUntilUtc.Clear();
            }
            _nextFlaggedPoolRollUtc = DateTime.MinValue;

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
            _lastSeverePromotionUtc = DateTime.MinValue;
            _nextFlaggedPoolRollUtc = DateTime.MinValue;
            TerminalSession.Clear();
            TryDeleteFlagBlip();
            lock (Lock) {
                AlertedPlates.Clear();
                PlateCooldown.Clear();
                PaperworkPlateQuietUntilUtc.Clear();
            }
            Log("ALPR stopped", false, LogSeverity.Info);
        }

        internal static void Clear() {
            CurrentHit = null;
            _currentHitHoldUntilUtc = DateTime.MinValue;
            _lastSeverePromotionUtc = DateTime.MinValue;
            _nextFlaggedPoolRollUtc = DateTime.MinValue;
            TerminalSession.Clear();
            TryDeleteFlagBlip();
            lock (Lock) {
                AlertedPlates.Clear();
                PlateCooldown.Clear();
                PaperworkPlateQuietUntilUtc.Clear();
            }
        }

        private static int ClampAdvancedTickMs() {
            int t = AlprDefaults.AdvancedTickMs;
            if (t < 40) t = 40;
            if (t > 250) t = 250;
            return t;
        }

        private static void ScanLoop() {
            _scanLoopCount = 0;
            while (IsRunning) {
                Config cfg = null;
                try {
                    ExpireDisplayedHitIfNeeded();
                    cfg = GetConfig();
                    if (cfg == null || !cfg.alprEnabled) {
                        GameFiber.Sleep(1000);
                        continue;
                    }

                    int sleepMs = ClampAdvancedTickMs();

                    if (Main.Player == null || !Main.Player.Exists()) {
                        CurrentHit = null;
                        _currentHitHoldUntilUtc = DateTime.MinValue;
                        GameFiber.Sleep(sleepMs);
                        _scanLoopCount++;
                        continue;
                    }

                    Vehicle playerVehicle = Main.Player.CurrentVehicle;
                    bool inPoliceVehicle = playerVehicle != null && playerVehicle.Exists() && playerVehicle.IsPoliceVehicle;
                    IsInPoliceVehicleCached = inPoliceVehicle;
                    if (!inPoliceVehicle) {
                        CurrentHit = null;
                        _currentHitHoldUntilUtc = DateTime.MinValue;
                        TryDeleteFlagBlip();
                        GameFiber.Sleep(sleepMs);
                        _scanLoopCount++;
                        continue;
                    }

                    if (_scanLoopCount > 0 && _scanLoopCount % 30 == 0) {
                        PruneExpiredCooldowns();
                        TerminalSession.Prune(AlprDefaults.TerminalMaxRows, AlprDefaults.TerminalHistoryMinutes);
                    }

                    RunAdvancedScan(cfg, playerVehicle);
                } catch (Exception ex) {
                    Log($"ALPR scan error: {ex.Message}", false, LogSeverity.Error);
                }

                _scanLoopCount++;
                GameFiber.Sleep(ClampAdvancedTickMs());
            }
        }

        private static void RunAdvancedScan(Config cfg, Vehicle playerVehicle) {
            int maxV = Math.Min(AlprDefaults.MaxNearbyVehiclesScanCap, Math.Min(16, Math.Max(5, cfg.maxNumberOfNearbyPedsOrVehicles + 2)));
            List<PlateReadResult> reads = PlateSensorController.EvaluateTick(playerVehicle, maxV, 1f);
            if (reads == null || reads.Count == 0) return;

            int cooldownSec = GetConfiguredWebToastPlateCooldownSeconds();
            var flaggedCandidates = new List<(PlateReadResult read, ALPRHit hit, string plateKey)>();

            foreach (PlateReadResult read in reads) {
                Vehicle v = read.Target;
                if (v == null || !v.Exists()) continue;
                string plate = v.LicensePlate?.Trim();
                if (string.IsNullOrEmpty(plate)) continue;
                string plateKey = plate.ToUpperInvariant();
                try {
                    ALPRHit hit = BuildAlprHitForWebFromVehicle(v);
                    if (hit == null) continue;
                    bool onlyNoData = AlprHitBuilder.HasOnlyNotInDatabaseFlags(hit);
                    bool interesting = AlprHitBuilder.HasInterestingFlagsExcludingNotInDatabase(hit);
                    if (!onlyNoData && !interesting) {
                        CommitAlprReadToTerminalAndAlerts(read, hit, plateKey, cooldownSec);
                        continue;
                    }

                    if (AlprHitBuilder.IsPaperworkOnlyHit(hit)) {
                        var nowQuiet = DateTime.UtcNow;
                        lock (Lock) {
                            if (PaperworkPlateQuietUntilUtc.TryGetValue(plateKey, out DateTime until) && nowQuiet < until)
                                continue;
                        }
                    }

                    flaggedCandidates.Add((read, hit, plateKey));
                } catch (Exception ex) {
                    Log($"ALPR advanced skip {plate}: {ex.Message}", false, LogSeverity.Warning);
                }
            }

            if (flaggedCandidates.Count == 0)
                return;
            var rollUtc = DateTime.UtcNow;
            if (rollUtc < _nextFlaggedPoolRollUtc)
                return;
            _nextFlaggedPoolRollUtc = rollUtc.AddSeconds(Math.Max(1, AlprDefaults.MinSecondsBetweenFlaggedPoolRollAttempts));
            if (!RollBandPasses(AlprDefaults.TerminalFlaggedReadShowChanceMin, AlprDefaults.TerminalFlaggedReadShowChanceMax))
                return;

            int pick = AlertFlagRollRng.Next(flaggedCandidates.Count);
            (PlateReadResult read, ALPRHit hit, string plateKey) chosen = flaggedCandidates[pick];
            if (AlprHitBuilder.IsPaperworkOnlyHit(chosen.hit)) {
                lock (Lock) {
                    PaperworkPlateQuietUntilUtc[chosen.plateKey] = DateTime.UtcNow.AddSeconds(AlprDefaults.PaperworkPlateReshowCooldownSeconds);
                }
            }
            CommitAlprReadToTerminalAndAlerts(chosen.read, chosen.hit, chosen.plateKey, cooldownSec);
        }

        /// <summary>Writes one read to the terminal, detail hold, and severe web/sound path (after any random gate).</summary>
        private static void CommitAlprReadToTerminalAndAlerts(PlateReadResult read, ALPRHit hit, string plateKey, int cooldownSec) {
            Vehicle v = read.Target;
            if (v == null || !v.Exists()) return;

            string compact = AlprHitBuilder.BuildCompactSummary(hit);
            bool severe = AlprHitBuilder.HasSevereAlertForPromotion(hit);
            bool paperworkHold = AlprHitBuilder.HasPaperworkOrLicenseAlert(hit);
            TerminalSession.RecordRead(plateKey, hit, read.Sensor.Id, compact);
            _lastSensorFlashId = read.Sensor.Id;
            _lastSensorFlashUtc = DateTime.UtcNow;

            if (severe || paperworkHold) {
                int holdSec = severe ? DisplayedHitHoldSecondsSevere : DisplayedHitHoldSecondsPaperwork;
                TryPromoteOrRefreshDisplayedHit(hit, holdSec, severe);
            }

            if (!severe)
                return;

            bool plateFresh;
            lock (Lock) {
                plateFresh = !AlertedPlates.Contains(plateKey);
            }
            if (!plateFresh)
                return;

            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - _lastSeverePromotionUtc).TotalSeconds < AlprDefaults.MinSecondsBetweenSeverePromotions)
                return;

            lock (Lock) {
                if (AlertedPlates.Contains(plateKey))
                    return;
                AlertedPlates.Add(plateKey);
                PlateCooldown[plateKey] = nowUtc.AddSeconds(cooldownSec);
            }

            _lastSeverePromotionUtc = nowUtc;

            if (AlprDefaults.PlaySoundOnFlaggedHit)
                PlayAlprHitSound();
            TryEnqueueWebToastFromScanner(hit);
            if (AlprDefaults.BlipOnFlaggedHit)
                TryAttachFlagBlip(v);
        }

        /// <summary>Random gate: draw threshold in [min,max], pass if second draw is below it (expected rate ≈ (min+max)/2).</summary>
        private static bool RollBandPasses(double min, double max) {
            if (max <= min)
                return AlertFlagRollRng.NextDouble() < min;
            double threshold = min + AlertFlagRollRng.NextDouble() * (max - min);
            return AlertFlagRollRng.NextDouble() < threshold;
        }

        private static void TryAttachFlagBlip(Vehicle v) {
            try {
                TryDeleteFlagBlip();
                if (v == null || !v.Exists()) return;
                _flagBlip = v.AttachBlip();
                _flagBlip.Sprite = BlipSprite.PointOfInterest;
                try { _flagBlip.Color = System.Drawing.Color.Red; } catch { /* RPH blip color API differences */ }
            } catch { /* ignore */ }
        }

        private static void TryDeleteFlagBlip() {
            try {
                if (_flagBlip != null && _flagBlip.Exists()) {
                    _flagBlip.Delete();
                    _flagBlip = null;
                }
            } catch {
                _flagBlip = null;
            }
        }

        /// <summary>Builds an <see cref="ALPRHit"/> from a live vehicle using the same CDF/MDT flag rules as the scan loop.</summary>
        internal static ALPRHit BuildAlprHitForWebFromVehicle(Vehicle toScan) {
            return AlprHitBuilder.BuildFromVehicle(toScan);
        }

        /// <summary>Queues a web/native MDT ALPR toast from the built-in scanner (same WebSocket path as the browser plugin).</summary>
        internal static void TryEnqueueWebToastFromScanner(ALPRHit hit) {
            if (hit == null || !AlprHitBuilder.HasSevereAlertForPromotion(hit)) return;
            var copy = hit;
            System.Threading.ThreadPool.QueueUserWorkItem(_ => WebSocketHandler.BroadcastALPRHit(copy));
        }

        private static void PlayAlprHitSound() {
            try {
                Rage.Native.NativeFunction.Natives.PLAY_SOUND_FRONTEND(-1, "SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET", false);
            } catch {
                // Ignore if native fails (e.g. different RPH version)
            }
        }

        private static void PruneExpiredCooldowns() {
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
                var quietDone = PaperworkPlateQuietUntilUtc
                    .Where(kv => now >= kv.Value)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (string key in quietDone)
                    PaperworkPlateQuietUntilUtc.Remove(key);
            }
        }

        private static bool IsDisplayedHitHeld() {
            return CurrentHit != null && DateTime.UtcNow < _currentHitHoldUntilUtc;
        }

        /// <summary>True while the detail pane should show a live hold countdown (HUD uses this instead of guessing from list rows).</summary>
        internal static bool IsDetailHoldVisible() => IsDisplayedHitHeld();

        static string NormalizePlateKey(string plate) {
            return string.IsNullOrWhiteSpace(plate) ? "" : plate.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// Updates <see cref="CurrentHit"/> without flicker: same plate refreshes data and extends hold; different plate waits for hold to end
        /// unless the candidate is severe and the current detail is only paperwork.
        /// </summary>
        private static void TryPromoteOrRefreshDisplayedHit(ALPRHit hit, int holdTotalSeconds, bool candidateIsSevere) {
            if (hit == null) return;
            int hold = Math.Max(5, holdTotalSeconds);
            string nextKey = NormalizePlateKey(hit.Plate);
            if (string.IsNullOrEmpty(nextKey)) return;

            if (CurrentHit != null && IsDisplayedHitHeld()) {
                string curKey = NormalizePlateKey(CurrentHit.Plate);
                if (!string.IsNullOrEmpty(curKey) && curKey == nextKey) {
                    CurrentHit = hit;
                    _displayedHitHoldTotalSeconds = Math.Max(_displayedHitHoldTotalSeconds, hold);
                    _currentHitHoldUntilUtc = DateTime.UtcNow.AddSeconds(_displayedHitHoldTotalSeconds);
                    return;
                }
                if (candidateIsSevere && !AlprHitBuilder.HasSevereAlertForPromotion(CurrentHit)) {
                    SetDisplayedHit(hit, hold);
                }
                return;
            }

            SetDisplayedHit(hit, hold);
        }

        private static void SetDisplayedHit(ALPRHit hit, int holdTotalSeconds) {
            CurrentHit = hit;
            _displayedHitHoldTotalSeconds = Math.Max(5, holdTotalSeconds);
            _currentHitHoldUntilUtc = DateTime.UtcNow.AddSeconds(_displayedHitHoldTotalSeconds);
        }

        internal static int GetDisplayedHitHoldTotalSeconds() {
            return Math.Max(1, _displayedHitHoldTotalSeconds);
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
            return Math.Max(0, (int)Math.Round(remaining.TotalSeconds));
        }

        /// <summary>0–1 fraction of hold time left (smooth bar; independent of integer seconds label).</summary>
        internal static float GetDisplayedHitHoldRemainingFraction() {
            if (CurrentHit == null) return 0f;
            var rem = _currentHitHoldUntilUtc - DateTime.UtcNow;
            if (rem.TotalSeconds <= 0) return 0f;
            int tot = Math.Max(1, _displayedHitHoldTotalSeconds);
            return (float)Math.Min(1.0, Math.Max(0.0, rem.TotalSeconds / tot));
        }
    }
}
