================================================================================
AGENT-README: CodeBrix.Imaging
A Comprehensive Guide for AI Coding Agents
================================================================================

OVERVIEW
--------
CodeBrix.Imaging is a fully managed, cross-platform 2D image processing and font
handling library for .NET. It has ZERO external dependencies beyond .NET itself.

It is a fork of the open source SixLabors.ImageSharp (v2.1.3) and SixLabors.Fonts
(v1.0.0) libraries, licensed under Apache License 2.0.

IMPORTANT: If you are familiar with SixLabors.ImageSharp, the API surface of
CodeBrix.Imaging is very similar. However, ALL namespaces use "CodeBrix.Imaging"
instead of "SixLabors.ImageSharp". Do NOT mix the two libraries.

Source Repository: https://github.com/ellisnet/CodeBrix.Imaging
License: Apache License 2.0

================================================================================

INSTALLATION
------------
NuGet Package: CodeBrix.Imaging.ApacheLicenseForever
Dependencies: None

Requirements: .NET 10.0 or higher

To add to a .NET 10+ project:

    dotnet add package CodeBrix.Imaging.ApacheLicenseForever

Or in a .csproj file (NuGet will resolve the latest version):

    <PackageReference Include="CodeBrix.Imaging.ApacheLicenseForever" />

IMPORTANT: The package name is "CodeBrix.Imaging.ApacheLicenseForever" (not just
"CodeBrix.Imaging"). Always use this full package name when installing.

================================================================================

KEY NAMESPACES
--------------
When writing code with CodeBrix.Imaging, these are the primary namespaces:

    using CodeBrix.Imaging;                  // Core types: Image, Color, Configuration
    using CodeBrix.Imaging.Processing;       // Image processing operations (Resize, Crop, etc.)
    using CodeBrix.Imaging.PixelFormats;     // Pixel types: Rgba32, Rgb24, etc.
    using CodeBrix.Imaging.Formats.Png;      // PNG encoder/decoder
    using CodeBrix.Imaging.Formats.Jpeg;     // JPEG encoder/decoder
    using CodeBrix.Imaging.Formats.Bmp;      // BMP encoder/decoder
    using CodeBrix.Imaging.Formats.Gif;      // GIF encoder/decoder
    using CodeBrix.Imaging.Formats.Tiff;     // TIFF encoder/decoder
    using CodeBrix.Imaging.Formats.Webp;     // WebP encoder/decoder
    using CodeBrix.Imaging.Formats.Pbm;      // PBM encoder/decoder
    using CodeBrix.Imaging.Formats.Tga;      // TGA encoder/decoder
    using CodeBrix.Imaging.Helpers;          // BmpFormatHelper (8bpp grayscale BMP export)
    using CodeBrix.Imaging.Drawing;          // Drawing and text rendering (if applicable)

================================================================================

SUPPORTED IMAGE FORMATS
-----------------------
CodeBrix.Imaging supports reading and writing the following formats:
  - BMP  (Bitmap)
  - GIF  (Graphics Interchange Format)
  - JPEG (Joint Photographic Experts Group)
  - PBM  (Portable Bitmap)
  - PNG  (Portable Network Graphics)
  - TGA  (Truevision TGA)
  - TIFF (Tagged Image File Format)
  - WebP (Google WebP)

Format is auto-detected when loading. When saving, format is inferred from the
file extension, or you can specify an explicit encoder.

================================================================================

CORE API REFERENCE
==================

1. CREATING IMAGES
-------------------

Create a blank image with dimensions:

    using var image = new Image<Rgba32>(800, 600);

Create with a specific background color:

    var bgColor = new Rgba32(255, 0, 0, 255); // Red, fully opaque
    using var image = new Image<Rgba32>(800, 600, bgColor);

Create with a specific configuration:

    var config = Configuration.Default;
    using var image = new Image<Rgba32>(config, 800, 600);

IMPORTANT: Image implements IDisposable. Always use 'using' statements or
explicitly call Dispose() to avoid memory leaks.

2. LOADING IMAGES
------------------

Load from a file path:

    using var image = Image.Load("photo.jpg");

Load from a stream:

    using var stream = File.OpenRead("photo.png");
    using var image = Image.Load(stream);

Load with a specific pixel type:

    using var image = Image.Load<Rgba32>("photo.jpg");

3. LOADING IMAGES FROM RAW PIXEL DATA
---------------------------------------

IMPORTANT: Image.LoadPixelData<TPixel>() requires FOUR parameters, not three.
The fourth parameter is an IImageFormat specifying the intended image format.
This is a common point of confusion — other imaging libraries often make the
format parameter optional, but CodeBrix.Imaging requires it.

Signature:

    Image.LoadPixelData<TPixel>(byte[] data, int width, int height, IImageFormat format)

Required namespaces:

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.PixelFormats;     // For Rgba32, Rgb24, etc.
    using CodeBrix.Imaging.Formats.Png;      // For PngFormat.Instance (or other format)

Example — creating an image from an RGBA byte array and saving as PNG:

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.PixelFormats;
    using CodeBrix.Imaging.Formats.Png;

    // pixelData is a byte[] containing raw RGBA pixel data (4 bytes per pixel)
    // laid out in row-major order: R, G, B, A, R, G, B, A, ...
    byte[] pixelData = GetPixelDataFromSomewhere();
    int width = 800;
    int height = 600;

    using var image = Image.LoadPixelData<Rgba32>(
        pixelData, width, height, PngFormat.Instance);
    image.Save("output.png", new PngEncoder());

