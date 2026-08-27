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
        public int MaxPduSize { get; set; } = 16 * 1024;
    }
}
