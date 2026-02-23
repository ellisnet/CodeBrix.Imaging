using CodeBrix.Imaging.Formats;
using CodeBrix.Imaging.Formats.Bmp;
using CodeBrix.Imaging.Formats.Gif;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Png;
using CodeBrix.Imaging.Formats.Tga;
using CodeBrix.Imaging.Formats.Tiff;
using CodeBrix.Imaging.Formats.Webp;
using CodeBrix.Imaging.PixelFormats;
using CodeBrix.Imaging.Tests.Helpers;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Imaging.Tests.Core;

/// <summary>
/// Tests that validate the behavior of code affected by identified security vulnerabilities.
/// These tests establish baseline functionality so that security fixes can be verified
/// without breaking existing behavior.
/// 
/// Vulnerability areas covered:
/// 1. LoadPixelData - integer overflow in width * height calculation
/// 2. Image dimension validation - no upper bounds on width/height
/// 3. File path handling - LocalFileSystem used by Image.Load/Save
/// 4. Deflate compression - unsafe code in DeflaterEngine (tested via PNG roundtrip)
/// </summary>
public class ImageSecurityTests
{
    private readonly ITestOutputHelper _output;

    public ImageSecurityTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    #region LoadPixelData - Normal Operation Tests

    [Fact]
    public void LoadPixelData_WithRgba32Array_CreatesImageWithCorrectDimensions()
    {
        // Arrange
        int width = 4;
        int height = 3;
        var pixels = new Rgba32[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32(255, 0, 0, 255); // Red
        }

        // Act
        using var image = Image.LoadPixelData<Rgba32>(pixels, width, height, PngFormat.Instance);

        // Assert
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        _output.WriteLine($"Created image from pixel data: {image.Width}x{image.Height}");
    }

