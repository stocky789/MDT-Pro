using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MDTProNative.Wpf.Services;

/// <summary>Plays a short UI click sound on left mouse button (bundled <c>slick-notification.mp3</c> next to the exe).</summary>
public static class CadClickSound
{
    static SoundPlayer? _wav;
    static string? _wavPath;
    static MediaPlayer? _media;
    static string? _mediaPath;

    public static void RegisterGlobalClickHandler()
    {
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnWindowPreviewLeftDown), handledEventsToo: true);
    }

    static void OnWindowPreviewLeftDown(object sender, MouseButtonEventArgs e) => TryPlay();

    public static void InvalidateCache()
    {
        try { _wav?.Stop(); } catch { /* ignore */ }
        _wav = null;
        _wavPath = null;
        try { _media?.Stop(); } catch { /* ignore */ }
        _media = null;
        _mediaPath = null;
    }

    public static void TryPlay()
    {
        if (ImmersionStore.Current.MuteAllSoundEffects) return;
        if (!ImmersionStore.Current.ClickSounds) return;
        var path = ResolvePath();
        if (string.IsNullOrEmpty(path)) return;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (ext == ".wav")
            {
                if (_wavPath != path)
                {
                    try { _wav?.Stop(); } catch { /* ignore */ }
                    _wav = new SoundPlayer(path);
                    _wav.Load();
                    _wavPath = path;
                }

                _wav?.Play();
            }
            else
            {
                if (_mediaPath != path || _media == null)
                {
                    try { _media?.Stop(); } catch { /* ignore */ }
                    _media = new MediaPlayer { Volume = 1.0 };
                    _media.Open(new Uri(Path.GetFullPath(path)));
                    _mediaPath = path;
                }

                if (_media != null)
                {
                    _media.Position = TimeSpan.Zero;
                    _media.Play();
                }
            }
        }
        catch
        {
            InvalidateCache();
        }
    }

    static string? ResolvePath()
    {
        var prefs = ImmersionStore.Current;
        if (!string.IsNullOrWhiteSpace(prefs.ClickSoundPath))
        {
            var custom = prefs.ClickSoundPath.Trim();
            if (File.Exists(custom)) return Path.GetFullPath(custom);
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "slick-notification.mp3");
        return File.Exists(bundled) ? bundled : null;
    }
}
