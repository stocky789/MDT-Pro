using CommonDataFramework.Modules.PedDatabase;
using MDTPro.Setup;
using MDTPro.Utility;
using System.Reflection;
using Rage;
using System;
using System.Collections.Generic;
using System.Linq;
using static MDTPro.Setup.SetupController;
using static MDTPro.Utility.CitationArrestHelper;

namespace MDTPro.Data {
    public class MDTProPedData {
        internal readonly PedData CDFPedData;
        internal readonly Ped Holder;

        public string Name;
        public string FirstName;
        public string LastName;
        public uint ModelHash;
        public string ModelName;
        public string Birthday;
        public string Gender;
        public string Address;
        public bool IsInGang;
        public string AdvisoryText;
        public int TimesStopped;
        public bool IsWanted;
        public string WarrantText;
        public bool IsOnProbation;
        public bool IsOnParole;
        public string LicenseStatus;
        public string LicenseExpiration;
        public string WeaponPermitStatus;
        public string WeaponPermitExpiration;
        public string WeaponPermitType;
        public string FishingPermitStatus;
        public string FishingPermitExpiration;
        public string HuntingPermitStatus;
        public string HuntingPermitExpiration;
        public string IncarceratedUntil;
        public bool IsDeceased;
        public string DeceasedAt;
        public List<CitationGroup.Charge> Citations;
        public List<ArrestGroup.Charge> Arrests;
        public List<IdentificationEntry> IdentificationHistory;

        internal MDTProPedData(Ped ped) {
            CDFPedData = ped.GetPedData();
            Holder = ped;
            PopulateParameters();
        }
        internal MDTProPedData(PedData pedData) {
            CDFPedData = pedData;
            Holder = pedData.Holder;
            PopulateParameters();
        }
        public MDTProPedData() { }

        /// <summary>True when ped has name but no meaningful identity (DOB, license, address, gender). Used to avoid persisting or to trigger delayed CDF retry.</summary>
        public static bool IsMinimalIdentity(MDTProPedData p) {
            if (p == null) return true;
            bool hasName = !string.IsNullOrWhiteSpace(p.Name) || !string.IsNullOrWhiteSpace(p.FirstName) || !string.IsNullOrWhiteSpace(p.LastName);
            if (!hasName) return true;
            bool hasIdentity = !string.IsNullOrWhiteSpace(p.Birthday) || !string.IsNullOrWhiteSpace(p.LicenseStatus)
                || !string.IsNullOrWhiteSpace(p.Address) || !string.IsNullOrWhiteSpace(p.Gender);
            return !hasIdentity;
        }

