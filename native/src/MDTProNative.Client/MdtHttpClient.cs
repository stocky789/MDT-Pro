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

    public async Task<JToken?> PostJsonAsync(string relativePath, string? body, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(body ?? "", Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(relativePath.TrimStart('/'), content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JToken.Parse(text);
    }
}
