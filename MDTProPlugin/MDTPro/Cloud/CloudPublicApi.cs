using System;

namespace MDTPro.Cloud {
    /// <summary>MDT Cloud API origin (no in-game URL field). Release builds use production; Debug defaults to local dev.</summary>
    internal static class CloudPublicApi {
        internal const string ProductionCloudApiBaseUrl = "https://mdt.stockhosting.com.au";

#if DEBUG
        /// <summary>Default local MDT Cloud API for plugin development (see <c>cloud/docs/dev-stack.md</c>).</summary>
        internal const string DebugCloudApiBaseUrl = "http://127.0.0.1:8080";

        internal const string BaseUrl = DebugCloudApiBaseUrl;
#else
        internal const string BaseUrl = ProductionCloudApiBaseUrl;
#endif

        /// <summary>
        /// Trimmed origin with no trailing slash. In <c>DEBUG</c> builds, <c>MDT_CLOUD_API_BASE</c> overrides the default (e.g. another port).
        /// </summary>
        internal static string NormalizedBase() {
#if DEBUG
            string env = Environment.GetEnvironmentVariable("MDT_CLOUD_API_BASE");
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim().TrimEnd('/');
#endif
            return BaseUrl.TrimEnd('/');
        }
    }
}
