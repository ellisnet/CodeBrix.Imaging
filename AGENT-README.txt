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
Latest Version: 1.0.49 (as of Feb 2026)
Package Size: ~813 KB
Dependencies: None

Requirements: .NET 10.0 or higher

To add to a .NET 10+ project:

    dotnet add package CodeBrix.Imaging.ApacheLicenseForever

Or in a .csproj file:

    <PackageReference Include="CodeBrix.Imaging.ApacheLicenseForever" Version="1.0.49" />

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

3. SAVING IMAGES
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

4. FORMAT DETECTION AND IDENTIFICATION
---------------------------------------

Detect the format of an image without fully loading it:

    using var stream = File.OpenRead("unknown-image");
    var format = Image.DetectFormat(stream);
    Console.WriteLine(format.Name); // "PNG", "JPEG", "BMP", etc.

Get image dimensions without fully loading:

    using var stream = File.OpenRead("photo.png");
    var info = Image.Identify(stream);
    Console.WriteLine($"{info.Width}x{info.Height}");

5. FORMAT CONVERSION
---------------------

Convert between formats by loading in one format and saving in another:

    using var image = Image.Load("photo.bmp");
    image.Save("photo.png");

Or more explicitly:

    using var image = Image.Load("photo.bmp");
    image.Save("photo.webp"); // Converts BMP to WebP

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
formats (BMP, GIF, JPEG, PBM, PNG, TGA, TIFF, WebP).

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
Save:           image.Save("file.png")
Save stream:    image.Save(stream, new PngEncoder())
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
