using System.Buffers.Binary;

namespace Api.Tests.Media;

/// <summary>
/// Synthetic files with real headers, for the §7.2 checks.
///
/// Built byte by byte rather than checked in as fixtures for two reasons: a test can ask for the
/// exact dimensions or size its boundary needs, and a reviewer can see from here that a "1024×768
/// JPEG" really does say 1024×768 rather than trusting a binary blob in the repo.
///
/// All content is obviously synthetic — Appendix A's grep rule applies to test data too.
/// </summary>
internal static class TestImages
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>A PNG whose IHDR declares the given dimensions, padded to <paramref name="padTo"/> bytes.</summary>
    public static byte[] Png(int width, int height, int padTo = 0)
    {
        var bytes = new List<byte>(PngSignature);

        bytes.AddRange([0x00, 0x00, 0x00, 0x0D]);   // IHDR chunk length
        bytes.AddRange("IHDR"u8);
        bytes.AddRange(BigEndian(width));
        bytes.AddRange(BigEndian(height));
        bytes.AddRange([0x08, 0x02, 0x00, 0x00, 0x00]);   // 8-bit truecolour, no interlace
        bytes.AddRange([0x00, 0x00, 0x00, 0x00]);   // CRC — nothing here validates it

        return Pad(bytes, padTo);
    }

    /// <summary>
    /// A JPEG whose frame header declares the given dimensions.
    /// </summary>
    /// <param name="startOfFrameMarker">0xC0 baseline by default; 0xC2 is progressive.</param>
    /// <param name="exifBytes">
    /// Size of an APP1 segment placed *before* the frame header — how a phone photo pushes its
    /// dimensions past the first few hundred bytes.
    /// </param>
    public static byte[] Jpeg(int width, int height, byte startOfFrameMarker = 0xC0, int exifBytes = 0, int padTo = 0)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        if (exifBytes > 0)
        {
            bytes.AddRange([0xFF, 0xE1]);
            bytes.AddRange(BigEndian16(exifBytes + 2));
            bytes.AddRange(Enumerable.Repeat((byte)0x20, exifBytes));
        }

        bytes.AddRange([0xFF, startOfFrameMarker]);
        bytes.AddRange(BigEndian16(17));            // segment length
        bytes.Add(0x08);                            // sample precision
        bytes.AddRange(BigEndian16(height));
        bytes.AddRange(BigEndian16(width));
        bytes.Add(0x03);                            // three components
        bytes.AddRange([0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);

        bytes.AddRange([0xFF, 0xDA]);               // start of scan
        bytes.AddRange(BigEndian16(12));
        bytes.AddRange(Enumerable.Repeat((byte)0x00, 10));
        bytes.AddRange([0x00, 0x11, 0x22, 0x33]);   // "scan data"
        bytes.AddRange([0xFF, 0xD9]);               // EOI

        return Pad(bytes, padTo);
    }

    public static byte[] Pdf(int padTo = 0)
    {
        var bytes = new List<byte>("%PDF-1.7\nPLACEHOLDER test document\n"u8.ToArray());
        return Pad(bytes, padTo);
    }

    private static byte[] Pad(List<byte> bytes, int padTo)
    {
        while (bytes.Count < padTo)
        {
            bytes.Add(0x20);
        }

        return [.. bytes];
    }

    private static byte[] BigEndian(int value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] BigEndian16(int value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
        return buffer;
    }
}
