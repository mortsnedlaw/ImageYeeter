using System;
using System.Collections.Generic;
using System.Linq;

namespace DicomRouter.Infrastructure.Models;

/// <summary>
/// Represents a DICOM Transfer Syntax UID with metadata.
/// </summary>
public record TransferSyntaxInfo(
    string Uid,
    string Name,
    bool IsLossless,
    bool IsCompressed,
    string EncodingType)
{
    /// <summary>
    /// Common transfer syntaxes per DICOM standard.
    /// </summary>
    public static class Common
    {
        public static readonly TransferSyntaxInfo ImplicitVrLittleEndian =
            new("1.2.840.10008.1.2", "Implicit VR Little Endian", true, false, "Uncompressed");

        public static readonly TransferSyntaxInfo ExplicitVrLittleEndian =
            new("1.2.840.10008.1.2.1", "Explicit VR Little Endian", true, false, "Uncompressed");

        public static readonly TransferSyntaxInfo ExplicitVrBigEndian =
            new("1.2.840.10008.1.2.2", "Explicit VR Big Endian", true, false, "Uncompressed");

        public static readonly TransferSyntaxInfo RleLossless =
            new("1.2.840.10008.1.2.5", "RLE Lossless", true, true, "RLE");

        public static readonly TransferSyntaxInfo JpegBaseline =
            new("1.2.840.10008.1.2.4.50", "JPEG Baseline (Lossy)", false, true, "JPEG");

        public static readonly TransferSyntaxInfo JpegExtended =
            new("1.2.840.10008.1.2.4.51", "JPEG Extended (Lossy)", false, true, "JPEG");

        public static readonly TransferSyntaxInfo JpegLossless =
            new("1.2.840.10008.1.2.4.70", "JPEG Lossless", true, true, "JPEG");

        public static readonly TransferSyntaxInfo Jpeg2000Lossless =
            new("1.2.840.10008.1.2.4.91", "JPEG 2000 Lossless", true, true, "JPEG2000");

        public static readonly TransferSyntaxInfo Jpeg2000Lossy =
            new("1.2.840.10008.1.2.4.90", "JPEG 2000 Lossy", false, true, "JPEG2000");

        public static readonly TransferSyntaxInfo JpegLsLossless =
            new("1.2.840.10008.1.2.4.80", "JPEG-LS Lossless", true, true, "JPEG-LS");

        public static readonly TransferSyntaxInfo JpegLsLossy =
            new("1.2.840.10008.1.2.4.81", "JPEG-LS Lossy", false, true, "JPEG-LS");

        /// <summary>
        /// All commonly supported transfer syntaxes.
        /// </summary>
        public static IReadOnlyList<TransferSyntaxInfo> All { get; } = new[]
        {
            ImplicitVrLittleEndian,
            ExplicitVrLittleEndian,
            ExplicitVrBigEndian,
            RleLossless,
            JpegBaseline,
            JpegExtended,
            JpegLossless,
            Jpeg2000Lossless,
            Jpeg2000Lossy,
            JpegLsLossless,
            JpegLsLossy
        };
    }
}

/// <summary>
/// Represents a DICOM SOP Class UID.
/// </summary>
public record SopClassInfo(string Uid, string Name)
{
    /// <summary>
    /// Common Storage SOP Classes.
    /// </summary>
    public static class Storage
    {
        public static readonly SopClassInfo Ct =
            new("1.2.840.10008.5.1.4.1.1.2", "CT Image Storage");

        public static readonly SopClassInfo Mr =
            new("1.2.840.10008.5.1.4.1.1.4", "MR Image Storage");

        public static readonly SopClassInfo Us =
            new("1.2.840.10008.5.1.4.1.1.6.4", "Ultrasound Image Storage");

        public static readonly SopClassInfo Pt =
            new("1.2.840.10008.5.1.4.1.1.128", "PET Image Storage");

        public static readonly SopClassInfo Rtimage =
            new("1.2.840.10008.5.1.4.1.1.66.4", "RT Image Storage");

        public static readonly SopClassInfo Rtplan =
            new("1.2.840.10008.5.1.4.1.1.66.3", "RT Plan Storage");

        public static readonly SopClassInfo Rtdose =
            new("1.2.840.10008.5.1.4.1.1.66.2.4", "RT Dose Storage");

        public static readonly SopClassInfo Rtsrv =
            new("1.2.840.10008.5.1.4.1.1.66.5", "RT Structure Set Storage");

        public static readonly SopClassInfo SecondaryCaptureImageStorage =
            new("1.2.840.10008.5.1.4.1.1.7", "Secondary Capture Image Storage");

        public static readonly SopClassInfo Segmentation =
            new("1.2.840.10008.5.1.4.1.1.66.4", "Segmentation Storage");

        public static readonly SopClassInfo EnhancedMr =
            new("1.2.840.10008.5.1.4.1.1.4.1", "Enhanced MR Image Storage");

        public static readonly SopClassInfo EnhancedCt =
            new("1.2.840.10008.5.1.4.1.1.2.1", "Enhanced CT Image Storage");

        /// <summary>
        /// All common Storage SOP Classes.
        /// </summary>
        public static IReadOnlyList<SopClassInfo> All { get; } = new[]
        {
            Ct, Mr, Us, Pt, Rtimage, Rtplan, Rtdose, Rtsrv, SecondaryCaptureImageStorage, Segmentation, EnhancedMr, EnhancedCt
        };
    }
}

