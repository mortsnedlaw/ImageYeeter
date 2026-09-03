namespace DicomRouter.Infrastructure.Models
{
    /// <summary>
    /// Represents a destination SCP to forward DICOM instances to.
    /// </summary>
    public class Destination
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string AeTitle { get; set; } = string.Empty;
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 104;
        public bool UseTls { get; set; } = false;
        public string CallingAeTitle { get; set; } = "IMAGEYEETER";
        public int TimeoutSeconds { get; set; } = 30;
        public int RetryIntervalSeconds { get; set; } = 30;
        public int MaxAttempts { get; set; } = 5;
        public int MaxParallelSends { get; set; } = 1;
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Maximum PDU size for this destination (in bytes).
        /// </summary>
        public int MaxPduSize { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Presentation context configuration for this destination.
        /// Defines which SOP classes and transfer syntaxes to propose during association.
        /// If null, a default profile is used.
        /// </summary>
        public PresentationContextProfile? PresentationContextProfile { get; set; }

        /// <summary>
        /// Gets the effective presentation context profile for this destination.
        /// </summary>
        public PresentationContextProfile GetEffectiveProfile() =>
            PresentationContextProfile ?? PresentationContextProfile.CreateDefaultStorageProfile();
    }
}
