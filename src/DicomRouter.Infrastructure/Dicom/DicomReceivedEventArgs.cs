using System.Collections.Generic;
namespace DicomRouter.Infrastructure.Dicom
{
    /// <summary>
    /// Provides information about a received DICOM instance.
    /// </summary>
    public class DicomReceivedEventArgs
    {
        /// <summary>
        /// The dataset that was received.
        /// </summary>
        public NativeDicomDataset Dataset { get; set; } = new NativeDicomDataset(Array.Empty<DicomElement>());
        public byte[] RawDataset { get; set; } = Array.Empty<byte>();
        public string TransferSyntaxUid { get; set; } = string.Empty;

        /// <summary>
        /// Metadata dictionary extracted from the dataset for quick evaluation.
        /// </summary>
        public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Remote AE Title of the sender.
        /// </summary>
        public string RemoteAET { get; set; } = string.Empty;
        public string ListenerId { get; set; } = string.Empty;
        public string ListenerName { get; set; } = string.Empty;
    }
}
