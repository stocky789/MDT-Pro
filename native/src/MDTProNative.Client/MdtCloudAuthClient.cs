using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Client;

public sealed class MdtCloudAuthClient : IDisposable
{
    readonly HttpClient _http;

    public MdtCloudAuthClient(string baseUrl = "https://mdt.stockhosting.com.au")
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public void Dispose() => _http.Dispose();

    public async Task<JObject> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var body = JsonConvert.SerializeObject(new { email, password });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("api/auth/login", content, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JObject.Parse(text);
    }

    public async Task<JObject> SignupAsync(string email, string password, string officerFirstName, string officerLastName, string rank, CancellationToken cancellationToken = default)
    {
        var body = JsonConvert.SerializeObject(new { email, password, officerFirstName, officerLastName, rank });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("api/auth/signup", content, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JObject.Parse(text);
    }

    public async Task<JObject> LinkDeviceAsync(string accessToken, string installId, string deviceName, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/devices/link");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = new StringContent(JsonConvert.SerializeObject(new { installId, deviceName }), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JObject.Parse(text);
    }
}
