// Policing Redefined Backup API integration.
// Uses reflection so the plugin loads even when PR is not installed.
// API ref: https://policing-redefined.netlify.app/docs/developer-docs/pr/backup-api

using Rage;
using System;
using System.Reflection;

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
                if (ps.Length >= 2 && ps[2].ParameterType == typeof(bool)) {
                    _eBackupUnitType = ps[0].ParameterType;
                    _eBackupResponseCodeType = ps[1].ParameterType;
                    break;
                }
            }
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

        /// <summary>Request panic backup at player position. Returns true if dispatched.</summary>
        internal static bool RequestPanicBackup() {
            if (!IsAvailable) return false;
            try {
                var method = _backupApiType.GetMethod("RequestPanicBackup", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(bool), typeof(bool) }, null);
                if (method == null) return false;
                var result = method.Invoke(null, new object[] { true, true });
                return result is bool b && b;
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] BackupHelper.RequestPanicBackup: {ex.Message}");
                return false;
            }
        }

        /// <summary>Request local patrol backup at player position. Returns true if dispatched.</summary>
        internal static bool RequestLocalPatrolBackup(int responseCode = 2) {
            return RequestBackupAtPlayer("LocalPatrol", responseCode);
        }

        /// <summary>Request traffic stop backup. Only valid during an active traffic stop. unit defaults to LocalPatrol.</summary>
        internal static bool RequestTrafficStopBackup(string unitName = "LocalPatrol", int responseCode = 2) {
            if (!IsAvailable) return false;
            try {
                var unitEnum = GetPROpenType("EBackupUnit");
                var codeEnum = GetPROpenType("EBackupResponseCode");
                if (unitEnum == null || codeEnum == null) return false;
                var method = _backupApiType.GetMethod("RequestTrafficStopBackup", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { unitEnum, codeEnum, typeof(bool), typeof(bool), typeof(bool) }, null);
                if (method == null) return false;
                var unit = Enum.Parse(unitEnum, unitName);
                var code = Enum.ToObject(codeEnum, Math.Max(1, Math.Min(4, responseCode)));
                var result = method.Invoke(null, new[] { unit, code, true, true, true });
                return result is bool rb && rb;
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] BackupHelper.RequestTrafficStopBackup: {ex.Message}");
                return false;
            }
        }

        /// <summary>Request police transport for nearest arrested suspect. Returns true if dispatched.</summary>
        internal static bool RequestPoliceTransport(int responseCode = 2) {
            if (!IsAvailable) return false;
            try {
                var codeEnum = GetPROpenType("EBackupResponseCode");
                if (codeEnum == null) return false;
                var method = _backupApiType.GetMethod("RequestPoliceTransport", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { codeEnum, typeof(bool), typeof(bool), typeof(bool) }, null);
                if (method == null) return false;
                var code = Enum.ToObject(codeEnum, Math.Max(1, Math.Min(4, responseCode)));
                var result = method.Invoke(null, new[] { code, true, true, true });
                return result is bool rb && rb;
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] BackupHelper.RequestPoliceTransport: {ex.Message}");
                return false;
            }
        }

        /// <summary>Open tow service menu. Returns true if opened.</summary>
        internal static bool RequestTowServiceBackup() {
            if (!IsAvailable) return false;
            try {
                var method = _backupApiType.GetMethod("RequestTowServiceBackup", BindingFlags.Public | BindingFlags.Static);
                if (method == null) return false;
                var result = method.Invoke(null, null);
                return result is bool b && b;
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] BackupHelper.RequestTowServiceBackup: {ex.Message}");
                return false;
            }
        }

        /// <summary>Request backup at player position. unit: LocalPatrol, StatePatrol, LocalSWAT, Ambulance, etc. responseCode 1-4.</summary>
        private static bool RequestBackupAtPlayer(string unitName, int responseCode = 2) {
            if (!IsAvailable) return false;
            try {
                var unitEnum = GetPROpenType("EBackupUnit");
                var codeEnum = GetPROpenType("EBackupResponseCode");
                if (unitEnum == null || codeEnum == null) return false;
                var method = _backupApiType.GetMethod("RequestBackup", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { unitEnum, codeEnum, typeof(bool), typeof(bool), typeof(bool) }, null);
                if (method == null) return false;
                var unit = Enum.Parse(unitEnum, unitName);
                var code = Enum.ToObject(codeEnum, Math.Max(1, Math.Min(4, responseCode)));
                var result = method.Invoke(null, new[] { unit, code, true, true, true });
                return result is bool rb && rb;
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] BackupHelper.RequestBackupAtPlayer: {ex.Message}");
                return false;
            }
        }

        /// <summary>Request backup at player position. unit: EBackupUnit name (LocalPatrol, StatePatrol, LocalSWAT, Ambulance, etc).</summary>
        internal static bool RequestBackup(string unitName, int responseCode = 2) {
            return RequestBackupAtPlayer(unitName, responseCode);
        }

        /// <summary>Request group backup at player position. Returns true if dispatched.</summary>
        internal static bool RequestGroupBackup() {
            if (!IsAvailable) return false;
            try {
                var method = _backupApiType.GetMethod("RequestGroupBackup", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(bool), typeof(bool), typeof(bool) }, null);
                if (method == null) return false;
                var result = method.Invoke(null, new object[] { true, true, true });
                return result is bool b && b;
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] BackupHelper.RequestGroupBackup: {ex.Message}");
                return false;
            }
        }

        /// <summary>Request air backup (LocalAir or NooseAir). Only available during pursuits.</summary>
        internal static bool RequestAirBackup(string unitName = "LocalAir") {
            if (!IsAvailable) return false;
            try {
                var unitEnum = GetPROpenType("EBackupUnit");
                if (unitEnum == null) return false;
                var method = _backupApiType.GetMethod("RequestAirBackup", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { unitEnum, typeof(bool), typeof(bool), typeof(bool) }, null);
                if (method == null) return false;
                var unit = Enum.Parse(unitEnum, unitName);
                var result = method.Invoke(null, new[] { unit, true, true, true });
                return result is bool b && b;
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] BackupHelper.RequestAirBackup: {ex.Message}");
                return false;
            }
        }

        /// <summary>Request spike strips backup. Only available during pursuits.</summary>
        internal static bool RequestSpikeStripsBackup() {
            if (!IsAvailable) return false;
            try {
                var method = _backupApiType.GetMethod("RequestSpikeStripsBackup", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(bool), typeof(bool), typeof(bool) }, null);
                if (method == null) return false;
                var result = method.Invoke(null, new object[] { true, true, true });
                return result is bool b && b;
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] BackupHelper.RequestSpikeStripsBackup: {ex.Message}");
                return false;
            }
        }

        /// <summary>Initiate felony stop UI. Returns true if opened.</summary>
        internal static bool InitiateFelonyStop() {
            if (!IsAvailable) return false;
            try {
                var method = _backupApiType.GetMethod("InitiateFelonyStop", BindingFlags.Public | BindingFlags.Static);
                if (method == null) return false;
                var result = method.Invoke(null, null);
                return result is bool b && b;
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] BackupHelper.InitiateFelonyStop: {ex.Message}");
                return false;
            }
        }

        /// <summary>Dismiss all active and responding backup units.</summary>
        internal static void DismissAllBackupUnits(bool force = false) {
            if (!IsAvailable) return;
            try {
                var method = _backupApiType.GetMethod("DismissAllBackupUnits", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(bool) }, null);
                if (method == null) return;
                method.Invoke(null, new object[] { force });
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] BackupHelper.DismissAllBackupUnits: {ex.Message}");
            }
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
