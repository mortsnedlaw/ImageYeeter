using System.Net;
using System.Net.Sockets;
using System.Text;
using DicomRouter.Infrastructure;

namespace DicomRouter.Infrastructure.Dicom;

public sealed class NativeDicomListener : IDicomListener
{
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    private Task? _acceptLoop;
    private string _aeTitle = string.Empty;
    private readonly int _maxPduSize;
    private readonly SemaphoreSlim _associationSlots;
    private readonly TimeSpan _associationTimeout;
    private readonly TimeSpan _receiveTimeout;
    private readonly IRuntimeEventBus? _events;
    public event Func<DicomReceivedEventArgs, Task>? OnDicomReceived;

    public NativeDicomListener(int maxPduSize = 16 * 1024 * 1024, int maxAssociations = 32, int associationTimeoutSeconds = 30, int receiveTimeoutSeconds = 60, IRuntimeEventBus? events = null)
    {
        _maxPduSize = Math.Clamp(maxPduSize, 1024, 64 * 1024 * 1024);
        _associationSlots = new SemaphoreSlim(Math.Max(1, maxAssociations));
        _associationTimeout = TimeSpan.FromSeconds(Math.Max(1, associationTimeoutSeconds));
        _receiveTimeout = TimeSpan.FromSeconds(Math.Max(1, receiveTimeoutSeconds));
        _events = events;
    }

