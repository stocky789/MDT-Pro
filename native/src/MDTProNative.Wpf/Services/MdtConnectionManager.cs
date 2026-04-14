using System.Net.Http;
using System.Windows.Threading;
using MDTProNative.Client;
using MDTProNative.Core;
using MDTProNative.Wpf.Helpers;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Services;

/// <summary>Shared HTTP + WebSocket session for all native views (browser MDT unchanged).</summary>
public sealed class MdtConnectionManager : IAsyncDisposable
{
    readonly Dispatcher _dispatcher;
    MdtHttpClient? _http;
    MdtWebSocketSession? _wsTime;
    MdtWebSocketSession? _wsLocation;
    MdtWebSocketSession? _wsCallouts;
    /// <summary>Last <c>calloutEvent</c> payload so views that subscribe after navigation still populate (deep clone; safe to hold).</summary>
    JArray? _lastCalloutsSnapshot;
    int _lastCalloutCount;
    string? _lastCadUnitStatus;

    public MdtConnectionManager(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public MdtServerEndpoint? Endpoint { get; private set; }
    public MdtHttpClient? Http => _http;
    public bool IsConnected => _http != null;

    public event Action<string>? TimeUpdated;
    public event Action<string>? LocationUpdated;
    /// <param name="cadUnitStatus">CAD / unit line from plugin (<c>cadUnitStatus</c> on WebSocket payload).</param>
    public event Action<JArray?, int, string?>? CalloutsUpdated;
    public event Action<string>? Log;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        var endpoint = new MdtServerEndpoint(host, port);
        Endpoint = endpoint;
        _http = new MdtHttpClient(endpoint);

        _ = await _http.GetCurrentTimePlainAsync(cancellationToken).ConfigureAwait(false);
        EmitLog("HTTP: connected");

        _wsTime = new MdtWebSocketSession(endpoint);
        _wsTime.MessageReceived += OnTimeMessage;
        await _wsTime.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _wsTime.SendRawAsync("interval/time", cancellationToken).ConfigureAwait(false);

        _wsLocation = new MdtWebSocketSession(endpoint);
        _wsLocation.MessageReceived += OnLocationMessage;
        await _wsLocation.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _wsLocation.SendRawAsync("interval/playerLocation", cancellationToken).ConfigureAwait(false);

        _wsCallouts = new MdtWebSocketSession(endpoint);
        _wsCallouts.MessageReceived += OnCalloutMessage;
        await _wsCallouts.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _wsCallouts.SendRawAsync("calloutEvent", cancellationToken).ConfigureAwait(false);
    }

    void OnTimeMessage(string request, JToken? response)
    {
        if (request != "time") return;
        var s = response?.ToString().Trim('"') ?? "—";
        _dispatcher.BeginInvoke(() => TimeUpdated?.Invoke(s));
    }

    void OnLocationMessage(string request, JToken? response)
    {
        if (request != "playerLocation") return;
        var line = response != null ? JTokenDisplay.FormatLocation(response) : "—";
        if (string.IsNullOrWhiteSpace(line)) line = "—";
        _dispatcher.BeginInvoke(() => LocationUpdated?.Invoke(line));
    }

    void OnCalloutMessage(string request, JToken? response)
    {
        if (request != "calloutEvent" || response is not JObject root) return;
        var list = root["callouts"] as JArray;
        var count = list?.Count ?? 0;
        var cad = root["cadUnitStatus"]?.ToString();
        if (string.IsNullOrWhiteSpace(cad)) cad = null;
        _lastCalloutsSnapshot = list == null ? null : (JArray)list.DeepClone();
        _lastCalloutCount = count;
        _lastCadUnitStatus = cad;
        _dispatcher.BeginInvoke(() => CalloutsUpdated?.Invoke(list, count, cad));
        EmitLog($"calloutEvent: {count} active");
    }

    /// <summary>Replays the last callout WebSocket payload to <paramref name="handler"/> (e.g. Overview subscribes after the user was on another tab).</summary>
    public void ReplayLastCallouts(Action<JArray?, int, string?> handler)
    {
        if (_http == null) return;
        void Fire() => handler(_lastCalloutsSnapshot, _lastCalloutCount, _lastCadUnitStatus);
        if (_dispatcher.CheckAccess())
            Fire();
        else
            _dispatcher.Invoke(Fire);
    }

    void EmitLog(string line) => _dispatcher.BeginInvoke(() => Log?.Invoke(line));

    public async Task DisconnectAsync()
    {
        if (_wsTime != null) { _wsTime.MessageReceived -= OnTimeMessage; await DisposeWs(_wsTime); _wsTime = null; }
        if (_wsLocation != null) { _wsLocation.MessageReceived -= OnLocationMessage; await DisposeWs(_wsLocation); _wsLocation = null; }
        if (_wsCallouts != null) { _wsCallouts.MessageReceived -= OnCalloutMessage; await DisposeWs(_wsCallouts); _wsCallouts = null; }
        _http?.Dispose();
        _http = null;
        Endpoint = null;
        _lastCalloutsSnapshot = null;
        _lastCalloutCount = 0;
        _lastCadUnitStatus = null;
        _ = _dispatcher.BeginInvoke(() =>
        {
            TimeUpdated?.Invoke("—");
            LocationUpdated?.Invoke("—");
            CalloutsUpdated?.Invoke(null, 0, null);
        });
    }

    static async Task DisposeWs(MdtWebSocketSession? ws)
    {
        if (ws == null) return;
        try { await ws.DisposeAsync().ConfigureAwait(false); } catch { }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