Example — creating from RGB data (no alpha channel):

    using var image = Image.LoadPixelData<Rgb24>(
        rgbData, width, height, PngFormat.Instance);

The format parameter tells CodeBrix.Imaging what format the image will be
associated with. Common format instances:

    PngFormat.Instance   (from CodeBrix.Imaging.Formats.Png)
    JpegFormat.Instance  (from CodeBrix.Imaging.Formats.Jpeg)
    BmpFormat.Instance   (from CodeBrix.Imaging.Formats.Bmp)
    GifFormat.Instance   (from CodeBrix.Imaging.Formats.Gif)
    WebpFormat.Instance  (from CodeBrix.Imaging.Formats.Webp)
    TiffFormat.Instance  (from CodeBrix.Imaging.Formats.Tiff)
    TgaFormat.Instance   (from CodeBrix.Imaging.Formats.Tga)
    PbmFormat.Instance   (from CodeBrix.Imaging.Formats.Pbm)

NOTE: You can save the image in a different format than the one specified in
LoadPixelData by using a different encoder in the Save() call. The format
parameter in LoadPixelData sets the image's default format association, but
does not prevent saving in other formats.

LOADING BGRA PIXEL DATA (e.g., from PDFium, Direct2D, Cairo, GDI+):

    Many native renderers output pixels in BGRA byte order (Blue, Green, Red,
    Alpha). Use Image.LoadPixelDataFromBgra() to load BGRA data directly —
    the library handles the BGRA-to-RGBA conversion internally using
    SIMD-optimized (AVX2/SSSE3) channel reordering for maximum performance.

    Signature:

        Image.LoadPixelDataFromBgra(byte[] data, int width, int height, IImageFormat format)

    This returns an Image<Rgba32> with all channels correctly reordered.

    Example — loading BGRA pixel data from PDFium:

        using CodeBrix.Imaging;
        using CodeBrix.Imaging.PixelFormats;
        using CodeBrix.Imaging.Formats.Png;

        // bgraData is a byte[] from PDFium (or other native renderer)
        // in BGRA order: B, G, R, A, B, G, R, A, ...
        byte[] bgraData = GetBgraDataFromPdfium();
        int width = 2550;   // 8.5 inches at 300 DPI
        int height = 3300;  // 11 inches at 300 DPI

        using var image = Image.LoadPixelDataFromBgra(
            bgraData, width, height, PngFormat.Instance);
        image.Save("page.png", new PngEncoder());

    LoadPixelDataFromBgra also accepts ReadOnlySpan<byte> and an optional
    Configuration parameter, mirroring the LoadPixelData overloads:

        Image.LoadPixelDataFromBgra(ReadOnlySpan<byte> data, int width, int height, IImageFormat format)
        Image.LoadPixelDataFromBgra(Configuration config, byte[] data, int width, int height, IImageFormat format)
        Image.LoadPixelDataFromBgra(Configuration config, ReadOnlySpan<byte> data, int width, int height, IImageFormat format)

    PERFORMANCE NOTE: LoadPixelDataFromBgra is significantly faster than
    manually swapping R and B bytes in a loop before calling LoadPixelData.
    The internal conversion uses hardware-accelerated SIMD instructions
    (AVX2 processes 8 pixels at a time, SSSE3 processes 4 pixels at a time)
    and writes directly into the image's pixel buffer — eliminating both the
    scalar byte-swap loop and the intermediate buffer allocation.

4. SAVING IMAGES
-----------------

Save to file (format inferred from extension):

    image.Save("output.png");
    image.Save("output.jpg");
    image.Save("output.bmp");

Save to file with explicit encoder:

    image.Save("output.png", new PngEncoder());
    image.Save("output.jpg", new JpegEncoder());
    image.Save("output.bmp", new BmpEncoder());

Save to stream:

    using var stream = new MemoryStream();
    image.Save(stream, new PngEncoder());

Save asynchronously:

    await image.SaveAsync(stream, new PngEncoder(), CancellationToken.None);

5. FORMAT DETECTION AND IDENTIFICATION
---------------------------------------

Detect the format of an image without fully loading it:

    using var stream = File.OpenRead("unknown-image");
    var format = Image.DetectFormat(stream);
    Console.WriteLine(format.Name); // "PNG", "JPEG", "BMP", etc.

Get image dimensions without fully loading:

    using var stream = File.OpenRead("photo.png");
    var info = Image.Identify(stream);
    Console.WriteLine($"{info.Width}x{info.Height}");

6. FORMAT CONVERSION
---------------------

Convert between formats by loading in one format and saving in another:

    using var image = Image.Load("photo.bmp");
    image.Save("photo.png");

Or more explicitly:

    using var image = Image.Load("photo.bmp");
    image.Save("photo.webp"); // Converts BMP to WebP

7. EXPORTING AS 8BPP GRAYSCALE BMP
------------------------------------

BmpFormatHelper provides extension methods for exporting any image as an
8-bit-per-pixel grayscale BMP file. This is useful for document imaging
workflows, scanner integrations, and systems that require 8bpp indexed BMPs
(e.g., legacy document management systems).

Required namespace:

    using CodeBrix.Imaging.Helpers;   // For BmpFormatHelper extension methods

