using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Helpers;

/// <summary>Parsed vehicle fields from <c>/data/specificVehicle</c> (MDT Pro + CDF).</summary>
public sealed record ReportMdtVehicleSnapshot(
    string? Plate,
    string? ModelDisplayName,
    string? Owner,
    string? Vin)
{
    public static ReportMdtVehicleSnapshot? FromVehicleJson(JToken? t)
    {
        if (t is not JObject o) return null;

        return new ReportMdtVehicleSnapshot(
            PickStr(o, "LicensePlate", "licensePlate"),
            PickStr(o, "ModelDisplayName", "modelDisplayName"),
            PickStr(o, "Owner", "owner"),
            PickStr(o, "VehicleIdentificationNumber", "vehicleIdentificationNumber", "Vin", "vin"));
    }

    static string? PickStr(JObject jo, params string[] keys)
    {
        foreach (var k in keys)
        {
            var x = jo[k];
            if (x == null || x.Type == JTokenType.Null) continue;
            var s = x.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
        }

        return null;
    }
}
