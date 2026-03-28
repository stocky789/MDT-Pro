// StopThePed: inject*SearchItems + reflection discovery of search-result getters (public API surface in References/StopThePed).
using Rage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MDTPro.Utility {
    internal static class StpReflectionHelper {
        private static List<MethodInfo> _vehicleSearchGetters;
        private static List<MethodInfo> _pedSearchGetters;

        internal static Assembly TryGetStopThePedAssembly() {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm.IsDynamic) continue;
                if (string.Equals(asm.GetName().Name, "StopThePed", StringComparison.OrdinalIgnoreCase))
                    return asm;
            }
            return null;
        }

        internal static bool TryInvokeInjectVehicleSearchItems(Vehicle vehicle) {
            return TryInvokeSingleArg("StopThePed.API.Functions", "injectVehicleSearchItems", vehicle);
        }

        internal static bool TryInvokeInjectPedSearchItems(Ped ped) {
            return TryInvokeSingleArg("StopThePed.API.Functions", "injectPedSearchItems", ped);
        }

        private static bool TryInvokeSingleArg(string typeName, string methodName, object arg) {
            if (arg == null) return false;
            Type fn = ModIntegration.FindTypeInLoadedAssemblies(typeName);
            if (fn == null) return false;
            Type argType = arg.GetType();
            MethodInfo m = fn.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { argType }, null);
            if (m == null) return false;
            try {
                m.Invoke(null, new[] { arg });
                return true;
            } catch {
                return false;
            }
        }

        /// <summary>After inject + short yield, try every plausible static Vehicle → IEnumerable getter on StopThePed.</summary>
        internal static bool TryGetVehicleSearchItemsEnumerable(Vehicle vehicle, out IEnumerable items) {
            items = null;
            if (vehicle == null || !vehicle.Exists()) return false;
            foreach (MethodInfo m in GetOrBuildVehicleSearchGetters()) {
                if (m.GetParameters().Length != 1 || m.GetParameters()[0].ParameterType != typeof(Vehicle)) continue;
                try {
                    object r = m.Invoke(null, new object[] { vehicle });
                    if (r != null && !(r is string) && r is IEnumerable en) {
                        items = en;
                        return true;
                    }
                } catch { /* try next */ }
            }
            return false;
        }

        /// <summary>After inject + short yield, try static Ped → IEnumerable getters on StopThePed.</summary>
        internal static bool TryGetPedSearchItemsEnumerable(Ped ped, out IEnumerable items) {
            items = null;
            if (ped == null || !ped.IsValid()) return false;
            foreach (MethodInfo m in GetOrBuildPedSearchGetters()) {
                if (m.GetParameters().Length != 1 || m.GetParameters()[0].ParameterType != typeof(Ped)) continue;
                try {
                    object r = m.Invoke(null, new object[] { ped });
                    if (r != null && !(r is string) && r is IEnumerable en) {
                        items = en;
                        return true;
                    }
                } catch { /* try next */ }
            }
            return false;
        }

        private static List<MethodInfo> GetOrBuildVehicleSearchGetters() {
            if (_vehicleSearchGetters != null) return _vehicleSearchGetters;
            _vehicleSearchGetters = BuildSearchGetters(typeof(Vehicle));
            return _vehicleSearchGetters;
        }

        private static List<MethodInfo> GetOrBuildPedSearchGetters() {
            if (_pedSearchGetters != null) return _pedSearchGetters;
            _pedSearchGetters = BuildSearchGetters(typeof(Ped));
            return _pedSearchGetters;
        }

        private static bool NameLooksLikeSearchResultGetter(string methodName) {
            if (string.IsNullOrEmpty(methodName)) return false;
            string n = methodName.ToLowerInvariant();
            if (n.Contains("inject") || n.Contains("setvehicle") || n.Contains("setped")) return false;
            if (n.Contains("search") || n.Contains("item") || n.Contains("inventory") || n.Contains("seized")
                || n.Contains("found") || n.Contains("frisk") || n.Contains("contraband") || n.Contains("stash"))
                return true;
            return false;
        }

        private static List<MethodInfo> BuildSearchGetters(Type entityParam) {
            var list = new List<MethodInfo>();
            Assembly asm = TryGetStopThePedAssembly();
            if (asm == null) return list;
            Type[] types = SafeGetTypes(asm);
            foreach (Type t in types) {
                if (t.FullName == null || !t.FullName.StartsWith("StopThePed", StringComparison.Ordinal)) continue;
                try {
                    foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                        if (!m.IsStatic || m.IsSpecialName) continue;
                        ParameterInfo[] ps = m.GetParameters();
                        if (ps.Length != 1 || ps[0].ParameterType != entityParam) continue;
                        if (!typeof(IEnumerable).IsAssignableFrom(m.ReturnType) || m.ReturnType == typeof(string)) continue;
                        if (!NameLooksLikeSearchResultGetter(m.Name)) continue;
                        list.Add(m);
                    }
                } catch { /* type load */ }
            }
            return list.OrderByDescending(m => m.Name.Length).ToList();
        }

        private static Type[] SafeGetTypes(Assembly asm) {
            try {
                return asm.GetExportedTypes();
            } catch {
                try {
                    return asm.GetTypes();
                } catch (ReflectionTypeLoadException ex) {
                    return ex.Types?.Where(x => x != null).ToArray() ?? Array.Empty<Type>();
                } catch {
                    return Array.Empty<Type>();
                }
            }
        }

        /// <summary>StopThePed.API.Functions.isPedStopped — used to attach void STP events to a plausible ped.</summary>
        internal static bool TryIsPedStoppedStp(Ped ped) {
            if (ped == null || !ped.IsValid()) return false;
            Type fn = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.Functions");
            if (fn == null) return false;
            MethodInfo m = fn.GetMethod("isPedStopped", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Ped) }, null);
            if (m == null) return false;
            try {
                object r = m.Invoke(null, new object[] { ped });
                return r is bool b && b;
            } catch {
                return false;
            }
        }

        /// <summary>StopThePed.API.Functions.isPedAlcoholOverLimit (decompiled public API).</summary>
        internal static bool TryIsPedAlcoholOverLimit(Ped ped) {
            return TryInvokePedBool("isPedAlcoholOverLimit", ped);
        }

        /// <summary>StopThePed.API.Functions.isPedUnderDrugsInfluence (decompiled public API).</summary>
        internal static bool TryIsPedUnderDrugsInfluence(Ped ped) {
            return TryInvokePedBool("isPedUnderDrugsInfluence", ped);
        }

        private static bool TryInvokePedBool(string methodName, Ped ped) {
            if (ped == null || !ped.IsValid()) return false;
            Type fn = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.Functions");
            if (fn == null) return false;
            MethodInfo m = fn.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Ped) }, null);
            if (m == null) return false;
            try {
                object r = m.Invoke(null, new object[] { ped });
                return r is bool b && b;
            } catch {
                return false;
            }
        }

        /// <summary>StopThePed.API.Functions.getVehicleRegistrationStatus → <c>STPVehicleStatus</c> (decompiled StopThePed/Decompiled). Single source for what STP shows after a check, separate from CDF document enums.</summary>
        internal static bool TryGetVehicleRegistrationStatusStp(Vehicle veh, out string status) {
            return TryGetStpVehicleDocStatus(veh, "getVehicleRegistrationStatus", out status);
        }

        /// <summary>StopThePed.API.Functions.getVehicleInsuranceStatus → <c>STPVehicleStatus</c>.</summary>
        internal static bool TryGetVehicleInsuranceStatusStp(Vehicle veh, out string status) {
            return TryGetStpVehicleDocStatus(veh, "getVehicleInsuranceStatus", out status);
        }

        private static bool TryGetStpVehicleDocStatus(Vehicle veh, string methodName, out string status) {
            status = null;
            if (veh == null || !veh.Exists()) return false;
            Type fn = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.Functions");
            if (fn == null) return false;
            MethodInfo m = fn.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Vehicle) }, null);
            if (m == null) return false;
            try {
                object r = m.Invoke(null, new object[] { veh });
                if (r == null) return false;
                status = r.ToString();
                return !string.IsNullOrEmpty(status);
            } catch {
                return false;
            }
        }
    }
}
