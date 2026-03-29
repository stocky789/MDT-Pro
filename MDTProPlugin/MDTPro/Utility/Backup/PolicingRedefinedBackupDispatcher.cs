// Policing Redefined Backup API — must run on game fiber (caller marshals).
using Rage;
using System;
using System.Reflection;

namespace MDTPro.Utility.Backup {
    internal sealed class PolicingRedefinedBackupDispatcher : IBackupDispatcher {
        private readonly Type _backupApiType;
        private Type _eBackupUnitType;
        private Type _eBackupResponseCodeType;

        internal PolicingRedefinedBackupDispatcher() {
            _backupApiType = ModIntegration.FindTypeInLoadedAssemblies("PolicingRedefined.API.BackupAPI")
                ?? Type.GetType("PolicingRedefined.API.BackupAPI, PolicingRedefined");
        }

        public string ProviderId => "PolicingRedefined";
        public bool IsAvailable => _backupApiType != null;

        private void EnsureEnumTypesResolved() {
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
            Game.LogTrivial("[MDTPro] PolicingRedefinedBackupDispatcher: Could not resolve EBackupUnit/EBackupResponseCode.");
        }

        private Type GetPROpenType(string typeName) {
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

        private static bool TryParseBackupUnitEnum(Type unitEnum, string unitName, out object unit) {
            unit = null;
            if (unitEnum == null || string.IsNullOrWhiteSpace(unitName)) return false;
            try {
                unit = Enum.Parse(unitEnum, unitName, true);
                return true;
            } catch (ArgumentException) {
                return false;
            }
        }

        public bool RequestPanicBackup() {
            if (!IsAvailable) return false;
            var method = _backupApiType.GetMethod("RequestPanicBackup", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(bool), typeof(bool) }, null);
            if (method == null) return false;
            var r = method.Invoke(null, new object[] { true, true });
            return r is bool b && b;
        }

        public bool RequestBackup(string unitName, int responseCode) {
            if (!IsAvailable) return false;
            var unitEnum = GetPROpenType("EBackupUnit");
            var codeEnum = GetPROpenType("EBackupResponseCode");
            if (unitEnum == null || codeEnum == null) return false;
            var method = _backupApiType.GetMethod("RequestBackup", BindingFlags.Public | BindingFlags.Static,
                null, new[] { unitEnum, codeEnum, typeof(bool), typeof(bool), typeof(bool) }, null);
            if (method == null) return false;
            if (!TryParseBackupUnitEnum(unitEnum, unitName, out object unit)) return false;
            var code = Enum.ToObject(codeEnum, Math.Max(0, Math.Min(3, responseCode - 1)));
            var r = method.Invoke(null, new[] { unit, code, true, true, true });
            return r is bool rb && rb;
        }

        public bool RequestTrafficStopBackup(string unitName, int responseCode) {
            if (!IsAvailable) return false;
            var unitEnum = GetPROpenType("EBackupUnit");
            var codeEnum = GetPROpenType("EBackupResponseCode");
            if (unitEnum == null || codeEnum == null) return false;
            var method = _backupApiType.GetMethod("RequestTrafficStopBackup", BindingFlags.Public | BindingFlags.Static,
                null, new[] { unitEnum, codeEnum, typeof(bool), typeof(bool), typeof(bool) }, null);
            if (method == null) return false;
            if (!TryParseBackupUnitEnum(unitEnum, unitName, out object unit)) return false;
            var code = Enum.ToObject(codeEnum, Math.Max(0, Math.Min(3, responseCode - 1)));
            var r = method.Invoke(null, new[] { unit, code, true, true, true });
            return r is bool rb && rb;
        }

        public bool RequestPoliceTransport(int responseCode) {
            if (!IsAvailable) return false;
            var codeEnum = GetPROpenType("EBackupResponseCode");
            if (codeEnum == null) return false;
            var method = _backupApiType.GetMethod("RequestPoliceTransport", BindingFlags.Public | BindingFlags.Static,
                null, new[] { codeEnum, typeof(bool), typeof(bool), typeof(bool) }, null);
            if (method == null) return false;
            var code = Enum.ToObject(codeEnum, Math.Max(0, Math.Min(3, responseCode - 1)));
            var r = method.Invoke(null, new[] { code, true, true, true });
            return r is bool rb && rb;
        }

        public bool RequestTowServiceBackup() {
            if (!IsAvailable) return false;
            var method = _backupApiType.GetMethod("RequestTowServiceBackup", BindingFlags.Public | BindingFlags.Static);
            return method != null && method.Invoke(null, null) is bool b && b;
        }

        public bool RequestGroupBackup() {
            if (!IsAvailable) return false;
            var method = _backupApiType.GetMethod("RequestGroupBackup", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(bool), typeof(bool), typeof(bool) }, null);
            return method != null && method.Invoke(null, new object[] { true, true, true }) is bool b && b;
        }

        public bool RequestAirBackup(string unitName) {
            if (!IsAvailable) return false;
            var unitEnum = GetPROpenType("EBackupUnit");
            if (unitEnum == null) return false;
            var method = _backupApiType.GetMethod("RequestAirBackup", BindingFlags.Public | BindingFlags.Static,
                null, new[] { unitEnum, typeof(bool), typeof(bool), typeof(bool) }, null);
            if (method == null || !TryParseBackupUnitEnum(unitEnum, unitName, out object unit)) return false;
            return method.Invoke(null, new[] { unit, true, true, true }) is bool b && b;
        }

        public bool RequestSpikeStripsBackup() {
            if (!IsAvailable) return false;
            var method = _backupApiType.GetMethod("RequestSpikeStripsBackup", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(bool), typeof(bool), typeof(bool) }, null);
            return method != null && method.Invoke(null, new object[] { true, true, true }) is bool b && b;
        }

        public bool InitiateFelonyStop() {
            if (!IsAvailable) return false;
            var method = _backupApiType.GetMethod("InitiateFelonyStop", BindingFlags.Public | BindingFlags.Static);
            return method != null && method.Invoke(null, null) is bool b && b;
        }

        public void DismissAllBackupUnits(bool force) {
            if (!IsAvailable) return;
            try {
                var method = _backupApiType.GetMethod("DismissAllBackupUnits", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(bool) }, null);
                method?.Invoke(null, new object[] { force });
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] PR DismissAllBackupUnits: {ex.Message}");
            }
        }
    }
}
