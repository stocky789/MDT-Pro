using System;

namespace MDTPro.Utility {
    /// <summary>
    /// Runs work from HTTP/WebSocket (thread-pool) threads on a single long-lived game fiber.
    /// Avoids spawning a new <see cref="GameFiber"/> per request (unreliable scheduling when GTA throttles or pauses script ticks)
    /// and keeps sensitive paths (e.g. arrest saves, lock ordering) serialized on that fiber.
    /// </summary>
    internal static class GameFiberHttpBridge {
        static readonly object FiberLifecycleLock = new object();
        static volatile bool FiberShouldRun;

        internal static void Start() {
            lock (FiberLifecycleLock) {
                FiberShouldRun = true;
                // Bridge items are processed on the combined <see cref="GameWorkScheduler"/> fiber.
            }
        }

        internal static void Stop() {
            lock (FiberLifecycleLock) {
                FiberShouldRun = false;
            }
        }

        internal static int PendingWorkCount {
            get { return GameWorkScheduler.PendingWorkCount; }
        }

        /// <summary>
        /// Enqueue <paramref name="action"/> on the bridge fiber and block until it runs or <paramref name="timeoutMilliseconds"/> elapses.
        /// </summary>
        /// <returns><c>false</c> on timeout or if the bridge stopped before the item ran.</returns>
        internal static bool TryExecuteBlocking(Action action, int timeoutMilliseconds, out Exception caughtException) {
            caughtException = null;
            if (action == null) return true;
            if (!FiberShouldRun) {
                caughtException = new OperationCanceledException("MDT Pro HTTP bridge stopped.");
                return false;
            }
            return GameWorkScheduler.TryExecuteBlocking(
                action,
                timeoutMilliseconds,
                out caughtException,
                "http-bridge",
                GameWorkJobTrigger.HttpBridge,
                GameWorkPriority.Interactive);
        }

        internal static bool TryExecuteBlocking(
            Action action,
            int timeoutMilliseconds,
            out Exception caughtException,
            string jobName,
            GameWorkJobTrigger trigger,
            GameWorkPriority priority = GameWorkPriority.Interactive) {
            caughtException = null;
            if (action == null) return true;
            if (!FiberShouldRun) {
                caughtException = new OperationCanceledException("MDT Pro HTTP bridge stopped.");
                return false;
            }
            return GameWorkScheduler.TryExecuteBlocking(action, timeoutMilliseconds, out caughtException, jobName, trigger, priority);
        }

        internal static void EnqueueFireAndForget(Action action) {
            if (action == null) return;
            if (!FiberShouldRun) return;
            GameWorkScheduler.Enqueue(action, "http-bridge-fire-and-forget", GameWorkJobTrigger.HttpBridge, GameWorkPriority.Interactive);
        }

        internal static void EnqueueFireAndForget(
            Action action,
            string jobName,
            GameWorkJobTrigger trigger,
            GameWorkPriority priority = GameWorkPriority.Interactive,
            string coalesceKey = null) {
            if (action == null || !FiberShouldRun) return;
            GameWorkScheduler.Enqueue(action, jobName, trigger, priority, coalesceKey);
        }
    }
}
