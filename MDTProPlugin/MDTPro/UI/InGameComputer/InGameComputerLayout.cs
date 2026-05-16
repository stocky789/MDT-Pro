using LemonUI.Elements;
using LemonUI;
using LemonUI.Menus;
using LemonUI.Tools;
using System;
using System.Drawing;

namespace MDTPro.UI.InGameComputer {
    internal static class InGameComputerLayout {
        private static float _aspect = 16f / 9f;
        private static float _height = 1080f;

        internal static void RefreshScreenMetrics() {
            try {
                SizeF resolution = GameScreen.AbsoluteResolution;
                float aspect = GameScreen.AspectRatio;
                if (resolution.Height >= 1f)
                    _height = resolution.Height;
                if (aspect >= 1.2f && aspect <= 3.8f)
                    _aspect = aspect;
            } catch { /* keep last good metrics */ }
        }

        internal static void ApplyMenuColumn(NativeMenu menu) {
            if (menu == null) return;
            RefreshScreenMetrics();

            float virtualWidth = 1080f * _aspect;
            float margin = Clamp(virtualWidth * 0.035f, 34f, 92f);
            float width = MenuWidthForAspect(virtualWidth);
            float x = (virtualWidth - width) * 0.5f;
            float y = MenuTopForHeight();

            if (x < margin) x = margin;
            if (x + width > virtualWidth - margin)
                x = Math.Max(margin, virtualWidth - margin - width);

            menu.Alignment = Alignment.Left;
            menu.Width = width;
            menu.Offset = new PointF(x, y);
            menu.SafeZoneAware = false;
        }

        internal static float BannerHeight => Clamp(70f + (_height / 1080f) * 10f, 70f, 88f);
        internal static float BannerPad => Clamp(BannerHeight * 0.08f, 6f, 8f);
        internal static float BadgeHeight => Clamp(BannerHeight - (BannerPad * 2f), 56f, 74f);
        internal static float BadgeMaxWidthFraction => _aspect >= 2.15f ? 0.13f : 0.17f;

        private static float MenuWidthForAspect(float virtualWidth) {
            if (_aspect >= 2.35f)
                return Clamp(virtualWidth * 0.36f, 620f, 840f);
            if (_aspect >= 2.05f)
                return Clamp(virtualWidth * 0.42f, 620f, 860f);
            if (_aspect <= 1.34f)
                return Clamp(virtualWidth * 0.66f, 520f, 640f);
            if (_aspect <= 1.62f)
                return Clamp(virtualWidth * 0.58f, 560f, 700f);
            return Clamp(virtualWidth * 0.50f, 620f, 820f);
        }

        private static float MenuTopForHeight() {
            float normalized = Clamp(_height / 1080f, 0.66f, 2.2f);
            return Clamp(122f + ((1f - normalized) * 18f), 104f, 132f);
        }

        private static float Clamp(float value, float min, float max) {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
