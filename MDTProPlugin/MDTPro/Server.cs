using MDTPro.ServerAPI;
using Rage;
using System;
using System.IO;
using System.Net;
using System.Threading;
using static MDTPro.Utility.Helper;

namespace MDTPro {
    internal class Server {
        internal static volatile bool RunServer;

        private static HttpListener listener;

        /// <summary>HTTP server thread entry. Call <see cref="Stop"/> from the game side before starting a new thread so the prior listener releases its port.</summary>
        internal static void Start() {
            // Prior Stop() should have run on the game fiber; give Windows a moment to release the socket.
            Thread.Sleep(120);

            RunServer = true;
            var http = new HttpListener();
            http.Prefixes.Add($"http://+:{Setup.SetupController.GetConfig().port}/");
            listener = http;
            try {
                http.Start();
            } catch (Exception ex) {
                Log($"Listening on Server failed — port may be in use or URL ACL missing. {ex.GetType().Name}: {ex.Message}", true, LogSeverity.Error);
                try {
                    http.Close();
                } catch {
                    /* ignore */
                }
                listener = null;
                RunServer = false;
                return;
            }
            string localIp = GetLocalIPAddress();
            if (string.IsNullOrEmpty(localIp)) localIp = "localhost";
            int port = Setup.SetupController.GetConfig().port;
            string fullIp = $"http://{localIp}:{port}";
            string fullName = $"http://{Environment.MachineName}:{port}";
            Log($"Listening on: {fullIp}");
            Log($"Listening on: {fullName}");
            File.WriteAllText(Setup.SetupController.IpAddressesPath, $"{fullIp}\n{fullName}");
            if (Setup.SetupController.GetConfig().showListeningAddressNotification)
                Utility.RageNotification.ShowAddressNotification(localIp, Environment.MachineName, port);

            while (RunServer) {
                try {
                    var ctxListener = listener;
                    if (ctxListener == null) break;
                    var context = ctxListener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                } catch (HttpListenerException) {
                    // Expected when Stop() calls HttpListener.Stop() — ends the accept loop.
                    break;
                } catch (ObjectDisposedException) {
                    break;
                } catch (InvalidOperationException) {
                    break;
                } catch (Exception e) {
                    if (RunServer) Log($"Server Exception: {e.Message}", true, LogSeverity.Error);
                }
            }

            try {
                listener?.Close();
            } catch {
                /* ignore */
            }
            listener = null;
        }

        private static void HandleRequest(HttpListenerContext ctx) {
            if (ctx.Request.IsWebSocketRequest && ctx.Request.RawUrl == "/ws") {
                WebSocketHandler.HandleWebSocket(ctx);
                return;
            }

            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse res = ctx.Response;

            try {
                APIResponse apiRes = GetAPIResponse(req);

                byte[] buffer = apiRes.buffer;

                res.ContentType = apiRes.contentType;
                res.ContentLength64 = buffer.LongLength;
                res.StatusCode = apiRes.status;

                res.OutputStream.Write(buffer, 0, buffer.Length);
            } catch (Exception e) {
                Log($"HandleRequest exception: {e.Message}", true, LogSeverity.Error);
                try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] HandleRequest exception:\n{SanitizeExceptionForLog(e)}"); } catch { }
                try {
                    res.StatusCode = 500;
                    byte[] errBuffer = System.Text.Encoding.UTF8.GetBytes("Internal Server Error");
                    res.ContentLength64 = errBuffer.LongLength;
                    res.OutputStream.Write(errBuffer, 0, errBuffer.Length);
                } catch { }
            } finally {
                try { res.OutputStream.Close(); } catch { }
            }
        }

        /// <summary>Stops accepting HTTP; unblocks <see cref="HttpListener.GetContext"/>. Safe to call from the game fiber (synchronous, quick). WebSocket cleanup runs in the thread pool so we do not block on async.</summary>
        internal static void Stop() {
            RunServer = false;
            HttpListener http = listener;
            if (http != null) {
                try {
                    http.Stop();
                } catch {
                    /* ignore */
                }
                try {
                    http.Close();
                } catch {
                    /* ignore */
                }
            }
            listener = null;

            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    WebSocketHandler.CloseAllWebSockets().ConfigureAwait(false).GetAwaiter().GetResult();
                } catch {
                    /* ignore */
                }
            });
        }

        internal static APIResponse GetAPIResponse(HttpListenerRequest req) {
            string path = req.Url.AbsolutePath;
            if (path.StartsWith("/data/")) {
                return new DataAPIResponse(req);
            } else if (path.StartsWith("/post/")) {
                return new PostAPIResponse(req);
            } else if (path.StartsWith("/plugin/")) {
                return new PluginAPIResponse(req); 
            } else if (path.StartsWith("/page/")) {
                return new PageAPIResponse(req);
            } else if (path.StartsWith("/style/")) {
                return new StyleAPIResponse(req);
            } else if (path.StartsWith("/script/")) {
                return new ScriptAPIResponse(req);
            } else if (path.StartsWith("/image/")) {
                return new ImageAPIResponse(req);
            }
            return new APIResponse(req);
        }
    }
}