IMPORTANT: These are "export" methods, not "save" methods. They bypass the
library's standard encoder pipeline and do NOT update the image's
Metadata.ExpectedFormat property. The in-memory image remains unchanged
(still Rgba32) after the export. If you subsequently call image.Save(),
it will save in whatever format was previously associated with the image.

Methods (all are extension methods on Image):

    // Sync - with default grayscale weights (R=0.3, G=0.59, B=0.11)
    image.ExportAs8bppGrayscaleBmpFormat(stream, indexingMode);

    // Async - with default grayscale weights
    await image.ExportAs8bppGrayscaleBmpFormatAsync(stream, indexingMode);

    // Sync - with custom color matrix
    image.ExportAs8bppGrayscaleBmpFormat(stream, colorMatrix, indexingMode);

    // Async - with custom color matrix
    await image.ExportAs8bppGrayscaleBmpFormatAsync(stream, colorMatrix, indexingMode);

Indexing Modes (BmpIndexingMode enum, from CodeBrix.Imaging.Helpers):

    BmpIndexingMode.Normal (default)
        256-entry linear grayscale palette (index 0 = black, 255 = white).
        Each pixel's computed gray value maps directly to its palette index.

    BmpIndexingMode.SystemDrawingCompatible
        224-entry GDI+ halftone palette with empirically-determined quantization.
        Produces output that matches System.Drawing's Format8bppIndexed conversion,
        suitable for interop with systems that expect GDI+-compatible 8bpp BMPs.

Available pre-defined color matrices (all public static readonly on BmpFormatHelper):

    BmpFormatHelper.DefaultGrayscaleColorMatrix
        R=0.3, G=0.59, B=0.11 — matches System.Drawing.Imaging.ColorMatrix
        grayscale conversion weights. This is the default when no color matrix
        is specified.

    BmpFormatHelper.Bt601GrayscaleColorMatrix
        R=0.299, G=0.587, B=0.114 — ITU-R BT.601 luma coefficients.
        Matches CodeBrix.Imaging.Processing.GrayscaleMode.Bt601.

    BmpFormatHelper.Bt709GrayscaleColorMatrix
        R=0.2126, G=0.7152, B=0.0722 — ITU-R BT.709 luma coefficients.
        Matches CodeBrix.Imaging.Processing.GrayscaleMode.Bt709.

The custom ColorMatrix overloads accept any 5x4 color matrix, but the output
is always grayscale. The matrix controls how RGB channels are weighted to
compute a single grayscale intensity value per pixel; it does NOT produce
color 8bpp output.

Example — export to file:

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Helpers;

    using var image = Image.Load("photo.jpg");
    await using var fs = new FileStream("output-8bpp.bmp", FileMode.Create);
    await image.ExportAs8bppGrayscaleBmpFormatAsync(fs);

Example — export to MemoryStream with SystemDrawingCompatible mode:

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Helpers;

    using var image = Image.Load("photo.jpg");
    using var ms = new MemoryStream();
    await image.ExportAs8bppGrayscaleBmpFormatAsync(ms,
        BmpIndexingMode.SystemDrawingCompatible);
    byte[] bmpBytes = ms.ToArray();

Example — export with custom color matrix (red-channel emphasis):

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Helpers;

    var redHeavyMatrix = new ColorMatrix(
        .8f, .8f, .8f, 0f,
        .1f, .1f, .1f, 0f,
        .1f, .1f, .1f, 0f,
        0f, 0f, 0f, 1f,
        0f, 0f, 0f, 0f);

    using var image = Image.Load("photo.jpg");
    using var ms = new MemoryStream();
    await image.ExportAs8bppGrayscaleBmpFormatAsync(ms, redHeavyMatrix);

================================================================================

IMAGE PROCESSING OPERATIONS
============================
All processing operations use the Mutate() method, which modifies the image
in-place. Requires: using CodeBrix.Imaging.Processing;

IMPORTANT: Operations can be chained inside a single Mutate() call for better
performance. This is more efficient than calling Mutate() multiple times.

--- GEOMETRIC OPERATIONS ---

Resize:
    image.Mutate(x => x.Resize(800, 600));

Crop:
    image.Mutate(x => x.Crop(new Rectangle(100, 100, 500, 400)));
    // Parameters: x, y, width, height

Rotate:
    image.Mutate(x => x.Rotate(90));   // Rotate 90 degrees clockwise
    image.Mutate(x => x.Rotate(180));  // Rotate 180 degrees
    image.Mutate(x => x.Rotate(270));  // Rotate 270 degrees

Flip:
    image.Mutate(x => x.Flip(FlipMode.Horizontal));
    image.Mutate(x => x.Flip(FlipMode.Vertical));

RotateFlip (combined operation):
    image.Mutate(x => x.RotateFlip(RotateMode.Rotate90, FlipMode.Horizontal));

NOTE: Rotate and RotateFlip operations may change the image dimensions
(e.g., rotating a 800x600 image by 90 degrees produces a 600x800 image).

--- COLOR/TONE ADJUSTMENTS ---

Grayscale:
    image.Mutate(x => x.Grayscale());

Invert (negative):
    image.Mutate(x => x.Invert());

Sepia:
    image.Mutate(x => x.Sepia());

Kodachrome:
    image.Mutate(x => x.Kodachrome());

Polaroid:
    image.Mutate(x => x.Polaroid());

Lomograph:
    image.Mutate(x => x.Lomograph());

