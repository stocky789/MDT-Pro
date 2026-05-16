using MDTPro.Setup;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace MDTPro.ServerAPI {
    internal static class BridgeSecurity {
        internal const string TokenHeaderName = "X-MDT-Bridge-Token";
        internal const string LegacyTokenHeaderName = "X-MdtPro-Bridge-Token";
        internal const string CookieName = "MDTProBridge";

        internal static bool IsProtectedHttpPath(string path) {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return path.Equals("/config", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/integration", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/data/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/post/", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsUnsafeMethod(string method) =>
            !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase);

        internal static string EnsureToken() {
            try {
                Directory.CreateDirectory(SetupController.DataPath);
                if (File.Exists(SetupController.BridgeAuthTokenPath)) {
                    string existing = File.ReadAllText(SetupController.BridgeAuthTokenPath).Trim();
                    if (existing.Length >= 32) return existing;
                }

                byte[] bytes = new byte[32];
                using (var rng = RandomNumberGenerator.Create()) {
                    rng.GetBytes(bytes);
                }
                string token = Convert.ToBase64String(bytes);
                File.WriteAllText(SetupController.BridgeAuthTokenPath, token);
                return token;
            } catch {
                byte[] bytes = new byte[32];
                using (var rng = RandomNumberGenerator.Create()) {
                    rng.GetBytes(bytes);
                }
                return Convert.ToBase64String(bytes);
            }
        }

        internal static bool IsAuthorized(HttpListenerRequest req, bool requireExplicitToken) {
            string configured = EnsureToken();
            if (string.IsNullOrWhiteSpace(configured)) return false;

            string supplied = req.Headers[TokenHeaderName];
            if (string.IsNullOrWhiteSpace(supplied))
                supplied = req.Headers[LegacyTokenHeaderName];
            if (string.IsNullOrWhiteSpace(supplied)) {
                string auth = req.Headers["Authorization"];
                if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    supplied = auth.Substring("Bearer ".Length).Trim();
            }
            if (string.IsNullOrWhiteSpace(supplied))
                supplied = req.QueryString["bridgeToken"];
            if (string.IsNullOrWhiteSpace(supplied))
                supplied = req.QueryString["bridgeAuth"];
            if (!requireExplicitToken && string.IsNullOrWhiteSpace(supplied))
                supplied = req.Cookies[CookieName]?.Value;

            return FixedTimeEquals(configured, supplied);
        }

        internal static bool HasSameOrigin(HttpListenerRequest req) {
            string origin = req.Headers["Origin"];
            if (string.IsNullOrWhiteSpace(origin)) return true;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri)) return false;
            return string.Equals(originUri.Host, req.Url.Host, StringComparison.OrdinalIgnoreCase)
                && originUri.Port == req.Url.Port;
        }

        internal static void AppendBridgeCookie(HttpListenerResponse res) {
            string token = EnsureToken();
            if (string.IsNullOrWhiteSpace(token)) return;
            res.Headers["Set-Cookie"] = $"{CookieName}={Uri.EscapeDataString(token)}; Path=/; SameSite=Strict";
        }

        internal static void AppendBootstrapHeaders(HttpListenerRequest req, HttpListenerResponse res) {
            string token = EnsureToken();
            if (string.IsNullOrWhiteSpace(token)) return;
            res.Headers["X-MdtPro-Bridge-Token"] = token;
            if (IsLikelyLoopback(req))
                res.Headers[TokenHeaderName] = token;
        }

        internal static byte[] UnauthorizedBody() =>
            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "MDT bridge token is required." }));

        internal static string SafeConfigJson() {
            var cfg = JObject.FromObject(SetupController.GetConfig());
            foreach (var key in SensitiveConfigKeys)
                cfg.Remove(key);
            return cfg.ToString(Formatting.None);
        }

        static readonly string[] SensitiveConfigKeys = {
            "bridgeAuthToken",
            "cloudAccessToken",
            "cloudRefreshToken",
            "cloudInstallId",
            "cloudApiBaseUrl"
        };

        static bool IsLikelyLoopback(HttpListenerRequest req) {
            try {
                return IPAddress.IsLoopback(req.RemoteEndPoint.Address);
            } catch {
                return false;
            }
        }

        static bool FixedTimeEquals(string expected, string actual) {
            if (expected == null || actual == null) return false;
            var a = Encoding.UTF8.GetBytes(expected);
            var b = Encoding.UTF8.GetBytes(actual);
            int diff = a.Length ^ b.Length;
            int len = Math.Min(a.Length, b.Length);
            for (int i = 0; i < len; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
