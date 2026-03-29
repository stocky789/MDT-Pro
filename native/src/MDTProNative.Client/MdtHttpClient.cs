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
        _http = new HttpClient { BaseAddress = new Uri(endpoint.HttpBaseUrl + "/"), Timeout = TimeSpan.FromSeconds(30) };
    }

    public MdtServerEndpoint Endpoint { get; }

    public void Dispose() => _http.Dispose();

    /// <summary>Game time string; server returns <b>text/plain</b> (not JSON).</summary>
    public async Task<string?> GetCurrentTimePlainAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("data/currentTime", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<JObject?> GetConfigJsonAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("config", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JObject.Parse(text);
    }

    public async Task<JToken?> GetDataJsonAsync(string dataPath, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("data/" + dataPath.TrimStart('/'), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
