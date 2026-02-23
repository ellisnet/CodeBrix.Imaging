using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Formats.Bmp;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Png;
using CodeBrix.Imaging.PixelFormats;
using CodeBrix.Imaging.Tests.Helpers;
using System;
using Xunit;

namespace CodeBrix.Imaging.Tests.Advanced;

public class AdvancedImageExtensionsTests
{
    private readonly ITestOutputHelper _output;

    public AdvancedImageExtensionsTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    #region DetectEncoder Tests

    [Fact]
    public void DetectEncoder_WithPngExtension_ReturnsPngEncoder()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        var filePath = "test.png";

        // Act
        var encoder = image.DetectEncoder(filePath);

        // Assert
        Assert.NotNull(encoder);
        Assert.IsType<PngEncoder>(encoder);
        _output.WriteLine($"Detected encoder type: {encoder.GetType().Name}");
    }

    [Fact]
    public void DetectEncoder_WithJpgExtension_ReturnsJpegEncoder()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        var filePath = "test.jpg";

        // Act
        var encoder = image.DetectEncoder(filePath);

        // Assert
        Assert.NotNull(encoder);
        Assert.IsType<JpegEncoder>(encoder);
        _output.WriteLine($"Detected encoder type: {encoder.GetType().Name}");
    }

    [Fact]
    public void DetectEncoder_WithJpegExtension_ReturnsJpegEncoder()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        var filePath = "test.jpeg";

        // Act
        var encoder = image.DetectEncoder(filePath);

        // Assert
        Assert.NotNull(encoder);
        Assert.IsType<JpegEncoder>(encoder);
        _output.WriteLine($"Detected encoder type: {encoder.GetType().Name}");
    }

    [Fact]
    public void DetectEncoder_WithBmpExtension_ReturnsBmpEncoder()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        var filePath = "test.bmp";

        // Act
        var encoder = image.DetectEncoder(filePath);

        // Assert
        Assert.NotNull(encoder);
        Assert.IsType<BmpEncoder>(encoder);
        _output.WriteLine($"Detected encoder type: {encoder.GetType().Name}");
    }

    [Fact]
    public void DetectEncoder_WithUnsupportedExtension_ThrowsNotSupportedException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        var filePath = "test.xyz";

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() => image.DetectEncoder(filePath));
        _output.WriteLine($"Exception message: {exception.Message}");
        Assert.Contains("No encoder was found", exception.Message);
    }

    [Fact]
    public void DetectEncoder_WithNullPath_ThrowsArgumentNullException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => image.DetectEncoder(null));
    }

    #endregion

    #region AcceptVisitor Tests

    [Fact]
    public void AcceptVisitor_WithValidVisitor_InvokesVisitor()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        var visitor = new TestImageVisitor();

        // Act
        image.AcceptVisitor(visitor);

        // Assert
        Assert.True(visitor.WasVisited);
        _output.WriteLine("Visitor was successfully invoked");
    }

    [Fact]
    public void AcceptVisitor_WithEmbeddedResourceImage_InvokesVisitor()
    {
        // Arrange
        using var image = ImageTestHelper.LoadImage("test-image-01.png");
        var visitor = new TestImageVisitor();

        // Act
        image.AcceptVisitor(visitor);

        // Assert
        Assert.True(visitor.WasVisited);
        _output.WriteLine($"Visitor invoked on image with dimensions: {image.Width}x{image.Height}");
    }

    #endregion

    #region GetConfiguration Tests

    [Fact]
    public void GetConfiguration_OnImage_ReturnsConfiguration()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act
        var config = image.GetConfiguration();

        // Assert
        Assert.NotNull(config);
        _output.WriteLine($"Configuration retrieved successfully");
    }

    [Fact]
    public void GetConfiguration_OnImageFrame_ReturnsConfiguration()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        var frame = image.Frames[0];

        // Act
        var config = frame.GetConfiguration();

        // Assert
        Assert.NotNull(config);
        _output.WriteLine($"Configuration retrieved from frame successfully");
    }

    #endregion

    #region GetPixelMemoryGroup Tests

    [Fact]
    public void GetPixelMemoryGroup_OnImage_ReturnsMemoryGroup()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act
        var memoryGroup = image.GetPixelMemoryGroup();

        // Assert
        Assert.NotNull(memoryGroup);
        _output.WriteLine($"Memory group count: {memoryGroup.Count}");
    }

    [Fact]
    public void GetPixelMemoryGroup_OnImageFrame_ReturnsMemoryGroup()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        var frame = image.Frames.RootFrame;

        // Act
        var memoryGroup = frame.GetPixelMemoryGroup();

        // Assert
        Assert.NotNull(memoryGroup);
        _output.WriteLine($"Memory group count: {memoryGroup.Count}");
    }

    #endregion

    #region DangerousGetPixelRowMemory Tests

    [Fact]
    public void DangerousGetPixelRowMemory_OnImage_ReturnsMemory()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act
        var memory = image.DangerousGetPixelRowMemory(0);

        // Assert
        Assert.Equal(10, memory.Length);
        _output.WriteLine($"Row memory length: {memory.Length}");
    }

    [Fact]
    public void DangerousGetPixelRowMemory_OnImageFrame_ReturnsMemory()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);
        var frame = image.Frames.RootFrame;

        // Act
        var memory = frame.DangerousGetPixelRowMemory(5);

        // Assert
        Assert.Equal(10, memory.Length);
        _output.WriteLine($"Row memory length at row 5: {memory.Length}");
    }

    [Fact]
    public void DangerousGetPixelRowMemory_WithNegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => image.DangerousGetPixelRowMemory(-1));
    }

    [Fact]
    public void DangerousGetPixelRowMemory_WithIndexOutOfBounds_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => image.DangerousGetPixelRowMemory(10));
    }

    #endregion

    private class TestImageVisitor : IImageVisitor
    {
        public bool WasVisited { get; private set; }

        public void Visit<TPixel>(Image<TPixel> image)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            WasVisited = true;
        }
    }
}
