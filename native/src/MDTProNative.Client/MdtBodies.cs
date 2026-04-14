using Newtonsoft.Json;

namespace MDTProNative.Client;

/// <summary>Request bodies matching browser MDT (JSON strings, etc.).</summary>
public static class MdtBodies
{
    public static string JsonString(string value) => JsonConvert.SerializeObject(value);
}