--- ADJUSTMENT OPERATIONS ---

Brightness (float, 1.0 = normal):
    image.Mutate(x => x.Brightness(1.5f));  // Increase brightness
    image.Mutate(x => x.Brightness(0.5f));  // Decrease brightness

Contrast (float, 1.0 = normal):
    image.Mutate(x => x.Contrast(1.5f));    // Increase contrast
    image.Mutate(x => x.Contrast(0.5f));    // Decrease contrast

Saturation (float, 1.0 = normal):
    image.Mutate(x => x.Saturation(1.5f));  // Increase saturation
    image.Mutate(x => x.Saturation(0.0f));  // Fully desaturated

Lightness (float):
    image.Mutate(x => x.Lightness(1.5f));

Hue rotation (degrees):
    image.Mutate(x => x.Hue(90f));          // Rotate hue by 90 degrees

Opacity:
    image.Mutate(x => x.Opacity(0.5f));     // 50% opacity

--- BLUR AND SHARPEN ---

Gaussian Blur (sigma value):
    image.Mutate(x => x.GaussianBlur(3));   // Moderate blur
    image.Mutate(x => x.GaussianBlur(10));  // Heavy blur

Gaussian Sharpen:
    image.Mutate(x => x.GaussianSharpen(3));

Box Blur:
    image.Mutate(x => x.BoxBlur(5));

Bokeh Blur:
    image.Mutate(x => x.BokehBlur());

--- ARTISTIC EFFECTS ---

Pixelate:
    image.Mutate(x => x.Pixelate(8));       // 8-pixel block size

Oil Paint:
    image.Mutate(x => x.OilPaint());

Vignette:
    image.Mutate(x => x.Vignette());

Glow:
    image.Mutate(x => x.Glow());

Edge Detection:
    image.Mutate(x => x.DetectEdges());

Histogram Equalization:
    image.Mutate(x => x.HistogramEqualization());

--- CHAINING OPERATIONS (RECOMMENDED) ---

Chain multiple operations in a single Mutate() call for best performance:

    image.Mutate(x => x
        .Resize(1024, 768)
        .Grayscale()
        .GaussianBlur(3)
        .Brightness(1.1f)
        .Contrast(1.2f));

This is MORE EFFICIENT than:

    image.Mutate(x => x.Resize(1024, 768));
    image.Mutate(x => x.Grayscale());
    image.Mutate(x => x.GaussianBlur(3));
    // ... etc. (each call iterates over all pixels separately)

================================================================================

COLOR API
=========

Creating Colors:

    // From RGBA values (0-255)
    var color = Color.FromRgba(255, 0, 0, 255);     // Red, fully opaque
    var color = Color.FromRgb(0, 255, 0);            // Green, fully opaque

    // From hex string
    var color = Color.ParseHex("#FF0000");            // Red
    var color = Color.ParseHex("#00FF00FF");           // Green with alpha
    var color = Color.ParseHex("0000FF");              // Blue (no # prefix)
    var color = Color.ParseHex("FFF");                 // White (shorthand)

    // Safe parsing (returns bool)
    if (Color.TryParseHex("#FF0000", out var color)) { /* use color */ }

    // From named colors
    var color = Color.Parse("Red");
    var color = Color.Parse("Green");

    // Safe named color parsing
    if (Color.TryParse("Blue", out var color)) { /* use color */ }

Named Color Constants:

    Color.Red, Color.Green, Color.Blue, Color.White, Color.Black,
    Color.Transparent, Color.AliceBlue, Color.Crimson, Color.DarkSlateGray,
    // ... and many more standard CSS/HTML named colors

Modifying Colors:

    var semiTransparentRed = Color.Red.WithAlpha(0.5f);  // 50% opacity

Converting to Hex:

    string hex = color.ToHex();  // Returns hex string representation

Color Equality:

    bool areEqual = (color1 == color2);
    bool areEqual = color1.Equals(color2);

================================================================================

PIXEL FORMATS
=============

Available pixel format types:

    Rgba32  - 32-bit RGBA (8 bits per channel, most common)
    Rgb24   - 24-bit RGB (8 bits per channel, no alpha)

The generic Image<TPixel> allows specifying pixel format:

    using var image = new Image<Rgba32>(100, 100);   // 32-bit with alpha
    using var image = new Image<Rgb24>(100, 100);    // 24-bit no alpha

Clone/convert between pixel formats:

    using var original = new Image<Rgba32>(100, 100);
    using var clone = original.CloneAs<Rgb24>();  // Convert Rgba32 -> Rgb24

Pixel type metadata:

    var pixelType = image.PixelType;
    int bpp = pixelType.BitsPerPixel;  // e.g., 32 for Rgba32

================================================================================

FONT HANDLING
=============

CodeBrix.Imaging includes TrueType font support (forked from SixLabors.Fonts).

Font Families and System Fonts:

    // Access system fonts (platform-dependent availability)
    var fontFamily = SystemFonts.Get("Arial");

    // Check if system fonts are available
    // Note: Font tests include skip logic for environments without fonts

Creating Fonts:

    var font = new Font(fontFamily, 24);                      // Size 24
    var font = new Font(fontFamily, 24, FontStyle.Bold);      // Bold
    var font = new Font(fontFamily, 24, FontStyle.Italic);    // Italic

    // Font from embedded TrueType file
    var collection = new FontCollection();
    var family = collection.Add("path/to/font.ttf");
    var font = family.CreateFont(24, FontStyle.Regular);

