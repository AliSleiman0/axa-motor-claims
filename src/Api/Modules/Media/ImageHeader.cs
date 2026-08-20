using System.Buffers.Binary;

namespace Api.Modules.Media;

/// <summary>What <see cref="ImageHeader.Read"/> could make of the bytes it was given.</summary>
public enum ImageHeaderStatus
{
    /// <summary>Dimensions read.</summary>
    Ok,

    /// <summary>The magic bytes are not an image this app accepts.</summary>
    NotAnImage,

    /// <summary>An image, but its size marker sits past the bytes supplied — see the prefix ceiling.</summary>
    Incomplete,
}

public readonly record struct ImageInfo(string ContentType, int Width, int Height);

/// <summary>
/// Reads pixel dimensions out of a JPEG or PNG header. Pure, span-based, no allocation, no library.
///
/// **Why no library.** design.md §7.2 item 5 requires the server to re-validate dimensions, so
/// something has to decode a header. `System.Drawing` is Windows-only and this ships to Linux
/// containers (§3); SixLabors.ImageSharp v3 is under a split licence that becomes a procurement
/// question the day AXA takes ownership of the source (HANDOFF §5), which is a poor trade for ninety
/// lines of header parsing. The BRD needs width and height, not decoding.
///
/// It doubles as the content-type check: the returned <see cref="ImageInfo.ContentType"/> is what the
/// bytes actually are, so a caller who declares `image/jpeg` and sends a PDF is caught rather than
/// believed. §9's threat model for the public surface (slice 5.3, which reuses this) assumes the
/// client is hostile.
/// </summary>
public static class ImageHeader
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PdfSignature = [0x25, 0x50, 0x44, 0x46, 0x2D]; // "%PDF-"

    public const string Pdf = "application/pdf";

    public static ImageHeaderStatus Read(ReadOnlySpan<byte> header, out ImageInfo info)
    {
        info = default;

        if (header.StartsWith(PngSignature))
        {
            return ReadPng(header, out info);
        }

        // SOI. Everything else about a JPEG is a walk of length-prefixed segments.
        return header.Length >= 2 && header[0] == 0xFF && header[1] == 0xD8
            ? ReadJpeg(header, out info)
            : ImageHeaderStatus.NotAnImage;
    }

    public static bool LooksLikePdf(ReadOnlySpan<byte> header) => header.StartsWith(PdfSignature);

    /// <summary>
    /// What these bytes actually are, for the buckets that accept non-images. Only the formats §7.1's
    /// document buckets allow are recognised; anything else is refused rather than guessed at.
    /// Returns null for a truncated image header too — callers that need to tell those apart call
    /// <see cref="Read"/> and check for <see cref="ImageHeaderStatus.Incomplete"/>.
    /// </summary>
    public static string? SniffContentType(ReadOnlySpan<byte> header)
    {
        if (LooksLikePdf(header))
        {
            return Pdf;
        }

        return Read(header, out var info) == ImageHeaderStatus.Ok ? info.ContentType : null;
    }

    private static ImageHeaderStatus ReadPng(ReadOnlySpan<byte> header, out ImageInfo info)
    {
        info = default;

        // Signature, then the IHDR chunk: 4-byte length, 4-byte type, then width and height.
        const int IhdrWidthOffset = 16;
        if (header.Length < IhdrWidthOffset + 8)
        {
            return ImageHeaderStatus.Incomplete;
        }

        if (!header.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return ImageHeaderStatus.NotAnImage;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(IhdrWidthOffset, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(IhdrWidthOffset + 4, 4));

        if (width <= 0 || height <= 0)
        {
            return ImageHeaderStatus.NotAnImage;
        }

        info = new ImageInfo(Png, width, height);
        return ImageHeaderStatus.Ok;
    }

    private static ImageHeaderStatus ReadJpeg(ReadOnlySpan<byte> header, out ImageInfo info)
    {
        info = default;
        var position = 2;

        while (position + 1 < header.Length)
        {
            // Markers are FF-prefixed; a run of FF fill bytes between segments is legal padding.
            if (header[position] != 0xFF)
            {
                return ImageHeaderStatus.NotAnImage;
            }

            var marker = header[position + 1];
            position += 2;

            if (marker == 0xFF)
            {
                position--;
                continue;
            }

            // Standalone markers carry no length: TEM, the restart markers, SOI and EOI.
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
            {
                continue;
            }

            if (position + 2 > header.Length)
            {
                return ImageHeaderStatus.Incomplete;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(position, 2));
            if (segmentLength < 2)
            {
                return ImageHeaderStatus.NotAnImage;
            }

            if (IsStartOfFrame(marker))
            {
                // SOF payload: 1 byte sample precision, then height and width, both big-endian.
                if (position + 7 > header.Length)
                {
                    return ImageHeaderStatus.Incomplete;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(position + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(position + 5, 2));

                if (width == 0 || height == 0)
                {
                    return ImageHeaderStatus.NotAnImage;
                }

                info = new ImageInfo(Jpeg, width, height);
                return ImageHeaderStatus.Ok;
            }

            // SOS: entropy-coded scan data follows and there is no SOF ahead of it in a valid file.
            if (marker == 0xDA)
            {
                return ImageHeaderStatus.NotAnImage;
            }

            position += segmentLength;
        }

        // Ran out of bytes before the size marker — a very large EXIF block, most likely. The caller
        // reports this separately from "not an image" so it is diagnosable rather than mystifying.
        return ImageHeaderStatus.Incomplete;
    }

    // SOF0-SOF15, minus the three markers that share the range but are not frame headers:
    // C4 (define Huffman tables), C8 (JPEG extension), CC (define arithmetic coding conditioning).
    private static bool IsStartOfFrame(byte marker) =>
        marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
}
