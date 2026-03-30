namespace MDTProNative.Wpf.Helpers;

/// <summary>
/// Canonical strings for ped license/permit fields that sync with CDF / LSPDFR (same values MDT Pro persists and <c>Enum.TryParse</c> accepts in-game).
/// Permit statuses: <see href="https://policing-redefined.netlify.app/docs/developer-docs/cdf/peds/permits">CDF — Ped Permits (EDocumentStatus, EWeaponPermitType)</see>.
/// Driver license: CDF <c>PedData.DriversLicenseState</c> / LSPDFR <c>Persona.ELicenseState</c> (see CommonDataFramework <c>PedData.HandlePersonaUpdate</c> switch).
/// </summary>
public static class CdfPedFormOptions
{
    /// <summary>ELicenseState-style values stored in <c>LicenseStatus</c> (PascalCase, case-insensitive parse in plugin).</summary>
    public static IReadOnlyList<string> DriverLicenseStates { get; } =
    [
        "None",
        "Unlicensed",
        "Valid",
        "Expired",
        "Suspended",
        "Revoked",
    ];

    /// <summary>EDocumentStatus for hunting/fishing/weapon permit <c>Status</c> fields.</summary>
    public static IReadOnlyList<string> DocumentStatuses { get; } =
    [
        "None",
        "Valid",
        "Expired",
        "Revoked",
    ];

    /// <summary>EWeaponPermitType — stored in <c>WeaponPermitType</c>; empty string = none / not set.</summary>
    public static IReadOnlyList<(string Label, string Value)> WeaponPermitTypes { get; } =
    [
        ("(none)", ""),
        ("CCW — concealed carry (CcwPermit)", "CcwPermit"),
        ("FFL — federal firearms (FflPermit)", "FflPermit"),
    ];

    /// <summary>Maps legacy or alternate spellings from API/DB to a canonical <see cref="WeaponPermitTypes"/> <c>Value</c>.</summary>
    public static string NormalizeWeaponPermitType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var t = raw.Trim();
        if (string.Equals(t, "CcwPermit", StringComparison.OrdinalIgnoreCase)) return "CcwPermit";
        if (string.Equals(t, "FflPermit", StringComparison.OrdinalIgnoreCase)) return "FflPermit";
        var lower = t.ToLowerInvariant();
        if (lower is "ccwpermit" or "ccw permit" or "ccw") return "CcwPermit";
        if (lower.Contains("ccw") || lower.Contains("concealed")) return "CcwPermit";
        if (lower.Contains("ffl") || lower.Contains("federal firearms")) return "FflPermit";
        return t;
    }
}
