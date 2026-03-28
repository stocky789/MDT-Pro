// Ultimate Backup API via reflection (UltimateBackup.API.Functions). Runs on game fiber.
using Rage;
using System;
using System.Linq;
using System.Reflection;

namespace MDTPro.Utility.Backup {
    internal sealed class UltimateBackupBackupDispatcher : IBackupDispatcher {
        private readonly Type _functionsType;

        internal UltimateBackupBackupDispatcher() {
            _functionsType = ModIntegration.FindTypeInLoadedAssemblies("UltimateBackup.API.Functions")
                ?? Type.GetType("UltimateBackup.API.Functions, UltimateBackup");
        }

        public string ProviderId => "UltimateBackup";
        public bool IsAvailable => _functionsType != null;

        /// <summary>Invokes first static method on Functions that matches name and accepts the given argument types. Void methods count as success.</summary>
        private bool InvokeStatic(string methodName, object[] args) {
            if (_functionsType == null) return false;
            try {
                var methods = _functionsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name == methodName)
                    .OrderByDescending(m => m.GetParameters().Length)
                    .ToList();
                foreach (var m in methods) {
                    var ps = m.GetParameters();
                    if (ps.Length != args.Length) continue;
                    bool ok = true;
                    for (int i = 0; i < ps.Length; i++) {
                        if (args[i] == null) {
                            bool nullArgAllowed = !ps[i].ParameterType.IsValueType
                                || Nullable.GetUnderlyingType(ps[i].ParameterType) != null;
                            if (!nullArgAllowed) {
                                ok = false;
                                break;
                            }
                        } else if (!ps[i].ParameterType.IsAssignableFrom(args[i].GetType())) {
                            ok = false;
                            break;
                        }
                    }
                    if (!ok) continue;
                    object r = m.Invoke(null, args);
                    if (r == null || (r is bool b && b)) return true;
                }
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] UltimateBackup.{methodName}: {ex.Message}");
            }
            return false;
        }

        private bool TryCode2(bool statePatrol) {
            if (InvokeStatic("callCode2Backup", new object[] { true, statePatrol })) return true;
            if (InvokeStatic("callCode2Backup", new object[] { true })) return true;
            return InvokeStatic("callCode2Backup", Array.Empty<object>());
        }

        private bool TryCode3(bool statePatrol) {
            if (InvokeStatic("callCode3Backup", new object[] { true, statePatrol })) return true;
            if (InvokeStatic("callCode3Backup", new object[] { true })) return true;
            return InvokeStatic("callCode3Backup", Array.Empty<object>());
        }

        public bool RequestPanicBackup() {
            if (InvokeStatic("callPanicButtonBackup", new object[] { true })) return true;
            return InvokeStatic("callPanicButtonBackup", Array.Empty<object>());
        }

        public bool RequestBackup(string unitName, int responseCode) {
            string u = unitName ?? "";
            if (u.IndexOf("SWAT", StringComparison.OrdinalIgnoreCase) >= 0) {
                bool noose = u.IndexOf("Noose", StringComparison.OrdinalIgnoreCase) >= 0;
                return InvokeStatic("callCode3SwatBackup", new object[] { true, noose });
            }
            if (u.IndexOf("K9", StringComparison.OrdinalIgnoreCase) >= 0) {
                bool k9State = u.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0;
                if (InvokeStatic("callK9Backup", new object[] { true, k9State })) return true;
                return InvokeStatic("callK9Backup", Array.Empty<object>());
            }
            if (u.IndexOf("Ambulance", StringComparison.OrdinalIgnoreCase) >= 0)
                return InvokeStatic("callAmbulance", Array.Empty<object>());
            if (u.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) >= 0)
                return InvokeStatic("callFireDepartment", Array.Empty<object>());
            if (u.IndexOf("Coroner", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf("Animal", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            int rc = Math.Max(1, Math.Min(4, responseCode));
            bool statePatrol = u.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0;
            if (rc >= 3) return TryCode3(statePatrol);
            return TryCode2(statePatrol);
        }

        public bool RequestTrafficStopBackup(string unitName, int responseCode) {
            bool statePatrol = unitName != null && unitName.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0;
            if (InvokeStatic("callTrafficStopBackup", new object[] { true, statePatrol })) return true;
            return InvokeStatic("callTrafficStopBackup", Array.Empty<object>());
        }

        public bool RequestPoliceTransport(int responseCode) {
            return false;
        }

        public bool RequestTowServiceBackup() {
            return false;
        }

        public bool RequestGroupBackup() {
            if (InvokeStatic("callGroupBackup", new object[] { true })) return true;
            return InvokeStatic("callGroupBackup", Array.Empty<object>());
        }

        public bool RequestAirBackup(string unitName) {
            bool noose = unitName != null && unitName.IndexOf("Noose", StringComparison.OrdinalIgnoreCase) >= 0;
            if (InvokeStatic("callPursuitBackup", new object[] { true, noose })) return true;
            if (InvokeStatic("callPursuitBackup", new object[] { true })) return true;
            return InvokeStatic("callPursuitBackup", Array.Empty<object>());
        }

        public bool RequestSpikeStripsBackup() {
            return InvokeStatic("callSpikeStripsBackup", Array.Empty<object>());
        }

        public bool InitiateFelonyStop() {
            bool statePatrol = true;
            if (InvokeStatic("callFelonyStopBackup", new object[] { true, statePatrol })) return true;
            return InvokeStatic("callFelonyStopBackup", Array.Empty<object>());
        }

        public void DismissAllBackupUnits(bool force) {
            if (InvokeStatic("dismissAllBackupUnits", new object[] { force })) return;
            InvokeStatic("dismissAllBackupUnits", Array.Empty<object>());
        }
    }
}
