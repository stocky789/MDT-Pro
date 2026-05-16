using LemonUI.Elements;
using LemonUI.Tools;
using LemonUI;
using MDTPro.Setup;
using Rage;
using Rage.Native;
using System;
using System.Drawing;
using System.IO;

namespace MDTPro.UI.InGameComputer {
    internal sealed class InGameComputerBanner : BaseElement, IDisposable {
        const ulong DrawRectNativeHash = 0x3A618A217E5154F0UL;

        readonly InGameComputerTheme _theme;
        readonly EventHandler<GraphicsEventArgs> _onRawFrameRender;
        readonly ScaledText _titleText;
        Texture _badgeTexture;
        bool _triedLoad;
        bool _loadFailed;
        float _texAspect = 1f;
        RectangleF _badgeDestPixels;

        internal InGameComputerBanner(InGameComputerTheme theme) : base(PointF.Empty, new SizeF(0, InGameComputerLayout.BannerHeight)) {
            _theme = theme ?? InGameComputerTheme.Resolve(null);
            Color = _theme.Banner;
            _titleText = new ScaledText(PointF.Empty, _theme.NavTitle, 0.92f, LemonUI.Elements.Font.ChaletComprimeCologne) {
                Alignment = Alignment.Center,
                Color = _theme.Text,
                Shadow = true
            };
            _onRawFrameRender = OnRawFrameRender;
            Game.RawFrameRender += _onRawFrameRender;
        }

        public void Dispose() {
            try {
                Game.RawFrameRender -= _onRawFrameRender;
            } catch { /* ignore */ }
            _badgeDestPixels = RectangleF.Empty;
            _badgeTexture = null;
        }

        void OnRawFrameRender(object sender, GraphicsEventArgs e) {
            if (_loadFailed || _badgeTexture == null || e?.Graphics == null)
                return;
            if (_badgeDestPixels.Width < 1f || _badgeDestPixels.Height < 1f)
                return;
            try {
                e.Graphics.DrawTexture(_badgeTexture, _badgeDestPixels);
            } catch {
                /* render pass must not crash the plugin */
            }
        }

        public override void Recalculate() {
            literalSize = new SizeF(literalSize.Width, InGameComputerLayout.BannerHeight);
            base.Recalculate();
            relativePosition = new PointF(relativePosition.X + relativeSize.Width * 0.5f, relativePosition.Y + relativeSize.Height * 0.5f);
        }

        public override void Draw() {
            if (Size == SizeF.Empty)
                return;

            TryLoadBadgeOnce();

            NativeFunction.CallByHash<int>(DrawRectNativeHash,
                relativePosition.X,
                relativePosition.Y,
                relativeSize.Width,
                relativeSize.Height,
                _theme.Banner.R,
                _theme.Banner.G,
                _theme.Banner.B,
                _theme.Banner.A);

            float accentHeight = 3f.ToYRelative();
            NativeFunction.CallByHash<int>(DrawRectNativeHash,
                relativePosition.X,
                relativePosition.Y + (relativeSize.Height * 0.5f) - (accentHeight * 0.5f),
                relativeSize.Width,
                accentHeight,
                _theme.BannerAccent.R,
                _theme.BannerAccent.G,
                _theme.BannerAccent.B,
                255);

            DrawCenteredTitle();

            _badgeDestPixels = RectangleF.Empty;
            if (_badgeTexture == null || _loadFailed)
                return;

            try {
                float cx = relativePosition.X;
                float cy = relativePosition.Y;
                float rw = relativeSize.Width;
                float rh = relativeSize.Height;
                float bannerLeft = cx - rw * 0.5f;
                float bannerTop = cy - rh * 0.5f;
                float padX = InGameComputerLayout.BannerPad.ToXRelative();
                float badgeH = Math.Min(InGameComputerLayout.BadgeHeight.ToYRelative(), Math.Max(0f, rh - (InGameComputerLayout.BannerPad.ToYRelative() * 2f)));
                float badgeW = badgeH * (_texAspect / SafeAspect());
                float maxBadgeW = rw * InGameComputerLayout.BadgeMaxWidthFraction;
                if (badgeW > maxBadgeW) {
                    badgeW = maxBadgeW;
                    badgeH = badgeW * (SafeAspect() / _texAspect);
                }

                float badgeLeft = bannerLeft + padX;
                float badgeTop = bannerTop + (rh - badgeH) * 0.5f;
                float platePadX = 4f.ToXRelative();
                float platePadY = 4f.ToYRelative();
                NativeFunction.CallByHash<int>(DrawRectNativeHash,
                    badgeLeft + (badgeW * 0.5f),
                    badgeTop + (badgeH * 0.5f),
                    badgeW + (platePadX * 2f),
                    badgeH + (platePadY * 2f),
                    4,
                    7,
                    10,
                    95);

                float sw = Math.Max(1f, Game.Resolution.Width);
                float sh = Math.Max(1f, Game.Resolution.Height);
                float px = (float)Math.Round(badgeLeft * sw);
                float py = (float)Math.Round(badgeTop * sh);
                float pw = (float)Math.Round(badgeW * sw);
                float ph = (float)Math.Round(badgeH * sh);
                _badgeDestPixels = new RectangleF(
                    px,
                    py,
                    Math.Max(1f, pw),
                    Math.Max(1f, ph));
            } catch {
                _badgeDestPixels = RectangleF.Empty;
            }
        }

        void DrawCenteredTitle() {
            if (_titleText == null)
                return;
            try {
                float bannerLeft = relativePosition.X - relativeSize.Width * 0.5f;
                float bannerTop = relativePosition.Y - relativeSize.Height * 0.5f;
                float titleX = (bannerLeft + relativeSize.Width * 0.5f).ToXScaled();
                float bannerHeight = relativeSize.Height.ToYScaled();
                float titleY = bannerTop.ToYScaled() + Math.Max(6f, bannerHeight * 0.14f);
                _titleText.Position = new PointF(titleX, titleY);
                _titleText.Draw();
            } catch {
                /* title draw must not crash the plugin */
            }
        }

        void TryLoadBadgeOnce() {
            if (_triedLoad)
                return;
            _triedLoad = true;
            try {
                string path = FindBadgeFile();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                    _loadFailed = true;
                    return;
                }
                _badgeTexture = Game.CreateTextureFromFile(path);
                if (_badgeTexture == null) {
                    _loadFailed = true;
                    return;
                }
                Size size = _badgeTexture.Size;
                if (size.Width < 1 || size.Height < 1) {
                    _badgeTexture = null;
                    _loadFailed = true;
                    return;
                }
                _texAspect = size.Width / (float)size.Height;
            } catch {
                _badgeTexture = null;
                _loadFailed = true;
            }
        }

        string FindBadgeFile() {
            string safeFile = Path.GetFileName(_theme.BadgeFile ?? "");
            if (string.IsNullOrEmpty(safeFile))
                return null;

            string[] roots = {
                Path.Combine(SetupController.MDTProPath, "plugins", "DepartmentStyling", "images"),
                Path.Combine(SetupController.MDTProPath, "plugins", "DepartmentStyling", "image"),
                SetupController.ImgDirPath,
                SetupController.ImgDefaultsDirPath
            };

            foreach (string root in roots) {
                try {
                    if (string.IsNullOrEmpty(root)) continue;
                    string path = Path.Combine(root, safeFile);
                    if (File.Exists(path)) return path;
                } catch { /* try next path */ }
            }

            return null;
        }

        static float SafeAspect() {
            float aspect = GameScreen.AspectRatio;
            return (aspect < 0.5f || aspect > 4f) ? 16f / 9f : aspect;
        }
    }
}
