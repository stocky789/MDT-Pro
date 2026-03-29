using MDTPro.Setup;
using Rage;
using System;
using System.Reflection;

namespace MDTPro.Utility {
    /// <summary>Runtime integration profile: Policing Redefined vs StopThePed vs Ultimate Backup. Initialized on duty after plugins are discoverable.</summary>
    internal static class ModIntegration {
        /// <summary>Policing Redefined assembly is in the LSPDFR plugin list.</summary>
        internal static bool PrPluginLoaded { get; private set; }
        /// <summary>StopThePed assembly is in the LSPDFR plugin list.</summary>
        internal static bool StpPluginLoaded { get; private set; }

        internal static bool SubscribedPolicingRedefinedStopEvents { get; private set; }
        internal static bool SubscribedStopThePedStopEvents { get; private set; }

        /// <summary>PR SearchItemsAPI (or equivalent type name) resolvable — used for ped/vehicle search item capture.</summary>
        internal static bool HasPolicingRedefinedSearchItemsApi { get; private set; }

        /// <summary>Policing Redefined BackupAPI type resolvable.</summary>
        internal static bool HasPolicingRedefinedBackupApi { get; private set; }

        /// <summary>UltimateBackup.API.Functions type resolvable.</summary>
        internal static bool HasUltimateBackupApi { get; private set; }

        /// <summary>Active backup provider id: PolicingRedefined, UltimateBackup, or empty.</summary>
        internal static string ActiveBackupProviderId { get; private set; } = "";

        internal static void SetActiveBackupProvider(string id) {
            ActiveBackupProviderId = id ?? "";
        }

        internal static string ConfiguredStopEvents { get; private set; } = "Auto";
        internal static string ConfiguredBackupProvider { get; private set; } = "Auto";

        internal static void ResetForTests() {
            PrPluginLoaded = StpPluginLoaded = false;
            SubscribedPolicingRedefinedStopEvents = SubscribedStopThePedStopEvents = false;
            HasPolicingRedefinedSearchItemsApi = HasPolicingRedefinedBackupApi = HasUltimateBackupApi = false;
            ActiveBackupProviderId = "";
        }

        internal static void InitializeOnDuty(bool prInProcess, bool stpInProcess) {
            PrPluginLoaded = prInProcess;
            StpPluginLoaded = stpInProcess;
            var cfg = SetupController.GetConfig();
            ConfiguredStopEvents = NormalizeChoice(cfg.integrationStopEvents, "Auto", new[] { "Auto", "PolicingRedefined", "StopThePed" });
            ConfiguredBackupProvider = NormalizeConfiguredBackupProvider(cfg.integrationBackupProvider);

            HasPolicingRedefinedBackupApi = prInProcess && FindTypeInLoadedAssemblies("PolicingRedefined.API.BackupAPI") != null;
            HasUltimateBackupApi = FindTypeInLoadedAssemblies("UltimateBackup.API.Functions") != null;

            HasPolicingRedefinedSearchItemsApi = false;
            if (prInProcess) {
                foreach (var typeName in new[] {
                    "PolicingRedefined.API.SearchItemsAPI",
                    "PolicingRedefined.API.SearchItemAPI",
                    "PolicingRedefined.Interaction.Assets.SearchItemsAPI"
                }) {
                    Type t = FindTypeInLoadedAssemblies(typeName);
                    if (t == null) continue;
                    if (t.GetMethod("GetPedSearchItems", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Ped) }, null) != null
                        || t.GetMethod("GetVehicleSearchItems", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Rage.Vehicle) }, null) != null) {
                        HasPolicingRedefinedSearchItemsApi = true;
                        break;
                    }
                }
            }

            SubscribedPolicingRedefinedStopEvents = false;
            SubscribedStopThePedStopEvents = false;

            bool wantPrStops = ConfiguredStopEvents.Equals("PolicingRedefined", StringComparison.OrdinalIgnoreCase)
                || (ConfiguredStopEvents.Equals("Auto", StringComparison.OrdinalIgnoreCase) && prInProcess);
            bool wantStpStops = ConfiguredStopEvents.Equals("StopThePed", StringComparison.OrdinalIgnoreCase)
                || (ConfiguredStopEvents.Equals("Auto", StringComparison.OrdinalIgnoreCase) && !prInProcess && stpInProcess);

            if (wantPrStops && prInProcess)
                SubscribedPolicingRedefinedStopEvents = true;
            else if (wantStpStops && stpInProcess)
                SubscribedStopThePedStopEvents = true;

            Game.LogTrivial($"[MDT Pro] Integration: stopEvents={ConfiguredStopEvents} → PR stops={(SubscribedPolicingRedefinedStopEvents ? "yes" : "no")}, STP stops={(SubscribedStopThePedStopEvents ? "yes" : "no")}; PR search API={(HasPolicingRedefinedSearchItemsApi ? "yes" : "no")}; backup config={ConfiguredBackupProvider}");
        }

        /// <summary>Same rules as <see cref="ConfiguredBackupProvider"/> — unknown/typo values become Auto.</summary>
        internal static string NormalizeConfiguredBackupProvider(string value) {
            return NormalizeChoice(value, "Auto", new[] { "Auto", "PolicingRedefined", "UltimateBackup" });
        }

        private static string NormalizeChoice(string value, string defaultVal, string[] allowed) {
            if (string.IsNullOrWhiteSpace(value)) return defaultVal;
            string v = value.Trim();
            foreach (var a in allowed) {
                if (v.Equals(a, StringComparison.OrdinalIgnoreCase)) return a;
            }
            return defaultVal;
        }

        internal static Type FindTypeInLoadedAssemblies(string fullName) {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm.IsDynamic) continue;
                try {
                    var t = asm.GetType(fullName);
                    if (t != null) return t;
                } catch { /* ignore */ }
            }
            return null;
        }

    }
}
