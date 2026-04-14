using System;
using System.Reflection;

namespace CalloutInterfaceAPI.External
{
    /// <summary>
    /// Invokes StopThePed via reflection so CalloutInterfaceAPI does not require StopThePed.dll at compile time (STP loads with the game).
    /// </summary>
    internal static class StopThePedInvoker
    {
        static Type _functionsType;
        static Type _stpVehicleStatusEnum;

        static void EnsureTypes()
        {
            if (_functionsType != null) return;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                if (!string.Equals(asm.GetName().Name, "StopThePed", StringComparison.OrdinalIgnoreCase)) continue;
                _functionsType = asm.GetType("StopThePed.API.Functions");
                _stpVehicleStatusEnum = asm.GetType("StopThePed.API.STPVehicleStatus");
                break;
            }
        }

        internal static VehicleDocumentStatus GetVehicleDocumentStatus(Rage.Vehicle vehicle, VehicleDocument document)
        {
            if (!vehicle) return VehicleDocumentStatus.Unknown;
            EnsureTypes();
            if (_functionsType == null) return VehicleDocumentStatus.Unknown;
            string methodName = document == VehicleDocument.Insurance ? "getVehicleInsuranceStatus" : "getVehicleRegistrationStatus";
            MethodInfo m = _functionsType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Rage.Vehicle) }, null);
            if (m == null) return VehicleDocumentStatus.Unknown;
            try
            {
                object r = m.Invoke(null, new object[] { vehicle });
                return MapStpToDoc(r);
            }
            catch
            {
                return VehicleDocumentStatus.Unknown;
            }
        }

        static VehicleDocumentStatus MapStpToDoc(object stpEnumValue)
        {
            if (stpEnumValue == null) return VehicleDocumentStatus.Unknown;
            string s = stpEnumValue.ToString();
            if (string.Equals(s, "Expired", StringComparison.OrdinalIgnoreCase)) return VehicleDocumentStatus.Expired;
            if (string.Equals(s, "None", StringComparison.OrdinalIgnoreCase)) return VehicleDocumentStatus.None;
            if (string.Equals(s, "Valid", StringComparison.OrdinalIgnoreCase)) return VehicleDocumentStatus.Valid;
            return VehicleDocumentStatus.Unknown;
        }

        internal static void SetVehicleDocumentStatus(Rage.Vehicle vehicle, VehicleDocument document, VehicleDocumentStatus status)
        {
            if (!vehicle) return;
            EnsureTypes();
            if (_functionsType == null || _stpVehicleStatusEnum == null) return;
            string methodName = document == VehicleDocument.Insurance ? "setVehicleInsuranceStatus" : "setVehicleRegistrationStatus";
            MethodInfo m = _functionsType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Rage.Vehicle), _stpVehicleStatusEnum }, null);
            if (m == null) return;
            string enumName = status == VehicleDocumentStatus.Expired ? "Expired" : (status == VehicleDocumentStatus.None ? "None" : "Valid");
            object stpVal;
            try
            {
                stpVal = Enum.Parse(_stpVehicleStatusEnum, enumName);
            }
            catch
            {
                return;
            }
            try
            {
                m.Invoke(null, new[] { vehicle, stpVal });
            }
            catch
            {
            }
        }
    }
}
