using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using MDTPro.Data;
using MDTPro.Setup;
using Rage;

namespace MDTPro.Utility {
    internal enum GameWorkJobTrigger {
        Passive,
        UserRequest,
        Event,
        CloudCommand,
        HttpBridge
    }

    internal enum GameWorkPriority {
        Critical = 0,
        Interactive = 1,
        Background = 2
    }

    /// <summary>
    /// Single game fiber for HTTP bridge draining plus database, WebSocket/dynamic, and firearm cadence.
    /// Replaces separate data / dynamic / firearm fibers to avoid overlapping heavy native passes in one frame cluster.
    /// </summary>
    internal static class GameWorkScheduler {
        static readonly object LifecycleLock = new object();
        static readonly object QueueLock = new object();
        static readonly Queue<ScheduledWorkItem> CriticalQueue = new Queue<ScheduledWorkItem>();
        static readonly Queue<ScheduledWorkItem> InteractiveQueue = new Queue<ScheduledWorkItem>();
        static readonly Queue<ScheduledWorkItem> BackgroundQueue = new Queue<ScheduledWorkItem>();
        static readonly Dictionary<string, ScheduledWorkItem> Coalesced = new Dictionary<string, ScheduledWorkItem>(StringComparer.OrdinalIgnoreCase);
        static volatile bool ShouldRun;
        static volatile bool AcceptingWork;
        static GameFiber WorkerFiber;
        static ScheduledWorkItem CurrentItem;
        static DateTime LastQueueDepthLogUtc = DateTime.MinValue;

        /// <summary>Background items older than this when picked up are dropped instead of run; protects against backlog under sustained game stalls.</summary>
        const int MaxBackgroundJobAgeMs = 10000;
        /// <summary>Slice the long idle wait into chunks so newly enqueued Critical/Interactive jobs are picked up within this many ms.</summary>
        const int IdleWakeSliceMs = 25;

        sealed class ScheduledWorkItem {
            internal readonly string JobName;
            internal readonly GameWorkJobTrigger Trigger;
            internal readonly GameWorkPriority Priority;
            internal readonly string CoalesceKey;
            internal readonly DateTime EnqueuedUtc = DateTime.UtcNow;
            internal readonly ManualResetEventSlim Done;
            internal readonly bool Blocking;
            internal Action Action;
            internal Exception Caught;
            internal bool Cancelled;

            internal ScheduledWorkItem(string jobName, GameWorkJobTrigger trigger, GameWorkPriority priority, Action action, bool blocking, string coalesceKey) {
                JobName = string.IsNullOrWhiteSpace(jobName) ? "game-work" : jobName.Trim();
                Trigger = trigger;
                Priority = priority;
                Action = action;
                Blocking = blocking;
                CoalesceKey = string.IsNullOrWhiteSpace(coalesceKey) ? null : coalesceKey.Trim();
                Done = blocking ? new ManualResetEventSlim(false) : null;
            }
        }

        internal static void Start() {
            lock (LifecycleLock) {
                ShouldRun = true;
                AcceptingWork = true;
                if (WorkerFiber != null && WorkerFiber.IsAlive)
                    return;
                WorkerFiber = GameFiber.StartNew(WorkerLoop, "mdtpro-game-work");
            }
        }

        internal static void Stop() {
            lock (LifecycleLock) {
                AcceptingWork = false;
                ShouldRun = false;
            }
            Helper.Log("[Lifecycle] GameWorkScheduler stopping", false, Helper.LogSeverity.Info);
            FailQueuedWork(new OperationCanceledException("MDT Pro game-work scheduler stopped."));
            SignalCurrentForShutdown();
            try {
                if (WorkerFiber != null && WorkerFiber.IsAlive && GameFiber.CanSleepNow) {
                    var waitStopwatch = Stopwatch.StartNew();
                    while (WorkerFiber.IsAlive && waitStopwatch.ElapsedMilliseconds < 500) {
                        GameFiber.Wait(10);
                    }
                }
            } catch {
                /* ignore */
            }
            try {
                if (WorkerFiber != null && WorkerFiber.IsAlive)
                    WorkerFiber.Abort();
            } catch {
                /* ignore */
            }
            WorkerFiber = null;
            CurrentItem = null;
            Helper.Log("[Lifecycle] GameWorkScheduler stopped", false, Helper.LogSeverity.Info);
        }

