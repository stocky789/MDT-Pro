using System.Net.WebSockets;
using System.Text;
using MDTProNative.Core;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Client;

/// <summary>Matches MDT Pro plugin WebSocket text protocol (<c>/ws</c>).</summary>
public sealed class MdtWebSocketSession : IAsyncDisposable
{
    readonly MdtServerEndpoint _endpoint;
    ClientWebSocket? _socket;
    CancellationTokenSource? _runCts;
    Task? _receiveTask;

    public MdtWebSocketSession(MdtServerEndpoint endpoint) => _endpoint = endpoint;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public event Action<string, JToken?>? MessageReceived;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(_endpoint.WebSocketUrl), cancellationToken).ConfigureAwait(false);
        _receiveTask = RunReceiveLoopAsync(_runCts.Token);
    }

    public async Task SendRawAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_socket?.State != WebSocketState.Open) throw new InvalidOperationException("WebSocket is not connected.");
        var bytes = Encoding.UTF8.GetBytes(text);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    async Task RunReceiveLoopAsync(CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        var socket = _socket;
        if (socket == null) return;
        try
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray()).Trim();
                if (string.IsNullOrEmpty(json)) continue;

                string? request = null;
                JToken? response = null;
                try
                {
                    var obj = JObject.Parse(json);
                    request = obj["request"]?.ToString();
                    response = obj["response"];
                }
                catch
                {
                    request = "(parse error)";
                    response = null;
                }

                MessageReceived?.Invoke(request ?? "", response);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    public async Task DisconnectAsync()
    {
        if (_runCts != null)
        {
            try { _runCts.Cancel(); } catch { }
            _runCts.Dispose();
            _runCts = null;
        }
        if (_receiveTask != null)
        {
            try { await _receiveTask.ConfigureAwait(false); } catch { }
            _receiveTask = null;
        }
        if (_socket != null)
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
            _socket.Dispose();
            _socket = null;
        }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
