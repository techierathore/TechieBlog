namespace BlogEngine.Common;

/// <summary>
/// Reads the pixel dimensions of an uploaded image straight from its file header.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>BlogImage.Width</c> and <c>BlogImage.Height</c> exist so a layout can
/// reserve space for an image without fetching it (REQ-FN-026). Filling them needs the dimensions at
/// upload time, and the only cost-free place to get them is the few bytes at the front of the file
/// that every raster format uses to declare its own size.</para>
///
/// <para><b>Code Flow:</b> The image service buffers the upload once, hands the buffer here, and
/// <see cref="TryReadDimensions"/> dispatches on the magic bytes to a per-format reader. Nothing is
/// decoded — no pixel data is touched — so the work is a handful of byte reads regardless of how
/// large the upload is.</para>
///
/// <para><b>Dependencies:</b> None. Deliberately no imaging library: <c>System.Drawing.Common</c> is
/// Windows-only and would break the Linux container, and a full decoder is a large dependency to
/// carry for two integers.</para>
///
/// <para><b>Usage:</b> PNG, GIF, JPEG, BMP and all three WebP chunk layouts are understood. Anything
/// else — SVG, PDF, a truncated upload, a file whose extension lies about its content — returns
/// <c>false</c>, which the caller must treat as "dimensions unknown" and store as NULL. A
/// <c>false</c> is never a reason to reject an upload: the format allow-list has already run.</para>
/// </remarks>
public static class ImageDimensionReader
{
    /// <summary>
    /// Shortest header this reader can draw a conclusion from (GIF: 6-byte signature + 4 size bytes).
    /// </summary>
    private const int MinimumHeaderLength = 10;

    /// <summary>
    /// Attempts to read an image's pixel dimensions from its header bytes.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Format is decided by the magic bytes, never by the file
    /// extension, because the extension is attacker-supplied and the header is the file's own
    /// declaration. An unrecognised or truncated header is an ordinary outcome, not an error.</para>
    /// <para><b>Flow:</b> reject a buffer too short to hold any signature → test each signature in
    /// turn → delegate to that format's reader.</para>
    /// <para><b>Side Effects:</b> None; reads the caller's buffer without modifying it.</para>
    /// </remarks>
    /// <param name="content">The complete uploaded file, or at least its leading bytes.</param>
    /// <param name="width">Receives the pixel width when the read succeeds; zero otherwise.</param>
    /// <param name="height">Receives the pixel height when the read succeeds; zero otherwise.</param>
    /// <returns><c>true</c> when both dimensions were read; <c>false</c> when the format is not
    /// recognised, the header is truncated, or the declared size is not positive.</returns>
    public static bool TryReadDimensions(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (content.Length < MinimumHeaderLength)
        {
            return false;
        }

        if (IsPng(content))
        {
            return TryReadPng(content, out width, out height);
        }

        if (IsGif(content))
        {
            return TryReadGif(content, out width, out height);
        }

        if (IsBmp(content))
        {
            return TryReadBmp(content, out width, out height);
        }

        if (IsWebP(content))
        {
            return TryReadWebP(content, out width, out height);
        }

        return IsJpeg(content) && TryReadJpeg(content, out width, out height);
    }

    /// <summary>
    /// Tests for the eight-byte PNG signature.
    /// </summary>
    /// <param name="content">The file's leading bytes.</param>
    /// <returns><c>true</c> when the buffer starts with the PNG signature.</returns>
    private static bool IsPng(ReadOnlySpan<byte> content)
    {
        return content.Length >= 24
               && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47
               && content[4] == 0x0D && content[5] == 0x0A && content[6] == 0x1A && content[7] == 0x0A;
    }

    /// <summary>
    /// Reads a PNG's dimensions from its IHDR chunk.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> IHDR is required by the specification to be the first chunk, so
    /// width and height always sit at byte 16 and byte 20 as big-endian 32-bit integers.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="content">The file's leading bytes.</param>
    /// <param name="width">Receives the pixel width.</param>
    /// <param name="height">Receives the pixel height.</param>
    /// <returns><c>true</c> when both values are positive.</returns>
    private static bool TryReadPng(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = ReadBigEndianInt32(content, 16);
        height = ReadBigEndianInt32(content, 20);
        return AreValid(width, height);
    }

    /// <summary>
    /// Tests for a GIF87a or GIF89a signature.
    /// </summary>
    /// <param name="content">The file's leading bytes.</param>
    /// <returns><c>true</c> when the buffer starts with a GIF signature.</returns>
    private static bool IsGif(ReadOnlySpan<byte> content)
    {
        return content.Length >= 10
               && content[0] == (byte)'G' && content[1] == (byte)'I' && content[2] == (byte)'F';
    }

