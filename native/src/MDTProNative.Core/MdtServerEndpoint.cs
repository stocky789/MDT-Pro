namespace MDTProNative.Core;

/// <summary>MDT Pro plugin HTTP/WebSocket listen address (same as browser MDT).</summary>
public sealed record MdtServerEndpoint(string Host, int Port, string? BridgeAuthToken = null)
{
    public string HttpBaseUrl => $"http://{Host}:{Port}";
    public string WebSocketUrl => string.IsNullOrWhiteSpace(BridgeAuthToken)
        ? $"ws://{Host}:{Port}/ws"
        : $"ws://{Host}:{Port}/ws?bridgeAuth={Uri.EscapeDataString(BridgeAuthToken)}";

    /// <summary>Absolute URL for a served path (e.g. <c>page/map.html</c>).</summary>
    public string HttpUrl(string path) => $"{HttpBaseUrl}/{path.TrimStart('/')}";
}
