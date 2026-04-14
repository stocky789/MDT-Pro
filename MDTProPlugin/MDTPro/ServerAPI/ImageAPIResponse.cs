using MDTPro.Setup;
using System;
using System.IO;
using System.Net;

namespace MDTPro.ServerAPI {
    internal class ImageAPIResponse : APIResponse {
        internal ImageAPIResponse(HttpListenerRequest req) : base(null) {
            string rel = req.Url.AbsolutePath.Substring("/image/".Length);
            if (string.IsNullOrEmpty(rel)) return;

            // Bundled face-crop ped portraits only (no CDN). /image/peds/{model}.webp|.png or {model}__{d}_{t}.webp — must run before extension stripping below.
            if (rel.StartsWith("peds/", StringComparison.OrdinalIgnoreCase)) {
                string fileName = rel.Substring("peds/".Length);
                if (!IsSafePedCatalogueFileName(fileName)) return;
                string pedDir = Path.GetFullPath(Path.Combine(SetupController.MDTProPath, "images", "peds"));
                string full = Path.GetFullPath(Path.Combine(pedDir, fileName));
                if (!full.StartsWith(pedDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return;
                buffer = File.ReadAllBytes(full);
                status = 200;
                contentType = fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                    ? "image/webp"
                    : "image/png";
                return;
            }

            string path = rel;
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
            } else if (path.Equals("pedIdUnavailable.svg", StringComparison.OrdinalIgnoreCase)
                || path.Equals("pedIdUnavailable", StringComparison.OrdinalIgnoreCase)) {
                string pedIdSvg = Path.Combine(SetupController.ImgDirPath, "pedIdUnavailable.svg");
                if (File.Exists(pedIdSvg)) {
                    buffer = File.ReadAllBytes(pedIdSvg);
                    status = 200;
                    contentType = "image/svg+xml";
                }
            }
        }

        /// <summary>Single file segment only; .webp or .png; <c>model</c> or <c>model__d_t</c> (digits only in d and t).</summary>
        private static bool IsSafePedCatalogueFileName(string fileName) {
            if (string.IsNullOrEmpty(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;
            if (fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0 || fileName.Contains(".."))
                return false;
            if (!fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                && !fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return false;
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(baseName) || baseName.Length > 96) return false;
            return IsSafePedCatalogueBaseName(baseName);
        }

        private static bool IsSafePedCatalogueBaseName(string baseName) {
            int vSep = baseName.IndexOf("__", StringComparison.Ordinal);
            if (vSep < 0) {
                foreach (char c in baseName) {
                    if ((c < 'a' || c > 'z') && (c < '0' || c > '9') && c != '_')
                        return false;
                }
                return true;
            }
            string model = baseName.Substring(0, vSep);
            string rest = baseName.Substring(vSep + 2);
            if (model.Length < 1 || rest.Length < 3) return false;
            if (rest.IndexOf("__", StringComparison.Ordinal) >= 0) return false;
            int us = rest.IndexOf('_');
            if (us < 1 || us >= rest.Length - 1) return false;
            if (rest.IndexOf('_', us + 1) >= 0) return false;
            foreach (char c in model) {
                if ((c < 'a' || c > 'z') && (c < '0' || c > '9') && c != '_')
                    return false;
            }
            string d = rest.Substring(0, us);
            string t = rest.Substring(us + 1);
            foreach (char c in d) {
                if (c < '0' || c > '9') return false;
            }
            foreach (char c in t) {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }
    }
}
