using Newtonsoft.Json;
using Rage;
using System;
using System.Globalization;

namespace MDTPro.Data.Reports {
    /// <summary>Accepts Status as int, string "0", or name (e.g. Closed). Web <c>dataset.status</c> is always a string.</summary>
    public class ReportStatusJsonConverter : JsonConverter<ReportStatus> {
        public override void WriteJson(JsonWriter writer, ReportStatus value, JsonSerializer serializer) {
            writer.WriteValue((int)value);
        }

        public override ReportStatus ReadJson(JsonReader reader, Type objectType, ReportStatus existingValue, bool hasExistingValue, JsonSerializer serializer) {
            switch (reader.TokenType) {
                case JsonToken.Integer:
                case JsonToken.Float:
                    return (ReportStatus)Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);
                case JsonToken.String:
                    string s = reader.Value?.ToString();
                    if (string.IsNullOrWhiteSpace(s)) return ReportStatus.Open;
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                        return (ReportStatus)i;
                    if (Enum.TryParse(s, true, out ReportStatus byName))
                        return byName;
                    return ReportStatus.Open;
                default:
                    return ReportStatus.Open;
            }
        }
    }

    public class Report {
        public string Id;
        public int ShortYear; // two-digit year; sufficient for practical use
        public OfficerInformationData OfficerInformation;
        public Location Location;
        public DateTime TimeStamp;
        [JsonConverter(typeof(ReportStatusJsonConverter))]
        public ReportStatus Status;
        public string Notes;
    }

    public class Location {
        public string Area;
        public string Street;
        public string County;
        public string Postal;

        internal Location(Vector3 vector3) {
            LSPD_First_Response.Engine.Scripting.WorldZone zone = LSPD_First_Response.Mod.API.Functions.GetZoneAtPosition(vector3);
            Area = zone.RealAreaName;
            Street = World.GetStreetName(vector3);
            County = zone.County.ToString();
            try {
                Postal = CommonDataFramework.Modules.Postals.PostalCodeController.GetPostalCode(vector3);
            } catch {
                Postal = null;
            }
        }

        public Location() { }
    }
    
    public enum ReportStatus {
        Closed,
        Open,
        Canceled,
        /// <summary>Arrest only: filed but not closed for court; user can attach reports.</summary>
        Pending
    }
}
