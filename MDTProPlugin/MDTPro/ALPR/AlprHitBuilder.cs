using CommonDataFramework.Modules.PedDatabase;
using CommonDataFramework.Modules.VehicleDatabase;
using MDTPro.Data;
using MDTPro.Utility;
using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace MDTPro.ALPR {
    /// <summary>Builds <see cref="ALPRHit"/> and compact terminal lines from live vehicles using CDF + MDT persistence (MDT-owned).</summary>
    internal static class AlprHitBuilder {
        internal static ALPRHit BuildFromVehicle(Vehicle toScan) {
            if (toScan == null || !toScan.Exists()) return null;
            string plate = toScan.LicensePlate?.Trim();
            if (string.IsNullOrEmpty(plate)) return null;

            MDTProVehicleData vd = new MDTProVehicleData(toScan);
            MDTProVehicleData dbVehicle = DataController.GetVehicleByLicensePlate(plate);
            List<string> flags;
            string owner;
            string modelDisplayName;

            if (vd.CDFVehicleData?.Owner != null) {
                flags = BuildFlagsFromLiveCdfVehicle(vd, dbVehicle);
                modelDisplayName = vd.ModelDisplayName ?? vd.ModelName ?? "";
                owner = (dbVehicle != null && !string.IsNullOrEmpty(dbVehicle.Owner))
                    ? dbVehicle.Owner
                    : (vd.Owner ?? "");
            } else {
                if (dbVehicle != null) {
                    flags = BuildFlagsPersistedVehicleOnly(dbVehicle);
                    owner = dbVehicle.Owner ?? "—";
                    modelDisplayName = dbVehicle.ModelDisplayName ?? dbVehicle.ModelName ?? "";
                } else {
                    flags = new List<string> { "Not in database" };
                    owner = "—";
                    modelDisplayName = "";
                }
                if (string.IsNullOrEmpty(modelDisplayName))
                    modelDisplayName = GetModelDisplayName(toScan);
            }

            if (string.IsNullOrEmpty(modelDisplayName))
                modelDisplayName = GetModelDisplayName(toScan);

            if (dbVehicle != null && dbVehicle.IsStolen && flags != null && !flags.Any(f => string.Equals(f, "Stolen", StringComparison.OrdinalIgnoreCase)))
                flags.Add("Stolen");

            string vehicleColor = GetVehicleColorDisplay(vd, dbVehicle);

            return new ALPRHit {
                Plate = vd.LicensePlate ?? plate,
                Owner = owner,
                ModelDisplayName = modelDisplayName,
                VehicleColor = vehicleColor,
                Flags = flags,
                TimeScanned = DateTime.UtcNow
            };
        }

        /// <summary>Single-line summary for terminal READS column: short LE-style hit codes so text fits the HUD width.</summary>
        internal static string BuildCompactSummary(ALPRHit hit) {
            if (hit?.Flags == null || hit.Flags.Count == 0) return "";
            var parts = new List<string>();
            foreach (string raw in hit.Flags) {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string f = raw.Trim();
                string code = TerminalHitCode(f);
                if (!string.IsNullOrEmpty(code))
                    parts.Add(code);
            }
            return string.Join(" ", parts);
        }

        /// <summary>Maps a full flag string to a short terminal token (fixed-ish width, similar to real ALPR hit lists).</summary>
        static string TerminalHitCode(string f) {
            if (string.Equals(f, "Not in database", StringComparison.OrdinalIgnoreCase)) return "NO DATA";
            if (string.Equals(f, "Stolen", StringComparison.OrdinalIgnoreCase)) return "STOLEN";
            if (string.Equals(f, "BOLO", StringComparison.OrdinalIgnoreCase)) return "BOLO";
            if (string.Equals(f, "Owner wanted", StringComparison.OrdinalIgnoreCase)) return "WANTED";
            if (string.Equals(f, "Owner unlicensed", StringComparison.OrdinalIgnoreCase)) return "NO DL";
            if (string.Equals(f, "Owner license suspended", StringComparison.OrdinalIgnoreCase)) return "DL SUSP";
            if (string.Equals(f, "Owner license revoked", StringComparison.OrdinalIgnoreCase)) return "DL REV";
            if (string.Equals(f, "Owner license expired", StringComparison.OrdinalIgnoreCase)) return "DL EXP";
            if (string.Equals(f, "Registration expired", StringComparison.OrdinalIgnoreCase)) return "REG EXP";
            if (string.Equals(f, "No registration", StringComparison.OrdinalIgnoreCase)) return "NO REG";
            if (string.Equals(f, "Insurance expired", StringComparison.OrdinalIgnoreCase)) return "INS EXP";
            if (string.Equals(f, "No insurance", StringComparison.OrdinalIgnoreCase)) return "NO INS";
            if (f.Length <= 14) return f.ToUpperInvariant();
            return (f.Length > 12 ? f.Substring(0, 12).Trim() : f).ToUpperInvariant() + "…";
        }

        /// <summary>Two-column style line for the detail pane (fixed-field look similar to commercial ALPR status lines).</summary>
        internal static string FormatFlagForDetailHud(string raw) {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string t = raw.Trim();
            if (string.Equals(t, "Not in database", StringComparison.OrdinalIgnoreCase)) return "VEHICLE   NOT IN MDT";
            if (string.Equals(t, "Stolen", StringComparison.OrdinalIgnoreCase)) return "STATUS      STOLEN";
            if (string.Equals(t, "BOLO", StringComparison.OrdinalIgnoreCase)) return "STATUS      BOLO";
            if (string.Equals(t, "Owner wanted", StringComparison.OrdinalIgnoreCase)) return "OWNER       WANTED";
            if (string.Equals(t, "Owner unlicensed", StringComparison.OrdinalIgnoreCase)) return "DL          NONE";
            if (string.Equals(t, "Owner license suspended", StringComparison.OrdinalIgnoreCase)) return "DL          SUSPENDED";
            if (string.Equals(t, "Owner license revoked", StringComparison.OrdinalIgnoreCase)) return "DL          REVOKED";
            if (string.Equals(t, "Owner license expired", StringComparison.OrdinalIgnoreCase)) return "DL          EXPIRED";
            if (string.Equals(t, "Registration expired", StringComparison.OrdinalIgnoreCase)) return "REG         EXPIRED";
            if (string.Equals(t, "No registration", StringComparison.OrdinalIgnoreCase)) return "REG         NONE";
            if (string.Equals(t, "Insurance expired", StringComparison.OrdinalIgnoreCase)) return "INS         EXPIRED";
            if (string.Equals(t, "No insurance", StringComparison.OrdinalIgnoreCase)) return "INS         NONE";
            return t;
        }

        /// <summary>True when the hit has registration, insurance, or driver-license hits (not stolen/BOLO/wanted — those use <see cref="HasSevereAlertForPromotion"/>).</summary>
        internal static bool HasPaperworkOrLicenseAlert(ALPRHit hit) {
            if (hit?.Flags == null) return false;
            foreach (string raw in hit.Flags) {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string f = raw.Trim();
                if (string.Equals(f, "Not in database", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(f, "Stolen", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(f, "BOLO", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(f, "Owner wanted", StringComparison.OrdinalIgnoreCase)) continue;
                return true;
            }
            return false;
        }

        /// <summary>True when the hit has any flag other than bare &quot;Not in database&quot; (terminal list / detail text).</summary>
        internal static bool HasInterestingFlagsExcludingNotInDatabase(ALPRHit hit) {
            if (hit?.Flags == null || hit.Flags.Count == 0) return false;
            foreach (string f in hit.Flags) {
                if (string.IsNullOrEmpty(f)) continue;
                if (string.Equals(f.Trim(), "Not in database", StringComparison.OrdinalIgnoreCase))
                    continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// High-priority hits only: stolen, active BOLO, or owner wanted. Registration/insurance/DL issues stay in the READS list
        /// but do not drive sounds, web toasts, or HUD hold spam (CDF marks many routine vehicles with paperwork flags).
        /// </summary>
        internal static bool HasSevereAlertForPromotion(ALPRHit hit) {
            if (hit?.Flags == null) return false;
            foreach (string raw in hit.Flags) {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string f = raw.Trim();
                if (string.Equals(f, "Stolen", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(f, "BOLO", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(f, "Owner wanted", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static List<string> BuildFlagsFromLiveCdfVehicle(MDTProVehicleData vd, MDTProVehicleData dbVehicle) {
            var flags = new List<string>();
            if (vd != null && vd.IsStolen)
                flags.Add("Stolen");
            if (DataController.HasActiveBOLOs(vd) || (dbVehicle != null && DataController.HasActiveBOLOs(dbVehicle)))
                flags.Add("BOLO");

            VehicleData cdf = vd?.CDFVehicleData;
            if (cdf != null) {
                AppendCdfRegistrationInsuranceFlags(flags, cdf);
                if (cdf.Owner?.Wanted == true)
                    flags.Add("Owner wanted");
                try {
                    AppendCdfOwnerDriverLicenseFlags(flags, cdf.Owner);
                } catch { /* CDF / owner API differences */ }
            }

            return flags;
        }

        internal static List<string> BuildFlagsPersistedVehicleOnly(MDTProVehicleData dbVehicle) {
            var flags = new List<string>();
            if (dbVehicle == null) return flags;
            if (dbVehicle.IsStolen)
                flags.Add("Stolen");
            if (DataController.HasActiveBOLOs(dbVehicle))
                flags.Add("BOLO");
            return flags;
        }

        internal static string GetVehicleColorDisplay(MDTProVehicleData vd, MDTProVehicleData dbVehicle) {
            string primary = null;
            string secondary = null;
            if (vd != null && (vd.PrimaryColor != null || vd.SecondaryColor != null || vd.PrimaryColorSpecific != null || vd.SecondaryColorSpecific != null)) {
                primary = !string.IsNullOrWhiteSpace(vd.PrimaryColor) ? vd.PrimaryColor.Trim() : vd.PrimaryColorSpecific?.Trim();
                secondary = !string.IsNullOrWhiteSpace(vd.SecondaryColor) ? vd.SecondaryColor.Trim() : vd.SecondaryColorSpecific?.Trim();
            }
            if ((string.IsNullOrEmpty(primary) || string.IsNullOrEmpty(secondary)) && dbVehicle != null) {
                if (string.IsNullOrEmpty(primary))
                    primary = !string.IsNullOrWhiteSpace(dbVehicle.PrimaryColor) ? dbVehicle.PrimaryColor.Trim() : dbVehicle.PrimaryColorSpecific?.Trim();
                if (string.IsNullOrEmpty(secondary))
                    secondary = !string.IsNullOrWhiteSpace(dbVehicle.SecondaryColor) ? dbVehicle.SecondaryColor.Trim() : dbVehicle.SecondaryColorSpecific?.Trim();
            }
            if (!string.IsNullOrEmpty(primary) && !string.IsNullOrEmpty(secondary))
                return primary + " / " + secondary;
            if (!string.IsNullOrEmpty(primary)) return primary;
            if (!string.IsNullOrEmpty(secondary)) return secondary;
            if (vd?.Color != null && !string.IsNullOrWhiteSpace(vd.Color)) return vd.Color.Trim();
            if (dbVehicle?.Color != null && !string.IsNullOrWhiteSpace(dbVehicle.Color)) return dbVehicle.Color.Trim();
            return null;
        }

        private static void AppendCdfRegistrationInsuranceFlags(List<string> flags, VehicleData cdf) {
            if (cdf == null) return;
            try {
                if (cdf.Registration != null)
                    AppendOneCdfDocumentFlags(flags, cdf.Registration, "Registration expired", "No registration");
            } catch { /* CDF version differences */ }
            try {
                if (cdf.Insurance != null)
                    AppendOneCdfDocumentFlags(flags, cdf.Insurance, "Insurance expired", "No insurance");
            } catch { }
        }

        private static void AppendOneCdfDocumentFlags(List<string> flags, object document, string expiredLabel, string invalidLabel) {
            if (document == null || flags == null) return;
            string statusStr = null;
            DateTime? expiration = null;
            try {
                var stProp = document.GetType().GetProperty("Status");
                if (stProp != null)
                    statusStr = stProp.GetValue(document)?.ToString();
                var expProp = document.GetType().GetProperty("ExpirationDate");
                if (expProp != null) {
                    object ev = expProp.GetValue(document);
                    if (ev is DateTime dt) expiration = dt;
                }
            } catch {
                return;
            }

            if (DocumentStatusImpliesExpired(statusStr, expiration)) {
                flags.Add(expiredLabel);
                return;
            }
            if (IsSuspendedRevokedOrNoneStatus(statusStr))
                flags.Add(invalidLabel);
        }

        private static bool DocumentStatusImpliesExpired(string status, DateTime? expirationDate) {
            if (!string.IsNullOrEmpty(status) && string.Equals(status, "Expired", StringComparison.OrdinalIgnoreCase))
                return true;
            if (expirationDate.HasValue && expirationDate.Value.Date < DateTime.UtcNow.Date) {
                if (string.IsNullOrEmpty(status) || string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsSuspendedRevokedOrNoneStatus(string status) {
            if (string.IsNullOrEmpty(status)) return false;
            return string.Equals(status, "Suspended", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Revoked", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "None", StringComparison.OrdinalIgnoreCase);
        }

        internal static void AppendCdfOwnerDriverLicenseFlags(List<string> flags, PedData owner) {
            if (flags == null || owner == null) return;
            string status;
            DateTime? expiration = null;
            try {
                status = owner.DriversLicenseState.ToString();
                try { expiration = owner.DriversLicenseExpiration; } catch { expiration = null; }
            } catch {
                return;
            }
            AppendOwnerLicenseAlertFromStatusAndExpiration(flags, status, expiration);
        }

        internal static void AppendMdtPedOwnerDriverLicenseFlags(List<string> flags, MDTProPedData ped) {
            if (flags == null || ped == null) return;
            string status = ped.LicenseStatus?.Trim();
            DateTime? expiration = null;
            if (!string.IsNullOrEmpty(ped.LicenseExpiration) && DateTime.TryParse(ped.LicenseExpiration, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime exp))
                expiration = exp;
            else if (!string.IsNullOrEmpty(ped.LicenseExpiration) && DateTime.TryParse(ped.LicenseExpiration, out exp))
                expiration = exp;
            AppendOwnerLicenseAlertFromStatusAndExpiration(flags, status, expiration);
        }

        private static void AppendOwnerLicenseAlertFromStatusAndExpiration(List<string> flags, string status, DateTime? expiration) {
            if (flags == null) return;
            if (!string.IsNullOrEmpty(status)) {
                if (string.Equals(status, "Unlicensed", StringComparison.OrdinalIgnoreCase)) {
                    if (!flags.Contains("Owner unlicensed")) flags.Add("Owner unlicensed");
                    return;
                }
                if (string.Equals(status, "Suspended", StringComparison.OrdinalIgnoreCase)) {
                    if (!flags.Contains("Owner license suspended")) flags.Add("Owner license suspended");
                    return;
                }
                if (string.Equals(status, "Revoked", StringComparison.OrdinalIgnoreCase)) {
                    if (!flags.Contains("Owner license revoked")) flags.Add("Owner license revoked");
                    return;
                }
            }
            if (!string.IsNullOrEmpty(status) && string.Equals(status, "Expired", StringComparison.OrdinalIgnoreCase)) {
                if (!flags.Contains("Owner license expired")) flags.Add("Owner license expired");
                return;
            }
            if (DocumentStatusImpliesExpired(status, expiration) && !string.Equals(status, "Suspended", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(status, "Revoked", StringComparison.OrdinalIgnoreCase) && !string.Equals(status, "Unlicensed", StringComparison.OrdinalIgnoreCase)) {
                if (!flags.Contains("Owner license expired")) flags.Add("Owner license expired");
            }
        }

        private static string GetModelDisplayName(Vehicle v) {
            if (v == null || !v.Exists()) return "";
            try {
                string raw = NativeFunction.Natives.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL<string>(v.Model.Hash);
                return string.IsNullOrEmpty(raw) ? "" : Game.GetLocalizedString(raw);
            } catch {
                return v.Model.Name ?? "";
            }
        }
    }
}
