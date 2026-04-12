using System;
using System.Threading.Tasks;
using FellowOakDicom.Network.Client;
using FellowOakDicom;
using DicomRouter.Infrastructure.Models;

namespace DicomRouter.Infrastructure.Dicom
{
    /// <summary>
    /// Forwards datasets to destination SCPs using fo-dicom DicomClient.
    /// </summary>
    public class DicomForwarder
    {
        /// <summary>
        /// Sends the provided dataset to the destination endpoint.
        /// </summary>
        public async Task<bool> ForwardAsync(DicomDataset dataset, Destination dest, string callingAET = "DICOMROUTER")
        {
            try
            {
                var client = new DicomClient();
                client.NegotiateAsyncOps();

                var request = new DicomCStoreRequest(new DicomFile(dataset));
                await client.AddRequestAsync(request).ConfigureAwait(false);

                await client.SendAsync(dest.Host, dest.Port, dest.UseTls, callingAET, dest.AeTitle).ConfigureAwait(false);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
