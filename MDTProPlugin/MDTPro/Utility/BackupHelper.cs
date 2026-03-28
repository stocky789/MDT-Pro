// Quick Actions / HTTP backup: routes to Policing Redefined or Ultimate Backup (see BackupService + ModIntegration).
using Rage;
using System;
using System.Reflection;
using MDTPro.Utility.Backup;

namespace MDTPro.Utility {
    internal static class BackupHelper {
        internal static bool IsAvailable => BackupService.Dispatcher.IsAvailable;

        private static bool Invoke(Func<bool> action) => BackupService.InvokeOnGameFiber(action);

        internal static bool RequestPanicBackup() {
            return Invoke(() => BackupService.Dispatcher.RequestPanicBackup());
        }

        internal static bool RequestLocalPatrolBackup(int responseCode = 2) {
            return RequestBackup("LocalPatrol", responseCode);
        }

        internal static bool RequestTrafficStopBackup(string unitName = "LocalPatrol", int responseCode = 2) {
            return Invoke(() => BackupService.Dispatcher.RequestTrafficStopBackup(unitName, responseCode));
        }

        internal static bool RequestPoliceTransport(int responseCode = 2) {
            return Invoke(() => BackupService.Dispatcher.RequestPoliceTransport(responseCode));
        }

        internal static bool RequestTowServiceBackup() {
            return Invoke(() => BackupService.Dispatcher.RequestTowServiceBackup());
        }

        internal static bool RequestBackup(string unitName, int responseCode = 2) {
            return Invoke(() => BackupService.Dispatcher.RequestBackup(unitName, responseCode));
        }

        internal static bool RequestGroupBackup() {
            return Invoke(() => BackupService.Dispatcher.RequestGroupBackup());
        }

        internal static bool RequestAirBackup(string unitName = "LocalAir") {
            return Invoke(() => BackupService.Dispatcher.RequestAirBackup(unitName));
        }

        internal static bool RequestSpikeStripsBackup() {
            return Invoke(() => BackupService.Dispatcher.RequestSpikeStripsBackup());
        }

        internal static bool InitiateFelonyStop() {
            return Invoke(() => BackupService.Dispatcher.InitiateFelonyStop());
        }

        internal static void DismissAllBackupUnits(bool force = false) {
            BackupService.DismissOnGameFiber(force);
        }

        /// <summary>True if player is on a Policing Redefined on-foot traffic stop. Not used by Ultimate Backup.</summary>
        internal static bool IsOnFootTrafficStop() {
            if (!ModIntegration.HasPolicingRedefinedBackupApi) return false;
            try {
                Type onFootApiType = ModIntegration.FindTypeInLoadedAssemblies("PolicingRedefined.API.OnFootTrafficStopAPI");
                if (onFootApiType == null) return false;
                var method = onFootApiType.GetMethod("IsOnAnyFootTrafficStop", BindingFlags.Public | BindingFlags.Static);
                if (method == null) return false;
                return method.Invoke(null, null) is bool b && b;
            } catch {
                return false;
            }
        }
    }
}
