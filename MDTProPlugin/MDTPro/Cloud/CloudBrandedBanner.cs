using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using LemonUI.Elements;
using LemonUI.Tools;
using MDTPro.Setup;
using Rage;
using Rage.Native;

namespace MDTPro.Cloud {
    /// <summary>LemonUI menu banner: solid bar + optional MDT Cloud logo (left), same coordinate space as <see cref="ScaledRectangle"/>.</summary>
    internal sealed class CloudBrandedBanner : BaseElement, IDisposable {
        const ulong DrawRectNativeHash = 0x3A618A217E5154F0UL;
        const float BannerHeightScaled = 56f;
        const float PadScaled = 12f;
        /// <summary>Cap logo width as a fraction of banner inner width (was 0.4; higher for readability).</summary>
        const float MaxLogoWidthFrac = 0.52f;

        readonly Color _barColor;
        readonly EventHandler<GraphicsEventArgs> _onRawFrameRender;
        Texture _logoTexture;
        bool _triedLoad;
        bool _loadFailed;
        float _texAspect = 1f;
        RectangleF _logoDestPixels;

        internal CloudBrandedBanner() : base(PointF.Empty, new SizeF(0, BannerHeightScaled)) {
            _barColor = Color.FromArgb(255, 18, 24, 32);
            Color = _barColor;
            _onRawFrameRender = OnRawFrameRender;
            Game.RawFrameRender += _onRawFrameRender;
        }

        public void Dispose() {
            try {
                Game.RawFrameRender -= _onRawFrameRender;
            } catch { /* ignore */ }
            _logoDestPixels = RectangleF.Empty;
            _logoTexture = null;
        }

        void OnRawFrameRender(object sender, GraphicsEventArgs e) {
            if (_loadFailed || _logoTexture == null || e?.Graphics == null)
                return;
            float pw = _logoDestPixels.Width;
            float ph = _logoDestPixels.Height;
            if (pw < 1f || ph < 1f)
                return;
            try {
                e.Graphics.DrawTexture(_logoTexture, _logoDestPixels);
            } catch {
                /* avoid breaking render pass */
            }
        }

        public override void Recalculate() {
            base.Recalculate();
            relativePosition = new PointF(relativePosition.X + relativeSize.Width * 0.5f, relativePosition.Y + relativeSize.Height * 0.5f);
        }

        public override void Draw() {
            if (Size == SizeF.Empty)
                return;
            TryLoadLogoOnce();
            // Same native as LemonUI <see cref="ScaledRectangle.Draw"/> (filled rect, 0–1 coords, center-based position).
            NativeFunction.CallByHash<int>(DrawRectNativeHash,
                relativePosition.X,
                relativePosition.Y,
                relativeSize.Width,
                relativeSize.Height,
                _barColor.R,
                _barColor.G,
                _barColor.B,
                _barColor.A);
            _logoDestPixels = RectangleF.Empty;
            if (_logoTexture == null || _loadFailed)
                return;
            try {
                float cx = relativePosition.X;
                float cy = relativePosition.Y;
                float rw = relativeSize.Width;
                float rh = relativeSize.Height;
                float halfW = rw * 0.5f;
                float halfH = rh * 0.5f;
                float bannerLeft = cx - halfW;
                float bannerTop = cy - halfH;
                float padXN = PadScaled.ToXRelative();
                float padYN = PadScaled.ToYRelative();
                float inLeft = bannerLeft + padXN;
                float inTop = bannerTop + padYN;
                float inW = System.Math.Max(0f, rw - 2f * padXN);
                float inH = System.Math.Max(0f, rh - 2f * padYN);
                float maxLogoW = System.Math.Min(inW * MaxLogoWidthFrac, inW);
                float aspect = GameScreen.AspectRatio;
                if (aspect < 0.5f || aspect > 4f)
                    aspect = 16f / 9f;
                float logoH = inH;
                float logoW = logoH * (_texAspect / aspect);
                if (logoW > maxLogoW) {
                    logoW = maxLogoW;
                    logoH = logoW * (aspect / _texAspect);
                }
                float logoLeft = inLeft;
                float logoTop = inTop + (inH - logoH) * 0.5f;
                int sw = Game.Resolution.Width;
                int sh = Game.Resolution.Height;
                float px = logoLeft * sw;
                float py = logoTop * sh;
                float pw = logoW * sw;
                float ph = logoH * sh;
                if (pw >= 1f && ph >= 1f)
                    _logoDestPixels = new RectangleF(px, py, pw, ph);
            } catch {
                _logoDestPixels = RectangleF.Empty;
            }
        }

        void TryLoadLogoOnce() {
            if (_triedLoad)
                return;
            _triedLoad = true;
            try {
                string path = EnsureLogoFileOnDisk();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                    _loadFailed = true;
                    return;
                }
                _logoTexture = Game.CreateTextureFromFile(path);
                if (_logoTexture == null) {
                    _loadFailed = true;
                    return;
                }
                Size tsz = _logoTexture.Size;
                int tw = tsz.Width;
                int th = System.Math.Max(1, tsz.Height);
                if (tw < 1) {
                    _loadFailed = true;
                    _logoTexture = null;
                    return;
                }
                _texAspect = tw / (float)th;
            } catch {
                _loadFailed = true;
                _logoTexture = null;
            }
        }

        static string EnsureLogoFileOnDisk() {
            try {
                if (!Directory.Exists(SetupController.ImgDirPath))
                    Directory.CreateDirectory(SetupController.ImgDirPath);
                string dest = Path.Combine(SetupController.ImgDirPath, "mdt-cloud-logo.png");
                if (File.Exists(dest)) {
                    try {
                        if (new FileInfo(dest).Length > 0)
                            return dest;
                    } catch { /* rewrite */ }
                }
                Assembly asm = typeof(CloudBrandedBanner).Assembly;
                string res = asm.GetManifestResourceNames()?.FirstOrDefault(n => n.EndsWith("mdt-cloud-logo.png", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(res))
                    return null;
                using (Stream s = asm.GetManifestResourceStream(res)) {
                    if (s == null)
                        return null;
                    using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None)) {
                        s.CopyTo(fs);
                    }
                }
                return dest;
            } catch {
                return null;
            }
        }
    }
}
