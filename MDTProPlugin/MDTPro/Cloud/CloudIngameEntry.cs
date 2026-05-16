using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Rage;
using MDTPro.UI;
using MDTPro.Utility;

namespace MDTPro.Cloud {
    /// <summary>Starts the in-game MDT Cloud LemonUI session (pattern from MDT Pro LiteEntry).</summary>
    internal static class CloudIngameEntry {
        private static readonly object SessionLock = new object();
        private static volatile bool _sessionRunning;
        private static CloudLoginSession _activeSession;

        internal static bool IsSessionRunning {
            get {
                lock (SessionLock) return _sessionRunning;
            }
        }

        internal static bool IsLemonUiDllPresent() {
            const string dll = "LemonUI.RagePluginHook.dll";
            try {
                // If the dependency is already loaded (e.g. another copy was resolved by the CLR), treat as present
                // even when a strict file probe beside MDTPro fails (unusual layouts / partial deploys).
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                    try {
                        if (string.Equals(asm.GetName().Name, "LemonUI.RagePluginHook", StringComparison.OrdinalIgnoreCase))
                            return true;
                    } catch {
                        /* ignore */
                    }
                }
                foreach (string dir in EnumerateLemonUiSearchDirectories()) {
                    try {
                        if (File.Exists(Path.Combine(dir, dll)))
                            return true;
                    } catch {
                        /* ignore */
                    }
                }
                return false;
            } catch {
                return false;
            }
        }

        /// <summary>Roots checked for LemonUI.RagePluginHook.dll (RPH may load plugins from nested folders).</summary>
        private static IEnumerable<string> EnumerateLemonUiSearchDirectories() {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            void add(string path) {
                if (string.IsNullOrWhiteSpace(path)) return;
                try {
                    string full = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (seen.Add(full)) list.Add(full);
                } catch {
                    /* ignore */
                }
            }
            try {
                string root = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(root)) {
                    add(root);
                    add(Path.Combine(root, "Plugins"));
                }
            } catch {
                /* ignore */
            }
            try {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                for (int i = 0; i < 10 && !string.IsNullOrEmpty(pluginDir); i++) {
                    add(pluginDir);
                    try {
                        pluginDir = Directory.GetParent(pluginDir)?.FullName;
                    } catch {
                        break;
                    }
                }
            } catch {
                /* ignore */
            }
            return list;
        }

        /// <summary>Abort cloud login UI from host unload or off-duty (must not leave LemonUI running).</summary>
        internal static void RequestAbortFromHost() {
            try {
                _activeSession?.AbortFromHost();
            } catch { /* ignore */ }
        }

        internal static void Start() {
            lock (SessionLock) {
                if (_sessionRunning) {
                    try {
                        Game.DisplayNotification("CHAR_BLOCKED", "CHAR_BLOCKED", "MDT Cloud", "Already open", "Exit from the MDT Cloud menu first.");
                    } catch { /* ignore */ }
                    return;
                }
                if (!IsLemonUiDllPresent()) {
                    RageNotification.ShowError("MDT Cloud: LemonUI.RagePluginHook.dll not found beside MDTPro.dll or in your GTA V folder. Add the NuGet DLL from the build output.");
                    return;
                }
                _sessionRunning = true;
            }

            try {
                SettingsMenu.CloseAllMenusForCloudOverlay();
                GameFiber.StartNew(() => {
                    var session = new CloudLoginSession();
                    try {
                        lock (SessionLock) { _activeSession = session; }
                        session.Run();
                    } catch (Exception ex) {
                        try {
                            Game.DisplayNotification("CHAR_BLOCKED", "CHAR_BLOCKED", "MDT Cloud", "Error", ex.Message ?? "Unknown error");
                        } catch { /* ignore */ }
                        Helper.Log($"MDT Cloud login session: {ex.Message}", true, Helper.LogSeverity.Warning);
                    } finally {
                        lock (SessionLock) {
                            _activeSession = null;
                            _sessionRunning = false;
                        }
                    }
                }, "MDTPro.CloudLogin");
            } catch {
                lock (SessionLock) { _sessionRunning = false; }
                throw;
            }
        }
    }
}
