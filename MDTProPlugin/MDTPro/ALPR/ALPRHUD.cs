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
        private static bool _subscribed;
        private static bool _previewMode;
        private static string _previewAnchor = "TopRight";
        private static int _previewOffsetX = 20;
        private static int _previewOffsetY = 150;
        private static readonly ALPRHit PreviewDummyHit = new ALPRHit {
            Plate = "PREVIEW",
            Owner = "ALPR panel position",
            ModelDisplayName = "Drag to move · Enter save · Backspace cancel",
            Flags = new System.Collections.Generic.List<string>(),
            TimeScanned = System.DateTime.UtcNow
        };

        private const float PanelWidth = 240f;
        private const float PreviewPanelHeight = 74f; // Padding*2 + LineHeight*3 for preview dummy

        private const string FontName = "Arial";
        private const float FontSizeTitle = 15f;
        private const float FontSizeBody = 12f;
        private const float LineHeight = 18f;
        private const float Padding = 10f;
        // Vanilla GTA 5–style: dark panel, subtle border, LSPD blue accent
        private static readonly Color BgColor = Color.FromArgb(230, 22, 22, 28);
        private static readonly Color BorderColor = Color.FromArgb(200, 45, 55, 72);
        private static readonly Color AccentColor = Color.FromArgb(255, 0, 120, 215); // LSPD blue
        private static readonly Color TextColor = Color.FromArgb(255, 240, 240, 240);
        private static readonly Color TextMutedColor = Color.FromArgb(255, 180, 185, 195);
        private static readonly Color FlagStolenColor = Color.FromArgb(255, 255, 95, 90);
        private static readonly Color FlagExpiredColor = Color.FromArgb(255, 255, 200, 80);

        internal static void Start() {
            if (_subscribed) return;
            // Use FrameRender so draw calls are queued with the game's renderer and stay visible
            // (RawFrameRender draws too early and gets overdrawn by the game HUD)
            Game.FrameRender += OnFrameRender;
            _subscribed = true;
        }

        internal static void Stop() {
            if (!_subscribed) return;
            Game.FrameRender -= OnFrameRender;
            _subscribed = false;
        }

        private static void OnFrameRender(object sender, GraphicsEventArgs e) {
            try {
                if (_previewMode) {
                    DrawPanelAt(e.Graphics, PreviewDummyHit, _previewAnchor, _previewOffsetX, _previewOffsetY);
                    return;
                }
                // Only show ALPR HUD when: enabled in settings, on duty (ALPR only runs then), and in police vehicle.
                if (ALPRController.CurrentHit == null) return;
                var cfg = GetConfig();
                if (cfg == null || !cfg.alprEnabled) return;
                if (!IsPlayerInPoliceVehicle()) return;
                DrawPanel(e.Graphics, ALPRController.CurrentHit, cfg);
            } catch (Exception) {
                // Silently ignore draw errors (e.g. during unload)
            }
        }

        /// <summary>Start drawing a preview panel at the given position (for in-menu repositioning).</summary>
        internal static void StartPreviewMode(string anchor, int offsetX, int offsetY) {
            _previewMode = true;
            _previewAnchor = anchor ?? "TopRight";
            _previewOffsetX = Math.Max(0, offsetX);
            _previewOffsetY = Math.Max(0, offsetY);
        }

        /// <summary>Update preview position (arrow-key move mode).</summary>
        internal static void UpdatePreviewPosition(int offsetX, int offsetY) {
            _previewOffsetX = Math.Max(0, offsetX);
            _previewOffsetY = Math.Max(0, offsetY);
        }

        /// <summary>Stop preview and resume normal HUD (if any).</summary>
        internal static void EndPreviewMode() {
            _previewMode = false;
        }

        /// <summary>When in preview mode, returns the panel's screen rectangle for hit-testing. Panel top-left follows anchor + offset.</summary>
        internal static bool TryGetPreviewBounds(out float x, out float y, out float w, out float h) {
            w = PanelWidth;
            h = PreviewPanelHeight;
            if (!_previewMode) {
                x = y = 0;
                return false;
            }
            int actualW = Game.Resolution.Width;
            int actualH = Game.Resolution.Height;
            int scrW = Math.Max(640, actualW);
            int scrH = Math.Max(480, actualH);
            ResolvePosition(_previewAnchor, _previewOffsetX, _previewOffsetY, scrW, scrH, w, h, out x, out y);
            x = Math.Max(0, Math.Min(actualW - w, x));
            y = Math.Max(0, Math.Min(actualH - h, y));
            return true;
        }

        /// <summary>Convert desired panel top-left (screen coords) to anchor-relative offset. Clamps to valid range.</summary>
        internal static void ScreenToOffset(string anchor, float screenX, float screenY, out int offsetX, out int offsetY) {
            int actualW = Game.Resolution.Width;
            int actualH = Game.Resolution.Height;
            float panelW = PanelWidth;
            float panelH = PreviewPanelHeight;
            screenX = Math.Max(0, Math.Min(actualW - panelW, screenX));
            screenY = Math.Max(0, Math.Min(actualH - panelH, screenY));
            switch ((anchor ?? "TopRight").ToLowerInvariant()) {
                case "topleft":
                    offsetX = (int)screenX;
                    offsetY = (int)screenY;
                    break;
                case "topright":
                    offsetX = (int)(actualW - panelW - screenX);
                    offsetY = (int)screenY;
                    break;
                case "bottomleft":
                    offsetX = (int)screenX;
                    offsetY = (int)(actualH - panelH - screenY);
                    break;
                case "bottomright":
                    offsetX = (int)(actualW - panelW - screenX);
                    offsetY = (int)(actualH - panelH - screenY);
                    break;
                default:
                    offsetX = (int)(actualW - panelW - screenX);
                    offsetY = (int)screenY;
                    break;
            }
            offsetX = Math.Max(0, Math.Min(400, offsetX));
            offsetY = Math.Max(0, Math.Min(400, offsetY));
        }

        private static bool IsPlayerInPoliceVehicle() {
            if (Main.Player == null || !Main.Player.Exists()) return false;
            Vehicle v = Main.Player.CurrentVehicle;
            return v != null && v.Exists() && v.IsPoliceVehicle;
        }

        private static void DrawPanel(Rage.Graphics g, ALPRHit hit, Config cfg) {
            if (hit == null || cfg == null) return;
            DrawPanelAt(g, hit, cfg.alprHudAnchor, cfg.alprHudOffsetX, cfg.alprHudOffsetY);
        }

        private static void DrawPanelAt(Rage.Graphics g, ALPRHit hit, string anchor, int offsetX, int offsetY) {
            if (hit == null) return;
            int actualW = Game.Resolution.Width;
            int actualH = Game.Resolution.Height;
            int w = Math.Max(640, actualW);
            int h = Math.Max(480, actualH);
            float panelW = PanelWidth;
            int lineCount = 3 + (hit.Flags?.Count ?? 0);
            float panelH = Padding * 2 + LineHeight * lineCount;

            float x, y;
            ResolvePosition(anchor ?? "TopRight", offsetX, offsetY, w, h, panelW, panelH, out x, out y);
            // Clamp to actual screen bounds so the panel stays visible on low-resolution displays
            x = Math.Max(0, Math.Min(actualW - panelW, x));
            y = Math.Max(0, Math.Min(actualH - panelH, y));

            var rect = new RectangleF(x, y, panelW, panelH);
            const float AccentBarWidth = 4f;

            // Background (vanilla GTA–style dark panel)
            g.DrawRectangle(rect, BgColor);
            // Subtle border (drawn first so accent bar sits inside it)
            g.DrawRectangle(new RectangleF(rect.X, rect.Y, rect.Width, 1), BorderColor);
            g.DrawRectangle(new RectangleF(rect.X, rect.Y + rect.Height - 1, rect.Width, 1), BorderColor);
            g.DrawRectangle(new RectangleF(rect.X, rect.Y, 1, rect.Height), BorderColor);
            g.DrawRectangle(new RectangleF(rect.X + rect.Width - 1, rect.Y, 1, rect.Height), BorderColor);
            // Left accent bar (LSPD blue), inset by 1px so it does not overlap the left border
            g.DrawRectangle(new RectangleF(rect.X + 1, rect.Y, AccentBarWidth, rect.Height), AccentColor);

            float lineY = rect.Y + Padding;
            float textX = rect.X + 1 + Padding + AccentBarWidth + 4f;

            // Plate (title, bold look)
            g.DrawText(hit.Plate ?? "", FontName, FontSizeTitle, new PointF(textX, lineY), TextColor);
            lineY += LineHeight;

            // Owner
            g.DrawText((hit.Owner ?? "").Length > 24 ? (hit.Owner ?? "").Substring(0, 24) + "…" : (hit.Owner ?? ""), FontName, FontSizeBody, new PointF(textX, lineY), TextMutedColor);
            lineY += LineHeight;

            // Model
            string model = hit.ModelDisplayName ?? "";
            if (model.Length > 26) model = model.Substring(0, 26) + "…";
            g.DrawText(model, FontName, FontSizeBody, new PointF(textX, lineY), TextMutedColor);
            lineY += LineHeight;

            // Flags
            if (hit.Flags != null) {
                foreach (string f in hit.Flags) {
                    Color c = IsSevereFlag(f) ? FlagStolenColor : FlagExpiredColor;
                    g.DrawText("• " + f, FontName, FontSizeBody, new PointF(textX, lineY), c);
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
