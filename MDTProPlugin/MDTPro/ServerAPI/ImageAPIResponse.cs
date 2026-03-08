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
                string badgeSvg = $"{SetupController.ImgDirPath}/badge.svg";
                string badgePng = $"{SetupController.ImgDirPath}/badge.png";
                if (File.Exists(badgeSvg)) {
                    buffer = File.ReadAllBytes(badgeSvg);
                    status = 200;
                    contentType = "image/svg+xml";
                } else if (File.Exists(badgePng)) {
                    buffer = File.ReadAllBytes(badgePng);
                    status = 200;
                    contentType = "image/png";
                }
            } else if (path == "firearms" || path == "firearms.svg") {
                string firearmsPath = $"{SetupController.ImgDirPath}/firearms.svg";
                if (File.Exists(firearmsPath)) {
                    buffer = File.ReadAllBytes(firearmsPath);
                    status = 200;
                    contentType = "image/svg+xml";
                }
            }
        }
    }
}
