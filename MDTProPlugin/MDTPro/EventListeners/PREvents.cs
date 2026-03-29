using MDTPro.Data;
using MDTPro.Setup;
using MDTPro.Utility;
using Rage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MDTPro.EventListeners {
    internal static class PREvents {
        private static bool subscribed;
        private static readonly string[] eventNames = {
            "OnPedStopped",
            "OnPedPatDown",
            "OnIdentificationGiven",
            "OnDriverIdentificationGiven",
            "OnOccupantIdentificationGiven",
            "OnRequestPedCheck",
            "OnPedRanThroughDispatch",
            "OnPedArrested",
            "OnPedReleased",
            "OnVehicleStopped",
            "OnPedSurrendered",
            "OnDeadPedSearched",
            "OnRequestVehicleCheck",
            "OnVehicleRanThroughDispatch",
            "OnPedAskedToExitVehicle",
            "OnDriverAskedToTurnOffEngine"
        };

        private static readonly HashSet<string> trafficStopEventNames = new HashSet<string> {
            "OnPedAskedToExitVehicle",
            "OnDriverAskedToTurnOffEngine"
        };

        private static readonly Dictionary<string, string> identificationEventTypes = new Dictionary<string, string> {
            { "OnIdentificationGiven",         "State ID" },
            { "OnDriverIdentificationGiven",   "Driver's License" },
            { "OnOccupantIdentificationGiven", "Occupant ID" }
        };

        private static readonly HashSet<string> vehicleDispatchEventNames = new HashSet<string> {
            "OnRequestVehicleCheck",
            "OnVehicleRanThroughDispatch"
        };

        /// <summary>Optional PR events for vehicle search — capture contraband when vehicle is actually searched (not just when plate is run).</summary>
        private static readonly HashSet<string> vehicleSearchEventNames = new HashSet<string>(
            new[] { "OnVehicleSearched", "OnVehicleSearchComplete", "OnVehicleSearchFinished" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly List<(EventInfo Event, Delegate Handler)> _prHandlerRegistrations = new List<(EventInfo, Delegate)>();

        private static void RegisterPrHandler(EventInfo eventInfo, Delegate handler) {
            eventInfo.AddEventHandler(null, handler);
            _prHandlerRegistrations.Add((eventInfo, handler));
        }

        /// <summary>Unregister PR static handlers so RPH plugin reload does not leave stale delegates into this assembly.</summary>
        internal static void UnsubscribeAll() {
            for (int i = _prHandlerRegistrations.Count - 1; i >= 0; i--) {
                var (evt, handler) = _prHandlerRegistrations[i];
                try {
                    evt?.RemoveEventHandler(null, handler);
                } catch {
                    /* ignore */
                }
            }
            _prHandlerRegistrations.Clear();
            subscribed = false;
        }

        internal static void SubscribeToPREvents() {
            if (subscribed) return;

            try {
                Type eventsApiType = Type.GetType("PolicingRedefined.API.EventsAPI, PolicingRedefined");
                if (eventsApiType == null) return;

                foreach (string eventName in eventNames) {
                    EventInfo eventInfo = eventsApiType.GetEvent(eventName, BindingFlags.Public | BindingFlags.Static);
                    if (eventInfo == null) continue;

                    Delegate handler = CreateForwardingDelegate(eventInfo.EventHandlerType, eventName);
                    RegisterPrHandler(eventInfo, handler);
                }

                SubscribeToOnFootTrafficStopStarted();
                SubscribeToOnFootTrafficStopEnded();
                SubscribeToOptionalWeaponFirearmEvents(eventsApiType);
                SubscribeToOptionalVehicleSearchEvents(eventsApiType);
                subscribed = true;
            } catch (Exception e) {
                Game.LogTrivial($"MDT Pro: [Warning] Failed to subscribe to PR events: {e.Message}");
            }
        }

        private static void SubscribeToOnFootTrafficStopStarted() {
            try {
                Type apiType = Type.GetType("PolicingRedefined.API.OnFootTrafficStopAPI, PolicingRedefined");
                if (apiType == null) return;
                EventInfo eventInfo = apiType.GetEvent("OnFootTrafficStopStarted", BindingFlags.Public | BindingFlags.Static);
                if (eventInfo == null) return;

                Delegate handler = CreateForwardingDelegate(eventInfo.EventHandlerType, "OnFootTrafficStopStarted");
                RegisterPrHandler(eventInfo, handler);
            } catch (Exception e) {
                Game.LogTrivial($"MDT Pro: [Warning] Failed to subscribe to PR OnFootTrafficStopStarted: {e.Message}");
            }
        }

        /// <summary>Tries to subscribe to PR vehicle search events if they exist. Capture runs when vehicle is actually searched, not just when plate is run.</summary>
        private static void SubscribeToOptionalVehicleSearchEvents(Type eventsApiType) {
            if (eventsApiType == null) return;
            try {
                foreach (string name in vehicleSearchEventNames) {
                    EventInfo evt = eventsApiType.GetEvent(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                    if (evt == null) continue;
                    try {
                        Delegate handler = CreateForwardingDelegate(evt.EventHandlerType, name);
                        RegisterPrHandler(evt, handler);
                        Game.LogTrivial($"[MDTPro] Subscribed to PR event: {name} (vehicle search contraband)");
                        return; // One handler is enough
                    } catch (Exception ex) {
                        Game.LogTrivial($"[MDTPro] Could not subscribe to {name}: {ex.Message}");
                    }
                }
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] Optional vehicle search event subscription failed: {ex.Message}");
            }
        }

        /// <summary>Tries to subscribe to PR weapon/firearm check events if they exist (e.g. OnRequestWeaponCheck, OnWeaponRanThroughDispatch).</summary>
        private static void SubscribeToOptionalWeaponFirearmEvents(Type eventsApiType) {
            if (eventsApiType == null) return;
            try {
                string[] candidates = { "OnRequestWeaponCheck", "OnWeaponRanThroughDispatch", "OnFirearmCheckComplete", "OnWeaponCheckComplete", "OnRequestFirearmCheck", "OnFirearmRanThroughDispatch" };
                foreach (string name in candidates) {
                    EventInfo evt = eventsApiType.GetEvent(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                    if (evt == null) continue;
                    try {
                        Delegate handler = CreateForwardingDelegate(evt.EventHandlerType, name);
                        RegisterPrHandler(evt, handler);
                        Game.LogTrivial($"[MDTPro] Subscribed to PR event: {name}");
                    } catch (Exception ex) {
                        Game.LogTrivial($"[MDTPro] Could not subscribe to {name}: {ex.Message}");
                    }
                }
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] Optional weapon event subscription failed: {ex.Message}");
            }
        }

        private static void SubscribeToOnFootTrafficStopEnded() {
            try {
                Type apiType = Type.GetType("PolicingRedefined.API.OnFootTrafficStopAPI, PolicingRedefined");
                if (apiType == null) return;
                EventInfo eventInfo = apiType.GetEvent("OnFootTrafficStopEnded", BindingFlags.Public | BindingFlags.Static);
                if (eventInfo == null) return;

                Delegate handler = CreateForwardingDelegate(eventInfo.EventHandlerType, "OnFootTrafficStopEnded");
                RegisterPrHandler(eventInfo, handler);
            } catch (Exception e) {
                Game.LogTrivial($"MDT Pro: [Warning] Failed to subscribe to PR OnFootTrafficStopEnded: {e.Message}");
            }
        }

        private static Delegate CreateForwardingDelegate(Type delegateType, string eventName) {
            MethodInfo invokeMethod = delegateType.GetMethod("Invoke");
            ParameterInfo[] parameters = invokeMethod.GetParameters();
            ParameterExpression[] parameterExpressions = parameters
                .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
                .ToArray();

            NewArrayExpression argsArrayExpression = Expression.NewArrayInit(
                typeof(object),
                parameterExpressions.Select(parameterExpression => Expression.Convert(parameterExpression, typeof(object))));

            MethodCallExpression body = Expression.Call(
                typeof(PREvents).GetMethod(nameof(HandlePREvent), BindingFlags.NonPublic | BindingFlags.Static),
                Expression.Constant(eventName),
                argsArrayExpression);

            return Expression.Lambda(delegateType, body, parameterExpressions).Compile();
        }

        private static void HandlePREvent(string eventName, object[] args) {
            if (args == null || args.Length == 0) return;

            if (eventName == "OnVehicleStopped" && args[0] is Vehicle vehicle) {
                DataController.ResolveVehicleAndDriverForStop(vehicle);
                return;
            }

            if (vehicleDispatchEventNames.Contains(eventName) && args[0] is Vehicle dispatchVehicle) {
                if (SetupController.GetConfig().firearmDebugLogging)
                    Helper.Log($"[Firearm] PR event fired: {eventName}, plate={dispatchVehicle?.LicensePlate ?? "—"}", false, Helper.LogSeverity.Info);
                DataController.ResolveVehicleAndDriverForStop(dispatchVehicle);
                try {
                    if (dispatchVehicle != null && dispatchVehicle.Exists()) {
                        DataController.CaptureVehicleSearchItems(dispatchVehicle);
                        if (eventName == "OnRequestVehicleCheck") DataController.TryCapturePickupAndPlayerFirearms();
                        // Delayed retry: player typically searches after running plate. Capture again in 8s.
                        GameFiber.StartNew(() => {
                            GameFiber.Wait(8000);
                            try {
                                if (dispatchVehicle != null && dispatchVehicle.Exists())
                                    DataController.CaptureVehicleSearchItems(dispatchVehicle);
                            } catch (Exception ex) {
                                Game.LogTrivial($"MDT Pro: [Warning] Delayed vehicle search capture: {ex.Message}");
                            }
                        }, "MDTPro-vehicle-search-delayed");
                    }
                } catch (Exception ex) {
                    Game.LogTrivial($"MDT Pro: [Warning] OnVehicleRanThroughDispatch capture: {ex.Message}");
                }
                return;
            }

            // Vehicle search events (if PR exposes OnVehicleSearched etc.)
            if (vehicleSearchEventNames.Contains(eventName) && args != null && args.Length >= 1) {
                Vehicle searchVehicle = null;
                foreach (object arg in args) {
                    if (arg is Vehicle v && v.Exists()) { searchVehicle = v; break; }
                }
                if (searchVehicle != null) {
                    try {
                        DataController.CaptureVehicleSearchItems(searchVehicle);
                    } catch (Exception ex) {
                        Game.LogTrivial($"MDT Pro: [Warning] {eventName} capture: {ex.Message}");
                    }
                }
                return;
            }

            // OnRequestPedCheck: player just requested dispatch to run this ped. Capture immediately (PR may have search items from prior pat-down). Also capture player-held weapon in case they're checking that.
            if (eventName == "OnRequestPedCheck" && args.Length >= 1 && args[0] is Ped requestPed) {
                if (SetupController.GetConfig().firearmDebugLogging)
                    Helper.Log($"[Firearm] PR event fired: OnRequestPedCheck, pedHandle={requestPed?.Handle ?? 0}", false, Helper.LogSeverity.Info);
                try {
                    if (requestPed != null && requestPed.IsValid())
                        DataController.CaptureFirearmsFromPed(requestPed, "Firearm check (request)");
                    DataController.TryCapturePickupAndPlayerFirearms();
                } catch (Exception ex) {
                    Game.LogTrivial($"MDT Pro: [Warning] OnRequestPedCheck capture: {ex.Message}");
                }
            }

            // OnPedRanThroughDispatch: when dispatch returns ped info (may include warrant/firearm results). Refresh wanted status from CDF so MDT Person Search shows warrants; capture firearms for Firearms Check.
            if (eventName == "OnPedRanThroughDispatch" && args.Length >= 1 && args[0] is Ped dispatchPed) {
                if (SetupController.GetConfig().firearmDebugLogging)
                    Helper.Log($"[Firearm] PR event fired: OnPedRanThroughDispatch, pedHandle={dispatchPed?.Handle ?? 0}", false, Helper.LogSeverity.Info);
                try {
                    if (dispatchPed != null && dispatchPed.IsValid()) {
                        DataController.RefreshPedWantedStatusFromCDF(dispatchPed);
                        DataController.CaptureFirearmsFromPed(dispatchPed, "Firearm check (dispatch)");
                    }
                } catch (Exception ex) {
                    Game.LogTrivial($"MDT Pro: [Warning] OnPedRanThroughDispatch capture: {ex.Message}");
                }
            }

            // Optional weapon/firearm check events: if PR fires these, capture so firearm shows in MDT.
            // PR may pass (Ped) when checking someone else's weapon, or no Ped when checking player's held weapon.
            if ((eventName.Contains("Weapon") || eventName.Contains("Firearm")) && args != null) {
                if (SetupController.GetConfig().firearmDebugLogging)
                    Helper.Log($"[Firearm] PR event fired: {eventName}", false, Helper.LogSeverity.Info);
                bool captured = false;
                foreach (object arg in args) {
                    if (arg is Ped wpnPed && wpnPed.IsValid()) {
                        try {
                            DataController.CaptureFirearmsFromPed(wpnPed, "Firearm check");
                            captured = true;
                            break;
                        } catch (Exception ex) {
                            Game.LogTrivial($"MDT Pro: [Warning] {eventName} firearm capture: {ex.Message}");
                        }
                    }
                }
                // When checking player's held weapon, PR often doesn't pass a Ped. Trigger immediate capture attempt.
                if (!captured) DataController.TryCapturePickupAndPlayerFirearms();
            }

            if (eventName == "OnFootTrafficStopStarted" && args.Length > 0) {
                HandleOnFootTrafficStopStarted(args[0]);
                return;
            }

            if (eventName == "OnFootTrafficStopEnded") {
                return;
            }

            if (trafficStopEventNames.Contains(eventName) && args.Length >= 2 && args[0] is Ped tsPed && args[1] is Vehicle tsVehicle) {
                try {
                    if (tsPed != null && tsPed.IsValid()) {
                        DataController.ResolvePedForReEncounter(tsPed);
                        DataController.AddIdentificationEvent(tsPed, eventName == "OnPedAskedToExitVehicle" ? "Procedural: Asked to exit vehicle" : "Procedural: Asked to turn off engine");
                    }
                    if (tsVehicle != null && tsVehicle.Exists())
                        DataController.ResolveVehicleAndDriverForStop(tsVehicle);
                } catch (Exception ex) {
                    Game.LogTrivial($"MDT Pro: [Warning] {eventName} handler: {ex.Message}");
                }
                return;
            }

            if (identificationEventTypes.ContainsKey(eventName) && args.Length >= 2 && args[0] is Ped idPed) {
                string idType = MapIdentificationEnum(args[1]) ?? "Identification";
                if (eventName == "OnOccupantIdentificationGiven") idType = idType + " (occupant)";
                DataController.ResolvePedForReEncounter(idPed);
                DataController.AddIdentificationEvent(idPed, idType);
                return;
            }

            // OnDeadPedSearched (PedDelegate): fires when PR search finds ID on a corpse. Add to ID History and capture firearms/drugs.
            if (eventName == "OnDeadPedSearched" && args.Length >= 1 && args[0] is Ped deadPed) {
                if (SetupController.GetConfig().firearmDebugLogging)
                    Helper.Log($"[Firearm] PR event fired: OnDeadPedSearched, pedHandle={deadPed?.Handle ?? 0}", false, Helper.LogSeverity.Info);
                DataController.AddIdentificationEvent(deadPed, "Dead body search");
                DataController.CaptureFirearmsFromPed(deadPed, "Dead body search");
                return;
            }

            foreach (object value in args) {
                ResolvePedFromValue(value, eventName);
            }
        }

        private static void HandleOnFootTrafficStopStarted(object handle) {
            if (handle == null) return;
            try {
                Type apiType = Type.GetType("PolicingRedefined.API.OnFootTrafficStopAPI, PolicingRedefined");
                if (apiType == null) return;
                MethodInfo getVehicle = apiType.GetMethod("GetOnFootTrafficStopVehicle", BindingFlags.Public | BindingFlags.Static);
                MethodInfo getSuspect = apiType.GetMethod("GetOnFootTrafficStopSuspect", BindingFlags.Public | BindingFlags.Static);
                if (getVehicle == null || getSuspect == null) return;

                object vehicleObj = getVehicle.Invoke(null, new[] { handle });
                object suspectObj = getSuspect.Invoke(null, new[] { handle });
                if (suspectObj is Ped suspect && suspect.IsValid())
                    DataController.ResolvePedForReEncounter(suspect);
                if (vehicleObj is Vehicle vehicle && vehicle.Exists())
                    DataController.ResolveVehicleAndDriverForStop(vehicle);
            } catch (Exception ex) {
                Game.LogTrivial($"MDT Pro: [Warning] OnFootTrafficStopStarted handler: {ex.Message}");
            }
        }

        /// <summary>Maps PR's EGivenIdentification enum to our ID type strings. PR may have more than ID/DriversLicense (e.g. WeaponPermit); docs only list those two.</summary>
        private static string MapIdentificationEnum(object enumValue) {
            if (enumValue == null) return null;
            Type t = enumValue.GetType();
            if (!t.IsEnum) return null;
            string name = enumValue.ToString();
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (string.Equals(name, "ID", StringComparison.OrdinalIgnoreCase)) return "State ID";
            if (string.Equals(name, "DriversLicense", StringComparison.OrdinalIgnoreCase)) return "Driver's License";
            if (string.Equals(name, "WeaponPermit", StringComparison.OrdinalIgnoreCase)) return "Weapon Permit";
            if (string.Equals(name, "WeaponsPermit", StringComparison.OrdinalIgnoreCase)) return "Weapons Permit";
            if (string.Equals(name, "FirearmsPermit", StringComparison.OrdinalIgnoreCase)) return "Firearms Permit";
            if (string.Equals(name, "FishingPermit", StringComparison.OrdinalIgnoreCase)) return "Fishing Permit";
            if (string.Equals(name, "HuntingPermit", StringComparison.OrdinalIgnoreCase)) return "Hunting Permit";
            // Pass through any other PR identification type as human-readable (PascalCase -> Title Case)
            return System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2").Trim();
        }

        private static void ResolvePedFromValue(object value, string eventName) {
            if (value == null) return;

            if (value is Ped ped) {
                if (eventName == "OnPedReleased") {
                    try {
                        if (ped == null || !ped.IsValid()) return;
                    } catch { return; }
                }
                DataController.ResolvePedForReEncounter(ped);
                if (eventName == "OnPedArrested") DataController.CaptureEvidenceForPed(ped);
                else if (eventName == "OnPedPatDown") {
                    DataController.MarkPedPatDown(ped);
                    DataController.AddIdentificationEvent(ped, "Pat-down");
                }
                else if (eventName == "OnPedSurrendered") DataController.MarkPedFleeing(ped);
                else if (identificationEventTypes.TryGetValue(eventName, out string idType)) DataController.AddIdentificationEvent(ped, idType);
                return;
            }

            Type valueType = value.GetType();
            IEnumerable<PropertyInfo> pedProperties = valueType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead && typeof(Ped).IsAssignableFrom(property.PropertyType));

            foreach (PropertyInfo property in pedProperties) {
                try {
                    object pedValue = property.GetValue(value);
                    if (pedValue is Ped nestedPed) {
                        DataController.ResolvePedForReEncounter(nestedPed);
                        if (eventName == "OnPedArrested") DataController.CaptureEvidenceForPed(nestedPed);
                        else if (eventName == "OnPedPatDown") {
                            DataController.MarkPedPatDown(nestedPed);
                            DataController.AddIdentificationEvent(nestedPed, "Pat-down");
                        }
                        else if (eventName == "OnPedSurrendered") DataController.MarkPedFleeing(nestedPed);
                        else if (identificationEventTypes.TryGetValue(eventName, out string idType)) DataController.AddIdentificationEvent(nestedPed, idType);
                    }
                } catch {
                    // Ignore malformed event payloads and continue.
                }
            }
        }
    }
}
