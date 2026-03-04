using System.IO;
using System.Net;
using static MDTPro.Setup.SetupController;


namespace MDTPro.ServerAPI {
    internal class StyleAPIResponse : APIResponse {
        internal StyleAPIResponse(HttpListenerRequest req) : base(null) {
            string path = req.Url.AbsolutePath.Substring("/style/".Length);
            if (string.IsNullOrEmpty(path)) return;
            if (path.EndsWith(".css")) path = path.Substring(0, path.Length - ".css".Length);
            if (File.Exists($"{MDTProPath}/main/styles/{path}.css")) {
                buffer = File.ReadAllBytes($"{MDTProPath}/main/styles/{path}.css");
                status = 200;
                contentType = "text/css";
            }
        }
    }
}
