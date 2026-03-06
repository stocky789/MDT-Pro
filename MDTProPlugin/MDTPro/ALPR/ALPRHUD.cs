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
        private static readonly ALPRHit ScanningDummyHit = new ALPRHit {
            Plate = "ALPR",
            Owner = "Scanning",
            ModelDisplayName = "",
            Flags = new System.Collections.Generic.List<string>(),
            TimeScanned = System.DateTime.UtcNow
        };

        private const float BasePanelWidth = 240f;
        private const float BasePadding = 10f;
        private const float BaseLineHeight = 18f;
        // PreviewPanelHeight = BasePadding*2 + BaseLineHeight*3 for preview dummy

        private const string FontName = "Arial";
        private const float BaseFontSizeTitle = 15f;
        private const float BaseFontSizeBody = 12f;
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
                var cfg = GetConfig();
                float scale = GetEffectiveScale(cfg);
                if (_previewMode) {
                    DrawPanelAt(e.Graphics, PreviewDummyHit, _previewAnchor, _previewOffsetX, _previewOffsetY, scale);
                    return;
                }
                // Only show ALPR HUD when: enabled in settings, on duty (ALPR only runs then), and in police vehicle.
                if (cfg == null || !cfg.alprEnabled) return;
                if (!IsPlayerInPoliceVehicle()) return;
                DrawPanel(e.Graphics, ALPRController.CurrentHit ?? ScanningDummyHit, cfg);
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

        private static float GetEffectiveScale(Config cfg) {
            if (cfg == null) return 1f;
            return Math.Max(0.75f, Math.Min(2f, cfg.alprHudScale));
        }

        /// <summary>When in preview mode, returns the panel's screen rectangle for hit-testing. Panel top-left follows anchor + offset.</summary>
        internal static bool TryGetPreviewBounds(out float x, out float y, out float w, out float h) {
            float scale = GetEffectiveScale(GetConfig());
            w = BasePanelWidth * scale;
            h = (BasePadding * 2 + BaseLineHeight * 3) * scale;
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
            float scale = GetEffectiveScale(GetConfig());
            float panelW = BasePanelWidth * scale;
            float panelH = (BasePadding * 2 + BaseLineHeight * 3) * scale;
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
            float scale = GetEffectiveScale(cfg);
            DrawPanelAt(g, hit, cfg.alprHudAnchor, cfg.alprHudOffsetX, cfg.alprHudOffsetY, scale);
        }

        private static void DrawPanelAt(Rage.Graphics g, ALPRHit hit, string anchor, int offsetX, int offsetY, float scale) {
            if (hit == null) return;
            if (scale <= 0) scale = 1f;

            float panelW = BasePanelWidth * scale;
            float padding = BasePadding * scale;
            float lineHeight = BaseLineHeight * scale;
            float fontSizeTitle = BaseFontSizeTitle * scale;
            float fontSizeBody = BaseFontSizeBody * scale;
            float accentBarWidth = 4f * scale;

            int actualW = Game.Resolution.Width;
            int actualH = Game.Resolution.Height;
            int w = Math.Max(640, actualW);
            int h = Math.Max(480, actualH);
            int lineCount = 3 + (hit.Flags?.Count ?? 0);
            float panelH = padding * 2 + lineHeight * lineCount;

            float x, y;
            ResolvePosition(anchor ?? "TopRight", offsetX, offsetY, w, h, panelW, panelH, out x, out y);
            // Clamp to actual screen bounds so the panel stays visible on low-resolution displays
            x = Math.Max(0, Math.Min(actualW - panelW, x));
            y = Math.Max(0, Math.Min(actualH - panelH, y));

            var rect = new RectangleF(x, y, panelW, panelH);

            // Background (vanilla GTA–style dark panel)
            g.DrawRectangle(rect, BgColor);
            // Subtle border (drawn first so accent bar sits inside it)
            float borderW = Math.Max(1, (int)(1 * scale));
            g.DrawRectangle(new RectangleF(rect.X, rect.Y, rect.Width, borderW), BorderColor);
            g.DrawRectangle(new RectangleF(rect.X, rect.Y + rect.Height - borderW, rect.Width, borderW), BorderColor);
            g.DrawRectangle(new RectangleF(rect.X, rect.Y, borderW, rect.Height), BorderColor);
            g.DrawRectangle(new RectangleF(rect.X + rect.Width - borderW, rect.Y, borderW, rect.Height), BorderColor);
            // Left accent bar (LSPD blue), inset so it does not overlap the left border
            g.DrawRectangle(new RectangleF(rect.X + borderW, rect.Y, accentBarWidth, rect.Height), AccentColor);

            float lineY = rect.Y + padding;
            float textX = rect.X + borderW + padding + accentBarWidth + 4f * scale;

            // Plate (title, bold look)
            g.DrawText(hit.Plate ?? "", FontName, fontSizeTitle, new PointF(textX, lineY), TextColor);
            lineY += lineHeight;

            // Owner
            int ownerMaxLen = (int)(24 * scale);
            string owner = hit.Owner ?? "";
            if (owner.Length > ownerMaxLen) owner = owner.Substring(0, ownerMaxLen) + "…";
            g.DrawText(owner, FontName, fontSizeBody, new PointF(textX, lineY), TextMutedColor);
            lineY += lineHeight;

            // Model
            string model = hit.ModelDisplayName ?? "";
            int modelMaxLen = (int)(26 * scale);
            if (model.Length > modelMaxLen) model = model.Substring(0, modelMaxLen) + "…";
            g.DrawText(model, FontName, fontSizeBody, new PointF(textX, lineY), TextMutedColor);
            lineY += lineHeight;

            // Flags
            if (hit.Flags != null) {
                foreach (string f in hit.Flags) {
                    Color c = IsSevereFlag(f) ? FlagStolenColor : FlagExpiredColor;
                    g.DrawText("• " + f, FontName, fontSizeBody, new PointF(textX, lineY), c);
                    lineY += lineHeight;
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
