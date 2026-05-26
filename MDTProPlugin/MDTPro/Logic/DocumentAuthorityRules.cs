using System;
using System.Globalization;

namespace MDTPro.Logic
{
    /// <summary>
    /// Pure authority rules for STP/CDF/PR document data. This file intentionally has no RAGE,
    /// LSPDFR, CDF, or PR dependencies so the fragile document-source decisions can run in CI.
    /// </summary>
    public static class DocumentAuthorityRules
    {
        public static string? ComposeStpLicenseExpiry(string? birthday, string? cdfExpiration)
        {
            if (!TryParseDate(birthday, out DateTime dob)) return null;
            if (!TryParseDate(cdfExpiration, out DateTime cdf)) return null;

            int day = dob.Day;
            int month = dob.Month;
            int year = cdf.Year;
            int maxDay = DateTime.DaysInMonth(year, month);
            if (day > maxDay) day = maxDay;
            return new DateTime(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public static bool ShouldEmitVehicleDocumentExpiration(string? status, string? expiration, bool verifiedFromLiveDocument)
        {
            if (!verifiedFromLiveDocument) return false;
            if (string.IsNullOrWhiteSpace(expiration)) return false;
            if (IsWeakVehicleDocumentStatus(status)) return false;
            return true;
        }

        public static string? ReconcileStpVehicleExpiration(string? stpStatus, string? currentExpiration, bool currentVerified)
        {
            if (!currentVerified) return null;
            if (IsExpiredStatus(stpStatus) && TryParseDate(currentExpiration, out DateTime exp) && exp.Date > DateTime.UtcNow.Date)
                return null;
            return string.IsNullOrWhiteSpace(currentExpiration) ? null : currentExpiration;
        }

        public static bool IsWeakVehicleDocumentStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return true;
            string normalized = status.Trim().ToLowerInvariant();
            return normalized == "unknown" || normalized == "error" || normalized == "missing" || normalized == "n/a" || normalized == "na";
        }

        private static bool IsExpiredStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;
            string normalized = status.Trim().ToLowerInvariant();
            return normalized == "expired" || normalized == "revoked" || normalized == "suspended" || normalized == "invalid";
        }

        private static bool TryParseDate(string? value, out DateTime date)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                date = default(DateTime);
                return false;
            }

            string trimmed = value.Trim();
            string[] formats =
            {
                "yyyy-MM-dd",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
                "yyyy-MM-ddTHH:mm:ssK",
                "M/d/yyyy",
                "MM/dd/yyyy",
                "d/M/yyyy",
                "dd/MM/yyyy"
            };
            if (DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date))
                return true;
            return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date);
        }
    }
}
