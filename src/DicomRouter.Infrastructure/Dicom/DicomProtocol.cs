using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace DicomRouter.Infrastructure.Dicom;

internal enum PduType : byte { AssociateRequest = 1, AssociateAccept = 2, Reject = 3, Data = 4, ReleaseRequest = 5, ReleaseResponse = 6, Abort = 7 }
internal sealed record PresentationContext(byte Id, string AbstractSyntax, string TransferSyntax, bool Accepted);

internal static class DicomProtocol
{
    private static readonly byte[] UidImplicit = Encoding.ASCII.GetBytes("1.2.840.10008.1.2\0");
    private static readonly byte[] UidExplicit = Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0");

    public static async Task<(PduType Type, byte[] Body)> ReadPduAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = await ReadExactlyAsync(stream, 6, ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(2));
        return ((PduType)header[0], await ReadExactlyAsync(stream, checked((int)length), ct).ConfigureAwait(false));
    }

    public static async Task WritePduAsync(NetworkStream stream, PduType type, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        var header = new byte[6];
        header[0] = (byte)type;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(2), (uint)body.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
    }

    public static byte[] BuildAssociateRequest(string callingAe, string calledAe, IReadOnlyList<string> sopClasses)
    {
        using var body = new MemoryStream();
        WriteUInt16(body, 1); WriteUInt16(body, 0);
        WriteFixed(body, calledAe, 16); WriteFixed(body, callingAe, 16); body.Write(new byte[32]);
        byte contextId = 1;
        foreach (var sop in sopClasses.Distinct(StringComparer.Ordinal))
        {
            using var item = new MemoryStream();
            item.WriteByte(contextId); item.WriteByte(0); WriteAsciiItem(item, 0x30, sop);
            WriteAsciiItem(item, 0x40, "1.2.840.10008.1.2.1");
            WriteItem(body, 0x20, item.ToArray()); contextId += 2;
        }
        using var app = new MemoryStream(); WriteAsciiItem(app, 0x10, "1.2.840.10008.3.1.1.1"); WriteAsciiItem(app, 0x20, "1.2.840.10008.1.2.0"); WriteItem(body, 0x10, app.ToArray());
        return body.ToArray();
    }

    public static (string CallingAe, string CalledAe, List<PresentationContext> Contexts) ParseAssociateRequest(byte[] body)
    {
        var called = Encoding.ASCII.GetString(body, 4, 16).Trim();
        var calling = Encoding.ASCII.GetString(body, 20, 16).Trim();
        var contexts = new List<PresentationContext>();
        var offset = 68;
        while (offset + 4 <= body.Length)
        {
            var type = body[offset]; var length = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(offset + 2));
            if (offset + 4 + length > body.Length) break;
            if (type == 0x20)
            {
                var item = body.AsSpan(offset + 4, length); var id = item[0]; var p = 4; string abstractUid = ""; var transferUid = "";
                while (p + 4 <= item.Length)
                {
                    var il = BinaryPrimitives.ReadUInt16BigEndian(item.Slice(p + 2));
                    if (p + 4 + il > item.Length) break;
                    var value = Encoding.ASCII.GetString(item.Slice(p + 4, il)).TrimEnd('\0');
                    if (item[p] == 0x30) abstractUid = value; else if (item[p] == 0x40) transferUid = value;
                    p += 4 + il;
                }
                contexts.Add(new PresentationContext(id, abstractUid, transferUid, false));
            }
            offset += 4 + length;
        }
        return (calling, called, contexts);
    }

    public static List<PresentationContext> ParseAssociateAccept(byte[] body)
    {
        var contexts = new List<PresentationContext>();
        var offset = 68;
        while (offset + 4 <= body.Length)
        {
            var type = body[offset]; var length = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(offset + 2));
            if (offset + 4 + length > body.Length) break;
            if (type == 0x21)
            {
                var item = body.AsSpan(offset + 4, length);
                var transfer = item.Length >= 8 ? Encoding.ASCII.GetString(item.Slice(8)).TrimEnd('\0') : "";
                contexts.Add(new PresentationContext(item[0], "", transfer, item.Length > 4 && item[2] == 0));
            }
            offset += 4 + length;
        }
        return contexts;
    }

    public static byte[] BuildAssociateAccept(string calledAe, string callingAe, IEnumerable<PresentationContext> contexts)
    {
        var body = new byte[74]; BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(), 1); BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(2), 0);
        WriteFixed(body, 4, calledAe, 16); WriteFixed(body, 20, callingAe, 16);
        using var tail = new MemoryStream();
        foreach (var context in contexts) { var item = new byte[] { context.Id, 0, 0, 0 }; using var content = new MemoryStream(); content.WriteByte(context.Id); content.WriteByte(0); WriteAsciiItem(content, 0x40, context.TransferSyntax); var data = content.ToArray(); WriteItem(tail, 0x21, data); }
        using var app = new MemoryStream(); WriteAsciiItem(app, 0x10, "1.2.840.10008.3.1.1.1"); WriteAsciiItem(app, 0x20, "1.2.840.10008.1.2.0"); WriteItem(tail, 0x10, app.ToArray());
        return body.Concat(tail.ToArray()).ToArray();
    }

    public static byte[] BuildAssociateReject(byte result = 1, byte source = 1, byte reason = 7) => new byte[] { 0, 0, result, source, reason };

    public static byte[] BuildCommand(ushort commandField, ushort messageId, ushort datasetType, string sopClass, uint sopInstance = 0, ushort status = 0)
    {
        var parts = new List<(DicomTag Tag, string Vr, byte[] Value)>();
        AddUs(parts, DicomTag.CommandField, commandField);
        if (messageId != 0) AddUs(parts, DicomTag.MessageId, messageId);
        if (sopClass.Length > 0) parts.Add((commandField is 0x0001 or 0x8001 ? DicomTag.AffectedSopClassUid : DicomTag.RequestedSopClassUid, "UI", PaddedUid(sopClass)));
        if (sopInstance != 0) parts.Add((commandField == 0x0001 ? DicomTag.AffectedSopInstanceUid : DicomTag.RequestedSopInstanceUid, "UI", PaddedUid(sopInstance.ToString())));
        AddUs(parts, DicomTag.CommandDataSetType, datasetType); if (status != 0) AddUs(parts, DicomTag.Status, status);
        var withoutLength = WriteCommandElements(parts); var result = new List<(DicomTag, string, byte[])> { (new DicomTag(0, 0), "UL", BitConverter.GetBytes((uint)withoutLength.Length)) }; result.AddRange(parts);
        return WriteCommandElements(result);
    }

    public static byte[] BuildDataPdu(byte contextId, byte control, byte[] command, byte[]? dataset = null)
    {
        using var body = new MemoryStream();
        WritePdv(body, contextId, 0x03, command);
        if (dataset is not null) WritePdv(body, contextId, 0x02, dataset);
        return body.ToArray();
    }

    public static IEnumerable<(byte ContextId, byte Control, byte[] Payload)> ParseDataPdu(byte[] body)
    {
        var offset = 0; while (offset + 4 <= body.Length) { var length = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(offset)); if (length < 2 || length > body.Length - offset - 4) yield break; var id = body[offset + 4]; var control = body[offset + 5]; yield return (id, control, body.AsSpan(offset + 6, (int)length - 2).ToArray()); offset += 4 + (int)length; }
    }

    private static byte[] WriteCommandElements(IEnumerable<(DicomTag Tag, string Vr, byte[] Value)> values)
    { var dataset = new NativeDicomDataset(values.Select(x => new DicomElement(x.Tag, x.Vr, x.Value))); return dataset.Write(DicomTransferSyntax.ImplicitVrLittleEndian); }
    private static void AddUs(List<(DicomTag, string, byte[])> list, DicomTag tag, ushort value) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, value); list.Add((tag, "US", b)); }
    private static byte[] PaddedUid(string value) { var b = Encoding.ASCII.GetBytes(value); return b.Length % 2 == 0 ? b : b.Concat(new byte[] { 0 }).ToArray(); }
    private static void WritePdv(Stream stream, byte id, byte control, byte[] payload) { WriteUInt32Big(stream, (uint)(payload.Length + 2)); stream.WriteByte(id); stream.WriteByte(control); stream.Write(payload); }
    private static void WriteItem(Stream stream, byte type, byte[] data) { stream.WriteByte(type); stream.WriteByte(0); WriteUInt16Big(stream, (ushort)data.Length); stream.Write(data); }
    private static void WriteAsciiItem(Stream stream, byte type, string value) => WriteItem(stream, type, Encoding.ASCII.GetBytes(value));
    private static void WriteFixed(Stream stream, string value, int length) => stream.Write(Encoding.ASCII.GetBytes(value.PadRight(length).Substring(0, length)));
    private static void WriteFixed(byte[] buffer, int offset, string value, int length) => Encoding.ASCII.GetBytes(value.PadRight(length).Substring(0, length)).CopyTo(buffer, offset);
    private static void WriteUInt16(Stream s, ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); s.Write(b); }
    private static void WriteUInt16Big(Stream s, ushort v) => WriteUInt16(s, v);
    private static void WriteUInt32Big(Stream s, uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); s.Write(b); }
    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count, CancellationToken ct) { var data = new byte[count]; var offset = 0; while (offset < count) { var n = await stream.ReadAsync(data.AsMemory(offset, count - offset), ct).ConfigureAwait(false); if (n == 0) throw new EndOfStreamException(); offset += n; } return data; }
}
