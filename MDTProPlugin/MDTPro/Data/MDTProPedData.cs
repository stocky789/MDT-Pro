using CommonDataFramework.Modules.PedDatabase;
using MDTPro.Setup;
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

        private void PopulateParameters() {
            if (CDFPedData == null) {
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
                WeaponPermitStatus = CDFPedData.WeaponPermit.Status.ToString();
                WeaponPermitExpiration = CDFPedData.WeaponPermit.ExpirationDate?.ToString("s");
                WeaponPermitType = CDFPedData.WeaponPermit.PermitType.ToString();
                FishingPermitStatus = CDFPedData.FishingPermit.Status.ToString();
                FishingPermitExpiration = CDFPedData.FishingPermit.ExpirationDate?.ToString("s");
                HuntingPermitStatus = CDFPedData.HuntingPermit.Status.ToString();
                HuntingPermitExpiration = CDFPedData.HuntingPermit.ExpirationDate?.ToString("s");
            } catch (Exception e) {
                Game.LogTrivial($"[MDTPro] Warning: Could not read CDF permit/license data for {Name}: {e.Message}");
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

            if (Citations.Count > 0 || Arrests.Count > 0) {
                CDFPedData.TimesStopped += Citations.Count / 2 + Arrests.Count / 2;
            }

            CDFPedData.Citations = Citations.Count;

            TimesStopped = CDFPedData.TimesStopped;
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
        }

        public class IdentificationEntry {
            public string Type;
            public string Timestamp;
        }

        internal void ApplyPersistentIdentity(MDTProPedData source) {
            if (source == null) return;

            Name = source.Name;
            FirstName = source.FirstName;
            LastName = source.LastName;
            Birthday = source.Birthday;
            Gender = source.Gender;
            Address = source.Address;
            IsInGang = source.IsInGang;
            AdvisoryText = source.AdvisoryText;
            TimesStopped = source.TimesStopped;
            IsWanted = source.IsWanted;
            WarrantText = source.WarrantText;
            IsOnProbation = source.IsOnProbation;
            IsOnParole = source.IsOnParole;
            LicenseStatus = source.LicenseStatus;
            LicenseExpiration = source.LicenseExpiration;
            WeaponPermitStatus = source.WeaponPermitStatus;
            WeaponPermitExpiration = source.WeaponPermitExpiration;
            WeaponPermitType = source.WeaponPermitType;
            FishingPermitStatus = source.FishingPermitStatus;
            FishingPermitExpiration = source.FishingPermitExpiration;
            HuntingPermitStatus = source.HuntingPermitStatus;
            HuntingPermitExpiration = source.HuntingPermitExpiration;
            IncarceratedUntil = source.IncarceratedUntil;

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
