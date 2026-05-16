using System;
using System.Drawing;
using LemonUI;
using LemonUI.Elements;
using Alignment = LemonUI.Alignment;

namespace MDTPro.Cloud {
    /// <summary>Centered busy card while HTTP runs (pattern from MDT Pro Lite).</summary>
    internal sealed class CloudBusyOverlay : IProcessable {
        private readonly ScaledRectangle _card;
        private readonly ScaledRectangle _borderTop;
        private readonly ScaledRectangle _borderBottom;
        private readonly ScaledRectangle _borderLeft;
        private readonly ScaledRectangle _borderRight;
        private readonly ScaledText _title;
        private readonly ScaledText _hint;
        private string _hintLine = "";

        internal CloudBusyOverlay() {
            _card = new ScaledRectangle(PointF.Empty, SizeF.Empty) {
                Color = Color.FromArgb(250, 18, 24, 32)
            };
            Color edge = Color.FromArgb(255, 64, 140, 210);
            _borderTop = new ScaledRectangle(PointF.Empty, SizeF.Empty) { Color = edge };
            _borderBottom = new ScaledRectangle(PointF.Empty, SizeF.Empty) { Color = edge };
            _borderLeft = new ScaledRectangle(PointF.Empty, SizeF.Empty) { Color = edge };
            _borderRight = new ScaledRectangle(PointF.Empty, SizeF.Empty) { Color = edge };
            _title = new ScaledText(PointF.Empty, "Signing in…", 0.42f, LemonUI.Elements.Font.ChaletLondon) {
                Color = Color.FromArgb(255, 252, 252, 255),
                Alignment = Alignment.Left,
                WordWrap = 520f
            };
            _hint = new ScaledText(PointF.Empty, "", 0.30f, LemonUI.Elements.Font.ChaletLondon) {
                Color = Color.FromArgb(230, 170, 185, 210),
                Alignment = Alignment.Left,
                WordWrap = 520f
            };
        }

        public bool Visible { get; set; }

        internal void Show(string hintOrNull) {
            _hintLine = string.IsNullOrWhiteSpace(hintOrNull) ? "" : hintOrNull.Trim();
            if (_hintLine.Length > 52) _hintLine = _hintLine.Substring(0, 49) + "…";
            Visible = true;
        }

        internal void Hide() {
            Visible = false;
            _hintLine = "";
        }

        public void Process() {
            if (!Visible) return;
            RectangleF panel = CloudLemonLayout.ComputePanelBounds();
            float cardW = Math.Min(380f, Math.Max(260f, panel.Width * 0.42f));
            float cardH = string.IsNullOrEmpty(_hintLine) ? 68f : 88f;
            float cx = panel.X + panel.Width * 0.5f;
            float cy = panel.Y + panel.Height * 0.46f;
            float x = cx - cardW * 0.5f;
            float y = cy - cardH * 0.5f;
            const float bw = 2f;

            _card.Position = new PointF(x, y);
            _card.Size = new SizeF(cardW, cardH);
            _card.Draw();

            _borderTop.Position = new PointF(x, y);
            _borderTop.Size = new SizeF(cardW, bw);
            _borderBottom.Position = new PointF(x, y + cardH - bw);
            _borderBottom.Size = new SizeF(cardW, bw);
            _borderLeft.Position = new PointF(x, y);
            _borderLeft.Size = new SizeF(bw, cardH);
            _borderRight.Position = new PointF(x + cardW - bw, y);
            _borderRight.Size = new SizeF(bw, cardH);
            _borderTop.Draw();
            _borderBottom.Draw();
            _borderLeft.Draw();
            _borderRight.Draw();

            int step = (Environment.TickCount / 350) % 4;
            string dots = step == 0 ? "" : step == 1 ? "." : step == 2 ? ".." : "...";
            _title.Text = "Signing in" + dots;
            float pad = 20f;
            _title.Position = new PointF(x + pad, y + 16f);
            _title.WordWrap = cardW - pad * 2f;
            _title.Draw();

            if (!string.IsNullOrEmpty(_hintLine)) {
                _hint.Text = _hintLine;
                _hint.Position = new PointF(x + pad, y + 48f);
                _hint.WordWrap = cardW - pad * 2f;
                _hint.Draw();
            }
        }
    }
}
