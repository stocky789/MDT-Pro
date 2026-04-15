using MDTPro.Data;
using MDTPro.Setup;
using MDTPro.Utility;
using Rage;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static MDTPro.Setup.SetupController;

namespace MDTPro.ALPR {
    /// <summary>
    /// Draws the ALPR HUD panel on screen using Rage Graphics API.
    /// </summary>
    internal static class ALPRHUD {
        private static bool _subscribed;
        private static GameFiber _layoutFiber;
        private static volatile bool _runLayoutLoop;
        /// <summary>Set by the layout fiber when Left Alt is held (in-game move/resize hint).</summary>
        internal static volatile bool LayoutModifierHeld;
        private static volatile bool _needsLayoutSave;
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

        private static readonly IReadOnlyList<AlprTerminalRow> PreviewTerminalRows = new List<AlprTerminalRow> {
            new AlprTerminalRow("8QKZ1234", new ALPRHit {
                Plate = "8QKZ1234", Owner = "Preview", ModelDisplayName = "Preview Sedan", VehicleColor = "Black",
                Flags = new List<string> { "Stolen" }, TimeScanned = DateTime.UtcNow
            }, "FL", "STOLN", DateTime.UtcNow),
            new AlprTerminalRow("PREVIEW2", new ALPRHit {
                Plate = "PREVIEW2", Owner = "Row two", ModelDisplayName = "Example", VehicleColor = "White",
                Flags = new List<string>(), TimeScanned = DateTime.UtcNow
            }, "RR", "", DateTime.UtcNow),
        };

        private const float BasePanelWidth = 312f;
        private const float BasePadding = 10f;
        private const float BaseLineHeight = 18f;
        /// <summary>Extra space below content so the hold bar and timer don't get clipped.</summary>
        private const float BaseBottomMargin = 6f;
        // PreviewPanelHeight = BasePadding*2 + BaseLineHeight*3 for preview dummy

        private const string FontName = "Arial";
        private const float BaseFontSizeTitle = 15f;
        private const float BaseFontSizeBody = 12f;
        // Modernized ALPR card style (dark glass + clear state accents)
        private static readonly Color BgOuterColor = Color.FromArgb(230, 10, 14, 22);
        private static readonly Color BgInnerColor = Color.FromArgb(235, 18, 25, 38);
        private static readonly Color BorderColor = Color.FromArgb(220, 53, 68, 92);
        private static readonly Color DividerColor = Color.FromArgb(130, 82, 98, 122);
        private static readonly Color ScanAccentColor = Color.FromArgb(255, 26, 160, 255);
        private static readonly Color AlertAccentColor = Color.FromArgb(255, 255, 95, 90);
        private static readonly Color PillBgColor = Color.FromArgb(170, 30, 40, 58);
        private static readonly Color TextColor = Color.FromArgb(255, 245, 248, 252);
        private static readonly Color TextMutedColor = Color.FromArgb(255, 170, 182, 202);
        private static readonly Color FlagStolenColor = Color.FromArgb(255, 255, 95, 90);
        private static readonly Color FlagExpiredColor = Color.FromArgb(255, 255, 200, 80);
        /// <summary>Registration/insurance/DL etc. — visible in READS but not promoted like stolen/BOLO.</summary>
        private static readonly Color PaperworkRowAccentColor = Color.FromArgb(255, 230, 165, 75);

        internal static void Start() {
            if (_subscribed) return;
            // Use RawFrameRender for stable drawing without flicker (FrameRender queues draws and can flicker).
            // Do not call natives in OnFrameRender; use ALPRController.IsInPoliceVehicleCached and GetConfig() cache only.
            Game.RawFrameRender += OnFrameRender;
            _subscribed = true;
            _runLayoutLoop = true;
            if (_layoutFiber == null || !_layoutFiber.IsAlive)
                _layoutFiber = GameFiber.StartNew(LayoutInteractionLoop);
        }

        internal static void Stop() {
            _runLayoutLoop = false;
            FlushLayoutSaveIfNeeded();
            _layoutFiber?.Abort();
            _layoutFiber = null;
            LayoutModifierHeld = false;
            if (!_subscribed) return;
            Game.RawFrameRender -= OnFrameRender;
            _subscribed = false;
        }

