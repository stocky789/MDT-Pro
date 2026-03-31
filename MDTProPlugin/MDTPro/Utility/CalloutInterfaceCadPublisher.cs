using System;
using System.Reflection;

namespace MDTPro.Utility {
    /// <summary>
    /// Publishes CAD unit status to Callout Interface when that API exposes <c>PublishCadUnitStatus</c>.
    /// Uses reflection so an older <c>CalloutInterfaceAPI.dll</c> (without the method) does not break every HTTP POST
    /// via <see cref="ServerAPI.PostAPIResponse"/> JIT (MissingMethodException on ctor).
    /// </summary>
    internal static class CalloutInterfaceCadPublisher {
        static readonly object LockObj = new object();
        static Action<string> _publish;
        static bool _inited;
        static bool _loggedMissing;

        internal static void TryPublishCadUnitStatus(string statusText) {
            if (string.IsNullOrEmpty(statusText)) return;
            var line = statusText.Trim();
            // HTTP / listener threads must not call into Rage scheduling directly — marshal onto the shared game-fiber bridge.
            if (!GameFiberHttpBridge.TryExecuteBlocking(() => {
                try {
                    var del = GetPublish();
                    del?.Invoke(line);
                } catch {
                    /* CI API or game state */
                }
            }, 10_000, out _))
                Helper.Log("MDT Pro: CAD status publish timed out waiting for game fiber.", false, Helper.LogSeverity.Warning);
        }

        static Action<string> GetPublish() {
            if (_inited) return _publish;
            lock (LockObj) {
                if (_inited) return _publish;
                _publish = BuildPublish();
                _inited = true;
            }
            return _publish;
        }

        static Action<string> BuildPublish() {
            try {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                    if (!string.Equals(asm.GetName().Name, "CalloutInterfaceAPI", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var t = asm.GetType("CalloutInterfaceAPI.Functions");
                    if (t == null) continue;
                    var m = t.GetMethod("PublishCadUnitStatus", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                    if (m == null) continue;
                    return s => {
                        try {
                            m.Invoke(null, new object[] { s });
                        } catch {
                            /* ignore */
                        }
                    };
                }
            } catch {
                /* ignore */
            }

            if (!_loggedMissing) {
                _loggedMissing = true;
                Helper.Log("MDT Pro: CalloutInterfaceAPI has no PublishCadUnitStatus(string). Update CalloutInterfaceAPI.dll with your Callout Interface build if you need CAD status sync to CI.", false, Helper.LogSeverity.Warning);
            }
            return null;
        }
    }
}