    public Task StartAsync(string aeTitle, string ip, int port)
    {
        if (_listener != null) throw new InvalidOperationException("Listener is already running.");
        _aeTitle = aeTitle;
        _stop = new CancellationTokenSource();
        var address = ip is "0.0.0.0" or "*" ? IPAddress.Any : IPAddress.Parse(ip);
        _listener = new TcpListener(address, port);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_stop.Token);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try { while (!ct.IsCancellationRequested) { var client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false); _ = HandleClientAsync(client, ct); } }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverToken)
    {
        if (!await _associationSlots.WaitAsync(TimeSpan.Zero, serverToken).ConfigureAwait(false))
        {
            client.Dispose();
            return;
        }
        using (client)
        using (var stream = client.GetStream())
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(serverToken))
        {
            try
            {
                linked.CancelAfter(_associationTimeout);
                var (type, body) = await DicomProtocol.ReadPduAsync(stream, linked.Token, _maxPduSize).ConfigureAwait(false);
                _events?.Publish(new RuntimeEvent(RuntimeEventType.ProtocolEvent, DateTime.UtcNow, $"TCP {type} ({body.Length} bytes)"));
                if (type != PduType.AssociateRequest) return;
                var request = DicomProtocol.ParseAssociateRequest(body);
                _events?.Publish(new RuntimeEvent(RuntimeEventType.AssociationOpened, DateTime.UtcNow, $"{request.CallingAe} -> {_aeTitle}"));
                if (!string.Equals(request.CalledAe, _aeTitle, StringComparison.OrdinalIgnoreCase))
                {
                    await DicomProtocol.WritePduAsync(stream, PduType.Reject, DicomProtocol.BuildAssociateReject(), linked.Token).ConfigureAwait(false);
                    return;
                }
                var accepted = request.Contexts.Select(context => context with
                {
                    Accepted = context.AbstractSyntax is "1.2.840.10008.1.1" or "1.2.840.10008.5.1.4.1.1.2" or "1.2.840.10008.5.1.4.1.1.4",
                    TransferSyntax = context.TransferSyntax
                }).ToList();
                if (!accepted.Any(x => x.Accepted))
                {
                    await DicomProtocol.WritePduAsync(stream, PduType.Reject, DicomProtocol.BuildAssociateReject(1, 1, 3), linked.Token).ConfigureAwait(false);
                    return;
                }
                await DicomProtocol.WritePduAsync(stream, PduType.AssociateAccept, DicomProtocol.BuildAssociateAccept(_aeTitle, request.CallingAe, accepted.Where(x => x.Accepted)), linked.Token).ConfigureAwait(false);
                linked.CancelAfter(_receiveTimeout);
                await MessageLoopAsync(stream, accepted, request.CallingAe, linked.Token).ConfigureAwait(false);
            }
            catch (EndOfStreamException) { }
            catch (OperationCanceledException) { }
            catch { try { await DicomProtocol.WritePduAsync(stream, PduType.Abort, new byte[] { 0, 0, 0, 0 }, CancellationToken.None); } catch { } }
            finally { _associationSlots.Release(); }
        }
    }

    private async Task MessageLoopAsync(NetworkStream stream, IReadOnlyList<PresentationContext> contexts, string remoteAe, CancellationToken ct)
    {
        var command = new MemoryStream(); var dataset = new MemoryStream(); byte contextId = 0; var commandComplete = false;
        while (!ct.IsCancellationRequested)
        {
            var (type, body) = await DicomProtocol.ReadPduAsync(stream, ct, _maxPduSize).ConfigureAwait(false);
            _events?.Publish(new RuntimeEvent(RuntimeEventType.ProtocolEvent, DateTime.UtcNow, $"PDU {type} ({body.Length} bytes)"));
            if (type == PduType.ReleaseRequest) { await DicomProtocol.WritePduAsync(stream, PduType.ReleaseResponse, new byte[4], ct); return; }
            if (type == PduType.Abort) return;
            if (type != PduType.Data) continue;
            foreach (var pdv in DicomProtocol.ParseDataPdu(body))
            {
                contextId = pdv.ContextId;
                if ((pdv.Control & 1) != 0)
                {
                    command.Write(pdv.Payload);
                    commandComplete = (pdv.Control & 2) != 0;
                    if (commandComplete && NativeDicomDataset.Parse(command.ToArray(), DicomTransferSyntax.ImplicitVrLittleEndian, true).GetUInt16(DicomTag.CommandDataSetType) == 0x0101)
                    {
                        var commandSet = NativeDicomDataset.Parse(command.ToArray(), DicomTransferSyntax.ImplicitVrLittleEndian, true);
                        await HandleCommandAsync(stream, contexts, contextId, remoteAe, commandSet, null, ct).ConfigureAwait(false);
                        command.SetLength(0); commandComplete = false;
                    }
                }
                else
                {
                    dataset.Write(pdv.Payload);
                    if ((pdv.Control & 2) != 0 && commandComplete)
                    {
                        var commandSet = NativeDicomDataset.Parse(command.ToArray(), DicomTransferSyntax.ImplicitVrLittleEndian, true);
                        var data = dataset.ToArray();
                        await HandleCommandAsync(stream, contexts, contextId, remoteAe, commandSet, data, ct).ConfigureAwait(false);
                        command.SetLength(0); commandComplete = false;
                        dataset.SetLength(0);
                    }
                }
            }
        }
    }

    private async Task HandleCommandAsync(NetworkStream stream, IReadOnlyList<PresentationContext> contexts, byte contextId, string remoteAe, NativeDicomDataset command, byte[]? data, CancellationToken ct)
    {
        var field = command.GetUInt16(DicomTag.CommandField); var messageId = command.GetUInt16(DicomTag.MessageId);
        var context = contexts.FirstOrDefault(x => x.Id == contextId && x.Accepted); if (context == null) return;
        if (field == 0x0030)
        {
            var response = DicomProtocol.BuildCommand(0x8030, messageId, 0x0101, "1.2.840.10008.1.1", status: 0);
            await DicomProtocol.WritePduAsync(stream, PduType.Data, DicomProtocol.BuildDataPdu(contextId, 0, response), ct).ConfigureAwait(false);
            return;
        }
        if (field != 0x0001 || data == null) return;
        var syntax = context.TransferSyntax == "1.2.840.10008.1.2" ? DicomTransferSyntax.ImplicitVrLittleEndian : DicomTransferSyntax.ExplicitVrLittleEndian;
        var dataset = NativeDicomDataset.Parse(data, syntax);
        var args = new DicomReceivedEventArgs { Dataset = dataset, RawDataset = data, TransferSyntaxUid = context.TransferSyntax, Metadata = DicomMetadata.Extract(dataset), RemoteAET = remoteAe };
        _events?.Publish(new RuntimeEvent(RuntimeEventType.DicomObjectReceived, DateTime.UtcNow, $"C-STORE {dataset.Get(DicomTag.SOPInstanceUid)}", Data: new Dictionary<string, string> { ["TransferSyntax"] = context.TransferSyntax }));
        ushort status = 0;
        try { if (OnDicomReceived != null) await OnDicomReceived(args).ConfigureAwait(false); }
        catch { status = 0x0110; }
        var responseCommand = DicomProtocol.BuildCommand(0x8001, messageId, 0x0101, dataset.Get(DicomTag.SOPClassUid), sopInstance: dataset.Get(DicomTag.SOPInstanceUid), status: status);
        await DicomProtocol.WritePduAsync(stream, PduType.Data, DicomProtocol.BuildDataPdu(contextId, 0, responseCommand), ct).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        _stop?.Cancel(); _listener?.Stop();
        if (_acceptLoop != null) try { await _acceptLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _acceptLoop = null; _listener = null; _stop?.Dispose(); _stop = null;
    }

    public void Dispose() { StopAsync().GetAwaiter().GetResult(); _associationSlots.Dispose(); }
}

internal static class DicomMetadata
{
    public static Dictionary<string, string> Extract(NativeDicomDataset dataset) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SliceThickness"] = dataset.Get(DicomTag.SliceThickness), ["Modality"] = dataset.Get(DicomTag.Modality),
        ["StudyDescription"] = dataset.Get(DicomTag.StudyDescription), ["SeriesDescription"] = dataset.Get(DicomTag.SeriesDescription),
        ["PatientID"] = dataset.Get(DicomTag.PatientId), ["BodyPartExamined"] = dataset.Get(DicomTag.BodyPartExamined),
        ["Rows"] = dataset.Get(DicomTag.Rows), ["Columns"] = dataset.Get(DicomTag.Columns), ["NumberOfSlices"] = dataset.Get(DicomTag.NumberOfFrames),
        ["SOPClassUID"] = dataset.Get(DicomTag.SOPClassUid), ["StudyDate"] = dataset.Get(DicomTag.StudyDate)
    }.Concat(dataset.Elements.ToDictionary(x => $"({x.Tag.Group:X4},{x.Tag.Element:X4})", x => x.Text, StringComparer.OrdinalIgnoreCase)).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
}

internal static class DicomValueExtensions
{
    public static int AsInteger(this string value) => int.TryParse(value, out var result) ? result : 0;
}
