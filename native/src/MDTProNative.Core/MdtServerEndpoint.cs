namespace MDTProNative.Core;

/// <summary>MDT Pro plugin HTTP/WebSocket listen address (same as browser MDT).</summary>
public sealed record MdtServerEndpoint(string Host, int Port)
{
    public string HttpBaseUrl => $"http://{Host}:{Port}";
    public string WebSocketUrl => $"ws://{Host}:{Port}/ws";
}
