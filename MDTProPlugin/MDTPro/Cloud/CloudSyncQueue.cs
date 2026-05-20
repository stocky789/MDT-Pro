using MDTPro.Setup;
using MDTPro.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MDTPro.Cloud
{
    internal sealed class CloudSyncEnvelope
    {
        public string EntityType { get; set; }
        public string Operation { get; set; }
        public string IdempotencyKey { get; set; }
        public DateTimeOffset OccurredAtUtc { get; set; }
        public JObject Payload { get; set; }
    }

    internal static class CloudSyncQueue
    {
        private static readonly object LockObj = new object();
        private static readonly Queue<CloudSyncEnvelope> Pending = new Queue<CloudSyncEnvelope>();
        private static DateTime _lastFlushUtc = DateTime.MinValue;
        private static bool _flushRunning;
        private static bool _loadedFromDisk;
        internal static bool SuppressEnqueue;
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        private static string QueuePath => Path.Combine(SetupController.DataPath, "cloudSyncQueue.json");

        internal static int Count
        {
            get
            {
                EnsureLoaded();
                lock (LockObj) return Pending.Count;
            }
        }

        internal static void Enqueue(string entityType, string operation, JObject payload, string idempotencyKey = null)
        {
            int generation = CloudPluginBridge.AuthGeneration;
            if (!CloudPluginBridge.IsCloudWorkCurrent(generation)) return;
            if (CloudStopThePedBlock.ShouldBlockMdtCloudApiTraffic()) return;
            if (SuppressEnqueue) return;
            if (payload == null) payload = new JObject();
            EnsureLoaded();

            Config cfg = SetupController.GetConfig();
            int max = cfg.cloudSyncMaxQueuedItems <= 0 ? 1000 : cfg.cloudSyncMaxQueuedItems;
            lock (LockObj)
            {
                bool dropped = false;
                while (Pending.Count >= max)
                {
                    Pending.Dequeue();
                    dropped = true;
                }
                if (dropped && !cfg.cloudOfflineFallbackEnabled)
                    Helper.Log("Cloud sync queue reached its maximum size and dropped the oldest pending item.", false, Helper.LogSeverity.Warning);
                Pending.Enqueue(new CloudSyncEnvelope
                {
                    EntityType = entityType,
                    Operation = operation,
                    IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? BuildStableKey(entityType, operation, payload) : idempotencyKey,
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    Payload = payload
                });
                PersistLocked();
            }
            TryFlushInBackground(respectFlushInterval: false);
        }

        /// <param name="respectFlushInterval">When true (game fiber heartbeat), do not POST more often than <c>cloudSyncFlushIntervalMs</c> after the last attempt. When false, flush as soon as there is queued work so person/vehicle updates reach the cloud without waiting for the next poll tick.</param>
        internal static void TryFlushInBackground(bool respectFlushInterval = true)
        {
            int generation = CloudPluginBridge.AuthGeneration;
            if (!CloudPluginBridge.IsCloudWorkCurrent(generation)) return;
            if (CloudStopThePedBlock.ShouldBlockMdtCloudApiTraffic()) return;
            EnsureLoaded();
            Config cfg = SetupController.GetConfig();
            int interval = cfg.cloudSyncFlushIntervalMs <= 0 ? Config.PerfCaptureIntervalMs.CloudSyncFlush : cfg.cloudSyncFlushIntervalMs;
            lock (LockObj)
            {
                if (_flushRunning || Pending.Count == 0) return;
                if (respectFlushInterval && (DateTime.UtcNow - _lastFlushUtc).TotalMilliseconds < interval) return;
                _flushRunning = true;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool flushSucceeded = false;
                try
                {
                    flushSucceeded = FlushAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Helper.Log($"Cloud sync flush failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                }
                finally
                {
                    lock (LockObj)
                    {
                        _flushRunning = false;
                        _lastFlushUtc = DateTime.UtcNow;
                    }
                    // After a successful POST, drain any remaining queue (batches of 50) or work enqueued during the request.
                    if (flushSucceeded) TryFlushInBackground(respectFlushInterval: false);
                }
            });
        }

        internal static bool FlushSynchronously(int maxBatches = 10)
        {
            int generation = CloudPluginBridge.AuthGeneration;
            if (!CloudPluginBridge.IsCloudWorkCurrent(generation)) return false;
            if (CloudStopThePedBlock.ShouldBlockMdtCloudApiTraffic()) return false;
            EnsureLoaded();
            bool anySuccess = false;
            for (int i = 0; i < maxBatches; i++)
            {
                lock (LockObj)
                {
                    if (_flushRunning || Pending.Count == 0) return anySuccess;
                    _flushRunning = true;
                }
                try
                {
                    FlushAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    anySuccess = true;
                }
                catch (Exception ex)
                {
                    Helper.Log($"Cloud sync startup flush failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                    return anySuccess;
                }
                finally
                {
                    lock (LockObj)
                    {
                        _flushRunning = false;
                        _lastFlushUtc = DateTime.UtcNow;
                    }
                }
            }
            return anySuccess;
        }

        /// <returns>True if a batch was POSTed and the server returned success; false if nothing to send, requeued, or HTTP failure.</returns>
        private static async Task<bool> FlushAsync()
        {
            int generation = CloudPluginBridge.AuthGeneration;
            if (!CloudPluginBridge.IsCloudWorkCurrent(generation)) return false;
            List<CloudSyncEnvelope> batch = new List<CloudSyncEnvelope>();
            lock (LockObj)
            {
                while (Pending.Count > 0 && batch.Count < 50) batch.Add(Pending.Dequeue());
                PersistLocked();
            }
            if (batch.Count == 0) return true;
            if (!CloudPluginBridge.IsCloudWorkCurrent(generation) || CloudStopThePedBlock.ShouldBlockMdtCloudApiTraffic())
            {
                Requeue(batch);
                return false;
            }

            Config cfg = SetupController.GetConfig();
            CloudCredentials credentials = CloudCredentialStore.Load(cfg);
            if (!CloudPluginBridge.IsCloudWorkCurrent(generation))
            {
                Requeue(batch);
                return false;
            }
            if (string.IsNullOrWhiteSpace(credentials.AccessToken) || string.IsNullOrWhiteSpace(credentials.DeviceToken))
            {
                Requeue(batch);
                return false;
            }

            string installId = EnsureInstallId(cfg);
            if (!CloudPluginBridge.IsCloudWorkCurrent(generation))
            {
                Requeue(batch);
                return false;
            }
            JObject body = new JObject
            {
                ["installId"] = installId,
                ["items"] = JArray.FromObject(batch)
            };

            if (!CloudPluginBridge.IsCloudWorkCurrent(generation))
            {
                Requeue(batch);
                return false;
            }
            HttpResponseMessage res = await SendSyncRequestAsync(credentials, body).ConfigureAwait(false);
            if (!CloudPluginBridge.IsCloudWorkCurrent(generation))
            {
                res.Dispose();
                Requeue(batch);
                return false;
            }
            if ((res.StatusCode == System.Net.HttpStatusCode.Unauthorized || res.StatusCode == System.Net.HttpStatusCode.Forbidden) &&
                CloudPluginBridge.IsCloudWorkCurrent(generation) && CloudCredentialStore.TryRefresh(cfg, ref credentials))
            {
                res.Dispose();
                if (!CloudPluginBridge.IsCloudWorkCurrent(generation))
                {
                    Requeue(batch);
                    return false;
                }
                res = await SendSyncRequestAsync(credentials, body).ConfigureAwait(false);
            }

            using (res)
            {
                if (!CloudPluginBridge.IsCloudWorkCurrent(generation))
                {
                    Requeue(batch);
                    return false;
                }
                if (!res.IsSuccessStatusCode)
                {
                    if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized || res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        CloudCredentialStore.Clear();
                    Requeue(batch);
                    Helper.Log($"Cloud sync flush rejected: HTTP {(int)res.StatusCode}", false, Helper.LogSeverity.Warning);
                    return false;
                }
                string json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                RememberResolvedEntityIds(batch, json);
                lock (LockObj) PersistLocked();
                return true;
            }
        }

        private static void RememberResolvedEntityIds(List<CloudSyncEnvelope> batch, string responseJson)
        {
            if (batch == null || batch.Count == 0 || string.IsNullOrWhiteSpace(responseJson)) return;
            try
            {
                JObject response = JObject.Parse(responseJson);
                JArray results = response["results"] as JArray;
                if (results == null) return;
                foreach (JObject result in results)
                {
                    string key = result.Value<string>("idempotencyKey");
                    string entityId = result.Value<string>("entityId");
                    bool accepted = result.Value<bool?>("accepted") ?? false;
                    if (!accepted || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(entityId)) continue;
                    CloudSyncEnvelope envelope = batch.FirstOrDefault(x => string.Equals(x.IdempotencyKey, key, StringComparison.Ordinal));
                    if (envelope == null) continue;
                    CloudIdentityCache.RememberFromSyncResult(envelope.EntityType ?? "", envelope.Payload, entityId);
                }
            }
            catch (Exception ex)
            {
                Helper.Log($"Cloud sync identity cache update failed: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        private static Task<HttpResponseMessage> SendSyncRequestAsync(CloudCredentials credentials, JObject body)
        {
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, CloudMode.ApiBaseUrl() + "/api/mdt/sync");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.AccessToken);
            req.Headers.TryAddWithoutValidation("X-MDT-Device-Token", credentials.DeviceToken);
            req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
            return Http.SendAsync(req);
        }

        private static string EnsureInstallId(Config cfg)
        {
            if (!string.IsNullOrWhiteSpace(cfg.cloudInstallId)) return cfg.cloudInstallId;
            cfg.cloudInstallId = Guid.NewGuid().ToString("N");
            try
            {
                Helper.WriteToJsonFile(SetupController.ConfigPath, cfg);
                SetupController.ClearCache();
            }
            catch { }
            return cfg.cloudInstallId;
        }

        private static void Requeue(List<CloudSyncEnvelope> batch)
        {
            lock (LockObj)
            {
                for (int i = 0; i < batch.Count; i++)
                    Pending.Enqueue(batch[i]);
                PersistLocked();
            }
        }

        private static void EnsureLoaded()
        {
            if (_loadedFromDisk) return;
            lock (LockObj)
            {
                if (_loadedFromDisk) return;
                try
                {
                    if (File.Exists(QueuePath))
                    {
                        var items = JsonConvert.DeserializeObject<List<CloudSyncEnvelope>>(File.ReadAllText(QueuePath));
                        if (items != null)
                        {
                            foreach (var item in items)
                            {
                                if (item != null && !string.IsNullOrWhiteSpace(item.IdempotencyKey))
                                    Pending.Enqueue(item);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Helper.Log($"Cloud sync queue load failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                }
                finally
                {
                    _loadedFromDisk = true;
                }
            }
        }

        private static void PersistLocked()
        {
            try
            {
                string dir = Path.GetDirectoryName(QueuePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(QueuePath, JsonConvert.SerializeObject(Pending.ToArray(), Formatting.None));
            }
            catch (Exception ex)
            {
                Helper.Log($"Cloud sync queue persist failed: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        private static string BuildStableKey(string entityType, string operation, JObject payload)
        {
            string canonical = $"{entityType ?? ""}:{operation ?? ""}:{payload.ToString(Formatting.None)}";
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return $"{entityType}:{operation}:{sb}";
            }
        }
    }
}
