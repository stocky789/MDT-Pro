using System.IO;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Client;

/// <summary>
/// Bundled copies of <c>MDTPro/defaults/citationOptions.json</c> and <c>arrestOptions.json</c> so native report
/// combos still populate when GET fails (proxy/loopback issues, empty server file, or JSON null).
/// </summary>
public static class MdtEmbeddedChargeOptions
{
    internal const string CitationResourceName = "MDTProNative.Client.defaults.citationOptions.json";
    internal const string ArrestResourceName = "MDTProNative.Client.defaults.arrestOptions.json";

    /// <summary>Root JSON array (charge groups), or null if the embedded file is missing or invalid.</summary>
    public static JArray? LoadCitationRoot() => LoadRoot(CitationResourceName);

    /// <summary>Root JSON array (charge groups), or null if the embedded file is missing or invalid.</summary>
    public static JArray? LoadArrestRoot() => LoadRoot(ArrestResourceName);

    static JArray? LoadRoot(string logicalName)
    {
        var asm = typeof(MdtEmbeddedChargeOptions).Assembly;
        using var s = asm.GetManifestResourceStream(logicalName);
        if (s == null) return null;
        using var reader = new StreamReader(s);
        var text = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(text)) return null;
        JToken t;
        try
        {
            t = JToken.Parse(text);
        }
        catch
        {
            return null;
        }

        if (t.Type == JTokenType.Null) return null;
        return t as JArray;
    }
}
