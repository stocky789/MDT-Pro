namespace MDTProNative.Core;

/// <summary>MDT Pro plugin HTTP/WebSocket listen address (same as browser MDT).</summary>
public sealed record MdtServerEndpoint(string Host, int Port)
{
    public string HttpBaseUrl => $"http://{Host}:{Port}";
    public string WebSocketUrl => $"ws://{Host}:{Port}/ws";

    /// <summary>Absolute URL for a served path (e.g. <c>page/map.html</c>).</summary>
    public string HttpUrl(string path) => $"{HttpBaseUrl}/{path.TrimStart('/')}";
}
