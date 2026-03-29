namespace MDTPro.Utility.Backup {
    internal sealed class NullBackupDispatcher : IBackupDispatcher {
        internal static readonly NullBackupDispatcher Instance = new NullBackupDispatcher();
        private NullBackupDispatcher() { }
        public string ProviderId => "";
        public bool IsAvailable => false;
        public bool RequestPanicBackup() => false;
        public bool RequestBackup(string unitName, int responseCode) => false;
        public bool RequestTrafficStopBackup(string unitName, int responseCode) => false;
        public bool RequestPoliceTransport(int responseCode) => false;
        public bool RequestTowServiceBackup() => false;
        public bool RequestGroupBackup() => false;
        public bool RequestAirBackup(string unitName) => false;
        public bool RequestSpikeStripsBackup() => false;
        public bool InitiateFelonyStop() => false;
        public void DismissAllBackupUnits(bool force) { }
    }
}
