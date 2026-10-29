#:package SkiaSharp@3.119.0
#:package SkiaSharp.NativeAssets.Linux.NoDependencies@3.119.0

// REQ-NFR-043 (BRD-103, ADR-017): renders favicon.ico (32 px), icon-192.png and icon-512.png
// from the approved brand tile (docs/brand/TechieBlog.png). Never hand-made; run it again and
// the files are byte-identical.
//
//   dotnet run scripts/render-brand-icons.cs
//
// SkiaSharp has no ICO encoder, so the .ico container is written here: a 6-byte ICONDIR plus one
// 16-byte ICONDIRENTRY pointing at a PNG payload (valid since Windows Vista and in every browser).

using SkiaSharp;

string root = FindRepoRoot();
string tilePath = Path.Combine(root, "docs", "brand", "TechieBlog.png");
string outDir = Path.Combine(root, "source", "BlogUI", "wwwroot", "images", "brand");
Directory.CreateDirectory(outDir);

using SKBitmap tile = SKBitmap.Decode(tilePath)
    ?? throw new InvalidOperationException($"Cannot decode {tilePath}");

byte[] png32 = Render(tile, 32);
File.WriteAllBytes(Path.Combine(outDir, "icon-192.png"), Render(tile, 192));
File.WriteAllBytes(Path.Combine(outDir, "icon-512.png"), Render(tile, 512));
File.WriteAllBytes(Path.Combine(outDir, "favicon.ico"), WrapAsIco(png32, 32));
Console.WriteLine($"Rendered favicon.ico, icon-192.png, icon-512.png into {outDir}");

static byte[] Render(SKBitmap source, int size)
{
    SKImageInfo info = new(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using SKBitmap scaled = source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell))
        ?? throw new InvalidOperationException($"Resize to {size} failed");
    using SKImage image = SKImage.FromBitmap(scaled);
    using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

static byte[] WrapAsIco(byte[] png, int size)
{
    using MemoryStream ms = new();
    using BinaryWriter w = new(ms);
    w.Write((ushort)0);          // reserved
    w.Write((ushort)1);          // type: icon
    w.Write((ushort)1);          // image count
    w.Write((byte)size);         // width
    w.Write((byte)size);         // height
    w.Write((byte)0);            // palette colours
    w.Write((byte)0);            // reserved
    w.Write((ushort)1);          // colour planes
    w.Write((ushort)32);         // bits per pixel
    w.Write((uint)png.Length);   // payload size
    w.Write((uint)22);           // payload offset (6 + 16)
    w.Write(png);
    return ms.ToArray();
}

static string FindRepoRoot()
{
    for (DirectoryInfo? d = new(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
    {
        if (File.Exists(Path.Combine(d.FullName, "TechieBlog.slnx"))) return d.FullName;
    }
    throw new InvalidOperationException("Run from inside the repository (TechieBlog.slnx not found).");
}