    /// <summary>
    /// Reads a GIF's logical screen dimensions.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The logical screen descriptor follows the six-byte signature and
    /// opens with the canvas width and height as little-endian 16-bit values.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="content">The file's leading bytes.</param>
    /// <param name="width">Receives the pixel width.</param>
    /// <param name="height">Receives the pixel height.</param>
    /// <returns><c>true</c> when both values are positive.</returns>
    private static bool TryReadGif(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = ReadLittleEndianUInt16(content, 6);
        height = ReadLittleEndianUInt16(content, 8);
        return AreValid(width, height);
    }

    /// <summary>
    /// Tests for the two-byte BMP signature.
    /// </summary>
    /// <param name="content">The file's leading bytes.</param>
    /// <returns><c>true</c> when the buffer starts with a BMP signature.</returns>
    private static bool IsBmp(ReadOnlySpan<byte> content)
    {
        return content.Length >= 26 && content[0] == (byte)'B' && content[1] == (byte)'M';
    }

    /// <summary>
    /// Reads a BMP's dimensions from its DIB header.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Height is signed — a negative value means the rows are stored
    /// top-down — so the magnitude is what describes the image.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="content">The file's leading bytes.</param>
    /// <param name="width">Receives the pixel width.</param>
    /// <param name="height">Receives the pixel height.</param>
    /// <returns><c>true</c> when both values are positive.</returns>
    private static bool TryReadBmp(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = Math.Abs(ReadLittleEndianInt32(content, 18));
        height = Math.Abs(ReadLittleEndianInt32(content, 22));
        return AreValid(width, height);
    }

    /// <summary>
    /// Tests for the RIFF/WEBP container signature.
    /// </summary>
    /// <param name="content">The file's leading bytes.</param>
    /// <returns><c>true</c> when the buffer is a RIFF container whose form type is WEBP.</returns>
    private static bool IsWebP(ReadOnlySpan<byte> content)
    {
        return content.Length >= 30
               && content[0] == (byte)'R' && content[1] == (byte)'I' && content[2] == (byte)'F' && content[3] == (byte)'F'
               && content[8] == (byte)'W' && content[9] == (byte)'E' && content[10] == (byte)'B' && content[11] == (byte)'P';
    }

    /// <summary>
    /// Reads a WebP's canvas dimensions from whichever of its three chunk layouts is present.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> WebP has three incompatible headers behind one container:
    /// <c>VP8 </c> (lossy) hides the size in a 14-bit field of the VP8 frame tag, <c>VP8L</c>
    /// (lossless) packs width-1 and height-1 into 28 bits, and <c>VP8X</c> (extended, used for
    /// animation and alpha) states the canvas as two 24-bit values. All three are read here because a
    /// browser will happily produce any of them.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="content">The file's leading bytes.</param>
    /// <param name="width">Receives the pixel width.</param>
    /// <param name="height">Receives the pixel height.</param>
    /// <returns><c>true</c> when both values are positive.</returns>
    private static bool TryReadWebP(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;
        var chunk = content.Slice(12, 4);

        if (chunk[3] == (byte)'X' && content.Length >= 30)
        {
            width = ReadLittleEndianUInt24(content, 24) + 1;
            height = ReadLittleEndianUInt24(content, 27) + 1;
        }
        else if (chunk[3] == (byte)'L' && content.Length >= 25)
        {
            // Byte 20 is the VP8L signature (0x2F); the 28 size bits start at byte 21, LSB first.
            width = (content[21] | (content[22] & 0x3F) << 8) + 1;
            height = ((content[22] & 0xC0) >> 6 | content[23] << 2 | (content[24] & 0x0F) << 10) + 1;
        }
        else if (chunk[3] == (byte)' ' && content.Length >= 30)
        {
            width = ReadLittleEndianUInt16(content, 26) & 0x3FFF;
            height = ReadLittleEndianUInt16(content, 28) & 0x3FFF;
        }

        return AreValid(width, height);
    }

    /// <summary>
    /// Tests for the two-byte JPEG start-of-image marker.
    /// </summary>
    /// <param name="content">The file's leading bytes.</param>
    /// <returns><c>true</c> when the buffer starts with <c>FFD8</c>.</returns>
    private static bool IsJpeg(ReadOnlySpan<byte> content)
    {
        return content.Length >= 4 && content[0] == 0xFF && content[1] == 0xD8;
    }

