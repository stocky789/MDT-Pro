using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using MDTProNative.Core;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Client;

/// <summary>Read-only HTTP access to MDT Pro <c>/data/*</c> and root JSON routes.</summary>
public sealed class MdtHttpClient : IDisposable
{
    readonly HttpClient _http;

    public MdtHttpClient(MdtServerEndpoint endpoint)
    {
        Endpoint = endpoint;
        var handler = new HttpClientHandler();
        // Corporate/system proxy often breaks localhost; browser MDT still works because it is same-origin in the game page.
        if (IsLikelyLoopbackHost(endpoint.Host))
            handler.UseProxy = false;
        var baseUrl = endpoint.HttpBaseUrl.TrimEnd('/') + "/";
        // Plugin may block on the game fiber while GTA is paused; align with server-side infinite-wait saves.
        _http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(5) };
    }

    static bool IsLikelyLoopbackHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)) return true;
        return host is "::1" or "[::1]";
    }

    public MdtServerEndpoint Endpoint { get; }

    public void Dispose() => _http.Dispose();

    /// <summary>GET binary asset when present (e.g. bundled ped portrait <c>image/peds/mp_m_freemode_01.webp</c>); returns null on 404 or failure.</summary>
    public async Task<byte[]?> GetOptionalBytesAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var rel = relativePath.Trim().Replace('\\', '/').TrimStart('/');
        if (rel.Length == 0 || rel.Contains("..", StringComparison.Ordinal))
            return null;
        try
        {
            using var response = await _http.GetAsync(rel, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Game time string; server returns <b>text/plain</b> (not JSON).</summary>
    public async Task<string?> GetCurrentTimePlainAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("data/currentTime", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetVersionPlainAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("version", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
    }

    public async Task<JObject?> GetIntegrationJsonAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("integration", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JObject.Parse(text);
    }

    public async Task<JObject?> GetConfigJsonAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("config", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JObject.Parse(text);
    }

    public async Task<JObject?> GetLanguageJsonAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("language", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JObject.Parse(text);
    }

    /// <summary>Property/evidence receipt dropdowns (drug types, quantities, firearm types); same payload as browser <c>/seizureOptions</c>.</summary>
    public async Task<JObject?> GetSeizureOptionsJsonAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("seizureOptions", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JObject.Parse(text);
    }

    /// <summary>Arrest report charge groups (same root array as browser <c>/arrestOptions</c>).</summary>
    public async Task<JArray?> GetArrestOptionsJsonAsync(CancellationToken cancellationToken = default)
    {
        var remote = await TryGetChargeOptionsRootAsync("arrestOptions", cancellationToken).ConfigureAwait(false);
        if (remote is { Count: > 0 })
            return remote;
        return MdtEmbeddedChargeOptions.LoadArrestRoot();
    }

    /// <summary>Citation report charge groups (same root array as browser <c>/citationOptions</c>).</summary>
    public async Task<JArray?> GetCitationOptionsJsonAsync(CancellationToken cancellationToken = default)
    {
        var remote = await TryGetChargeOptionsRootAsync("citationOptions", cancellationToken).ConfigureAwait(false);
        if (remote is { Count: > 0 })
            return remote;
        return MdtEmbeddedChargeOptions.LoadCitationRoot();
    }

    static JArray? ParseChargeOptionsRoot(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        JToken t;
        try
        {
            t = JToken.Parse(text);
        }
        catch
        {
            return null;
        }

        if (t.Type == JTokenType.Null) return null;
        return t as JArray;
    }

    async Task<JArray?> TryGetChargeOptionsRootAsync(string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseChargeOptionsRoot(text);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Plugin static image (e.g. <c>DepartmentStyling/image/lssd-badge.png</c>).</summary>
    public async Task<byte[]?> GetPluginImageBytesAsync(string pluginId, string imageFileName, CancellationToken cancellationToken = default)
    {
        var name = (imageFileName ?? "").Trim().Replace("\\", "/");
        if (name.Length == 0 || name.Contains("..", StringComparison.Ordinal))
            return null;
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            name += ".png";
        var rel = "plugin/" + pluginId.Trim('/') + "/image/" + name;
        using var response = await _http.GetAsync(rel, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<JToken?> GetDataJsonAsync(string dataPath, CancellationToken cancellationToken = default)
    {
        var rel = "data/" + dataPath.TrimStart('/');
        using var response = await _http.GetAsync(rel, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var snippet = text.Length > 240 ? text[..240] + "…" : text;
            throw new HttpRequestException($"GET {rel} failed: HTTP {(int)response.StatusCode}. {snippet}");
        }

        return string.IsNullOrWhiteSpace(text) ? null : JToken.Parse(text);
    }

    /// <summary>POST with JSON body (same pattern as browser MDT for many <c>/data/*</c> routes).</summary>
    public async Task<(HttpStatusCode Status, string Body)> PostAsync(string relativePath, string? body, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(body ?? "", Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(relativePath.TrimStart('/'), content, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return (response.StatusCode, text);
    }

    /// <summary>POST <c>/data/nearbyVehicles</c>; reads <c>X-MdtPro-Nearby-Scan: deferred</c> when the plugin game fiber did not finish (GTA V often paused while alt-tabbed).</summary>
    public async Task<(HttpStatusCode Status, string Body, bool NearbyVehicleScanDeferred)> PostNearbyVehiclesAsync(int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 20);
        using var content = new StringContent(limit.ToString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("data/nearbyVehicles", content, cancellationToken).ConfigureAwait(false);
        var deferred = false;
        if (response.Headers.TryGetValues("X-MdtPro-Nearby-Scan", out var vals))
            deferred = vals.Any(v => string.Equals((v ?? "").Trim(), "deferred", StringComparison.OrdinalIgnoreCase));
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return (response.StatusCode, text, deferred);
    }

    public async Task<JToken?> PostForJsonAsync(string relativePath, string? body, CancellationToken cancellationToken = default)
    {
        var (status, text) = await PostAsync(relativePath, body, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.TrimStart();
        if (t.StartsWith('{') || t.StartsWith('[')) return JToken.Parse(text);
        _ = status;
        return null;
    }

    /// <summary><c>/post/*</c> mutations; response may be plain text or JSON.</summary>
    public async Task<(HttpStatusCode Status, string Body)> PostActionAsync(string postPath, string? jsonBody, CancellationToken cancellationToken = default)
    {
        var path = "post/" + postPath.TrimStart('/');
        return await PostAsync(path, jsonBody, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JToken?> PostJsonAsync(string relativePath, string? body, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(body ?? "", Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(relativePath.TrimStart('/'), content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JToken.Parse(text);
    }
}
