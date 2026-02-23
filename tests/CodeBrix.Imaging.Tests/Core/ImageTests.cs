using CodeBrix.Imaging.Formats.Bmp;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Png;
using CodeBrix.Imaging.PixelFormats;
using CodeBrix.Imaging.Tests.Helpers;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Imaging.Tests.Core;

public class ImageTests
{
    private readonly ITestOutputHelper _output;

    public ImageTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidDimensions_CreatesImage()
    {
        // Arrange & Act
        using var image = new Image<Rgba32>(100, 50);

        // Assert
        Assert.Equal(100, image.Width);
        Assert.Equal(50, image.Height);
        _output.WriteLine($"Created image: {image.Width}x{image.Height}");
    }

    [Fact]
    public void Constructor_WithConfiguration_CreatesImage()
    {
        // Arrange
        var config = Configuration.Default;

        // Act
        using var image = new Image<Rgba32>(config, 100, 50);

        // Assert
        Assert.Equal(100, image.Width);
        Assert.Equal(50, image.Height);
        _output.WriteLine($"Created image with configuration: {image.Width}x{image.Height}");
    }

    [Fact]
    public void Constructor_WithBackgroundColor_CreatesImage()
    {
        // Arrange
        var backgroundColor = new Rgba32(255, 0, 0, 255);

        // Act
        using var image = new Image<Rgba32>(100, 50, backgroundColor);

        // Assert
        Assert.Equal(100, image.Width);
        Assert.Equal(50, image.Height);
        _output.WriteLine($"Created image with background color: {image.Width}x{image.Height}");
    }

    #endregion

    #region Properties Tests

    [Fact]
    public void PixelType_ReturnsCorrectPixelType()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act
        var pixelType = image.PixelType;

