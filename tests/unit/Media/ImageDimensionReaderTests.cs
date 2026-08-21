using BlogEngine.Common;

namespace TechieBlog.Tests.Media;

/// <summary>
/// Unit tests for <see cref="ImageDimensionReader"/> — the header parser that supplies
/// <c>BlogImage.Width</c> and <c>BlogImage.Height</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Covers REQ-FN-026. Each test builds the minimal valid header for one
/// container format and asserts the declared size comes back, because the columns were previously
/// NULL on every row and a wrong number would be worse than the NULL it replaces.</para>
/// <para><b>Dependencies:</b> None — the reader is pure and touches no database, disk or network.</para>
/// </remarks>
public class ImageDimensionReaderTests
{
    /// <summary>
    /// A PNG declares its size in the IHDR chunk as two big-endian 32-bit integers, and the reader
    /// returns exactly those numbers.
    /// </summary>
    [Fact]
    public void ReadsPngDimensions()
    {
        var header = new byte[24];
        WriteBytes(header, 0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        WriteBigEndianInt32(header, 16, 640);
        WriteBigEndianInt32(header, 20, 480);

        var read = ImageDimensionReader.TryReadDimensions(header, out var width, out var height);

        Assert.True(read);
        Assert.Equal((640, 480), (width, height));
    }

    /// <summary>
    /// A GIF states its canvas size as little-endian 16-bit values immediately after the six-byte
    /// signature, which is the opposite byte order from PNG and the reason the two are parsed apart.
    /// </summary>
    [Fact]
    public void ReadsGifDimensions()
    {
        var header = new byte[10];
        WriteBytes(header, 0, (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a');
        WriteLittleEndianUInt16(header, 6, 320);
        WriteLittleEndianUInt16(header, 8, 200);

        var read = ImageDimensionReader.TryReadDimensions(header, out var width, out var height);

        Assert.True(read);
        Assert.Equal((320, 200), (width, height));
    }

    /// <summary>
    /// A JPEG hides its size behind a chain of metadata segments, so the reader must walk past an
    /// APP0 block of arbitrary length to reach the start-of-frame marker rather than index a fixed
    /// offset.
    /// </summary>
    [Fact]
    public void ReadsJpegDimensionsBehindMetadataSegment()
    {
        var header = new byte[32];
        WriteBytes(header, 0, 0xFF, 0xD8);          // start of image
        WriteBytes(header, 2, 0xFF, 0xE0);          // APP0 marker
        WriteBigEndianUInt16(header, 4, 10);        // APP0 segment length, so SOF0 starts at 14
        WriteBytes(header, 14, 0xFF, 0xC0);         // SOF0 marker
        WriteBigEndianUInt16(header, 16, 17);       // SOF0 segment length
        WriteBigEndianUInt16(header, 19, 768);      // height
        WriteBigEndianUInt16(header, 21, 1024);     // width

        var read = ImageDimensionReader.TryReadDimensions(header, out var width, out var height);

        Assert.True(read);
        Assert.Equal((1024, 768), (width, height));
    }

    /// <summary>
    /// A lossy WebP keeps its size in 14-bit fields of the VP8 frame tag, so the two high bits of
    /// each 16-bit word are scaling flags that must be masked off rather than read as size.
    /// </summary>
    [Fact]
    public void ReadsLossyWebPDimensions()
    {
        var header = new byte[30];
        WriteBytes(header, 0, (byte)'R', (byte)'I', (byte)'F', (byte)'F');
        WriteBytes(header, 8, (byte)'W', (byte)'E', (byte)'B', (byte)'P');
        WriteBytes(header, 12, (byte)'V', (byte)'P', (byte)'8', (byte)' ');
        WriteBytes(header, 23, 0x9D, 0x01, 0x2A);
        WriteLittleEndianUInt16(header, 26, 0xC000 | 300);   // scaling bits set on purpose
        WriteLittleEndianUInt16(header, 28, 0xC000 | 150);

        var read = ImageDimensionReader.TryReadDimensions(header, out var width, out var height);

        Assert.True(read);
        Assert.Equal((300, 150), (width, height));
    }

    /// <summary>
    /// An extended WebP states its canvas as two 24-bit "minus one" values, so the reader must add
    /// the one back rather than report a picture a pixel short in each direction.
    /// </summary>
    [Fact]
    public void ReadsExtendedWebPDimensions()
    {
        var header = new byte[30];
        WriteBytes(header, 0, (byte)'R', (byte)'I', (byte)'F', (byte)'F');
        WriteBytes(header, 8, (byte)'W', (byte)'E', (byte)'B', (byte)'P');
        WriteBytes(header, 12, (byte)'V', (byte)'P', (byte)'8', (byte)'X');
        WriteLittleEndianUInt24(header, 24, 1919);
        WriteLittleEndianUInt24(header, 27, 1079);

        var read = ImageDimensionReader.TryReadDimensions(header, out var width, out var height);

        Assert.True(read);
        Assert.Equal((1920, 1080), (width, height));
    }

    /// <summary>
    /// A BMP stores a negative height to mean "rows are top-down", and the reader reports the
    /// magnitude so a top-down bitmap is not recorded with a negative dimension.
    /// </summary>
    [Fact]
    public void ReadsTopDownBmpDimensions()
    {
        var header = new byte[26];
        WriteBytes(header, 0, (byte)'B', (byte)'M');
        WriteLittleEndianInt32(header, 18, 800);
        WriteLittleEndianInt32(header, 22, -600);

        var read = ImageDimensionReader.TryReadDimensions(header, out var width, out var height);

        Assert.True(read);
        Assert.Equal((800, 600), (width, height));
    }

    /// <summary>
    /// A format with no readable header — an SVG or a PDF, both of which the media library accepts —
    /// is reported as unreadable rather than as a zero-sized image, so the caller stores NULL.
    /// </summary>
    [Fact]
    public void ReportsUnreadableForVectorContent()
    {
        var content = "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'></svg>"u8.ToArray();

        var read = ImageDimensionReader.TryReadDimensions(content, out var width, out var height);

        Assert.False(read);
        Assert.Equal((0, 0), (width, height));
    }

    /// <summary>
    /// A PNG signature followed by a truncated IHDR yields zeros, which the reader rejects rather
    /// than storing as the image's real size.
    /// </summary>
    [Fact]
    public void RejectsTruncatedPngHeader()
    {
        var header = new byte[24];
        WriteBytes(header, 0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);

        var read = ImageDimensionReader.TryReadDimensions(header, out _, out _);

        Assert.False(read);
    }

    /// <summary>
    /// A buffer too short to hold any known signature is rejected without an index-out-of-range.
    /// </summary>
    [Fact]
    public void RejectsEmptyBuffer()
    {
        var read = ImageDimensionReader.TryReadDimensions(Array.Empty<byte>(), out _, out _);

        Assert.False(read);
    }

    /// <summary>Copies literal bytes into a header buffer at a given offset.</summary>
    /// <param name="buffer">The buffer being built.</param>
    /// <param name="offset">Where the first byte lands.</param>
    /// <param name="values">The bytes to copy.</param>
    private static void WriteBytes(byte[] buffer, int offset, params byte[] values)
    {
        values.CopyTo(buffer, offset);
    }

    /// <summary>Writes a big-endian 32-bit integer.</summary>
    /// <param name="buffer">The buffer being built.</param>
    /// <param name="offset">Index of the most significant byte.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteBigEndianInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    /// <summary>Writes a big-endian 16-bit unsigned integer.</summary>
    /// <param name="buffer">The buffer being built.</param>
    /// <param name="offset">Index of the most significant byte.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteBigEndianUInt16(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    /// <summary>Writes a little-endian 16-bit unsigned integer.</summary>
    /// <param name="buffer">The buffer being built.</param>
    /// <param name="offset">Index of the least significant byte.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteLittleEndianUInt16(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }

    /// <summary>Writes a little-endian 24-bit unsigned integer.</summary>
    /// <param name="buffer">The buffer being built.</param>
    /// <param name="offset">Index of the least significant byte.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteLittleEndianUInt24(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
    }

    /// <summary>Writes a little-endian signed 32-bit integer.</summary>
    /// <param name="buffer">The buffer being built.</param>
    /// <param name="offset">Index of the least significant byte.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteLittleEndianInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }
}
