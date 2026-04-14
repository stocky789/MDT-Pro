using System.Net;
using MDTProNative.Client;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Helpers;

/// <summary>HTTP helpers for native report prefills (same routes as vehicle search).</summary>
public static class ReportNearbyVehiclesClient
{
    public sealed record NearbySummary(string Plate, string? ModelDisplay, double? DistanceMeters, bool Stolen);

    /// <param name="ScanDeferred">Host set <c>X-MdtPro-Nearby-Scan: deferred</c> — live world scan did not run (typical when GTA V is unfocused / paused).</param>
    public sealed record NearbyFetchResult(IReadOnlyList<NearbySummary> Items, bool ScanDeferred);

    public static async Task<NearbyFetchResult> FetchNearbyAsync(
        MdtHttpClient http,
        int limit = 8,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 20);
        var (status, text, scanDeferred) = await http.PostNearbyVehiclesAsync(limit, cancellationToken).ConfigureAwait(false);
        var list = new List<NearbySummary>();
        if (status != HttpStatusCode.OK || string.IsNullOrWhiteSpace(text))
        {
            if (status != HttpStatusCode.OK)
                MdtShellEvents.LogCad($"Nearby vehicles: HTTP {(int)status}.");
            return new NearbyFetchResult(list, false);
        }
        if (!TryParseNearbyVehiclesArray(text, out var arr) || arr == null)
        {
            var head = text.Trim();
            if (head.Length > 120) head = head[..120] + "…";
            MdtShellEvents.LogCad($"Nearby vehicles: expected JSON array from host; got: {head}");
            return new NearbyFetchResult(list, scanDeferred);
        }

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

        return new NearbyFetchResult(list, scanDeferred);
    }

    /// <summary>Parses <c>/data/nearbyVehicles</c> body: raw array, UTF-8 BOM, optional JSON string wrapper, or <c>{ data: [...] }</c>.</summary>
    internal static bool TryParseNearbyVehiclesArray(string text, out JArray? arr)
    {
        arr = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimStart('\uFEFF');
        try
        {
            var tok = JToken.Parse(t);
            tok = UnwrapJsonStringToken(tok);
            if (tok is JArray ja)
            {
                arr = ja;
                return true;
            }

            if (tok is JObject jobj)
            {
                if (jobj["data"] is JArray d)
                {
                    arr = d;
                    return true;
                }

                if (jobj["nearbyVehicles"] is JArray n)
                {
                    arr = n;
                    return true;
                }
            }
        }
        catch
        {
            /* caller logs */
        }

        return false;
    }

    static JToken UnwrapJsonStringToken(JToken tok)
    {
        if (tok.Type != JTokenType.String) return tok;
        var inner = tok.Value<string>();
        if (string.IsNullOrWhiteSpace(inner)) return tok;
        inner = inner.Trim();
        if (inner.Length < 2) return tok;
        if ((inner[0] == '[' && inner[^1] == ']') || (inner[0] == '{' && inner[^1] == '}'))
        {
            try
            {
                return JToken.Parse(inner);
            }
            catch
            {
                return tok;
            }
        }

        return tok;
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
