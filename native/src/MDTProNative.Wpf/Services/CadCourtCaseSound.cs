using System.IO;
using System.Windows.Media;

namespace MDTProNative.Wpf.Services;

/// <summary>Plays bundled <c>courtcase-update.mp3</c> when the Court module successfully updates a docket.</summary>
public static class CadCourtCaseSound
{
    const string BundledFileName = "courtcase-update.mp3";

    static MediaPlayer? _player;
    static string? _openedPath;

    public static void TryPlay()
    {
        if (ImmersionStore.Current.MuteAllSoundEffects) return;
        if (!ImmersionStore.Current.TerminalSounds) return;
        var path = Path.Combine(AppContext.BaseDirectory, BundledFileName);
        if (!File.Exists(path)) return;
        path = Path.GetFullPath(path);
        try
        {
            if (_player == null || _openedPath != path)
            {
                try { _player?.Stop(); } catch { /* ignore */ }
                _player = new MediaPlayer { Volume = 1.0 };
                _player.Open(new Uri(path));
                _openedPath = path;
            }

            _player.Position = TimeSpan.Zero;
            _player.Play();
        }
        catch
        {
            InvalidateCache();
        }
    }

    public static void InvalidateCache()
    {
        try { _player?.Stop(); } catch { /* ignore */ }
        _player = null;
        _openedPath = null;
    }
}
