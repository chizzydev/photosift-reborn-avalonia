using ImageMagick;

namespace PhotoSiftReborn;

/// <summary>
/// Isolated raster-only *preview* decoder. Original photos are never changed.
/// Q8 native pixel-cache caps are NOT caps on total process RSS; measure on target OS.
/// A filename extension is never trusted to grant access to delegate formats.
/// </summary>
internal static class MagickPreviewDecoder
{
    public const int PreviewLongEdge = 2048;
    public const ulong MaximumPixels = 64_000_000;
    public const int MaximumPngBytes = 48 * 1024 * 1024;
    public const int MaximumInspectionPngBytes = 128 * 1024 * 1024;

    // Process-global ImageMagick limits: set before any image is read.
    // No OpenMP, one managed worker; threads also clamped in native layer.
    static MagickPreviewDecoder()
    {
        ResourceLimits.Memory = 256UL * 1024 * 1024;
        ResourceLimits.Disk = 512UL * 1024 * 1024;
        ResourceLimits.Area = 24_000_000;
        ResourceLimits.Width = 16_000;
        ResourceLimits.Height = 16_000;
        ResourceLimits.Thread = 1;
    }

    internal static byte[] DecodeToPng(string path, bool originalPixels = false)
    {
        // An allowed extension PLUS header signature: no PDF, PS, EPS, external
        // delegates, URL inputs, or disguised files in this workflow.
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".jpeg" or ".jpg" or ".png" or ".bmp" or ".gif"))
            throw new NotSupportedException("Only ordinary JPEG, PNG, BMP and GIF previews are enabled.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[12];
        int read = stream.Read(header);
        if (!HasExpectedSignature(extension, header[..read]))
            throw new InvalidDataException("Image header does not match its supported extension.");
        stream.Position = 0;

        // Header metadata check BEFORE allocating the full native pixel cache.
        using (var probe = new MagickImage())
        {
            probe.Ping(stream);
            CheckGeometry(probe.Width, probe.Height);
        }
        stream.Position = 0;
        using var img = new MagickImage(stream);
        CheckGeometry(img.Width, img.Height);
        img.AutoOrient(); // EXIF orientation; all eight standard orientations.
        CheckGeometry(img.Width, img.Height);
        var longest = Math.Max(img.Width, img.Height);
        if (!originalPixels && longest > PreviewLongEdge)
        {
            // 2048 x 2048 bounding box, preserve aspect ratio.
            img.Resize(new MagickGeometry(PreviewLongEdge, PreviewLongEdge));
        }
        img.Strip(); // Preview need not retain EXIF/GPS; source bytes unchanged.
        img.Format = MagickFormat.Png;
        var encoded = img.ToByteArray();
        if (encoded.Length > (originalPixels ? MaximumInspectionPngBytes : MaximumPngBytes))
            throw new InvalidDataException("Encoded image exceeds the bounded display memory limit. Use Fit preview.");
        return encoded;
    }

    private static void CheckGeometry(uint width, uint height)
    {
        if (width == 0 || height == 0 || (ulong)width * height > MaximumPixels)
            throw new InvalidDataException("Image dimensions exceed the preview's 64-megapixel limit.");
    }

    private static bool HasExpectedSignature(string extension, ReadOnlySpan<byte> h)
    {
        if (extension is ".jpg" or ".jpeg")
            return h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF;
        if (extension == ".png")
            return h.Length >= 8 && h[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10});
        if (extension == ".bmp")
            return h.Length >= 2 && h[0] == 0x42 && h[1] == 0x4D;
        if (extension == ".gif")
            return h.Length >= 6 && (h[..6].SequenceEqual("GIF87a"u8) || h[..6].SequenceEqual("GIF89a"u8));
        return false;
    }
}
