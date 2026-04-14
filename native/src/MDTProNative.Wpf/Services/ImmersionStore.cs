using System.IO;
using Newtonsoft.Json;

namespace MDTProNative.Wpf.Services;

/// <summary>Native MDC immersion toggles (sounds + light motion). Persisted per machine.</summary>
public sealed class ImmersionPreferences
{
    public bool TerminalSounds { get; set; } = true;
    public bool SubtleAnimations { get; set; } = true;
    public bool CalloutChime { get; set; } = true;
    /// <summary>Left-click UI tick (see <see cref="ClickSoundPath"/>).</summary>
    public bool ClickSounds { get; set; } = true;
    /// <summary>Optional full path to a replacement click sound; if null/empty, uses bundled <c>slick-notification.mp3</c>.</summary>
    public string? ClickSoundPath { get; set; }
    /// <summary>Footer mute: silences clicks, bundled MP3s, and Windows system CAD tones.</summary>
    public bool MuteAllSoundEffects { get; set; }
}

public static class ImmersionStore
{
    static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDTProNative", "immersion.json");

    static ImmersionPreferences? _cache;

    public static ImmersionPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new ImmersionPreferences();
            var json = File.ReadAllText(FilePath);
            return JsonConvert.DeserializeObject<ImmersionPreferences>(json) ?? new ImmersionPreferences();
        }
        catch
        {
            return new ImmersionPreferences();
        }
    }

    public static ImmersionPreferences Current => _cache ??= Load();

    public static void Save(ImmersionPreferences p)
    {
        _cache = p;
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(p, Formatting.Indented));
        }
        catch { /* ignore */ }

        CadClickSound.InvalidateCache();
        CadCourtCaseSound.InvalidateCache();
        CadSaveSound.InvalidateCache();
        CadCalloutSound.InvalidateCache();
    }

    public static void ReloadCache()
    {
        _cache = Load();
        CadClickSound.InvalidateCache();
        CadCourtCaseSound.InvalidateCache();
        CadSaveSound.InvalidateCache();
        CadCalloutSound.InvalidateCache();
    }
}
