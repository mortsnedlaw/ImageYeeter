namespace DicomRouter.Infrastructure.Models
{
    /// <summary>
    /// Represents a destination SCP to forward DICOM instances to.
    /// </summary>
    public class Destination
    {
        public string Name { get; set; } = string.Empty;
        public string AeTitle { get; set; } = string.Empty;
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 104;
        public bool UseTls { get; set; } = false;
    }
}
