namespace MDTPro.Data {
    /// <summary>Active or historical warrant line for MDT display and cloud sync.</summary>
    public class WarrantCharge {
        public string Name;
        /// <summary><c>Felony</c> or <c>Misdemeanor</c> (from custody ranges in arrest options).</summary>
        public string Severity;
        /// <summary>UTC round-trip ISO when the warrant was first recorded.</summary>
        public string IssuedAtUtc;
        public string ClearedAtUtc;
        public string ClearedByReportType;
        public string ClearedByReportId;

        internal bool IsActive => string.IsNullOrWhiteSpace(ClearedAtUtc);
    }
}
