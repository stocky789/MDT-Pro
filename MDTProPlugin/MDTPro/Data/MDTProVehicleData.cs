using CommonDataFramework.Modules.VehicleDatabase;
using Rage;
using System.Drawing;

namespace MDTPro.Data {
    public class MDTProVehicleData {
        internal readonly Vehicle Holder;
        internal readonly VehicleData CDFVehicleData;

        public string LicensePlate;
        public string ModelName;
        public string ModelDisplayName;
        public bool IsStolen;
        public string Owner;
        public string Color;
        public string VinStatus;
        public string Make;
        public string Model;
        public string PrimaryColor;
        public string SecondaryColor;
        public string VehicleIdentificationNumber;
        public string RegistrationStatus;
        public string RegistrationExpiration;
        public string InsuranceStatus;
        public string InsuranceExpiration;
        public VehicleBOLO[] BOLOs;

        /// <summary>True when vehicle is in world and BOLOs can be added/removed via CDF.</summary>
        public bool CanModifyBOLOs {
            get {
                if (Holder == null) return false;
                try { return Holder.Exists(); } catch { return false; }
            }
        }

        internal MDTProVehicleData(Vehicle vehicle) {
            Holder = vehicle;
            CDFVehicleData = vehicle.GetVehicleData();
            PopulateParameters();
        }
        internal MDTProVehicleData(VehicleData vehicleData) {
            Holder = vehicleData.Holder;
            CDFVehicleData = vehicleData;
            PopulateParameters();
        }
        internal MDTProVehicleData() { }

        private void PopulateParameters() {
            if (Holder == null || CDFVehicleData == null) return;
            if (CDFVehicleData.Owner == null) return;

            LicensePlate = Holder.LicensePlate;
            ModelName = Holder.Model.Name;
            IsStolen = CDFVehicleData.IsStolen;
            Owner = CDFVehicleData.Owner.FullName?.Trim() ?? string.Empty;

            if (CDFVehicleData.Owner.FullName?.Trim() != "Government") {
                DataController.AddCDFPedDataPedToDatabase(CDFVehicleData.Owner);
            }

            if (CDFVehicleData.Registration != null) {
                RegistrationStatus = CDFVehicleData.Registration.Status.ToString();
                RegistrationExpiration = CDFVehicleData.Registration.ExpirationDate?.ToString("s");
            }
            if (CDFVehicleData.Insurance != null) {
                InsuranceStatus = CDFVehicleData.Insurance.Status.ToString();
                InsuranceExpiration = CDFVehicleData.Insurance.ExpirationDate?.ToString("s");
            }
            Color = Rage.Native.NativeFunction.Natives.GET_VEHICLE_LIVERY<int>(Holder) == -1 ? GetColorDisplay(Holder.PrimaryColor, Holder.SecondaryColor) : null;
            VehicleIdentificationNumber = CDFVehicleData.Vin?.Number;
            try {
                var vin = CDFVehicleData.Vin;
                if (vin != null) {
                    var statusProp = vin.GetType().GetProperty("Status");
                    if (statusProp != null) VinStatus = statusProp.GetValue(vin)?.ToString();
                }
            } catch { VinStatus = null; }
            try {
                Make = CDFVehicleData.GetType().GetProperty("Make")?.GetValue(CDFVehicleData) as string;
                Model = CDFVehicleData.GetType().GetProperty("Model")?.GetValue(CDFVehicleData) as string;
                PrimaryColor = CDFVehicleData.GetType().GetProperty("PrimaryColor")?.GetValue(CDFVehicleData) as string;
                SecondaryColor = CDFVehicleData.GetType().GetProperty("SecondaryColor")?.GetValue(CDFVehicleData) as string;
            } catch { }

            string unlocalizedModelDisplayName = Rage.Native.NativeFunction.Natives.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL<string>(Holder.Model.Hash);

            ModelDisplayName = Game.GetLocalizedString(unlocalizedModelDisplayName);

            BOLOs = CDFVehicleData.GetAllBOLOs();
        }

        /// <summary>Converts vehicle primary/secondary colors to a readable display string (e.g. "Black / White") using CalloutInterfaceAPI.</summary>
        private static string GetColorDisplay(System.Drawing.Color primary, System.Drawing.Color secondary) {
            try {
                string p = CalloutInterfaceAPI.Functions.GetColorName(primary);
                string s = CalloutInterfaceAPI.Functions.GetColorName(secondary);
                if (!string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(s) && p != s)
                    return p + " / " + s;
                if (!string.IsNullOrEmpty(p)) return p;
                if (!string.IsNullOrEmpty(s)) return s;
            } catch { }
            return $"{primary.R}-{primary.G}-{primary.B}";
        }

        /// <summary>Updates registration/insurance string fields from CDF (expirations and default statuses). With StopThePed stops, <see cref="DataController.TryOverlayStopThePedVehicleDocStatusFromApi"/> overwrites statuses from STP’s API (<c>getVehicleRegistrationStatus</c> / <c>getVehicleInsuranceStatus</c>). Does not replace Holder/CDFVehicleData.</summary>
        internal void CopyRegistrationInsuranceFromCdf(VehicleData cdf) {
            if (cdf == null) return;
            try {
                if (cdf.Registration != null) {
                    RegistrationStatus = cdf.Registration.Status.ToString();
                    RegistrationExpiration = cdf.Registration.ExpirationDate?.ToString("s");
                }
            } catch { /* CDF version differences */ }
            try {
                if (cdf.Insurance != null) {
                    InsuranceStatus = cdf.Insurance.Status.ToString();
                    InsuranceExpiration = cdf.Insurance.ExpirationDate?.ToString("s");
                }
            } catch { }
        }

        /// <summary>Apply persistent vehicle identity from a previously seen vehicle (same owner + model). Keeps current LicensePlate.
        /// Registration and Insurance are NOT copied from source — CDF/PR is authoritative at stop time (revoked/expired can change).</summary>
        internal void ApplyPersistentVehicleIdentity(MDTProVehicleData source) {
            if (source == null) return;
            IsStolen = source.IsStolen;
            Owner = source.Owner;
            VehicleIdentificationNumber = source.VehicleIdentificationNumber;
            VinStatus = source.VinStatus;
            Make = source.Make;
            Model = source.Model;
            PrimaryColor = source.PrimaryColor;
            SecondaryColor = source.SecondaryColor;
            BOLOs = source.BOLOs;
            // Do NOT overwrite RegistrationStatus, RegistrationExpiration, InsuranceStatus, InsuranceExpiration.
            // PR populates CDF at stop time; re-encounter DB may have stale Valid when PR has since Revoked/Expired.
        }
    }
}
