using System.Collections.Generic;
using FellowOakDicom;

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
        public DicomDataset Dataset { get; set; } = new DicomDataset();

        /// <summary>
        /// Metadata dictionary extracted from the dataset for quick evaluation.
        /// </summary>
        public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Remote AE Title of the sender.
        /// </summary>
        public string RemoteAET { get; set; } = string.Empty;
    }
}
