using System.IO;

namespace MDTProNative.Wpf.Services;

/// <summary>Persists quick-action notepad (web MDT uses localStorage).</summary>
public static class NotepadStore
{
    static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDTProNative", "notepad.txt");

    public static string Load()
    {
        try
        {
            var p = FilePath;
            var dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return File.Exists(p) ? File.ReadAllText(p) : "";
        }
        catch { return ""; }
    }

    public static void Save(string text)
    {
        try
        {
            var p = FilePath;
            var dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(p, text ?? "");
        }
        catch { /* ignore */ }
    }
}
