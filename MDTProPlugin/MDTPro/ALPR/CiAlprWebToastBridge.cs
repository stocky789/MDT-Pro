using CalloutInterfaceAPI;
using CalloutInterfaceAPI.Records;
using MDTPro.Data;
using MDTPro.Setup;
using Rage;
using System;
using static MDTPro.Setup.SetupController;
using static MDTPro.Utility.Helper;

namespace MDTPro.ALPR {
    /// <summary>
    /// Subscribes to Callout Interface plate-check events and forwards qualifying hits to web clients as ALPR toasts (phase 1: primary web source).
    /// </summary>
    internal static class CiAlprWebToastBridge {
        private static bool _subscribed;
        private static readonly object Gate = new object();

        internal static void Start() {
            lock (Gate) {
                if (_subscribed) return;
                Events.OnPlateCheck += OnPlateCheck;
                _subscribed = true;
            }
            Log("Callout Interface ALPR web toast bridge subscribed.", false, LogSeverity.Info);
        }

        internal static void Stop() {
            lock (Gate) {
                if (!_subscribed) return;
                Events.OnPlateCheck -= OnPlateCheck;
                _subscribed = false;
            }
        }

        private static void OnPlateCheck(VehicleRecord record, string source) {
            try {
                var cfg = GetConfig();
                if (cfg == null || !cfg.alprWebToastsFromCalloutInterface) return;
                // OnPlateCheck is raised by Callout Interface; no need to gate on IsCalloutInterfaceAvailable (avoids rare API/version mismatches dropping events).

                ALPRHit hit = null;
                if (record?.Entity != null && record.Entity.Exists())
                    hit = ALPRController.BuildAlprHitForWebFromVehicle(record.Entity);
                if (hit == null)
                    hit = ALPRController.BuildAlprHitFromCalloutInterfaceVehicleRecord(record);
                if (hit == null) return;

                // Always apply CI plate-check registration/insurance/owner-wanted; entity-only build ignores VehicleRecord.*.
                ALPRController.MergeCalloutInterfacePlateCheckIntoHit(hit, record);

                ALPRController.TryEnqueueWebToastFromCalloutInterface(hit);
            } catch (Exception ex) {
                Log($"CI ALPR web toast: {ex.Message}", false, LogSeverity.Warning);
            }
        }
    }
}
