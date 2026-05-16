using MDTPro.Setup;
using System;

namespace MDTPro.Cloud {
    internal static class CloudMode {
        internal static bool IsEnabled() {
            try {
                Config cfg = SetupController.GetConfig();
                return string.Equals(cfg.storageMode, "Cloud", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(ApiBaseUrl());
            } catch {
                return false;
            }
        }

        internal static string ApiBaseUrl() {
#if DEBUG
            string url = SetupController.GetConfig().cloudApiBaseUrl;
            if (string.IsNullOrWhiteSpace(url)) url = CloudPublicApi.NormalizedBase();
            return url.TrimEnd('/');
#else
            return CloudPublicApi.NormalizedBase();
#endif
        }
    }
}
