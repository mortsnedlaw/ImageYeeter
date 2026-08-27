using System.Net.Sockets;
using System.Threading;
using DicomRouter.Infrastructure.Models;
using DicomRouter.Infrastructure;

namespace DicomRouter.Infrastructure.Dicom
{
    /// <summary>
    /// Forwards stored datasets using a native DICOM association.
    /// </summary>
    public class DicomForwarder
    {
        /// <summary>
        /// Sends the provided dataset to the destination endpoint.
        /// </summary>
        private int _messageId;
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
        public IRuntimeEventBus? Events { get; init; }

        public async Task<bool> EchoAsync(string host, int port, string calledAETitle, string callingAETitle, CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
                await using var stream = client.GetStream();
                await DicomProtocol.WritePduAsync(stream, PduType.AssociateRequest, DicomProtocol.BuildAssociateRequest(callingAETitle, calledAETitle, new[] { "1.2.840.10008.1.1" }), timeout.Token).ConfigureAwait(false);
                Events?.Publish(new RuntimeEvent(RuntimeEventType.ForwardStarted, DateTime.UtcNow, $"C-ECHO {calledAETitle}"));
                var association = await DicomProtocol.ReadPduAsync(stream, timeout.Token).ConfigureAwait(false);
                if (association.Type != PduType.AssociateAccept) return false;
                var context = DicomProtocol.ParseAssociateAccept(association.Body).FirstOrDefault(x => x.Accepted);
                if (context == null) return false;
                var id = (ushort)Interlocked.Increment(ref _messageId);
                var command = DicomProtocol.BuildCommand(0x0030, id, 0x0101, "1.2.840.10008.1.1");
                await DicomProtocol.WritePduAsync(stream, PduType.Data, DicomProtocol.BuildDataPdu(context.Id, 0, command), timeout.Token).ConfigureAwait(false);
                while (true)
                {
                    var response = await DicomProtocol.ReadPduAsync(stream, timeout.Token).ConfigureAwait(false);
                    if (response.Type != PduType.Data) return false;
                    using var responseBytes = new MemoryStream();
                    foreach (var pdv in DicomProtocol.ParseDataPdu(response.Body))
                    {
                        responseBytes.Write(pdv.Payload);
                        if ((pdv.Control & 2) == 0) continue;
                        var responseCommand = NativeDicomDataset.Parse(responseBytes.ToArray(), DicomTransferSyntax.ImplicitVrLittleEndian, true);
                        responseBytes.SetLength(0);
                        if (responseCommand.GetUInt16(DicomTag.MessageIdBeingRespondedTo) == id)
                        {
                            await DicomProtocol.WritePduAsync(stream, PduType.ReleaseRequest, new byte[4], timeout.Token).ConfigureAwait(false);
                            var success = responseCommand.GetUInt16(DicomTag.Status) == 0;
                            Events?.Publish(new RuntimeEvent(success ? RuntimeEventType.ForwardSucceeded : RuntimeEventType.ForwardFailed, DateTime.UtcNow, $"C-ECHO {calledAETitle}"));
                            return success;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { return false; }
            catch (SocketException) { return false; }
            catch (IOException) { return false; }
        }

        public async Task<bool> ForwardAsync(NativeDicomDataset dataset, Destination dest, string callingAET = "DICOMROUTER", CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(dest.Host, dest.Port, timeout.Token).ConfigureAwait(false);
                await using var stream = client.GetStream();
                var sopClass = dataset.Get(DicomTag.SOPClassUid);
                if (string.IsNullOrWhiteSpace(sopClass)) return false;
                await DicomProtocol.WritePduAsync(stream, PduType.AssociateRequest, DicomProtocol.BuildAssociateRequest(callingAET, dest.AeTitle, new[] { sopClass }, dataset.TransferSyntax), timeout.Token).ConfigureAwait(false);
                Events?.Publish(new RuntimeEvent(RuntimeEventType.ForwardStarted, DateTime.UtcNow, $"C-STORE {dest.Name}"));
                var association = await DicomProtocol.ReadPduAsync(stream, timeout.Token).ConfigureAwait(false);
                if (association.Type != PduType.AssociateAccept) return false;
                var context = DicomProtocol.ParseAssociateAccept(association.Body).FirstOrDefault(x => x.Accepted);
                if (context == null) return false;
                var negotiatedPdu = DicomProtocol.ParseMaximumPduLength(association.Body);
                var messageId = (ushort)Interlocked.Increment(ref _messageId);
                var command = DicomProtocol.BuildCommand(0x0001, messageId, 0x0000, sopClass, dataset.Get(DicomTag.SOPInstanceUid));
                await DicomProtocol.WriteDataPdusAsync(stream, context.Id, command, dataset.OriginalBytes, Math.Min(dest.MaxPduSize, negotiatedPdu), timeout.Token).ConfigureAwait(false);
                while (true)
                {
                    var response = await DicomProtocol.ReadPduAsync(stream, timeout.Token).ConfigureAwait(false);
                    if (response.Type == PduType.Abort || response.Type == PduType.ReleaseRequest) return false;
                    if (response.Type != PduType.Data) continue;
                    using var responseBytes = new MemoryStream();
                    foreach (var pdv in DicomProtocol.ParseDataPdu(response.Body))
                    {
                        responseBytes.Write(pdv.Payload);
                        if ((pdv.Control & 2) == 0) continue;
                        var responseCommand = NativeDicomDataset.Parse(responseBytes.ToArray(), DicomTransferSyntax.ImplicitVrLittleEndian, true);
                        responseBytes.SetLength(0);
                        if (responseCommand.GetUInt16(DicomTag.MessageIdBeingRespondedTo) == messageId)
                        {
                            var status = responseCommand.GetUInt16(DicomTag.Status);
                            await DicomProtocol.WritePduAsync(stream, PduType.ReleaseRequest, new byte[4], timeout.Token).ConfigureAwait(false);
                            var success = status == 0;
                            Events?.Publish(new RuntimeEvent(success ? RuntimeEventType.ForwardSucceeded : RuntimeEventType.ForwardFailed, DateTime.UtcNow, $"C-STORE {dest.Name}"));
                            return success;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return false;
            }
            catch (SocketException) { return false; }
            catch (IOException) { return false; }
        }
    }
}
