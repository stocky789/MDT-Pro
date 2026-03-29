using CommonDataFramework.Modules.PedDatabase;
using CommonDataFramework.Modules.VehicleDatabase;
using MDTPro.Data;
using Rage;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MDTPro.EventListeners {
    internal static class CDFEvents {
        private static bool subscribed;
        private static readonly List<(EventInfo Event, Delegate Handler)> _cdfRegistrations = new List<(EventInfo, Delegate)>();

        internal static void Subscribe() {
            if (subscribed) return;
            try {
                var eventsApiType = Type.GetType("CommonDataFramework.API.EventsAPI, CommonDataFramework")
                    ?? Type.GetType("CommonDataFramework.Modules.EventsAPI, CommonDataFramework");
                if (eventsApiType == null) return;

                var pedMethod = typeof(DataController).GetMethod("OnCDFPedDataRemoved", BindingFlags.Public | BindingFlags.Static);
                var vehicleMethod = typeof(DataController).GetMethod("OnCDFVehicleDataRemoved", BindingFlags.Public | BindingFlags.Static);
                if (pedMethod == null || vehicleMethod == null) return;

                bool pedSubscribed = false;
                var onPedRemoved = eventsApiType.GetEvent("OnPedDataRemoved", BindingFlags.Public | BindingFlags.Static);
                if (onPedRemoved != null) {
                    try {
                        var handler = Delegate.CreateDelegate(onPedRemoved.EventHandlerType, pedMethod);
                        onPedRemoved.AddEventHandler(null, handler);
                        _cdfRegistrations.Add((onPedRemoved, handler));
                        pedSubscribed = true;
                    } catch (Exception ex) {
                        Game.LogTrivial($"MDT Pro: [Warning] OnPedDataRemoved subscription: {ex.Message}");
                    }
                }

                bool vehicleSubscribed = false;
                var onVehicleRemoved = eventsApiType.GetEvent("OnVehicleDataRemoved", BindingFlags.Public | BindingFlags.Static);
                if (onVehicleRemoved != null) {
                    try {
                        var handler = Delegate.CreateDelegate(onVehicleRemoved.EventHandlerType, vehicleMethod);
                        onVehicleRemoved.AddEventHandler(null, handler);
                        _cdfRegistrations.Add((onVehicleRemoved, handler));
                        vehicleSubscribed = true;
                    } catch (Exception ex) {
                        Game.LogTrivial($"MDT Pro: [Warning] OnVehicleDataRemoved subscription: {ex.Message}");
                    }
                }

                // Only mark subscribed when all available events were successfully subscribed
                bool pedExpected = onPedRemoved != null;
                bool vehicleExpected = onVehicleRemoved != null;
                if ((!pedExpected || pedSubscribed) && (!vehicleExpected || vehicleSubscribed)) {
                    subscribed = true;
                }
            } catch (Exception e) {
                Game.LogTrivial($"MDT Pro: [Warning] Failed to subscribe to CDF events: {e.Message}");
            }
        }

        internal static void UnsubscribeAll() {
            for (int i = _cdfRegistrations.Count - 1; i >= 0; i--) {
                var (evt, handler) = _cdfRegistrations[i];
                try {
                    evt?.RemoveEventHandler(null, handler);
                } catch {
                    /* ignore */
                }
            }
            _cdfRegistrations.Clear();
            subscribed = false;
        }
    }
}
