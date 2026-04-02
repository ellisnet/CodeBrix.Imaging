using CodeBrix.Imaging.Helpers;
using CodeBrix.Imaging.Tests.Helpers;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Imaging.Tests.Advanced;

// ReSharper disable once InconsistentNaming
public class Format8bppIndexedTests
{
    // ReSharper disable once InconsistentNaming
    private readonly ITestOutputHelper _output;

    public Format8bppIndexedTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Theory]
    [InlineData("test-image-01.png", BmpIndexingMode.Normal,
        "ExportAs8bppGrayscaleBmp_from_PNG_BmpIndexingMode_Normal_reference.bmp")]

    [InlineData("test-image-01.jpg", BmpIndexingMode.Normal,
        "ExportAs8bppGrayscaleBmp_from_JPG_BmpIndexingMode_Normal_reference.bmp")]

    [InlineData("test-image-01.bmp", BmpIndexingMode.Normal,
        "ExportAs8bppGrayscaleBmp_from_BMP_BmpIndexingMode_Normal_reference.bmp")]

    [InlineData("test-image-01.png", BmpIndexingMode.SystemDrawingCompatible,
        "ExportAs8bppGrayscaleBmp_from_PNG_BmpIndexingMode_SystemDrawingCompatible_reference.bmp")]

    [InlineData("test-image-01.jpg", BmpIndexingMode.SystemDrawingCompatible,
        "ExportAs8bppGrayscaleBmp_from_JPG_BmpIndexingMode_SystemDrawingCompatible_reference.bmp")]

    [InlineData("test-image-01.bmp", BmpIndexingMode.SystemDrawingCompatible,
        "ExportAs8bppGrayscaleBmp_from_BMP_BmpIndexingMode_SystemDrawingCompatible_reference.bmp")]
    public async Task ExportAs8bppGrayscaleBmpFormatAsync_exports_image(string resourceName, BmpIndexingMode indexingMode,
        string compareResourceName)
    {
        // Arrange
        using var sampleImage = ImageTestHelper.LoadImage(resourceName);

        // Act - Export as 8bpp grayscale BMP using the CodeBrix.Imaging library
        using var outputMs = new MemoryStream();
        await sampleImage.ExportAs8bppGrayscaleBmpFormatAsync(outputMs, indexingMode);
        var actualBytes = outputMs.ToArray();

        // Load the reference image from embedded resources
        await using var referenceStream = ImageTestHelper.GetImageStream(compareResourceName);
        using var referenceMs = new MemoryStream();
        await referenceStream.CopyToAsync(referenceMs, CancellationToken.None);
        var expectedBytes = referenceMs.ToArray();

        // Assert
        _output.WriteLine($"Source image: {resourceName} ({sampleImage.Width}x{sampleImage.Height})");
        _output.WriteLine($"Reference image: {compareResourceName}");
        _output.WriteLine($"Actual byte count: {actualBytes.Length}");
        _output.WriteLine($"Expected byte count: {expectedBytes.Length}");

        Assert.Equal(expectedBytes.Length, actualBytes.Length);

        // Verify the BMP file header (14 bytes) and info header (40 bytes) match exactly.
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        const int totalHeaderSize = fileHeaderSize + infoHeaderSize;

        Assert.True(actualBytes.Length >= totalHeaderSize, "File too small to contain BMP headers");
        Assert.Equal(
            expectedBytes.AsSpan(0, totalHeaderSize).ToArray(),
            actualBytes.AsSpan(0, totalHeaderSize).ToArray());
        _output.WriteLine("BMP file header + info header: exact match");

        // Verify the color palette matches exactly.
        // The pixel data offset (stored at bytes 10-13) tells us where headers + palette end.
        int pixelDataOffset = BinaryPrimitives.ReadInt32LittleEndian(expectedBytes.AsSpan(10));
        int paletteSize = pixelDataOffset - totalHeaderSize;
        _output.WriteLine($"Palette size: {paletteSize} bytes ({paletteSize / 4} entries)");

        Assert.Equal(
            expectedBytes.AsSpan(totalHeaderSize, paletteSize).ToArray(),
            actualBytes.AsSpan(totalHeaderSize, paletteSize).ToArray());
        _output.WriteLine("Color palette: exact match");

        // Full byte-level comparison of the entire file (headers + palette + pixel data).
        // Note: This may fail due to minor differences in how CodeBrix.Imaging and System.Drawing
        // decode the same source image (±1 in RGB channel values), which can cause pixels near
        // grayscale quantization boundaries to map to different palette indices.
        Assert.Equal(expectedBytes, actualBytes);
    }

    /// <summary>
    /// A red-channel-heavy color matrix (R=0.8, G=0.1, B=0.1) used to exercise the
    /// custom ColorMatrix overload of ExportAs8bppGrayscaleBmpFormatAsync.
    /// This produces visually distinct output compared to the default grayscale weights.
    /// </summary>
    private static readonly ColorMatrix RedHeavyColorMatrix = new(
        .8f, .8f, .8f, 0f,
        .1f, .1f, .1f, 0f,
        .1f, .1f, .1f, 0f,
        0f, 0f, 0f, 1f,
        0f, 0f, 0f, 0f);

    [Theory]
    [InlineData("test-image-01.png", BmpIndexingMode.Normal,
        "ExportAs8bppGrayscaleBmp_with_matrix_from_PNG_BmpIndexingMode_Normal_reference.bmp")]

    [InlineData("test-image-01.jpg", BmpIndexingMode.Normal,
        "ExportAs8bppGrayscaleBmp_with_matrix_from_JPG_BmpIndexingMode_Normal_reference.bmp")]

    [InlineData("test-image-01.bmp", BmpIndexingMode.Normal,
        "ExportAs8bppGrayscaleBmp_with_matrix_from_BMP_BmpIndexingMode_Normal_reference.bmp")]

    [InlineData("test-image-01.png", BmpIndexingMode.SystemDrawingCompatible,
        "ExportAs8bppGrayscaleBmp_with_matrix_from_PNG_BmpIndexingMode_SystemDrawingCompatible_reference.bmp")]

    [InlineData("test-image-01.jpg", BmpIndexingMode.SystemDrawingCompatible,
        "ExportAs8bppGrayscaleBmp_with_matrix_from_JPG_BmpIndexingMode_SystemDrawingCompatible_reference.bmp")]

    [InlineData("test-image-01.bmp", BmpIndexingMode.SystemDrawingCompatible,
        "ExportAs8bppGrayscaleBmp_with_matrix_from_BMP_BmpIndexingMode_SystemDrawingCompatible_reference.bmp")]
    public async Task ExportAs8bppGrayscaleBmpFormatAsync_with_matrix_exports_image(string resourceName, BmpIndexingMode indexingMode,
        string compareResourceName)
    {
        // Arrange
        using var sampleImage = ImageTestHelper.LoadImage(resourceName);

        // Act - Export as 8bpp grayscale BMP using a red-channel-heavy color matrix
        using var outputMs = new MemoryStream();
        await sampleImage.ExportAs8bppGrayscaleBmpFormatAsync(outputMs, RedHeavyColorMatrix, indexingMode);
        var actualBytes = outputMs.ToArray();

        // Load the reference image from embedded resources
        await using var referenceStream = ImageTestHelper.GetImageStream(compareResourceName);
        using var referenceMs = new MemoryStream();
        await referenceStream.CopyToAsync(referenceMs, CancellationToken.None);
        var expectedBytes = referenceMs.ToArray();

        // Assert
        _output.WriteLine($"Source image: {resourceName} ({sampleImage.Width}x{sampleImage.Height})");
        _output.WriteLine($"Reference image: {compareResourceName}");
        _output.WriteLine($"Actual byte count: {actualBytes.Length}");
        _output.WriteLine($"Expected byte count: {expectedBytes.Length}");

        Assert.Equal(expectedBytes.Length, actualBytes.Length);

        // Verify the BMP file header (14 bytes) and info header (40 bytes) match exactly.
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        const int totalHeaderSize = fileHeaderSize + infoHeaderSize;

        Assert.True(actualBytes.Length >= totalHeaderSize, "File too small to contain BMP headers");
        Assert.Equal(
            expectedBytes.AsSpan(0, totalHeaderSize).ToArray(),
            actualBytes.AsSpan(0, totalHeaderSize).ToArray());
        _output.WriteLine("BMP file header + info header: exact match");

        // Verify the color palette matches exactly.
        // The pixel data offset (stored at bytes 10-13) tells us where headers + palette end.
        int pixelDataOffset = BinaryPrimitives.ReadInt32LittleEndian(expectedBytes.AsSpan(10));
        int paletteSize = pixelDataOffset - totalHeaderSize;
        _output.WriteLine($"Palette size: {paletteSize} bytes ({paletteSize / 4} entries)");

        Assert.Equal(
            expectedBytes.AsSpan(totalHeaderSize, paletteSize).ToArray(),
            actualBytes.AsSpan(totalHeaderSize, paletteSize).ToArray());
        _output.WriteLine("Color palette: exact match");

        // Full byte-level comparison of the entire file (headers + palette + pixel data).
        Assert.Equal(expectedBytes, actualBytes);
    }
}
