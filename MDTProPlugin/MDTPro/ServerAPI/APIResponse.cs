using MDTPro;
using MDTPro.Plugins;
using MDTPro.Setup;
using MDTPro.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace MDTPro.ServerAPI {
    internal class APIResponse {
        internal byte[] buffer = Encoding.UTF8.GetBytes("404 - Not found");
        internal int status = 404;
        internal string contentType = "text/plain";
        /// <summary>Optional custom headers (e.g. <c>X-MdtPro-Nearby-Scan</c> when the game fiber could not run).</summary>
        internal List<(string name, string value)> ExtraResponseHeaders;

        internal APIResponse(HttpListenerRequest req) {
            if (req == null) return;
            string path = req.Url.AbsolutePath;
            if (path == "/") {
                buffer = File.ReadAllBytes($"{SetupController.MDTProPath}/main/pages/index.html");
                status = 200;
                contentType = "text/html";
            } else if (path == "/favicon" || path == "/favicon.svg") {
                string faviconSvg = $"{SetupController.MDTProPath}/img/favicon.svg";
                string faviconPng = $"{SetupController.MDTProPath}/img/favicon.png";
                if (File.Exists(faviconSvg)) {
                    buffer = File.ReadAllBytes(faviconSvg);
                    status = 200;
                    contentType = "image/svg+xml";
                } else if (File.Exists(faviconPng)) {
                    buffer = File.ReadAllBytes(faviconPng);
                    status = 200;
                    contentType = "image/png";
                }
            } else if (path == "/customization") {
                buffer = File.ReadAllBytes($"{SetupController.MDTProPath}/customization/index.html");
                status = 200;
                contentType = "text/html";
            } else if (path == "/version") {
                buffer = Encoding.UTF8.GetBytes(Main.Version);
                status = 200;
                contentType = "text/plain";
            } else if (path == "/config") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(SetupController.GetConfig()));
                status = 200;
                contentType = "text/json";
            } else if (path == "/integration") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new {
                    version = Main.Version,
                    policingRedefinedLoaded = Main.usePR,
                    stopThePedLoaded = Main.useSTP,
                    stopEventsProvider = ModIntegration.SubscribedPolicingRedefinedStopEvents ? "PolicingRedefined"
                        : (ModIntegration.SubscribedStopThePedStopEvents ? "StopThePed" : "none"),
                    backupProvider = string.IsNullOrEmpty(ModIntegration.ActiveBackupProviderId) ? "none" : ModIntegration.ActiveBackupProviderId,
                    policingRedefinedSearchItemsApi = ModIntegration.HasPolicingRedefinedSearchItemsApi,
                    integrationStopEvents = ModIntegration.ConfiguredStopEvents,
                    integrationBackupProvider = ModIntegration.ConfiguredBackupProvider
                }));
                status = 200;
                contentType = "text/json";
            } else if (path == "/language") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(SetupController.GetLanguage()));
                status = 200;
                contentType = "text/json";
            } else if (path == "/citationOptions") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(SetupController.GetCitationOptions()));
                status = 200;
                contentType = "text/json";
            } else if (path == "/arrestOptions") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(SetupController.GetArrestOptions()));
                status = 200;
                contentType = "text/json";
            } else if (path == "/seizureOptions") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(SetupController.GetSeizureOptions()));
                status = 200;
                contentType = "text/json";
            } else if (path == "/pluginInfo") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(PluginController.GetPlugins()));
                status = 200;
                contentType = "text/json";
            } else if (path == "/wallpaperSettings") {
                buffer = Encoding.UTF8.GetBytes(WallpaperUserStore.GetStateJson());
                status = 200;
                contentType = "text/json";
                ExtraResponseHeaders = new List<(string, string)> { ("Cache-Control", "no-store") };
            } else if (path == "/roads.geojson") {
                buffer = File.ReadAllBytes($"{SetupController.MDTProPath}/roads.geojson");
                status = 200;
                contentType = "text/json";
            }
        }
    }
}
