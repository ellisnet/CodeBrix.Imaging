using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Common.Helpers;
using CodeBrix.Imaging.Metadata;
using CodeBrix.Imaging.PixelFormats;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;

// ReSharper disable SuggestVarOrType_BuiltInTypes
// ReSharper disable InconsistentNaming

namespace CodeBrix.Imaging.Helpers;

/// <summary>
/// Specifies how pixel colors are mapped to palette indices when saving an image
/// as an 8-bit-per-pixel indexed BMP file.
/// </summary>
public enum BmpIndexingMode
{
    /// <summary>
    /// Uses a standard 256-entry linear grayscale palette where each palette index
    /// directly corresponds to the computed grayscale value (index 0 = black, 255 = white).
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Produces output that matches the BMP format written by System.Drawing (GDI+) when
    /// converting a grayscale image to <c>PixelFormat.Format8bppIndexed</c>. Uses the GDI+
    /// halftone palette (224 entries: a 6×6×6 color cube plus 8 system colors) and maps
    /// grayscale values to palette indices using the same empirically-determined quantization
    /// ranges that GDI+ uses internally.
    /// </summary>
    SystemDrawingCompatible = 1
}

public static class BmpFormatHelper
{
    #region SystemDrawingCompatible mode support

    /// <summary>
    /// Number of palette entries in the GDI+ halftone palette: 216 (6×6×6 color cube) + 8 system colors.
    /// </summary>
    private const int HalftonePaletteEntryCount = 224;

    /// <summary>
    /// The GDI+ halftone palette used by System.Drawing when converting to Format8bppIndexed.
    /// This is a 6×6×6 color cube (216 entries) plus 8 additional system colors,
    /// for a total of 224 entries in BGRA format with the reserved byte set to 0xFF.
    /// </summary>
    private static readonly byte[] HalftonePalette = BuildHalftonePalette();

    /// <summary>
    /// Lookup table mapping each grayscale value (0-255) to the palette index that GDI+
    /// assigns when converting a grayscale image to Format8bppIndexed with the halftone palette.
    /// These ranges were determined empirically by creating a 256-pixel grayscale gradient in
    /// System.Drawing and observing the resulting palette indices after Clone to Format8bppIndexed.
    /// Note: GDI+ does NOT use simple Euclidean distance for this mapping.
    /// </summary>
    private static readonly byte[] GrayscaleToHalftoneIndex = BuildGrayscaleToHalftoneIndexLookup();

