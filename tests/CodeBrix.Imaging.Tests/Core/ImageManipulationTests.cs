using CodeBrix.Imaging.Fonts;
using CodeBrix.Imaging.Fonts.Rendering;
using CodeBrix.Imaging.Formats;
using CodeBrix.Imaging.Formats.Bmp;
using CodeBrix.Imaging.Formats.Gif;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Pbm;
using CodeBrix.Imaging.Formats.Png;
using CodeBrix.Imaging.Formats.Tga;
using CodeBrix.Imaging.Formats.Tiff;
using CodeBrix.Imaging.Formats.Webp;
using CodeBrix.Imaging.PixelFormats;
using CodeBrix.Imaging.Processing;
using CodeBrix.Imaging.Tests.Helpers;
using System;
using System.IO;
using Xunit;

namespace CodeBrix.Imaging.Tests.Core;

public class ImageManipulationTests
{
#if TESTING_ON_WINDOWS
    public const string TempFolder = @"C:\Temp";
#elif TESTING_ON_LINUX
    public const string TempFolder = @"/home/jeremy/Temp";
#elif TESTING_ON_MACOS
    public const string TempFolder = @"/Users/jeremy/Temp";
#elif TESTING_ON_LINUX_ORANGEPI
    public const string TempFolder = "/home/orangepi/Temp"; 
#endif

    private readonly ITestOutputHelper _output;

