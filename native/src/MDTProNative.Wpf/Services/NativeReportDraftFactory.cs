using MDTProNative.Client;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Services;

/// <summary>Builds new report JSON aligned with browser MDT <c>reports.js</c> (<c>generateReportId</c> + blank templates).</summary>
public static class NativeReportDraftFactory
{
    static readonly Dictionary<string, string> DataPathByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["incident"] = "incidentReports",
        ["citation"] = "citationReports",
        ["arrest"] = "arrestReports",
        ["impound"] = "impoundReports",
        ["trafficIncident"] = "trafficIncidentReports",
        ["injury"] = "injuryReports",
        ["propertyEvidence"] = "propertyEvidenceReports",
    };

    public static string? DataPathFor(string reportType) =>
        DataPathByType.TryGetValue(reportType, out var p) ? p : null;

    public static async Task<JObject?> CreateDraftAsync(MdtHttpClient http, string reportType, CancellationToken cancellationToken = default)
    {
        if (!DataPathByType.ContainsKey(reportType)) return null;

        var config = await http.GetConfigJsonAsync(cancellationToken).ConfigureAwait(false) ?? new JObject();
        var language = await http.GetLanguageJsonAsync(cancellationToken).ConfigureAwait(false) ?? new JObject();

        var dataPath = DataPathByType[reportType];
        JArray reports;
        try
        {
            var reportsTok = await http.GetDataJsonAsync(dataPath, cancellationToken).ConfigureAwait(false);
            reports = reportsTok as JArray ?? new JArray();
        }
        catch
        {
            // Still build a draft (new ID) if the list endpoint fails; user can save from JSON tab if needed.
            reports = new JArray();
        }

        var now = DateTime.Now;
        var shortYearInt = now.Year % 100;
        var shortYearStr = shortYearInt.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        var index = 1;
        foreach (var r in reports.OfType<JObject>())
        {
            var sy = r["ShortYear"]?.Value<int?>();
            if (sy == shortYearInt) index++;
        }

        var typePrefix = ResolveTypePrefix(language, reportType);
        var format = config["reportIdFormat"]?.ToString() ?? "{type}-{shortYear}-{index}";
        var pad = config["reportIdIndexPad"]?.Value<int?>() ?? 6;
        var id = format
            .Replace("{type}", typePrefix, StringComparison.Ordinal)
            .Replace("{shortYear}", shortYearStr, StringComparison.Ordinal)
            .Replace("{year}", now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{month}", now.Month.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{day}", now.Day.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{index}", index.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(pad, '0'), StringComparison.Ordinal);

        JObject? officerTok = null;
        try
        {
            var off = await http.GetDataJsonAsync("officerInformation", cancellationToken).ConfigureAwait(false);
            officerTok = off as JObject ?? JObject.Parse("{}");
        }
        catch
        {
            officerTok = new JObject();
        }

        JObject? locationTok = null;
        try
        {
            var loc = await http.GetDataJsonAsync("playerLocation", cancellationToken).ConfigureAwait(false);
            locationTok = loc as JObject ?? new JObject
            {
                ["Area"] = "",
                ["Street"] = "",
                ["County"] = "",
                ["Postal"] = ""
            };
        }
        catch
        {
            locationTok = new JObject
            {
                ["Area"] = "",
                ["Street"] = "",
                ["County"] = "",
                ["Postal"] = ""
            };
        }

        // Match reports.js fakeReport Status defaults
        var status = reportType.Equals("arrest", StringComparison.OrdinalIgnoreCase) ? 3
            : reportType.Equals("citation", StringComparison.OrdinalIgnoreCase) ? 0
            : 1;

        var root = new JObject
        {
            ["Id"] = id,
            ["ShortYear"] = shortYearInt,
            ["TimeStamp"] = now,
            ["Status"] = status,
            ["Notes"] = "",
            ["OfficerInformation"] = officerTok,
            ["Location"] = locationTok
        };

        switch (reportType.ToLowerInvariant())
        {
            case "incident":
                root["OffenderPedsNames"] = new JArray();
                root["WitnessPedsNames"] = new JArray();
                break;
            case "citation":
                root["Charges"] = new JArray();
                root["OffenderPedName"] = "";
                root["OffenderVehicleLicensePlate"] = "";
                root["CourtCaseNumber"] = JValue.CreateNull();
                break;
            case "arrest":
                root["Charges"] = new JArray();
                root["OffenderPedName"] = "";
                root["OffenderVehicleLicensePlate"] = "";
                root["CourtCaseNumber"] = JValue.CreateNull();
                root["AttachedReportIds"] = new JArray();
                root["DocumentedDrugs"] = false;
                root["DocumentedFirearms"] = false;
                break;
            case "impound":
                root["LicensePlate"] = "";
                root["VehicleModel"] = "";
                root["Owner"] = "";
                root["PersonAtFaultName"] = "";
                root["Vin"] = "";
                root["ImpoundReason"] = "";
                root["TowCompany"] = "";
                root["ImpoundLot"] = "";
                break;
            case "trafficincident":
                root["DriverNames"] = new JArray();
                root["PassengerNames"] = new JArray();
                root["PedestrianNames"] = new JArray();
                root["VehiclePlates"] = new JArray();
                root["VehicleModels"] = new JArray();
                root["InjuryReported"] = false;
                root["InjuryDetails"] = "";
                root["CollisionType"] = "";
                break;
            case "injury":
                root["InjuredPartyName"] = "";
                root["InjuryType"] = "";
                root["Severity"] = "";
                root["Treatment"] = "";
                root["IncidentContext"] = "";
                root["LinkedReportId"] = "";
                break;
            case "propertyevidence":
                root["SubjectPedNames"] = new JArray();
                root["SeizedDrugs"] = new JArray();
                root["SeizedFirearmTypes"] = new JArray();
                root["OtherContrabandNotes"] = "";
                break;
        }

        return root;
    }

    static string ResolveTypePrefix(JObject language, string reportType)
    {
        var map = language["reports"]?["idTypeMap"] as JObject;
        var key = reportType.ToLowerInvariant() switch
        {
            "trafficincident" => "trafficIncident",
            "propertyevidence" => "propertyEvidence",
            _ => reportType
        };
        var fromLang = map?[key]?.ToString();
        if (!string.IsNullOrWhiteSpace(fromLang)) return fromLang!;

        return key switch
        {
            "incident" => "I",
            "citation" => "C",
            "arrest" => "A",
            "impound" => "IMP",
            "trafficIncident" => "TIR",
            "injury" => "INJ",
            "propertyEvidence" => "PER",
            _ => (key.Length >= 3 ? key[..3] : key).ToUpperInvariant()
        };
    }
}
