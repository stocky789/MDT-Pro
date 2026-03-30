using MDTPro.ALPR;
using MDTPro.Data;
using MDTPro.EventListeners;
using MDTPro.Setup;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static MDTPro.Setup.Language.Callout;
using static MDTPro.Utility.Helper;

namespace MDTPro.ServerAPI {
    internal class WebSocketHandler {
        private static readonly List<WebSocket> WebSockets = new List<WebSocket>();
        private static readonly HashSet<WebSocket> AlprSubscribers = new HashSet<WebSocket>();
        private static readonly HashSet<WebSocket> CalloutSubscribers = new HashSet<WebSocket>();
        private static readonly object WebSocketLock = new Object();
        private static readonly Dictionary<WebSocket, CancellationTokenSource> IntervalTokens = new Dictionary<WebSocket, CancellationTokenSource>();

        internal static async void HandleWebSocket(HttpListenerContext ctx) {
            WebSocket webSocket = null;
            Action shiftHistoryHandler = null;
            CalloutEvents.CalloutEventHandler calloutEventHandler = null;

            void UnsubscribeIfNeeded() {
                if (shiftHistoryHandler != null) {
                    DataController.ShiftHistoryUpdated -= shiftHistoryHandler;
                    shiftHistoryHandler = null;
                }
                if (calloutEventHandler != null) {
                    CalloutEvents.OnCalloutEvent -= calloutEventHandler;
                    calloutEventHandler = null;
                }
            }

            try {
                HttpListenerWebSocketContext wsContext = await ctx.AcceptWebSocketAsync(null);
                webSocket = wsContext.WebSocket;
                byte[] buffer = new byte[1024];

                lock (WebSocketLock) {
                    WebSockets.Add(webSocket);
                }

                if (SetupController.GetConfig().verboseFileLogging)
                    Log($"New WebSocket #{WebSockets.IndexOf(webSocket)}", false, LogSeverity.Info);

                while (webSocket.State == WebSocketState.Open && Server.RunServer) {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close) {
                        break;
                    }

                    string clientMsg = Encoding.UTF8.GetString(buffer, 0, result.Count).Trim();

                    if (clientMsg.StartsWith("interval/")) {
                        string intervalMsg = clientMsg.Substring("interval/".Length);
                        CancellationTokenSource cts = new CancellationTokenSource();
                        lock (WebSocketLock) {
                            IntervalTokens[webSocket] = cts;
                        }
                        await SendUpdatesOnInterval(webSocket, clientMsg.Substring("interval/".Length), cts.Token);
                    } else {
                        switch (clientMsg) {
                            case "ping":
                                await SendData(webSocket, "\"Pong!\"", clientMsg);
                                break;
                            case "alprSubscribe":
                                lock (WebSocketLock) { AlprSubscribers.Add(webSocket); }
                                await SendData(webSocket, "\"subscribed\"", clientMsg);
                                break;
                            case "shiftHistoryUpdated":
                                shiftHistoryHandler = () => {
                                    if (webSocket.State != WebSocketState.Open || !Server.RunServer) return;
                                    SendData(webSocket, "\"Shift history updated\"", clientMsg).Wait();
                                };
                                DataController.ShiftHistoryUpdated += shiftHistoryHandler;
                                break;
                            case "calloutEvent":
                                lock (WebSocketLock) { CalloutSubscribers.Add(webSocket); }
                                SendData(webSocket, BuildCalloutPayloadJson(), clientMsg).Wait();

                                calloutEventHandler = (calloutInfo) => {
                                    if (webSocket.State != WebSocketState.Open || !Server.RunServer) return;
                                    SendData(webSocket, BuildCalloutPayloadJson(), clientMsg).Wait();
                                };
                                CalloutEvents.OnCalloutEvent += calloutEventHandler;
                                break;
                            default:
                                await SendData(webSocket, $"\"Unknown command: '{clientMsg}'\"", clientMsg);
                                break;
                        }
                    }
                }
            } catch (Exception e) {
                if (Server.RunServer) Log($"WebSocket Error: {e.Message}", false, LogSeverity.Error);
            } finally {
                UnsubscribeIfNeeded();
                if (webSocket != null) {
                    lock (WebSocketLock) {
                        WebSockets.Remove(webSocket);
                        AlprSubscribers.Remove(webSocket);
                        CalloutSubscribers.Remove(webSocket);
                        if (IntervalTokens.TryGetValue(webSocket, out var cts)) {
                            try { cts.Cancel(); } catch { }
                            IntervalTokens.Remove(webSocket);
                        }
                    }
                    if (webSocket.State == WebSocketState.Open) {
                        try {
                            webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait(500);
                        } catch { }
                    }
                }
            }
        }

