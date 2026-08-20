using System.Buffers.Binary;
using System.Text;

namespace Api.Tests.Media;

/// <summary>
/// Synthetic audio files with real container headers, in the <see cref="TestImages"/> style —
/// built byte by byte so a reviewer can see from here that a "WebM" really does open with EBML.
///
/// Headers only: nothing in this application decodes an audio stream (see <c>AudioHeader</c> on why
/// container-level is the honest limit while #10 is unanswered), so a real recording would prove
/// nothing these bytes do not. The real thing is exercised in the browser pass.
/// </summary>
internal static class TestAudio
{
    /// <summary>Chrome's <c>MediaRecorder</c> output: an EBML header element.</summary>
    public static byte[] Webm(int padTo = 0)
    {
        var bytes = new List<byte> { 0x1A, 0x45, 0xDF, 0xA3 };   // EBML element id
        bytes.AddRange([0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F]);   // element size
        bytes.AddRange("PLACEHOLDER-webm"u8);
        return Pad(bytes, padTo);
    }

    /// <summary>Safari's <c>MediaRecorder</c> output: an ISO base-media <c>ftyp</c> box.</summary>
    public static byte[] Mp4(int padTo = 0, string brand = "M4A ")
    {
        var bytes = new List<byte>();
        bytes.AddRange(BigEndian(24));              // box size
        bytes.AddRange("ftyp"u8);
        bytes.AddRange(Encoding.ASCII.GetBytes(brand));
        bytes.AddRange(BigEndian(0));               // minor version
        bytes.AddRange("mp42"u8);                   // one compatible brand
        return Pad(bytes, padTo);
    }

    public static byte[] Ogg(int padTo = 0)
    {
        var bytes = new List<byte>("OggS"u8.ToArray());
        bytes.Add(0x00);                            // stream structure version
        bytes.Add(0x02);                            // header type: beginning of stream
        bytes.AddRange(Enumerable.Repeat((byte)0x00, 8));   // granule position
        bytes.AddRange("PLACEHOLDER-ogg"u8);
        return Pad(bytes, padTo);
    }

    /// <summary>An <c>ftyp</c> box that is not the first one — not a file this app accepts.</summary>
    public static byte[] Mp4WithFtypOutOfPlace()
    {
        var bytes = new List<byte>();
        bytes.AddRange(BigEndian(16));
        bytes.AddRange("free"u8);                   // some other box first
        bytes.AddRange(BigEndian(0));
        bytes.AddRange(BigEndian(24));
        bytes.AddRange("ftyp"u8);
        bytes.AddRange("M4A "u8);
        return [.. bytes];
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
}