        private static void OnFrameRender(object sender, GraphicsEventArgs e) {
            try {
                var cfg = GetConfig();
                float scale = GetEffectiveScale(cfg);
                if (_previewMode) {
                    DrawAdvancedPanelAt(e.Graphics, cfg, _previewAnchor, _previewOffsetX, _previewOffsetY, scale, PreviewTerminalRows, PreviewDummyHit);
                    return;
                }
                // Only show ALPR HUD when: enabled in settings, on duty (ALPR only runs then), and in police vehicle.
                if (cfg == null || !cfg.alprEnabled) return;
                if (!ALPRController.IsInPoliceVehicleCached) return;
                var rows = ALPRController.GetTerminalRowsSnapshot();
                // Detail follows promoted hits only — never mirror READS row 0 or the panel flickers between plates as the list churns.
                ALPRHit detail = ALPRController.CurrentHit ?? ScanningDummyHit;
                DrawAdvancedPanelAt(e.Graphics, cfg, cfg.alprHudAnchor, cfg.alprHudOffsetX, cfg.alprHudOffsetY, scale, rows, detail);
                DrawLayoutChromeIfApplicable(e.Graphics, cfg);
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
            var cfg = GetConfig();
            float scale = GetEffectiveScale(cfg);
            w = BasePanelWidth * scale * 1.08f;
            int rowCap = AlprDefaults.TerminalMaxRows;
            int listLines = Math.Max(3, Math.Min(rowCap, 8));
            float extraList = (listLines * BaseLineHeight * 0.92f + 28f) * scale;
            h = (BasePadding * 2 + BaseLineHeight * 3) * scale + extraList;
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
            var cfgForH = GetConfig();
            float scale = GetEffectiveScale(cfgForH);
            float panelW = BasePanelWidth * scale * 1.08f;
            int rowCapH = AlprDefaults.TerminalMaxRows;
            int listLinesH = Math.Max(3, Math.Min(rowCapH, 8));
            float extraListH = (listLinesH * BaseLineHeight * 0.92f + 28f) * scale;
            float panelH = (BasePadding * 2 + BaseLineHeight * 3) * scale + extraListH;
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
            int capX = Math.Max(0, (int)Math.Ceiling(actualW - panelW));
            int capY = Math.Max(0, (int)Math.Ceiling(actualH - panelH));
            offsetX = Math.Max(0, Math.Min(capX, offsetX));
            offsetY = Math.Max(0, Math.Min(capY, offsetY));
        }

        private static void LayoutInteractionLoop() {
            bool dragging = false;
            bool resizing = false;
            float dragGrabDx = 0f;
            float dragGrabDy = 0f;
            float resizeStartScale = 1f;
            float resizeAnchorMy = 0f;
            const float gripPx = 28f;
            while (_runLayoutLoop) {
                try {
                    GameFiber.Yield();
                    if (!_runLayoutLoop || !_subscribed) continue;
                    if (_previewMode) {
                        dragging = resizing = false;
                        LayoutModifierHeld = false;
                        FlushLayoutSaveIfNeeded();
                        GameFiber.Sleep(40);
                        continue;
                    }
                    var cfg = GetConfig();
                    if (cfg == null || !cfg.alprEnabled || !ALPRController.IsInPoliceVehicleCached) {
                        FlushLayoutSaveIfNeeded();
                        dragging = resizing = false;
                        LayoutModifierHeld = false;
                        GameFiber.Sleep(80);
                        continue;
                    }
                    LayoutModifierHeld = Game.IsKeyDownRightNow(Keys.LMenu);
                    if (!TryGetLivePanelBounds(cfg, out float px, out float py, out float pw, out float ph)) {
                        GameFiber.Sleep(80);
                        continue;
                    }
                    Rage.MouseState mouse = Game.GetMouseState();
                    float mx = mouse.X;
                    float my = mouse.Y;
                    bool over = mx >= px && mx <= px + pw && my >= py && my <= py + ph;
                    bool onGrip = over && mx >= px + pw - gripPx && my >= py + ph - gripPx;
                    if (!LayoutModifierHeld) {
                        if (dragging || resizing) {
                            dragging = false;
                            resizing = false;
                            FlushLayoutSaveIfNeeded();
                        }
                        GameFiber.Sleep(40);
                        continue;
                    }
                    if (mouse.IsLeftButtonDown) {
                        if (!dragging && !resizing) {
                            if (onGrip) {
                                resizing = true;
                                resizeStartScale = Math.Max(0.75f, Math.Min(2f, cfg.alprHudScale));
                                resizeAnchorMy = my;
                            } else if (over) {
                                dragging = true;
                                dragGrabDx = mx - px;
                                dragGrabDy = my - py;
                            }
                        }
                        if (resizing) {
                            float next = resizeStartScale + (resizeAnchorMy - my) * 0.012f;
                            next = Math.Max(0.75f, Math.Min(2f, next));
                            if (Math.Abs(next - cfg.alprHudScale) > 0.0005f) {
                                cfg.alprHudScale = next;
                                _needsLayoutSave = true;
                            }
                            GameFiber.Sleep(12);
                            continue;
                        }
                        if (dragging) {
                            string anchor = cfg.alprHudAnchor ?? "TopRight";
                            ScreenToOffset(anchor, mx - dragGrabDx, my - dragGrabDy, out int ox, out int oy);
                            cfg.alprHudOffsetX = ox;
                            cfg.alprHudOffsetY = oy;
                            _needsLayoutSave = true;
                            GameFiber.Sleep(12);
                            continue;
                        }
                    } else {
                        if (dragging || resizing) {
                            dragging = false;
                            resizing = false;
                            FlushLayoutSaveIfNeeded();
                        }
                    }
                } catch {
                    /* ignore */
                }
                GameFiber.Sleep(30);
            }
        }

        private static void FlushLayoutSaveIfNeeded() {
            if (!_needsLayoutSave) return;
            try {
                var cfg = GetConfig();
                if (cfg == null) return;
                Helper.WriteToJsonFile(ConfigPath, cfg);
                ResetConfig();
            } catch {
                /* ignore */
            } finally {
                _needsLayoutSave = false;
            }
        }

        /// <summary>Screen rectangle of the live ALPR panel for hit-testing.</summary>
        internal static bool TryGetLivePanelBounds(Config cfg, out float x, out float y, out float w, out float h) {
            x = y = w = h = 0f;
            if (cfg == null || _previewMode) return false;
            float scale = GetEffectiveScale(cfg);
            int actualW = Game.Resolution.Width;
            int actualH = Game.Resolution.Height;
            int scrW = Math.Max(640, actualW);
            int scrH = Math.Max(480, actualH);
            var rows = ALPRController.GetTerminalRowsSnapshot();
            ALPRHit detail = ALPRController.CurrentHit ?? ScanningDummyHit;
            ComputeAdvancedPanelSize(rows, detail, scale, out w, out h);
            ResolvePosition(cfg.alprHudAnchor ?? "TopRight", cfg.alprHudOffsetX, cfg.alprHudOffsetY, scrW, scrH, w, h, out x, out y);
            x = Math.Max(0, Math.Min(actualW - w, x));
            y = Math.Max(0, Math.Min(actualH - h, y));
            return true;
        }

        private static void ComputeAdvancedPanelSize(IReadOnlyList<AlprTerminalRow> rows, ALPRHit detailHit, float scale, out float panelW, out float panelH) {
            int maxRows = AlprDefaults.TerminalMaxRows;
            maxRows = Math.Max(4, Math.Min(12, maxRows));
            panelW = BasePanelWidth * scale * 1.08f;
            float padding = BasePadding * scale;
            float lineHeight = BaseLineHeight * scale;
            float rowCompactH = BaseLineHeight * 0.92f * scale;
            float topBandH = 24f * scale;
            float holdBarH = 3f * scale;
            float sensorBarH = 22f * scale;
            float bottomMargin = BaseBottomMargin * scale;
            int listCount = rows == null ? 0 : Math.Min(maxRows, rows.Count);
            bool listEmpty = listCount == 0;
            int listLineSlots = listEmpty ? 2 : listCount;
            ALPRHit dh = detailHit ?? ScanningDummyHit;
            bool detailScanning = IsScanningPlaceholder(dh);
            bool hasColorLine = !detailScanning && dh != null && !string.IsNullOrWhiteSpace(dh.VehicleColor);
            int detailFlagLines = (!detailScanning && dh?.Flags != null) ? dh.Flags.Count : 0;
            int holdDetailLines = (!detailScanning && ALPRController.IsDetailHoldVisible()) ? 1 : 0;
            int detailBodyLines = detailScanning ? 2 : (3 + (hasColorLine ? 1 : 0) + detailFlagLines + holdDetailLines);
            float listBlockH = topBandH * 0.85f + padding + listLineSlots * rowCompactH + sensorBarH + padding * 0.5f;
            float detailBlockH = padding + lineHeight * detailBodyLines + padding * 0.5f;
            panelH = topBandH + listBlockH + detailBlockH + bottomMargin + holdBarH;
        }

        private static void DrawLayoutChromeIfApplicable(Rage.Graphics g, Config cfg) {
            if (!LayoutModifierHeld || cfg == null || _previewMode) return;
            if (!TryGetLivePanelBounds(cfg, out float px, out float py, out float pw, out float ph)) return;
            var outline = Color.FromArgb(200, 80, 200, 255);
            float t = 2f;
            g.DrawRectangle(new RectangleF(px, py, pw, t), outline);
            g.DrawRectangle(new RectangleF(px, py + ph - t, pw, t), outline);
            g.DrawRectangle(new RectangleF(px, py, t, ph), outline);
            g.DrawRectangle(new RectangleF(px + pw - t, py, t, ph), outline);
            float gsz = 28f;
            var grip = new RectangleF(px + pw - gsz, py + ph - gsz, gsz, gsz);
            g.DrawRectangle(grip, Color.FromArgb(210, 60, 120, 200));
            g.DrawText("SIZE", FontName, 8f, new PointF(grip.X + 2f, grip.Y + grip.Height * 0.35f), TextMutedColor);
        }

        private static void DrawAdvancedPanelAt(Rage.Graphics g, Config cfg, string anchor, int offsetX, int offsetY, float scale,
            IReadOnlyList<AlprTerminalRow> rows, ALPRHit detailHit) {
            if (cfg == null || scale <= 0) scale = 1f;
            int maxRows = AlprDefaults.TerminalMaxRows;
            maxRows = Math.Max(4, Math.Min(12, maxRows));

            float panelW = BasePanelWidth * scale * 1.08f;
            float padding = BasePadding * scale;
            float lineHeight = BaseLineHeight * scale;
            float rowCompactH = BaseLineHeight * 0.92f * scale;
            float fontSizeTitle = (BaseFontSizeTitle + 1f) * scale;
            float fontSizeBody = BaseFontSizeBody * scale;
            float fontSizeSmall = (BaseFontSizeBody - 1f) * scale;
            float borderW = Math.Max(1, (int)(1 * scale));
            float topBandH = 24f * scale;
            float holdBarH = 3f * scale;
            float sensorBarH = 22f * scale;
            float bottomMargin = BaseBottomMargin * scale;

            int listCount = rows == null ? 0 : Math.Min(maxRows, rows.Count);
            bool listEmpty = listCount == 0;
            int listLineSlots = listEmpty ? 2 : listCount;

            ALPRHit dh = detailHit ?? ScanningDummyHit;
            bool detailScanning = IsScanningPlaceholder(dh);
            bool severeAlert = dh != null && AlprHitBuilder.HasSevereAlertForPromotion(dh);
            bool paperworkAlert = dh != null && AlprHitBuilder.HasPaperworkOrLicenseAlert(dh);
            bool hotAlert = severeAlert || paperworkAlert;
            Color accentColor = hotAlert ? AlertAccentColor : ScanAccentColor;

            bool hasColorLine = !detailScanning && dh != null && !string.IsNullOrWhiteSpace(dh.VehicleColor);
            int detailFlagLines = (!detailScanning && dh?.Flags != null) ? dh.Flags.Count : 0;
            int holdDetailLines = (!detailScanning && ALPRController.IsDetailHoldVisible()) ? 1 : 0;
            int detailBodyLines = detailScanning ? 2 : (3 + (hasColorLine ? 1 : 0) + detailFlagLines + holdDetailLines);

            float listBlockH = topBandH * 0.85f + padding + listLineSlots * rowCompactH + sensorBarH + padding * 0.5f;
            float detailBlockH = padding + lineHeight * detailBodyLines + padding * 0.5f;
            float panelH = topBandH + listBlockH + detailBlockH + bottomMargin + holdBarH;

            int actualW = Game.Resolution.Width;
            int actualH = Game.Resolution.Height;
            int w = Math.Max(640, actualW);
            int h = Math.Max(480, actualH);
            float x, y;
            ResolvePosition(anchor ?? "TopRight", offsetX, offsetY, w, h, panelW, panelH, out x, out y);
            x = Math.Max(0, Math.Min(actualW - panelW, x));
            y = Math.Max(0, Math.Min(actualH - panelH, y));

            var rect = new RectangleF(x, y, panelW, panelH);
            g.DrawRectangle(rect, BgOuterColor);
            g.DrawRectangle(new RectangleF(rect.X + borderW, rect.Y + borderW, rect.Width - borderW * 2, rect.Height - borderW * 2), BgInnerColor);
            g.DrawRectangle(new RectangleF(rect.X, rect.Y, rect.Width, borderW), BorderColor);
            g.DrawRectangle(new RectangleF(rect.X, rect.Y + rect.Height - borderW, rect.Width, borderW), BorderColor);
            g.DrawRectangle(new RectangleF(rect.X, rect.Y, borderW, rect.Height), BorderColor);
            g.DrawRectangle(new RectangleF(rect.X + rect.Width - borderW, rect.Y, borderW, rect.Height), BorderColor);

            g.DrawRectangle(new RectangleF(rect.X + borderW, rect.Y + borderW, rect.Width - borderW * 2, topBandH), Color.FromArgb(120, accentColor));
            float contentX = rect.X + padding;
            float lineY = rect.Y + topBandH + padding * 0.35f;

            g.DrawText("MDT ALPR", FontName, fontSizeSmall, new PointF(contentX, rect.Y + 4f * scale), TextColor);
            lineY = rect.Y + topBandH + padding;

            g.DrawText("READS", FontName, fontSizeSmall * 0.95f, new PointF(contentX, lineY), TextMutedColor);
            lineY += rowCompactH * 0.85f;

            if (listEmpty) {
                g.DrawText("Awaiting plate reads…", FontName, fontSizeBody, new PointF(contentX, lineY), TextMutedColor);
                lineY += rowCompactH;
            } else {
                float summaryColX = contentX + 118f * scale;
                float summaryRight = rect.X + rect.Width - borderW - padding - 2f;
                for (int i = 0; i < listCount; i++) {
                    AlprTerminalRow row = rows[i];
                    string plate = SafeTrim(row.Hit?.Plate ?? row.PlateKey, 14);
                    string sens = SafeTrim(row.SensorId, 3);
                    string comp = TrimHudLineToWidth(row.CompactSummary, summaryColX, summaryRight, fontSizeSmall);
                    Color rowAccent = RowAccentForHit(row.Hit);
                    g.DrawText(sens, FontName, fontSizeSmall, new PointF(contentX, lineY), ALPRController.IsTerminalSensorFlashActive(row.SensorId) ? ScanAccentColor : rowAccent);
                    g.DrawText(plate, FontName, fontSizeBody, new PointF(contentX + 28f * scale, lineY), TextColor);
                    if (!string.IsNullOrEmpty(comp))
                        g.DrawText(comp, FontName, fontSizeSmall, new PointF(summaryColX, lineY), rowAccent);
                    lineY += rowCompactH;
                }
            }

            DrawSensorIndicatorRow(g, contentX, lineY, fontSizeSmall, scale);
            lineY += sensorBarH;

            g.DrawRectangle(new RectangleF(contentX, lineY, panelW - padding * 2, 1f), DividerColor);
            lineY += padding * 0.6f;
            g.DrawText("DETAIL", FontName, fontSizeSmall * 0.9f, new PointF(contentX, lineY), TextMutedColor);
            lineY += lineHeight * 0.9f;

            if (detailScanning) {
                g.DrawText("Scanning", FontName, fontSizeTitle, new PointF(contentX, lineY), TextMutedColor);
                lineY += lineHeight;
                g.DrawText("Monitoring nearby plates", FontName, fontSizeBody, new PointF(contentX, lineY), TextMutedColor);
                lineY += lineHeight;
            } else if (dh != null) {
                g.DrawText(SafeTrim(dh.Plate, 22), FontName, fontSizeTitle, new PointF(contentX, lineY), TextColor);
                lineY += lineHeight;
                string owner = SafeTrim(dh.Owner, 28);
                g.DrawText("Owner: " + (string.IsNullOrEmpty(owner) ? "Unknown" : owner), FontName, fontSizeBody, new PointF(contentX, lineY), TextMutedColor);
                lineY += lineHeight;
                string model = SafeTrim(dh.ModelDisplayName, 24);
                g.DrawText("Vehicle: " + (string.IsNullOrEmpty(model) ? "Unknown" : model), FontName, fontSizeBody, new PointF(contentX, lineY), TextMutedColor);
                lineY += lineHeight;
                if (hasColorLine) {
                    g.DrawText("Color: " + SafeTrim(dh.VehicleColor, 28), FontName, fontSizeBody, new PointF(contentX, lineY), TextMutedColor);
                    lineY += lineHeight;
                }
                if (dh.Flags != null) {
                    float detailTextRight = rect.X + rect.Width - borderW - padding - 2f;
                    foreach (string f in dh.Flags) {
                        Color c = IsSevereFlag(f) ? FlagStolenColor : FlagExpiredColor;
                        string line = TrimHudLineToWidth(AlprHitBuilder.FormatFlagForDetailHud(f), contentX, detailTextRight, fontSizeBody);
                        g.DrawText(line, FontName, fontSizeBody, new PointF(contentX, lineY), c);
                        lineY += lineHeight;
                    }
                }
                if (ALPRController.IsDetailHoldVisible()) {
                    int remaining = ALPRController.GetDisplayedHitSecondsRemaining();
                    g.DrawText("Hold: " + remaining + "s", FontName, fontSizeBody, new PointF(contentX, lineY), TextMutedColor);
                    lineY += lineHeight;
                }
            }

            float fillWidth = rect.Width - borderW * 2;
            if (hotAlert && ALPRController.IsDetailHoldVisible()) {
                fillWidth *= ALPRController.GetDisplayedHitHoldRemainingFraction();
            }
            g.DrawRectangle(new RectangleF(rect.X + borderW, rect.Y + rect.Height - holdBarH - borderW, fillWidth, holdBarH), accentColor);
        }

        private static void DrawSensorIndicatorRow(Rage.Graphics g, float contentX, float lineY, float fontSizeSmall, float scale) {
            string[] ids = { "FL", "FR", "RL", "RR" };
            float step = 52f * scale;
            for (int i = 0; i < ids.Length; i++) {
                bool flash = ALPRController.IsTerminalSensorFlashActive(ids[i]);
                var pill = new RectangleF(contentX + i * step, lineY, 40f * scale, 16f * scale);
                g.DrawRectangle(pill, flash ? Color.FromArgb(200, ScanAccentColor) : PillBgColor);
                g.DrawText(ids[i], FontName, fontSizeSmall * 0.9f, new PointF(pill.X + 8f * scale, pill.Y + 1f * scale), flash ? TextColor : TextMutedColor);
            }
        }

        private static bool IsScanningPlaceholder(ALPRHit hit) {
            if (hit == null) return false;
            return string.Equals(hit.Owner ?? string.Empty, "Scanning", StringComparison.OrdinalIgnoreCase) &&
                   !hit.HasFlags;
        }

        private static string SafeTrim(string value, int maxLen) {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (maxLen <= 0 || value.Length <= maxLen) return value;
            return value.Substring(0, maxLen) + "…";
        }

        /// <summary>Shorten text so DrawText at <paramref name="fontSize"/> is unlikely to spill past <paramref name="rightX"/> (Rage has no clip rect).</summary>
        private static string TrimHudLineToWidth(string value, float leftX, float rightX, float fontSize) {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            float avail = rightX - leftX - 2f;
            if (avail < 24f) avail = 24f;
            // Average glyph width for mixed Latin text at small HUD sizes (conservative).
            float approxChar = Math.Max(4.5f, fontSize * 0.52f);
            int maxLen = (int)Math.Floor(avail / approxChar);
            if (maxLen < 10) maxLen = 10;
            if (maxLen > 140) maxLen = 140;
            return SafeTrim(value, maxLen);
        }

        private static bool IsSevereFlag(string flag) {
            if (string.IsNullOrWhiteSpace(flag)) return false;
            string f = flag.Trim();
            return string.Equals(f, "Stolen", StringComparison.OrdinalIgnoreCase)
                || string.Equals(f, "BOLO", StringComparison.OrdinalIgnoreCase)
                || string.Equals(f, "Owner wanted", StringComparison.OrdinalIgnoreCase);
        }

        private static Color RowAccentForHit(ALPRHit hit) {
            if (hit == null) return TextMutedColor;
            if (AlprHitBuilder.HasSevereAlertForPromotion(hit)) return AlertAccentColor;
            if (AlprHitBuilder.HasInterestingFlagsExcludingNotInDatabase(hit)) return PaperworkRowAccentColor;
            return TextMutedColor;
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