        private static Task SendUpdatesOnInterval(WebSocket webSocket, string clientMsg, CancellationToken token) {
            return Task.Run(async () => {
                string lastLocationJson = null;
                string lastTimeJson = null;
                string lastCoordsJson = null;
                try {
                    while (webSocket.State == WebSocketState.Open && Server.RunServer && !token.IsCancellationRequested) {
                        string responseMsg;
                        switch (clientMsg) {
                            case "playerLocation":
                                DataController.RefreshMdtLocationOnGameFiberBlocking(800);
                                responseMsg = JsonConvert.SerializeObject(DataController.MdtPreferredLocation);

                                if (responseMsg != lastLocationJson) {
                                    lastLocationJson = responseMsg;
                                    await SendData(webSocket, responseMsg, clientMsg, token);
                                }
                                break;
                            case "time":
                                responseMsg = $"\"{DataController.CurrentTime}\"";

                                if (responseMsg != lastTimeJson) {
                                    lastTimeJson = responseMsg;
                                    await SendData(webSocket, responseMsg, clientMsg, token);
                                }
                                break;
                            case "playerCoords":
                                responseMsg = JsonConvert.SerializeObject(DataController.PlayerCoords);

                                if (responseMsg != lastCoordsJson) {
                                    lastCoordsJson = responseMsg;
                                    await SendData(webSocket, responseMsg, clientMsg, token);
                                }
                                break;
                            default:
                                await SendData(webSocket, $"\"Unknown interval command: '{clientMsg}'\"", clientMsg, token);
                                return;
                        }

                        await Task.Delay(SetupController.GetConfig().webSocketUpdateInterval, token);
                    }
                } catch (OperationCanceledException) {
                } catch (WebSocketException wse) when (wse.InnerException?.Message.Contains("nonexistent network connection") ?? false) {
                    Log("WebSocket lost", false, LogSeverity.Warning);
                } catch (Exception e) {
                    string innerMessage = e.InnerException != null ? $"Inner: {e.InnerException.Message}" : "";
                    Log($"WebSocket Error on interval: {e.Message}{innerMessage}", false, LogSeverity.Error);
                }
            });
        }

        internal static string BuildCalloutPayloadJson() {
            return JsonConvert.SerializeObject(new {
                callouts = CalloutEvents.CalloutList,
                cadUnitStatus = CalloutEvents.CadUnitStatus ?? ""
            });
        }

        /// <summary>Pushes the latest callout list + CAD unit status to all <c>calloutEvent</c> subscribers (e.g. after <c>POST /post/cadUnitStatus</c>).</summary>
        internal static void BroadcastCalloutPayload() {
            string inner = BuildCalloutPayloadJson();
            string msg = $"{{\"response\":{inner},\"request\":\"calloutEvent\"}}";
            byte[] bytes = Encoding.UTF8.GetBytes(msg);
            lock (WebSocketLock) {
                foreach (var ws in new List<WebSocket>(CalloutSubscribers)) {
                    try {
                        if (ws.State == WebSocketState.Open)
                            ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                    } catch { }
                }
            }
        }

        private static async Task SendData(WebSocket webSocket, string data, string clientMsg, CancellationToken token = default) {
            string responseMsg = $"{{ \"response\": {data}, \"request\": \"{clientMsg}\" }}";

            await webSocket.SendAsync(
                new ArraySegment<byte>(Encoding.UTF8.GetBytes(responseMsg)),
                WebSocketMessageType.Text,
                true,
                token
            );
        }

        /// <summary>Broadcast ALPR hit to subscribed clients.</summary>
        internal static void BroadcastALPRHit(ALPRHit hit) {
            if (hit == null) return;
            string json = JsonConvert.SerializeObject(new {
                plate = hit.Plate,
                owner = hit.Owner,
                modelDisplayName = hit.ModelDisplayName,
                flags = hit.Flags ?? new System.Collections.Generic.List<string>(),
                timeScanned = hit.TimeScanned.ToString("o")
            });
            string msg = $"{{\"response\":{json},\"request\":\"alprSubscribe\"}}";
            byte[] bytes = Encoding.UTF8.GetBytes(msg);
            lock (WebSocketLock) {
                foreach (var ws in new List<WebSocket>(AlprSubscribers)) {
                    try {
                        if (ws.State == WebSocketState.Open)
                            ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                    } catch { }
                }
            }
        }

        internal static async Task CloseAllWebSockets() {
            WebSocket[] webSocketsArr;
            lock (WebSocketLock) {
                foreach (var cts in IntervalTokens.Values) {
                    cts.Cancel();
                }
                IntervalTokens.Clear();
                AlprSubscribers.Clear();
                CalloutSubscribers.Clear();

                webSocketsArr = WebSockets.ToArray();
                WebSockets.Clear();
            }

            foreach (WebSocket webSocket in webSocketsArr) {
                try {
                    if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived) {
                        if (SetupController.GetConfig().verboseFileLogging)
                            Log($"Closing WebSocket #{Array.IndexOf(webSocketsArr, webSocket)}", false, LogSeverity.Info);
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                } catch (Exception e) {
                    Log($"WebSocket close error: {e.Message}", false, LogSeverity.Warning);
                }
            }
        }
    }
}
