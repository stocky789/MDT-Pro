using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using MDTPro.Setup;
using MDTPro.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDTPro.Cloud
{
    internal sealed class CloudCredentials
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string DeviceToken { get; set; } = "";
    }

    internal static class CloudCredentialStore
    {
        static string StorePath => Path.Combine(SetupController.DataPath, "cloudCredentials.dpapi");
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MDTPro.CloudCredentials.v1");

        internal static CloudCredentials Load(Config cfg = null)
        {
            try
            {
                if (File.Exists(StorePath))
                {
                    byte[] protectedBytes = File.ReadAllBytes(StorePath);
                    byte[] bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                    return JsonConvert.DeserializeObject<CloudCredentials>(Encoding.UTF8.GetString(bytes)) ?? new CloudCredentials();
                }
            }
            catch (Exception ex)
            {
                Helper.Log("Failed to read protected cloud credentials: " + ex.Message, false, Helper.LogSeverity.Warning);
            }

            var migrated = new CloudCredentials
            {
                AccessToken = cfg?.cloudAccessToken ?? "",
                RefreshToken = cfg?.cloudRefreshToken ?? ""
            };
            if (!string.IsNullOrWhiteSpace(migrated.AccessToken) || !string.IsNullOrWhiteSpace(migrated.RefreshToken))
            {
                Save(migrated.AccessToken, migrated.RefreshToken, migrated.DeviceToken);
                ClearLegacyConfigTokens(cfg);
                Helper.Log("Migrated MDT Cloud credentials into protected Windows storage.", false, Helper.LogSeverity.Info);
            }
            return migrated;
        }

        internal static void Save(string accessToken, string refreshToken, string deviceToken)
        {
            Directory.CreateDirectory(SetupController.DataPath);
            var credentials = new CloudCredentials
            {
                AccessToken = accessToken ?? "",
                RefreshToken = refreshToken ?? "",
                DeviceToken = deviceToken ?? ""
            };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(credentials));
            byte[] protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(StorePath, protectedBytes);
        }

        internal static bool Clear()
        {
            try
            {
                if (File.Exists(StorePath)) File.Delete(StorePath);
                return true;
            }
            catch (Exception ex)
            {
                Helper.Log("Failed to clear protected cloud credentials: " + ex.Message, false, Helper.LogSeverity.Warning);
                return false;
            }
        }

        internal static bool TryRefresh(Config cfg, ref CloudCredentials credentials)
        {
            if (!CloudPluginBridge.CanRefreshCredentials()) return false;
            if (credentials == null || string.IsNullOrWhiteSpace(credentials.RefreshToken)) return false;
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
                {
                    var body = JsonConvert.SerializeObject(new
                    {
                        refreshToken = credentials.RefreshToken,
                        installId = cfg?.cloudInstallId,
                        deviceToken = credentials.DeviceToken
                    });
                    using (var response = http.PostAsync(CloudMode.ApiBaseUrl() + "/api/auth/refresh", new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false).GetAwaiter().GetResult())
                    {
                        string text = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            Clear();
                            ClearLegacyConfigTokens(cfg);
                            return false;
                        }
                        if (!response.IsSuccessStatusCode) return false;

                        if (!CloudPluginBridge.CanRefreshCredentials()) return false;
                        JObject json = JObject.Parse(text);
                        credentials.AccessToken = json.Value<string>("accessToken") ?? "";
                        credentials.RefreshToken = json.Value<string>("refreshToken") ?? credentials.RefreshToken;
                        if (!CloudPluginBridge.CanRefreshCredentials()) return false;
                        Save(credentials.AccessToken, credentials.RefreshToken, credentials.DeviceToken);
                        ClearLegacyConfigTokens(cfg);
                        return !string.IsNullOrWhiteSpace(credentials.AccessToken);
                    }
                }
            }
            catch (Exception ex)
            {
                Helper.Log("Cloud token refresh failed: " + ex.Message, false, Helper.LogSeverity.Warning);
                return false;
            }
        }

        static void ClearLegacyConfigTokens(Config cfg)
        {
            if (cfg == null) return;
            if (string.IsNullOrWhiteSpace(cfg.cloudAccessToken) && string.IsNullOrWhiteSpace(cfg.cloudRefreshToken)) return;
            try
            {
                cfg.cloudAccessToken = "";
                cfg.cloudRefreshToken = "";
                Helper.WriteToJsonFile(SetupController.ConfigPath, cfg);
                SetupController.ClearCache();
            }
            catch (Exception ex)
            {
                Helper.Log("Failed to clear legacy cloud tokens: " + ex.Message, false, Helper.LogSeverity.Warning);
            }
        }
    }
}