Font Properties:

    font.Name        // Font name
    font.Family      // FontFamily object
    font.Size        // Font size (float)
    font.IsBold      // bool
    font.IsItalic    // bool
    font.FontMetrics // Font metrics data

Creating Fonts from Existing (Prototype Pattern):

    var boldFont = new Font(existingFont, FontStyle.Bold);
    var largerFont = new Font(existingFont, 36); // Same style, different size

Supported Font Files:

    - TrueType fonts (.ttf)
    - Variable fonts (e.g., Nabla-Regular-VariableFont_EDPT_EHLT.ttf)
    - Color emoji fonts (e.g., NotoColorEmoji-Regular.ttf)

Drawing Text on Images:

    using var image = Image.Load("photo.jpg");
    var font = SystemFonts.CreateFont("Arial", 24, FontStyle.Bold);
    var color = Color.White;

    // Calculate position (example: 10% from bottom-right corner)
    var position = new PointF(image.Width * 0.9f, image.Height * 0.9f);

    image.Mutate(x => x.DrawText("Hello World", font, color, position));
    image.Save("photo-with-text.jpg");

================================================================================

IMAGE METADATA
==============

CodeBrix.Imaging supports reading and writing image metadata:

    using var image = Image.Load("photo.jpg");
    var metadata = image.Metadata;  // ImageMetadata object

Supported metadata types:
  - EXIF (Exchangeable Image File Format)
  - IPTC (International Press Telecommunications Council)
  - XMP  (Extensible Metadata Platform)

================================================================================

FRAMES (ANIMATED IMAGES)
=========================

Images can contain multiple frames (e.g., animated GIFs):

    using var image = Image.Load("animated.gif");
    var frameCount = image.Frames.Count;  // Number of frames

    // Access individual frames
    var frame = image.Frames[0];  // First frame

================================================================================

ADVANCED FEATURES
=================

Encoder Detection:

    // Detect appropriate encoder based on file extension
    var encoder = image.DetectEncoder("output.png");  // Returns PngEncoder

Visitor Pattern:

    // Image supports the visitor pattern via IImageVisitor
    image.AcceptVisitor(myVisitor);

Configuration:

    // Access image configuration
    var config = image.GetConfiguration();

    // Access configuration from a frame
    var frameConfig = frame.GetConfiguration();

Pixel Memory Access (Advanced/Unsafe):

    // Direct pixel row memory access for high-performance scenarios
    var memory = image.DangerousGetPixelRowMemory(rowIndex);

    // Get pixel memory group
    var memoryGroup = image.GetPixelMemoryGroup();

    // CAUTION: DangerousGetPixelRowMemory throws ArgumentOutOfRangeException
    // for invalid row indices. Always validate bounds first.

Parallel Processing:

    // The library supports parallel execution for image processing operations
    // ParallelExecutionSettings can be configured for performance tuning

================================================================================

COMPLETE EXAMPLES
=================

Example 1: Load, Process, and Save in Multiple Formats
-------------------------------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Processing;

    using var image = Image.Load("input.jpg");

    image.Mutate(x => x
        .Resize(1920, 1080)
        .Brightness(1.1f)
        .Contrast(1.2f)
        .Saturation(1.1f));

    image.Save("output.png");    // Save as PNG
    image.Save("output.webp");   // Save as WebP
    image.Save("output.jpg");    // Save as JPEG

Example 2: Create a Thumbnail
------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Processing;

    using var image = Image.Load("large-photo.jpg");
    image.Mutate(x => x.Resize(200, 200));
    image.Save("thumbnail.jpg");

Example 3: Apply Watermark Text
---------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Processing;
    using CodeBrix.Imaging.PixelFormats;

    using var image = Image.Load("photo.jpg");

    var font = SystemFonts.CreateFont("Arial", 24, FontStyle.Bold);
    var position = new PointF(image.Width * 0.9f, image.Height * 0.9f);

    image.Mutate(x => x.DrawText("(c) 2026", font, Color.White, position));
    image.Save("watermarked.jpg");

Example 4: Batch Format Conversion
------------------------------------
    using CodeBrix.Imaging;

    string[] inputFiles = Directory.GetFiles("input/", "*.bmp");
    foreach (var file in inputFiles)
    {
        using var image = Image.Load(file);
        var outputPath = Path.ChangeExtension(file, ".png");
        image.Save(outputPath);
    }

Example 5: Image Processing Pipeline with Stream I/O
------------------------------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Processing;
    using CodeBrix.Imaging.Formats.Png;

    public byte[] ProcessImage(Stream inputStream)
    {
        using var image = Image.Load(inputStream);

        image.Mutate(x => x
            .Resize(800, 600)
            .Grayscale()
            .GaussianBlur(2));

        using var outputStream = new MemoryStream();
        image.Save(outputStream, new PngEncoder());
        return outputStream.ToArray();
    }

Example 6: Detect Format Before Processing
--------------------------------------------
    using CodeBrix.Imaging;

    using var stream = File.OpenRead("unknown-file");
    var format = Image.DetectFormat(stream);

    if (format != null)
    {
        Console.WriteLine($"Format: {format.Name}");
        stream.Position = 0; // Reset stream position
        using var image = Image.Load(stream);
        // Process image...
    }

