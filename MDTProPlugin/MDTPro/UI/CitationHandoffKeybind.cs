// Keybind to open the StopThePed-path citation handoff menu when a citation was closed in the MDT (see StpCitationHandoffQueue).
using System.Windows.Forms;
using MDTPro.Utility;
using Rage;
using static MDTPro.Utility.Helper;

namespace MDTPro.UI {
    internal static class CitationHandoffKeybind {
        /// <summary>Key to open a pending citation handoff (MDTPro.ini [MDTPro] CitationHandoffKey).</summary>
        /// <summary>Default F10. Change in MDTPro.ini if you use LSPDFR Enhanced (its menu often uses F10) or another mod on F10.</summary>
        public static Keys HandoffKey { get; set; } = Keys.F10;

        private static GameFiber _fiber;
        private static bool _stopRequested;
        private static bool _keyWasDown;

        internal static string CurrentKeyLabel => HandoffKey.ToString();

        internal static void Start() {
            if (_fiber != null && _fiber.IsAlive) return;
            if (_fiber != null) {
                while (_fiber.IsAlive) GameFiber.Yield();
            }
            _stopRequested = false;
            _keyWasDown = Game.IsKeyDownRightNow(HandoffKey);
            _fiber = GameFiber.StartNew(Loop);
        }

        internal static void Stop() {
            _stopRequested = true;
            _fiber?.Abort();
        }

        private static void Loop() {
            // Do not gate on Server.RunServer: the listener thread sets RunServer true only after Thread.Sleep(120),
            // so this fiber can start first and exit immediately — F10 would never work. Handoff is in-game only.
            while (!_stopRequested) {
                try {
                    bool down = Game.IsKeyDownRightNow(HandoffKey);
                    if (down && !_keyWasDown)
                        StpCitationHandoffQueue.TryProcessKeyPress();
                    _keyWasDown = down;
                } catch (System.Exception ex) {
                    Log($"CitationHandoffKeybind: {ex.Message}", true, LogSeverity.Warning);
                }
                GameFiber.Yield();
            }
        }
    }
}
