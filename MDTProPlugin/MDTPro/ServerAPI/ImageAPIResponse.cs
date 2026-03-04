using MDTPro.Setup;
using System.IO;
using System.Net;

namespace MDTPro.ServerAPI {
    internal class ImageAPIResponse : APIResponse {
        internal ImageAPIResponse(HttpListenerRequest req) : base(null) {
            string path = req.Url.AbsolutePath.Substring("/image/".Length);
            if (string.IsNullOrEmpty(path)) return;
            if (path.EndsWith(".png") || path.EndsWith(".jpg")) path = path.Substring(0, path.Length - 4);
            if (path.EndsWith(".jpeg")) path = path.Substring(0, path.Length - ".jpeg".Length);

            if (path == "map") {
                buffer = File.ReadAllBytes($"{SetupController.ImgDirPath}/map.png");
                status = 200;
                contentType = "image/png";
            } else if (path == "desktop") {
                string desktopPath = $"{SetupController.ImgDirPath}/desktop.png";
                if (File.Exists(desktopPath)) {
                    buffer = File.ReadAllBytes(desktopPath);
                    status = 200;
                    contentType = "image/png";
                }
            } else if (path == "badge") {
                string badgePath = $"{SetupController.ImgDirPath}/badge.png";
                if (File.Exists(badgePath)) {
                    buffer = File.ReadAllBytes(badgePath);
                    status = 200;
                    contentType = "image/png";
                }
            }
        }
    }
}