Example 7: Create Image from Raw Pixel Data (e.g., from a native renderer)
---------------------------------------------------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.PixelFormats;
    using CodeBrix.Imaging.Formats.Png;

    // Suppose you have raw RGBA pixel data from a native rendering engine
    // (e.g., PDFium, Skia, Cairo, or a custom renderer).
    // The data must be in the correct byte order for the pixel format:
    //   Rgba32 expects: R, G, B, A, R, G, B, A, ... (row-major order)
    //   Rgb24 expects:  R, G, B, R, G, B, ...

    int width = 2550;   // e.g., 8.5 inches at 300 DPI
    int height = 3300;  // e.g., 11 inches at 300 DPI
    byte[] pixelData = new byte[width * height * 4]; // 4 bytes per Rgba32 pixel

    // ... fill pixelData from your renderer ...

    // IMPORTANT: LoadPixelData requires 4 arguments. The 4th is the image format.
    using var image = Image.LoadPixelData<Rgba32>(
        pixelData, width, height, PngFormat.Instance);

    // Save as PNG
    image.Save("rendered_page.png", new PngEncoder());

    // Or save as JPEG (the format in LoadPixelData doesn't restrict save format)
    image.Save("rendered_page.jpg", new JpegEncoder());

Example 8: Load BGRA Pixel Data from a Native Renderer
--------------------------------------------------------
    using System.Runtime.InteropServices;
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.PixelFormats;
    using CodeBrix.Imaging.Formats.Png;

    // Many native renderers (PDFium, Direct2D, Cairo, GDI+) output pixels in
    // BGRA order (Blue, Green, Red, Alpha). Use LoadPixelDataFromBgra() to
    // load directly — no manual byte swapping needed.

    // Simple case: BGRA data already in a byte array with no stride padding
    byte[] bgraData = GetBgraFromRenderer();
    int width = 800;
    int height = 600;

    using var image = Image.LoadPixelDataFromBgra(
        bgraData, width, height, PngFormat.Instance);
    image.Save("output.png", new PngEncoder());

    // Advanced case: Reading from an unmanaged buffer with stride padding.
    // Native renderers often use a stride (bytes per row) that is larger than
    // width * bytesPerPixel due to memory alignment. When stride != width * 4,
    // you must copy the data into a contiguous array first, stripping padding.
    IntPtr nativeBuffer = GetBufferFromNativeRenderer();
    int stride = GetStrideFromNativeRenderer();

    var bgraPixelData = new byte[width * height * 4];
    for (var y = 0; y < height; y++)
    {
        Marshal.Copy(
            nativeBuffer + y * stride,  // source: row start in native buffer
            bgraPixelData,
            y * width * 4,               // destination: contiguous row start
            width * 4);                  // copy only the pixel data (no padding)
    }

    using var image2 = Image.LoadPixelDataFromBgra(
        bgraPixelData, width, height, PngFormat.Instance);
    image2.Save("output.png", new PngEncoder());

================================================================================

PERFORMANCE TIPS FOR CODING AGENTS
====================================

1. CHAIN OPERATIONS: Always chain multiple operations inside a single Mutate()
   call rather than making separate Mutate() calls. Each Mutate() call iterates
   over all pixels, so chaining reduces total iterations.

2. DISPOSE IMAGES: Always use 'using' statements. Image objects allocate
   significant unmanaged memory. Failing to dispose causes memory leaks.
   Multiple Dispose() calls are safe (no exception thrown).

3. USE STREAMS FOR WEB/API SCENARIOS: When processing images in web applications
   or APIs, use stream-based loading and saving to avoid unnecessary disk I/O.

4. USE Image.Identify() FOR METADATA ONLY: If you only need image dimensions
   or format info, use Image.Identify() instead of Image.Load(). This reads
   only the image header, not the full pixel data.

5. USE Image.DetectFormat() FOR FORMAT CHECKING: To check an image's format
   without loading it, use DetectFormat() which only reads the file header.

6. CHOOSE APPROPRIATE PIXEL FORMAT: Use Rgb24 instead of Rgba32 when you don't
   need alpha channel transparency. This uses 25% less memory per pixel.

7. RESIZE EARLY: When building processing pipelines, resize the image first
   (if making it smaller) to reduce the number of pixels subsequent operations
   must process.

8. ASYNC SAVING: Use SaveAsync() for non-blocking I/O in async applications.

9. NO EXTERNAL DEPENDENCIES: This library is self-contained. You do NOT need
   to install any native libraries, platform-specific packages, or runtime
   dependencies. Just add the NuGet package and it works.

10. CROSS-PLATFORM: Code written with CodeBrix.Imaging works on Windows, Linux,
    and macOS without modification. No platform-specific code needed.

================================================================================

COMMON PITFALLS TO AVOID
=========================

1. DO NOT confuse the NuGet package name with the namespace.
   - Package: CodeBrix.Imaging.ApacheLicenseForever
   - Namespace: CodeBrix.Imaging

2. DO NOT use SixLabors.ImageSharp namespaces. Even though this is a fork,
   all namespaces are CodeBrix.Imaging.*.

3. DO NOT forget to add "using CodeBrix.Imaging.Processing;" when using
   processing methods like Resize(), Crop(), Grayscale(), etc.

4. DO NOT forget to dispose Image objects. They hold significant memory.

5. DO NOT target .NET versions below 10.0. This library requires .NET 10+.

6. DO NOT assume system fonts are available in all environments (e.g.,
   Docker containers, CI/CD). Embed font files as resources if needed.

