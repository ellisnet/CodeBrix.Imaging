using CodeBrix.Imaging.Helpers;
using CodeBrix.Imaging.PixelFormats;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Imaging.Tests.Advanced;

/// <summary>
/// Tests for <see cref="BmpFormatHelper"/> argument validation and edge cases.
/// These tests verify that the validation logic in the public export methods
/// correctly rejects invalid inputs, ensuring that refactoring the validation
/// (e.g., extracting a shared helper method) preserves the expected behavior.
/// </summary>
// ReSharper disable once InconsistentNaming
public class BmpFormatHelperValidationTests
{
    // ReSharper disable once InconsistentNaming
    private readonly ITestOutputHelper _output;

    public BmpFormatHelperValidationTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    #region Sync overload (without ColorMatrix) validation

    [Fact]
    public void ExportAs8bppGrayscaleBmpFormat_NullImage_ThrowsArgumentNullException()
    {
        // Arrange
        Image image = null;
        using var stream = new MemoryStream();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            image.ExportAs8bppGrayscaleBmpFormat(stream));
        _output.WriteLine("Null image correctly rejected");
    }

    [Fact]
    public void ExportAs8bppGrayscaleBmpFormat_NullStream_ThrowsArgumentNullException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            image.ExportAs8bppGrayscaleBmpFormat(null));
        _output.WriteLine("Null stream correctly rejected");
    }

    [Fact]
    public void ExportAs8bppGrayscaleBmpFormat_NonWritableStream_ThrowsArgumentException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        using var readOnlyStream = new MemoryStream(new byte[100], writable: false);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            image.ExportAs8bppGrayscaleBmpFormat(readOnlyStream));
        Assert.Equal("stream", ex.ParamName);
        _output.WriteLine($"Non-writable stream correctly rejected: {ex.Message}");
    }

    [Fact]
    public void ExportAs8bppGrayscaleBmpFormat_InvalidIndexingMode_ThrowsArgumentException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        using var stream = new MemoryStream();
        var invalidMode = (BmpIndexingMode)999;

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            image.ExportAs8bppGrayscaleBmpFormat(stream, invalidMode));
        Assert.Equal("indexingMode", ex.ParamName);
        _output.WriteLine($"Invalid indexing mode correctly rejected: {ex.Message}");
    }

    #endregion

    #region Sync overload (with ColorMatrix) validation

    [Fact]
    public void ExportAs8bppGrayscaleBmpFormat_WithMatrix_NullImage_ThrowsArgumentNullException()
    {
        // Arrange
        Image image = null;
        using var stream = new MemoryStream();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            image.ExportAs8bppGrayscaleBmpFormat(stream, BmpFormatHelper.DefaultGrayscaleColorMatrix));
        _output.WriteLine("Null image correctly rejected (ColorMatrix overload)");
    }

    [Fact]
    public void ExportAs8bppGrayscaleBmpFormat_WithMatrix_NullStream_ThrowsArgumentNullException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            image.ExportAs8bppGrayscaleBmpFormat(null, BmpFormatHelper.DefaultGrayscaleColorMatrix));
        _output.WriteLine("Null stream correctly rejected (ColorMatrix overload)");
    }

    [Fact]
    public void ExportAs8bppGrayscaleBmpFormat_WithMatrix_NonWritableStream_ThrowsArgumentException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        using var readOnlyStream = new MemoryStream(new byte[100], writable: false);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            image.ExportAs8bppGrayscaleBmpFormat(readOnlyStream, BmpFormatHelper.DefaultGrayscaleColorMatrix));
        Assert.Equal("stream", ex.ParamName);
        _output.WriteLine($"Non-writable stream correctly rejected (ColorMatrix overload): {ex.Message}");
    }

    [Fact]
    public void ExportAs8bppGrayscaleBmpFormat_WithMatrix_InvalidIndexingMode_ThrowsArgumentException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        using var stream = new MemoryStream();
        var invalidMode = (BmpIndexingMode)999;

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            image.ExportAs8bppGrayscaleBmpFormat(stream, BmpFormatHelper.DefaultGrayscaleColorMatrix, invalidMode));
        Assert.Equal("indexingMode", ex.ParamName);
        _output.WriteLine($"Invalid indexing mode correctly rejected (ColorMatrix overload): {ex.Message}");
    }

    #endregion

    #region Async overload (without ColorMatrix) validation

    [Fact]
    public async Task ExportAs8bppGrayscaleBmpFormatAsync_NullImage_ThrowsArgumentNullException()
    {
        // Arrange
        Image image = null;
        using var stream = new MemoryStream();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await image.ExportAs8bppGrayscaleBmpFormatAsync(stream));
        _output.WriteLine("Null image correctly rejected (async)");
    }

    [Fact]
    public async Task ExportAs8bppGrayscaleBmpFormatAsync_NullStream_ThrowsArgumentNullException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await image.ExportAs8bppGrayscaleBmpFormatAsync(null));
        _output.WriteLine("Null stream correctly rejected (async)");
    }

    [Fact]
    public async Task ExportAs8bppGrayscaleBmpFormatAsync_NonWritableStream_ThrowsArgumentException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        using var readOnlyStream = new MemoryStream(new byte[100], writable: false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await image.ExportAs8bppGrayscaleBmpFormatAsync(readOnlyStream));
        Assert.Equal("stream", ex.ParamName);
        _output.WriteLine($"Non-writable stream correctly rejected (async): {ex.Message}");
    }

    [Fact]
    public async Task ExportAs8bppGrayscaleBmpFormatAsync_InvalidIndexingMode_ThrowsArgumentException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        using var stream = new MemoryStream();
        var invalidMode = (BmpIndexingMode)999;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await image.ExportAs8bppGrayscaleBmpFormatAsync(stream, invalidMode));
        Assert.Equal("indexingMode", ex.ParamName);
        _output.WriteLine($"Invalid indexing mode correctly rejected (async): {ex.Message}");
    }

    #endregion

    #region Async overload (with ColorMatrix) validation

    [Fact]
    public async Task ExportAs8bppGrayscaleBmpFormatAsync_WithMatrix_NullImage_ThrowsArgumentNullException()
    {
        // Arrange
        Image image = null;
        using var stream = new MemoryStream();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await image.ExportAs8bppGrayscaleBmpFormatAsync(stream, BmpFormatHelper.DefaultGrayscaleColorMatrix));
        _output.WriteLine("Null image correctly rejected (async, ColorMatrix overload)");
    }

    [Fact]
    public async Task ExportAs8bppGrayscaleBmpFormatAsync_WithMatrix_NullStream_ThrowsArgumentNullException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await image.ExportAs8bppGrayscaleBmpFormatAsync(null, BmpFormatHelper.DefaultGrayscaleColorMatrix));
        _output.WriteLine("Null stream correctly rejected (async, ColorMatrix overload)");
    }

    [Fact]
    public async Task ExportAs8bppGrayscaleBmpFormatAsync_WithMatrix_NonWritableStream_ThrowsArgumentException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        using var readOnlyStream = new MemoryStream(new byte[100], writable: false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await image.ExportAs8bppGrayscaleBmpFormatAsync(readOnlyStream, BmpFormatHelper.DefaultGrayscaleColorMatrix));
        Assert.Equal("stream", ex.ParamName);
        _output.WriteLine($"Non-writable stream correctly rejected (async, ColorMatrix overload): {ex.Message}");
    }

    [Fact]
    public async Task ExportAs8bppGrayscaleBmpFormatAsync_WithMatrix_InvalidIndexingMode_ThrowsArgumentException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        using var stream = new MemoryStream();
        var invalidMode = (BmpIndexingMode)999;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await image.ExportAs8bppGrayscaleBmpFormatAsync(stream, BmpFormatHelper.DefaultGrayscaleColorMatrix, invalidMode));
        Assert.Equal("indexingMode", ex.ParamName);
        _output.WriteLine($"Invalid indexing mode correctly rejected (async, ColorMatrix overload): {ex.Message}");
    }

    #endregion

    #region Color Matrix Constants Tests

    [Fact]
    public void DefaultGrayscaleColorMatrix_HasExpectedWeights()
    {
        // Assert - verify the well-known System.Drawing-compatible weights
        var matrix = BmpFormatHelper.DefaultGrayscaleColorMatrix;
        Assert.Equal(0.3f, matrix.M11);
        Assert.Equal(0.59f, matrix.M21);
        Assert.Equal(0.11f, matrix.M31);
        _output.WriteLine($"Default weights: R={matrix.M11}, G={matrix.M21}, B={matrix.M31}");
    }

    [Fact]
    public void Bt601GrayscaleColorMatrix_HasExpectedWeights()
    {
        // Assert - verify BT.601 luma coefficients
        var matrix = BmpFormatHelper.Bt601GrayscaleColorMatrix;
        Assert.Equal(0.299f, matrix.M11);
        Assert.Equal(0.587f, matrix.M21);
        Assert.Equal(0.114f, matrix.M31);
        _output.WriteLine($"BT.601 weights: R={matrix.M11}, G={matrix.M21}, B={matrix.M31}");
    }

    [Fact]
    public void Bt709GrayscaleColorMatrix_HasExpectedWeights()
    {
        // Assert - verify BT.709 luma coefficients
        var matrix = BmpFormatHelper.Bt709GrayscaleColorMatrix;
        Assert.Equal(0.2126f, matrix.M11);
        Assert.Equal(0.7152f, matrix.M21);
        Assert.Equal(0.0722f, matrix.M31);
        _output.WriteLine($"BT.709 weights: R={matrix.M11}, G={matrix.M21}, B={matrix.M31}");
    }

    [Fact]
    public void GrayscaleColorMatrices_HaveSameStructure()
    {
        // All grayscale matrices should have the same structure:
        // Row 1-3: weight replicated across R,G,B columns; A column = 0
        // Row 4: identity for alpha (0,0,0,1)
        // Row 5: zero translation (0,0,0,0)
        var matrices = new[]
        {
            ("Default", BmpFormatHelper.DefaultGrayscaleColorMatrix),
            ("BT.601", BmpFormatHelper.Bt601GrayscaleColorMatrix),
            ("BT.709", BmpFormatHelper.Bt709GrayscaleColorMatrix)
        };

        foreach (var (name, m) in matrices)
        {
            // Row 1: R weight replicated
            Assert.Equal(m.M11, m.M12);
            Assert.Equal(m.M11, m.M13);
            Assert.Equal(0f, m.M14);

            // Row 2: G weight replicated
            Assert.Equal(m.M21, m.M22);
            Assert.Equal(m.M21, m.M23);
            Assert.Equal(0f, m.M24);

            // Row 3: B weight replicated
            Assert.Equal(m.M31, m.M32);
            Assert.Equal(m.M31, m.M33);
            Assert.Equal(0f, m.M34);

            // Row 4: alpha identity
            Assert.Equal(0f, m.M41);
            Assert.Equal(0f, m.M42);
            Assert.Equal(0f, m.M43);
            Assert.Equal(1f, m.M44);

            // Row 5: zero translation
            Assert.Equal(0f, m.M51);
            Assert.Equal(0f, m.M52);
            Assert.Equal(0f, m.M53);
            Assert.Equal(0f, m.M54);

            _output.WriteLine($"{name}: structure verified (R={m.M11}, G={m.M21}, B={m.M31})");
        }
    }

    #endregion

    #region Sync default overload produces valid BMP output

    [Theory]
    [InlineData(BmpIndexingMode.Normal)]
    [InlineData(BmpIndexingMode.SystemDrawingCompatible)]
    public void ExportAs8bppGrayscaleBmpFormat_ProducesValidBmpOutput(BmpIndexingMode indexingMode)
    {
        // Arrange
        using var image = new Image<Rgba32>(4, 3);
        using var stream = new MemoryStream();

        // Act
        image.ExportAs8bppGrayscaleBmpFormat(stream, indexingMode);
        var bytes = stream.ToArray();

        // Assert - verify BMP header signature
        Assert.True(bytes.Length > 54, "Output should contain at least BMP headers");
        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);
        // Bits per pixel at offset 28 should be 8
        Assert.Equal(8, BitConverter.ToInt16(bytes, 28));
        _output.WriteLine($"Valid BMP output: {bytes.Length} bytes, mode={indexingMode}");
    }

    #endregion

    #region Async default overload produces valid BMP output

    [Theory]
    [InlineData(BmpIndexingMode.Normal)]
    [InlineData(BmpIndexingMode.SystemDrawingCompatible)]
    public async Task ExportAs8bppGrayscaleBmpFormatAsync_ProducesValidBmpOutput(BmpIndexingMode indexingMode)
    {
        // Arrange
        using var image = new Image<Rgba32>(4, 3);
        using var stream = new MemoryStream();

        // Act
        await image.ExportAs8bppGrayscaleBmpFormatAsync(stream, indexingMode);
        var bytes = stream.ToArray();

        // Assert
        Assert.True(bytes.Length > 54, "Output should contain at least BMP headers");
        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);
        Assert.Equal(8, BitConverter.ToInt16(bytes, 28));
        _output.WriteLine($"Valid async BMP output: {bytes.Length} bytes, mode={indexingMode}");
    }

    #endregion

    #region Sync and Async produce identical output

    [Theory]
    [InlineData(BmpIndexingMode.Normal)]
    [InlineData(BmpIndexingMode.SystemDrawingCompatible)]
    public async Task SyncAndAsync_DefaultOverloads_ProduceIdenticalOutput(BmpIndexingMode indexingMode)
    {
        // Arrange
        using var image = new Image<Rgba32>(8, 6, new Rgba32(100, 150, 200, 255));

        // Act - sync
        using var syncStream = new MemoryStream();
        image.ExportAs8bppGrayscaleBmpFormat(syncStream, indexingMode);
        var syncBytes = syncStream.ToArray();

        // Act - async
        using var asyncStream = new MemoryStream();
        await image.ExportAs8bppGrayscaleBmpFormatAsync(asyncStream, indexingMode);
        var asyncBytes = asyncStream.ToArray();

        // Assert
        Assert.Equal(syncBytes, asyncBytes);
        _output.WriteLine($"Sync/async output identical: {syncBytes.Length} bytes, mode={indexingMode}");
    }

    [Theory]
    [InlineData(BmpIndexingMode.Normal)]
    [InlineData(BmpIndexingMode.SystemDrawingCompatible)]
    public async Task SyncAndAsync_ColorMatrixOverloads_ProduceIdenticalOutput(BmpIndexingMode indexingMode)
    {
        // Arrange
        using var image = new Image<Rgba32>(8, 6, new Rgba32(100, 150, 200, 255));
        var matrix = BmpFormatHelper.Bt709GrayscaleColorMatrix;

        // Act - sync
        using var syncStream = new MemoryStream();
        image.ExportAs8bppGrayscaleBmpFormat(syncStream, matrix, indexingMode);
        var syncBytes = syncStream.ToArray();

        // Act - async
        using var asyncStream = new MemoryStream();
        await image.ExportAs8bppGrayscaleBmpFormatAsync(asyncStream, matrix, indexingMode);
        var asyncBytes = asyncStream.ToArray();

        // Assert
        Assert.Equal(syncBytes, asyncBytes);
        _output.WriteLine($"Sync/async ColorMatrix output identical: {syncBytes.Length} bytes, mode={indexingMode}");
    }

    #endregion
}