/// <summary>
/// Configuration for a single Presentation Context.
/// Represents negotiated SOP class with proposed and accepted transfer syntaxes.
/// </summary>
public class PresentationContextConfig
{
    /// <summary>
    /// Unique ID for this presentation context (1-255, odd numbers).
    /// </summary>
    public byte Id { get; set; } = 1;

    /// <summary>
    /// SOP Class UID (e.g., 1.2.840.10008.5.1.4.1.1.2 for CT).
    /// </summary>
    public string SopClassUid { get; set; } = string.Empty;

    /// <summary>
    /// Proposed transfer syntax UIDs in order of preference.
    /// At minimum, must include Implicit VR Little Endian.
    /// </summary>
    public List<string> ProposedTransferSyntaxes { get; set; } = new()
    {
        "1.2.840.10008.1.2" // Implicit VR Little Endian (required)
    };

    /// <summary>
    /// Accepted transfer syntax UID after negotiation.
    /// Initially null; set after successful A-ASSOCIATE-AC.
    /// </summary>
    public string? AcceptedTransferSyntax { get; set; }

    /// <summary>
    /// If true, this presentation context is accepted.
    /// </summary>
    public bool Accepted { get; set; } = true;

    /// <summary>
    /// Reason for rejection (if not accepted).
    /// </summary>
    public string? RejectionReason { get; set; }

    /// <summary>
    /// Validates that this presentation context is well-formed.
    /// </summary>
    public bool Validate(out string? error)
    {
        if (string.IsNullOrEmpty(SopClassUid))
        {
            error = "SopClassUid is required";
            return false;
        }

        if (ProposedTransferSyntaxes == null || ProposedTransferSyntaxes.Count == 0)
        {
            error = "At least one transfer syntax must be proposed";
            return false;
        }

        if (!ProposedTransferSyntaxes.Contains("1.2.840.10008.1.2"))
        {
            error = "Implicit VR Little Endian (1.2.840.10008.1.2) must always be supported";
            return false;
        }

        error = null;
        return true;
    }
}

/// <summary>
/// Complete DICOM Presentation Context configuration for a listener or destination.
/// </summary>
public class PresentationContextProfile
{
    /// <summary>
    /// Friendly name of this profile.
    /// </summary>
    public string Name { get; set; } = "Default";

    /// <summary>
    /// Presentation contexts by SOP Class UID.
    /// </summary>
    public List<PresentationContextConfig> PresentationContexts { get; set; } = new();

    /// <summary>
    /// Maximum PDU size to negotiate (in bytes).
    /// Must be at least 4096 per DICOM standard.
    /// </summary>
    public uint MaxPduSize { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// Creates a profile supporting all common Storage SOP Classes with intelligent transfer syntax negotiation.
    /// </summary>
    public static PresentationContextProfile CreateDefaultStorageProfile()
    {
        var profile = new PresentationContextProfile
        {
            Name = "Standard Storage",
            MaxPduSize = 16 * 1024 * 1024
        };

        byte contextId = 1;
        foreach (var sopClass in SopClassInfo.Storage.All)
        {
            if (contextId > 255) break;

            profile.PresentationContexts.Add(new PresentationContextConfig
            {
                Id = contextId,
                SopClassUid = sopClass.Uid,
                ProposedTransferSyntaxes = new List<string>
                {
                    "1.2.840.10008.1.2",      // Implicit VR Little Endian (required)
                    "1.2.840.10008.1.2.1",    // Explicit VR Little Endian
                    "1.2.840.10008.1.2.4.70", // JPEG Lossless
                    "1.2.840.10008.1.2.4.91", // JPEG 2000 Lossless
                    "1.2.840.10008.1.2.4.80", // JPEG-LS Lossless
                    "1.2.840.10008.1.2.5"     // RLE Lossless
                }
            });

            contextId += 2; // Only odd presentation context IDs
        }

        return profile;
    }

    /// <summary>
    /// Gets presentation context for a specific SOP Class UID, or creates a minimal one if missing.
    /// </summary>
    public PresentationContextConfig GetOrCreateContext(string sopClassUid)
    {
        var existing = PresentationContexts.FirstOrDefault(x => x.SopClassUid == sopClassUid);
        if (existing != null) return existing;

        // Create minimal context with required transfer syntaxes
        var minimal = new PresentationContextConfig
        {
            Id = (byte)(2 + PresentationContexts.Count * 2),
            SopClassUid = sopClassUid,
            ProposedTransferSyntaxes = new List<string>
            {
                "1.2.840.10008.1.2"  // Implicit VR Little Endian (minimum)
            }
        };

        PresentationContexts.Add(minimal);
        return minimal;
    }

    /// <summary>
    /// Validates all presentation contexts.
    /// </summary>
    public bool ValidateAll(out List<string> errors)
    {
        errors = new List<string>();

        if (MaxPduSize < 4096)
            errors.Add("MaxPduSize must be at least 4096");

        foreach (var ctx in PresentationContexts)
        {
            if (!ctx.Validate(out var error))
                errors.Add($"Context {ctx.Id}: {error}");
        }

        return errors.Count == 0;
    }
}
