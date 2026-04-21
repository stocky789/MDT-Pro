using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Rage;

namespace MDTPro.Utility {
    /// <summary>
    /// Runs work from HTTP/WebSocket (thread-pool) threads on a single long-lived game fiber.
    /// Avoids spawning a new <see cref="GameFiber"/> per request (unreliable scheduling when GTA throttles or pauses script ticks)
    /// and keeps sensitive paths (e.g. arrest saves, lock ordering) serialized on that fiber.
    /// </summary>
    internal static class GameFiberHttpBridge {
        static readonly ConcurrentQueue<Action> FireAndForget = new ConcurrentQueue<Action>();
        static readonly ConcurrentQueue<BlockingWorkItem> Blocking = new ConcurrentQueue<BlockingWorkItem>();

        static readonly object FiberLifecycleLock = new object();
        static volatile bool FiberShouldRun;
        static bool FiberIsRunning;

        sealed class BlockingWorkItem {
            internal readonly Action Action;
            internal readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            internal Exception Caught;

            internal BlockingWorkItem(Action action) => Action = action;
        }

        internal static void Start() {
            lock (FiberLifecycleLock) {
                FiberShouldRun = true;
                if (FiberIsRunning)
                    return;
                FiberIsRunning = true;
                GameFiber.StartNew(ProcessLoop, "mdtpro-http-gamefiber-bridge");
            }
        }

        internal static void Stop() {
            lock (FiberLifecycleLock) {
                FiberShouldRun = false;
            }
        }

        static void FailAllPendingBlocking() {
            while (Blocking.TryDequeue(out var bi)) {
                bi.Caught = new OperationCanceledException("MDT Pro HTTP bridge stopped.");
                try { bi.Done.Set(); } catch { /* ignore */ }
            }
            while (FireAndForget.TryDequeue(out _)) { }
        }

        static void ProcessLoop() {
            try {
                while (FiberShouldRun) {
                    if (FireAndForget.TryDequeue(out var quick)) {
                        bool perf = Helper.IsPerformanceDiagnosticLoggingEnabled();
                        var sw = perf ? Stopwatch.StartNew() : null;
                        try { quick(); } catch (Exception ex) {
                            Helper.Log($"GameFiberHttpBridge fire-and-forget: {ex.Message}", false, Helper.LogSeverity.Warning);
                        }
                        if (perf && sw != null) {
                            long ms = sw.ElapsedMilliseconds;
                            if (ms >= 3)
                                Helper.Log($"[Perf] GameFiberHttpBridge fire-and-forget took {ms}ms", false, Helper.LogSeverity.Info);
                        }
                    } else if (Blocking.TryDequeue(out var bi)) {
                        bool perf = Helper.IsPerformanceDiagnosticLoggingEnabled();
                        var sw = perf ? Stopwatch.StartNew() : null;
                        try {
                            bi.Action();
                        } catch (Exception ex) {
                            bi.Caught = ex;
                        } finally {
                            try { bi.Done.Set(); } catch { /* ignore */ }
                        }
                        if (perf && sw != null) {
                            long ms = sw.ElapsedMilliseconds;
                            if (ms >= 2)
                                Helper.Log($"[Perf] GameFiberHttpBridge blocking work took {ms}ms", false, Helper.LogSeverity.Info);
                        }
                        // Never drain an arbitrary backlog in one script tick — yields keep the game
                        // fiber from hitch-spiking when many HTTP/WebSocket paths enqueue blocking work.
                        GameFiber.Yield();
                    } else {
                        GameFiber.Yield();
                    }
                }
            } finally {
                lock (FiberLifecycleLock) {
                    FiberIsRunning = false;
                }
                FailAllPendingBlocking();
            }
        }

        /// <summary>
        /// Enqueue <paramref name="action"/> on the bridge fiber and block until it runs or <paramref name="timeoutMilliseconds"/> elapses.
        /// </summary>
        /// <returns><c>false</c> on timeout or if the bridge stopped before the item ran.</returns>
        internal static bool TryExecuteBlocking(Action action, int timeoutMilliseconds, out Exception caughtException) {
            caughtException = null;
            if (action == null)
                return true;
            var item = new BlockingWorkItem(action);
            Blocking.Enqueue(item);
            bool signaled = item.Done.Wait(timeoutMilliseconds);
            if (!signaled)
                return false;
            caughtException = item.Caught;
            return true;
        }

        internal static void EnqueueFireAndForget(Action action) {
            if (action == null) return;
            FireAndForget.Enqueue(action);
        }
    }
}
