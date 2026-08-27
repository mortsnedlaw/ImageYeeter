using System.Buffers.Binary;
using System.Text;

namespace DicomRouter.Infrastructure.Dicom;

public enum DicomTransferSyntax
{
    ImplicitVrLittleEndian,
    ExplicitVrLittleEndian
}

public readonly record struct DicomTag(ushort Group, ushort Element)
{
    public uint Value => ((uint)Group << 16) | Element;
    public override string ToString() => $"{Group:X4},{Element:X4}";

    public static readonly DicomTag SOPClassUid = new(0x0008, 0x0016);
    public static readonly DicomTag SOPInstanceUid = new(0x0008, 0x0018);
    public static readonly DicomTag StudyInstanceUid = new(0x0020, 0x000D);
    public static readonly DicomTag SeriesInstanceUid = new(0x0020, 0x000E);
    public static readonly DicomTag StudyDate = new(0x0008, 0x0020);
    public static readonly DicomTag Modality = new(0x0008, 0x0060);
    public static readonly DicomTag PatientId = new(0x0010, 0x0020);
    public static readonly DicomTag StudyDescription = new(0x0008, 0x1030);
    public static readonly DicomTag SeriesDescription = new(0x0008, 0x103E);
    public static readonly DicomTag SliceThickness = new(0x0018, 0x0050);
    public static readonly DicomTag BodyPartExamined = new(0x0018, 0x0015);
    public static readonly DicomTag Rows = new(0x0028, 0x0010);
    public static readonly DicomTag Columns = new(0x0028, 0x0011);
    public static readonly DicomTag NumberOfFrames = new(0x0028, 0x0008);
    public static readonly DicomTag CommandField = new(0x0000, 0x0100);
    public static readonly DicomTag MessageId = new(0x0000, 0x0110);
    public static readonly DicomTag MessageIdBeingRespondedTo = new(0x0000, 0x0120);
    public static readonly DicomTag CommandDataSetType = new(0x0000, 0x0800);
    public static readonly DicomTag Status = new(0x0000, 0x0900);
    public static readonly DicomTag AffectedSopClassUid = new(0x0000, 0x0002);
    public static readonly DicomTag RequestedSopClassUid = new(0x0000, 0x0003);
    public static readonly DicomTag AffectedSopInstanceUid = new(0x0000, 0x1000);
    public static readonly DicomTag RequestedSopInstanceUid = new(0x0000, 0x1001);
}

public sealed record DicomElement(DicomTag Tag, string VR, byte[] Value)
{
    public string Text => VR is "US" or "SS" && Value.Length >= 2
        ? BinaryPrimitives.ReadUInt16LittleEndian(Value).ToString(System.Globalization.CultureInfo.InvariantCulture)
        : Encoding.ASCII.GetString(Value).TrimEnd('\0', ' ');
    public int Integer => Value.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(Value) : 0;
}

public sealed class NativeDicomDataset
{
    private readonly List<DicomElement> _elements = new();
    public IReadOnlyList<DicomElement> Elements => _elements;
    public byte[] OriginalBytes { get; }
    public DicomTransferSyntax TransferSyntax { get; }

    public NativeDicomDataset(IEnumerable<DicomElement> elements, byte[]? originalBytes = null, DicomTransferSyntax syntax = DicomTransferSyntax.ExplicitVrLittleEndian)
    {
        _elements.AddRange(elements);
        OriginalBytes = originalBytes ?? Write(syntax);
        TransferSyntax = syntax;
    }

    public string Get(DicomTag tag) => _elements.FirstOrDefault(x => x.Tag == tag)?.Text ?? string.Empty;
    public ushort GetUInt16(DicomTag tag) => _elements.FirstOrDefault(x => x.Tag == tag)?.Integer is var value && value >= 0 ? (ushort)value : (ushort)0;
    public string Get(uint tag) => Get(new DicomTag((ushort)(tag >> 16), (ushort)tag));

    public static NativeDicomDataset Parse(ReadOnlySpan<byte> bytes, DicomTransferSyntax syntax, bool commandSet = false)
    {
        var elements = new List<DicomElement>();
        var offset = 0;
        while (offset + 8 <= bytes.Length)
        {
            var group = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
            var element = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset + 2, 2));
            var tag = new DicomTag(group, element);
            offset += 4;
            var explicitVr = !commandSet && syntax == DicomTransferSyntax.ExplicitVrLittleEndian;
            string vr;
            uint length;
            if (explicitVr)
            {
                vr = Encoding.ASCII.GetString(bytes.Slice(offset, 2));
                offset += 2;
                if (IsLongVr(vr))
                {
                    offset += 2;
                    length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
                    offset += 4;
                }
                else
                {
                    length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
                    offset += 2;
                }
            }
            else
            {
                vr = commandSet ? CommandVr(tag) : "UN";
                length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
                offset += 4;
            }

            if (length == 0xffffffff || length > int.MaxValue || offset + (int)length > bytes.Length)
                break;
            elements.Add(new DicomElement(tag, vr, bytes.Slice(offset, (int)length).ToArray()));
            offset += (int)length;
        }
        return new NativeDicomDataset(elements, bytes.ToArray(), syntax);
    }

    public byte[] Write(DicomTransferSyntax syntax)
    {
        using var stream = new MemoryStream();
        foreach (var item in _elements)
        {
            Span<byte> tag = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(tag, item.Tag.Group);
            BinaryPrimitives.WriteUInt16LittleEndian(tag[2..], item.Tag.Element);
            stream.Write(tag);
            var vr = syntax == DicomTransferSyntax.ExplicitVrLittleEndian ? (item.VR.Length == 2 ? item.VR : "UN") : "";
            if (vr.Length > 0)
            {
                stream.Write(Encoding.ASCII.GetBytes(vr));
                if (IsLongVr(vr))
                {
                    stream.Write(new byte[2]);
                    WriteUInt32(stream, (uint)item.Value.Length);
                }
                else WriteUInt16(stream, (ushort)item.Value.Length);
            }
            else WriteUInt32(stream, (uint)item.Value.Length);
            stream.Write(item.Value);
        }
        return stream.ToArray();
    }

    private static bool IsLongVr(string vr) => vr is "OB" or "OD" or "OF" or "OL" or "OV" or "OW" or "SQ" or "UC" or "UR" or "UT" or "UN";
    private static string CommandVr(DicomTag tag) => tag == DicomTag.CommandField || tag == DicomTag.MessageId || tag == DicomTag.MessageIdBeingRespondedTo || tag == DicomTag.CommandDataSetType || tag == DicomTag.Status ? "US" : "UI";
    private static void WriteUInt16(Stream s, ushort value) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, value); s.Write(b); }
    private static void WriteUInt32(Stream s, uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); s.Write(b); }
}
