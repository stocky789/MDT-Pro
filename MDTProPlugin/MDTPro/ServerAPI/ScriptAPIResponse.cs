using System.IO;
using System.Net;
using static MDTPro.Setup.SetupController;

namespace MDTPro.ServerAPI {
    internal class ScriptAPIResponse : APIResponse {
        internal ScriptAPIResponse(HttpListenerRequest req) : base(null) {
            string path = req.Url.AbsolutePath.Substring("/script/".Length);
            if (string.IsNullOrEmpty(path)) return;
            if (path.EndsWith(".js")) path = path.Substring(0, path.Length - ".js".Length);
            if (File.Exists($"{MDTProPath}/main/scripts/{path}.js")) {
                buffer = File.ReadAllBytes($"{MDTProPath}/main/scripts/{path}.js");
                status = 200;
                contentType = "application/js";
            }
        }
    }
}
