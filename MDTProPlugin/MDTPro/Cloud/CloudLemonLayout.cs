using System.Drawing;
using LemonUI;
using LemonUI.Menus;
using LemonUI.Tools;

namespace MDTPro.Cloud {
    /// <summary>Virtual canvas layout for cloud LemonUI (adapted from MDT Pro Lite layout ideas).</summary>
    internal static class CloudLemonLayout {
        private static float _cachedAspect = 16f / 9f;
        private static float _cachedAbsW = 1920f;
        private static float _cachedAbsH = 1080f;

        internal static void RefreshScreenMetrics() {
            try {
                SizeF res = GameScreen.AbsoluteResolution;
                float w = res.Width;
                float h = res.Height;
                float a = GameScreen.AspectRatio;
                if (w >= 1f && h >= 1f) {
                    _cachedAbsW = w;
                    _cachedAbsH = h;
                }
                if (a >= 0.5f && a <= 4f)
                    _cachedAspect = a;
            } catch { /* keep cache */ }
        }

        private static float Clamp(float v, float min, float max) => System.Math.Max(min, System.Math.Min(max, v));

        internal static float VirtualWidth => 1080f * _cachedAspect;
        internal static float VirtualHeight => 1080f;
        internal static float PanelPad => Clamp(VirtualHeight * 0.013f, 10f, 22f);

        private static float ReferenceHeightPx => System.Math.Max(1f, _cachedAbsH);

        /// <summary>Centered panel in virtual coordinates (simplified Lite <see cref="ComputePanelBounds"/>).</summary>
        internal static RectangleF ComputePanelBounds() {
            float vw = VirtualWidth;
            float vh = VirtualHeight;
            float vMin = System.Math.Min(vw, vh);
            float margin = Clamp(vMin * 0.02f, 8f, 32f);
            float innerW = System.Math.Max(0f, vw - 2f * margin);
            float innerH = System.Math.Max(0f, vh - 2f * margin);
            float targetW = innerW * 0.94f;
            float targetH = innerH * 0.90f;
            float aspect = _cachedAspect;
            if (aspect >= 2.15f)
                targetW = System.Math.Min(targetW, vh * 1.52f);
            else if (aspect >= 1.95f)
                targetW = System.Math.Min(targetW, vh * 1.72f);
            float minW = System.Math.Max(320f, innerW * 0.46f);
            float minH = System.Math.Max(400f, innerH * 0.50f);
            targetW = Clamp(targetW, minW, innerW);
            targetH = Clamp(targetH, minH, innerH);
            float cx = vw * 0.5f;
            float cy = vh * 0.5f;
            float x = cx - targetW * 0.5f;
            float y = cy - targetH * 0.5f;
            if (x < margin) x = margin;
            if (y < margin) y = margin;
            if (x + targetW > vw - margin) x = vw - margin - targetW;
            if (y + targetH > vh - margin) y = vh - margin - targetH;
            return new RectangleF(x, y, targetW, targetH);
        }

        private static float GetMenuWidth(RectangleF panel) {
            float w = panel.Width - 2f * PanelPad - 12f;
            float minMenu = System.Math.Max(260f, panel.Width * 0.34f);
            if (w < minMenu) w = minMenu;
            return w;
        }

        private static PointF ComputeMenuOffset(float menuWidth) {
            RectangleF panel = ComputePanelBounds();
            float contentLeft = panel.X + PanelPad;
            float contentW = panel.Width - 2f * PanelPad;
            float x = contentLeft + System.Math.Max(0f, (contentW - menuWidth) * 0.5f);
            float y = panel.Y + panel.Height * 0.38f;
            if (x < 6f) x = 6f;
            return new PointF(x, y);
        }

        internal static void ApplyMenuColumn(NativeMenu menu) {
            if (menu == null) return;
            float w = GetMenuWidth(ComputePanelBounds());
            menu.Alignment = Alignment.Left;
            menu.Width = w;
            menu.Offset = ComputeMenuOffset(w);
            menu.SafeZoneAware = false;
        }
    }
}
