using MDTPro.Data;
using MDTPro.Setup;
using Newtonsoft.Json.Linq;
using Rage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MDTPro.Utility {
    internal static class PRFirearmSearchItemHelper {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        internal static JObject InspectCapabilities() {
            JObject result = new JObject {
                ["prActive"] = Main.usePR,
                ["searchItemsApi"] = false,
                ["firearmItemType"] = null,
                ["weaponItemType"] = null,
                ["searchItemType"] = null
            };

            try {
                Type api = FindSearchItemsApiType();
                Type searchItem = FindSearchItemType();
                Type firearm = FindTypeByName("FirearmItem");
                Type weapon = FindTypeByName("WeaponItem");
                result["searchItemsApi"] = api != null;
                result["searchItemsApiType"] = api?.FullName;
                result["searchItemType"] = searchItem?.FullName;
                result["firearmItemType"] = firearm?.FullName;
                result["weaponItemType"] = weapon?.FullName;
                result["pedWriteMethods"] = JArray.FromObject(GetMethodSummaries(api, "Ped"));
                result["vehicleWriteMethods"] = JArray.FromObject(GetMethodSummaries(api, "Vehicle"));
                result["firearmMembers"] = JArray.FromObject(GetMemberSummaries(firearm ?? weapon ?? searchItem));

                if (SetupController.GetConfig().firearmDebugLogging)
                    Helper.Log("[Firearm] PR capabilities: " + result.ToString(Newtonsoft.Json.Formatting.None), false, Helper.LogSeverity.Info);
            } catch (Exception ex) {
                result["error"] = ex.Message;
                Helper.Log($"[Firearm] PR capability inspection failed: {ex.Message}", false, Helper.LogSeverity.Warning);
            }

            return result;
        }

        internal static bool TryApplyToPed(JObject payload, out string message) {
            message = null;
            if (!Main.usePR) {
                message = "Policing Redefined is not active.";
                return false;
            }

            Ped ped = ResolvePed(payload);
            if (ped == null || !ped.IsValid()) {
                message = "Target ped is not present in the game world.";
                return false;
            }

            object item = BuildSearchItem(payload, ped, null, out message);
            if (item == null) return false;
            bool ok = TryWriteSearchItem(ped, null, item, out message);
            if (ok) CaptureAfterWrite(ped, null, "Cloud firearm");
            return ok;
        }

        internal static bool TryApplyToVehicle(JObject payload, out string message) {
            message = null;
            if (!Main.usePR) {
                message = "Policing Redefined is not active.";
                return false;
            }

            Vehicle vehicle = ResolveVehicle(payload);
            if (vehicle == null || !vehicle.Exists()) {
                message = "Target vehicle is not present in the game world.";
                return false;
            }

            object item = BuildSearchItem(payload, null, vehicle, out message);
            if (item == null) return false;
            bool ok = TryWriteSearchItem(null, vehicle, item, out message);
            if (ok) CaptureAfterWrite(null, vehicle, "Cloud firearm");
            return ok;
        }

        private static void CaptureAfterWrite(Ped ped, Vehicle vehicle, string source) {
            try {
                if (ped != null && ped.IsValid()) DataController.CaptureFirearmsFromPed(ped, source);
                if (vehicle != null && vehicle.Exists()) DataController.CaptureVehicleSearchItems(vehicle);
            } catch { }
        }

        private static Ped ResolvePed(JObject payload) {
            string pedName = ReadString(payload, "pedName", "ownerPedName", "ownerName", "owner", "name");
            if (string.IsNullOrWhiteSpace(pedName)) return null;

            MDTProPedData pedData = DataController.GetPedDataByName(pedName);
            if (pedData != null && pedData.Holder != null && pedData.Holder.IsValid())
                return pedData.Holder;

            Rage.PoolHandle? handle = DataController.GetRecentlyIdentifiedPedHandle(
                pedName,
                pedData?.Birthday,
                pedData != null && pedData.ModelHash != 0 ? (uint?)pedData.ModelHash : null);
            if (handle.HasValue) {
                try {
                    Ped ped = World.GetEntityByHandle<Ped>(handle.Value);
                    if (ped != null && ped.IsValid()) return ped;
                } catch { }
            }

            return null;
        }

        private static Vehicle ResolveVehicle(JObject payload) {
            string plate = ReadString(payload, "plate", "licensePlate", "LicensePlate");
            string normalized = DataController.NormalizeVehiclePlateKey(plate);
            if (string.IsNullOrEmpty(normalized) || Main.Player == null || !Main.Player.Exists()) return null;

            try {
                Vehicle[] nearby = Main.Player.GetNearbyVehicles(DataController.ClampRageNearbyPoolQueryCount(16));
                foreach (Vehicle vehicle in nearby ?? new Vehicle[0]) {
                    if (PlateMatches(vehicle, normalized)) return vehicle;
                }
                foreach (Vehicle vehicle in World.GetAllVehicles()) {
                    if (vehicle == null || !vehicle.Exists()) continue;
                    try {
                        if (Main.Player.DistanceTo(vehicle) > 95f) continue;
                    } catch { continue; }
                    if (PlateMatches(vehicle, normalized)) return vehicle;
                }
            } catch { }

            return null;
        }

        private static bool PlateMatches(Vehicle vehicle, string normalizedPlate) {
            if (vehicle == null || !vehicle.Exists()) return false;
            string plate = null;
            try { plate = vehicle.LicensePlate; } catch { }
            return !string.IsNullOrWhiteSpace(plate) && DataController.NormalizeVehiclePlateKey(plate) == normalizedPlate;
        }

        private static object BuildSearchItem(JObject payload, Ped ped, Vehicle vehicle, out string message) {
            message = null;
            Type baseType = FindSearchItemType();
            Type preferred = FindTypeByName("FirearmItem") ?? FindTypeByName("WeaponItem") ?? baseType;
            if (preferred == null) {
                message = "PR search item types were not found.";
                return null;
            }

            string label = BuildDisplayLabel(payload);
            object baseItem = baseType != null ? TryCreateBaseSearchItem(baseType, label) : null;
            object item = TryCreateSearchItem(preferred, baseItem, label, payload, ped, vehicle);
            if (item == null && baseItem != null) item = baseItem;
            if (item == null) {
                message = "Could not construct a PR firearm search item.";
                return null;
            }

            ApplyKnownProperties(item, label, payload);
            return item;
        }

        private static string BuildDisplayLabel(JObject payload) {
            string serial = ReadString(payload, "serialNumber", "SerialNumber", "serial", "Serial");
            bool scratched = ReadBool(payload, "isSerialScratched", "IsSerialScratched")
                || (!string.IsNullOrWhiteSpace(ReadString(payload, "status", "Status")) && ReadString(payload, "status", "Status").IndexOf("scratch", StringComparison.OrdinalIgnoreCase) >= 0);
            string id = ReadString(payload, "firearmId", "FirearmId", "id", "Id");
            string weapon = ReadString(payload, "weaponDisplayName", "WeaponDisplayName", "weaponType", "WeaponType", "weapon", "weaponModelId", "WeaponModelId");
            if (string.IsNullOrWhiteSpace(weapon)) weapon = "Firearm";

            string sn = scratched ? "Scratched" : (string.IsNullOrWhiteSpace(serial) ? "Unknown" : serial.Trim());
            string shortId = string.IsNullOrWhiteSpace(id) ? "" : $" | MDT ID: {id.Trim()}";
            return $"SN: {sn}{shortId} | {weapon.Trim()}";
        }

        private static object TryCreateBaseSearchItem(Type baseType, string label) {
            object item = null;
            try {
                ConstructorInfo ctor = baseType.GetConstructor(new[] { typeof(string) });
                if (ctor != null) item = ctor.Invoke(new object[] { label });
            } catch { }
            if (item == null) {
                try { item = Activator.CreateInstance(baseType); } catch { }
            }
            if (item != null) ApplyTextProperty(item, label);
            return item;
        }

        private static object TryCreateSearchItem(Type itemType, object baseItem, string label, JObject payload, Ped ped, Vehicle vehicle) {
            foreach (ConstructorInfo ctor in itemType.GetConstructors().OrderByDescending(c => c.GetParameters().Length)) {
                ParameterInfo[] parameters = ctor.GetParameters();
                object[] args = new object[parameters.Length];
                bool usable = true;
                for (int i = 0; i < parameters.Length; i++) {
                    if (!TryBuildConstructorArg(parameters[i], baseItem, label, payload, ped, vehicle, out args[i])) {
                        usable = false;
                        break;
                    }
                }
                if (!usable) continue;
                try { return ctor.Invoke(args); } catch { }
            }
            return null;
        }

        private static bool TryBuildConstructorArg(ParameterInfo parameter, object baseItem, string label, JObject payload, Ped ped, Vehicle vehicle, out object value) {
            value = null;
            Type t = parameter.ParameterType;
            string name = parameter.Name ?? "";
            if (baseItem != null && t.IsInstanceOfType(baseItem)) {
                value = baseItem;
                return true;
            }
            if (t == typeof(Ped)) {
                value = ped;
                return ped != null;
            }
            if (t == typeof(Vehicle)) {
                value = vehicle;
                return vehicle != null;
            }
            if (t == typeof(string)) {
                value = name.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0
                    ? (ReadString(payload, "weaponModelId", "WeaponModelId") ?? label)
                    : label;
                return true;
            }
            if (t == typeof(bool)) {
                value = name.IndexOf("visible", StringComparison.OrdinalIgnoreCase) >= 0
                    || ReadBool(payload, "isStolen", "IsStolen");
                return true;
            }
            if (t == typeof(int)) {
                value = unchecked((int)ReadLong(payload, "weaponModelHash", "WeaponModelHash"));
                return true;
            }
            if (t == typeof(uint)) {
                value = unchecked((uint)ReadLong(payload, "weaponModelHash", "WeaponModelHash"));
                return true;
            }
            if (t.IsEnum) {
                string wanted = ReadBool(payload, "isSerialScratched", "IsSerialScratched") ? "ScratchedSN" : "Normal";
                try {
                    value = Enum.Parse(t, wanted, true);
                    return true;
                } catch {
                    value = Enum.GetValues(t).GetValue(0);
                    return true;
                }
            }
            if (t.IsValueType) {
                value = Activator.CreateInstance(t);
                return true;
            }
            return !t.IsAbstract && TryCreateDefaultReference(t, out value);
        }

        private static bool TryCreateDefaultReference(Type type, out object value) {
            value = null;
            try {
                value = Activator.CreateInstance(type);
                return value != null;
            } catch {
                return false;
            }
        }

        private static bool TryWriteSearchItem(Ped ped, Vehicle vehicle, object item, out string message) {
            message = null;
            Type api = FindSearchItemsApiType();
            if (api == null) {
                message = "PR SearchItemsAPI was not found.";
                return false;
            }

            if (TryInvokeAdd(api, ped, vehicle, item)) {
                message = "Applied firearm search item.";
                return true;
            }

            if (TryInvokeOverwrite(api, ped, vehicle, item)) {
                message = "Applied firearm search item.";
                return true;
            }

            message = "No compatible PR search item write method was found.";
            return false;
        }

        private static bool TryInvokeAdd(Type api, Ped ped, Vehicle vehicle, object item) {
            string methodName = ped != null ? "AddCustomPedSearchItem" : "AddCustomVehicleSearchItem";
            object target = (object)ped ?? vehicle;
            foreach (MethodInfo method in api.GetMethods(PublicStatic).Where(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))) {
                ParameterInfo[] ps = method.GetParameters();
                if (ps.Length != 2 || !ps[0].ParameterType.IsInstanceOfType(target) || !ps[1].ParameterType.IsInstanceOfType(item)) continue;
                try {
                    method.Invoke(null, new[] { target, item });
                    return true;
                } catch { }
            }
            return false;
        }

        private static bool TryInvokeOverwrite(Type api, Ped ped, Vehicle vehicle, object item) {
            string getName = ped != null ? "GetPedSearchItems" : "GetVehicleSearchItems";
            string overwriteName = ped != null ? "OverwritePedSearchItems" : "OverwriteVehicleSearchItems";
            object target = (object)ped ?? vehicle;
            MethodInfo getter = api.GetMethod(getName, PublicStatic, null, new[] { target.GetType() }, null);
            object existing = null;
            try { existing = getter?.Invoke(null, new[] { target }); } catch { }

            foreach (MethodInfo method in api.GetMethods(PublicStatic).Where(m => m.Name.Equals(overwriteName, StringComparison.OrdinalIgnoreCase))) {
                ParameterInfo[] ps = method.GetParameters();
                if (ps.Length != 2 || !ps[0].ParameterType.IsInstanceOfType(target)) continue;
                object list = BuildTypedList(ps[1].ParameterType, existing as IEnumerable, item);
                if (list == null) continue;
                try {
                    method.Invoke(null, new[] { target, list });
                    return true;
                } catch { }
            }
            return false;
        }

        private static object BuildTypedList(Type listType, IEnumerable existing, object item) {
            Type itemType = listType.IsArray ? listType.GetElementType() : (listType.IsGenericType ? listType.GetGenericArguments().FirstOrDefault() : null);
            if (itemType == null || !itemType.IsInstanceOfType(item)) return null;
            IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
            if (existing != null) {
                foreach (object existingItem in existing) {
                    if (existingItem != null && itemType.IsInstanceOfType(existingItem))
                        list.Add(existingItem);
                }
            }
            list.Add(item);
            if (listType.IsArray) {
                Array arr = Array.CreateInstance(itemType, list.Count);
                list.CopyTo(arr, 0);
                return arr;
            }
            if (listType.IsInstanceOfType(list))
                return list;
            return TryCreateCompatibleCollection(listType, itemType, list);
        }

        private static object TryCreateCompatibleCollection(Type listType, Type itemType, IList list) {
            if (listType == null || itemType == null || list == null || listType.IsInterface || listType.IsAbstract)
                return null;

            foreach (ConstructorInfo ctor in listType.GetConstructors()) {
                ParameterInfo[] ps = ctor.GetParameters();
                if (ps.Length != 1 || !ps[0].ParameterType.IsInstanceOfType(list)) continue;
                try {
                    object created = ctor.Invoke(new object[] { list });
                    if (listType.IsInstanceOfType(created))
                        return created;
                } catch { }
            }

            object target;
            try {
                target = Activator.CreateInstance(listType);
            } catch {
                return null;
            }

            if (target is IList nonGenericList) {
                foreach (object value in list)
                    nonGenericList.Add(value);
                return listType.IsInstanceOfType(target) ? target : null;
            }

            foreach (MethodInfo add in listType.GetMethods(PublicInstance).Where(m => m.Name.Equals("Add", StringComparison.OrdinalIgnoreCase))) {
                ParameterInfo[] ps = add.GetParameters();
                if (ps.Length != 1 || !ps[0].ParameterType.IsAssignableFrom(itemType)) continue;
                try {
                    foreach (object value in list)
                        add.Invoke(target, new[] { value });
                    return listType.IsInstanceOfType(target) ? target : null;
                } catch { }
            }

            return null;
        }

        private static void ApplyKnownProperties(object item, string label, JObject payload) {
            ApplyTextProperty(item, label);
            SetPropertyIfExists(item, "IsStolen", ReadBool(payload, "isStolen", "IsStolen"));
            SetPropertyIfExists(item, "WeaponModelId", ReadString(payload, "weaponModelId", "WeaponModelId"));
            long hash = ReadLong(payload, "weaponModelHash", "WeaponModelHash");
            if (hash != 0) {
                SetPropertyIfExists(item, "WeaponModelHash", hash);
                SetPropertyIfExists(item, "ModelHash", hash);
            }
            if (ReadBool(payload, "isSerialScratched", "IsSerialScratched"))
                SetEnumPropertyIfExists(item, "FirearmState", "ScratchedSN");
            else
                SetEnumPropertyIfExists(item, "FirearmState", "Normal");
        }

        private static void ApplyTextProperty(object item, string label) {
            SetPropertyIfExists(item, "Value", label);
            SetPropertyIfExists(item, "Description", label);
            SetPropertyIfExists(item, "Name", label);
            SetPropertyIfExists(item, "DisplayName", label);
        }

        private static void SetPropertyIfExists(object item, string propertyName, object value) {
            if (item == null || value == null) return;
            PropertyInfo prop = item.GetType().GetProperty(propertyName, PublicInstance);
            if (prop == null || !prop.CanWrite) return;
            try {
                object converted = value;
                if (prop.PropertyType == typeof(int)) converted = unchecked((int)Convert.ToInt64(value));
                else if (prop.PropertyType == typeof(uint)) converted = unchecked((uint)Convert.ToInt64(value));
                else if (prop.PropertyType == typeof(string)) converted = value.ToString();
                else if (prop.PropertyType == typeof(bool)) converted = Convert.ToBoolean(value);
                prop.SetValue(item, converted);
            } catch { }
        }

        private static void SetEnumPropertyIfExists(object item, string propertyName, string enumValue) {
            if (item == null) return;
            PropertyInfo prop = item.GetType().GetProperty(propertyName, PublicInstance) ?? item.GetType().GetProperty("State", PublicInstance);
            if (prop == null || !prop.CanWrite || !prop.PropertyType.IsEnum) return;
            try {
                prop.SetValue(item, Enum.Parse(prop.PropertyType, enumValue, true));
            } catch { }
        }

        private static Type FindSearchItemsApiType() {
            return Type.GetType("PolicingRedefined.API.SearchItemsAPI, PolicingRedefined")
                ?? Type.GetType("PolicingRedefined.API.SearchItemAPI, PolicingRedefined")
                ?? Type.GetType("PolicingRedefined.Interaction.Assets.SearchItemsAPI, PolicingRedefined")
                ?? ModIntegration.FindTypeInLoadedAssemblies("PolicingRedefined.API.SearchItemsAPI")
                ?? ModIntegration.FindTypeInLoadedAssemblies("PolicingRedefined.Interaction.Assets.SearchItemsAPI");
        }

        private static Type FindSearchItemType() {
            return FindTypeByName("SearchItem");
        }

        private static Type FindTypeByName(string typeName) {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm.IsDynamic) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (Type type in types) {
                    if (type.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)
                        && (type.FullName ?? "").IndexOf("PolicingRedefined", StringComparison.OrdinalIgnoreCase) >= 0)
                        return type;
                }
            }
            return null;
        }

        private static List<string> GetMethodSummaries(Type api, string target) {
            var rows = new List<string>();
            if (api == null) return rows;
            foreach (MethodInfo method in api.GetMethods(PublicStatic)) {
                if (method.Name.IndexOf(target, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (method.Name.IndexOf("Search", StringComparison.OrdinalIgnoreCase) < 0) continue;
                rows.Add(method.Name + "(" + string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
            }
            return rows;
        }

        private static List<string> GetMemberSummaries(Type type) {
            var rows = new List<string>();
            if (type == null) return rows;
            rows.AddRange(type.GetConstructors().Select(c => type.Name + "(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")"));
            rows.AddRange(type.GetProperties(PublicInstance).Select(p => "Property " + p.PropertyType.Name + " " + p.Name));
            rows.AddRange(type.GetFields(PublicInstance).Select(f => "Field " + f.FieldType.Name + " " + f.Name));
            return rows;
        }

        private static string ReadString(JObject payload, params string[] names) {
            if (payload == null) return null;
            foreach (string name in names) {
                JToken token = payload[name];
                if (token == null || token.Type == JTokenType.Null) continue;
                string value = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return null;
        }

        private static bool ReadBool(JObject payload, params string[] names) {
            string value = ReadString(payload, names);
            return bool.TryParse(value, out bool parsed) && parsed;
        }

        private static long ReadLong(JObject payload, params string[] names) {
            string value = ReadString(payload, names);
            return long.TryParse(value, out long parsed) ? parsed : 0L;
        }
    }
}
