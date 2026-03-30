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
    /// <summary>StopThePed.API.Events — subscribed when Policing Redefined is not used for stop integration (see ModIntegration). Event names match <c>STP/StopThePed.dll</c>; verify with <c>scripts/verify-stp-api.ps1</c>, snapshot in <c>STP/StopThePed.API.cs</c>.</summary>
    internal static class STPEvents {
        private static readonly HashSet<string> _stpHandlersAttached = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _stpEventsAbsent = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object _stpSubscribeLock = new object();
        /// <summary>So we can <see cref="EventInfo.RemoveEventHandler"/> on plugin unload / RPH reload — otherwise STP keeps delegates into a torn-down MDT Pro assembly.</summary>
        private static readonly List<(EventInfo Event, Delegate Handler)> _stpRegistrations = new List<(EventInfo, Delegate)>();

        // Must match StopThePed.API.Events on STP/StopThePed.dll — run scripts/verify-stp-api.ps1 after upgrading STP.
        private static readonly string[] subscriptionEventNames = {
            "stopPedEvent",
            "askIdEvent",
            "pedArrestedEvent",
            "releasePedArrestEvent",
            "patDownPedEvent",
            "breathalyzerTestEvent",
            "drugSwabTestEvent",
            "HorizontalGazeTestEvent",
            "walkTurnTestEvent",
            "oneLegStandTestEvent",
            "performCPREvent",
            "searchVehicleEvent",
            "stopTrafficEvent",
            "slowDownTrafficEvent",
            "askDriverLicenseEvent",
            "askPassengerIdEvent",
            "askRegistrationEvent",
            "askInsuranceEvent",
            "performFieldDrugTestEvent",
            "performWeaponSerialCheckEvent",
            "callTransportEvent",
            "callTowTruckEvent",
            "callInsuranceEvent",
            "callCoronerEvent",
            "callAnimalControlEvent",
            "callTaxiEvent",
            "callUberEvent",
            "callAmbulancePickupEvent"
        };

        internal static void SubscribeToStpEvents() {
            lock (_stpSubscribeLock) {
                try {
                    Type eventsType = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.Events");
                    if (eventsType == null) {
                        Game.LogTrivial("MDT Pro: [Warning] StopThePed.API.Events not found — is StopThePed loaded?");
                        return;
                    }

                    Type pedHandlerType = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.STPPedEventHandler");
                    Type vehicleHandlerType = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.STPVehicleEventHandler");
                    Type voidHandlerType = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.STPEventHandler");
                    if (pedHandlerType == null || vehicleHandlerType == null || voidHandlerType == null) {
                        Game.LogTrivial("MDT Pro: [Warning] StopThePed delegate types not found.");
                        return;
                    }

                    int newThisPass = 0;
                    foreach (string eventName in subscriptionEventNames) {
                        if (_stpHandlersAttached.Contains(eventName) || _stpEventsAbsent.Contains(eventName)) continue;
                        EventInfo evt = eventsType.GetEvent(eventName, BindingFlags.Public | BindingFlags.Static);
                        if (evt == null) {
                            _stpEventsAbsent.Add(eventName);
                            continue;
                        }
                        Type delegateType = evt.EventHandlerType;
                        string kind = delegateType == pedHandlerType ? "ped" : delegateType == vehicleHandlerType ? "vehicle" : "void";
                        try {
                            Delegate handler = CreateForwardingDelegate(delegateType, eventName, kind);
                            evt.AddEventHandler(null, handler);
                            _stpRegistrations.Add((evt, handler));
                            _stpHandlersAttached.Add(eventName);
                            newThisPass++;
                        } catch (Exception ex) {
                            Game.LogTrivial($"MDT Pro: [Warning] STP event {eventName}: {ex.Message}");
                        }
                    }

                    if (newThisPass > 0)
                        Game.LogTrivial($"[MDT Pro] StopThePed.API.Events: attached {newThisPass} handler(s) ({_stpHandlersAttached.Count} total).");
                } catch (Exception e) {
                    Game.LogTrivial($"MDT Pro: [Warning] STPEvents subscribe failed: {e.Message}");
                }
            }
        }

        /// <summary>Removes all STP static event handlers registered by this plugin load. Call from Main.Finally before the assembly unloads.</summary>
        internal static void UnsubscribeAll() {
            lock (_stpSubscribeLock) {
                for (int i = _stpRegistrations.Count - 1; i >= 0; i--) {
                    var (evt, handler) = _stpRegistrations[i];
                    try {
                        evt?.RemoveEventHandler(null, handler);
                    } catch {
                        /* ignore */
                    }
                }
                _stpRegistrations.Clear();
                _stpHandlersAttached.Clear();
            }
        }

        private static Delegate CreateForwardingDelegate(Type delegateType, string eventName, string kind) {
            MethodInfo invokeMethod = delegateType.GetMethod("Invoke");
            ParameterInfo[] parameters = invokeMethod.GetParameters();
            ParameterExpression[] parameterExpressions = parameters
                .Select(p => Expression.Parameter(p.ParameterType, p.Name))
                .ToArray();

            NewArrayExpression argsArrayExpression = Expression.NewArrayInit(
                typeof(object),
                parameterExpressions.Select(p => Expression.Convert(p, typeof(object))));

            MethodCallExpression body = Expression.Call(
                typeof(STPEvents).GetMethod(nameof(HandleStpEvent), BindingFlags.NonPublic | BindingFlags.Static),
                Expression.Constant(eventName),
                Expression.Constant(kind),
                argsArrayExpression);

            return Expression.Lambda(delegateType, body, parameterExpressions).Compile();
        }

        private static void HandleStpEvent(string eventName, string kind, object[] args) {
            try {
                if (kind == "void") {
                    if (eventName == "performWeaponSerialCheckEvent") {
                        // STP void event has no ped: do NOT use TryCapturePickupAndPlayerFirearms alone on STP-only
                        // — that only saved the officer's held weapon with no serial. Mirror field-drug-test flow:
                        // nearest STP-stopped ped + inject/get search items so serials match in-game STP UI.
                        if (ModIntegration.SubscribedStopThePedStopEvents)
                            GameFiber.StartNew(() => TryCaptureStpNearestStoppedPedSearch("Firearm check (STP)"));
                        if (ModIntegration.HasPolicingRedefinedSearchItemsApi)
                            DataController.TryCapturePickupAndPlayerFirearms();
                    } else if (eventName == "performFieldDrugTestEvent")
                        GameFiber.StartNew(() => TryCaptureStpNearestStoppedPedSearch("Field drug test (STP)"));
                    else if (eventName == "stopTrafficEvent" || eventName == "slowDownTrafficEvent") {
                        /* STP traffic control — no ped/vehicle args; optional native-side effects only */
                    } else if (eventName.StartsWith("call", StringComparison.Ordinal) && eventName.EndsWith("Event", StringComparison.Ordinal)) {
                        if (SetupController.GetConfig().firearmDebugLogging)
                            Game.LogTrivial($"[MDT Pro] STP service event: {eventName}");
                    }
                    return;
                }

                if (kind == "vehicle" && args != null && args.Length >= 1 && args[0] is Vehicle veh && veh.Exists()) {
                    DataController.TouchStopThePedStopScene(veh.Position);
                    if (eventName == "askPassengerIdEvent") {
                        // RAM + search UX only; SQLite vehicle row when registration/insurance is run (see ped-side STP handlers).
                        DataController.ResolveVehicleAndDriverForStop(veh, persistToSql: false);
                        DataController.AddIdentificationEventForVehicleOccupantsStp(veh, "Occupant ID (STP)");
                    } else if (eventName == "searchVehicleEvent") {
                        DataController.ResolveVehicleAndDriverForStop(veh, persistToSql: false);
                        DataController.CaptureVehicleSearchItems(veh);
                        GameFiber.StartNew(() => {
                            GameFiber.Wait(8000);
                            try {
                                if (veh != null && veh.Exists())
                                    DataController.CaptureVehicleSearchItems(veh);
                            } catch { /* ignore */ }
                        }, "MDTPro-stp-vehicle-search-delayed");
                    }
                    return;
                }

                if (kind != "ped" || args == null || args.Length < 1 || !(args[0] is Ped ped) || !ped.IsValid())
                    return;

                if (eventName == "releasePedArrestEvent") {
                    try {
                        if (ped == null || !ped.IsValid()) return;
                    } catch { return; }
                    DataController.ResolvePedForReEncounter(ped, persistToSql: true);
                    DataController.ClearStopThePedStopScene();
                    return;
                }

                DataController.TouchStopThePedStopScene(ped.Position);

                if (eventName == "pedArrestedEvent") {
                    DataController.ResolvePedForReEncounter(ped, persistToSql: true);
                    DataController.CaptureFirearmsFromPed(ped, "Arrest (STP)");
                    return;
                }

                if (eventName == "patDownPedEvent") {
                    DataController.ResolvePedForReEncounter(ped, persistToSql: false);
                    DataController.MarkPedPatDown(ped);
                    DataController.AddIdentificationEvent(ped, "Pat-down");
                    return;
                }

                if (eventName == "askDriverLicenseEvent") {
                    DataController.ResolvePedForReEncounter(ped, persistToSql: false);
                    DataController.AddIdentificationEvent(ped, "Driver's License");
                    TryAssociateVehicleForStoppedPed(ped, persistVehicleToSql: false);
                    return;
                }

                if (eventName == "askRegistrationEvent" || eventName == "askInsuranceEvent") {
                    DataController.ResolvePedForReEncounter(ped, persistToSql: false);
                    DataController.AddIdentificationEvent(ped, eventName == "askRegistrationEvent" ? "Registration (STP)" : "Insurance (STP)");
                    TryAssociateVehicleForStoppedPed(ped, persistVehicleToSql: true);
                    return;
                }

                if (eventName == "askIdEvent") {
                    DataController.ResolvePedForReEncounter(ped, persistToSql: false);
                    DataController.AddIdentificationEvent(ped, "Identification (STP)");
                    TryAssociateVehicleForStoppedPed(ped, persistVehicleToSql: false);
                    return;
                }

                if (eventName == "stopPedEvent") {
                    DataController.ResolvePedForReEncounter(ped, persistToSql: false);
                    TryAssociateVehicleForStoppedPed(ped, persistVehicleToSql: false);
                    return;
                }

                if (eventName == "breathalyzerTestEvent" || eventName == "drugSwabTestEvent") {
                    DataController.ResolvePedForReEncounter(ped, persistToSql: false);
                    DataController.ApplyStopThePedImpairmentEvidence(ped);
                    return;
                }

                if (eventName == "HorizontalGazeTestEvent" || eventName == "walkTurnTestEvent" || eventName == "oneLegStandTestEvent") {
                    DataController.ResolvePedForReEncounter(ped, persistToSql: false);
                    DataController.ApplyStopThePedImpairmentEvidence(ped);
                    return;
                }

                if (eventName == "performCPREvent") {
                    DataController.ResolvePedForReEncounter(ped, persistToSql: false);
                    DataController.AddIdentificationEvent(ped, "CPR (STP)");
                    return;
                }
            } catch (Exception ex) {
                Game.LogTrivial($"MDT Pro: [Warning] STP handler {eventName}: {ex.Message}");
            }
        }

        /// <summary>Void STP events have no ped handle — use isPedStopped + proximity (no NativeDB list for mod inventory).</summary>
        private static void TryCaptureStpNearestStoppedPedSearch(string source) {
            try {
                // Slightly longer yield after firearm check so STP can commit weapon/serial to search items.
                GameFiber.Wait(source != null && source.IndexOf("Firearm", StringComparison.OrdinalIgnoreCase) >= 0 ? 200 : 50);
                Ped self = Game.LocalPlayer.Character;
                if (self == null || !self.IsValid()) return;
                Ped best = null;
                float bestD = 18f;
                foreach (Ped p in World.GetAllPeds()) {
                    if (p == null || !p.IsValid() || p == self) continue;
                    if (p.IsDead) continue;
                    float d = self.DistanceTo(p);
                    if (d > 18f) continue;
                    if (!StpReflectionHelper.TryIsPedStoppedStp(p)) continue;
                    if (best == null || d < bestD) {
                        best = p;
                        bestD = d;
                    }
                }
                if (best != null) {
                    DataController.ResolvePedForReEncounter(best, persistToSql: false);
                    DataController.CaptureFirearmsFromPed(best, source);
                }
            } catch { /* ignore */ }
        }

        /// <param name="persistVehicleToSql">True when STP shows vehicle documents (registration/insurance); false for stop/ID/license-only.</param>
        private static void TryAssociateVehicleForStoppedPed(Ped ped, bool persistVehicleToSql = false) {
            try {
                if (ped == null || !ped.IsValid()) return;
                if (ped.IsInAnyVehicle(false)) {
                    var v = ped.CurrentVehicle;
                    if (v != null && v.Exists())
                        DataController.ResolveVehicleAndDriverForStop(v, persistToSql: persistVehicleToSql);
                }
            } catch { /* ignore */ }
        }
    }
}
