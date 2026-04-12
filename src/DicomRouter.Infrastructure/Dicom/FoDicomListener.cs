using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Server;
using DicomRouter.Core.Services;
using DicomRouter.Core.Models;

namespace DicomRouter.Infrastructure.Dicom
{
    /// <summary>
    /// A simple fo-dicom based SCP that accepts C-STORE and raises an event.
    /// </summary>
    public class FoDicomListener : IDicomListener
    {
        private DicomServer<StoreSCP> _server;
        private readonly Func<DicomReceivedEventArgs, Task> _onReceivedHandler;

        /// <inheritdoc />
        public event Func<DicomReceivedEventArgs, Task> OnDicomReceived;

        public FoDicomListener()
        {
        }

        /// <inheritdoc />
        public async Task StartAsync(string aeTitle, string ip, int port)
        {
            _server = DicomServer.Create<StoreSCP>(port);
            StoreSCP.SetGlobalHandler(HandleReceived);
            await Task.CompletedTask;
        }

        private Task HandleReceived(DicomReceivedEventArgs args)
        {
            var handler = OnDicomReceived;
            if (handler != null)
            {
                return handler(args);
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync()
        {
            _server?.Dispose();
            _server = null;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _server?.Dispose();
        }

        /// <summary>
        /// Internal SCP implementation used by fo-dicom.
        /// </summary>
        private class StoreSCP : DicomService, IDicomServiceProvider, IDicomCStoreProvider
        {
            private static Func<DicomReceivedEventArgs, Task> _globalHandler;

            public StoreSCP(INetworkStream stream, Encoding fallbackEncoding, Logger log) : base(stream, fallbackEncoding, log)
            {
            }

            public static void SetGlobalHandler(Func<DicomReceivedEventArgs, Task> handler)
            {
                _globalHandler = handler;
            }

            public void OnReceiveAssociationRequest(DicomAssociation association)
            {
                foreach (var pc in association.PresentationContexts)
                {
                    pc.AcceptTransferSyntaxes(pc.GetTransferSyntaxes().ToArray());
                }
                SendAssociationAccept(association);
            }

            public void OnReceiveAssociationReleaseRequest()
            {
                SendAssociationReleaseResponse();
            }

            public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
            {
            }

            public void OnConnectionClosed(Exception exception)
            {
            }

            public DicomCStoreResponse OnCStoreRequest(DicomCStoreRequest request)
            {
                var ds = request.File.Dataset ?? request.Dataset;
                var meta = ExtractMetadata(ds);
                var args = new DicomReceivedEventArgs
                {
                    Dataset = ds,
                    Metadata = meta,
                    RemoteAET = Association.CallingAE
                };

                _globalHandler?.Invoke(args).GetAwaiter().GetResult();

                return new DicomCStoreResponse(request, DicomStatus.Success);
            }

            private IDictionary<string, string> ExtractMetadata(DicomDataset ds)
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                void TryAdd(DicomTag tag, string name)
                {
                    if (ds.TryGetSingleValue(tag, out string value))
                        dict[name] = value;
                }

                TryAdd(DicomTag.SliceThickness, "SliceThickness");
                TryAdd(DicomTag.Modality, "Modality");
                TryAdd(DicomTag.StudyDescription, "StudyDescription");
                TryAdd(DicomTag.SeriesDescription, "SeriesDescription");
                TryAdd(DicomTag.PatientID, "PatientID");
                TryAdd(DicomTag.BodyPartExamined, "BodyPartExamined");
                TryAdd(DicomTag.Rows, "Rows");
                TryAdd(DicomTag.Columns, "Columns");
                TryAdd(DicomTag.NumberOfFrames, "NumberOfSlices");
                TryAdd(DicomTag.SOPClassUID, "SOPClassUID");
                TryAdd(DicomTag.StudyDate, "StudyDate");

                return dict;
            }
        }
    }
}
