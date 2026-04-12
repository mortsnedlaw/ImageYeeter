using System;
using System.Collections.Generic;

namespace DicomRouter.Infrastructure.Dicom
{
    /// <summary>
    /// Metadata stored for a spooled DICOM transmission.
    /// </summary>
    public class SpoolItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DicomFileName { get; set; } = string.Empty;
        public List<string> DestinationNames { get; set; } = new List<string>();
        public Dictionary<string, string> TagOverrides { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int Attempts { get; set; } = 0;
        public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public string CallingAET { get; set; } = string.Empty;
    }
}