    /// <summary>
    /// Walks a JPEG's marker segments to the start-of-frame that declares the image size.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> JPEG states its dimensions only inside a start-of-frame marker,
    /// whose position depends on how many metadata segments (EXIF, ICC, comments) precede it — so
    /// the segment chain has to be walked rather than indexed. The SOF family spans C0-CF minus C4,
    /// C8 and CC, which are Huffman/arithmetic tables and extensions, not frames.</para>
    /// <para><b>Flow:</b> skip to the first marker → for each segment, either read the frame header
    /// or jump over the segment by its declared length → stop at the end of the buffer.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="content">The file's leading bytes.</param>
    /// <param name="width">Receives the pixel width.</param>
    /// <param name="height">Receives the pixel height.</param>
    /// <returns><c>true</c> when a start-of-frame was found and both values are positive.</returns>
    private static bool TryReadJpeg(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;
        var position = 2;

        while (position + 9 < content.Length)
        {
            if (content[position] != 0xFF)
            {
                position++;
                continue;
            }

            var marker = content[position + 1];
            if (IsStartOfFrame(marker))
            {
                height = ReadBigEndianUInt16(content, position + 5);
                width = ReadBigEndianUInt16(content, position + 7);
                return AreValid(width, height);
            }

            position += 2 + ReadBigEndianUInt16(content, position + 2);
        }

        return false;
    }

    /// <summary>
    /// Decides whether a JPEG marker introduces a start-of-frame segment.
    /// </summary>
    /// <param name="marker">The byte following an <c>FF</c> marker prefix.</param>
    /// <returns><c>true</c> for SOF0-SOF15, excluding the table and extension markers.</returns>
    private static bool IsStartOfFrame(byte marker)
    {
        return marker >= 0xC0 && marker <= 0xCF
               && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
    }

    /// <summary>
    /// Rejects zero, negative and implausibly large dimensions.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A corrupt or misidentified header produces arbitrary numbers, and
    /// storing one would be worse than storing NULL — the caller can tell "unknown" from "wrong" only
    /// if nonsense is filtered here. The ceiling is far above any real upload the size limits allow.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="width">Candidate width.</param>
    /// <param name="height">Candidate height.</param>
    /// <returns><c>true</c> when both values are plausible pixel counts.</returns>
    private static bool AreValid(int width, int height)
    {
        const int MaximumPlausibleDimension = 100000;
        return width > 0 && height > 0
               && width <= MaximumPlausibleDimension && height <= MaximumPlausibleDimension;
    }

    /// <summary>
    /// Reads a big-endian 32-bit integer.
    /// </summary>
    /// <param name="content">The buffer to read from.</param>
    /// <param name="offset">Index of the most significant byte.</param>
    /// <returns>The decoded value, or zero when the buffer is too short.</returns>
    private static int ReadBigEndianInt32(ReadOnlySpan<byte> content, int offset)
    {
        if (offset + 4 > content.Length)
        {
            return 0;
        }

        return content[offset] << 24 | content[offset + 1] << 16
               | content[offset + 2] << 8 | content[offset + 3];
    }

    /// <summary>
    /// Reads a big-endian 16-bit unsigned integer.
    /// </summary>
    /// <param name="content">The buffer to read from.</param>
    /// <param name="offset">Index of the most significant byte.</param>
    /// <returns>The decoded value, or zero when the buffer is too short.</returns>
    private static int ReadBigEndianUInt16(ReadOnlySpan<byte> content, int offset)
    {
        return offset + 2 > content.Length ? 0 : content[offset] << 8 | content[offset + 1];
    }

    /// <summary>
    /// Reads a little-endian 16-bit unsigned integer.
    /// </summary>
    /// <param name="content">The buffer to read from.</param>
    /// <param name="offset">Index of the least significant byte.</param>
    /// <returns>The decoded value, or zero when the buffer is too short.</returns>
    private static int ReadLittleEndianUInt16(ReadOnlySpan<byte> content, int offset)
    {
        return offset + 2 > content.Length ? 0 : content[offset] | content[offset + 1] << 8;
    }

    /// <summary>
    /// Reads a little-endian 24-bit unsigned integer, the width unit of the WebP VP8X chunk.
    /// </summary>
    /// <param name="content">The buffer to read from.</param>
    /// <param name="offset">Index of the least significant byte.</param>
    /// <returns>The decoded value, or zero when the buffer is too short.</returns>
    private static int ReadLittleEndianUInt24(ReadOnlySpan<byte> content, int offset)
    {
        if (offset + 3 > content.Length)
        {
            return 0;
        }

        return content[offset] | content[offset + 1] << 8 | content[offset + 2] << 16;
    }

    /// <summary>
    /// Reads a little-endian signed 32-bit integer.
    /// </summary>
    /// <param name="content">The buffer to read from.</param>
    /// <param name="offset">Index of the least significant byte.</param>
    /// <returns>The decoded value, or zero when the buffer is too short.</returns>
    private static int ReadLittleEndianInt32(ReadOnlySpan<byte> content, int offset)
    {
        if (offset + 4 > content.Length)
        {
            return 0;
        }

        return content[offset] | content[offset + 1] << 8
               | content[offset + 2] << 16 | content[offset + 3] << 24;
    }
}
