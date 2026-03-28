namespace MDTPro.Utility.Backup {
    internal interface IBackupDispatcher {
        string ProviderId { get; }
        bool IsAvailable { get; }
        bool RequestPanicBackup();
        bool RequestBackup(string unitName, int responseCode);
        bool RequestTrafficStopBackup(string unitName, int responseCode);
        bool RequestPoliceTransport(int responseCode);
        bool RequestTowServiceBackup();
        bool RequestGroupBackup();
        bool RequestAirBackup(string unitName);
        bool RequestSpikeStripsBackup();
        bool InitiateFelonyStop();
        void DismissAllBackupUnits(bool force);
    }
}
