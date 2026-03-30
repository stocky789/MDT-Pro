using System;
using System.Globalization;
using Newtonsoft.Json;

namespace MDTPro.Data {
    public class OfficerInformationData {
        public string firstName;
        public string lastName;
        public string rank;
        public string callSign;
        public string agency;
        /// <summary>LSPDFR agency script name (e.g. LSPD, LSSD) for report branding / mapping.</summary>
        public string agencyScriptName;
        /// <summary>Browser MDT sends badge as a string; values may be empty or non-numeric. Strict int? deserialization would throw and fail create*Report with HTTP 500.</summary>
        [JsonConverter(typeof(LooseNullableInt32JsonConverter))]
        public int? badgeNumber;
    }

    /// <summary>Parses JSON int, float, or string into <see cref="Nullable{Int32}"/>; empty or non-numeric strings become null (same rules as <c>ParseOfficerInformationPostBody</c>).</summary>
    internal sealed class LooseNullableInt32JsonConverter : JsonConverter<int?> {
        public override void WriteJson(JsonWriter writer, int? value, JsonSerializer serializer) {
            if (value.HasValue) writer.WriteValue(value.Value);
            else writer.WriteNull();
        }

        public override int? ReadJson(JsonReader reader, Type objectType, int? existingValue, bool hasExistingValue, JsonSerializer serializer) {
            switch (reader.TokenType) {
                case JsonToken.Null:
                case JsonToken.Undefined:
                    return null;
                case JsonToken.Integer:
                    return Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);
                case JsonToken.Float:
                    double d = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                    if (double.IsNaN(d) || double.IsInfinity(d)) return null;
                    long r = (long)Math.Round(d);
                    if (r < int.MinValue || r > int.MaxValue) return null;
                    return (int)r;
                case JsonToken.String:
                    string s = reader.Value?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(s)) return null;
                    return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : (int?)null;
                default:
                    return null;
            }
        }
    }
}
