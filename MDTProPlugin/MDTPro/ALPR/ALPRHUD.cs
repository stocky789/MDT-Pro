using MDTPro.Data;
using MDTPro.Setup;
using Rage;
using System;
using System.Drawing;
using static MDTPro.Setup.SetupController;

namespace MDTPro.ALPR {
    /// <summary>
    /// Draws the ALPR HUD panel on screen using Rage Graphics API.
    /// </summary>
    internal static class ALPRHUD {
        private static GameFiber _drawFiber;
        private const string FontName = "Arial";
        private const float FontSizeTitle = 14f;
        private const float FontSizeBody = 11f;
        private const float LineHeight = 16f;
        private const float Padding = 8f;
        private static readonly Color BgColor = Color.FromArgb(180, 20, 25, 35);
        private static readonly Color BorderColor = Color.FromArgb(220, 70, 130, 180);
        private static readonly Color TextColor = Color.White;
        private static readonly Color FlagStolenColor = Color.FromArgb(255, 220, 80, 80);
        private static readonly Color FlagExpiredColor = Color.FromArgb(255, 255, 165, 0);

        internal static void Start() {
            if (_drawFiber != null) return;
            _drawFiber = GameFiber.StartNew(DrawLoop);
        }

        internal static void Stop() {
            _drawFiber?.Abort();
            _drawFiber = null;
        }

        private static void DrawLoop() {
            while (ALPRController.IsRunning) {
                try {
                    var cfg = GetConfig();
                    if (ALPRController.CurrentHit != null && cfg != null && cfg.alprEnabled) {
                        DrawPanel(ALPRController.CurrentHit);
                    }
                } catch (Exception) {
                    // Silently ignore draw errors (e.g. during unload)
                }
                GameFiber.Yield();
            }
        }

        private static void DrawPanel(ALPRHit hit) {
            if (hit == null) return;
            int actualW = Game.Resolution.Width;
            int actualH = Game.Resolution.Height;
            int w = Math.Max(640, actualW);
            int h = Math.Max(480, actualH);
            var cfg = GetConfig();
            if (cfg == null) return;
            float panelW = 220f;
            int lineCount = 3 + (hit.Flags?.Count ?? 0); // plate, owner, model + flags
            float panelH = Padding * 2 + LineHeight * lineCount;

            float x, y;
            ResolvePosition(cfg.alprHudAnchor, cfg.alprHudOffsetX, cfg.alprHudOffsetY, w, h, panelW, panelH, out x, out y);
            // Clamp to actual screen bounds so the panel stays visible on low-resolution displays
            x = Math.Max(0, Math.Min(actualW - panelW, x));
            y = Math.Max(0, Math.Min(actualH - panelH, y));

            var rect = new RectangleF(x, y, panelW, panelH);

            // Background
            Rage.Graphics.DrawRectangle(rect, BgColor);
            // Border
            Rage.Graphics.DrawRectangle(new RectangleF(rect.X, rect.Y, rect.Width, 2), BorderColor);
            Rage.Graphics.DrawRectangle(new RectangleF(rect.X, rect.Y + rect.Height - 2, rect.Width, 2), BorderColor);
            Rage.Graphics.DrawRectangle(new RectangleF(rect.X, rect.Y, 2, rect.Height), BorderColor);
            Rage.Graphics.DrawRectangle(new RectangleF(rect.X + rect.Width - 2, rect.Y, 2, rect.Height), BorderColor);

            float lineY = rect.Y + Padding;

            // Plate (title)
            Rage.Graphics.DrawText(hit.Plate ?? "", FontName, FontSizeTitle, new PointF(rect.X + Padding, lineY), TextColor);
            lineY += LineHeight;

            // Owner
            Rage.Graphics.DrawText((hit.Owner ?? "").Length > 22 ? (hit.Owner ?? "").Substring(0, 22) + "…" : (hit.Owner ?? ""), FontName, FontSizeBody, new PointF(rect.X + Padding, lineY), TextColor);
            lineY += LineHeight;

            // Model
            string model = hit.ModelDisplayName ?? "";
            if (model.Length > 24) model = model.Substring(0, 24) + "…";
            Rage.Graphics.DrawText(model, FontName, FontSizeBody, new PointF(rect.X + Padding, lineY), TextColor);
            lineY += LineHeight;

            // Flags
            if (hit.Flags != null) {
                foreach (string f in hit.Flags) {
                    Color c = IsSevereFlag(f) ? FlagStolenColor : FlagExpiredColor;
                    Rage.Graphics.DrawText("• " + f, FontName, FontSizeBody, new PointF(rect.X + Padding, lineY), c);
                    lineY += LineHeight;
                }
            }
        }

        private static bool IsSevereFlag(string flag) {
            if (string.IsNullOrEmpty(flag)) return false;
            string lower = flag.ToLowerInvariant();
            return lower.Contains("stolen") || lower.Contains("wanted") || lower.Contains("no registration") || lower.Contains("no insurance");
        }

        private static void ResolvePosition(string anchor, int offsetX, int offsetY, int screenW, int screenH, float panelW, float panelH, out float x, out float y) {
            offsetX = Math.Max(0, offsetX);
            offsetY = Math.Max(0, offsetY);
            switch ((anchor ?? "TopRight").ToLowerInvariant()) {
                case "topleft":
                    x = offsetX;
                    y = offsetY;
                    break;
                case "topright":
                    x = screenW - panelW - offsetX;
                    y = offsetY;
                    break;
                case "bottomleft":
                    x = offsetX;
                    y = screenH - panelH - offsetY;
                    break;
                case "bottomright":
                    x = screenW - panelW - offsetX;
                    y = screenH - panelH - offsetY;
                    break;
                default:
                    x = screenW - panelW - offsetX;
                    y = offsetY;
                    break;
            }
        }
    }
}
