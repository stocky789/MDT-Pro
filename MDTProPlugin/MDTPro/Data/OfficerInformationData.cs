namespace MDTPro.Data {
    public class OfficerInformationData {
        public string firstName;
        public string lastName;
        public string rank;
        public string callSign;
        public string agency;
        /// <summary>LSPDFR agency script name (e.g. LSPD, LSSD) for report branding / mapping.</summary>
        public string agencyScriptName;
        public int? badgeNumber;
    }
}
