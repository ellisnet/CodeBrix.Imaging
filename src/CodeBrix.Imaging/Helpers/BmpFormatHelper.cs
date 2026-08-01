using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Common.Helpers;
using CodeBrix.Imaging.Metadata;
using CodeBrix.Imaging.PixelFormats;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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

/// <summary>
/// Provides extension methods for exporting images as 8-bit-per-pixel grayscale BMP files.
/// </summary>
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

    /// <summary>
    /// Builds the BMP file header, info header and colour palette as a single contiguous
    /// block, and reports the row stride the pixel data must use.
    /// </summary>
    private static byte[] BuildHeaderAndPalette(
        Image image, BmpIndexingMode indexingMode, out int rowStride)
    {
        bool compatible = indexingMode == BmpIndexingMode.SystemDrawingCompatible;

        int width = image.Width;
        int height = image.Height;

        // Calculate row stride - each row is padded to a 4-byte boundary
        rowStride = (width + 3) & ~3;
        long pixelDataSizeLong = (long)rowStride * height;

        // BMP structure sizes
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40; // BITMAPINFOHEADER (V3)
        int paletteEntryCount = compatible ? HalftonePaletteEntryCount : 256;
        int colorPaletteSize = paletteEntryCount * 4;
        int pixelDataOffset = fileHeaderSize + infoHeaderSize + colorPaletteSize;

        // The limit is checked against the TOTAL file size, not just the pixel data: the
        // headers and palette add ~1 KB on top, so a pixel-data size of exactly int.MaxValue
        // would overflow the Int32 written into the BMP file-size header field.
        long fileSizeLong = pixelDataOffset + pixelDataSizeLong;
        if (fileSizeLong > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"The image dimensions ({width}x{height}) are too large for the 8bpp BMP format. "
                + $"The resulting file size ({fileSizeLong:N0} bytes) exceeds the maximum of {int.MaxValue:N0} bytes.");
        }

        int pixelDataSize = (int)pixelDataSizeLong;
        int fileSize = (int)fileSizeLong;

        // Get resolution from image metadata (BMP uses pixels per meter)
        int hResolution = 0;
        int vResolution = 0;
        var metadata = image.Metadata;
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

        // Header + palette are written as one contiguous block.
        byte[] block = new byte[pixelDataOffset];
        Span<byte> header = block.AsSpan(0, fileHeaderSize + infoHeaderSize);

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

        // Colour palette, immediately following the headers.
        Span<byte> palette = block.AsSpan(pixelDataOffset - colorPaletteSize, colorPaletteSize);
        if (compatible)
        {
            HalftonePalette.AsSpan(0, colorPaletteSize).CopyTo(palette);
        }
        else
        {
            for (int i = 0; i < 256; i++)
            {
                int idx = i * 4;
                palette[idx] = (byte)i;     // Blue
                palette[idx + 1] = (byte)i; // Green
                palette[idx + 2] = (byte)i; // Red
                palette[idx + 3] = 0;       // Reserved
            }
        }

        return block;
    }

    /// <summary>
    /// Converts one row of pixels to 8bpp palette indices. The row buffer is the full BMP row
    /// stride; bytes past the image width are row padding and are left at zero.
    /// </summary>
    private static void MapRowToPaletteIndices(
        ReadOnlySpan<Rgba32> pixelRow, Span<byte> rowBuffer, ColorMatrix colorMatrix, bool compatible)
    {
        // The color matrix is applied as:
        //   gray = R * M11 + G * M21 + B * M31 + A * M41 + M51 * 255
        // where R, G, B, A are byte values (0-255) and the result is clamped to [0, 255].
        float rWeight = colorMatrix.M11;
        float gWeight = colorMatrix.M21;
        float bWeight = colorMatrix.M31;
        float aWeight = colorMatrix.M41;
        float translation = colorMatrix.M51 * 255f;

        for (int x = 0; x < pixelRow.Length; x++)
        {
            ref readonly var pixel = ref pixelRow[x];
            float gray = (pixel.R * rWeight) + (pixel.G * gWeight) + (pixel.B * bWeight)
                         + (pixel.A * aWeight) + translation;

            byte grayByte = (byte)Math.Clamp((int)Math.Round(gray), 0, 255);

            // In Normal mode, the gray value IS the palette index (linear grayscale palette).
            // In SystemDrawingCompatible mode, map through the halftone lookup table.
            rowBuffer[x] = compatible ? GrayscaleToHalftoneIndex[grayByte] : grayByte;
        }
    }

    /// <summary>
    /// Writes the 8bpp pixel data in BMP bottom-up row order, converting one row at a time.
    /// </summary>
    /// <remarks>
    /// Deliberately avoids cloning the whole image to <see cref="Rgba32"/>: the extra memory
    /// is O(width), not O(width * height), which matters for the large scans this export
    /// exists to serve.
    /// </remarks>
    private sealed class PixelDataWriter : IImageVisitor, IImageVisitorAsync
    {
        private readonly Stream _stream;
        private readonly ColorMatrix _colorMatrix;
        private readonly bool _compatible;
        private readonly int _rowStride;

        public PixelDataWriter(Stream stream, ColorMatrix colorMatrix, bool compatible, int rowStride)
        {
            _stream = stream;
            _colorMatrix = colorMatrix;
            _compatible = compatible;
            _rowStride = rowStride;
        }

        public void Visit<TPixel>(Image<TPixel> image)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var configuration = image.GetConfiguration();
            byte[] rowBuffer = new byte[_rowStride]; // padding bytes stay 0
            Rgba32[] rgbaRow = new Rgba32[image.Width];

            for (int y = image.Height - 1; y >= 0; y--)
            {
                ConvertRow(configuration, image, y, rgbaRow, rowBuffer);
                _stream.Write(rowBuffer, 0, _rowStride);
            }

            _stream.Flush();
        }

        public async Task VisitAsync<TPixel>(Image<TPixel> image, CancellationToken cancellationToken)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var configuration = image.GetConfiguration();
            byte[] rowBuffer = new byte[_rowStride];
            Rgba32[] rgbaRow = new Rgba32[image.Width];

            for (int y = image.Height - 1; y >= 0; y--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConvertRow(configuration, image, y, rgbaRow, rowBuffer);
                await _stream.WriteAsync(rowBuffer.AsMemory(0, _rowStride), cancellationToken)
                    .ConfigureAwait(false);
            }

            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private void ConvertRow<TPixel>(
            Configuration configuration, Image<TPixel> image, int y, Rgba32[] rgbaRow, byte[] rowBuffer)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var sourceRow = image.DangerousGetPixelRowMemory(y).Span;
            PixelOperations<TPixel>.Instance.ToRgba32(configuration, sourceRow, rgbaRow);
            MapRowToPaletteIndices(rgbaRow, rowBuffer, _colorMatrix, _compatible);
        }
    }

    private static void Write8bppBmp(Image image, Stream stream, ColorMatrix colorMatrix, BmpIndexingMode indexingMode)
    {
        byte[] headerAndPalette = BuildHeaderAndPalette(image, indexingMode, out int rowStride);
        stream.Write(headerAndPalette, 0, headerAndPalette.Length);

        bool compatible = indexingMode == BmpIndexingMode.SystemDrawingCompatible;
        image.AcceptVisitor(new PixelDataWriter(stream, colorMatrix, compatible, rowStride));
    }

    private static async Task Write8bppBmpAsync(
        Image image,
        Stream stream,
        ColorMatrix colorMatrix,
        BmpIndexingMode indexingMode,
        CancellationToken cancellationToken)
    {
        byte[] headerAndPalette = BuildHeaderAndPalette(image, indexingMode, out int rowStride);
        await stream.WriteAsync(headerAndPalette.AsMemory(), cancellationToken).ConfigureAwait(false);

        bool compatible = indexingMode == BmpIndexingMode.SystemDrawingCompatible;
        await image.AcceptVisitorAsync(
                new PixelDataWriter(stream, colorMatrix, compatible, rowStride), cancellationToken)
            .ConfigureAwait(false);
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
    /// A grayscale color matrix using ITU-R BT.601 luma coefficients: R=0.299, G=0.587, B=0.114.
    /// These are the same weights used by <see cref="Processing.GrayscaleMode.Bt601"/>.
    /// <see href="https://en.wikipedia.org/wiki/Luma_%28video%29#Rec._601_luma_versus_Rec._709_luma_coefficients"/>
    /// </summary>
    public static readonly ColorMatrix Bt601GrayscaleColorMatrix = new(
        .299f, .299f, .299f, 0f,
        .587f, .587f, .587f, 0f,
        .114f, .114f, .114f, 0f,
        0f, 0f, 0f, 1f,
        0f, 0f, 0f, 0f);

    /// <summary>
    /// A grayscale color matrix using ITU-R BT.709 luma coefficients: R=0.2126, G=0.7152, B=0.0722.
    /// These are the same weights used by <see cref="Processing.GrayscaleMode.Bt709"/>.
    /// <see href="https://en.wikipedia.org/wiki/Rec._709#Luma_coefficients"/>
    /// </summary>
    public static readonly ColorMatrix Bt709GrayscaleColorMatrix = new(
        .2126f, .2126f, .2126f, 0f,
        .7152f, .7152f, .7152f, 0f,
        .0722f, .0722f, .0722f, 0f,
        0f, 0f, 0f, 1f,
        0f, 0f, 0f, 0f);

    private static void ValidateExportArguments(Image image, Stream stream, BmpIndexingMode indexingMode)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
        {
            throw new ArgumentException("The stream must be writable.", nameof(stream));
        }

        if (!Enum.IsDefined(indexingMode))
        {
            throw new ArgumentException($"Unknown member: {indexingMode}", nameof(indexingMode));
        }
    }

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
        ValidateExportArguments(image, stream, indexingMode);
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
    public static Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image,
        Stream stream, ColorMatrix colorMatrix, BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
        => ExportAs8bppGrayscaleBmpFormatAsync(image, stream, colorMatrix, indexingMode, CancellationToken.None);

    /// <summary>
    /// Asynchronously exports the image as an 8-bit-per-pixel grayscale BMP file to the specified
    /// stream, applying the given color matrix to determine each pixel's palette index.
    /// </summary>
    /// <param name="image">The image to export.</param>
    /// <param name="stream">The stream to write the BMP data to.</param>
    /// <param name="colorMatrix">The color matrix used to transform pixel colors to a single grayscale value.</param>
    /// <param name="indexingMode">The palette and quantization mode to use.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static async Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image,
        Stream stream, ColorMatrix colorMatrix, BmpIndexingMode indexingMode,
        CancellationToken cancellationToken)
    {
        ValidateExportArguments(image, stream, indexingMode);
        await Write8bppBmpAsync(image, stream, colorMatrix, indexingMode, cancellationToken)
            .ConfigureAwait(false);
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
        ValidateExportArguments(image, stream, indexingMode);
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
    public static Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image,
        Stream stream, BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
        => ExportAs8bppGrayscaleBmpFormatAsync(image, stream, DefaultGrayscaleColorMatrix, indexingMode,
            CancellationToken.None);

    /// <summary>
    /// Asynchronously exports the image as an 8-bit-per-pixel grayscale indexed BMP file to the
    /// specified stream, using the default grayscale color matrix (R=0.3, G=0.59, B=0.11).
    /// </summary>
    /// <param name="image">The image to export.</param>
    /// <param name="stream">The stream to write the BMP data to.</param>
    /// <param name="indexingMode">The palette and quantization mode to use.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image,
        Stream stream, BmpIndexingMode indexingMode, CancellationToken cancellationToken)
        => ExportAs8bppGrayscaleBmpFormatAsync(image, stream, DefaultGrayscaleColorMatrix, indexingMode,
            cancellationToken);

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The path must not be null or whitespace.", nameof(path));
        }
    }

    /// <summary>
    /// Exports the image as an 8-bit-per-pixel grayscale indexed BMP file at the specified path,
    /// using the default grayscale color matrix (R=0.3, G=0.59, B=0.11).
    /// An existing file at that path is overwritten.
    /// </summary>
    /// <param name="image">The image to export.</param>
    /// <param name="path">The file path to write the BMP data to.</param>
    /// <param name="indexingMode">The palette and quantization mode to use. Defaults to <see cref="BmpIndexingMode.Normal"/>.</param>
    public static void ExportAs8bppGrayscaleBmpFormat(this Image image,
        string path, BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
        => ExportAs8bppGrayscaleBmpFormat(image, path, DefaultGrayscaleColorMatrix, indexingMode);

    /// <summary>
    /// Exports the image as an 8-bit-per-pixel grayscale BMP file at the specified path,
    /// applying the given color matrix to determine each pixel's palette index.
    /// An existing file at that path is overwritten.
    /// </summary>
    /// <param name="image">The image to export.</param>
    /// <param name="path">The file path to write the BMP data to.</param>
    /// <param name="colorMatrix">The color matrix used to transform pixel colors to a single grayscale value.</param>
    /// <param name="indexingMode">The palette and quantization mode to use. Defaults to <see cref="BmpIndexingMode.Normal"/>.</param>
    public static void ExportAs8bppGrayscaleBmpFormat(this Image image,
        string path, ColorMatrix colorMatrix, BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidatePath(path);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        ExportAs8bppGrayscaleBmpFormat(image, stream, colorMatrix, indexingMode);
    }

    /// <summary>
    /// Asynchronously exports the image as an 8-bit-per-pixel grayscale indexed BMP file at the
    /// specified path, using the default grayscale color matrix (R=0.3, G=0.59, B=0.11).
    /// An existing file at that path is overwritten.
    /// </summary>
    /// <param name="image">The image to export.</param>
    /// <param name="path">The file path to write the BMP data to.</param>
    /// <param name="indexingMode">The palette and quantization mode to use. Defaults to <see cref="BmpIndexingMode.Normal"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image,
        string path, BmpIndexingMode indexingMode = BmpIndexingMode.Normal,
        CancellationToken cancellationToken = default)
        => ExportAs8bppGrayscaleBmpFormatAsync(image, path, DefaultGrayscaleColorMatrix, indexingMode,
            cancellationToken);

    /// <summary>
    /// Asynchronously exports the image as an 8-bit-per-pixel grayscale BMP file at the specified
    /// path, applying the given color matrix to determine each pixel's palette index.
    /// An existing file at that path is overwritten.
    /// </summary>
    /// <param name="image">The image to export.</param>
    /// <param name="path">The file path to write the BMP data to.</param>
    /// <param name="colorMatrix">The color matrix used to transform pixel colors to a single grayscale value.</param>
    /// <param name="indexingMode">The palette and quantization mode to use. Defaults to <see cref="BmpIndexingMode.Normal"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static async Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image,
        string path, ColorMatrix colorMatrix, BmpIndexingMode indexingMode = BmpIndexingMode.Normal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidatePath(path);

        await using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await ExportAs8bppGrayscaleBmpFormatAsync(image, stream, colorMatrix, indexingMode, cancellationToken)
            .ConfigureAwait(false);
    }
}
