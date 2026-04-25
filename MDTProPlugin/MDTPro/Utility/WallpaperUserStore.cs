using MDTPro.Setup;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;

namespace MDTPro.Utility {
    /// <summary>User desktop wallpaper: optional PNG/JPEG in <c>img/</c> and flag in <c>data/wallpaperUser.json</c>.</summary>
    internal static class WallpaperUserStore {
        private const int MaxImageBytes = 12 * 1024 * 1024;

        private class WallpaperState {
            public bool useCustom;
            public string format = "png"; // "png" | "jpg"
        }

        private static string StatePath => Path.Combine(SetupController.DataPath, "wallpaperUser.json");
        private static string PathPng => Path.Combine(SetupController.ImgDirPath, "wallpaperUser.png");
        private static string PathJpg => Path.Combine(SetupController.ImgDirPath, "wallpaperUser.jpg");

        private static WallpaperState ReadState() {
            try {
                if (!File.Exists(StatePath)) return new WallpaperState();
                return JsonConvert.DeserializeObject<WallpaperState>(File.ReadAllText(StatePath, Encoding.UTF8)) ?? new WallpaperState();
            } catch {
                return new WallpaperState();
            }
        }

        private static void WriteState(WallpaperState s) {
            File.WriteAllText(StatePath, JsonConvert.SerializeObject(s, Formatting.Indented), new UTF8Encoding(false));
        }

        internal static string GetStateJson() {
            var s = ReadState();
            string path = s.useCustom ? (s.format == "jpg" ? PathJpg : PathPng) : null;
            bool hasImage = !string.IsNullOrEmpty(path) && File.Exists(path);
            if (s.useCustom && !hasImage) {
                s.useCustom = false;
                try { WriteState(s); } catch { }
            }
            string updated = null;
            if (hasImage && path != null) {
                try {
                    updated = File.GetLastWriteTimeUtc(path).ToString("O");
                } catch { }
            }
            return JsonConvert.SerializeObject(new {
                useCustom = s.useCustom && hasImage,
                hasImage,
                updated
            });
        }

        /// <summary>Tries to read the custom file when the saved state has <c>useCustom</c> and the file exists.</summary>
        internal static bool TryGetCustomFile(out byte[] bytes, out string contentType) {
            bytes = null;
            contentType = null;
            var s = ReadState();
            if (s == null || !s.useCustom) return false;
            string p = s.format == "jpg" ? PathJpg : PathPng;
            if (!File.Exists(p)) return false;
            try {
                bytes = File.ReadAllBytes(p);
                contentType = s.format == "jpg" ? "image/jpeg" : "image/png";
                return true;
            } catch {
                return false;
            }
        }

        private static void DeleteUserFiles() {
            try { if (File.Exists(PathPng)) File.Delete(PathPng); } catch { }
            try { if (File.Exists(PathJpg)) File.Delete(PathJpg); } catch { }
        }

        internal static string TryReset() {
            try {
                DeleteUserFiles();
                WriteState(new WallpaperState { useCustom = false, format = "png" });
                return null;
            } catch (Exception e) {
                return e.Message;
            }
        }

        /// <summary>Base64 may include <c>data:image/...</c> prefix. Validates PNG or JPEG magic bytes only.</summary>
        internal static string TrySaveFromBase64(string rawBase64, out int savedBytes) {
            savedBytes = 0;
            if (string.IsNullOrWhiteSpace(rawBase64))
                return "No image data.";

            string b = rawBase64.Trim();
            int dash = b.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (dash >= 0) b = b.Substring(dash + 7);

            byte[] data;
            try {
                data = Convert.FromBase64String(b);
            } catch {
                return "Invalid base64.";
            }
            if (data.Length < 8 || data.Length > MaxImageBytes)
                return data.Length > MaxImageBytes ? "Image is too large (max 12 MB)." : "Image is too small or corrupt.";

            string format;
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                format = "png";
            else if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                format = "jpg";
            else
                return "Only PNG or JPEG images are allowed.";

            try {
                if (!Directory.Exists(SetupController.ImgDirPath)) Directory.CreateDirectory(SetupController.ImgDirPath);
                if (!Directory.Exists(SetupController.DataPath)) Directory.CreateDirectory(SetupController.DataPath);

                string target = format == "jpg" ? PathJpg : PathPng;
                string other = format == "jpg" ? PathPng : PathJpg;
                if (File.Exists(other)) {
                    try { File.Delete(other); } catch { }
                }
                File.WriteAllBytes(target, data);
                savedBytes = data.Length;
                WriteState(new WallpaperState { useCustom = true, format = format });
                return null;
            } catch (Exception e) {
                return e.Message;
            }
        }
    }
}