    private static byte[] BuildHalftonePalette()
    {
        var palette = new byte[HalftonePaletteEntryCount * 4];
        ReadOnlySpan<byte> cubeValues = [0, 51, 102, 153, 204, 255];

        // 6×6×6 color cube (216 entries)
        int offset = 0;
        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    palette[offset] = cubeValues[b];     // Blue
                    palette[offset + 1] = cubeValues[g]; // Green
                    palette[offset + 2] = cubeValues[r]; // Red
                    palette[offset + 3] = 0xFF;          // Reserved
                    offset += 4;
                }
            }
        }

        // 8 additional system colors (R, G, B)
        ReadOnlySpan<byte> systemColors =
        [
            192, 192, 192, // Silver
            128, 128, 128, // Gray
            128, 0, 0,     // Dark Red
            0, 128, 0,     // Dark Green
            0, 0, 128,     // Dark Blue
            128, 128, 0,   // Dark Yellow
            128, 0, 128,   // Dark Magenta
            0, 128, 128    // Dark Cyan
        ];

        for (int i = 0; i < 8; i++)
        {
            int si = i * 3;
            palette[offset] = systemColors[si + 2]; // Blue
            palette[offset + 1] = systemColors[si + 1]; // Green
            palette[offset + 2] = systemColors[si]; // Red
            palette[offset + 3] = 0xFF;             // Reserved
            offset += 4;
        }

        return palette;
    }

    private static byte[] BuildGrayscaleToHalftoneIndexLookup()
    {
        // GDI+ halftone palette gray-level ranges determined empirically:
        //   gray   0- 31 -> palette index   0  (R=G=B=  0, cube entry)
        //   gray  32- 79 -> palette index  43  (R=G=B= 51, cube entry)
        //   gray  80-111 -> palette index  86  (R=G=B=102, cube entry)
        //   gray 112-143 -> palette index 217  (R=G=B=128, system color)
        //   gray 144-175 -> palette index 129  (R=G=B=153, cube entry)
        //   gray 176-191 -> palette index 216  (R=G=B=192, system color)
        //   gray 192-223 -> palette index 172  (R=G=B=204, cube entry)
        //   gray 224-255 -> palette index 215  (R=G=B=255, cube entry)
        var lookup = new byte[256];

        for (int gray = 0; gray < 256; gray++)
        {
            lookup[gray] = gray switch
            {
                <= 31 => 0,
                <= 79 => 43,
                <= 111 => 86,
                <= 143 => 217,
                <= 175 => 129,
                <= 191 => 216,
                <= 223 => 172,
                _ => 215
            };
        }

        return lookup;
    }

    #endregion

    private static void Write8bppBmp(Image image, Stream stream, ColorMatrix colorMatrix, BmpIndexingMode indexingMode)
    {
        bool compatible = indexingMode == BmpIndexingMode.SystemDrawingCompatible;

        int width = image.Width;
        int height = image.Height;

        // Calculate row stride - each row is padded to a 4-byte boundary
        int rowStride = (width + 3) & ~3;
        int pixelDataSize = rowStride * height;

        // BMP structure sizes
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40; // BITMAPINFOHEADER (V3)
        int paletteEntryCount = compatible ? HalftonePaletteEntryCount : 256;
        int colorPaletteSize = paletteEntryCount * 4;
        int pixelDataOffset = fileHeaderSize + infoHeaderSize + colorPaletteSize;
        int fileSize = pixelDataOffset + pixelDataSize;

        // Get resolution from image metadata (BMP uses pixels per meter)
        int hResolution = 0;
        int vResolution = 0;
        ImageMetadata metadata = image.Metadata;
        if (metadata.ResolutionUnits != PixelResolutionUnit.AspectRatio
            && metadata.HorizontalResolution > 0
            && metadata.VerticalResolution > 0)
        {
            switch (metadata.ResolutionUnits)
            {
                case PixelResolutionUnit.PixelsPerInch:
                    hResolution = (int)Math.Round(UnitConverter.InchToMeter(metadata.HorizontalResolution));
                    vResolution = (int)Math.Round(UnitConverter.InchToMeter(metadata.VerticalResolution));
                    break;
                case PixelResolutionUnit.PixelsPerCentimeter:
                    hResolution = (int)Math.Round(UnitConverter.CmToMeter(metadata.HorizontalResolution));
                    vResolution = (int)Math.Round(UnitConverter.CmToMeter(metadata.VerticalResolution));
                    break;
                case PixelResolutionUnit.PixelsPerMeter:
                    hResolution = (int)Math.Round(metadata.HorizontalResolution);
                    vResolution = (int)Math.Round(metadata.VerticalResolution);
                    break;
            }
        }

        // Write BMP File Header (14 bytes) + Info Header V3 (40 bytes)
        Span<byte> header = stackalloc byte[fileHeaderSize + infoHeaderSize];
        header.Clear();

        // File Header
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header[2..], fileSize);
        // bytes 6-9: reserved (already 0)
        BinaryPrimitives.WriteInt32LittleEndian(header[10..], pixelDataOffset);

        // Info Header V3 (BITMAPINFOHEADER)
        BinaryPrimitives.WriteInt32LittleEndian(header[14..], infoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[18..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[22..], height); // positive = bottom-up row order
        BinaryPrimitives.WriteInt16LittleEndian(header[26..], 1);     // planes
        BinaryPrimitives.WriteInt16LittleEndian(header[28..], 8);     // bits per pixel
        // bytes 30-33: compression = BI_RGB (already 0)
        BinaryPrimitives.WriteInt32LittleEndian(header[38..], hResolution);
        BinaryPrimitives.WriteInt32LittleEndian(header[42..], vResolution);

        if (compatible)
        {
            // System.Drawing writes imageDataSize=0 for BI_RGB, and sets both
            // colorsUsed and colorsImportant to the halftone palette entry count.
            // bytes 34-37: imageDataSize = 0 (already 0)
            BinaryPrimitives.WriteInt32LittleEndian(header[46..], paletteEntryCount);
            BinaryPrimitives.WriteInt32LittleEndian(header[50..], paletteEntryCount);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(header[34..], pixelDataSize);
            BinaryPrimitives.WriteInt32LittleEndian(header[46..], 256);
            // bytes 50-53: important colors = 0 (already 0)
        }

        stream.Write(header);

        // Write color palette
        if (compatible)
        {
            stream.Write(HalftonePalette, 0, colorPaletteSize);
        }
        else
        {
            Span<byte> palette = stackalloc byte[colorPaletteSize];
            for (int i = 0; i < 256; i++)
            {
                int idx = i * 4;
                palette[idx] = (byte)i;     // Blue
                palette[idx + 1] = (byte)i; // Green
                palette[idx + 2] = (byte)i; // Red
                palette[idx + 3] = 0;       // Reserved
            }

            stream.Write(palette);
        }

        // Extract color matrix weights for computing the grayscale value.
        // The color matrix is applied as:
        //   gray = R * M11 + G * M21 + B * M31 + A * M41 + M51 * 255
        // where R, G, B, A are byte values (0-255) and the result is clamped to [0, 255].
        float rWeight = colorMatrix.M11;
        float gWeight = colorMatrix.M21;
        float bWeight = colorMatrix.M31;
        float aWeight = colorMatrix.M41;
        float translation = colorMatrix.M51 * 255f;

        // Write pixel data (bottom-up row order, as required by BMP format)
        using var rgba32Image = image.CloneAs<Rgba32>();
        byte[] rowBuffer = new byte[rowStride]; // padding bytes are initialized to 0

        for (int y = height - 1; y >= 0; y--)
        {
            Memory<Rgba32> rowMemory = rgba32Image.DangerousGetPixelRowMemory(y);
            Span<Rgba32> pixelRow = rowMemory.Span;

            for (int x = 0; x < width; x++)
            {
                ref Rgba32 pixel = ref pixelRow[x];
                float gray = (pixel.R * rWeight) + (pixel.G * gWeight) + (pixel.B * bWeight)
                             + (pixel.A * aWeight) + translation;

                byte grayByte = (byte)Math.Clamp((int)Math.Round(gray), 0, 255);

                // In Normal mode, the gray value IS the palette index (linear grayscale palette).
                // In SystemDrawingCompatible mode, map through the halftone lookup table.
                rowBuffer[x] = compatible ? GrayscaleToHalftoneIndex[grayByte] : grayByte;
            }

            // Clear any padding bytes beyond the image width
            for (int p = width; p < rowStride; p++)
            {
                rowBuffer[p] = 0;
            }

            stream.Write(rowBuffer, 0, rowStride);
        }

        stream.Flush();
    }

    /// <summary>
    /// The default grayscale color matrix using luminance weights matching those used by
    /// System.Drawing.Imaging.ColorMatrix for grayscale conversion: R=0.3, G=0.59, B=0.11.
    /// </summary>
    public static readonly ColorMatrix DefaultGrayscaleColorMatrix = new(
        .3f, .3f, .3f, 0f,
        .59f, .59f, .59f, 0f,
        .11f, .11f, .11f, 0f,
        0f, 0f, 0f, 1f,
        0f, 0f, 0f, 0f);

    /// <summary>
    /// Exports the image as an 8-bit-per-pixel grayscale BMP file to the specified stream,
    /// applying the given color matrix to determine each pixel's palette index.
    /// In <see cref="BmpIndexingMode.Normal"/> mode, the output uses a linear 256-entry
    /// grayscale palette (index 0 = black through 255 = white).
    /// In <see cref="BmpIndexingMode.SystemDrawingCompatible"/> mode, the output uses
    /// the GDI+ halftone palette and quantization to match System.Drawing behavior.
    /// <para>
    /// Unlike the built-in <c>Save</c>/<c>SaveAs</c> methods, this method does not update
    /// <see cref="ImageMetadata.ExpectedFormat"/> because the 8bpp grayscale format is a
    /// specialized export that cannot be round-tripped through the library's encoder pipeline.
    /// </para>
    /// </summary>
    /// <param name="image">The image to export.</param>
    /// <param name="stream">The stream to write the BMP data to.</param>
    /// <param name="colorMatrix">The color matrix used to transform pixel colors to a single grayscale value.</param>
    /// <param name="indexingMode">The palette and quantization mode to use. Defaults to <see cref="BmpIndexingMode.Normal"/>.</param>
    public static void ExportAs8bppGrayscaleBmpFormat(this Image image, 
        Stream stream, ColorMatrix colorMatrix, BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);
        if (!Enum.IsDefined(indexingMode))
        {
            throw new ArgumentException($"Unknown member: {indexingMode}", nameof(indexingMode));
        }

        Write8bppBmp(image, stream, colorMatrix, indexingMode);
    }

    /// <summary>
    /// Asynchronously exports the image as an 8-bit-per-pixel grayscale BMP file to the specified stream,
    /// applying the given color matrix to determine each pixel's palette index.
    /// In <see cref="BmpIndexingMode.Normal"/> mode, the output uses a linear 256-entry
    /// grayscale palette (index 0 = black through 255 = white).
    /// In <see cref="BmpIndexingMode.SystemDrawingCompatible"/> mode, the output uses
    /// the GDI+ halftone palette and quantization to match System.Drawing behavior.
    /// <para>
    /// Unlike the built-in <c>Save</c>/<c>SaveAs</c> methods, this method does not update
    /// <see cref="ImageMetadata.ExpectedFormat"/> because the 8bpp grayscale format is a
    /// specialized export that cannot be round-tripped through the library's encoder pipeline.
    /// </para>
    /// </summary>
    /// <param name="image">The image to export.</param>
    /// <param name="stream">The stream to write the BMP data to.</param>
    /// <param name="colorMatrix">The color matrix used to transform pixel colors to a single grayscale value.</param>
    /// <param name="indexingMode">The palette and quantization mode to use. Defaults to <see cref="BmpIndexingMode.Normal"/>.</param>
    public static async Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image, 
        Stream stream, ColorMatrix colorMatrix, BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);
        if (!Enum.IsDefined(indexingMode))
        {
            throw new ArgumentException($"Unknown member: {indexingMode}", nameof(indexingMode));
        }

        using var ms = new MemoryStream();
        Write8bppBmp(image, ms, colorMatrix, indexingMode);
        ms.Position = 0;
        await ms.CopyToAsync(stream);
    }

    /// <summary>
    /// Exports the image as an 8-bit-per-pixel grayscale indexed BMP file to the specified stream,
    /// using the default grayscale color matrix (R=0.3, G=0.59, B=0.11).
    /// In <see cref="BmpIndexingMode.Normal"/> mode, the output uses a linear 256-entry
    /// grayscale palette. In <see cref="BmpIndexingMode.SystemDrawingCompatible"/> mode,
    /// the output uses the GDI+ halftone palette and quantization to match System.Drawing behavior.
    /// <para>
    /// Unlike the built-in <c>Save</c>/<c>SaveAs</c> methods, this method does not update
    /// <see cref="ImageMetadata.ExpectedFormat"/> because the 8bpp indexed format is a
    /// specialized export that cannot be round-tripped through the library's encoder pipeline.
    /// </para>
    /// </summary>
    /// <param name="image">The image to export.</param>
    /// <param name="stream">The stream to write the BMP data to.</param>
    /// <param name="indexingMode">The palette and quantization mode to use. Defaults to <see cref="BmpIndexingMode.Normal"/>.</param>
    public static void ExportAs8bppGrayscaleBmpFormat(this Image image, 
        Stream stream, BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);
        if (!Enum.IsDefined(indexingMode))
        {
            throw new ArgumentException($"Unknown member: {indexingMode}", nameof(indexingMode));
        }

        Write8bppBmp(image, stream, DefaultGrayscaleColorMatrix, indexingMode);
    }

    /// <summary>
    /// Asynchronously exports the image as an 8-bit-per-pixel grayscale indexed BMP file to the specified stream,
    /// using the default grayscale color matrix (R=0.3, G=0.59, B=0.11).
    /// In <see cref="BmpIndexingMode.Normal"/> mode, the output uses a linear 256-entry
    /// grayscale palette. In <see cref="BmpIndexingMode.SystemDrawingCompatible"/> mode,
    /// the output uses the GDI+ halftone palette and quantization to match System.Drawing behavior.
    /// <para>
    /// Unlike the built-in <c>Save</c>/<c>SaveAs</c> methods, this method does not update
    /// <see cref="ImageMetadata.ExpectedFormat"/> because the 8bpp indexed format is a
    /// specialized export that cannot be round-tripped through the library's encoder pipeline.
    /// </para>
    /// </summary>
    /// <param name="image">The image to export.</param>
    /// <param name="stream">The stream to write the BMP data to.</param>
    /// <param name="indexingMode">The palette and quantization mode to use. Defaults to <see cref="BmpIndexingMode.Normal"/>.</param>
    public static async Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image, 
        Stream stream, BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);
        if (!Enum.IsDefined(indexingMode))
        {
            throw new ArgumentException($"Unknown member: {indexingMode}", nameof(indexingMode));
        }

        using var ms = new MemoryStream();
        Write8bppBmp(image, ms, DefaultGrayscaleColorMatrix, indexingMode);
        ms.Position = 0;
        await ms.CopyToAsync(stream);
    }
}