        private void PopulateParameters() {
            if (CDFPedData == null) {
                Helper.Log($"[MDTPro] Ped built with CDF null, using LSPDFR fallback: {(Name ?? "(no name)")}", false, Helper.LogSeverity.Info);
                PopulateFromLSPDFRPersonaFallback();
                return;
            }

            Name = CDFPedData.FullName;
            FirstName = CDFPedData.Firstname;
            LastName = CDFPedData.Lastname;
            ModelHash = Holder != null && Holder.IsValid() ? (uint)Holder.Model.Hash : 0;
            ModelName = Holder != null && Holder.IsValid() ? Holder.Model.Name : null;
            Birthday = CDFPedData.Birthday.ToString("s");
            Gender = CDFPedData.Gender.ToString();
            IsWanted = CDFPedData.Wanted;
            IsOnProbation = CDFPedData.IsOnProbation;
            IsOnParole = CDFPedData.IsOnParole;
            if (CDFPedData.Address != null && CDFPedData.Address.Zone != null)
                Address = $"{CDFPedData.Address}, {CDFPedData.Address.Zone.RealAreaName}";
            else
                Address = CDFPedData.Address?.ToString() ?? string.Empty;
            try {
                LicenseStatus = CDFPedData.DriversLicenseState.ToString();
                LicenseExpiration = CDFPedData.DriversLicenseExpiration?.ToString("s");
                if (CDFPedData.WeaponPermit != null) {
                    WeaponPermitStatus = CDFPedData.WeaponPermit.Status.ToString();
                    WeaponPermitExpiration = CDFPedData.WeaponPermit.ExpirationDate?.ToString("s");
                    WeaponPermitType = GetWeaponPermitType(CDFPedData.WeaponPermit);
                }
                FishingPermitStatus = CDFPedData.FishingPermit.Status.ToString();
                FishingPermitExpiration = CDFPedData.FishingPermit.ExpirationDate?.ToString("s");
                HuntingPermitStatus = CDFPedData.HuntingPermit.Status.ToString();
                HuntingPermitExpiration = CDFPedData.HuntingPermit.ExpirationDate?.ToString("s");
            } catch (Exception e) {
                Helper.Log($"[MDTPro] CDF permit/license read failed for {Name}: {e.Message}", false, Helper.LogSeverity.Warning);
            }
            if (CDFPedData.HasRealPed && Holder != null && Holder.IsValid()) {
                try {
                    var rg = Holder.RelationshipGroup;
                    string groupName = rg.Name;
                    IsInGang = groupName != null && groupName.ToLower().Contains("gang");
                } catch {
                    IsInGang = false;
                }
            }
            AdvisoryText = CDFPedData.AdvisoryText;
            WarrantText = IsWanted ? GetRandomWarrantCharge().name : null;

            Citations = SelectWithProbability(GetConfig().hasPriorCitationsProbability)
                ? GetRandomCitationCharges(GetConfig().maxNumberOfPriorCitations)
                : new List<CitationGroup.Charge>();

            if (IsWanted) {
                Arrests = SelectWithProbability(GetConfig().hasPriorArrestsWithWarrantProbability)
                    ? GetRandomArrestCharges(GetConfig().maxNumberOfPriorArrestsWithWarrant)
                    : new List<ArrestGroup.Charge>();
            } else {
                Arrests = SelectWithProbability(GetConfig().hasPriorArrestsProbability)
                    ? GetRandomArrestCharges(GetConfig().maxNumberOfPriorArrests)
                    : new List<ArrestGroup.Charge>();
            }

            // CDF can mark supervision without any prior charges in our random roll — keep Person Search consistent (forum: probation/parole with empty arrest history).
            if ((IsOnProbation || IsOnParole) && Arrests.Count == 0) {
                int cap = Math.Max(1, GetConfig().maxNumberOfPriorArrests);
                Arrests = GetRandomArrestCharges(Math.Min(2, cap));
            }

            if (Citations.Count > 0 || Arrests.Count > 0) {
                CDFPedData.TimesStopped += Citations.Count / 2 + Arrests.Count / 2;
            }

            CDFPedData.Citations = Citations.Count;

            TimesStopped = CDFPedData.TimesStopped;

            TryParseNameIntoFirstLast();
        }

        /// <summary>When we have Name but empty FirstName/LastName (e.g. callout peds, CDF/LSPDFR minimal records), derive first/last from full name.</summary>
        internal void TryParseNameIntoFirstLast() {
            if (string.IsNullOrEmpty(Name)) return;
            if (!string.IsNullOrEmpty(FirstName) && !string.IsNullOrEmpty(LastName)) return;
            var parts = Name.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) {
                if (string.IsNullOrEmpty(FirstName)) FirstName = parts[0];
                if (string.IsNullOrEmpty(LastName)) LastName = parts[1];
            } else if (parts.Length == 1 && string.IsNullOrEmpty(FirstName) && string.IsNullOrEmpty(LastName)) {
                FirstName = parts[0];
            }
        }

        /// <summary>Read weapon permit type from CDF, normalizing PR/CDF variations (e.g. "CCW Permit") to our language keys.</summary>
        private static string GetWeaponPermitType(CommonDataFramework.Modules.PedDatabase.WeaponPermit weaponPermit) {
            if (weaponPermit == null) return null;
            string raw = null;
            try {
                raw = weaponPermit.PermitType.ToString();
            } catch {
                try {
                    var prop = weaponPermit.GetType().GetProperty("PermitType", BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null) raw = prop.GetValue(weaponPermit)?.ToString();
                } catch { }
            }
            if (string.IsNullOrWhiteSpace(raw)) return null;
            // Normalize PR/CDF values to our language keys; PR often shows "CCW Permit"
            string lower = raw.Trim().ToLowerInvariant();
            if (lower == "ccw permit" || lower == "ccw") return "CCWPermit";
            if (lower.Contains("ccw") || lower == "concealed" || lower.Contains("concealed carry"))
                return "CcwPermit";
            if (lower.Contains("ffl") || lower == "federal firearms") return "FflPermit";
            if (lower == "none" || lower == "invalid") return null;
            return raw.Trim();
        }

