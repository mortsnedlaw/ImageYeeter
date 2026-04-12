using System;
using System.Threading.Tasks;
using DicomRouter.Infrastructure.Models;

namespace DicomRouter.Infrastructure.Dicom
{
    /// <summary>
    /// Minimal contract for a DICOM SCP listener used by the application.
    /// </summary>
    public interface IDicomListener : IDisposable
    {
        /// <summary>
        /// Start listening on the configured endpoint.
        /// </summary>
        Task StartAsync(string aeTitle, string ip, int port);

        /// <summary>
        /// Stop listening and free resources.
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// Event raised when an incoming dataset has been received. Handler receives the file path of a spooled file or a serialized dataset.
        /// </summary>
        event Func<DicomReceivedEventArgs, Task> OnDicomReceived;
    }
}
