using System;
using MDTPro.Utility;

namespace MDTPro.Cloud {
    /// <summary>Blocks MDT Cloud HTTP traffic when StopThePed is present in the LSPDFR plugin list (STP currently sends unreliable data to the cloud).</summary>
    internal static class CloudStopThePedBlock {
        const string ConnectionDetail = "StopThePed not supported for MDT Cloud";
        static DateTime _lastNotifyUtc = DateTime.MinValue;
        static readonly TimeSpan NotifyCooldown = TimeSpan.FromSeconds(120);

        internal static bool IsStopThePedPluginPresent() {
            try {
                return DependencyCheck.IsStopThePedAvailable();
            } catch {
                return false;
            }
        }

        internal static bool ShouldBlockMdtCloudApiTraffic() =>
            CloudMode.IsEnabled() && IsStopThePedPluginPresent();

        internal static string BlockedConnectionDetail() => ConnectionDetail;

        internal static void NotifyBlockedIfNeeded() {
            if (!ShouldBlockMdtCloudApiTraffic()) return;
            DateTime now = DateTime.UtcNow;
            if (now - _lastNotifyUtc < NotifyCooldown) return;
            _lastNotifyUtc = now;
            RageNotification.ShowError(
                "We detected your game running StopThePed. For protection of our databases, MDT Cloud access to the server has been blocked.");
        }
    }
}
