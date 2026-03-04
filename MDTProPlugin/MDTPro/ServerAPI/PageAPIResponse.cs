using System.IO;
using System.Net;
using static MDTPro.Setup.SetupController;

namespace MDTPro.ServerAPI {
    internal class PageAPIResponse : APIResponse {
        internal PageAPIResponse(HttpListenerRequest req) : base(null) {
            string path = req.Url.AbsolutePath.Substring("/page/".Length);
            if (string.IsNullOrEmpty(path)) return;
            if (path.EndsWith(".html")) path = path.Substring(0, path.Length - ".html".Length);
            if (File.Exists($"{MDTProPath}/main/pages/{path}.html")) {
                buffer = File.ReadAllBytes($"{MDTProPath}/main/pages/{path}.html");
                status = 200;
                contentType = "text/html";
            }
        }
    }
}