    public ImageManipulationTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Theory]
    [InlineData("test-image-01.bmp", "Roboto-Regular.ttf", 
        TempFolder, PngFormat.FormatName, "hello world", 
        "#0000FF", false, true, false)]

    [InlineData("test-image-01.jpg", "Roboto-Regular.ttf", 
        TempFolder, BmpFormat.FormatName, "hello world",
        "#FF0000", false, false, false)]

    [InlineData("test-image-01.png", "Roboto-Regular.ttf", 
        TempFolder, JpegFormat.FormatName, "hello world",
        "#0000FF", true, true, false)]

    [InlineData("test-image-01.bmp", "Nabla-Regular-VariableFont_EDPT_EHLT.ttf", 
        TempFolder, JpegFormat.FormatName, "hello world",
        "#0000FF", false, false, false)]

    [InlineData("test-image-01.jpg", "Nabla-Regular-VariableFont_EDPT_EHLT.ttf", 
        TempFolder, PngFormat.FormatName, "hello world",
        "#FF0000", true, true, false)]

    [InlineData("test-image-01.png", "Nabla-Regular-VariableFont_EDPT_EHLT.ttf", 
        TempFolder, BmpFormat.FormatName, "hello world",
        "#0000FF", false, false, false)]
    public void save_image_with_text(
        string sampleImageFilename, 
        string sampleFontFilename,
        string outputFolderPath,
        string desiredImageFormat,
        string textToWrite,
        string textColorHex,
        bool withForceMonoColor,
        bool withConvertImageToGrayscale,
        bool withFileDelete)
    {
        // Arrange
        using var sampleImage = ImageTestHelper.LoadImage(sampleImageFilename);
        var fonts = new FontCollection();
        var fontFamily = FontTestHelper.LoadFont(sampleFontFilename, fonts);
        var font = new Font(fontFamily, 40f);

        // Determine the file extension and encoder based on desired format
        var fileExtension = desiredImageFormat.ToUpperInvariant() switch
        {
            PngFormat.FormatName => PngFormat.FormatDefaultExtension,
            JpegFormat.FormatName or JpegFormat.FormatAltName => JpegFormat.FormatDefaultExtension,
            BmpFormat.FormatName => BmpFormat.FormatDefaultExtension,
            _ => throw new ArgumentException($"Unsupported image format: {desiredImageFormat}", nameof(desiredImageFormat))
        };

        var encoder = desiredImageFormat.ToUpperInvariant() switch
        {
            PngFormat.FormatName => (IImageEncoder)new PngEncoder(),
            JpegFormat.FormatName or JpegFormat.FormatAltName => new JpegEncoder { Quality = 90 },
            BmpFormat.FormatName => new BmpEncoder(),
            _ => throw new ArgumentException($"Unsupported image format: {desiredImageFormat}", nameof(desiredImageFormat))
        };

        // Ensure output directory exists
        if (!Directory.Exists(outputFolderPath))
        {
            throw new InvalidOperationException($"The specified folder could not be found: {outputFolderPath}");
        }

        // Generate a unique filename
        var uniqueFileName = $"{DateTime.Now.Ticks}_Added_Text_test-output{fileExtension}";
        var outputFilePath = Path.Combine(outputFolderPath, uniqueFileName);

        // Act

        // Convert the image to Rgba32 pixel format for text rendering
        using var imageWithText = sampleImage.CloneAs<Rgba32>();

        // Convert to grayscale if requested
        if (withConvertImageToGrayscale)
        {
            imageWithText.Mutate(ctx => ctx.Grayscale());
        }

        // Get image dimensions
        int imageWidth = imageWithText.Width;
        int imageHeight = imageWithText.Height;

        // Calculate the anchor point: 10% from right edge, 10% from bottom edge
        // This is where the bottom-right corner of the text should align
        float anchorX = imageWidth * 0.9f;  // 90% from left = 10% from right
        float anchorY = imageHeight * 0.9f; // 90% from top = 10% from bottom

        // Measure the text to get its dimensions
        var textBounds = TextRenderingExtensions.MeasureText(textToWrite, font);

        // Calculate the text origin (top-left corner) so that the bottom-right
        // corner of the text aligns with our anchor point
        float textOriginX = anchorX - textBounds.Width;
        float textOriginY = anchorY - textBounds.Height;

        // Draw the text onto the image
        // Parse the hex color string to create the text color
        var textColor = Rgba32.ParseHex(textColorHex);

        // If the font is a color font (COLR/CPAL format), the glyph colors from the font will be used,
        // instead of the specified color UNLESS the forceMonoColor parameter is true
        // (which forces the use of the specified color).
        imageWithText.DrawText(textToWrite, font, textColor, textOriginX, textOriginY, withForceMonoColor);

        _output.WriteLine($"Source image: {sampleImageFilename} ({imageWidth}x{imageHeight})");
        _output.WriteLine($"Grayscale applied: {withConvertImageToGrayscale}");
        _output.WriteLine($"Font family: {fontFamily.Name}");
        _output.WriteLine($"Font file: {sampleFontFilename}");
        _output.WriteLine($"Is color font (has COLR table): {font.IsColorFont}");
        _output.WriteLine($"Color font format: {font.ColorFormat}");
        _output.WriteLine($"Text color (hex): {textColorHex}");
        _output.WriteLine($"Text color (RGBA): {textColor}");
        _output.WriteLine($"Force mono-color: {withForceMonoColor}");
        _output.WriteLine($"Font size: {font.Size}pt");
        _output.WriteLine($"Text to write: {textToWrite}");
        _output.WriteLine($"Text bounds: {textBounds.Width:F1}x{textBounds.Height:F1}");
        _output.WriteLine($"Anchor point (bottom-right of text): ({anchorX:F1}, {anchorY:F1})");
        _output.WriteLine($"Text origin (top-left of text): ({textOriginX:F1}, {textOriginY:F1})");
        _output.WriteLine($"Output format: {desiredImageFormat}");
        _output.WriteLine($"Output path: {outputFilePath}");

        // Save the image with the rendered text to the specified format
        using (var outputStream = File.Create(outputFilePath))
        {
            imageWithText.Save(outputStream, encoder);
        }

        // Assert
        Assert.True(File.Exists(outputFilePath), $"Output file should exist: {outputFilePath}");

        var fileInfo = new FileInfo(outputFilePath);
        Assert.True(fileInfo.Length > 0, "Output file should have content");
        _output.WriteLine($"Output file size: {fileInfo.Length} bytes");

        // Verify the saved image can be loaded back
        using var reloadedImage = Image.Load(outputFilePath);
        Assert.Equal(sampleImage.Width, reloadedImage.Width);
        Assert.Equal(sampleImage.Height, reloadedImage.Height);
        _output.WriteLine($"Reloaded image dimensions: {reloadedImage.Width}x{reloadedImage.Height}");

        // Clean up the test file
        if (withFileDelete)
        {
            File.Delete(outputFilePath);
            _output.WriteLine("Test file cleaned up successfully");
        }
    }

    [Theory]
    [InlineData("test-image-01.bmp", BmpFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.bmp", GifFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.bmp", JpegFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.bmp", PbmFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.bmp", PngFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.bmp", TgaFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.bmp", TiffFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.bmp", WebpFormat.FormatName, TempFolder, false)]

    [InlineData("test-image-01.jpg", BmpFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.jpg", GifFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.jpg", JpegFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.jpg", PbmFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.jpg", PngFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.jpg", TgaFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.jpg", TiffFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.jpg", WebpFormat.FormatName, TempFolder, false)]

    [InlineData("test-image-01.png", BmpFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.png", GifFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.png", JpegFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.png", PbmFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.png", PngFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.png", TgaFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.png", TiffFormat.FormatName, TempFolder, false)]
    [InlineData("test-image-01.png", WebpFormat.FormatName, TempFolder, false)]

    public void save_image_with_desired_format(
        string sampleImageFilename, 
        string desiredImageFormat, 
        string outputFolderPath, 
        bool withFileDelete)
    {
        // Arrange

        // Determine the expected source format based on the file extension
        var sourceExtension = Path.GetExtension(sampleImageFilename).ToLowerInvariant();
        var expectedSourceFormat = sourceExtension switch
        {
            BmpFormat.FormatDefaultExtension => (IImageFormat)BmpFormat.Instance,
            JpegFormat.FormatDefaultExtension or JpegFormat.FormatAltDefaultExtension => JpegFormat.Instance,
            PngFormat.FormatDefaultExtension => PngFormat.Instance,
            GifFormat.FormatDefaultExtension => GifFormat.Instance,
            TgaFormat.FormatDefaultExtension => TgaFormat.Instance,
            TiffFormat.FormatDefaultExtension or TiffFormat.FormatAltDefaultExtension => TiffFormat.Instance,
            WebpFormat.FormatDefaultExtension => WebpFormat.Instance,
            PbmFormat.FormatDefaultExtension or PbmFormat.FormatAlt1DefaultExtension or PbmFormat.FormatAlt2DefaultExtension => PbmFormat.Instance,
            _ => throw new ArgumentException($"Unsupported source image extension: {sourceExtension}", nameof(sampleImageFilename))
        };

        // Determine the desired format instance, file extension, and encoder
        var (desiredFormatInstance, desiredFileExtension, encoder) = desiredImageFormat switch
        {
            BmpFormat.FormatName => ((IImageFormat)BmpFormat.Instance, BmpFormat.FormatDefaultExtension, (IImageEncoder)new BmpEncoder()),
            GifFormat.FormatName => (GifFormat.Instance, GifFormat.FormatDefaultExtension, new GifEncoder()),
            JpegFormat.FormatName or JpegFormat.FormatAltName => (JpegFormat.Instance, JpegFormat.FormatDefaultExtension, new JpegEncoder { Quality = 90 }),
            PbmFormat.FormatName => (PbmFormat.Instance, PbmFormat.FormatDefaultExtension, new PbmEncoder()),
            PngFormat.FormatName => (PngFormat.Instance, PngFormat.FormatDefaultExtension, new PngEncoder()),
            TgaFormat.FormatName => (TgaFormat.Instance, TgaFormat.FormatDefaultExtension, new TgaEncoder()),
            TiffFormat.FormatName => (TiffFormat.Instance, TiffFormat.FormatDefaultExtension, new TiffEncoder()),
            WebpFormat.FormatName => (WebpFormat.Instance, WebpFormat.FormatDefaultExtension, new WebpEncoder()),
            _ => throw new ArgumentException($"Unsupported desired image format: {desiredImageFormat}", nameof(desiredImageFormat))
        };

        // Ensure output directory exists
        if (!Directory.Exists(outputFolderPath))
        {
            throw new InvalidOperationException($"The specified folder could not be found: {outputFolderPath}");
        }

        // Generate a unique filename
        var uniqueFileName = $"{DateTime.Now.Ticks}_FormatConversion_{expectedSourceFormat.Name}_to_{desiredImageFormat}_test-output{desiredFileExtension}";
        var outputFilePath = Path.Combine(outputFolderPath, uniqueFileName);

        // Act

        // Step 1: Load the image and verify the ExpectedFormat matches the source file's format
        using var sampleImage = ImageTestHelper.LoadImage(sampleImageFilename);
        using var image = sampleImage.CloneAs<Rgba32>();

        _output.WriteLine($"Source image: {sampleImageFilename} ({image.Width}x{image.Height})");
        _output.WriteLine($"Expected source format: {expectedSourceFormat.Name}");
        _output.WriteLine($"Actual source format: {image.Format.Name}");

        Assert.Equal(expectedSourceFormat.Name, image.Format.Name);

        // Step 2: Save the image in the desired format
        _output.WriteLine($"Saving image as: {desiredImageFormat} ({desiredFileExtension})");
        _output.WriteLine($"Output path: {outputFilePath}");

        using (var outputStream = File.Create(outputFilePath))
        {
            image.Save(outputStream, encoder);
        }

        // Step 3: After saving, the image's ExpectedFormat should now match the desired format
        _output.WriteLine($"Format after save: {image.Format.Name}");

        Assert.Equal(desiredFormatInstance.Name, image.Format.Name);

        // Step 4: Load the saved file and verify its ExpectedFormat matches the desired format
        Assert.True(File.Exists(outputFilePath), $"Output file should exist: {outputFilePath}");

        var fileInfo = new FileInfo(outputFilePath);
        Assert.True(fileInfo.Length > 0, "Output file should have content");
        _output.WriteLine($"Output file size: {fileInfo.Length} bytes");

        using var reloadedImage = Image.Load(outputFilePath);

        _output.WriteLine($"Reloaded image format: {reloadedImage.Format.Name}");
        _output.WriteLine($"Reloaded image dimensions: {reloadedImage.Width}x{reloadedImage.Height}");

        Assert.Equal(desiredFormatInstance.Name, reloadedImage.Format.Name);
        Assert.Equal(image.Width, reloadedImage.Width);
        Assert.Equal(image.Height, reloadedImage.Height);

        // Step 5: Clean up the test file if requested
        if (withFileDelete)
        {
            File.Delete(outputFilePath);
            _output.WriteLine("Test file cleaned up successfully");
        }
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_invert(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "Invert",
            ctx => ctx.Invert());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_grayscale(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "Grayscale",
            ctx => ctx.Grayscale());
    }

    // Tests black-and-white image conversion using BinaryThreshold instead of BlackWhite (BlackWhite was used
    // in the original code, but it doesn't allow for threshold configuration, so BinaryThreshold is more demonstrative).
    // 
    // The BlackWhite() method applies a fixed color matrix transformation without any
    // configurable threshold - it simply converts to grayscale-like black and white toning.
    // 
    // BinaryThreshold() provides precise control over the brightness cutoff point:
    // - threshold = 0.0 → Almost all pixels become white (only darkest become black)
    // - threshold = 0.5 → Balanced - pixels above 50% brightness become white, below become black
    // - threshold = 1.0 → Almost all pixels become black (only brightest become white)
    // 
    // This allows testing the visual impact of different threshold values on the
    // black-and-white conversion, making the effect more demonstrable.
    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, 0.5f, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, 0.3f, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, 0.7f, false)]
    public void process_image_with_black_white(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        float threshold,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            $"BinaryThreshold (threshold: {threshold})",
            ctx => ctx.BinaryThreshold(threshold));
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_sepia(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "Sepia",
            ctx => ctx.Sepia());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_kodachrome(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "Kodachrome",
            ctx => ctx.Kodachrome());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_polaroid(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "Polaroid",
            ctx => ctx.Polaroid());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_lomograph(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "Lomograph",
            ctx => ctx.Lomograph());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, 1.5f, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, 0.5f, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, 1.2f, false)]
    public void process_image_with_brightness(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        float amount,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            $"Brightness (amount: {amount})",
            ctx => ctx.Brightness(amount));
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, 1.5f, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, 0.5f, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, 1.2f, false)]
    public void process_image_with_contrast(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        float amount,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            $"Contrast (amount: {amount})",
            ctx => ctx.Contrast(amount));
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, 1.5f, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, 0.5f, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, 2.0f, false)]
    public void process_image_with_saturate(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        float amount,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            $"Saturate (amount: {amount})",
            ctx => ctx.Saturate(amount));
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, 1.5f, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, 0.5f, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, 1.2f, false)]
    public void process_image_with_lightness(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        float amount,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            $"Lightness (amount: {amount})",
            ctx => ctx.Lightness(amount));
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, 90f, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, 180f, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, -45f, false)]
    public void process_image_with_hue(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        float degrees,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            $"Hue (degrees: {degrees})",
            ctx => ctx.Hue(degrees));
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, 0.5f, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, 0.75f, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, 0.25f, false)]
    public void process_image_with_opacity(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        float opacity,
        bool withFileDelete)
    {
        // Arrange
        using var sampleImage = ImageTestHelper.LoadImage(sampleImageFilename);

        // Determine the file extension and encoder based on desired format
        var fileExtension = desiredImageFormat.ToUpperInvariant() switch
        {
            PngFormat.FormatName => PngFormat.FormatDefaultExtension,
            JpegFormat.FormatName or JpegFormat.FormatAltName => JpegFormat.FormatDefaultExtension,
            BmpFormat.FormatName => BmpFormat.FormatDefaultExtension,
            _ => throw new ArgumentException($"Unsupported image format: {desiredImageFormat}", nameof(desiredImageFormat))
        };

        var encoder = desiredImageFormat.ToUpperInvariant() switch
        {
            PngFormat.FormatName => (IImageEncoder)new PngEncoder(),
            JpegFormat.FormatName or JpegFormat.FormatAltName => new JpegEncoder { Quality = 90 },
            BmpFormat.FormatName => new BmpEncoder(),
            _ => throw new ArgumentException($"Unsupported image format: {desiredImageFormat}", nameof(desiredImageFormat))
        };

        // Ensure output directory exists
        if (!Directory.Exists(outputFolderPath))
        {
            throw new InvalidOperationException($"The specified folder could not be found: {outputFolderPath}");
        }

        // Generate a unique filename
        var uniqueFileName = $"{DateTime.Now.Ticks}_Opacity_amount_{opacity}_test-output{fileExtension}";
        var outputFilePath = Path.Combine(outputFolderPath, uniqueFileName);

        // Act

        // Create the foreground image (original, will have opacity applied when composited)
        using var foregroundImage = sampleImage.CloneAs<Rgba32>();

        // Create the background image (flipped upside-down and grayscale to be visually different)
        using var backgroundImage = sampleImage.CloneAs<Rgba32>();
        backgroundImage.Mutate(ctx => ctx
            .Flip(FlipMode.Vertical)
            .Grayscale());

        // Get image dimensions
        int imageWidth = foregroundImage.Width;
        int imageHeight = foregroundImage.Height;

        // Layer the foreground image on top of the background image with the specified opacity
        // The opacity controls how much of the foreground shows vs the background bleeding through
        backgroundImage.Mutate(ctx => ctx.DrawImage(foregroundImage, opacity));

        _output.WriteLine($"Source image: {sampleImageFilename} ({imageWidth}x{imageHeight})");
        _output.WriteLine($"Processing operation: Opacity (amount: {opacity})");
        _output.WriteLine($"Background: Flipped vertically + Grayscale");
        _output.WriteLine($"Foreground opacity: {opacity} (1.0 = fully opaque, 0.0 = fully transparent)");
        _output.WriteLine($"Output format: {desiredImageFormat}");
        _output.WriteLine($"Output path: {outputFilePath}");

        // Save the composited image to the specified format
        using (var outputStream = File.Create(outputFilePath))
        {
            backgroundImage.Save(outputStream, encoder);
        }

        // Assert
        Assert.True(File.Exists(outputFilePath), $"Output file should exist: {outputFilePath}");

        var fileInfo = new FileInfo(outputFilePath);
        Assert.True(fileInfo.Length > 0, "Output file should have content");
        _output.WriteLine($"Output file size: {fileInfo.Length} bytes");

        // Verify the saved image can be loaded back
        using var reloadedImage = Image.Load(outputFilePath);
        Assert.Equal(sampleImage.Width, reloadedImage.Width);
        Assert.Equal(sampleImage.Height, reloadedImage.Height);
        _output.WriteLine($"Reloaded image dimensions: {reloadedImage.Width}x{reloadedImage.Height}");

        // Clean up the test file
        if (withFileDelete)
        {
            File.Delete(outputFilePath);
            _output.WriteLine("Test file cleaned up successfully");
        }
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_gaussian_blur(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "GaussianBlur",
            ctx => ctx.GaussianBlur());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_gaussian_sharpen(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "GaussianSharpen",
            ctx => ctx.GaussianSharpen());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_box_blur(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "BoxBlur",
            ctx => ctx.BoxBlur());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_bokeh_blur(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "BokehBlur",
            ctx => ctx.BokehBlur());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, FlipMode.Horizontal, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, FlipMode.Vertical, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, FlipMode.Horizontal, false)]
    public void process_image_with_flip(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        FlipMode flipMode,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            $"Flip (mode: {flipMode})",
            ctx => ctx.Flip(flipMode));
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, 90f, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, 180f, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, 45f, false)]
    public void process_image_with_rotate(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        float degrees,
        bool withFileDelete)
    {
        ProcessImageWithOperationWithSizeChange(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            $"Rotate (degrees: {degrees})",
            ctx => ctx.Rotate(degrees));
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, RotateMode.Rotate90, FlipMode.None, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, RotateMode.Rotate180, FlipMode.Horizontal, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, RotateMode.Rotate270, FlipMode.Vertical, false)]
    public void process_image_with_rotate_flip(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        RotateMode rotateMode,
        FlipMode flipMode,
        bool withFileDelete)
    {
        ProcessImageWithOperationWithSizeChange(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            $"RotateFlip (rotate: {rotateMode}, flip: {flipMode})",
            ctx => ctx.RotateFlip(rotateMode, flipMode));
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, 8, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, 16, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, 4, false)]
    public void process_image_with_pixelate(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        int size,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            $"Pixelate (size: {size})",
            ctx => ctx.Pixelate(size));
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_oil_paint(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "OilPaint",
            ctx => ctx.OilPaint());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_vignette(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "Vignette",
            ctx => ctx.Vignette());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, "#FFFF00", false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, "#FFFACD", false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, "#FFD700", false)]
    public void process_image_with_glow(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        string glowColorHex,
        bool withFileDelete)
    {
        var glowColor = Color.ParseHex(glowColorHex);
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            $"Glow (color: {glowColorHex})",
            ctx => ctx.Glow(glowColor));
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_detect_edges(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "DetectEdges",
            ctx => ctx.DetectEdges());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_auto_orient(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "AutoOrient",
            ctx => ctx.AutoOrient());
    }

    [Theory]
    [InlineData("test-image-01.bmp", TempFolder, PngFormat.FormatName, false)]
    [InlineData("test-image-01.jpg", TempFolder, JpegFormat.FormatName, false)]
    [InlineData("test-image-01.png", TempFolder, BmpFormat.FormatName, false)]
    public void process_image_with_histogram_equalization(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete)
    {
        ProcessImageWithOperation(
            sampleImageFilename,
            outputFolderPath,
            desiredImageFormat,
            withFileDelete,
            "HistogramEqualization",
            ctx => ctx.HistogramEqualization());
    }

    private void ProcessImageWithOperation(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete,
        string operationName,
        Func<IImageProcessingContext, IImageProcessingContext> operation)
    {
        // Arrange
        using var sampleImage = ImageTestHelper.LoadImage(sampleImageFilename);

        // Determine the file extension and encoder based on desired format
        var fileExtension = desiredImageFormat.ToUpperInvariant() switch
        {
            PngFormat.FormatName => PngFormat.FormatDefaultExtension,
            JpegFormat.FormatName or JpegFormat.FormatAltName => JpegFormat.FormatDefaultExtension,
            BmpFormat.FormatName => BmpFormat.FormatDefaultExtension,
            _ => throw new ArgumentException($"Unsupported image format: {desiredImageFormat}", nameof(desiredImageFormat))
        };

        var encoder = desiredImageFormat.ToUpperInvariant() switch
        {
            PngFormat.FormatName => (IImageEncoder)new PngEncoder(),
            JpegFormat.FormatName or JpegFormat.FormatAltName => new JpegEncoder { Quality = 90 },
            BmpFormat.FormatName => new BmpEncoder(),
            _ => throw new ArgumentException($"Unsupported image format: {desiredImageFormat}", nameof(desiredImageFormat))
        };

        // Ensure output directory exists
        if (!Directory.Exists(outputFolderPath))
        {
            throw new InvalidOperationException($"The specified folder could not be found: {outputFolderPath}");
        }

        // Generate a unique filename
        var uniqueFileName = $"{DateTime.Now.Ticks}_{operationName.Replace(" ", "_").Replace(":", "").Replace("(", "").Replace(")", "")}_test-output{fileExtension}";
        var outputFilePath = Path.Combine(outputFolderPath, uniqueFileName);

        // Act

        // Convert the image to Rgba32 pixel format for processing
        using var processedImage = sampleImage.CloneAs<Rgba32>();

        // Get image dimensions
        int imageWidth = processedImage.Width;
        int imageHeight = processedImage.Height;

        // Apply the processing operation
        processedImage.Mutate(ctx => operation(ctx));

        _output.WriteLine($"Source image: {sampleImageFilename} ({imageWidth}x{imageHeight})");
        _output.WriteLine($"Processing operation: {operationName}");
        _output.WriteLine($"Output format: {desiredImageFormat}");
        _output.WriteLine($"Output path: {outputFilePath}");

        // Save the processed image to the specified format
        using (var outputStream = File.Create(outputFilePath))
        {
            processedImage.Save(outputStream, encoder);
        }

        // Assert
        Assert.True(File.Exists(outputFilePath), $"Output file should exist: {outputFilePath}");

        var fileInfo = new FileInfo(outputFilePath);
        Assert.True(fileInfo.Length > 0, "Output file should have content");
        _output.WriteLine($"Output file size: {fileInfo.Length} bytes");

        // Verify the saved image can be loaded back
        using var reloadedImage = Image.Load(outputFilePath);
        Assert.Equal(sampleImage.Width, reloadedImage.Width);
        Assert.Equal(sampleImage.Height, reloadedImage.Height);
        _output.WriteLine($"Reloaded image dimensions: {reloadedImage.Width}x{reloadedImage.Height}");

        // Clean up the test file
        if (withFileDelete)
        {
            File.Delete(outputFilePath);
            _output.WriteLine("Test file cleaned up successfully");
        }
    }

    private void ProcessImageWithOperationWithSizeChange(
        string sampleImageFilename,
        string outputFolderPath,
        string desiredImageFormat,
        bool withFileDelete,
        string operationName,
        Func<IImageProcessingContext, IImageProcessingContext> operation)
    {
        // Arrange
        using var sampleImage = ImageTestHelper.LoadImage(sampleImageFilename);

        // Determine the file extension and encoder based on desired format
        var fileExtension = desiredImageFormat.ToUpperInvariant() switch
        {
            PngFormat.FormatName => PngFormat.FormatDefaultExtension,
            JpegFormat.FormatName or JpegFormat.FormatAltName => JpegFormat.FormatDefaultExtension,
            BmpFormat.FormatName => BmpFormat.FormatDefaultExtension,
            _ => throw new ArgumentException($"Unsupported image format: {desiredImageFormat}", nameof(desiredImageFormat))
        };

        var encoder = desiredImageFormat.ToUpperInvariant() switch
        {
            PngFormat.FormatName => (IImageEncoder)new PngEncoder(),
            JpegFormat.FormatName or JpegFormat.FormatAltName => new JpegEncoder { Quality = 90 },
            BmpFormat.FormatName => new BmpEncoder(),
            _ => throw new ArgumentException($"Unsupported image format: {desiredImageFormat}", nameof(desiredImageFormat))
        };

        // Ensure output directory exists
        if (!Directory.Exists(outputFolderPath))
        {
            throw new InvalidOperationException($"The specified folder could not be found: {outputFolderPath}");
        }

        // Generate a unique filename
        var uniqueFileName = $"{DateTime.Now.Ticks}_{operationName.Replace(" ", "_").Replace(":", "").Replace("(", "").Replace(")", "")}_test-output{fileExtension}";
        var outputFilePath = Path.Combine(outputFolderPath, uniqueFileName);

        // Act

        // Convert the image to Rgba32 pixel format for processing
        using var processedImage = sampleImage.CloneAs<Rgba32>();

        // Get original image dimensions
        int originalWidth = processedImage.Width;
        int originalHeight = processedImage.Height;

        // Apply the processing operation
        processedImage.Mutate(ctx => operation(ctx));

        _output.WriteLine($"Source image: {sampleImageFilename} ({originalWidth}x{originalHeight})");
        _output.WriteLine($"Processing operation: {operationName}");
        _output.WriteLine($"Processed image dimensions: {processedImage.Width}x{processedImage.Height}");
        _output.WriteLine($"Output format: {desiredImageFormat}");
        _output.WriteLine($"Output path: {outputFilePath}");

        // Save the processed image to the specified format
        using (var outputStream = File.Create(outputFilePath))
        {
            processedImage.Save(outputStream, encoder);
        }

        // Assert
        Assert.True(File.Exists(outputFilePath), $"Output file should exist: {outputFilePath}");

        var fileInfo = new FileInfo(outputFilePath);
        Assert.True(fileInfo.Length > 0, "Output file should have content");
        _output.WriteLine($"Output file size: {fileInfo.Length} bytes");

        // Verify the saved image can be loaded back
        using var reloadedImage = Image.Load(outputFilePath);
        // For operations that change image size, just verify the reloaded dimensions match the processed image
        Assert.Equal(processedImage.Width, reloadedImage.Width);
        Assert.Equal(processedImage.Height, reloadedImage.Height);
        _output.WriteLine($"Reloaded image dimensions: {reloadedImage.Width}x{reloadedImage.Height}");

        // Clean up the test file
        if (withFileDelete)
        {
            File.Delete(outputFilePath);
            _output.WriteLine("Test file cleaned up successfully");
        }
    }
}