        /// <summary>When CDF PedData is null, populate Name/Model from LSPDFR Persona so we can still resolve peds.</summary>
        private void PopulateFromLSPDFRPersonaFallback() {
            Citations = new List<CitationGroup.Charge>();
            Arrests = new List<ArrestGroup.Charge>();
            IdentificationHistory = new List<IdentificationEntry>();
            if (Holder == null || !Holder.IsValid()) return;
            try {
                var persona = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(Holder);
                if (persona != null) {
                    Name = persona.FullName;
                    FirstName = persona.Forename;
                    LastName = persona.Surname;
                }
                ModelHash = (uint)Holder.Model.Hash;
                ModelName = Holder.Model.Name;
            } catch { /* LSPDFR not available or ped invalid */ }
            TryParseNameIntoFirstLast();
        }

        public class IdentificationEntry {
            public string Type;
            public string Timestamp;
        }

        /// <summary>Apply persistent (SQLite) identity onto current ped. For string/identity fields, only overwrite when source has a value so we do not replace good CDF/current data with nulls from an old DB record.</summary>
        internal void ApplyPersistentIdentity(MDTProPedData source) {
            if (source == null) return;

            Name = source.Name;
            if (!string.IsNullOrEmpty(source.FirstName)) FirstName = source.FirstName;
            if (!string.IsNullOrEmpty(source.LastName)) LastName = source.LastName;
            if (!string.IsNullOrEmpty(source.Birthday)) Birthday = source.Birthday;
            if (!string.IsNullOrEmpty(source.Gender)) Gender = source.Gender;
            if (!string.IsNullOrEmpty(source.Address)) Address = source.Address;
            IsInGang = source.IsInGang;
            if (!string.IsNullOrEmpty(source.AdvisoryText)) AdvisoryText = source.AdvisoryText;
            TimesStopped = source.TimesStopped;
            IsWanted = source.IsWanted;
            WarrantText = source.WarrantText;
            IsOnProbation = source.IsOnProbation;
            IsOnParole = source.IsOnParole;
            if (!string.IsNullOrEmpty(source.LicenseStatus)) LicenseStatus = source.LicenseStatus;
            if (!string.IsNullOrEmpty(source.LicenseExpiration)) LicenseExpiration = source.LicenseExpiration;
            if (!string.IsNullOrEmpty(source.WeaponPermitStatus)) WeaponPermitStatus = source.WeaponPermitStatus;
            if (!string.IsNullOrEmpty(source.WeaponPermitExpiration)) WeaponPermitExpiration = source.WeaponPermitExpiration;
            if (!string.IsNullOrEmpty(source.WeaponPermitType)) WeaponPermitType = source.WeaponPermitType;
            if (!string.IsNullOrEmpty(source.FishingPermitStatus)) FishingPermitStatus = source.FishingPermitStatus;
            if (!string.IsNullOrEmpty(source.FishingPermitExpiration)) FishingPermitExpiration = source.FishingPermitExpiration;
            if (!string.IsNullOrEmpty(source.HuntingPermitStatus)) HuntingPermitStatus = source.HuntingPermitStatus;
            if (!string.IsNullOrEmpty(source.HuntingPermitExpiration)) HuntingPermitExpiration = source.HuntingPermitExpiration;
            if (!string.IsNullOrEmpty(source.IncarceratedUntil)) IncarceratedUntil = source.IncarceratedUntil;
            IsDeceased = source.IsDeceased;
            DeceasedAt = source.DeceasedAt;

            Citations = source.Citations?
                .Select(charge => new CitationGroup.Charge {
                    name = charge.name,
                    minFine = charge.minFine,
                    maxFine = charge.maxFine,
                    canRevokeLicense = charge.canRevokeLicense,
                    isArrestable = charge.isArrestable
                })
                .ToList() ?? new List<CitationGroup.Charge>();

            Arrests = source.Arrests?
                .Select(charge => new ArrestGroup.Charge {
                    name = charge.name,
                    minFine = charge.minFine,
                    maxFine = charge.maxFine,
                    canRevokeLicense = charge.canRevokeLicense,
                    isArrestable = charge.isArrestable,
                    minDays = charge.minDays,
                    maxDays = charge.maxDays,
                    probation = charge.probation,
                    canBeWarrant = charge.canBeWarrant
                })
                .ToList() ?? new List<ArrestGroup.Charge>();
        }

