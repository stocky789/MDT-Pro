using MDTPro.Utility;
using System.IO;
using System.Net;
using static MDTPro.Setup.SetupController;

namespace MDTPro.ServerAPI {
    internal class PluginAPIResponse : APIResponse {
        internal PluginAPIResponse(HttpListenerRequest req) : base(null) {
            string path = req.Url.AbsolutePath.Substring("/plugin/".Length);
            if (string.IsNullOrEmpty(path)) return;

            string[] pathArr = path.Split('/'); // 0 - plugin-id, 1 - type, 2 - file name
            if (pathArr.Length != 3) return;

            pathArr[0] = pathArr[0].Replace("%20", " ");
            pathArr[2] = pathArr[2].Replace("%20", " ");

            if (pathArr[1] == "page") {
                if (pathArr[2].EndsWith(".html")) pathArr[2] = pathArr[2].Substring(0, pathArr[2].Length - ".html".Length);

                if (File.Exists($"{PluginsPath}/{pathArr[0]}/pages/{pathArr[2]}.html")) {
                    buffer = File.ReadAllBytes($"{PluginsPath}/{pathArr[0]}/pages/{pathArr[2]}.html");
                    status = 200;
                    contentType = "text/html";
                }
            } else if (pathArr[1] == "script") {
                if (pathArr[2].EndsWith(".js")) pathArr[2] = pathArr[2].Substring(0, pathArr[2].Length - ".js".Length);

                if (File.Exists($"{PluginsPath}/{pathArr[0]}/scripts/{pathArr[2]}.js")) {
                    buffer = File.ReadAllBytes($"{PluginsPath}/{pathArr[0]}/scripts/{pathArr[2]}.js");
                    status = 200;
                    contentType = "application/js";
                }
            } else if (pathArr[1] == "style") {
                if (pathArr[2].EndsWith(".css")) pathArr[2] = pathArr[2].Substring(0, pathArr[2].Length - ".css".Length);

                if (File.Exists($"{PluginsPath}/{pathArr[0]}/styles/{pathArr[2]}.css")) {
                    buffer = File.ReadAllBytes($"{PluginsPath}/{pathArr[0]}/styles/{pathArr[2]}.css");
                    status = 200;
                    contentType = "text/css";
                }
            } else if (pathArr[1] == "image") {
                string imgName = pathArr[2];
                if (string.IsNullOrEmpty(imgName)) return;
                if (!imgName.EndsWith(".png") && !imgName.EndsWith(".jpg") && !imgName.EndsWith(".svg")) imgName += ".png";
                string imgPath = $"{PluginsPath}/{pathArr[0]}/images/{imgName}";
                if (File.Exists(imgPath)) {
                    buffer = File.ReadAllBytes(imgPath);
                    status = 200;
                    contentType = imgName.EndsWith(".svg") ? "image/svg+xml" : (imgName.EndsWith(".jpg") ? "image/jpeg" : "image/png");
                }
            }
        }
    }
}
