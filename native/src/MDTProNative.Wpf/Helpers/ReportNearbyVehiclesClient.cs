using System.Net;
using MDTProNative.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Helpers;

/// <summary>HTTP helpers for native report prefills (same routes as vehicle search).</summary>
public static class ReportNearbyVehiclesClient
{
    public sealed record NearbySummary(string Plate, string? ModelDisplay, double? DistanceMeters, bool Stolen);

    public static async Task<IReadOnlyList<NearbySummary>> FetchNearbyAsync(
        MdtHttpClient http,
        int limit = 12,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 20);
        var (status, text) = await http.PostAsync("data/nearbyVehicles", limit.ToString(), cancellationToken).ConfigureAwait(false);
        var list = new List<NearbySummary>();
        if (status != HttpStatusCode.OK || string.IsNullOrWhiteSpace(text)) return list;
        var trimmed = text.Trim();
        if (!trimmed.StartsWith('[')) return list;
        var arr = JArray.Parse(trimmed);
        foreach (var t in arr)
        {
            if (t is not JObject o) continue;
            var plate = PickStr(o, "LicensePlate", "licensePlate");
            if (string.IsNullOrWhiteSpace(plate)) continue;
            list.Add(new NearbySummary(
                plate.Trim(),
                PickStr(o, "ModelDisplayName", "modelDisplayName"),
                PickDouble(o, "Distance", "distance"),
                PickBool(o, "IsStolen", "isStolen")));
        }

        return list;
    }

    /// <summary>Resolves full vehicle row; use plate, VIN, or <c>context</c> / <c>current</c> for Stop The Ped / in-game context vehicle.</summary>
    public static async Task<JObject?> FetchSpecificVehicleAsync(
        MdtHttpClient http,
        string plateOrContextToken,
        CancellationToken cancellationToken = default)
    {
        var key = (plateOrContextToken ?? "").Trim();
        if (key.Length == 0) return null;
        var (status, text) = await http
            .PostAsync("data/specificVehicle", JsonConvert.SerializeObject(key), cancellationToken)
            .ConfigureAwait(false);
        if (status != HttpStatusCode.OK || string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        if (trimmed.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
        if (!trimmed.StartsWith('{')) return null;
        try
        {
            return JObject.Parse(text);
        }
        catch
        {
            return null;
        }
    }

    static string? PickStr(JObject o, params string[] keys)
    {
        foreach (var k in keys)
        {
            var t = o[k];
            if (t == null || t.Type == JTokenType.Null) continue;
            var s = t.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }

        return null;
    }

    static double? PickDouble(JObject o, params string[] keys)
    {
        foreach (var k in keys)
        {
            var t = o[k];
            if (t == null || t.Type == JTokenType.Null) continue;
            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer)
                return t.Value<double>();
            if (double.TryParse(t.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
        }

        return null;
    }

    static bool PickBool(JObject o, params string[] keys)
    {
        foreach (var k in keys)
        {
            var t = o[k];
            if (t == null || t.Type == JTokenType.Null) continue;
            if (t.Type == JTokenType.Boolean) return t.Value<bool>();
            if (bool.TryParse(t.ToString(), out var b)) return b;
        }

        return false;
    }
}