7. DO NOT forget to reset stream position after DetectFormat() if you
   subsequently want to Load() from the same stream.

8. DO NOT call Image.LoadPixelData<TPixel>() with only 3 arguments. It
   requires 4: (byte[] data, int width, int height, IImageFormat format).
   Omitting the format parameter will cause a compilation error (CS1501).
   Use the appropriate format instance, e.g., PngFormat.Instance.

9. DO NOT pass BGRA-ordered pixel data directly to LoadPixelData<Rgba32>().
    Many native renderers (PDFium, Direct2D, Cairo, GDI+) output BGRA byte
    order. Use LoadPixelDataFromBgra() instead — it handles the conversion
    internally using SIMD-optimized channel reordering. If you pass BGRA data
    to LoadPixelData<Rgba32>(), colors will appear with red and blue swapped.

10. DO NOT confuse stride with width when reading from native pixel buffers.
     Stride (bytes per row) may be larger than width * bytesPerPixel due to
     memory alignment padding. Always use the stride reported by the native
     API when indexing into the source buffer.

11. DO NOT confuse ExportAs8bppGrayscaleBmpFormat with Save/SaveAsBmp.
     The Export methods write a specialized 8bpp grayscale BMP directly to
     the stream and do NOT update Metadata.ExpectedFormat. The in-memory
     image is unchanged after export. Use the standard Save()/SaveAsBmp()
     methods for normal BMP saving (24bpp/32bpp).

12. DO NOT expect ExportAs8bppGrayscaleBmpFormat with a custom ColorMatrix
     to produce color output. The ColorMatrix controls how RGB channels are
     weighted to compute a single grayscale intensity value — the output is
     always grayscale regardless of the matrix used.

================================================================================

COMMON USING STATEMENT COMBINATIONS
=====================================

For most image processing tasks, copy this block:

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Processing;
    using CodeBrix.Imaging.PixelFormats;

For saving to a specific format with an explicit encoder, add one of:

    using CodeBrix.Imaging.Formats.Png;
    using CodeBrix.Imaging.Formats.Jpeg;
    using CodeBrix.Imaging.Formats.Bmp;
    using CodeBrix.Imaging.Formats.Webp;

For loading raw pixel data (e.g., from native renderers), use:

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.PixelFormats;
    using CodeBrix.Imaging.Formats.Png;   // Or whichever format you need

For exporting as 8bpp grayscale BMP, use:

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Helpers;        // BmpFormatHelper, BmpIndexingMode

For text rendering on images, add:

    using CodeBrix.Imaging.Drawing;

For font loading from files, add:

    using CodeBrix.Imaging.Fonts;

================================================================================

WHAT THIS LIBRARY DOES NOT DO
===============================

