using MDTPro.Data;
using MDTPro.Setup;
using MDTPro.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace MDTPro.Cloud {
    internal static class CloudEncounterLeaseClient {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        private static readonly object LockObj = new object();
        private static string _personId;
        private static string _leaseToken;
        private static DateTimeOffset _expiresAtUtc;
        private static DateTimeOffset _lastRenewAttemptUtc;
        private static bool _leaseRequestRunning;

        internal static bool CanPushPedToCdf(MDTProPedData ped) {
            if (ped == null || !CloudMode.IsEnabled()) return true;
            if (CloudStopThePedBlock.ShouldBlockMdtCloudApiTraffic()) return true;
            string personId = CloudIdentityCache.GetPersonId(ped);
            if (string.IsNullOrWhiteSpace(personId)) return true;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (LockObj) {
                bool hasLease = string.Equals(_personId, personId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(_leaseToken) &&
                    _expiresAtUtc > now.AddSeconds(2);
                if (hasLease) {
                    if (_expiresAtUtc <= now.AddSeconds(10))
                        QueueEnsureLease(personId, BuildContext(ped));
                    return true;
                }
            }
            QueueEnsureLease(personId, BuildContext(ped));
            return false;
        }

        internal static void ObserveContextPerson(MDTProPedData ped) {
            if (!CloudMode.IsEnabled()) {
                QueueReleaseCurrent();
                return;
            }
            if (CloudStopThePedBlock.ShouldBlockMdtCloudApiTraffic()) {
                lock (LockObj) ClearLocked();
                return;
            }
            string personId = ped == null ? null : CloudIdentityCache.GetPersonId(ped);
            if (!string.IsNullOrWhiteSpace(personId)) {
                QueueEnsureLease(personId, BuildContext(ped));
                return;
            }
            QueueReleaseCurrent();
        }

        internal static void ReleaseCurrent() {
            string personId;
            string token;
            ClearAndCopyLease(out personId, out token);
            if (CloudStopThePedBlock.ShouldBlockMdtCloudApiTraffic()) return;
            if (!string.IsNullOrWhiteSpace(personId) && !string.IsNullOrWhiteSpace(token))
                SendLeaseRequest("/api/mdt/reencounter/release", personId, token, null);
        }

        private static bool EnsureLease(string personId, JObject context) {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string previousPersonId = null;
            string previousToken = null;
            bool shouldRenew = false;
            lock (LockObj) {
                if (string.Equals(_personId, personId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(_leaseToken) &&
                    _expiresAtUtc > now.AddSeconds(10)) {
                    if ((now - _lastRenewAttemptUtc).TotalSeconds < 20) return true;
                    shouldRenew = true;
                } else if (!string.IsNullOrWhiteSpace(_personId) &&
                    !string.Equals(_personId, personId, StringComparison.OrdinalIgnoreCase)) {
                    previousPersonId = _personId;
                    previousToken = _leaseToken;
                    ClearLocked();
                }
            }
            if (!string.IsNullOrWhiteSpace(previousPersonId) && !string.IsNullOrWhiteSpace(previousToken))
                SendLeaseRequest("/api/mdt/reencounter/release", previousPersonId, previousToken, null);
            if (shouldRenew) return TryRenew(personId, context);
            return TryClaim(personId, context);
        }

        private static void QueueEnsureLease(string personId, JObject context) {
            if (CloudStopThePedBlock.ShouldBlockMdtCloudApiTraffic()) return;
            if (string.IsNullOrWhiteSpace(personId)) return;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (LockObj) {
                if (_leaseRequestRunning) return;
                if (string.Equals(_personId, personId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(_leaseToken) &&
                    _expiresAtUtc > now.AddSeconds(10) &&
                    (now - _lastRenewAttemptUtc).TotalSeconds < 20) {
                    return;
                }
                _leaseRequestRunning = true;
            }

            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    EnsureLease(personId, context);
                } catch (Exception ex) {
                    Helper.Log($"Cloud re-encounter lease background update failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                } finally {
                    lock (LockObj) {
                        _leaseRequestRunning = false;
                    }
                }
            });
        }

        private static void QueueReleaseCurrent() {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    ReleaseCurrent();
                } catch (Exception ex) {
                    Helper.Log($"Cloud re-encounter lease background release failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                }
            });
        }

        private static bool TryClaim(string personId, JObject context) {
            JObject response = SendLeaseRequest("/api/mdt/reencounter/claim", personId, null, context);
            if (response == null || !(response.Value<bool?>("accepted") ?? false)) {
                Helper.Log("Cloud re-encounter claim denied; skipped CDF overwrite for this person.", false, Helper.LogSeverity.Info);
                return false;
            }
            string token = response.Value<string>("leaseToken");
            DateTimeOffset expires;
            if (string.IsNullOrWhiteSpace(token) || !DateTimeOffset.TryParse(response.Value<string>("expiresAtUtc"), out expires))
                return false;
            lock (LockObj) {
                _personId = personId;
                _leaseToken = token;
                _expiresAtUtc = expires;
                _lastRenewAttemptUtc = DateTimeOffset.UtcNow;
            }
            return true;
        }

        private static bool TryRenew(string personId, JObject context) {
            string token;
            lock (LockObj) {
                if (!string.Equals(_personId, personId, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(_leaseToken))
                    return false;
                token = _leaseToken;
                _lastRenewAttemptUtc = DateTimeOffset.UtcNow;
            }

            JObject response = SendLeaseRequest("/api/mdt/reencounter/renew", personId, token, context);
            if (response == null || !(response.Value<bool?>("accepted") ?? false)) {
                lock (LockObj) ClearLocked();
                Helper.Log("Cloud re-encounter lease could not be renewed; skipped CDF overwrite for this person.", false, Helper.LogSeverity.Info);
                return false;
            }
            DateTimeOffset expires;
            if (DateTimeOffset.TryParse(response.Value<string>("expiresAtUtc"), out expires)) {
                lock (LockObj) _expiresAtUtc = expires;
            }
            return true;
        }

        private static JObject SendLeaseRequest(string path, string personId, string leaseToken, JObject context, bool retry = true) {
            if (CloudStopThePedBlock.ShouldBlockMdtCloudApiTraffic()) return null;
            Config cfg = SetupController.GetConfig();
            CloudCredentials credentials = CloudCredentialStore.Load(cfg);
            if (string.IsNullOrWhiteSpace(credentials.AccessToken) ||
                string.IsNullOrWhiteSpace(credentials.DeviceToken) ||
                string.IsNullOrWhiteSpace(cfg.cloudInstallId)) {
                return null;
            }

            JObject body = new JObject {
                ["installId"] = cfg.cloudInstallId,
                ["personId"] = personId,
                ["leaseToken"] = leaseToken,
                ["context"] = context
            };
            try {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, CloudMode.ApiBaseUrl() + path)) {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.AccessToken);
                    request.Headers.TryAddWithoutValidation("X-MDT-Device-Token", credentials.DeviceToken);
                    request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = Http.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult()) {
                        if (retry && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) &&
                            CloudCredentialStore.TryRefresh(cfg, ref credentials)) {
                            return SendLeaseRequest(path, personId, leaseToken, context, retry: false);
                        }
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                            CloudCredentialStore.Clear();
                        if (!response.IsSuccessStatusCode) return null;
                        string json = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                        return JObject.Parse(json);
                    }
                }
            } catch (Exception ex) {
                Helper.Log($"Cloud re-encounter lease request failed: {ex.Message}", false, Helper.LogSeverity.Warning);
                return null;
            }
        }

        private static JObject BuildContext(MDTProPedData ped) {
            if (ped == null) return null;
            return JObject.FromObject(new {
                name = ped.Name,
                firstName = ped.FirstName,
                lastName = ped.LastName,
                birthday = ped.Birthday,
                address = ped.Address,
                gender = ped.Gender,
                modelHash = ped.ModelHash,
                modelName = ped.ModelName
            });
        }

        private static void ClearLocked() {
            _personId = null;
            _leaseToken = null;
            _expiresAtUtc = DateTimeOffset.MinValue;
            _lastRenewAttemptUtc = DateTimeOffset.MinValue;
        }

        private static void ClearAndCopyLease(out string personId, out string token) {
            lock (LockObj) {
                personId = _personId;
                token = _leaseToken;
                ClearLocked();
            }
        }
    }
}