        internal static bool Enqueue(
            Action work,
            string jobName,
            GameWorkJobTrigger trigger,
            GameWorkPriority priority,
            string coalesceKey = null) {
            if (work == null) return true;
            var item = new ScheduledWorkItem(jobName, trigger, priority, work, blocking: false, coalesceKey: coalesceKey);
            return EnqueueItem(item);
        }

        internal static bool TryExecuteBlocking(
            Action work,
            int timeoutMilliseconds,
            out Exception caughtException,
            string jobName = "blocking-work",
            GameWorkJobTrigger trigger = GameWorkJobTrigger.HttpBridge,
            GameWorkPriority priority = GameWorkPriority.Interactive) {
            caughtException = null;
            if (work == null) return true;
            if (GameFiber.CanSleepNow) {
                bool perf = Helper.IsPerformanceDiagnosticLoggingEnabled();
                var sw = perf ? Stopwatch.StartNew() : null;
                try {
                    work();
                } catch (Exception ex) {
                    caughtException = ex;
                } finally {
                    GameThreadHeartbeat.RecordTick();
                }
                if (perf && sw != null) {
                    long ms = sw.ElapsedMilliseconds;
                    if (ms >= 2)
                        LogSlowJob(jobName, ms, trigger);
                }
                return caughtException == null;
            }
            if (timeoutMilliseconds == Timeout.Infinite || timeoutMilliseconds <= 0)
                timeoutMilliseconds = 15000;
            var item = new ScheduledWorkItem(jobName, trigger, priority, work, blocking: true, coalesceKey: null);
            if (!EnqueueItem(item)) {
                caughtException = new OperationCanceledException("MDT Pro game-work scheduler is not running.");
                try { item.Done?.Dispose(); } catch { }
                return false;
            }
            try {
                bool signaled = item.Done.Wait(timeoutMilliseconds);
                if (!signaled) {
                    item.Cancelled = true;
                    caughtException = new TimeoutException("Game thread did not run in time.");
                    return false;
                }
                caughtException = item.Caught;
                return caughtException == null || !(caughtException is OperationCanceledException);
            } finally {
                // Caller has finished waiting; safe to dispose. The worker swallows ObjectDisposedException
                // from any late Set() in its finally block.
                try { item.Done?.Dispose(); } catch { }
            }
        }

        internal static int PendingWorkCount {
            get {
                lock (QueueLock) return CriticalQueue.Count + InteractiveQueue.Count + BackgroundQueue.Count + (CurrentItem != null ? 1 : 0);
            }
        }

        /// <summary>True when there is queued work that should preempt the idle wait between cadenced passes (Critical or Interactive only — Background is allowed to wait for the next cadence).</summary>
        static bool HasUrgentQueuedWork {
            get {
                lock (QueueLock) return CriticalQueue.Count > 0 || InteractiveQueue.Count > 0;
            }
        }

        internal static string QueueDepthSummary {
            get {
                lock (QueueLock) {
                    return $"critical {CriticalQueue.Count}, interactive {InteractiveQueue.Count}, background {BackgroundQueue.Count}";
                }
            }
        }

        static bool EnqueueItem(ScheduledWorkItem item) {
            if (item == null) return true;
            lock (QueueLock) {
                if (!AcceptingWork) {
                    item.Caught = new OperationCanceledException("MDT Pro game-work scheduler is stopped.");
                    item.Done?.Set();
                    return false;
                }

                if (!string.IsNullOrEmpty(item.CoalesceKey)) {
                    if (Coalesced.TryGetValue(item.CoalesceKey, out var existing)) {
                        existing.Action = item.Action;
                        existing.Cancelled = false;
                        return true;
                    }
                    Coalesced[item.CoalesceKey] = item;
                }

                QueueForPriority(item.Priority).Enqueue(item);
                return true;
            }
        }

        static Queue<ScheduledWorkItem> QueueForPriority(GameWorkPriority priority) {
            switch (priority) {
                case GameWorkPriority.Critical: return CriticalQueue;
                case GameWorkPriority.Background: return BackgroundQueue;
                default: return InteractiveQueue;
            }
        }

        static bool TryDequeueItem(out ScheduledWorkItem item) {
            lock (QueueLock) {
                if (CriticalQueue.Count > 0) item = CriticalQueue.Dequeue();
                else if (InteractiveQueue.Count > 0) item = InteractiveQueue.Dequeue();
                else if (BackgroundQueue.Count > 0) item = BackgroundQueue.Dequeue();
                else {
                    item = null;
                    return false;
                }
                if (!string.IsNullOrEmpty(item.CoalesceKey))
                    Coalesced.Remove(item.CoalesceKey);
                return true;
            }
        }

