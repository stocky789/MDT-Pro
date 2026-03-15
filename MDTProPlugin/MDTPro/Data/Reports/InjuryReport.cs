namespace MDTPro.Data.Reports {
    public class InjuryReport : Report {
        public string InjuredPartyName;
        public string InjuryType;
        public string Severity;
        public string Treatment;
        public string IncidentContext;
        public string LinkedReportId;
        /// <summary>Optional JSON snapshot of in-game injury data when report was created from game data (legacy).</summary>
        public string GameInjurySnapshot;
    }
}
