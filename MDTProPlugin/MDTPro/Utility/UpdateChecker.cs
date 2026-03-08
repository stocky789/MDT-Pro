using Newtonsoft.Json.Linq;
using Rage;
using System;
using System.Globalization;
using System.IO;
using System.Net;

namespace MDTPro.Utility {
    /// <summary>
    /// Checks GitHub Releases for a newer version and shows an in-game notification if one exists.
    /// Runs in the background; does not block plugin loading. Fails silently on network/parse errors.
    /// </summary>
    internal static class UpdateChecker {
        private const string ApiUrlTemplate = "https://api.github.com/repos/{0}/releases/latest";

        /// <summary>
        /// Runs an async update check. Call from a background fiber/thread.
        /// If a newer version is found, displays a notification on the game thread.
        /// </summary>
        /// <param name="currentVersion">Current assembly version (e.g. "0.9.0")</param>
        /// <param name="githubRepo">Repository in "owner/repo" format, or null/empty to skip</param>
        /// <param name="checkEnabled">Whether to perform the check</param>
        public static void CheckForUpdates(string currentVersion, string githubRepo, bool checkEnabled) {
            if (!checkEnabled || string.IsNullOrWhiteSpace(githubRepo)) return;
            if (string.IsNullOrWhiteSpace(currentVersion)) return;

            try {
                // GitHub API requires TLS 1.2; .NET Framework may default to TLS 1.0/1.1 otherwise.
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                string url = string.Format(CultureInfo.InvariantCulture, ApiUrlTemplate, githubRepo.Trim());
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.UserAgent = "MDTPro-Updater/1.0";
                request.Method = "GET";
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;

                using (var response = (HttpWebResponse)request.GetResponse()) {
                    var stream = response.GetResponseStream();
                    if (stream == null) return;
                    using (var reader = new StreamReader(stream)) {
                        string json = reader.ReadToEnd();
                        ParseAndNotify(json, currentVersion);
                    }
                }
            } catch (WebException ex) {
                if (ex.Response is HttpWebResponse httpRes)
                    Helper.Log($"Update check failed: HTTP {(int)httpRes.StatusCode}", false, Helper.LogSeverity.Warning);
                else
                    Helper.Log($"Update check failed: {ex.Message}", false, Helper.LogSeverity.Warning);
            } catch (Exception ex) {
                Helper.Log($"Update check failed: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        private static void ParseAndNotify(string json, string currentVersion) {
            try {
                var obj = JObject.Parse(json);
                string tagName = obj["tag_name"]?.ToString();
                string htmlUrl = obj["html_url"]?.ToString();

                if (string.IsNullOrWhiteSpace(tagName)) return;

                string latestVersion = NormalizeVersion(tagName);
                if (string.IsNullOrEmpty(latestVersion)) return;

                var lang = Setup.SetupController.GetLanguage().inGame;
                string message;
                string subtitle;
                if (IsNewer(latestVersion, currentVersion)) {
                    message = string.IsNullOrEmpty(lang.updateAvailable)
                        ? "Installed Version: v{0}~n~Available Version: v{1} - Update Available"
                        : lang.updateAvailable;
                    message = string.Format(CultureInfo.InvariantCulture, message, currentVersion, latestVersion);
                    if (!string.IsNullOrEmpty(htmlUrl))
                        message += $"~n~{htmlUrl}";
                    subtitle = "Update available";
                } else {
                    message = string.IsNullOrEmpty(lang.updateUpToDate)
                        ? "Installed Version: v{0}~n~Available Version: v{0} - Up to Date"
                        : lang.updateUpToDate;
                    message = string.Format(CultureInfo.InvariantCulture, message, currentVersion);
                    subtitle = "Up to date";
                }
                RageNotification.Show(message, RageNotification.NotificationType.Info, subtitle);
            } catch (Exception ex) {
                Helper.Log($"Update check parse failed: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        /// <summary>
        /// Normalizes "v0.9.0", "0.9.0-beta" etc. for comparison. Strips "v" prefix and any "-suffix".
        /// </summary>
        private static string NormalizeVersion(string tag) {
            if (string.IsNullOrWhiteSpace(tag)) return string.Empty;
            tag = tag.Trim().TrimStart('v', 'V');
            int hyphen = tag.IndexOf('-');
            if (hyphen >= 0) tag = tag.Substring(0, hyphen);
            return tag.Trim();
        }

        /// <summary>
        /// Returns true if latest is newer than current (semver-like comparison).
        /// </summary>
        private static bool IsNewer(string latest, string current) {
            int[] l = ParseVersionParts(latest);
            int[] c = ParseVersionParts(current);
            if (l == null || c == null) return false;

            int maxLen = Math.Max(l.Length, c.Length);
            for (int i = 0; i < maxLen; i++) {
                int lVal = i < l.Length ? l[i] : 0;
                int cVal = i < c.Length ? c[i] : 0;
                if (lVal > cVal) return true;
                if (lVal < cVal) return false;
            }
            return false;
        }

        private static int[] ParseVersionParts(string version) {
            if (string.IsNullOrWhiteSpace(version)) return null;
            string[] parts = version.Split('.');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) {
                string p = parts[i].Trim();
                if (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out result[i]))
                    return null;
            }
            return result;
        }
    }
}
