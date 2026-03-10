// Policing Redefined Backup API integration.
// PR Backup API must run on the game fiber. We dispatch all calls via GameFiber.
// API ref: https://policing-redefined.netlify.app/docs/developer-docs/pr/backup-api

using Rage;
using System;
using System.Reflection;
using System.Threading;

namespace MDTPro.Utility {
    internal static class BackupHelper {
        private static Type _backupApiType;
        private static Type _onFootApiType;
        private static Type _eBackupUnitType;
        private static Type _eBackupResponseCodeType;
        private static bool _initialized;
        private static bool _prAvailable;

        private static void EnsureInitialized() {
            if (_initialized) return;
            _backupApiType = FindTypeInLoadedAssemblies("PolicingRedefined.API.BackupAPI")
                ?? Type.GetType("PolicingRedefined.API.BackupAPI, PolicingRedefined");
            _onFootApiType = _backupApiType != null
                ? _backupApiType.Assembly.GetType("PolicingRedefined.API.OnFootTrafficStopAPI")
                : FindTypeInLoadedAssemblies("PolicingRedefined.API.OnFootTrafficStopAPI");
            _prAvailable = _backupApiType != null;
            _initialized = true;
        }

        /// <summary>Search loaded assemblies for a type by full name (fixes Type.GetType failures when PR loads from game folder).</summary>
        private static Type FindTypeInLoadedAssemblies(string fullName) {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm.IsDynamic) continue;
                try {
                    var t = asm.GetType(fullName);
                    if (t != null) return t;
                } catch { /* ignore */ }
            }
            return null;
        }

        /// <summary>Resolve EBackupUnit and EBackupResponseCode from RequestBackup method params (bulletproof regardless of PR namespace).</summary>
        private static void EnsureEnumTypesResolved() {
            if (_eBackupUnitType != null && _eBackupResponseCodeType != null) return;
            if (_backupApiType == null) return;
            foreach (var m in _backupApiType.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                if (m.Name != "RequestBackup") continue;
                var ps = m.GetParameters();
                if (ps.Length == 5 && ps[2].ParameterType == typeof(bool)) {
                    _eBackupUnitType = ps[0].ParameterType;
                    _eBackupResponseCodeType = ps[1].ParameterType;
                    return;
                }
            }
            Game.LogTrivial("[MDTPro] BackupHelper: Could not resolve EBackupUnit/EBackupResponseCode from RequestBackup. Enum-based backup will fail.");
        }

        /// <summary>Resolve enum type from PR assembly; prefers method param types, falls back to namespace search.</summary>
        private static Type GetPROpenType(string typeName) {
            EnsureEnumTypesResolved();
            if (typeName == "EBackupUnit" && _eBackupUnitType != null) return _eBackupUnitType;
            if (typeName == "EBackupResponseCode" && _eBackupResponseCodeType != null) return _eBackupResponseCodeType;
            if (_backupApiType?.Assembly == null) return null;
            var asm = _backupApiType.Assembly;
            foreach (var ns in new[] { "PolicingRedefined.API", "PolicingRedefined.Enums", "PolicingRedefined" }) {
                var t = asm.GetType(ns + "." + typeName);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>True if Policing Redefined Backup API is available.</summary>
        internal static bool IsAvailable {
            get {
                EnsureInitialized();
                return _prAvailable;
            }
        }

        /// <summary>Invoke PR Backup API on game fiber (required by PR). Returns result.</summary>
        private static bool InvokeOnGameFiber(Func<bool> action) {
            if (!IsAvailable || action == null) return false;
            bool result = false;
            var done = new ManualResetEventSlim(false);
            GameFiber.StartNew(() => {
                try {
                    result = action();
                } catch (Exception ex) {
                    Game.LogTrivial($"[MDTPro] BackupHelper: {ex.Message}");
                } finally {
                    done.Set();
                }
            });
            return done.Wait(5000) && result;
        }

        /// <summary>Request panic backup at player position. Returns true if dispatched.</summary>
        internal static bool RequestPanicBackup() {
            return InvokeOnGameFiber(() => {
                var method = _backupApiType.GetMethod("RequestPanicBackup", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(bool), typeof(bool) }, null);
                if (method == null) return false;
                var r = method.Invoke(null, new object[] { true, true });
                return r is bool b && b;
            });
        }

        /// <summary>Request local patrol backup at player position. Returns true if dispatched.</summary>
        internal static bool RequestLocalPatrolBackup(int responseCode = 2) {
            return RequestBackupAtPlayer("LocalPatrol", responseCode);
        }

        /// <summary>Request traffic stop backup. Only valid during an active traffic stop. unit defaults to LocalPatrol.</summary>
        internal static bool RequestTrafficStopBackup(string unitName = "LocalPatrol", int responseCode = 2) {
            return InvokeOnGameFiber(() => RequestTrafficStopBackupInner(unitName, responseCode));
        }
        private static bool RequestTrafficStopBackupInner(string unitName, int responseCode) {
            var unitEnum = GetPROpenType("EBackupUnit");
            var codeEnum = GetPROpenType("EBackupResponseCode");
            if (unitEnum == null || codeEnum == null) return false;
            var method = _backupApiType.GetMethod("RequestTrafficStopBackup", BindingFlags.Public | BindingFlags.Static,
                null, new[] { unitEnum, codeEnum, typeof(bool), typeof(bool), typeof(bool) }, null);
            if (method == null) return false;
            var unit = Enum.Parse(unitEnum, unitName);
            var code = Enum.ToObject(codeEnum, Math.Max(0, Math.Min(3, responseCode - 1)));
            var r = method.Invoke(null, new[] { unit, code, true, true, true });
            return r is bool rb && rb;
        }

        /// <summary>Request police transport for nearest arrested suspect. Returns true if dispatched.</summary>
        internal static bool RequestPoliceTransport(int responseCode = 2) {
            return InvokeOnGameFiber(() => RequestPoliceTransportInner(responseCode));
        }
        private static bool RequestPoliceTransportInner(int responseCode) {
            var codeEnum = GetPROpenType("EBackupResponseCode");
            if (codeEnum == null) return false;
            var method = _backupApiType.GetMethod("RequestPoliceTransport", BindingFlags.Public | BindingFlags.Static,
                null, new[] { codeEnum, typeof(bool), typeof(bool), typeof(bool) }, null);
            if (method == null) return false;
            var code = Enum.ToObject(codeEnum, Math.Max(0, Math.Min(3, responseCode - 1)));
            var r = method.Invoke(null, new[] { code, true, true, true });
            return r is bool rb && rb;
        }

        /// <summary>Open tow service menu. Returns true if opened.</summary>
        internal static bool RequestTowServiceBackup() {
            return InvokeOnGameFiber(() => {
                var method = _backupApiType.GetMethod("RequestTowServiceBackup", BindingFlags.Public | BindingFlags.Static);
                return method != null && method.Invoke(null, null) is bool b && b;
            });
        }

        /// <summary>Request backup at player position. unit: LocalPatrol, StatePatrol, LocalSWAT, Ambulance, etc. responseCode 1-4 (UI); PR enum is 0-based (Code1=0, Code2=1, Code3=2, Code4=3).</summary>
        private static bool RequestBackupAtPlayer(string unitName, int responseCode = 2) {
            return InvokeOnGameFiber(() => RequestBackupAtPlayerInner(unitName, responseCode));
        }
        private static bool RequestBackupAtPlayerInner(string unitName, int responseCode) {
            var unitEnum = GetPROpenType("EBackupUnit");
            var codeEnum = GetPROpenType("EBackupResponseCode");
            if (unitEnum == null || codeEnum == null) return false;
            var method = _backupApiType.GetMethod("RequestBackup", BindingFlags.Public | BindingFlags.Static,
                null, new[] { unitEnum, codeEnum, typeof(bool), typeof(bool), typeof(bool) }, null);
            if (method == null) return false;
            var unit = Enum.Parse(unitEnum, unitName);
            var code = Enum.ToObject(codeEnum, Math.Max(0, Math.Min(3, responseCode - 1)));
            var r = method.Invoke(null, new[] { unit, code, true, true, true });
            return r is bool rb && rb;
        }

        /// <summary>Request backup at player position. unit: EBackupUnit name (LocalPatrol, StatePatrol, LocalSWAT, Ambulance, etc).</summary>
        internal static bool RequestBackup(string unitName, int responseCode = 2) {
            return RequestBackupAtPlayer(unitName, responseCode);
        }

        /// <summary>Request group backup at player position. Returns true if dispatched.</summary>
        internal static bool RequestGroupBackup() {
            return InvokeOnGameFiber(() => {
                var method = _backupApiType.GetMethod("RequestGroupBackup", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(bool), typeof(bool), typeof(bool) }, null);
                return method != null && method.Invoke(null, new object[] { true, true, true }) is bool b && b;
            });
        }

        /// <summary>Request air backup (LocalAir or NooseAir). Only available during pursuits.</summary>
        internal static bool RequestAirBackup(string unitName = "LocalAir") {
            return InvokeOnGameFiber(() => {
                var unitEnum = GetPROpenType("EBackupUnit");
                if (unitEnum == null) return false;
                var method = _backupApiType.GetMethod("RequestAirBackup", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { unitEnum, typeof(bool), typeof(bool), typeof(bool) }, null);
                return method != null && method.Invoke(null, new[] { Enum.Parse(unitEnum, unitName), true, true, true }) is bool b && b;
            });
        }

        /// <summary>Request spike strips backup. Only available during pursuits.</summary>
        internal static bool RequestSpikeStripsBackup() {
            return InvokeOnGameFiber(() => {
                var method = _backupApiType.GetMethod("RequestSpikeStripsBackup", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(bool), typeof(bool), typeof(bool) }, null);
                return method != null && method.Invoke(null, new object[] { true, true, true }) is bool b && b;
            });
        }

        /// <summary>Initiate felony stop UI. Returns true if opened.</summary>
        internal static bool InitiateFelonyStop() {
            return InvokeOnGameFiber(() => {
                var method = _backupApiType.GetMethod("InitiateFelonyStop", BindingFlags.Public | BindingFlags.Static);
                return method != null && method.Invoke(null, null) is bool b && b;
            });
        }

        /// <summary>Dismiss all active and responding backup units.</summary>
        internal static void DismissAllBackupUnits(bool force = false) {
            if (!IsAvailable) return;
            GameFiber.StartNew(() => {
                try {
                    var method = _backupApiType.GetMethod("DismissAllBackupUnits", BindingFlags.Public | BindingFlags.Static,
                        null, new[] { typeof(bool) }, null);
                    method?.Invoke(null, new object[] { force });
                } catch (Exception ex) {
                    Game.LogTrivial($"[MDTPro] BackupHelper.DismissAllBackupUnits: {ex.Message}");
                }
            });
        }

        /// <summary>Returns true if player is on an on-foot traffic stop.</summary>
        internal static bool IsOnFootTrafficStop() {
            if (_onFootApiType == null) {
                EnsureInitialized();
                if (_onFootApiType == null) return false;
            }
            try {
                var method = _onFootApiType.GetMethod("IsOnAnyFootTrafficStop", BindingFlags.Public | BindingFlags.Static);
                if (method == null) return false;
                return method.Invoke(null, null) is bool b && b;
            } catch {
                return false;
            }
        }
    }
}
