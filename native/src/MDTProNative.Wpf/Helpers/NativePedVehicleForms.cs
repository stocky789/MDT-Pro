using System.Windows.Controls;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Helpers;

/// <summary>Shared helpers for native ped/vehicle structured forms (JSON field names match plugin models).</summary>
public static class NativePedVehicleForms
{
    public static void SetStr(JObject o, string key, string? value) =>
        o[key] = (value ?? "").Trim();

    public static void SetBool(JObject o, string key, CheckBox? cb) =>
        o[key] = cb?.IsChecked == true;

    public static void SetInt(JObject o, string key, string text, int fallback)
    {
        if (int.TryParse((text ?? "").Trim(), out var n))
            o[key] = n;
        else
            o[key] = fallback;
    }

    public static string GetStr(JToken? tok) =>
        tok == null || tok.Type == JTokenType.Null ? "" : tok.ToString();

    public static bool GetBool(JToken? tok) =>
        tok?.Type == JTokenType.Boolean && tok.Value<bool>();

    public static int GetInt(JToken? tok, int fallback)
    {
        if (tok == null || tok.Type == JTokenType.Null) return fallback;
        if (tok.Type == JTokenType.Integer) return tok.Value<int>();
        if (int.TryParse(tok.ToString(), out var n)) return n;
        return fallback;
    }
}