    [Fact]
    public void LoadPixelData_WithRgba32Array_PreservesPixelValues()
    {
        // Arrange
        int width = 3;
        int height = 2;
        var pixels = new Rgba32[width * height];
        pixels[0] = new Rgba32(255, 0, 0, 255);   // Red
        pixels[1] = new Rgba32(0, 255, 0, 255);   // Green
        pixels[2] = new Rgba32(0, 0, 255, 255);   // Blue
        pixels[3] = new Rgba32(255, 255, 0, 255); // Yellow
        pixels[4] = new Rgba32(255, 0, 255, 255); // Magenta
        pixels[5] = new Rgba32(0, 255, 255, 255); // Cyan

        // Act
        using var image = Image.LoadPixelData<Rgba32>(pixels, width, height, PngFormat.Instance);

        // Assert - verify pixel values are preserved
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[0, 0]);
        Assert.Equal(new Rgba32(0, 255, 0, 255), image[1, 0]);
        Assert.Equal(new Rgba32(0, 0, 255, 255), image[2, 0]);
        Assert.Equal(new Rgba32(255, 255, 0, 255), image[0, 1]);
        Assert.Equal(new Rgba32(255, 0, 255, 255), image[1, 1]);
        Assert.Equal(new Rgba32(0, 255, 255, 255), image[2, 1]);
        _output.WriteLine("All 6 pixel values preserved correctly");
    }

    [Fact]
    public void LoadPixelData_WithByteArray_CreatesImageWithCorrectDimensions()
    {
        // Arrange - Rgba32 is 4 bytes per pixel
        int width = 5;
        int height = 4;
        int bytesPerPixel = 4; // Rgba32
        var data = new byte[width * height * bytesPerPixel];

        // Fill with a pattern: each pixel gets (R=x, G=y, B=128, A=255)
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * bytesPerPixel;
                data[offset + 0] = (byte)(x * 50);  // R
                data[offset + 1] = (byte)(y * 80);  // G
                data[offset + 2] = 128;              // B
                data[offset + 3] = 255;              // A
            }
        }

        // Act
        using var image = Image.LoadPixelData<Rgba32>(data, width, height, BmpFormat.Instance);

        // Assert
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        _output.WriteLine($"Created image from byte data: {image.Width}x{image.Height}");
    }

    [Fact]
    public void LoadPixelData_WithByteArray_PreservesPixelValues()
    {
        // Arrange - Rgba32 is 4 bytes per pixel (R, G, B, A order in memory)
        int width = 2;
        int height = 2;
        int bytesPerPixel = 4;
        var data = new byte[width * height * bytesPerPixel];

        // Pixel (0,0) = Red
        data[0] = 255; data[1] = 0; data[2] = 0; data[3] = 255;
        // Pixel (1,0) = Green
        data[4] = 0; data[5] = 255; data[6] = 0; data[7] = 255;
        // Pixel (0,1) = Blue
        data[8] = 0; data[9] = 0; data[10] = 255; data[11] = 255;
        // Pixel (1,1) = White
        data[12] = 255; data[13] = 255; data[14] = 255; data[15] = 255;

        // Act
        using var image = Image.LoadPixelData<Rgba32>(data, width, height, PngFormat.Instance);

        // Assert
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[0, 0]);
        Assert.Equal(new Rgba32(0, 255, 0, 255), image[1, 0]);
        Assert.Equal(new Rgba32(0, 0, 255, 255), image[0, 1]);
        Assert.Equal(new Rgba32(255, 255, 255, 255), image[1, 1]);
        _output.WriteLine("All pixel values preserved from byte array");
    }

    [Fact]
    public void LoadPixelData_WithReadOnlySpan_CreatesImageWithCorrectDimensions()
    {
        // Arrange
        int width = 3;
        int height = 3;
        var pixels = new Rgba32[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32(100, 150, 200, 255);
        }

        ReadOnlySpan<Rgba32> span = new ReadOnlySpan<Rgba32>(pixels);

        // Act
        using var image = Image.LoadPixelData<Rgba32>(span, width, height, PngFormat.Instance);

        // Assert
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(new Rgba32(100, 150, 200, 255), image[1, 1]);
        _output.WriteLine($"Created image from ReadOnlySpan<Rgba32>: {image.Width}x{image.Height}");
    }

    [Fact]
    public void LoadPixelData_WithConfiguration_CreatesImageWithCorrectDimensions()
    {
        // Arrange
        int width = 6;
        int height = 4;
        var config = Configuration.Default;
        var pixels = new Rgba32[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32(50, 100, 150, 200);
        }

        // Act
        using var image = Image.LoadPixelData<Rgba32>(config, pixels, width, height, PngFormat.Instance);

        // Assert
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(new Rgba32(50, 100, 150, 200), image[0, 0]);
        _output.WriteLine($"Created image with configuration: {image.Width}x{image.Height}");
    }

    [Fact]
    public void LoadPixelData_WithExtraData_UsesOnlyRequiredPixels()
    {
        // Arrange - provide more data than needed
        int width = 2;
        int height = 2;
        var pixels = new Rgba32[width * height + 10]; // Extra pixels
        pixels[0] = new Rgba32(255, 0, 0, 255);
        pixels[1] = new Rgba32(0, 255, 0, 255);
        pixels[2] = new Rgba32(0, 0, 255, 255);
        pixels[3] = new Rgba32(255, 255, 255, 255);
        // Extra pixels should be ignored
        for (int i = 4; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32(99, 99, 99, 99);
        }

        // Act
        using var image = Image.LoadPixelData<Rgba32>(pixels, width, height, PngFormat.Instance);

        // Assert
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[0, 0]);
        Assert.Equal(new Rgba32(0, 255, 0, 255), image[1, 0]);
        Assert.Equal(new Rgba32(0, 0, 255, 255), image[0, 1]);
        Assert.Equal(new Rgba32(255, 255, 255, 255), image[1, 1]);
        _output.WriteLine("Extra pixel data correctly ignored");
    }

    [Fact]
    public void LoadPixelData_WithSinglePixel_CreatesOneByOneImage()
    {
        // Arrange
        var pixels = new Rgba32[] { new Rgba32(42, 84, 126, 255) };

        // Act
        using var image = Image.LoadPixelData<Rgba32>(pixels, 1, 1, BmpFormat.Instance);

        // Assert
        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(new Rgba32(42, 84, 126, 255), image[0, 0]);
        _output.WriteLine("1x1 image created successfully from pixel data");
    }

    [Fact]
    public void LoadPixelData_WithBgra32_PreservesPixelValues()
    {
        // Arrange
        int width = 2;
        int height = 2;
        var pixels = new Bgra32[width * height];
        pixels[0] = new Bgra32(255, 0, 0, 255);   // Red
        pixels[1] = new Bgra32(0, 255, 0, 255);   // Green
        pixels[2] = new Bgra32(0, 0, 255, 255);   // Blue
        pixels[3] = new Bgra32(128, 128, 128, 255); // Gray

        // Act
        using var image = Image.LoadPixelData<Bgra32>(pixels, width, height, PngFormat.Instance);

        // Assert
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(new Bgra32(255, 0, 0, 255), image[0, 0]);
        Assert.Equal(new Bgra32(0, 255, 0, 255), image[1, 0]);
        Assert.Equal(new Bgra32(0, 0, 255, 255), image[0, 1]);
        Assert.Equal(new Bgra32(128, 128, 128, 255), image[1, 1]);
        _output.WriteLine("Bgra32 pixel values preserved correctly");
    }

    [Fact]
    public void LoadPixelData_WithModerateDimensions_CreatesImageSuccessfully()
    {
        // Arrange - moderate size that exercises the width * height calculation
        int width = 1000;
        int height = 1000;
        var pixels = new Rgba32[width * height];
        var cornerPixel = new Rgba32(200, 100, 50, 255);
        pixels[0] = cornerPixel;
        pixels[width * height - 1] = new Rgba32(10, 20, 30, 255);

        // Act
        using var image = Image.LoadPixelData<Rgba32>(pixels, width, height, PngFormat.Instance);

        // Assert
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(cornerPixel, image[0, 0]);
        Assert.Equal(new Rgba32(10, 20, 30, 255), image[width - 1, height - 1]);
        _output.WriteLine($"Created {width}x{height} image ({width * height} pixels) successfully");
    }

    [Fact]
    public void LoadPixelData_WithInsufficientData_ThrowsArgumentException()
    {
        // Arrange - provide fewer pixels than width * height
        int width = 10;
        int height = 10;
        var pixels = new Rgba32[50]; // Only 50 pixels, need 100

        // Act & Assert
        var ex = Assert.ThrowsAny<ArgumentException>(
            () => Image.LoadPixelData<Rgba32>(pixels, width, height, PngFormat.Instance));
        _output.WriteLine($"Exception for insufficient data: {ex.Message}");
    }

    [Fact]
    public void LoadPixelData_SetsExpectedFormat()
    {
        // Arrange
        int width = 2;
        int height = 2;
        var pixels = new Rgba32[width * height];

        // Act - test with different formats
        using var pngImage = Image.LoadPixelData<Rgba32>(pixels, width, height, PngFormat.Instance);
        using var bmpImage = Image.LoadPixelData<Rgba32>(pixels, width, height, BmpFormat.Instance);
        using var jpegImage = Image.LoadPixelData<Rgba32>(pixels, width, height, JpegFormat.Instance);

        // Assert
        Assert.Equal(PngFormat.FormatName, pngImage.Format.Name);
        Assert.Equal(BmpFormat.FormatName, bmpImage.Format.Name);
        Assert.Equal(JpegFormat.FormatName, jpegImage.Format.Name);
        _output.WriteLine("ExpectedFormat correctly set for PNG, BMP, and JPEG");
    }

    #endregion

    #region Image Dimension Validation Tests

    [Fact]
    public void Constructor_WithZeroWidth_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => new Image<Rgba32>(0, 10));
        _output.WriteLine("Zero width correctly rejected");
    }

    [Fact]
    public void Constructor_WithZeroHeight_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => new Image<Rgba32>(10, 0));
        _output.WriteLine("Zero height correctly rejected");
    }

    [Fact]
    public void Constructor_WithNegativeWidth_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => new Image<Rgba32>(-1, 10));
        _output.WriteLine("Negative width correctly rejected");
    }

    [Fact]
    public void Constructor_WithNegativeHeight_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => new Image<Rgba32>(10, -1));
        _output.WriteLine("Negative height correctly rejected");
    }

    [Fact]
    public void Constructor_WithBothDimensionsZero_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => new Image<Rgba32>(0, 0));
        _output.WriteLine("Both dimensions zero correctly rejected");
    }

    [Fact]
    public void Constructor_WithBothDimensionsNegative_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => new Image<Rgba32>(-5, -5));
        _output.WriteLine("Both dimensions negative correctly rejected");
    }

    [Fact]
    public void Constructor_WithMinimumDimensions_CreatesImage()
    {
        // Arrange & Act - 1x1 is the minimum valid image
        using var image = new Image<Rgba32>(1, 1);

        // Assert
        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
        _output.WriteLine("1x1 image created successfully");
    }

    [Fact]
    public void Constructor_WithConfiguration_ZeroWidth_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => new Image<Rgba32>(Configuration.Default, 0, 10));
        _output.WriteLine("Zero width with configuration correctly rejected");
    }

    [Fact]
    public void Constructor_WithConfiguration_ZeroHeight_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => new Image<Rgba32>(Configuration.Default, 10, 0));
        _output.WriteLine("Zero height with configuration correctly rejected");
    }

    [Fact]
    public void Constructor_WithBackgroundColor_ZeroWidth_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(
            () => new Image<Rgba32>(0, 10, new Rgba32(255, 0, 0, 255)));
        _output.WriteLine("Zero width with background color correctly rejected");
    }

    [Fact]
    public void Constructor_WithBackgroundColor_ZeroHeight_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(
            () => new Image<Rgba32>(10, 0, new Rgba32(255, 0, 0, 255)));
        _output.WriteLine("Zero height with background color correctly rejected");
    }

    [Fact]
    public void Constructor_WithSmallValidDimensions_PreservesPixelData()
    {
        // Arrange
        var backgroundColor = new Rgba32(100, 150, 200, 255);

        // Act
        using var image = new Image<Rgba32>(3, 3, backgroundColor);

        // Assert - all pixels should have the background color
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Assert.Equal(backgroundColor, image[x, y]);
            }
        }

        _output.WriteLine("3x3 image with background color verified pixel-by-pixel");
    }

    [Fact]
    public void LoadPixelData_WithZeroWidth_ThrowsArgumentException()
    {
        // Arrange
        var pixels = new Rgba32[10];

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(
            () => Image.LoadPixelData<Rgba32>(pixels, 0, 10, PngFormat.Instance));
        _output.WriteLine("LoadPixelData with zero width correctly rejected");
    }

    [Fact]
    public void LoadPixelData_WithZeroHeight_ThrowsArgumentException()
    {
        // Arrange
        var pixels = new Rgba32[10];

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(
            () => Image.LoadPixelData<Rgba32>(pixels, 10, 0, PngFormat.Instance));
        _output.WriteLine("LoadPixelData with zero height correctly rejected");
    }

    [Fact]
    public void LoadPixelData_WithNegativeWidth_ThrowsArgumentException()
    {
        // Arrange
        var pixels = new Rgba32[10];

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(
            () => Image.LoadPixelData<Rgba32>(pixels, -1, 10, PngFormat.Instance));
        _output.WriteLine("LoadPixelData with negative width correctly rejected");
    }

    [Fact]
    public void LoadPixelData_WithNegativeHeight_ThrowsArgumentException()
    {
        // Arrange
        var pixels = new Rgba32[10];

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(
            () => Image.LoadPixelData<Rgba32>(pixels, 10, -1, PngFormat.Instance));
        _output.WriteLine("LoadPixelData with negative height correctly rejected");
    }

    #endregion

    #region File Path Load/Save Roundtrip Tests

    [Fact]
    public void Save_and_Load_WithFilePath_RoundtripsCorrectly_Png()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{PngFormat.FormatDefaultExtension}");

        try
        {
            using var original = new Image<Rgba32>(20, 15, new Rgba32(100, 150, 200, 255));

            // Act - save to file path
            original.Save(tempFile, new PngEncoder());
            Assert.True(File.Exists(tempFile), "File should exist after save");

            // Load back from file path
            using var loaded = Image.Load(tempFile);

            // Assert
            Assert.Equal(original.Width, loaded.Width);
            Assert.Equal(original.Height, loaded.Height);
            _output.WriteLine($"PNG roundtrip: {original.Width}x{original.Height}, file size: {new FileInfo(tempFile).Length} bytes");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Save_and_Load_WithFilePath_RoundtripsCorrectly_Jpeg()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{JpegFormat.FormatDefaultExtension}");

        try
        {
            using var original = new Image<Rgba32>(20, 15, new Rgba32(100, 150, 200, 255));

            // Act - save to file path
            original.Save(tempFile, new JpegEncoder());
            Assert.True(File.Exists(tempFile), "File should exist after save");

            // Load back from file path
            using var loaded = Image.Load(tempFile);

            // Assert - dimensions should match (pixel values may differ slightly due to JPEG compression)
            Assert.Equal(original.Width, loaded.Width);
            Assert.Equal(original.Height, loaded.Height);
            _output.WriteLine($"JPEG roundtrip: {original.Width}x{original.Height}, file size: {new FileInfo(tempFile).Length} bytes");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Save_and_Load_WithFilePath_RoundtripsCorrectly_Bmp()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{BmpFormat.FormatDefaultExtension}");

        try
        {
            using var original = new Image<Rgba32>(20, 15, new Rgba32(100, 150, 200, 255));

            // Act - save to file path
            original.Save(tempFile, new BmpEncoder());
            Assert.True(File.Exists(tempFile), "File should exist after save");

            // Load back from file path
            using var loaded = Image.Load(tempFile);

            // Assert
            Assert.Equal(original.Width, loaded.Width);
            Assert.Equal(original.Height, loaded.Height);
            _output.WriteLine($"BMP roundtrip: {original.Width}x{original.Height}, file size: {new FileInfo(tempFile).Length} bytes");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_WithFilePath_RoundtripsCorrectly()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{PngFormat.FormatDefaultExtension}");

        try
        {
            using var original = new Image<Rgba32>(25, 20, new Rgba32(50, 100, 200, 255));

            // Act - save async to file path
            await original.SaveAsync(tempFile, new PngEncoder(), CancellationToken.None);
            Assert.True(File.Exists(tempFile), "File should exist after async save");

            // Load async from file path
            using var loaded = await Image.LoadAsync(tempFile, CancellationToken.None);

            // Assert
            Assert.Equal(original.Width, loaded.Width);
            Assert.Equal(original.Height, loaded.Height);
            _output.WriteLine($"Async PNG roundtrip: {original.Width}x{original.Height}, file size: {new FileInfo(tempFile).Length} bytes");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_does_not_exist.png");

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => Image.Load(nonExistentPath));
        _output.WriteLine("Loading non-existent file correctly throws FileNotFoundException");
    }

    [Fact]
    public void Load_WithNullPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Image.Load((string)null));
        _output.WriteLine("Loading with null path correctly throws ArgumentNullException");
    }

    [Fact]
    public void Save_WithNullPath_ThrowsArgumentNullException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => image.Save((string)null, new PngEncoder()));
        _output.WriteLine("Saving with null path correctly throws ArgumentNullException");
    }

    [Fact]
    public void DetectFormat_WithFilePath_DetectsCorrectly()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{PngFormat.FormatDefaultExtension}");

        try
        {
            using var image = new Image<Rgba32>(5, 5);
            image.Save(tempFile, new PngEncoder());

            // Act
            var format = Image.DetectFormat(tempFile);

            // Assert
            Assert.NotNull(format);
            Assert.Equal(PngFormat.FormatName, format.Name);
            _output.WriteLine($"Format detected from file path: {format.Name}");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Identify_WithFilePath_ReturnsCorrectInfo()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{PngFormat.FormatDefaultExtension}");

        try
        {
            using var image = new Image<Rgba32>(30, 25);
            image.Save(tempFile, new PngEncoder());

            // Act
            var info = Image.Identify(tempFile);

            // Assert
            Assert.NotNull(info);
            Assert.Equal(30, info.Width);
            Assert.Equal(25, info.Height);
            _output.WriteLine($"Identified from file path: {info.Width}x{info.Height}");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    #endregion

    #region PNG Deflate Roundtrip Integrity Tests

    [Fact]
    public void PngRoundtrip_PreservesExactPixelValues_SolidColor()
    {
        // Arrange - create an image with a known solid color
        int width = 50;
        int height = 50;
        var expectedColor = new Rgba32(42, 84, 126, 255);
        using var original = new Image<Rgba32>(width, height, expectedColor);

        // Act - encode to PNG (uses deflate) and decode back
        using var stream = new MemoryStream();
        original.Save(stream, new PngEncoder());
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert - every pixel must match exactly
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(expectedColor, decoded[x, y]);
            }
        }

        _output.WriteLine($"PNG roundtrip: all {width * height} pixels match (solid color)");
    }

    [Fact]
    public void PngRoundtrip_PreservesExactPixelValues_Gradient()
    {
        // Arrange - create a gradient image with unique pixel values
        int width = 100;
        int height = 50;
        using var original = new Image<Rgba32>(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                original[x, y] = new Rgba32(
                    (byte)(x * 255 / (width - 1)),
                    (byte)(y * 255 / (height - 1)),
                    128,
                    255);
            }
        }

        // Act - encode to PNG (uses deflate) and decode back
        using var stream = new MemoryStream();
        original.Save(stream, new PngEncoder());
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert - every pixel must match exactly (PNG is lossless)
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var expected = original[x, y];
                var actual = decoded[x, y];
                Assert.Equal(expected, actual);
            }
        }

        _output.WriteLine($"PNG roundtrip: all {width * height} gradient pixels match exactly");
        _output.WriteLine($"PNG encoded size: {stream.Length} bytes");
    }

    [Fact]
    public void PngRoundtrip_PreservesExactPixelValues_RandomPattern()
    {
        // Arrange - create an image with a pseudo-random pattern to stress the deflate engine
        int width = 64;
        int height = 64;
        using var original = new Image<Rgba32>(width, height);
        var rng = new Random(12345); // Fixed seed for reproducibility

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                original[x, y] = new Rgba32(
                    (byte)rng.Next(256),
                    (byte)rng.Next(256),
                    (byte)rng.Next(256),
                    255);
            }
        }

        // Act - encode to PNG (uses deflate) and decode back
        using var stream = new MemoryStream();
        original.Save(stream, new PngEncoder());
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert - every pixel must match exactly (PNG is lossless)
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);

        var rng2 = new Random(12345); // Same seed for verification
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var expected = new Rgba32(
                    (byte)rng2.Next(256),
                    (byte)rng2.Next(256),
                    (byte)rng2.Next(256),
                    255);
                Assert.Equal(expected, decoded[x, y]);
            }
        }

        _output.WriteLine($"PNG roundtrip: all {width * height} random pixels match exactly");
        _output.WriteLine($"PNG encoded size: {stream.Length} bytes");
    }

    [Fact]
    public void PngRoundtrip_PreservesTransparency()
    {
        // Arrange - create an image with varying alpha values
        int width = 20;
        int height = 20;
        using var original = new Image<Rgba32>(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte alpha = (byte)(x * 255 / (width - 1));
                original[x, y] = new Rgba32(200, 100, 50, alpha);
            }
        }

        // Act
        using var stream = new MemoryStream();
        original.Save(stream, new PngEncoder());
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(original[x, y], decoded[x, y]);
            }
        }

        _output.WriteLine($"PNG roundtrip: transparency preserved for all {width * height} pixels");
    }

    [Fact]
    public void PngRoundtrip_LargerImage_PreservesPixelIntegrity()
    {
        // Arrange - larger image to exercise deflate with multiple compression blocks
        int width = 256;
        int height = 256;
        using var original = new Image<Rgba32>(width, height);

        // Create a checkerboard pattern
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isWhite = ((x / 8) + (y / 8)) % 2 == 0;
                original[x, y] = isWhite
                    ? new Rgba32(255, 255, 255, 255)
                    : new Rgba32(0, 0, 0, 255);
            }
        }

        // Act
        using var stream = new MemoryStream();
        original.Save(stream, new PngEncoder());
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(original[x, y], decoded[x, y]);
            }
        }

        _output.WriteLine($"PNG roundtrip: {width}x{height} checkerboard ({width * height} pixels) preserved exactly");
        _output.WriteLine($"PNG encoded size: {stream.Length} bytes");
    }

    [Fact]
    public void PngRoundtrip_FromEmbeddedResource_MaintainsPixelIntegrity()
    {
        // Arrange - load a real PNG image
        using var original = ImageTestHelper.LoadImage("test-image-01.png");
        using var originalTyped = original.CloneAs<Rgba32>();

        // Act - re-encode and decode
        using var stream = new MemoryStream();
        originalTyped.Save(stream, new PngEncoder());
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert - dimensions must match
        Assert.Equal(originalTyped.Width, decoded.Width);
        Assert.Equal(originalTyped.Height, decoded.Height);

        // Verify pixel-by-pixel that re-encoding a PNG preserves the data
        int mismatchCount = 0;
        for (int y = 0; y < decoded.Height; y++)
        {
            for (int x = 0; x < decoded.Width; x++)
            {
                if (!originalTyped[x, y].Equals(decoded[x, y]))
                {
                    mismatchCount++;
                }
            }
        }

        Assert.Equal(0, mismatchCount);
        _output.WriteLine($"PNG re-encode roundtrip: {decoded.Width}x{decoded.Height}, " +
            $"{decoded.Width * decoded.Height} pixels, 0 mismatches");
        _output.WriteLine($"Re-encoded PNG size: {stream.Length} bytes");
    }

    #endregion

    #region Multi-Format Roundtrip Tests (Encoding Pipeline Integrity)

    [Theory]
    [InlineData("PNG")]
    [InlineData("BMP")]
    [InlineData("TGA")]
    [InlineData("TIFF")]
    public void LosslessFormat_Roundtrip_PreservesPixelValues(string formatName)
    {
        // Arrange - create a small image with known pixel values
        int width = 10;
        int height = 10;
        using var original = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                original[x, y] = new Rgba32(
                    (byte)(x * 25),
                    (byte)(y * 25),
                    128,
                    255);
            }
        }

        // Select encoder based on format
        IImageEncoder encoder = formatName switch
        {
            PngFormat.FormatName => new PngEncoder(),
            BmpFormat.FormatName => new BmpEncoder(),
            TgaFormat.FormatName => new TgaEncoder(),
            TiffFormat.FormatName => new TiffEncoder(),
            _ => throw new ArgumentException($"Unexpected format: {formatName}")
        };

        // Act - encode and decode
        using var stream = new MemoryStream();
        original.Save(stream, encoder);
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert - lossless formats should preserve pixel values exactly
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(original[x, y], decoded[x, y]);
            }
        }

        _output.WriteLine($"{formatName} roundtrip: all pixels match, stream size: {stream.Length} bytes");
    }

    [Fact]
    public void GifRoundtrip_PreservesImageDimensions()
    {
        // Arrange
        int width = 20;
        int height = 15;
        using var original = new Image<Rgba32>(width, height, new Rgba32(100, 100, 100, 255));

        // Act
        using var stream = new MemoryStream();
        original.Save(stream, new GifEncoder());
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert - GIF may quantize colors, but dimensions should be preserved
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        _output.WriteLine($"GIF roundtrip: dimensions preserved ({width}x{height}), stream size: {stream.Length} bytes");
    }

    [Fact]
    public void JpegRoundtrip_PreservesImageDimensions()
    {
        // Arrange
        int width = 30;
        int height = 20;
        using var original = new Image<Rgba32>(width, height, new Rgba32(100, 150, 200, 255));

        // Act
        using var stream = new MemoryStream();
        original.Save(stream, new JpegEncoder { Quality = 100 });
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert - JPEG is lossy so only check dimensions
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        _output.WriteLine($"JPEG roundtrip: dimensions preserved ({width}x{height}), stream size: {stream.Length} bytes");
    }

    [Fact]
    public void WebpRoundtrip_PreservesImageDimensions()
    {
        // Arrange
        int width = 25;
        int height = 20;
        using var original = new Image<Rgba32>(width, height, new Rgba32(80, 120, 160, 255));

        // Act
        using var stream = new MemoryStream();
        original.Save(stream, new WebpEncoder());
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        _output.WriteLine($"WebP roundtrip: dimensions preserved ({width}x{height}), stream size: {stream.Length} bytes");
    }

    #endregion

    #region LoadPixelData with Save/Load Roundtrip Tests

    [Fact]
    public void LoadPixelData_then_PngSave_then_Load_PreservesPixels()
    {
        // Arrange - create image from raw pixel data
        int width = 8;
        int height = 8;
        var pixels = new Rgba32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = new Rgba32(
                    (byte)(x * 32),
                    (byte)(y * 32),
                    (byte)((x + y) * 16),
                    255);
            }
        }

        // Act - create from pixel data, save as PNG, reload
        using var image = Image.LoadPixelData<Rgba32>(pixels, width, height, PngFormat.Instance);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert - full pipeline: raw pixels -> Image -> PNG encode (deflate) -> decode -> verify
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var expected = pixels[y * width + x];
                Assert.Equal(expected, decoded[x, y]);
            }
        }

        _output.WriteLine($"Full pipeline roundtrip: {width}x{height} pixels preserved through LoadPixelData -> PNG -> Load");
    }

    [Fact]
    public void LoadPixelData_then_FileSave_then_FileLoad_PreservesPixels()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{PngFormat.FormatDefaultExtension}");

        try
        {
            int width = 5;
            int height = 5;
            var pixels = new Rgba32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Rgba32((byte)(i * 10), (byte)(i * 5), (byte)(255 - i * 10), 255);
            }

            // Act - full file I/O pipeline
            using var image = Image.LoadPixelData<Rgba32>(pixels, width, height, PngFormat.Instance);
            image.Save(tempFile, new PngEncoder());

            using var loaded = Image.Load<Rgba32>(tempFile);

            // Assert
            Assert.Equal(width, loaded.Width);
            Assert.Equal(height, loaded.Height);

            for (int i = 0; i < pixels.Length; i++)
            {
                int x = i % width;
                int y = i / width;
                Assert.Equal(pixels[i], loaded[x, y]);
            }

            _output.WriteLine($"Full file I/O pipeline roundtrip: {width}x{height} pixels preserved");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    #endregion

    #region Pixel Access Bounds Validation

    [Fact]
    public void PixelAccess_OutOfBoundsX_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = image[10, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = image[-1, 0]);
        _output.WriteLine("Out-of-bounds X pixel access correctly rejected");
    }

    [Fact]
    public void PixelAccess_OutOfBoundsY_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var image = new Image<Rgba32>(10, 10);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = image[0, 10]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = image[0, -1]);
        _output.WriteLine("Out-of-bounds Y pixel access correctly rejected");
    }

    [Fact]
    public void PixelAccess_AtMaxValidIndex_Succeeds()
    {
        // Arrange
        int width = 15;
        int height = 20;
        using var image = new Image<Rgba32>(width, height, new Rgba32(42, 42, 42, 255));

        // Act & Assert - accessing last valid pixel should work
        var pixel = image[width - 1, height - 1];
        Assert.Equal(new Rgba32(42, 42, 42, 255), pixel);
        _output.WriteLine($"Max valid index ({width - 1}, {height - 1}) accessed successfully");
    }

    #endregion
}