Do NOT attempt to use CodeBrix.Imaging for the following - it will not work:

  - PDF generation or rendering (use CodeBrix.PdfDocuments instead)
  - SVG parsing or rendering
  - Video processing or frame extraction
  - Camera/webcam capture
  - OCR (optical character recognition)
  - AI/ML-based image recognition or classification
  - RAW camera format processing (e.g., .CR2, .NEF, .ARW)
  - HEIF/HEIC format (Apple's image format)
  - 3D rendering or OpenGL/Vulkan integration
  - Animated WebP creation (static WebP only)
  - Direct GPU-accelerated processing

This library IS for: loading, saving, converting, resizing, cropping,
filtering, drawing on, and adding text to 2D raster images in the supported
formats (BMP, GIF, JPEG, PBM, PNG, TGA, TIFF, WebP). It can also create
images from raw pixel data via Image.LoadPixelData<TPixel>() (for RGBA data)
or Image.LoadPixelDataFromBgra() (for BGRA data from native renderers),
which is useful for integrating with native rendering engines that produce
raw pixel buffers. Additionally, it can export images as 8-bit-per-pixel
grayscale BMP files via ExportAs8bppGrayscaleBmpFormat() — useful for
document imaging workflows and interop with systems requiring 8bpp BMPs.

================================================================================

MINIMUM VIABLE PROJECT TEMPLATE
=================================

To scaffold a new .NET 10 console project that uses CodeBrix.Imaging:

    dotnet new console -n MyImageApp --framework net10.0
    cd MyImageApp
    dotnet add package CodeBrix.Imaging.ApacheLicenseForever

Then in Program.cs:

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Processing;

    using var image = Image.Load("input.jpg");
    image.Mutate(x => x.Resize(800, 600));
    image.Save("output.jpg");

    Console.WriteLine("Done!");

Build and run:

    dotnet build
    dotnet run

================================================================================

DEEPER LEARNING: TEST FILE CROSS-REFERENCES
=============================================

The CodeBrix.Imaging.Tests project in the source repository contains working
code examples for virtually every feature. If the documentation above is not
sufficient for a specific task, fetch and read the relevant test file from:

    https://github.com/ellisnet/CodeBrix.Imaging
    Path: tests/CodeBrix.Imaging.Tests/

Feature-to-test-file mapping:

  Image processing operations (resize, crop, rotate, flip, filters, effects,
  blur, sharpen, text rendering, format conversion):
    -> tests/CodeBrix.Imaging.Tests/Core/ImageManipulationTests.cs
       This is the MOST COMPREHENSIVE test file. It covers nearly all Mutate()
       operations with real working examples including text overlay positioning,
       all 8 format conversions, color/tone adjustments, blur effects, geometric
       transforms, and artistic effects.

  Image creation, loading, saving, format detection, pixel types, cloning,
  disposal, and async save:
    -> tests/CodeBrix.Imaging.Tests/Core/ImageTests.cs

  Image configuration:
    -> tests/CodeBrix.Imaging.Tests/Core/ConfigurationTests.cs

  Image security considerations:
    -> tests/CodeBrix.Imaging.Tests/Core/ImageSecurityTests.cs

  Color creation (RGBA, hex, named), parsing, equality, alpha modification:
    -> tests/CodeBrix.Imaging.Tests/Colors/ColorTests.cs

  Font creation, styles, properties, font families, system fonts:
    -> tests/CodeBrix.Imaging.Tests/Fonts/FontTests.cs

  Font collections (loading fonts from files):
    -> tests/CodeBrix.Imaging.Tests/Fonts/FontCollectionTests.cs

  Font families and font descriptions:
    -> tests/CodeBrix.Imaging.Tests/Fonts/FontFamilyTests.cs
    -> tests/CodeBrix.Imaging.Tests/Fonts/FontDescriptionTests.cs

  System font access:
    -> tests/CodeBrix.Imaging.Tests/Fonts/SystemFontsTests.cs

  Image format detection and encoding:
    -> tests/CodeBrix.Imaging.Tests/Formats/ImageFormatTests.cs

  Pixel format operations:
    -> tests/CodeBrix.Imaging.Tests/PixelFormats/

  8bpp grayscale BMP export (ExportAs8bppGrayscaleBmpFormat, BmpIndexingMode,
  custom ColorMatrix, byte-level reference comparison):
    -> tests/CodeBrix.Imaging.Tests/Advanced/Format8bppIndexedTests.cs

  8bpp grayscale BMP validation (argument checks, error cases, color matrix
  constants):
    -> tests/CodeBrix.Imaging.Tests/Advanced/BmpFormatHelperValidationTests.cs

  XMP metadata profile (XmpProfile creation, GetDocument, DeepClone,
  ToByteArray, null/empty handling):
    -> tests/CodeBrix.Imaging.Tests/Metadata/XmpProfileTests.cs

  Encoder detection, visitor pattern, configuration access, pixel memory:
    -> tests/CodeBrix.Imaging.Tests/Advanced/AdvancedImageExtensionsTests.cs

  Parallel processing settings:
    -> tests/CodeBrix.Imaging.Tests/Advanced/ParallelExecutionSettingsTests.cs

  Primitive shapes and drawing:
    -> tests/CodeBrix.Imaging.Tests/Primitives/

HOW TO USE: Fetch the raw file content from GitHub using a URL like:
    https://raw.githubusercontent.com/ellisnet/CodeBrix.Imaging/main/{path}
For example:
    https://raw.githubusercontent.com/ellisnet/CodeBrix.Imaging/main/tests/CodeBrix.Imaging.Tests/Core/ImageManipulationTests.cs

================================================================================

QUICK REFERENCE CARD
=====================

Install:        dotnet add package CodeBrix.Imaging.ApacheLicenseForever
Load:           Image.Load("file.jpg")
Load<T>:        Image.Load<Rgba32>("file.jpg")
Create:         new Image<Rgba32>(width, height)
LoadPixelData:  Image.LoadPixelData<Rgba32>(data, width, height, PngFormat.Instance)
LoadFromBgra:   Image.LoadPixelDataFromBgra(bgraData, width, height, PngFormat.Instance)
Save:           image.Save("file.png")
Save stream:    image.Save(stream, new PngEncoder())
Export 8bpp:    image.ExportAs8bppGrayscaleBmpFormat(stream)
Export 8bpp:    await image.ExportAs8bppGrayscaleBmpFormatAsync(stream)
Detect format:  Image.DetectFormat(stream)
Identify:       Image.Identify(stream)
Resize:         image.Mutate(x => x.Resize(w, h))
Crop:           image.Mutate(x => x.Crop(new Rectangle(x, y, w, h)))
Rotate:         image.Mutate(x => x.Rotate(degrees))
Flip:           image.Mutate(x => x.Flip(FlipMode.Horizontal))
Grayscale:      image.Mutate(x => x.Grayscale())
Blur:           image.Mutate(x => x.GaussianBlur(sigma))
Sharpen:        image.Mutate(x => x.GaussianSharpen(sigma))
Brightness:     image.Mutate(x => x.Brightness(amount))
Contrast:       image.Mutate(x => x.Contrast(amount))
Saturation:     image.Mutate(x => x.Saturation(amount))
Sepia:          image.Mutate(x => x.Sepia())
Invert:         image.Mutate(x => x.Invert())
Draw text:      image.Mutate(x => x.DrawText(text, font, color, point))
Detect edges:   image.Mutate(x => x.DetectEdges())
Pixelate:       image.Mutate(x => x.Pixelate(size))
Oil paint:      image.Mutate(x => x.OilPaint())
Vignette:       image.Mutate(x => x.Vignette())
Glow:           image.Mutate(x => x.Glow())
Clone as:       image.CloneAs<Rgb24>()

Formats:        BMP, GIF, JPEG, PBM, PNG, TGA, TIFF, WebP
Pixel types:    Rgba32 (32-bit), Rgb24 (24-bit)
Target:         .NET 10.0+

================================================================================