        /// <summary>Merge citations, warrants, permits, and incarceration from a saved ped while keeping the live ped's name/DOB/address from CDF. Used for model-based re-encounter so we never label one NPC with another's identity (many peds share the same model).</summary>
        internal void ApplyPersistentRecordPreservingLiveIdentity(MDTProPedData source) {
            if (source == null) return;

            if (!string.IsNullOrEmpty(source.AdvisoryText)) AdvisoryText = source.AdvisoryText;
            TimesStopped = source.TimesStopped;
            IsWanted = source.IsWanted;
            WarrantText = source.WarrantText;
            IsOnProbation = source.IsOnProbation;
            IsOnParole = source.IsOnParole;
            if (!string.IsNullOrEmpty(source.LicenseStatus)) LicenseStatus = source.LicenseStatus;
            if (!string.IsNullOrEmpty(source.LicenseExpiration)) LicenseExpiration = source.LicenseExpiration;
            if (!string.IsNullOrEmpty(source.WeaponPermitStatus)) WeaponPermitStatus = source.WeaponPermitStatus;
            if (!string.IsNullOrEmpty(source.WeaponPermitExpiration)) WeaponPermitExpiration = source.WeaponPermitExpiration;
            if (!string.IsNullOrEmpty(source.WeaponPermitType)) WeaponPermitType = source.WeaponPermitType;
            if (!string.IsNullOrEmpty(source.FishingPermitStatus)) FishingPermitStatus = source.FishingPermitStatus;
            if (!string.IsNullOrEmpty(source.FishingPermitExpiration)) FishingPermitExpiration = source.FishingPermitExpiration;
            if (!string.IsNullOrEmpty(source.HuntingPermitStatus)) HuntingPermitStatus = source.HuntingPermitStatus;
            if (!string.IsNullOrEmpty(source.HuntingPermitExpiration)) HuntingPermitExpiration = source.HuntingPermitExpiration;
            if (!string.IsNullOrEmpty(source.IncarceratedUntil)) IncarceratedUntil = source.IncarceratedUntil;
            IsDeceased = source.IsDeceased;
            DeceasedAt = source.DeceasedAt;

            Citations = source.Citations?
                .Select(charge => new CitationGroup.Charge {
                    name = charge.name,
                    minFine = charge.minFine,
                    maxFine = charge.maxFine,
                    canRevokeLicense = charge.canRevokeLicense,
                    isArrestable = charge.isArrestable
                })
                .ToList() ?? new List<CitationGroup.Charge>();

            Arrests = source.Arrests?
                .Select(charge => new ArrestGroup.Charge {
                    name = charge.name,
                    minFine = charge.minFine,
                    maxFine = charge.maxFine,
                    canRevokeLicense = charge.canRevokeLicense,
                    isArrestable = charge.isArrestable,
                    minDays = charge.minDays,
                    maxDays = charge.maxDays,
                    probation = charge.probation,
                    canBeWarrant = charge.canBeWarrant
                })
                .ToList() ?? new List<ArrestGroup.Charge>();
        }

        /// <summary>Try to sync CDF PedData name to match persistent identity after re-encounter. Uses CDF API property names (Firstname/Lastname). Wrapped in try-catch.</summary>
        internal void TrySyncCDFPersonaToPersistentIdentity() {
            if (CDFPedData == null || string.IsNullOrEmpty(Name)) return;
            try {
                string first = FirstName;
                string last = LastName;
                if (string.IsNullOrEmpty(first) && string.IsNullOrEmpty(last)) return;
                if (string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(Name)) {
                    var parts = Name.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    first = parts.Length > 0 ? parts[0] : null;
                    last = parts.Length > 1 ? parts[1] : null;
                }
                var type = CDFPedData.GetType();
                if (!string.IsNullOrEmpty(first)) {
                    var pi = type.GetProperty("Firstname", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                        ?? type.GetProperty("FirstName", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (pi != null && pi.CanWrite && pi.PropertyType == typeof(string))
                        pi.SetValue(CDFPedData, first);
                }
                if (!string.IsNullOrEmpty(last)) {
                    var pi = type.GetProperty("Lastname", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                        ?? type.GetProperty("LastName", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (pi != null && pi.CanWrite && pi.PropertyType == typeof(string))
                        pi.SetValue(CDFPedData, last);
                }
            } catch (Exception ex) {
                Game.LogTrivial($"[MDTPro] Could not sync CDF PedData name: {ex.Message}");
            }
        }
    }
}
