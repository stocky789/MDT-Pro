using MDTPro.Cloud;
using MDTPro.Utility;
using Rage;
using System;

namespace MDTPro.UI.InGameComputer {
    internal static class InGameComputerEntry {
        private static readonly object SessionLock = new object();
        private static volatile bool _sessionRunning;
        private static InGameComputerSession _activeSession;

        internal static bool IsSessionRunning {
            get {
                lock (SessionLock) return _sessionRunning;
            }
        }

        internal static void RequestAbortFromHost() {
            try {
                _activeSession?.AbortFromHost();
            } catch { /* ignore */ }
        }

        internal static void Start() {
            lock (SessionLock) {
                if (_sessionRunning) {
                    RageNotification.Show("The in-game MDT is already open.", RageNotification.NotificationType.Info);
                    return;
                }
                if (CloudIngameEntry.IsSessionRunning) {
                    RageNotification.Show("Close MDT Cloud sign-in before opening the in-game MDT.", RageNotification.NotificationType.Info);
                    return;
                }
                if (!CloudIngameEntry.IsLemonUiDllPresent()) {
                    RageNotification.ShowError("LemonUI.RagePluginHook.dll missing beside MDTPro.dll or in GTA V folder.");
                    return;
                }
                _sessionRunning = true;
            }

            try {
                SettingsMenu.CloseAllMenusForCloudOverlay();
                GameFiber.StartNew(() => {
                    var session = new InGameComputerSession();
                    try {
                        lock (SessionLock) { _activeSession = session; }
                        session.Run();
                    } catch (Exception ex) {
                        try {
                            Game.DisplayNotification("CHAR_BLOCKED", "CHAR_BLOCKED", "MDT Pro", "In-game MDT", ex.Message ?? "Unknown error");
                        } catch { /* ignore */ }
                        Helper.Log($"In-game MDT session: {ex.Message}", true, Helper.LogSeverity.Warning);
                    } finally {
                        lock (SessionLock) {
                            _activeSession = null;
                            _sessionRunning = false;
                        }
                    }
                }, "MDTPro.InGameComputer");
            } catch {
                lock (SessionLock) { _sessionRunning = false; }
                throw;
            }
        }
    }
}