        static void FailQueuedWork(Exception ex) {
            lock (QueueLock) {
                FailQueue(CriticalQueue, ex);
                FailQueue(InteractiveQueue, ex);
                FailQueue(BackgroundQueue, ex);
                Coalesced.Clear();
            }
        }

        static void FailQueue(Queue<ScheduledWorkItem> queue, Exception ex) {
            while (queue.Count > 0) {
                var item = queue.Dequeue();
                item.Cancelled = true;
                item.Caught = ex;
                try { item.Done?.Set(); } catch { }
            }
        }

        static void SignalCurrentForShutdown() {
            var item = CurrentItem;
            if (item == null || !item.Blocking) return;
            item.Cancelled = true;
            item.Caught = new OperationCanceledException("MDT Pro game-work scheduler stopped while work was running.");
            try { item.Done?.Set(); } catch { }
        }

        internal static void RunInstrumented(string jobName, GameWorkJobTrigger trigger, Action work) {
            if (work == null) return;
            bool perf = Helper.IsPerformanceDiagnosticLoggingEnabled();
            var sw = perf ? Stopwatch.StartNew() : null;
            try {
                work();
            } catch (Exception ex) {
                Helper.Log($"GameWorkScheduler job {jobName}: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
            if (perf && sw != null) {
                long ms = sw.ElapsedMilliseconds;
                if (ms >= 4)
                    LogSlowJob(jobName, ms, trigger);
            }
            GameThreadHeartbeat.RecordTick();
        }

        static void RunQueuedItem(ScheduledWorkItem item) {
            if (item == null) return;
            if (item.Cancelled) {
                try { item.Done?.Set(); } catch { }
                return;
            }
            // Background jobs that sat in the queue too long (e.g. while the game was paused or hitching) are
            // skipped instead of run so the backlog cannot stack up after a long stall.
            if (item.Priority == GameWorkPriority.Background && !item.Blocking) {
                long ageMs = (long)(DateTime.UtcNow - item.EnqueuedUtc).TotalMilliseconds;
                if (ageMs > MaxBackgroundJobAgeMs) {
                    item.Cancelled = true;
                    if (Helper.IsPerformanceDiagnosticLoggingEnabled())
                        Helper.Log($"[Perf] gameWork dropped stale background job={item.JobName} ageMs={ageMs}", false, Helper.LogSeverity.Info);
                    try { item.Done?.Set(); } catch { }
                    return;
                }
            }
            CurrentItem = item;
            bool perf = Helper.IsPerformanceDiagnosticLoggingEnabled();
            var sw = perf ? Stopwatch.StartNew() : null;
            try {
                item.Action();
            } catch (Exception ex) {
                item.Caught = ex;
                Helper.Log($"GameWorkScheduler queued job {item.JobName}: {ex.Message}", false, Helper.LogSeverity.Warning);
            } finally {
                if (perf && sw != null) {
                    long ms = sw.ElapsedMilliseconds;
                    if (ms >= 2)
                        LogSlowJob(item.JobName, ms, item.Trigger);
                }
                try { item.Done?.Set(); } catch { }
                CurrentItem = null;
                GameThreadHeartbeat.RecordTick();
            }
        }

        static void LogSlowJob(string jobName, long ms, GameWorkJobTrigger trigger) {
            try {
                int q = PendingWorkCount;
                string depths = QueueDepthSummary;
                string entities = DataController.GetGameWorkDiagnosticsSummary();
                Helper.Log(
                    $"[Perf] gameWork job={jobName} ms={ms} trigger={trigger} queue={q} ({depths}) {entities}",
                    false,
                    Helper.LogSeverity.Info);
            } catch {
                /* ignore */
            }
        }

        static void WorkerLoop() {
            // Wait for server startup, but remain interruptible by Stop() so reload/unload cannot get stuck here.
            var startupWait = Stopwatch.StartNew();
            while (!MDTPro.Server.RunServer && ShouldRun && startupWait.ElapsedMilliseconds < 60000) {
                if (!SafeWait(50))
                    return;
            }
            if (!MDTPro.Server.RunServer) {
                AcceptingWork = false;
                FailQueuedWork(new OperationCanceledException("MDT Pro server did not start."));
                return;
            }

            var swDb = Stopwatch.StartNew();
            var swDyn = Stopwatch.StartNew();
            var swFire = Stopwatch.StartNew();

            while (MDTPro.Server.RunServer && ShouldRun) {
                try {
                    GameThreadHeartbeat.RecordTick();
                    Config cfg = SetupController.GetConfig();
                    int queuedBudgetMs = cfg.gameWorkBridgeBudgetMsPerTick;
                    if (queuedBudgetMs < 1) queuedBudgetMs = 1;
                    if (queuedBudgetMs > 25) queuedBudgetMs = 25;

                    var slice = Stopwatch.StartNew();
                    while (slice.ElapsedMilliseconds < queuedBudgetMs && MDTPro.Server.RunServer && ShouldRun) {
                        if (!TryDequeueItem(out var item))
                            break;
                        RunQueuedItem(item);
                        GameFiber.Yield();
                        GameThreadHeartbeat.RecordTick();
                    }
                    LogQueueDepthIfNeeded();

                    int dbInterval = cfg.databaseUpdateInterval;
                    if (dbInterval < 500) dbInterval = 500;
                    if (swDb.ElapsedMilliseconds >= dbInterval) {
                        swDb.Restart();
                        RunInstrumented("SetDatabases", GameWorkJobTrigger.Passive, DataController.SetDatabases);
                        GameFiber.Yield();
                        RunInstrumented("CheckAndResolvePendingCases", GameWorkJobTrigger.Passive, DataController.CheckAndResolvePendingCases);
                        GameFiber.Yield();
                        RunInstrumented("TryCaptureVehicleSearches", GameWorkJobTrigger.Passive, DataController.TryCaptureVehicleSearches);
                    }

                    int dynInterval = cfg.webSocketUpdateInterval;
                    if (dynInterval < 250) dynInterval = 250;
                    if (swDyn.ElapsedMilliseconds >= dynInterval) {
                        swDyn.Restart();
                        RunInstrumented("SetDynamicData", GameWorkJobTrigger.Passive, DataController.SetDynamicData);
                    }

                    int fireWait = cfg.firearmPlayerHeldScanIntervalMs;
                    if (fireWait < 250) fireWait = 250;
                    if (fireWait > 30000) fireWait = 30000;
                    if (swFire.ElapsedMilliseconds >= fireWait) {
                        swFire.Restart();
                        RunInstrumented("TryCapturePickupAndPlayerFirearms", GameWorkJobTrigger.Passive, DataController.TryCapturePickupAndPlayerFirearms);
                    }

                    long msDb = Math.Max(0, dbInterval - swDb.ElapsedMilliseconds);
                    long msDyn = Math.Max(0, dynInterval - swDyn.ElapsedMilliseconds);
                    long msFire = Math.Max(0, fireWait - swFire.ElapsedMilliseconds);
                    long wait = msDb;
                    if (msDyn < wait) wait = msDyn;
                    if (msFire < wait) wait = msFire;
                    if (wait < 10) wait = 10;
                    if (wait > 500) wait = 500;
                    // Slice the idle wait so a Critical/Interactive job enqueued during this window is picked up
                    // within ~IdleWakeSliceMs instead of waiting the full cadence interval.
                    long remaining = wait;
                    while (remaining > 0 && MDTPro.Server.RunServer && ShouldRun) {
                        int chunk = remaining > IdleWakeSliceMs ? IdleWakeSliceMs : (int)remaining;
                        if (!SafeWait(chunk))
                            return;
                        remaining -= chunk;
                        if (HasUrgentQueuedWork) break;
                    }
                } catch (Exception ex) {
                    Helper.Log($"GameWorkScheduler loop: {ex.Message}", false, Helper.LogSeverity.Warning);
                    if (!ShouldRun) return;
                    if (!SafeWait(250))
                        return;
                }
            }
        }

        static bool SafeWait(int ms) {
            try {
                GameFiber.Wait(ms);
                GameThreadHeartbeat.RecordTick();
                return true;
            } catch (InvalidOperationException) {
                // Can happen during teardown/reload when this fiber is no longer active.
                return false;
            }
        }

        static void LogQueueDepthIfNeeded() {
            try {
                int pending = PendingWorkCount;
                if (pending <= 0 || !Helper.IsPerformanceDiagnosticLoggingEnabled()) return;
                DateTime now = DateTime.UtcNow;
                if ((now - LastQueueDepthLogUtc).TotalSeconds < 5d) return;
                LastQueueDepthLogUtc = now;
                Helper.Log($"[Perf] gameWork pending={pending} ({QueueDepthSummary})", false, Helper.LogSeverity.Info);
            } catch {
                /* ignore diagnostics */
            }
        }
    }
}
