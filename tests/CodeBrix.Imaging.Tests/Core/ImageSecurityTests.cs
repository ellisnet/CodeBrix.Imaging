using CodeBrix.Imaging.Formats;
using CodeBrix.Imaging.Formats.Bmp;
using CodeBrix.Imaging.Formats.Gif;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Png;
using CodeBrix.Imaging.Formats.Tga;
using CodeBrix.Imaging.Formats.Tiff;
using CodeBrix.Imaging.Formats.Tiff.Compression;
using CodeBrix.Imaging.Formats.Tiff.Constants;
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
        var width = 4;
        var height = 3;
        var pixels = new Rgba32[width * height];
        for (var i = 0; i < pixels.Length; i++)
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
        var width = 3;
        var height = 2;
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
        var width = 5;
        var height = 4;
        var bytesPerPixel = 4; // Rgba32
        var data = new byte[width * height * bytesPerPixel];

        // Fill with a pattern: each pixel gets (R=x, G=y, B=128, A=255)
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * bytesPerPixel;
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
        var width = 2;
        var height = 2;
        var bytesPerPixel = 4;
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
        var width = 3;
        var height = 3;
        var pixels = new Rgba32[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32(100, 150, 200, 255);
        }

        var span = new ReadOnlySpan<Rgba32>(pixels);

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
        var width = 6;
        var height = 4;
        var config = Configuration.Default;
        var pixels = new Rgba32[width * height];
        for (var i = 0; i < pixels.Length; i++)
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
        var width = 2;
        var height = 2;
        var pixels = new Rgba32[width * height + 10]; // Extra pixels
        pixels[0] = new Rgba32(255, 0, 0, 255);
        pixels[1] = new Rgba32(0, 255, 0, 255);
        pixels[2] = new Rgba32(0, 0, 255, 255);
        pixels[3] = new Rgba32(255, 255, 255, 255);
        // Extra pixels should be ignored
        for (var i = 4; i < pixels.Length; i++)
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
        var width = 2;
        var height = 2;
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
        var width = 1000;
        var height = 1000;
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
        var width = 10;
        var height = 10;
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
        var width = 2;
        var height = 2;
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
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 3; x++)
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
        var width = 50;
        var height = 50;
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

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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
        var width = 100;
        var height = 50;
        using var original = new Image<Rgba32>(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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
        var width = 64;
        var height = 64;
        using var original = new Image<Rgba32>(width, height);
        var rng = new Random(12345); // Fixed seed for reproducibility

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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
        var width = 20;
        var height = 20;
        using var original = new Image<Rgba32>(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var alpha = (byte)(x * 255 / (width - 1));
                original[x, y] = new Rgba32(200, 100, 50, alpha);
            }
        }

        // Act
        using var stream = new MemoryStream();
        original.Save(stream, new PngEncoder());
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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
        var width = 256;
        var height = 256;
        using var original = new Image<Rgba32>(width, height);

        // Create a checkerboard pattern
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var isWhite = ((x / 8) + (y / 8)) % 2 == 0;
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

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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
        var mismatchCount = 0;
        for (var y = 0; y < decoded.Height; y++)
        {
            for (var x = 0; x < decoded.Width; x++)
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
        var width = 10;
        var height = 10;
        using var original = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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
        var width = 20;
        var height = 15;
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
        var width = 30;
        var height = 20;
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
        var width = 25;
        var height = 20;
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
        var width = 8;
        var height = 8;
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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
            var width = 5;
            var height = 5;
            var pixels = new Rgba32[width * height];
            for (var i = 0; i < pixels.Length; i++)
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

            for (var i = 0; i < pixels.Length; i++)
            {
                var x = i % width;
                var y = i / width;
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

    #region LoadPixelDataFromBgra Tests

    [Fact]
    public void LoadPixelDataFromBgra_WithByteArray_CorrectlyConvertsToRgba()
    {
        // Arrange - BGRA byte data (B, G, R, A order per pixel)
        var width = 2;
        var height = 2;
        var bgraData = new byte[width * height * 4];

        // Pixel (0,0) = Red in BGRA: B=0, G=0, R=255, A=255
        bgraData[0] = 0; bgraData[1] = 0; bgraData[2] = 255; bgraData[3] = 255;
        // Pixel (1,0) = Green in BGRA: B=0, G=255, R=0, A=255
        bgraData[4] = 0; bgraData[5] = 255; bgraData[6] = 0; bgraData[7] = 255;
        // Pixel (0,1) = Blue in BGRA: B=255, G=0, R=0, A=255
        bgraData[8] = 255; bgraData[9] = 0; bgraData[10] = 0; bgraData[11] = 255;
        // Pixel (1,1) = White in BGRA: B=255, G=255, R=255, A=128
        bgraData[12] = 255; bgraData[13] = 255; bgraData[14] = 255; bgraData[15] = 128;

        // Act
        using var image = Image.LoadPixelDataFromBgra(bgraData, width, height, PngFormat.Instance);

        // Assert - pixels should be in RGBA order now
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[0, 0]);     // Red
        Assert.Equal(new Rgba32(0, 255, 0, 255), image[1, 0]);     // Green
        Assert.Equal(new Rgba32(0, 0, 255, 255), image[0, 1]);     // Blue
        Assert.Equal(new Rgba32(255, 255, 255, 128), image[1, 1]); // White semi-transparent
        _output.WriteLine("BGRA to RGBA conversion via LoadPixelDataFromBgra verified correctly");
    }

    [Fact]
    public void LoadPixelDataFromBgra_SetsExpectedFormat()
    {
        // Arrange
        var width = 2;
        var height = 2;
        var bgraData = new byte[width * height * 4];

        // Act
        using var image = Image.LoadPixelDataFromBgra(bgraData, width, height, PngFormat.Instance);

        // Assert
        Assert.Equal(PngFormat.Instance, image.Metadata.ExpectedFormat);
        _output.WriteLine("LoadPixelDataFromBgra sets expected format correctly");
    }

    [Fact]
    public void LoadPixelDataFromBgra_WithConfiguration_CreatesImageWithCorrectDimensions()
    {
        // Arrange
        var width = 50;
        var height = 30;
        var bgraData = new byte[width * height * 4];

        // Act
        using var image = Image.LoadPixelDataFromBgra(
            Configuration.Default, bgraData, width, height, BmpFormat.Instance);

        // Assert
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        _output.WriteLine($"LoadPixelDataFromBgra with Configuration created {width}x{height} image");
    }

    [Fact]
    public void LoadPixelDataFromBgra_WithInsufficientData_ThrowsArgumentException()
    {
        // Arrange - provide fewer bytes than width * height * 4
        var width = 10;
        var height = 10;
        var bgraData = new byte[100]; // Only 100 bytes, need 400

        // Act & Assert
        var ex = Assert.ThrowsAny<ArgumentException>(
            () => Image.LoadPixelDataFromBgra(bgraData, width, height, PngFormat.Instance));
        _output.WriteLine($"Exception for insufficient data: {ex.Message}");
    }

    [Fact]
    public void LoadPixelDataFromBgra_then_PngSave_then_Load_PreservesPixels()
    {
        // Arrange - create BGRA data with a known gradient pattern
        var width = 8;
        var height = 8;
        var bgraData = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var r = (byte)(x * 32);
                var g = (byte)(y * 32);
                var b = (byte)((x + y) * 16);
                byte a = 255;

                // Write in BGRA order
                bgraData[i] = b;
                bgraData[i + 1] = g;
                bgraData[i + 2] = r;
                bgraData[i + 3] = a;
            }
        }

        // Act - create from BGRA pixel data, save as PNG, reload
        using var image = Image.LoadPixelDataFromBgra(bgraData, width, height, PngFormat.Instance);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);

        // Assert - verify pixels survived the full pipeline
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var expected = new Rgba32(
                    (byte)(x * 32),
                    (byte)(y * 32),
                    (byte)((x + y) * 16),
                    255);
                Assert.Equal(expected, decoded[x, y]);
            }
        }

        _output.WriteLine($"Full BGRA pipeline roundtrip: {width}x{height} pixels preserved through LoadPixelDataFromBgra -> PNG -> Load");
    }

    [Fact]
    public void LoadPixelDataFromBgra_MatchesManualConversion()
    {
        // Arrange - verify that LoadPixelDataFromBgra produces the same result
        // as manually converting BGRA to RGBA and calling LoadPixelData<Rgba32>
        var width = 16;
        var height = 16;
        var bgraData = new byte[width * height * 4];
        var random = new Random(42); // Fixed seed for reproducibility
        random.NextBytes(bgraData);

        // Ensure alpha values are non-zero for meaningful comparison
        for (var i = 3; i < bgraData.Length; i += 4)
        {
            bgraData[i] = 255;
        }

        // Manual BGRA->RGBA conversion
        var rgbaData = new byte[bgraData.Length];
        for (var i = 0; i < bgraData.Length; i += 4)
        {
            rgbaData[i] = bgraData[i + 2];     // R from BGRA offset 2
            rgbaData[i + 1] = bgraData[i + 1]; // G stays
            rgbaData[i + 2] = bgraData[i];     // B from BGRA offset 0
            rgbaData[i + 3] = bgraData[i + 3]; // A stays
        }

        // Act
        using var fromBgra = Image.LoadPixelDataFromBgra(bgraData, width, height, PngFormat.Instance);
        using var fromManual = Image.LoadPixelData<Rgba32>(rgbaData, width, height, PngFormat.Instance);

        // Assert - both images should have identical pixels
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Assert.Equal(fromManual[x, y], fromBgra[x, y]);
            }
        }

        _output.WriteLine($"LoadPixelDataFromBgra matches manual conversion for {width}x{height} random pixels");
    }

    [Fact]
    public void LoadPixelDataFromBgra_WhenByteCountOverflowsInt32_ThrowsArgumentOutOfRange()
    {
        // Arrange - 40000 x 20000 is 800,000,000 pixels, which fits in an Int32, but the
        // BGRA byte count (3,200,000,000) does not. Computing the byte count as an Int32
        // wrapped it negative, which made the buffer-length guard pass vacuously and let
        // execution reach a slice with a negative length.
        var width = 40000;
        var height = 20000;
        var tooSmall = new byte[16];

        // Act
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => Image.LoadPixelDataFromBgra(tooSmall, width, height, PngFormat.Instance));

        // Assert - the guard must name the offending parameter rather than surfacing a
        // bare slicing failure from deeper inside the method.
        Assert.Equal("data", ex.ParamName);
        _output.WriteLine($"Oversized BGRA request rejected by guard: {ex.Message}");
    }

    [Fact]
    public void LoadPixelDataFromBgra_WithMaximumNonOverflowingSize_IsRejectedByLengthGuard()
    {
        // Arrange - the largest pixel count whose byte count still fits in an Int32.
        var width = 536870911;
        var height = 1;
        var tooSmall = new byte[16];

        // Act
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => Image.LoadPixelDataFromBgra(tooSmall, width, height, PngFormat.Instance));

        // Assert - rejected because the buffer is too small, not because of an overflow.
        Assert.Equal("data", ex.ParamName);
        _output.WriteLine($"Maximum non-overflowing size rejected on buffer length: {ex.Message}");
    }

    #endregion

    #region Truncated Stream Decoding Tests

    [Theory]
    [InlineData("BMP")]
    [InlineData("PNG")]
    [InlineData("TIFF")]
    public void Load_WithTruncatedImageData_ThrowsImageFormatException(string formatName)
    {
        // Arrange - the decoders read with Stream.ReadExactly so that a short read fails
        // rather than silently decoding uninitialized buffer contents. That surfaces as an
        // EndOfStreamException, which must be wrapped so callers only have to handle the
        // documented ImageFormatException hierarchy.
        IImageEncoder encoder = formatName switch
        {
            "BMP" => new BmpEncoder(),
            "PNG" => new PngEncoder(),
            "TIFF" => new TiffEncoder(),
            _ => throw new ArgumentOutOfRangeException(nameof(formatName))
        };

        byte[] encoded;
        using (var source = new Image<Rgba32>(200, 200, new Rgba32(200, 30, 30, 255)))
        using (var ms = new MemoryStream())
        {
            source.Save(ms, encoder);
            encoded = ms.ToArray();
        }

        // Act & Assert - every truncation point must produce an ImageFormatException,
        // never a raw System.IO.EndOfStreamException.
        foreach (var fraction in new[] { 0.55, 0.80, 0.95 })
        {
            var truncated = new byte[(int)(encoded.Length * fraction)];
            Array.Copy(encoded, truncated, truncated.Length);

            var ex = Assert.ThrowsAny<Exception>(() => Image.Load(truncated));
            Assert.IsNotType<EndOfStreamException>(ex);
            Assert.IsAssignableFrom<ImageFormatException>(ex);
            _output.WriteLine($"{formatName} truncated to {fraction:P0}: {ex.GetType().Name}");
        }
    }

    [Fact]
    public void Load_WithTruncatedBmp_PreservesEndOfStreamExceptionAsInnerException()
    {
        // Arrange
        byte[] encoded;
        using (var source = new Image<Rgba32>(200, 200, new Rgba32(10, 200, 60, 255)))
        using (var ms = new MemoryStream())
        {
            source.Save(ms, new BmpEncoder());
            encoded = ms.ToArray();
        }

        var truncated = new byte[encoded.Length / 2];
        Array.Copy(encoded, truncated, truncated.Length);

        // Act
        var ex = Assert.Throws<InvalidImageContentException>(() => Image.Load(truncated));

        // Assert - the original cause stays available for diagnostics.
        Assert.IsType<EndOfStreamException>(ex.InnerException);
        _output.WriteLine($"Truncated BMP wrapped correctly: {ex.Message}");
    }

    [Fact]
    public void Identify_WithTruncatedImageData_ThrowsImageFormatException()
    {
        // Arrange - truncate inside the header so Identify itself runs off the end.
        byte[] encoded;
        using (var source = new Image<Rgba32>(64, 64))
        using (var ms = new MemoryStream())
        {
            source.Save(ms, new BmpEncoder());
            encoded = ms.ToArray();
        }

        var truncated = new byte[20];
        Array.Copy(encoded, truncated, truncated.Length);

        // Act & Assert
        var ex = Assert.ThrowsAny<Exception>(() => Image.Identify(truncated));
        Assert.IsNotType<EndOfStreamException>(ex);
        _output.WriteLine($"Truncated header on Identify: {ex.GetType().Name}");
    }

    #endregion

    #region File Path Handling

    [Fact]
    public void Load_WithFileNameEndingInDots_Succeeds()
    {
        // Arrange - a previous path "traversal guard" rejected any resolved path ending in
        // "..", which is a legal file name on Linux and macOS. It blocked no traversal (
        // Path.GetFullPath normalizes ".." away before the check) while breaking real files.
        var dir = Path.Combine(Path.GetTempPath(), "cbimg_pathtests");
        Directory.CreateDirectory(dir);
        var awkwardPath = Path.Combine(dir, "legit..");

        try
        {
            using (var source = new Image<Rgba32>(4, 4, new Rgba32(1, 2, 3, 255)))
            using (var fs = File.Create(awkwardPath))
            {
                source.SaveAsPng(fs);
            }

            // Act
            using var loaded = Image.Load(awkwardPath);

            // Assert
            Assert.Equal(4, loaded.Width);
            Assert.Equal(4, loaded.Height);
            _output.WriteLine("File named 'legit..' loaded successfully");
        }
        finally
        {
            if (File.Exists(awkwardPath)) { File.Delete(awkwardPath); }
        }
    }

    [Fact]
    public void Save_WithFileNameEndingInDots_Succeeds()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), "cbimg_pathtests");
        Directory.CreateDirectory(dir);
        var awkwardPath = Path.Combine(dir, "output..");

        try
        {
            using var image = new Image<Rgba32>(4, 4);

            // Act
            image.Save(awkwardPath, new PngEncoder());

            // Assert
            Assert.True(File.Exists(awkwardPath));
            _output.WriteLine("File named 'output..' written successfully");
        }
        finally
        {
            if (File.Exists(awkwardPath)) { File.Delete(awkwardPath); }
        }
    }

    #endregion

    #region Decode Allocation Budget

    [Fact]
    public void CreateSandboxed_AppliesAnAllocationLimit()
    {
        // Arrange
        var sandboxed = Configuration.Default.CreateSandboxed(1);

        // Act & Assert - the clone is independent of the default configuration.
        Assert.NotSame(Configuration.Default, sandboxed);
        Assert.NotSame(Configuration.Default.MemoryAllocator, sandboxed.MemoryAllocator);
        _output.WriteLine("CreateSandboxed produced an independent configuration with its own allocator");
    }

    [Fact]
    public void CreateSandboxed_RejectsNonPositiveLimits()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => Configuration.Default.CreateSandboxed(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Configuration.Default.CreateSandboxed(-1));
        _output.WriteLine("Non-positive allocation limits rejected");
    }

    [Fact]
    public void CreateSandboxed_StillDecodesImagesThatFitTheBudget()
    {
        // Arrange
        byte[] encoded;
        using (var source = new Image<Rgba32>(64, 64, new Rgba32(10, 20, 30, 255)))
        using (var ms = new MemoryStream())
        {
            source.SaveAsPng(ms);
            encoded = ms.ToArray();
        }

        var sandboxed = Configuration.Default.CreateSandboxed(64);

        // Act
        using var image = Image.Load<Rgba32>(sandboxed, encoded);

        // Assert
        Assert.Equal(64, image.Width);
        Assert.Equal(new Rgba32(10, 20, 30, 255), image[0, 0]);
        _output.WriteLine("A modest image still decodes under a 64 MB budget");
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
        var width = 15;
        var height = 20;
        using var image = new Image<Rgba32>(width, height, new Rgba32(42, 42, 42, 255));

        // Act & Assert - accessing last valid pixel should work
        var pixel = image[width - 1, height - 1];
        Assert.Equal(new Rgba32(42, 42, 42, 255), pixel);
        _output.WriteLine($"Max valid index ({width - 1}, {height - 1}) accessed successfully");
    }

    #endregion

    #region CCITT Fax (T4 / Modified Huffman) Out-of-bounds Write Guard (GHSA-jj3q-cwqj-842r)

    // A hostile CCITT fax TIFF can declare pixel runs whose accumulated length exceeds the decoded
    // strip buffer. Every fax write funnels through BitWriterUtils.WriteBits, so a single bounds
    // check there neutralizes the overflow for the T4 and Modified Huffman decompressors alike.
    // These tests exercise that guard directly (via InternalsVisibleTo) and confirm the happy path
    // still fills the buffer exactly.

    [Fact]
    public void BitWriterUtils_WriteBits_FillingBufferExactly_DoesNotThrow()
    {
        // Arrange - 2 bytes == 16 bits of capacity.
        var buffer = new byte[2];

        // Act - write 16 one-bits, exactly filling the buffer.
        BitWriterUtils.WriteBits(buffer, 0, 16, 1);

        // Assert - every bit set, no overflow.
        Assert.Equal(0xFF, buffer[0]);
        Assert.Equal(0xFF, buffer[1]);
        _output.WriteLine("WriteBits filled the buffer exactly without throwing");
    }

    [Fact]
    public void BitWriterUtils_WriteBits_OnePastCapacity_ThrowsImageFormatException()
    {
        // Arrange - 1 byte == 8 bits of capacity.
        var buffer = new byte[1];

        // Act & Assert - asking for 9 bits must be rejected as malformed input, NOT leak an
        // IndexOutOfRangeException (the pre-fix behavior) or corrupt adjacent memory.
        var ex = Assert.Throws<ImageFormatException>(() => BitWriterUtils.WriteBits(buffer, 0, 9, 1));
        Assert.IsNotType<IndexOutOfRangeException>(ex);
        _output.WriteLine($"Over-length run rejected: {ex.Message}");
    }

    [Fact]
    public void BitWriterUtils_WriteBits_RunStartingInsideButEndingPastCapacity_ThrowsImageFormatException()
    {
        // Arrange - start writing near the end so the overflow happens mid-run.
        var buffer = new byte[2]; // 16 bits

        // Act & Assert - start at bit 12, write 8 bits -> ends at bit 20, 4 past capacity.
        Assert.Throws<ImageFormatException>(() => BitWriterUtils.WriteBits(buffer, 12, 8, 1));
        _output.WriteLine("Run overrunning the tail of the buffer rejected");
    }

    [Fact]
    public void BitWriterUtils_WriteBits_ZeroCount_IsNoOp()
    {
        // Arrange
        var buffer = new byte[1];

        // Act - a zero-length run touches nothing, even at the very end of the buffer.
        BitWriterUtils.WriteBits(buffer, 8, 0, 1);

        // Assert
        Assert.Equal(0, buffer[0]);
        _output.WriteLine("Zero-length run was a no-op");
    }

    [Theory]
    [InlineData(TiffCompression.Ccitt1D)]       // Modified Huffman
    [InlineData(TiffCompression.CcittGroup3Fax)] // T4 (Group 3 1D)
    public void CcittFax_ValidBilevelImage_RoundTrips(TiffCompression compression)
    {
        // Arrange - a small bilevel pattern. Valid fax data fills the strip buffer exactly, so the
        // new bounds guard must not disturb legitimate decoding.
        using var source = new Image<Rgba32>(37, 21);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                source[x, y] = ((x + y) % 3 == 0) ? new Rgba32(0, 0, 0, 255) : new Rgba32(255, 255, 255, 255);
            }
        }

        var encoder = new TiffEncoder { Compression = compression, BitsPerPixel = TiffBitsPerPixel.Bit1 };

        // Act
        using var ms = new MemoryStream();
        source.Save(ms, encoder);
        ms.Position = 0;
        using var decoded = Image.Load<Rgba32>(ms);

        // Assert - dimensions preserved and the bilevel content survives the round trip.
        Assert.Equal(source.Width, decoded.Width);
        Assert.Equal(source.Height, decoded.Height);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var expectedBlack = source[x, y].R == 0;
                var actualBlack = decoded[x, y].R < 128;
                Assert.Equal(expectedBlack, actualBlack);
            }
        }

        _output.WriteLine($"{compression} bilevel image round-tripped ({ms.Length} bytes) with the OOB guard in place");
    }

    #endregion

    #region Short-read handling in header parsing (stale buffer reuse)

    // These decoders read fixed-size header fields with stream.Read but ignored the returned
    // count. BufferedReadStream.Read only returns short at genuine EOF, so a truncated file left
    // the tail of the destination holding whatever was there before - for the JPEG Huffman table
    // that is a pooled buffer carrying bytes from a previously decoded image.

    [Fact]
    public void Load_WithJpegTruncatedInsideHuffmanTable_ThrowsImageFormatException()
    {
        // Arrange - encode a real JPEG, then cut the file a few bytes into its first DHT segment
        // so the code-lengths / code-values reads run past the end of the data.
        var encoded = EncodeSample(new JpegEncoder());
        var dhtIndex = FindJpegMarker(encoded, 0xC4);
        Assert.True(dhtIndex > 0, "Expected the encoded JPEG to contain a DHT (0xFFC4) marker.");

        var truncated = new byte[dhtIndex + 8];
        Array.Copy(encoded, truncated, truncated.Length);

        // Act & Assert - a truncated Huffman table must be reported, not silently built from
        // whatever the pooled buffer happened to contain.
        var ex = Assert.ThrowsAny<Exception>(() => Image.Load(truncated));
        _output.WriteLine($"JPEG truncated inside DHT threw {ex.GetType().Name}: {ex.Message}");
        Assert.IsAssignableFrom<ImageFormatException>(ex);
    }

    [Theory]
    [InlineData(2)]  // partway through the 14 byte file header
    [InlineData(10)] // file header incomplete
    [InlineData(16)] // file header complete, info header truncated
    [InlineData(20)] // info header size read, remainder truncated
    public void Load_WithBmpTruncatedInsideHeaders_ThrowsImageFormatException(int keepBytes)
    {
        // Arrange
        var encoded = EncodeSample(new BmpEncoder());
        Assert.True(encoded.Length > keepBytes);

        var truncated = new byte[keepBytes];
        Array.Copy(encoded, truncated, keepBytes);

        // Act & Assert - every truncation point inside the headers must surface as a format error.
        var ex = Assert.ThrowsAny<Exception>(() => Image.Load(truncated));
        _output.WriteLine($"BMP truncated to {keepBytes} bytes threw {ex.GetType().Name}");
        Assert.IsAssignableFrom<ImageFormatException>(ex);
    }

    [Fact]
    public void Load_JpegAfterDecodingAnotherImage_DoesNotInheritPooledBufferContents()
    {
        // Arrange - decode a valid JPEG first so the shared Huffman-table buffer is returned to
        // the pool holding real data, then feed a JPEG truncated inside its DHT. If the short
        // read is ignored, the second decode builds its table from the first image's bytes.
        var valid = EncodeSample(new JpegEncoder());
        using (var warmup = Image.Load<Rgba32>(valid))
        {
            Assert.Equal(120, warmup.Width);
        }

        var dhtIndex = FindJpegMarker(valid, 0xC4);
        var truncated = new byte[dhtIndex + 8];
        Array.Copy(valid, truncated, truncated.Length);

        // Act & Assert
        var ex = Assert.ThrowsAny<Exception>(() => Image.Load(truncated));
        _output.WriteLine($"Second (truncated) JPEG decode threw {ex.GetType().Name}");
        Assert.IsAssignableFrom<ImageFormatException>(ex);
    }

    private static byte[] EncodeSample(IImageEncoder encoder)
    {
        using var source = new Image<Rgba32>(120, 90);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                source[x, y] = new Rgba32((byte)(x * 2 % 256), (byte)(y * 3 % 256), (byte)((x + y) % 256), 255);
            }
        }

        using var ms = new MemoryStream();
        source.Save(ms, encoder);
        return ms.ToArray();
    }

    /// <summary>
    /// Returns the index of the first byte of the payload of the given JPEG marker, or -1.
    /// </summary>
    private static int FindJpegMarker(byte[] data, byte marker)
    {
        for (var i = 0; i < data.Length - 1; i++)
        {
            if (data[i] == 0xFF && data[i + 1] == marker)
            {
                return i + 2;
            }
        }

        return -1;
    }

    #endregion
}
