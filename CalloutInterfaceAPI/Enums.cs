namespace CalloutInterfaceAPI
{
    /// <summary>
    /// The type of vehicle document a policeman might be interested in.
    /// </summary>
    public enum VehicleDocument
    {
        /// <summary>Vehicle insurance document.</summary>
        Insurance,
        /// <summary>Vehicle registration document.</summary>
        Registration,
    }

    /// <summary>
    /// Replaces StopThePed's VehicleStatus enum for safety.
    /// </summary>
    public enum VehicleDocumentStatus
    {
        /// <summary>Document is expired.</summary>
        Expired,
        /// <summary>No document / not applicable.</summary>
        None,
        /// <summary>Status could not be determined.</summary>
        Unknown,
        /// <summary>Document is valid.</summary>
        Valid,
    }
}
