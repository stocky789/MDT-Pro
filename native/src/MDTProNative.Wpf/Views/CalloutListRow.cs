using MDTProNative.Wpf.Helpers;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views;

/// <summary>One row in the native callout queue (bound from WebSocket <c>callouts</c> items).</summary>
public sealed class CalloutListRow
{
    CalloutListRow(string id, string headline, string locationLine, string priorityLabel, string statusLabel, int acceptanceState, JObject source)
    {
        Id = id;
        Headline = headline;
        LocationLine = locationLine;
        PriorityLabel = priorityLabel;
        StatusLabel = statusLabel;
        AcceptanceState = acceptanceState;
        Source = source;
    }

    public string Id { get; }
    public string Headline { get; }
    public string LocationLine { get; }
    public string PriorityLabel { get; }
    public string StatusLabel { get; }
    public int AcceptanceState { get; }
    public JObject Source { get; }

    public static CalloutListRow? TryFromToken(JToken? token)
    {
        if (token is not JObject o) return null;
        var id = o["Id"]?.ToString() ?? o["id"]?.ToString();
        if (string.IsNullOrWhiteSpace(id)) return null;
        var name = o["Name"]?.ToString() ?? o["name"]?.ToString() ?? "(callout)";
        var locTok = o["Location"] ?? o["location"];
        var loc = locTok != null ? JTokenDisplay.FormatLocation(locTok) : "";
        var pri = o["Priority"]?.ToString() ?? o["priority"]?.ToString() ?? "";
        var state = ReadAcceptanceState(o);
        var status = StatusLabelFor(state);
        return new CalloutListRow(id, name, loc, string.IsNullOrWhiteSpace(pri) ? "—" : pri, status, state, o);
    }

    static int ReadAcceptanceState(JObject o)
    {
        var t = o["AcceptanceState"] ?? o["acceptanceState"];
        if (t == null || t.Type == JTokenType.Null) return 0;
        if (t.Type == JTokenType.Integer) return t.Value<int>();
        if (int.TryParse(t.ToString(), out var n)) return n;
        return 0;
    }

    /// <summary>LSPDFR <c>CalloutAcceptanceState</c>: 0 = Pending (shown as Open in CAD), 1 = Responded, …</summary>
    public static string StatusLabelFor(int state) => state switch
    {
        0 => "Open",
        1 => "Responded",
        2 => "En route",
        3 => "Finished",
        _ => "—"
    };
}
