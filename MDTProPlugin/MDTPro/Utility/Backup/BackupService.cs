using MDTPro.Setup;
using MDTPro.Utility;
using Rage;
using System;
using System.Runtime.ExceptionServices;

namespace MDTPro.Utility.Backup {
    internal static class BackupService {
        private static IBackupDispatcher _dispatcher;

        internal static IBackupDispatcher Dispatcher => _dispatcher ?? NullBackupDispatcher.Instance;

        internal static void ReloadFromConfig() {
            var cfg = SetupController.GetConfig();
            string choice = ModIntegration.NormalizeConfiguredBackupProvider(cfg.integrationBackupProvider);
            ModIntegration.SetActiveBackupProvider("");

            IBackupDispatcher pick = NullBackupDispatcher.Instance;

            bool wantPr = choice.Equals("PolicingRedefined", StringComparison.OrdinalIgnoreCase)
                || choice.Equals("Auto", StringComparison.OrdinalIgnoreCase);
            bool wantUb = choice.Equals("UltimateBackup", StringComparison.OrdinalIgnoreCase)
                || choice.Equals("Auto", StringComparison.OrdinalIgnoreCase);

            if (choice.Equals("PolicingRedefined", StringComparison.OrdinalIgnoreCase)) {
                var pr = new PolicingRedefinedBackupDispatcher();
                if (pr.IsAvailable) pick = pr;
            } else if (choice.Equals("UltimateBackup", StringComparison.OrdinalIgnoreCase)) {
                var ub = new UltimateBackupBackupDispatcher();
                if (ub.IsAvailable) pick = ub;
            } else {
                var pr = new PolicingRedefinedBackupDispatcher();
                if (wantPr && pr.IsAvailable) pick = pr;
                else if (wantUb) {
                    var ub = new UltimateBackupBackupDispatcher();
                    if (ub.IsAvailable) pick = ub;
                }
            }

            _dispatcher = pick;
            if (pick is NullBackupDispatcher)
                ModIntegration.SetActiveBackupProvider("");
            else
                ModIntegration.SetActiveBackupProvider(pick.ProviderId);

            Game.LogTrivial($"[MDT Pro] Backup: {(_dispatcher is NullBackupDispatcher ? "none (install Policing Redefined or Ultimate Backup)" : ModIntegration.ActiveBackupProviderId)} [config={choice}]");
        }

        internal static bool InvokeOnGameFiber(Func<bool> action) {
            if (action == null || !Dispatcher.IsAvailable) return false;
            object resultLock = new object();
            bool result = false;
            Exception fiberException = null;
            if (!GameFiberHttpBridge.TryExecuteBlocking(() => {
                try {
                    bool r = action();
                    lock (resultLock) { result = r; }
                } catch (Exception ex) {
                    Game.LogTrivial($"[MDTPro] BackupService: {ex.Message}");
                    lock (resultLock) { fiberException = ex; }
                }
            }, 5000, out var bridgeEx)) {
                return false;
            }
            lock (resultLock) {
                if (fiberException != null)
                    ExceptionDispatchInfo.Capture(fiberException).Throw();
                if (bridgeEx != null)
                    ExceptionDispatchInfo.Capture(bridgeEx).Throw();
                return result;
            }
        }

        internal static void DismissOnGameFiber(bool force) {
            if (!Dispatcher.IsAvailable) return;
            GameFiber.StartNew(() => {
                try {
                    Dispatcher.DismissAllBackupUnits(force);
                } catch (Exception ex) {
                    Game.LogTrivial($"[MDTPro] BackupService.Dismiss: {ex.Message}");
                }
            });
        }
    }
}
