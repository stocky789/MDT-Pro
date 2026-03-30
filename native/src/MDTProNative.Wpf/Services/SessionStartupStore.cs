using System.IO;
using Newtonsoft.Json;

namespace MDTProNative.Wpf.Services;

/// <summary>Last-used terminal display name and game PC address (not credentials).</summary>
public sealed class SessionStartupSettings
{
    public string? TerminalOfficerName { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 9000;
}

public static class SessionStartupStore
{
    static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDTProNative", "session_startup.json");

    public static SessionStartupSettings Load()
    {
        try
        {
            var p = FilePath;
            if (!File.Exists(p)) return new SessionStartupSettings();
            var json = File.ReadAllText(p);
            var o = JsonConvert.DeserializeObject<SessionStartupSettings>(json);
            return o ?? new SessionStartupSettings();
        }
        catch
        {
            return new SessionStartupSettings();
        }
    }

    public static void Save(SessionStartupSettings s)
    {
        try
        {
            var p = FilePath;
            var dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(p, JsonConvert.SerializeObject(s, Formatting.Indented));
        }
        catch { /* ignore */ }
    }
}