        // Assert
        Assert.NotNull(pixelType);
        Assert.Equal(32, pixelType.BitsPerPixel);
        _output.WriteLine($"Pixel type: BitsPerPixel={pixelType.BitsPerPixel}");
    }

    [Fact]
    public void Metadata_IsNotNull()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act
        var metadata = image.Metadata;

        // Assert
        Assert.NotNull(metadata);
        _output.WriteLine("Metadata is available");
    }

    [Fact]
    public void Frames_HasAtLeastOneFrame()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act
        var frames = image.Frames;

        // Assert
        Assert.NotNull(frames);
        Assert.True(frames.Count >= 1);
        _output.WriteLine($"Frame count: {frames.Count}");
    }

    #endregion

    #region Load Tests

    [Theory]
    [InlineData("test-image-01.png")]
    [InlineData("test-image-01.jpg")]
    [InlineData("test-image-01.bmp")]
    public void Load_FromEmbeddedResource_LoadsSuccessfully(string resourceName)
    {
        // Arrange & Act
        using var image = ImageTestHelper.LoadImage(resourceName);

        // Assert
        Assert.NotNull(image);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
        _output.WriteLine($"Loaded {resourceName}: {image.Width}x{image.Height}");
    }

    [Fact]
    public void Load_FromStream_LoadsSuccessfully()
    {
        // Arrange
        using var stream = ImageTestHelper.GetImageStream("test-image-01.png");

        // Act
        using var image = Image.Load(stream);

        // Assert
        Assert.NotNull(image);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
        _output.WriteLine($"Loaded from stream: {image.Width}x{image.Height}");
    }

    #endregion

    #region Save Tests

    [Fact]
    public void Save_ToStream_WithPngEncoder_SavesSuccessfully()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        using var stream = new MemoryStream();

        // Act
        image.Save(stream, new PngEncoder());

        // Assert
        Assert.True(stream.Length > 0);
        _output.WriteLine($"Saved image size: {stream.Length} bytes");
    }

    [Fact]
    public void Save_ToStream_WithJpegEncoder_SavesSuccessfully()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        using var stream = new MemoryStream();

        // Act
        image.Save(stream, new JpegEncoder());

        // Assert
        Assert.True(stream.Length > 0);
        _output.WriteLine($"Saved image size: {stream.Length} bytes");
    }

    [Fact]
    public void Save_ToStream_WithBmpEncoder_SavesSuccessfully()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        using var stream = new MemoryStream();

        // Act
        image.Save(stream, new BmpEncoder());

        // Assert
        Assert.True(stream.Length > 0);
        _output.WriteLine($"Saved image size: {stream.Length} bytes");
    }

    [Fact]
    public async Task SaveAsync_ToStream_WithPngEncoder_SavesSuccessfully()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        using var stream = new MemoryStream();

        // Act
        await image.SaveAsync(stream, new PngEncoder(), CancellationToken.None);

        // Assert
        Assert.True(stream.Length > 0);
        _output.WriteLine($"Saved image size: {stream.Length} bytes");
    }

    #endregion

    #region CloneAs Tests

    [Fact]
    public void CloneAs_ToSamePixelType_CreatesClone()
    {
        // Arrange
        using var original = new Image<Rgba32>(10, 10);

        // Act
        using var clone = original.CloneAs<Rgba32>();

        // Assert
        Assert.NotNull(clone);
        Assert.Equal(original.Width, clone.Width);
        Assert.Equal(original.Height, clone.Height);
        _output.WriteLine($"Cloned image: {clone.Width}x{clone.Height}");
    }

    [Fact]
    public void CloneAs_ToDifferentPixelType_CreatesClone()
    {
        // Arrange
        using var original = new Image<Rgba32>(10, 10);

        // Act
        using var clone = original.CloneAs<Rgb24>();

        // Assert
        Assert.NotNull(clone);
        Assert.Equal(original.Width, clone.Width);
        Assert.Equal(original.Height, clone.Height);
        _output.WriteLine($"Cloned image to Rgb24: {clone.Width}x{clone.Height}");
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_DisposesImage()
    {
        // Arrange
        var image = new Image<Rgba32>(10, 10);

        // Act
        image.Dispose();

        // Assert - saving to disposed image should throw
        using var stream = new MemoryStream();
        Assert.Throws<ObjectDisposedException>(() => image.Save(stream, new PngEncoder()));
        _output.WriteLine("Image was disposed successfully");
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var image = new Image<Rgba32>(10, 10);

        // Act & Assert - should not throw
        image.Dispose();
        image.Dispose();
        _output.WriteLine("Multiple dispose calls handled correctly");
    }

    #endregion

    #region DetectFormat Tests

    [Fact]
    public void DetectFormat_WithPngStream_DetectsPng()
    {
        // Arrange
        using var stream = ImageTestHelper.GetImageStream("test-image-01.png");

        // Act
        var format = Image.DetectFormat(stream);

        // Assert
        Assert.NotNull(format);
        Assert.Equal("PNG", format.Name);
        _output.WriteLine($"Detected format: {format.Name}");
    }

    [Fact]
    public void DetectFormat_WithJpegStream_DetectsJpeg()
    {
        // Arrange
        using var stream = ImageTestHelper.GetImageStream("test-image-01.jpg");

        // Act
        var format = Image.DetectFormat(stream);

        // Assert
        Assert.NotNull(format);
        Assert.Equal("JPEG", format.Name);
        _output.WriteLine($"Detected format: {format.Name}");
    }

    [Fact]
    public void DetectFormat_WithBmpStream_DetectsBmp()
    {
        // Arrange
        using var stream = ImageTestHelper.GetImageStream("test-image-01.bmp");

        // Act
        var format = Image.DetectFormat(stream);

        // Assert
        Assert.NotNull(format);
        Assert.Equal("BMP", format.Name);
        _output.WriteLine($"Detected format: {format.Name}");
    }

    #endregion

    #region Identify Tests

    [Fact]
    public void Identify_WithPngStream_ReturnsImageInfo()
    {
        // Arrange
        using var stream = ImageTestHelper.GetImageStream("test-image-01.png");

        // Act
        var info = Image.Identify(stream);

        // Assert
        Assert.NotNull(info);
        Assert.True(info.Width > 0);
        Assert.True(info.Height > 0);
        _output.WriteLine($"Identified image: {info.Width}x{info.Height}");
    }

    [Fact]
    public void Identify_WithJpegStream_ReturnsImageInfo()
    {
        // Arrange
        using var stream = ImageTestHelper.GetImageStream("test-image-01.jpg");

        // Act
        var info = Image.Identify(stream);

        // Assert
        Assert.NotNull(info);
        Assert.True(info.Width > 0);
        Assert.True(info.Height > 0);
        _output.WriteLine($"Identified image: {info.Width}x{info.Height}");
    }

    #endregion
}
