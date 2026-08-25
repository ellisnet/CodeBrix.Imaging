================================================================================
AGENT-README: CodeBrix.Imaging
A Guide for AI Coding Agents — CONSUMING the
CodeBrix.Imaging.ApacheLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Imaging is a fully managed, cross-platform 2D image processing, font
handling and text rendering library for .NET. It loads, saves, converts,
resizes, transforms, filters, quantizes, composites and annotates raster
images, and it rasterizes TrueType/CFF text directly onto those images. It is
100% managed code: there are no native libraries to deploy and no
platform-specific packages to reference.

Target framework: .NET 10 or later. There is no multi-targeting; the assembly
is net10.0 only.

PROVENANCE — READ THIS BEFORE WRITING ANY CODE
----------------------------------------------
CodeBrix.Imaging is a fork of the Apache-2.0 licensed SixLabors.ImageSharp
(v2.1.13) and SixLabors.Fonts (v1.0.0-beta18) source, with additional
CodeBrix-only APIs layered on top. Every namespace was renamed:

    SixLabors.ImageSharp.*   ->   CodeBrix.Imaging.*
    SixLabors.Fonts.*        ->   CodeBrix.Imaging.Fonts.*

Do NOT write `using SixLabors...` anywhere, do NOT add the upstream NuGet
packages, and do NOT mix the two libraries in one project — the type names are
identical and the compiler will report ambiguous references.

Two things are NOT a straight copy of the upstream layout, and getting them
wrong is the single most common failure mode:

  * Text rendering lives in CodeBrix.Imaging.Fonts.Rendering, and DrawText is
    an extension method on Image / Image<TPixel> — NOT on the Mutate() context.
    See "TEXT AND FONT RENDERING" below.
  * There is NO `CodeBrix.Imaging.Drawing` namespace. If you have seen that
    using directive in older documentation, it is wrong and will not compile.

Source repository: https://github.com/ellisnet/CodeBrix.Imaging

================================================================================

INSTALLATION
============
NuGet PackageId:  CodeBrix.Imaging.ApacheLicenseForever
Root namespace:   CodeBrix.Imaging
License:          Apache-2.0
NuGet dependencies: none — the package depends only on the .NET base class
                  library.
Requirements:     .NET 10.0 or later. No native/OS packages, no runtime
                  identifiers, no extra deployment steps.

    dotnet add package CodeBrix.Imaging.ApacheLicenseForever

Or, in a .csproj (let NuGet resolve the newest version):

    <PackageReference Include="CodeBrix.Imaging.ApacheLicenseForever" />

IMPORTANT: the package id and the namespace differ. Reference
"CodeBrix.Imaging.ApacheLicenseForever"; write `using CodeBrix.Imaging;`.
There is no package named plain "CodeBrix.Imaging".

The package ships XML documentation (IntelliSense) alongside the assembly.

================================================================================

KEY NAMESPACES / USINGS
=======================
    CodeBrix.Imaging
        Image, Image<TPixel>, ImageFrame, ImageFrame<TPixel>,
        ImageFrameCollection, Color, Configuration, GraphicsOptions,
        PixelAccessor<TPixel>, and the primitives Point, PointF, Size, SizeF,
        Rectangle, RectangleF, ColorMatrix, DenseMatrix<T>, Rational,
        SignedRational, Number.
        Also carries the Save/SaveAs*/ToByteArray extension methods and the
        Get<Format>Metadata() extension methods.

    CodeBrix.Imaging.Processing
        Mutate(), Clone(), and EVERY processing operation extension method
        (Resize, Crop, Rotate, Flip, Grayscale, DrawImage, Quantize, Dither,
        BinaryThreshold, ...), plus ResizeOptions, ResizeMode,
        AnchorPositionMode, KnownResamplers, KnownQuantizers, KnownDitherings,
        KnownFilterMatrices, KnownEdgeDetectorKernels, AffineTransformBuilder,
        ProjectiveTransformBuilder, FlipMode, RotateMode, GrayscaleMode,
        ColorBlindnessMode, BinaryThresholdMode, TaperSide, TaperCorner.

    CodeBrix.Imaging.PixelFormats
        The 29 pixel structs (Rgba32, Rgb24, Bgra32, L8, ...), IPixel<TPixel>,
        PixelOperations<TPixel>, PixelColorBlendingMode,
        PixelAlphaCompositionMode, PixelConversionModifiers.

    CodeBrix.Imaging.Formats
        IImageFormat, IImageEncoder, IImageDecoder, ImageFormatManager,
        PixelTypeInfo.

    CodeBrix.Imaging.Formats.Bmp / .Gif / .Jpeg / .Pbm / .Png / .Tga / .Tiff
    CodeBrix.Imaging.Formats.Webp
        Per-format Format singleton, Encoder, Decoder and Metadata types.

    CodeBrix.Imaging.Fonts
        Font, FontFamily, FontCollection, SystemFonts, FontStyle, FontMetrics,
        FontDescription, FontRectangle, TextOptions, TextMeasurer,
        TextRenderer, TextRun, and the layout enums (TextAlignment,
        HorizontalAlignment, VerticalAlignment, LayoutMode, WordBreaking,
        TextDirection, TextJustification, KerningMode, HintingMode,
        ColorFontSupport, TextDecorations), plus the exception types
        FontException, FontFamilyNotFoundException, GlyphMissingException,
        InvalidFontFileException, InvalidFontTableException,
        MissingFontTableException.

    CodeBrix.Imaging.Fonts.Rendering
        TextRenderingExtensions — DrawText(...) and MeasureText(...).
        ImageGlyphRenderer<TPixel> (the scanline rasterizer).

    CodeBrix.Imaging.Metadata
        ImageMetadata, ImageFrameMetadata, PixelResolutionUnit,
        FrameDecodingMode.

    CodeBrix.Imaging.Metadata.Profiles.Exif   ExifProfile, ExifTag, ExifTag<T>,
                                              ExifParts, ExifDataType.
    CodeBrix.Imaging.Metadata.Profiles.Icc    IccProfile, IccProfileHeader.
    CodeBrix.Imaging.Metadata.Profiles.Iptc   IptcProfile, IptcTag, IptcValue.
    CodeBrix.Imaging.Metadata.Profiles.Xmp    XmpProfile.

    CodeBrix.Imaging.Helpers
        BmpFormatHelper, BmpIndexingMode — the CodeBrix-only 8bpp grayscale
        BMP export.

    CodeBrix.Imaging.Advanced
        AdvancedImageExtensions (DetectEncoder, AcceptVisitor,
        GetConfiguration, GetPixelMemoryGroup, DangerousGetPixelRowMemory),
        IImageVisitor, IImageVisitorAsync, ParallelExecutionSettings,
        ParallelRowIterator.

    CodeBrix.Imaging.Memory
        MemoryAllocator, MemoryAllocatorOptions, AllocationOptions,
        SimpleGcMemoryAllocator, Buffer2D<T>, IMemoryGroup<T>, RowInterval.

    CodeBrix.Imaging.ColorSpaces
    CodeBrix.Imaging.ColorSpaces.Conversion
        CieLab, CieLch, CieLchuv, CieLuv, CieXyy, CieXyz, Cmyk, Hsl, Hsv,
        HunterLab, LinearRgb, Lms, Rgb, YCbCr, ColorSpaceConverter,
        ColorSpaceConverterOptions, RgbWorkingSpaces, Illuminants.

    CodeBrix.Imaging.Processing.Processors.Quantization
        IQuantizer, QuantizerOptions, QuantizerConstants, OctreeQuantizer,
        WuQuantizer, PaletteQuantizer, WebSafePaletteQuantizer,
        WernerPaletteQuantizer.

    CodeBrix.Imaging.Processing.Processors.Dithering
        IDither, OrderedDither, ErrorDither.

    CodeBrix.Imaging.Diagnostics
        MemoryDiagnostics (leak detection for undisposed buffers).

COPY-PASTE USING BLOCKS
-----------------------
Most image work:

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.PixelFormats;
    using CodeBrix.Imaging.Processing;

Add ONE of these when you construct an encoder/decoder explicitly or need a
format singleton:

    using CodeBrix.Imaging.Formats.Png;
    using CodeBrix.Imaging.Formats.Jpeg;
    using CodeBrix.Imaging.Formats.Bmp;
    using CodeBrix.Imaging.Formats.Gif;
    using CodeBrix.Imaging.Formats.Webp;
    using CodeBrix.Imaging.Formats.Tiff;
    using CodeBrix.Imaging.Formats.Tga;
    using CodeBrix.Imaging.Formats.Pbm;

Text rendering (BOTH lines are required):

    using CodeBrix.Imaging.Fonts;             // Font, SystemFonts, TextOptions
    using CodeBrix.Imaging.Fonts.Rendering;   // DrawText, MeasureText

Raw pixel import (from a native renderer):

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.PixelFormats;
    using CodeBrix.Imaging.Formats.Png;       // for PngFormat.Instance

8bpp grayscale BMP export:

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Helpers;           // BmpFormatHelper, BmpIndexingMode

Metadata:

    using CodeBrix.Imaging;                                   // Get*Metadata()
    using CodeBrix.Imaging.Metadata;
    using CodeBrix.Imaging.Metadata.Profiles.Exif;

================================================================================

SUPPORTED IMAGE FORMATS
=======================
Read and write, all registered in Configuration.Default:

    Format  Singleton             Encoder        Decoder        Metadata
    ------  --------------------  -------------  -------------  --------------
    BMP     BmpFormat.Instance    BmpEncoder     BmpDecoder     BmpMetadata
    GIF     GifFormat.Instance    GifEncoder     GifDecoder     GifMetadata +
                                                                GifFrameMetadata
    JPEG    JpegFormat.Instance   JpegEncoder    JpegDecoder    JpegMetadata
    PBM     PbmFormat.Instance    PbmEncoder     PbmDecoder     PbmMetadata
    PNG     PngFormat.Instance    PngEncoder     PngDecoder     PngMetadata
    TGA     TgaFormat.Instance    TgaEncoder     TgaDecoder     TgaMetadata
    TIFF    TiffFormat.Instance   TiffEncoder    TiffDecoder    TiffMetadata +
                                                                TiffFrameMetadata
    WebP    WebpFormat.Instance   WebpEncoder    WebpDecoder    WebpMetadata

Each format singleton exposes `Name`, `DefaultMimeType`, `MimeTypes`,
`FileExtensions` and `DefaultFileExtension`, plus const `FormatName`,
`FormatMimeType` and `FormatDefaultExtension` fields (JpegFormat also has
`FormatAltName`).

On load the format is auto-detected from the byte signature. On save the
format is inferred from the file extension unless you pass an explicit
encoder.

================================================================================

CORE API REFERENCE
==================

1. CREATING IMAGES
------------------
`Image` is the abstract, pixel-type-agnostic base; `Image<TPixel>` is the
concrete generic. Both implement IDisposable.

    public Image(int width, int height, IImageFormat expectedFormat = null)
    public Image(int width, int height, TPixel backgroundColor,
                 IImageFormat expectedFormat = null)
    public Image(Configuration configuration, int width, int height,
                 IImageFormat expectedFormat = null)
    public Image(Configuration configuration, int width, int height,
                 TPixel backgroundColor, IImageFormat expectedFormat = null)

    using var blank  = new Image<Rgba32>(800, 600);
    using var red    = new Image<Rgba32>(800, 600, new Rgba32(255, 0, 0, 255));
    using var scoped = new Image<Rgba32>(Configuration.Default, 800, 600);

Properties on Image (and therefore on Image<TPixel>):

    int Width, int Height
    PixelTypeInfo PixelType            // .BitsPerPixel, .AlphaRepresentation
    ImageMetadata Metadata
    IImageFormat Format
    double HorizontalResolution, VerticalResolution
    ImageFrameCollection Frames        // ImageFrameCollection<TPixel> on the
                                       // generic type (a `new` member)

Cloning and pixel-type conversion:

    public Image<TPixel> Clone()
    public Image<TPixel> Clone(Configuration configuration)
    public Image<TPixel2> CloneAs<TPixel2>()
    public abstract Image<TPixel2> CloneAs<TPixel2>(Configuration configuration)

    using var original = Image.Load("photo.jpg");     // Image (non-generic)
    using var rgba     = original.CloneAs<Rgba32>();  // Image<Rgba32>

ALWAYS dispose images (`using`). They own pooled buffers. Dispose() is
idempotent — calling it twice does not throw.

2. LOADING IMAGES
-----------------
`Image.Load` has overloads for file paths, streams, byte arrays and
ReadOnlySpan<byte>, in generic and non-generic form, with optional
Configuration, optional explicit IImageDecoder, and an optional
`out IImageFormat format`. Async variants exist for path and stream sources.

From a file path:

    public static Image Load(string path)
    public static Image Load(string path, out IImageFormat format)
    public static Image Load(string path, IImageDecoder decoder)
    public static Image Load(Configuration configuration, string path)
    public static Image Load(Configuration configuration, string path,
                             out IImageFormat format)
    public static Image Load(Configuration configuration, string path,
                             IImageDecoder decoder)
    public static Image<TPixel> Load<TPixel>(string path)
    public static Image<TPixel> Load<TPixel>(string path, out IImageFormat format)
    public static Image<TPixel> Load<TPixel>(string path, IImageDecoder decoder)
    public static Image<TPixel> Load<TPixel>(Configuration configuration, string path)
    public static Image<TPixel> Load<TPixel>(Configuration configuration, string path,
                                             out IImageFormat format)
    public static Image<TPixel> Load<TPixel>(Configuration configuration, string path,
                                             IImageDecoder decoder)
    public static Task<Image> LoadAsync(string path,
                                        CancellationToken cancellationToken = default)
    public static Task<Image> LoadAsync(string path, IImageDecoder decoder,
                                        CancellationToken cancellationToken = default)
    public static Task<Image<TPixel>> LoadAsync<TPixel>(string path,
                                        CancellationToken cancellationToken = default)
    public static Task<Image<TPixel>> LoadAsync<TPixel>(string path,
                                        IImageDecoder decoder,
                                        CancellationToken cancellationToken = default)

From a stream (same shapes, plus Configuration overloads):

    public static Image Load(Stream stream)
    public static Image Load(Stream stream, out IImageFormat format)
    public static Image Load(Stream stream, IImageDecoder decoder)
    public static Image Load(Configuration configuration, Stream stream)
    public static Task<Image> LoadAsync(Stream stream,
                                        CancellationToken cancellationToken = default)
    public static Task<Image> LoadAsync(Configuration configuration, Stream stream,
                                        CancellationToken cancellationToken = default)
    public static Image<TPixel> Load<TPixel>(Stream stream)
    public static Task<Image<TPixel>> LoadAsync<TPixel>(Stream stream,
                                        CancellationToken cancellationToken = default)

From bytes:

    public static Image Load(byte[] data)
    public static Image Load(byte[] data, out IImageFormat format)
    public static Image Load(byte[] data, IImageDecoder decoder)
    public static Image Load(ReadOnlySpan<byte> data)
    public static Image Load(ReadOnlySpan<byte> data, out IImageFormat format)
    public static Image Load(ReadOnlySpan<byte> data, IImageDecoder decoder)
    public static Image<TPixel> Load<TPixel>(byte[] data)
    public static Image<TPixel> Load<TPixel>(ReadOnlySpan<byte> data)

Examples:

    using var a = Image.Load("photo.jpg");                       // Image
    using var b = Image.Load<Rgba32>("photo.jpg");               // Image<Rgba32>
    using var c = Image.Load("photo.png", out var fmt);          // fmt.Name == "PNG"
    await using var s = File.OpenRead("photo.webp");
    using var d = await Image.LoadAsync(s);

    // Force a specific decoder and its options:
    using var e = Image.Load("frames.gif",
        new GifDecoder { DecodingMode = FrameDecodingMode.First });

3. SAVING IMAGES
----------------
On the Image instance (both overloads are instance methods, not extensions):

    public void Save(Stream stream, IImageEncoder encoder)
    public Task SaveAsync(Stream stream, IImageEncoder encoder,
                          CancellationToken cancellationToken = default)

Extension methods in CodeBrix.Imaging (ImageExtensions):

    public static void Save(this Image source, string path)
    public static void Save(this Image source, string path, IImageEncoder encoder)
    public static void Save(this Image source, Stream stream, IImageFormat format)
    public static Task SaveAsync(this Image source, string path,
                          CancellationToken cancellationToken = default)
    public static Task SaveAsync(this Image source, string path, IImageEncoder encoder,
                          CancellationToken cancellationToken = default)
    public static Task SaveAsync(this Image source, Stream stream, IImageFormat format,
                          CancellationToken cancellationToken = default)
    public static string ToBase64String(this Image source, IImageFormat format)
    public static byte[] ToByteArray(this Image source, IImageFormat format)
    public static byte[] ToByteArray(this Image source, IImageEncoder encoder)
    public static Task<byte[]> ToByteArrayAsync(this Image source, IImageFormat format,
                          CancellationToken cancellationToken = default)
    public static Task<byte[]> ToByteArrayAsync(this Image source, IImageEncoder encoder,
                          CancellationToken cancellationToken = default)

Per-format shorthands (also in CodeBrix.Imaging) — SaveAsBmp, SaveAsGif,
SaveAsJpeg, SaveAsPbm, SaveAsPng, SaveAsTga, SaveAsTiff, SaveAsWebp, each with
an Async twin, and each with four shapes:

    SaveAsPng(this Image source, string path)
    SaveAsPng(this Image source, string path, PngEncoder encoder)
    SaveAsPng(this Image source, Stream stream)
    SaveAsPng(this Image source, Stream stream, PngEncoder encoder)
    SaveAsPngAsync(this Image source, string path)
    SaveAsPngAsync(this Image source, string path, CancellationToken cancellationToken)
    SaveAsPngAsync(this Image source, string path, PngEncoder encoder,
                   CancellationToken cancellationToken = default)
    SaveAsPngAsync(this Image source, Stream stream,
                   CancellationToken cancellationToken = default)
    SaveAsPngAsync(this Image source, Stream stream, PngEncoder encoder,
                   CancellationToken cancellationToken = default)

Examples:

    image.Save("out.png");                        // encoder chosen by extension
    image.Save("out.jpg", new JpegEncoder { Quality = 85 });
    image.Save(stream, new PngEncoder());         // instance method
    image.Save(stream, PngFormat.Instance);       // format -> default encoder
    await image.SaveAsync(stream, new WebpEncoder(), cancellationToken);
    byte[] png = image.ToByteArray(PngFormat.Instance);
    string dataPayload = image.ToBase64String(JpegFormat.Instance);

`Save(path)` throws NotSupportedException if the extension is not one of the
eight supported formats. Use `DetectEncoder` to test first.

4. FORMAT DETECTION AND IDENTIFICATION
--------------------------------------
    public static IImageFormat DetectFormat(string filePath)
    public static IImageFormat DetectFormat(Stream stream)
    public static IImageFormat DetectFormat(byte[] data)
    public static IImageFormat DetectFormat(ReadOnlySpan<byte> data)
    public static IImageFormat DetectFormat(Configuration configuration, ...)
                                            // a Configuration-first overload
                                            // of each of the four above
    public static Task<IImageFormat> DetectFormatAsync(Stream stream,
                                        CancellationToken cancellationToken = default)
    public static Task<IImageFormat> DetectFormatAsync(Configuration configuration,
                                        Stream stream,
                                        CancellationToken cancellationToken = default)

    public static IImageInfo Identify(string filePath)
    public static IImageInfo Identify(string filePath, out IImageFormat format)
    public static IImageInfo Identify(Stream stream)
    public static IImageInfo Identify(Stream stream, out IImageFormat format)
    public static IImageInfo Identify(byte[] data)
    public static IImageInfo Identify(byte[] data, out IImageFormat format)
    public static IImageInfo Identify(Configuration configuration, string filePath,
                                      out IImageFormat format)
    public static IImageInfo Identify(Configuration configuration, Stream stream)
    public static IImageInfo Identify(Configuration configuration, Stream stream,
                                      out IImageFormat format)
    public static IImageInfo Identify(Configuration configuration, byte[] data,
                                      out IImageFormat format)
    public static Task<IImageInfo> IdentifyAsync(string filePath,
                                        CancellationToken cancellationToken = default)
    public static Task<IImageInfo> IdentifyAsync(Stream stream,
                                        CancellationToken cancellationToken = default)

IImageInfo exposes Width, Height, PixelType, Metadata, Format,
HorizontalResolution, VerticalResolution. The extension methods
`info.Size()` and `info.Bounds()` (in CodeBrix.Imaging) return Size and
Rectangle.

    using var stream = File.OpenRead("unknown");
    var format = Image.DetectFormat(stream);      // reads only the header;
                                                  // null if unrecognised
    stream.Position = 0;                          // REQUIRED — see below
    var info = Image.Identify(stream);            // reads only the header
    Console.WriteLine($"{format.Name} {info.Width}x{info.Height}");

STREAM POSITION: with the default configuration
`Configuration.ReadOrigin` is `ReadOrigin.Current`, so every Load/Identify/
DetectFormat starts reading at the stream's CURRENT position and leaves it
advanced. Reset `stream.Position = 0` between calls, or set
`configuration.ReadOrigin = ReadOrigin.Begin` on your own Configuration and
the library will rewind seekable streams for you. Non-seekable streams (an
HTTP request body, for instance) are buffered internally, so they work
without either step — but only once.

Also useful (CodeBrix.Imaging.Advanced):

    public static IImageEncoder DetectEncoder(this Image source, string filePath)

    var encoder = image.DetectEncoder("out.png");   // -> PngEncoder
    // throws NotSupportedException for an unknown extension,
    // ArgumentNullException for a null path.

5. LOADING FROM RAW PIXEL DATA
------------------------------
IMPORTANT: unlike the upstream library, `LoadPixelData` in CodeBrix.Imaging
REQUIRES a fourth argument — the IImageFormat the resulting image should be
associated with. Passing only three arguments is a compile error (CS1501).

    public static Image<TPixel> LoadPixelData<TPixel>(
        TPixel[] data, int width, int height, IImageFormat expectedFormat)
    public static Image<TPixel> LoadPixelData<TPixel>(
        ReadOnlySpan<TPixel> data, int width, int height, IImageFormat expectedFormat)
    public static Image<TPixel> LoadPixelData<TPixel>(
        byte[] data, int width, int height, IImageFormat expectedFormat)
    public static Image<TPixel> LoadPixelData<TPixel>(
        ReadOnlySpan<byte> data, int width, int height, IImageFormat expectedFormat)
    // plus a Configuration-first overload of each of the four above.

    byte[] rgba = GetPixels();          // R,G,B,A,R,G,B,A ... row-major
    using var image = Image.LoadPixelData<Rgba32>(rgba, 800, 600,
                                                  PngFormat.Instance);
    image.Save("out.png", new PngEncoder());

The `expectedFormat` argument sets `image.Metadata.ExpectedFormat`; it does
NOT restrict what you may later save as.

BGRA SOURCE DATA (PDFium, Direct2D, Cairo, GDI+, ...) — CodeBrix-only API:

    public static Image<Rgba32> LoadPixelDataFromBgra(
        byte[] data, int width, int height, IImageFormat expectedFormat)
    public static Image<Rgba32> LoadPixelDataFromBgra(
        ReadOnlySpan<byte> data, int width, int height, IImageFormat expectedFormat)
    public static Image<Rgba32> LoadPixelDataFromBgra(
        Configuration configuration, byte[] data, int width, int height,
        IImageFormat expectedFormat)
    public static Image<Rgba32> LoadPixelDataFromBgra(
        Configuration configuration, ReadOnlySpan<byte> data, int width, int height,
        IImageFormat expectedFormat)

It always returns Image<Rgba32> and reorders the channels internally with
SIMD (AVX2 eight pixels at a time, SSSE3 four at a time) straight into the
image buffer — no scalar swap loop and no intermediate array.

    using var page = Image.LoadPixelDataFromBgra(bgra, 2550, 3300,
                                                 PngFormat.Instance);

6. WRAPPING EXISTING MEMORY (ZERO-COPY)
---------------------------------------
`Image.WrapMemory` builds an Image<TPixel> over memory you already own; the
image does not copy and does not free the buffer.

    public static Image<TPixel> WrapMemory<TPixel>(
        Memory<TPixel> pixelMemory, int width, int height, IImageFormat expectedFormat)
    public static Image<TPixel> WrapMemory<TPixel>(
        IMemoryOwner<TPixel> pixelMemoryOwner, int width, int height,
        IImageFormat expectedFormat)
    public static Image<TPixel> WrapMemory<TPixel>(
        Memory<byte> byteMemory, int width, int height, IImageFormat expectedFormat)
    public static Image<TPixel> WrapMemory<TPixel>(
        IMemoryOwner<byte> byteMemoryOwner, int width, int height,
        IImageFormat expectedFormat)
    public static unsafe Image<TPixel> WrapMemory<TPixel>(
        void* pointer, int width, int height, IImageFormat expectedFormat)
    // each also has a (Configuration, ..., ImageMetadata) and a
    // (Configuration, ..., IImageFormat) overload.

The buffer must be exactly width * height pixels and must stay alive and
unmoved for the lifetime of the image.

================================================================================

ENCODER AND DECODER OPTIONS
===========================
Every encoder is a settable POCO implementing IImageEncoder; construct it with
an object initializer and pass it to Save/SaveAs*. Every decoder implements
IImageDecoder (and IImageInfoDetector) and is passed to Load.

PngEncoder (CodeBrix.Imaging.Formats.Png)
    PngBitDepth? BitDepth                  Bit1 | Bit2 | Bit4 | Bit8 | Bit16
    PngColorType? ColorType                Grayscale | Rgb | Palette |
                                           GrayscaleWithAlpha | RgbWithAlpha
    PngFilterMethod? FilterMethod          None | Sub | Up | Average | Paeth |
                                           Adaptive
    PngCompressionLevel CompressionLevel   Level0..Level9, plus the aliases
                                           NoCompression, BestSpeed,
                                           DefaultCompression (= Level6),
                                           BestCompression. Default:
                                           DefaultCompression.
    int TextCompressionThreshold           default 1024
    float? Gamma
    IQuantizer Quantizer                   used when ColorType is Palette
    byte Threshold                         default byte.MaxValue
    PngInterlaceMode? InterlaceMethod      None | Adam7
    PngChunkFilter? ChunkFilter            None | ExcludePhysicalChunk |
                                           ExcludeGammaChunk |
                                           ExcludeExifChunk |
                                           ExcludeTextChunks | ExcludeAll
    bool IgnoreMetadata
    PngTransparentColorMode TransparentColorMode    Preserve | Clear

    image.Save("out.png", new PngEncoder
    {
        ColorType = PngColorType.Palette,
        BitDepth = PngBitDepth.Bit8,
        CompressionLevel = PngCompressionLevel.BestCompression,
        Quantizer = KnownQuantizers.Wu,
        ChunkFilter = PngChunkFilter.ExcludeAll
    });

PngDecoder      bool IgnoreMetadata

JpegEncoder (CodeBrix.Imaging.Formats.Jpeg)
    int? Quality                           1..100; null = keep the source
                                           image's JpegMetadata.Quality
    JpegColorType? ColorType               YCbCrRatio420 | YCbCrRatio444 |
                                           YCbCrRatio422 | YCbCrRatio411 |
                                           YCbCrRatio410 | Luminance | Rgb |
                                           Cmyk

    image.Save("out.jpg", new JpegEncoder
    {
        Quality = 90,
        ColorType = JpegColorType.YCbCrRatio444
    });

JpegDecoder     bool IgnoreMetadata

GifEncoder (CodeBrix.Imaging.Formats.Gif)
    IQuantizer Quantizer                            default KnownQuantizers.Octree
    GifColorTableMode? ColorTableMode               Global | Local
    IPixelSamplingStrategy GlobalPixelSamplingStrategy
                                           default DefaultPixelSamplingStrategy;
                                           ExtensivePixelSamplingStrategy also
                                           ships

GifDecoder
    bool IgnoreMetadata                             default false
    FrameDecodingMode DecodingMode                  All | First (default All)
    uint MaxFrames                                  default uint.MaxValue —
                                                    a hard cap that protects
                                                    against decompression-bomb
                                                    GIFs

WebpEncoder (CodeBrix.Imaging.Formats.Webp)
    WebpFileFormatType? FileFormat         Lossless | Lossy
    int Quality                            default 75
    WebpEncodingMethod Method              Level0/Fastest .. Level6/BestQuality,
                                           default Default (= Level4)
    bool UseAlphaCompression                default true
    int EntropyPasses                       default 1
    int SpatialNoiseShaping                 default 50
    int FilterStrength                      default 60
    WebpTransparentColorMode TransparentColorMode   Clear | Preserve
    bool NearLossless
    int NearLosslessQuality                 default 100

WebpDecoder     bool IgnoreMetadata

BmpEncoder (CodeBrix.Imaging.Formats.Bmp)
    BmpBitsPerPixel? BitsPerPixel          Pixel1 | Pixel4 | Pixel8 | Pixel16 |
                                           Pixel24 | Pixel32
    bool SupportTransparency
    IQuantizer Quantizer                   used for the indexed bit depths

BmpDecoder
    RleSkippedPixelHandling RleSkippedPixelHandling  Black | Transparent |
                                                     FirstColorOfPalette

TiffEncoder (CodeBrix.Imaging.Formats.Tiff)
    TiffBitsPerPixel? BitsPerPixel         Bit1 | Bit4 | Bit6 | Bit8 | Bit10 |
                                           Bit12 | Bit14 | Bit16 | Bit24 |
                                           Bit30 | Bit36 | Bit42 | Bit48
    TiffCompression? Compression           None | Ccitt1D | CcittGroup3Fax |
                                           CcittGroup4Fax | Lzw | Jpeg |
                                           Deflate | PackBits | ... (see
                                           CodeBrix.Imaging.Formats.Tiff.Constants)
    DeflateCompressionLevel? CompressionLevel   Level0..Level9 with the same
                                           aliases as PNG
                                           (CodeBrix.Imaging.Compression.Zlib)
    TiffPhotometricInterpretation? PhotometricInterpretation
    TiffPredictor? HorizontalPredictor     None | Horizontal | FloatingPoint
    IQuantizer Quantizer

TiffDecoder
    bool IgnoreMetadata
    FrameDecodingMode DecodingMode         All | First

TgaEncoder (CodeBrix.Imaging.Formats.Tga)
    TgaBitsPerPixel? BitsPerPixel          Pixel8 | Pixel16 | Pixel24 | Pixel32
    TgaCompression Compression             None | RunLength (default RunLength)

PbmEncoder (CodeBrix.Imaging.Formats.Pbm)
    PbmEncoding? Encoding                  Plain | Binary
    PbmColorType? ColorType                BlackAndWhite | Grayscale | Rgb
    PbmComponentType? ComponentType        Bit | Byte | Short

TgaDecoder and PbmDecoder take no options.

================================================================================

RESIZE AND GEOMETRIC TRANSFORMS
===============================
All of these are extension methods on IImageProcessingContext in
CodeBrix.Imaging.Processing, i.e. they are used inside Mutate()/Clone().

MUTATE vs CLONE — the distinction that matters
----------------------------------------------
    public static void Mutate(this Image source,
                              Action<IImageProcessingContext> operation)
    public static void Mutate<TPixel>(this Image<TPixel> source,
                              Action<IImageProcessingContext> operation)
    public static void Mutate<TPixel>(this Image<TPixel> source,
                              Configuration configuration,
                              Action<IImageProcessingContext> operation)
    public static void Mutate<TPixel>(this Image<TPixel> source,
                              params IImageProcessor[] operations)

    public static Image Clone(this Image source,
                              Action<IImageProcessingContext> operation)
    public static Image<TPixel> Clone<TPixel>(this Image<TPixel> source,
                              Action<IImageProcessingContext> operation)
    public static Image<TPixel> Clone<TPixel>(this Image<TPixel> source,
                              Configuration configuration,
                              Action<IImageProcessingContext> operation)
    public static Image<TPixel> Clone<TPixel>(this Image<TPixel> source,
                              params IImageProcessor[] operations)

`Mutate` returns void and edits the receiver in place. `Clone` returns a NEW
image (which you must dispose) and leaves the receiver untouched. Use Clone
when you need thumbnails or variants beside the original:

    using var original  = Image.Load("photo.jpg");
    using var thumbnail = original.Clone(x => x.Resize(200, 200));
    // original still has its full size

RESIZE
------
    Resize(int width, int height)
    Resize(int width, int height, bool compand)
    Resize(int width, int height, IResampler sampler)
    Resize(int width, int height, IResampler sampler, bool compand)
    Resize(Size size)
    Resize(Size size, bool compand)
    Resize(Size size, IResampler sampler, bool compand)
    Resize(ResizeOptions options)

Passing 0 for one dimension preserves aspect ratio for that axis.

ResizeOptions (class, CodeBrix.Imaging.Processing):
    ResizeMode Mode                 default ResizeMode.Crop
    AnchorPositionMode Position     default AnchorPositionMode.Center
    PointF? CenterCoordinates
    Size Size
    IResampler Sampler              default KnownResamplers.Bicubic
    bool Compand                    default false
    Rectangle? TargetRectangle
    bool PremultiplyAlpha           default true
    Color PadColor

ResizeMode:        Crop, Pad, BoxPad, Max, Min, Stretch, Manual
AnchorPositionMode: Center, Top, Bottom, Left, Right, TopLeft, TopRight,
                   BottomRight, BottomLeft

KnownResamplers (static IResampler properties):
    Bicubic, Box, CatmullRom, Hermite, Lanczos2, Lanczos3, Lanczos5, Lanczos8,
    MitchellNetravali, NearestNeighbor, Robidoux, RobidouxSharp, Spline,
    Triangle, Welch

    image.Mutate(x => x.Resize(new ResizeOptions
    {
        Size = new Size(400, 400),
        Mode = ResizeMode.Pad,
        Position = AnchorPositionMode.Center,
        PadColor = Color.White,
        Sampler = KnownResamplers.Lanczos3
    }));

CROP, PAD AND ORIENTATION
-------------------------
    Crop(int width, int height)                     // from the top-left
    Crop(Rectangle cropRectangle)
    EntropyCrop()
    EntropyCrop(float threshold)
    Pad(int width, int height)                      // pads to a canvas size
    Pad(int width, int height, Color color)
    Flip(FlipMode flipMode)                         // None | Horizontal | Vertical
    Rotate(RotateMode rotateMode)                   // None | Rotate90 |
                                                    // Rotate180 | Rotate270
    Rotate(float degrees)                           // arbitrary angle, expands the canvas
    RotateFlip(RotateMode rotateMode, FlipMode flipMode)
    AutoOrient()                                    // applies the EXIF Orientation tag
    Skew(float degreesX, float degreesY)
    Skew(float degreesX, float degreesY, IResampler sampler)
    Swizzle<TSwizzler>(TSwizzler swizzler)          // TSwizzler : struct, ISwizzler

Rotate(90/180/270 as a float) and RotateMode.Rotate90/270 swap width and
height. Rotate(float) with an arbitrary angle grows the canvas to fit the
rotated bounds.

ARBITRARY TRANSFORMS
--------------------
    Transform(AffineTransformBuilder builder)
    Transform(AffineTransformBuilder builder, IResampler sampler)
    Transform(Rectangle sourceRectangle, AffineTransformBuilder builder,
              IResampler sampler)
    Transform(Rectangle sourceRectangle, Matrix3x2 transform,
              Size targetDimensions, IResampler sampler)
    Transform(ProjectiveTransformBuilder builder)
    Transform(ProjectiveTransformBuilder builder, IResampler sampler)
    Transform(Rectangle sourceRectangle, ProjectiveTransformBuilder builder,
              IResampler sampler)
    Transform(Rectangle sourceRectangle, Matrix4x4 transform,
              Size targetDimensions, IResampler sampler)

AffineTransformBuilder (2D affine, builds a Matrix3x2) — every method returns
the builder so calls chain, and each has a Prepend* and an Append* form:

    PrependRotationDegrees(float) / AppendRotationDegrees(float)
    PrependRotationDegrees(float, Vector2 origin) / Append...
    PrependRotationRadians(float) / AppendRotationRadians(float)
    PrependRotationRadians(float, Vector2 origin) / Append...
    PrependScale(float) / PrependScale(SizeF) / PrependScale(Vector2)
    AppendScale(float)  / AppendScale(SizeF)  / AppendScale(Vector2)
    PrependSkewDegrees(float x, float y) / ...(float, float, Vector2 origin)
    PrependSkewRadians(float x, float y) / ...(float, float, Vector2 origin)
    AppendSkewDegrees(...) / AppendSkewRadians(...)
    PrependTranslation(PointF) / PrependTranslation(Vector2)
    AppendTranslation(PointF)  / AppendTranslation(Vector2)
    PrependMatrix(Matrix3x2) / AppendMatrix(Matrix3x2)
    Matrix3x2 BuildMatrix(Size sourceSize)
    Matrix3x2 BuildMatrix(Rectangle sourceRectangle)

ProjectiveTransformBuilder (perspective, builds a Matrix4x4) has the same
Prepend/Append scale, skew, rotation, translation and matrix methods, plus:

    PrependTaper(TaperSide side, TaperCorner corner, float fraction)
    AppendTaper(TaperSide side, TaperCorner corner, float fraction)
    Matrix4x4 BuildMatrix(Size sourceSize)
    Matrix4x4 BuildMatrix(Rectangle sourceRectangle)

    TaperSide:   Left, Top, Right, Bottom
    TaperCorner: LeftOrTop, RightOrBottom, Both

    var builder = new AffineTransformBuilder()
        .AppendRotationDegrees(15f)
        .AppendScale(new SizeF(1.2f, 1.2f))
        .AppendTranslation(new PointF(20, 0));
    image.Mutate(x => x.Transform(builder, KnownResamplers.Bicubic));

Transform throws DegenerateTransformException
(CodeBrix.Imaging.Processing.Processors.Transforms) if the composed matrix
collapses the image to zero area.

================================================================================

IMAGE PROCESSING OPERATIONS
===========================
All operations below are IImageProcessingContext extension methods in
CodeBrix.Imaging.Processing, used inside Mutate()/Clone(). Almost every one
also has a `Rectangle rectangle` overload that restricts the effect to a
sub-region — those are listed once here rather than repeated per method.

COLOUR AND TONE FILTERS
-----------------------
    BlackWhite()                            BlackWhite(Rectangle)
    Grayscale()                             Grayscale(float amount)
    Grayscale(GrayscaleMode mode)           Grayscale(GrayscaleMode, float amount)
                                            (+ Rectangle overloads of all four)
    Invert()                                Invert(Rectangle)
    Sepia()                                 Sepia(float amount)
    Kodachrome()                            Polaroid()      Lomograph()
    Brightness(float amount)                // 1.0 = unchanged
    Contrast(float amount)                  // 1.0 = unchanged
    Saturate(float amount)                  // 1.0 = unchanged, 0 = greyscale
    Lightness(float amount)
    Hue(float degrees)
    Opacity(float amount)                   // 0..1
    ColorBlindness(ColorBlindnessMode mode)
    Filter(ColorMatrix matrix)              // arbitrary 5x4 colour matrix

    GrayscaleMode:      Bt709 (default), Bt601
    ColorBlindnessMode: Achromatomaly, Achromatopsia, Deuteranomaly,
                        Deuteranopia, Protanomaly, Protanopia, Tritanomaly,
                        Tritanopia

NOTE: the method is `Saturate`, not "Saturation".

KnownFilterMatrices (static, CodeBrix.Imaging.Processing) exposes the same
matrices for use with Filter(...):
    AchromatomalyFilter, AchromatopsiaFilter, DeuteranomalyFilter,
    DeuteranopiaFilter, ProtanomalyFilter, ProtanopiaFilter,
    TritanomalyFilter, TritanopiaFilter, BlackWhiteFilter, KodachromeFilter,
    LomographFilter, PolaroidFilter
and factory methods:
    CreateBrightnessFilter(float), CreateContrastFilter(float),
    CreateGrayscaleBt601Filter(float), CreateGrayscaleBt709Filter(float),
    CreateHueFilter(float), CreateInvertFilter(float),
    CreateOpacityFilter(float), CreateSaturateFilter(float),
    CreateLightnessFilter(float), CreateSepiaFilter(float)

    image.Mutate(x => x.Filter(KnownFilterMatrices.CreateSaturateFilter(1.4f)));

BLUR, SHARPEN AND CONVOLUTION
-----------------------------
    GaussianBlur()                    GaussianBlur(float sigma)
    GaussianSharpen()                 GaussianSharpen(float sigma)
    BoxBlur()                         BoxBlur(int radius)
    BokehBlur()                       BokehBlur(int radius, int components, float gamma)
    DetectEdges()                     // Sobel by default, grayscale = true
    DetectEdges(EdgeDetectorKernel kernel[, bool grayscale])
    DetectEdges(EdgeDetector2DKernel kernel[, bool grayscale])
    DetectEdges(EdgeDetectorCompassKernel kernel[, bool grayscale])

KnownEdgeDetectorKernels (CodeBrix.Imaging.Processing):
    EdgeDetector2DKernel:      Kayyali, Prewitt, RobertsCross, Scharr, Sobel
    EdgeDetectorKernel:        Laplacian3x3, Laplacian5x5, LaplacianOfGaussian
    EdgeDetectorCompassKernel: Kirsch, Robinson

    image.Mutate(x => x.DetectEdges(KnownEdgeDetectorKernels.Scharr, false));

EFFECTS
-------
    OilPaint()                        OilPaint(Rectangle)
    Pixelate()                        Pixelate(int size)
    Vignette()                        Vignette(Color)     Vignette(GraphicsOptions)
    Glow()                            Glow(Color)         Glow(float radius)
                                      Glow(GraphicsOptions)
    ProcessPixelRowsAsVector4(PixelRowOperation rowOperation)
    ProcessPixelRowsAsVector4(PixelRowOperation rowOperation,
                              PixelConversionModifiers modifiers)
    ProcessPixelRowsAsVector4(PixelRowOperation<Point> rowOperation)
    (+ Rectangle and modifier overloads of each)

    delegate void PixelRowOperation(Span<Vector4> span);
    delegate void PixelRowOperation<in T>(Span<Vector4> span, T value);

ProcessPixelRowsAsVector4 is the supported hook for a custom per-pixel effect
that still participates in the Mutate pipeline:

    image.Mutate(x => x.ProcessPixelRowsAsVector4(row =>
    {
        for (var i = 0; i < row.Length; i++)
        {
            row[i] = new Vector4(row[i].Z, row[i].Y, row[i].X, row[i].W);
        }
    }));

NORMALIZATION
-------------
    HistogramEqualization()
    HistogramEqualization(HistogramEqualizationOptions options)

HistogramEqualizationOptions:
    HistogramEqualizationMethod Method   Global (default) |
                                         AdaptiveTileInterpolation |
                                         AdaptiveSlidingWindow
    int LuminanceLevels                  default 256
    bool ClipHistogram                   default false
    int ClipLimit                        default 350
    int NumberOfTiles                    default 8

================================================================================

QUANTIZATION, DITHERING AND BINARIZATION
========================================

QUANTIZE
--------
    Quantize()                                   // KnownQuantizers.Octree
    Quantize(IQuantizer quantizer)
    Quantize(Rectangle rectangle)
    Quantize(IQuantizer quantizer, Rectangle rectangle)

KnownQuantizers (static IQuantizer, CodeBrix.Imaging.Processing):
    Octree   — adaptive octree palette (the default)
    Wu       — Xiaolin Wu's colour quantizer, usually the best quality
    WebSafe  — the fixed 216-colour web-safe palette
    Werner   — Werner's Nomenclature of Colours palette

Quantizer types (CodeBrix.Imaging.Processing.Processors.Quantization):
    OctreeQuantizer(), OctreeQuantizer(QuantizerOptions)
    WuQuantizer(), WuQuantizer(QuantizerOptions)
    PaletteQuantizer(ReadOnlyMemory<Color> palette)
    PaletteQuantizer(ReadOnlyMemory<Color> palette, QuantizerOptions options)
    WebSafePaletteQuantizer, WernerPaletteQuantizer
    All expose `QuantizerOptions Options { get; }` and
    `IQuantizer<TPixel> CreatePixelSpecificQuantizer<TPixel>(Configuration)`
    plus a (Configuration, QuantizerOptions) overload of the same.

QuantizerOptions:
    IDither Dither      default QuantizerConstants.DefaultDither
                        (= KnownDitherings.FloydSteinberg); set to null to
                        disable dithering
    float DitherScale   0..1
    int MaxColors       1..256

QuantizerConstants: MinColors = 1, MaxColors = 256, MinDitherScale = 0,
MaxDitherScale = 1, DefaultDither.

    image.Mutate(x => x.Quantize(new WuQuantizer(new QuantizerOptions
    {
        MaxColors = 64,
        Dither = KnownDitherings.Bayer8x8,
        DitherScale = 0.75f
    })));

    Color[] brand = { Color.Black, Color.White, Color.ParseHex("#1E88E5") };
    image.Mutate(x => x.Quantize(new PaletteQuantizer(brand)));

DITHER
------
    Dither()                                     // KnownDitherings.Bayer8x8
    Dither(IDither dither)
    Dither(IDither dither, float ditherScale)
    Dither(IDither dither, ReadOnlyMemory<Color> palette)
    Dither(IDither dither, float ditherScale, ReadOnlyMemory<Color> palette)
    (+ a Rectangle overload of each)

KnownDitherings (static IDither, CodeBrix.Imaging.Processing):
    Ordered:         Bayer2x2, Ordered3x3, Bayer4x4, Bayer8x8, Bayer16x16
    Error-diffusion: Atkinson, Burks, FloydSteinberg, JarvisJudiceNinke,
                     Sierra2, Sierra3, SierraLite, StevensonArce, Stucki

Built-in palettes usable as the `palette` argument:
    Color.WebSafePalette, Color.WernerPalette  (both ReadOnlyMemory<Color>)

BINARIZATION
------------
    BinaryThreshold(float threshold)                          // 0..1
    BinaryThreshold(float threshold, BinaryThresholdMode mode)
    BinaryThreshold(float threshold, Color upperColor, Color lowerColor)
    BinaryThreshold(float threshold, Color upperColor, Color lowerColor,
                    BinaryThresholdMode mode)
    (+ a Rectangle overload of each)

    BinaryDither(IDither dither)
    BinaryDither(IDither dither, Color upperColor, Color lowerColor)
    (+ Rectangle overloads; defaults are Color.White / Color.Black)

    AdaptiveThreshold()
    AdaptiveThreshold(float thresholdLimit)
    AdaptiveThreshold(Color upper, Color lower)
    AdaptiveThreshold(Color upper, Color lower, float thresholdLimit)
    AdaptiveThreshold(Color upper, Color lower, Rectangle rectangle)
    AdaptiveThreshold(Color upper, Color lower, float thresholdLimit,
                      Rectangle rectangle)

    BinaryThresholdMode: Luminance (default), Saturation, MaxChroma

    image.Mutate(x => x.BinaryThreshold(0.5f, BinaryThresholdMode.Luminance));

================================================================================

COMPOSITING AND OVERLAYS
========================

DRAWIMAGE — compositing one image onto another
----------------------------------------------
    DrawImage(Image image, float opacity)
    DrawImage(Image image, PixelColorBlendingMode colorBlending, float opacity)
    DrawImage(Image image, PixelColorBlendingMode colorBlending,
              PixelAlphaCompositionMode alphaComposition, float opacity)
    DrawImage(Image image, GraphicsOptions options)
    DrawImage(Image image, Point location, float opacity)
    DrawImage(Image image, Point location, PixelColorBlendingMode colorBlending,
              float opacity)
    DrawImage(Image image, Point location, PixelColorBlendingMode colorBlending,
              PixelAlphaCompositionMode alphaComposition, float opacity)
    DrawImage(Image image, Point location, GraphicsOptions options)

    using var background = Image.Load("bg.png");
    using var logo       = Image.Load("logo.png");
    background.Mutate(x => x.DrawImage(logo, new Point(20, 20), 0.85f));

PixelColorBlendingMode and PixelAlphaCompositionMode live in
CodeBrix.Imaging.PixelFormats.

BACKGROUND, GLOW AND VIGNETTE
-----------------------------
    BackgroundColor(Color color)
    BackgroundColor(Color color, Rectangle rectangle)
    BackgroundColor(GraphicsOptions options, Color color)
    BackgroundColor(GraphicsOptions options, Color color, Rectangle rectangle)

BackgroundColor fills the transparent areas underneath the existing pixels —
use it to flatten an image with alpha before saving to JPEG (which has no
alpha channel):

    image.Mutate(x => x.BackgroundColor(Color.White));
    image.Save("flat.jpg", new JpegEncoder { Quality = 90 });

GraphicsOptions (CodeBrix.Imaging) controls blending for the overlay
operations:
    bool Antialias                                  default true
    int AntialiasSubpixelDepth
    float BlendPercentage                           0..1
    PixelColorBlendingMode ColorBlendingMode        default Normal
    PixelAlphaCompositionMode AlphaCompositionMode  default SrcOver

Defaults can be set per pipeline or per configuration:

    context.SetGraphicsOptions(o => o.Antialias = false);
    configuration.SetGraphicsOptions(new GraphicsOptions { BlendPercentage = 0.5f });
    var current = context.GetGraphicsOptions();

================================================================================

PIXEL ACCESS
============
Three levels, cheapest-to-safest first.

1. THE INDEXER (simple, per-pixel, bounds-checked)
--------------------------------------------------
`Image<TPixel>` and `ImageFrame<TPixel>` both expose:

    public TPixel this[int x, int y] { get; set; }

    using var image = new Image<Rgba32>(64, 64);
    image[10, 20] = new Rgba32(255, 0, 0, 255);
    var pixel = image[10, 20];

The indexer is only on the GENERIC type. If you have a non-generic `Image`,
call `CloneAs<Rgba32>()` (or load with `Image.Load<Rgba32>`) first.

2. PROCESSPIXELROWS (fast, row-at-a-time, safe)
-----------------------------------------------
    public void ProcessPixelRows(PixelAccessorAction<TPixel> processPixels)
    public void ProcessPixelRows<TPixel2>(Image<TPixel2> image2,
                          PixelAccessorAction<TPixel, TPixel2> processPixels)
    public void ProcessPixelRows<TPixel2, TPixel3>(Image<TPixel2> image2,
                          Image<TPixel3> image3,
                          PixelAccessorAction<TPixel, TPixel2, TPixel3> processPixels)

The same three methods exist on ImageFrame<TPixel>.

    public ref struct PixelAccessor<TPixel>
    {
        public int Width { get; }
        public int Height { get; }
        public Span<TPixel> GetRowSpan(int rowIndex);
    }

    image.ProcessPixelRows(accessor =>
    {
        for (var y = 0; y < accessor.Height; y++)
        {
            var row = accessor.GetRowSpan(y);
            for (var x = 0; x < row.Length; x++)
            {
                row[x].A = 128;
            }
        }
    });

The buffer is pinned for the duration of the callback. Do NOT let the
PixelAccessor or any Span escape the callback, and do not resize the image
inside it.

3. BULK COPY AND DANGEROUS ACCESS
---------------------------------
    public void CopyPixelDataTo(Span<TPixel> destination)
    public void CopyPixelDataTo(Span<byte> destination)
    public bool DangerousTryGetSinglePixelMemory(out Memory<TPixel> memory)

    // CodeBrix.Imaging.Processing:
    public static Buffer2D<ulong> CalculateIntegralImage<TPixel>(
        this Image<TPixel> source)

    // CodeBrix.Imaging.Advanced:
    public static Memory<TPixel> DangerousGetPixelRowMemory<TPixel>(
        this Image<TPixel> source, int rowIndex)
    public static Memory<TPixel> DangerousGetPixelRowMemory<TPixel>(
        this ImageFrame<TPixel> source, int rowIndex)
    public static IMemoryGroup<TPixel> GetPixelMemoryGroup<TPixel>(
        this Image<TPixel> source)

`DangerousTryGetSinglePixelMemory` succeeds only when the backing buffer is
contiguous; set `Configuration.PreferContiguousImageBuffers = true` (on a
non-global Configuration instance, not Configuration.Default) before loading
if you need that. Disposing or leaking the image while still holding the
returned Memory/Span can corrupt memory.
`DangerousGetPixelRowMemory` throws ArgumentOutOfRangeException for an
out-of-range row index.

================================================================================

PIXEL FORMATS
=============
`Image<TPixel>` is generic over 29 pixel structs, all in
CodeBrix.Imaging.PixelFormats and all implementing IPixel<TPixel> (most also
IPackedVector<TPacked>). This is far more than the Rgba32/Rgb24 pair most code
uses — pick the narrowest one that carries the data you actually have.

8-BIT-PER-CHANNEL COLOUR
    Rgba32     32 bpp  R,G,B,A byte order. The default and the one most APIs
                       (LoadPixelDataFromBgra, ImageGlyphRenderer) work in.
    Rgb24      24 bpp  R,G,B — no alpha, 25% less memory than Rgba32.
    Bgra32     32 bpp  B,G,R,A — binary compatible with
                       System.Drawing.Imaging.PixelFormat.Format32bppArgb, so
                       it round-trips through LockBits buffers byte for byte.
    Bgr24      24 bpp  B,G,R.
    Argb32     32 bpp  A,R,G,B.
    Abgr32     32 bpp  A,B,G,R.
    Byte4      32 bpp  four raw bytes 0..255 (not normalized colour).

GREYSCALE AND ALPHA-ONLY
    L8          8 bpp  single 8-bit luminance.
    L16        16 bpp  single 16-bit luminance.
    La16       16 bpp  8-bit luminance + 8-bit alpha.
    La32       32 bpp  16-bit luminance + 16-bit alpha.
    A8          8 bpp  alpha only.

16-BIT-PER-CHANNEL AND HDR
    Rgb48      48 bpp  three 16-bit channels.
    Rgba64     64 bpp  four 16-bit channels.
    Rg32       32 bpp  two 16-bit normalized channels.
    RgbaVector 128 bpp four 32-bit floats, unpacked; the widest-gamut option.
    HalfSingle  16 bpp one 16-bit float.
    HalfVector2 32 bpp two 16-bit floats.
    HalfVector4 64 bpp four 16-bit floats.

PACKED / LOW-BIT-DEPTH
    Bgr565     16 bpp  5-6-5.
    Bgra4444   16 bpp  4-4-4-4.
    Bgra5551   16 bpp  5-5-5-1.
    Rgba1010102 32 bpp 10-10-10-2.

SIGNED / NON-COLOUR DATA
    NormalizedByte2, NormalizedByte4     signed normalized -1..1, 8 bit
    NormalizedShort2, NormalizedShort4   signed normalized -1..1, 16 bit
    Short2, Short4                       signed 16-bit integers

Every struct offers the same shape (shown for Rgba32):

    public Rgba32(byte r, byte g, byte b)
    public Rgba32(byte r, byte g, byte b, byte a)
    public Rgba32(float r, float g, float b, float a = 1)
    public Rgba32(Vector3 vector)
    public Rgba32(Vector4 vector)
    public Rgba32(uint packed)
    public byte R, G, B, A;                       // public mutable fields
    public uint PackedValue { get; set; }
    public uint Rgba { get; set; }
    public Rgb24 Rgb { get; set; }
    public static Rgba32 ParseHex(string hex)
    public static bool TryParseHex(string hex, out Rgba32 result)
    public readonly string ToHex()
    public void FromVector4(Vector4) / public readonly Vector4 ToVector4()
    public void FromScaledVector4(Vector4) / ToScaledVector4()
    public void FromRgba32/FromBgra32/FromArgb32/FromAbgr32/FromRgb24/
                FromBgr24/FromL8/FromL16/FromLa16/FromLa32/FromRgb48/
                FromRgba64/FromBgra5551(...)
    public static implicit operator Color(Rgba32)
    public static implicit operator Rgba32(Color)

Converting between pixel formats is a whole-image Clone:

    using var wide   = Image.Load<Rgba64>("scan.tiff");
    using var narrow = wide.CloneAs<Rgb24>();

Pixel type metadata at runtime:

    PixelTypeInfo info = image.PixelType;
    int bpp = info.BitsPerPixel;                       // 32 for Rgba32
    PixelAlphaRepresentation? a = info.AlphaRepresentation;

================================================================================

COLOR API
=========
`Color` (CodeBrix.Imaging) is a pixel-format-independent colour value.

    public static Color FromRgba(byte r, byte g, byte b, byte a)
    public static Color FromRgb(byte r, byte g, byte b)
    public static Color FromArgb(int argb)
    public static Color FromArgb(int r, int g, int b)
    public static Color FromArgb(int a, int r, int g, int b)
    public static Color FromArgb(int a, Color c)
    public static Color FromPixel<TPixel>(TPixel pixel)
    public static Color ParseHex(string hex)
    public static bool TryParseHex(string hex, out Color result)
    public static Color Parse(string input)
    public static bool TryParse(string input, out Color result)
    public Color WithAlpha(float alpha)            // 0..1, returns a new value
    public string ToHex()
    public TPixel ToPixel<TPixel>()
    public static void ToPixel<TPixel>(Configuration configuration,
                                       ReadOnlySpan<Color> source,
                                       Span<TPixel> destination)
    public static explicit operator Vector4(Color)
    public static explicit operator Color(Vector4)

Hex parsing accepts 3, 4, 6 or 8 hex digits, with or without a leading '#'.
3/4-digit forms are expanded (F0A -> FF00AA), 6-digit forms get an opaque
alpha appended.

    Color.ParseHex("#FF0000");   // opaque red
    Color.ParseHex("0000FF");    // opaque blue
    Color.ParseHex("FFF");       // opaque white
    Color.ParseHex("#00FF00FF"); // opaque green (RGBA order)

`Color.Parse` first tries the 150 W3C named colours (case-INSENSITIVE) and
falls back to hex parsing, so both of these work:

    Color.Parse("Red");
    Color.Parse("rebeccapurple");   // any of the 149 names, any casing
    Color.Parse("#336699");

Color declares 149 named-colour constants (AliceBlue ... YellowGreen,
including Transparent and RebeccaPurple) — all of them resolvable by
Color.Parse — plus `Color.Empty` (fully transparent black), which is a
constant only and is NOT in the name lookup. There are also two ready-made
palettes:

    ReadOnlyMemory<Color> Color.WebSafePalette
    ReadOnlyMemory<Color> Color.WernerPalette

Color and the pixel structs interconvert freely:

    Rgba32 px = Color.Red;                      // implicit
    Color   c = new Rgba32(0, 128, 255, 255);   // implicit
    Bgra32 b  = Color.Red.ToPixel<Bgra32>();

================================================================================

COLOR SPACES
============
CodeBrix.Imaging.ColorSpaces provides the strongly typed colour-space structs
CieLab, CieLch, CieLchuv, CieLuv, CieXyy, CieXyz, Cmyk, Hsl, Hsv, HunterLab,
LinearRgb, Lms, Rgb and YCbCr, plus Illuminants and RgbWorkingSpaces.

`ColorSpaceConverter` (CodeBrix.Imaging.ColorSpaces.Conversion) converts
between all of them, with ToCieLab, ToCieLch, ToCieLchuv, ToCieLuv, ToCieXyy,
ToCieXyz, ToCmyk, ToHsl, ToHsv, ToHunterLab, ToLinearRgb, ToLms, ToRgb and
ToYCbCr overloads for each source type.

    public ColorSpaceConverter()
    public ColorSpaceConverter(ColorSpaceConverterOptions options)

ColorSpaceConverterOptions:
    CieXyz WhitePoint, TargetLuvWhitePoint, TargetLabWhitePoint,
           TargetHunterLabWhitePoint
    RgbWorkingSpace TargetRgbWorkingSpace
    IChromaticAdaptation ChromaticAdaptation      default VonKriesChromaticAdaptation
    Matrix4x4 LmsAdaptationMatrix

    var converter = new ColorSpaceConverter();
    var lab = converter.ToCieLab(new Rgb(0.2f, 0.5f, 0.9f));

Companding helpers (SRgbCompanding, GammaCompanding, LCompanding,
Rec709Companding, Rec2020Companding) live in
CodeBrix.Imaging.ColorSpaces.Companding.

================================================================================

TEXT AND FONT RENDERING
=======================
READ THIS FIRST. Two rules decide whether your code compiles:

  1. The using directives are
         using CodeBrix.Imaging.Fonts;            // Font, SystemFonts, TextOptions
         using CodeBrix.Imaging.Fonts.Rendering;  // DrawText, MeasureText
     There is NO `CodeBrix.Imaging.Drawing` namespace.

  2. DrawText is an extension method ON THE IMAGE, not on the Mutate()
     processing context:
         image.DrawText("hello", font, Color.White, 10f, 10f);      // CORRECT
         image.Mutate(x => x.DrawText(...));                        // WRONG —
                                                                    // does not compile

DRAWTEXT OVERLOADS (TextRenderingExtensions)
--------------------------------------------
    public static Image DrawText(this Image image, string text, Font font,
        Color color, Vector2 location, bool forceMonoColor = false)
    public static Image DrawText(this Image image, string text, Font font,
        Color color, float x, float y, bool forceMonoColor = false)
    public static Image DrawText(this Image image, string text,
        TextOptions options, Color color)

    public static Image<TPixel> DrawText<TPixel>(this Image<TPixel> image,
        string text, Font font, TPixel color, Vector2 location,
        bool forceMonoColor = false)
    public static Image<TPixel> DrawText<TPixel>(this Image<TPixel> image,
        string text, Font font, TPixel color, float x, float y,
        bool forceMonoColor = false)
    public static Image<TPixel> DrawText<TPixel>(this Image<TPixel> image,
        string text, TextOptions options, TPixel color)
    public static Image<TPixel> DrawText<TPixel>(this Image<TPixel> image,
        string text, Font font, Color color, Vector2 location,
        bool forceMonoColor = false)
    public static Image<TPixel> DrawText<TPixel>(this Image<TPixel> image,
        string text, TextOptions options, Color color)

Every overload returns the same image instance so calls chain. `text` that is
null or empty is a no-op; a null image or null font/options throws
ArgumentNullException. The non-generic Image overloads recover the pixel type
internally through the visitor pattern, so you do not need to know it.

`forceMonoColor` only matters for colour fonts (COLR/CPAL, e.g. an emoji
font): false (the default) renders the font's own glyph colours, true forces
the colour you passed.

MEASURING
---------
    public static FontRectangle MeasureText(string text, Font font)
    public static FontRectangle MeasureText(string text, TextOptions options)

    // CodeBrix.Imaging.Fonts.TextMeasurer — the full measurement API:
    public static FontRectangle Measure(string text, TextOptions options)
    public static FontRectangle Measure(ReadOnlySpan<char> text, TextOptions options)
    public static FontRectangle MeasureBounds(string text, TextOptions options)
    public static FontRectangle MeasureBounds(ReadOnlySpan<char> text,
                                              TextOptions options)
    public static bool TryMeasureCharacterDimensions(string text,
        TextOptions options, out GlyphBounds[] characterBounds)
    public static bool TryMeasureCharacterBounds(string text,
        TextOptions options, out GlyphBounds[] characterBounds)
    public static int CountLines(string text, TextOptions options)

`Measure` returns the advance-based size; `MeasureBounds` returns the tight
ink bounds. FontRectangle is a readonly struct with X, Y, Width, Height,
Location, Size, Left, Top, Right, Bottom, IsEmpty, plus Empty, FromLTRB,
Center, Intersect, Inflate, Union, Transform, Contains and Deconstruct.

FONTS
-----
SystemFonts (static, CodeBrix.Imaging.Fonts):
    public static IReadOnlySystemFontCollection Collection { get; }
    public static IEnumerable<FontFamily> Families { get; }
    public static FontFamily Get(string name)
    public static FontFamily Get(string fontFamily, CultureInfo culture)
    public static bool TryGet(string fontFamily, out FontFamily family)
    public static bool TryGet(string fontFamily, CultureInfo culture,
                              out FontFamily family)
    public static Font CreateFont(string name, float size)
    public static Font CreateFont(string name, float size, FontStyle style)
    public static Font CreateFont(string name, CultureInfo culture, float size)
    public static Font CreateFont(string name, CultureInfo culture, float size,
                                  FontStyle style)
    public static IEnumerable<FontFamily> GetByCulture(CultureInfo culture)

FontCollection (embed your own fonts — the reliable option):
    public FontCollection()
    public FontFamily Add(string path)
    public FontFamily Add(string path, out FontDescription description)
    public FontFamily Add(Stream stream)
    public FontFamily Add(Stream stream, out FontDescription description)
    public FontFamily Add(string path, CultureInfo culture)
    public FontFamily Add(Stream stream, CultureInfo culture)
    public IEnumerable<FontFamily> AddCollection(string path)     // .ttc
    public IEnumerable<FontFamily> AddCollection(Stream stream)
    public FontFamily Get(string name)
    public FontFamily Get(string name, CultureInfo culture)
    public bool TryGet(string name, out FontFamily family)
    public IEnumerable<FontFamily> GetByCulture(CultureInfo culture)
    // extension methods (CodeBrix.Imaging.Fonts):
    public static FontCollection AddSystemFonts(this FontCollection collection)
    public static FontCollection AddSystemFonts(this FontCollection collection,
                                                Predicate<FontMetrics> match)

FontFamily:
    public string Name { get; }
    public CultureInfo Culture { get; }
    public Font CreateFont(float size)
    public Font CreateFont(float size, FontStyle style)
    public IEnumerable<FontStyle> GetAvailableStyles()
    public bool TryGetPaths(out IEnumerable<string> paths)
    public static FontFamily GetFontByName(string name)

Font:
    public Font(FontFamily family, float size)
    public Font(FontFamily family, float size, FontStyle style)
    public Font(string name, float size, FontStyle style)
    public Font(Font prototype, float size)                  // prototype pattern
    public Font(Font prototype, FontStyle style)
    public Font(Font prototype, float size, FontStyle style)
    public FontFamily Family { get; }
    public string Name { get; }
    public float Size { get; }
    public FontMetrics FontMetrics { get; }
    public bool IsBold / IsItalic / IsUnderline / IsStrikeout { get; }
    public bool IsColorFont { get; }
    public ColorFontFormat ColorFormat { get; }
    public bool TryGetPath(out string path)
    public IEnumerable<Glyph> GetGlyphs(CodePoint codePoint,
                                        ColorFontSupport support)
    public IEnumerable<Glyph> GetGlyphs(CodePoint codePoint,
                                        TextAttributes textAttributes,
                                        ColorFontSupport support)

    (ColorFontFormat is in CodeBrix.Imaging.Fonts; CodePoint is in
     CodeBrix.Imaging.Fonts.Unicode.)

FontStyle is a [Flags] enum: Regular, Bold, Italic, BoldItalic, Underline,
Strikeout.

Supported font files: TrueType (.ttf) including variable fonts, CFF/Type2
outlines, colour (COLR/CPAL) fonts, WOFF and WOFF2, and TrueType collections
(.ttc) via AddCollection.

TEXTOPTIONS — LAYOUT CONTROL
----------------------------
    public TextOptions(Font font)
    public TextOptions(TextOptions options)          // copy constructor

    Font Font                                  { get; set; }
    IReadOnlyList<FontFamily> FallbackFontFamilies   default empty
    float Dpi
    float TabWidth
    HintingMode HintingMode                    None | HintY | HintXY
    float LineSpacing
    Vector2 Origin                             default Vector2.Zero
    float WrappingLength                       default -1 (no wrapping)
    WordBreaking WordBreaking                  Normal | BreakAll | KeepAll
    TextDirection TextDirection                LeftToRight | RightToLeft |
                                               Auto (default)
    TextAlignment TextAlignment                Start | End | Center
    TextJustification TextJustification        None | InterWord | InterCharacter
    HorizontalAlignment HorizontalAlignment    Left | Right | Center
    VerticalAlignment VerticalAlignment        Top | Center | Bottom
    LayoutMode LayoutMode                      HorizontalTopBottom |
                                               HorizontalBottomTop |
                                               VerticalLeftRight |
                                               VerticalRightLeft
    KerningMode KerningMode                    Normal | None | Auto
    ColorFontSupport ColorFontSupport          None | MicrosoftColrFormat
                                               (default MicrosoftColrFormat)
    IReadOnlyList<Tag> FeatureTags             OpenType feature tags; Tag is in
                                               CodeBrix.Imaging.Fonts
                                               .Tables.AdvancedTypographic
    IReadOnlyList<TextRun> TextRuns            per-range font/decoration

TextRun (mixed formatting inside one string):
    int Start, int End
    Font Font
    TextAttributes TextAttributes
    TextDecorations TextDecorations            None | Underline | Strikeout |
                                               Overline
    public ReadOnlySpan<char> Slice(ReadOnlySpan<char> text)

GLYPH FALLBACK: if any codepoint is missing from the primary Font, layout
retries the whole string against each family in
`TextOptions.FallbackFontFamilies`, in order, at the same size and style,
until one run completes. Codepoints still unresolved after that render as the
font's glyph 0 (.notdef). Set FallbackFontFamilies explicitly for multi-script
or emoji text — there is no automatic system-wide fallback.

    var options = new TextOptions(font)
    {
        Origin = new Vector2(20, 20),
        WrappingLength = 400,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        WordBreaking = WordBreaking.Normal,
        FallbackFontFamilies = new[] { emojiFamily }
    };
    image.DrawText("hello \U0001F600 world", options, Color.Black);

LOW-LEVEL RENDERING
-------------------
`TextRenderer` drives any IGlyphRenderer, and `ImageGlyphRenderer<TPixel>`
(CodeBrix.Imaging.Fonts.Rendering) is the built-in one that rasterizes onto an
Image<TPixel>:

    public TextRenderer(IGlyphRenderer renderer)
    public static void RenderTextTo(IGlyphRenderer renderer, string text,
                                    TextOptions options)
    public static void RenderTextTo(IGlyphRenderer renderer,
                                    ReadOnlySpan<char> text, TextOptions options)
    public void RenderText(string text, TextOptions options)

    public ImageGlyphRenderer(Image<TPixel> image, TPixel color)

Outlines are filled with the NON-ZERO WINDING rule (so a glyph counter — the
hole in an 'o' — comes from an oppositely-wound contour), and coverage is
alpha-blended over the destination.

================================================================================

IMAGE METADATA AND PROFILES
===========================
`image.Metadata` is an ImageMetadata (CodeBrix.Imaging.Metadata);
`image.Frames[i].Metadata` is an ImageFrameMetadata.

ImageMetadata:
    double HorizontalResolution { get; set; }        // default 96
    double VerticalResolution { get; set; }          // default 96
    PixelResolutionUnit ResolutionUnits { get; set; } // default PixelsPerInch
    ExifProfile ExifProfile { get; set; }
    XmpProfile  XmpProfile  { get; set; }
    IccProfile  IccProfile  { get; set; }
    IptcProfile IptcProfile { get; set; }
    IImageFormat ExpectedFormat { get; set; }
    TFormatMetadata GetFormatMetadata<TFormatMetadata>(
        IImageFormat<TFormatMetadata> key)
    ImageMetadata DeepClone()
    const double DefaultHorizontalResolution = 96
    const double DefaultVerticalResolution = 96
    const PixelResolutionUnit DefaultPixelResolutionUnits = PixelsPerInch

ImageFrameMetadata: ExifProfile, XmpProfile, IccProfile, IptcProfile,
DeepClone(), GetFormatMetadata<TFormatMetadata, TFormatFrameMetadata>(...).

Any profile property may be null — always null-check before reading.

PER-FORMAT METADATA
-------------------
Extension methods in the CodeBrix.Imaging namespace (the returned types live
in the per-format namespaces):

    metadata.GetPngMetadata()   -> PngMetadata
    metadata.GetJpegMetadata()  -> JpegMetadata
    metadata.GetGifMetadata()   -> GifMetadata
    metadata.GetBmpMetadata()   -> BmpMetadata
    metadata.GetWebpMetadata()  -> WebpMetadata
    metadata.GetTiffMetadata()  -> TiffMetadata
    metadata.GetTgaMetadata()   -> TgaMetadata
    metadata.GetPbmMetadata()   -> PbmMetadata
    frameMetadata.GetGifMetadata()  -> GifFrameMetadata
    frameMetadata.GetTiffMetadata() -> TiffFrameMetadata

PngMetadata:  BitDepth, ColorType, InterlaceMethod, Gamma, TransparentRgb24,
              TransparentRgb48, TransparentL8, TransparentL16, HasTransparency,
              IList<PngTextData> TextData
JpegMetadata: int Quality, JpegColorType? ColorType
GifMetadata:  ushort RepeatCount (default 1), GifColorTableMode ColorTableMode,
              int GlobalColorTableLength, IList<string> Comments
GifFrameMetadata: int ColorTableLength, int FrameDelay (hundredths of a
              second), GifDisposalMethod DisposalMethod

EXIF
----
ExifProfile (CodeBrix.Imaging.Metadata.Profiles.Exif):

    public ExifProfile()
    public ExifProfile(byte[] data)
    public ExifParts Parts { get; set; }         // None | IfdTags | ExifTags |
                                                 // GpsTags | All
    public IReadOnlyList<ExifTag> InvalidTags { get; }
    public IExifValue<TValueType> GetValue<TValueType>(ExifTag<TValueType> tag)
    public void SetValue<TValueType>(ExifTag<TValueType> tag, TValueType value)
    public bool RemoveValue(ExifTag tag)
    public byte[] ToByteArray()
    public ExifProfile DeepClone()
    public Image CreateThumbnail()
    public Image<TPixel> CreateThumbnail<TPixel>()

Tags are strongly typed static properties on `ExifTag`, over 250 of them,
grouped by value type so that SetValue/GetValue are type-checked:

    ExifTag<string>   — ImageDescription, Make, Model, Software, DateTime,
                        Artist, Copyright, DocumentName, DateTimeOriginal,
                        DateTimeDigitized, LensMake, LensModel, SerialNumber,
                        ImageUniqueID, OwnerName, GPSLatitudeRef,
                        GPSLongitudeRef, GPSDateStamp, ...
    ExifTag<ushort>   — Compression, PhotometricInterpretation, Orientation,
                        SamplesPerPixel, ResolutionUnit, YCbCrPositioning,
                        Rating, ExposureProgram, ...
    ExifTag<Rational> — XResolution, YResolution, ExposureTime, FNumber,
                        ApertureValue, FocalLength, DigitalZoomRatio,
                        GPSAltitude, GPSSpeed, ...
    plus ExifTag<byte>, ExifTag<byte[]>, ExifTag<uint>, ExifTag<uint[]>,
    ExifTag<ushort[]>, ExifTag<double[]>, ExifTag<SignedRational>,
    ExifTag<SignedRational[]>, ExifTag<Number>, ExifTag<Number[]>,
    ExifTag<EncodedString>.

    using var image = Image.Load("photo.jpg");
    var exif = image.Metadata.ExifProfile ??= new ExifProfile();
    exif.SetValue(ExifTag.Copyright, "Copyright (c) 2026 Contoso");
    exif.SetValue(ExifTag.Artist, "A. Photographer");
    var model = exif.GetValue(ExifTag.Model)?.Value;
    exif.RemoveValue(ExifTag.GPSLatitude);
    image.Save("tagged.jpg");

`InvalidTags` lists tags that could not be parsed from a malformed source;
truncated or corrupt EXIF blocks are tolerated rather than throwing.

XMP
---
XmpProfile (CodeBrix.Imaging.Metadata.Profiles.Xmp):

    public XmpProfile()
    public XmpProfile(byte[] data)
    public XDocument GetDocument()          // null if there is no data
    public byte[] ToByteArray()
    public XmpProfile DeepClone()

ICC
---
IccProfile (CodeBrix.Imaging.Metadata.Profiles.Icc):

    public IccProfile()
    public IccProfile(byte[] data)
    public bool CheckIsValid()
    public byte[] ToByteArray()
    public IccProfile DeepClone()
    public static IccProfileId CalculateHash(byte[] data)

Also present: IccProfileHeader, IccTagDataEntry and the ICC enum/tag-entry
families under CodeBrix.Imaging.Metadata.Profiles.Icc.

IPTC
----
IptcProfile (CodeBrix.Imaging.Metadata.Profiles.Iptc):

    public IptcProfile()
    public IptcProfile(byte[] data)
    public byte[] Data { get; }
    public List<IptcValue> GetValues(IptcTag tag)
    public void SetValue(IptcTag tag, string value, bool strict = true)
    public void SetValue(IptcTag tag, Encoding encoding, string value,
                         bool strict = true)
    public void SetDateTimeValue(IptcTag tag, DateTimeOffset dateTimeOffset)
    public bool RemoveValue(IptcTag tag)
    public bool RemoveValue(IptcTag tag, string value)
    public void SetEncoding(Encoding encoding)
    public void UpdateData()
    public IptcProfile DeepClone()

IptcTag covers the IIM record-2 set: RecordVersion, ObjectType, Name,
EditStatus, Urgency, SubjectReference, Category, SupplementalCategories,
Keywords, LocationCode, LocationName, ReleaseDate, SpecialInstructions,
CreatedDate, CreatedTime, OriginatingProgram, Byline, City, Country,
Headline, Credit, Source, CopyrightNotice, Caption, and the rest.

Call `UpdateData()` after mutating values if you intend to read `Data`
directly.

================================================================================

FRAMES AND ANIMATION
====================
Every image has at least one frame. `image.Frames` is an
ImageFrameCollection (ImageFrameCollection<TPixel> on Image<TPixel>).

    public abstract int Count { get; }
    public ImageFrame RootFrame { get; }         // ImageFrame<TPixel> on the generic
    public ImageFrame this[int index] { get; }
    public ImageFrame AddFrame(ImageFrame source)
    public ImageFrame InsertFrame(int index, ImageFrame source)
    public abstract void RemoveFrame(int index)
    public abstract void MoveFrame(int sourceIndex, int destinationIndex)
    public abstract int IndexOf(ImageFrame frame)
    public abstract bool Contains(ImageFrame frame)
    public ImageFrame CreateFrame()
    public ImageFrame CreateFrame(Color backgroundColor)
    public Image ExportFrame(int index)          // REMOVES the frame and
                                                 // returns it as a new Image
    public Image CloneFrame(int index)           // COPIES the frame; the
                                                 // collection is unchanged
    public IEnumerator<ImageFrame> GetEnumerator()

On ImageFrameCollection<TPixel> the same members are re-declared with
`ImageFrame<TPixel>` / `Image<TPixel>` return types, plus:

    public ImageFrame<TPixel> AddFrame(ReadOnlySpan<TPixel> source)
    public ImageFrame<TPixel> AddFrame(TPixel[] source)
    public ImageFrame<TPixel> CreateFrame(TPixel backgroundColor)

ExportFrame vs CloneFrame is the sharp edge: ExportFrame REMOVES the frame
from the source image and hands it to you as a standalone Image; CloneFrame
copies it and leaves the collection intact. RemoveFrame (and therefore
ExportFrame) throws InvalidOperationException("Cannot remove last frame.") if
only one frame is left.

    using var gif = Image.Load<Rgba32>("animated.gif");
    Console.WriteLine($"{gif.Frames.Count} frames");
    for (var i = 0; i < gif.Frames.Count; i++)
    {
        using var frame = gif.Frames.CloneFrame(i);   // Image<Rgba32>
        frame.Save($"frame-{i:D3}.png");
    }

Building an animated GIF — the per-frame delay lives on the frame's GIF
metadata, and the loop count on the image's GIF metadata:

    using var animation = new Image<Rgba32>(200, 200);
    animation.Metadata.GetGifMetadata().RepeatCount = 0;   // 0 = loop forever
    animation.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 10;

    foreach (var source in frameImages)
    {
        var added = animation.Frames.AddFrame(source.Frames.RootFrame);
        added.Metadata.GetGifMetadata().FrameDelay = 10;   // hundredths of a second
        added.Metadata.GetGifMetadata().DisposalMethod =
            GifDisposalMethod.RestoreToBackground;
    }
    animation.SaveAsGif("animation.gif");

To decode only the first frame of a multi-frame GIF or TIFF, set
`DecodingMode = FrameDecodingMode.First` on GifDecoder / TiffDecoder, or cap
GifDecoder.MaxFrames.

ImageFrame<TPixel> itself exposes Width, Height, Metadata, PixelBuffer, the
[x, y] indexer, ProcessPixelRows, CopyPixelDataTo and
DangerousTryGetSinglePixelMemory — the same pixel-access surface as the image.

================================================================================

8BPP GRAYSCALE BMP EXPORT (CodeBrix-only)
=========================================
`BmpFormatHelper` (CodeBrix.Imaging.Helpers) writes any image as an 8-bit
indexed grayscale BMP. This exists for document-imaging pipelines, scanner
integrations and legacy systems that demand 8bpp indexed BMPs — the standard
BmpEncoder path produces 24/32bpp output.

    using CodeBrix.Imaging.Helpers;

    // Stream targets
    public static void ExportAs8bppGrayscaleBmpFormat(this Image image,
        Stream stream, BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
    public static void ExportAs8bppGrayscaleBmpFormat(this Image image,
        Stream stream, ColorMatrix colorMatrix,
        BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
    public static Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image,
        Stream stream, BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
    public static Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image,
        Stream stream, BmpIndexingMode indexingMode,
        CancellationToken cancellationToken)
    public static Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image,
        Stream stream, ColorMatrix colorMatrix,
        BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
    public static Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image,
        Stream stream, ColorMatrix colorMatrix, BmpIndexingMode indexingMode,
        CancellationToken cancellationToken)

    // File-path targets
    public static void ExportAs8bppGrayscaleBmpFormat(this Image image,
        string path, BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
    public static void ExportAs8bppGrayscaleBmpFormat(this Image image,
        string path, ColorMatrix colorMatrix,
        BmpIndexingMode indexingMode = BmpIndexingMode.Normal)
    public static Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image,
        string path, BmpIndexingMode indexingMode = BmpIndexingMode.Normal,
        CancellationToken cancellationToken = default)
    public static Task ExportAs8bppGrayscaleBmpFormatAsync(this Image image,
        string path, ColorMatrix colorMatrix,
        BmpIndexingMode indexingMode = BmpIndexingMode.Normal,
        CancellationToken cancellationToken = default)

BmpIndexingMode:
    Normal (0, the default)
        256-entry linear grayscale palette; index 0 = black, 255 = white. Each
        pixel's computed gray value maps straight to its palette index.
    SystemDrawingCompatible (1)
        224-entry GDI+ halftone palette with empirically matched
        quantization, so the bytes match what System.Drawing produced for
        Format8bppIndexed.

Pre-defined weighting matrices (public static readonly ColorMatrix on
BmpFormatHelper):
    DefaultGrayscaleColorMatrix   R=0.3,    G=0.59,   B=0.11
                                  (the default; matches
                                  System.Drawing.Imaging.ColorMatrix)
    Bt601GrayscaleColorMatrix     R=0.299,  G=0.587,  B=0.114  (ITU-R BT.601;
                                  matches GrayscaleMode.Bt601)
    Bt709GrayscaleColorMatrix     R=0.2126, G=0.7152, B=0.0722 (ITU-R BT.709;
                                  matches GrayscaleMode.Bt709)

A custom ColorMatrix controls only how the RGB channels are weighted into a
single intensity — the output is ALWAYS grayscale, never colour.

These are EXPORT methods, not Save methods: they bypass the encoder pipeline,
do NOT update `Metadata.ExpectedFormat`, and leave the in-memory image
unchanged. Argument validation throws ArgumentNullException for a null image
or stream, ArgumentException for a non-writable stream or an undefined
BmpIndexingMode.

    using var image = Image.Load("scan.jpg");
    await image.ExportAs8bppGrayscaleBmpFormatAsync("scan-8bpp.bmp");

    using var ms = new MemoryStream();
    await image.ExportAs8bppGrayscaleBmpFormatAsync(ms,
        BmpIndexingMode.SystemDrawingCompatible);
    byte[] bmpBytes = ms.ToArray();

================================================================================

CONFIGURATION, MEMORY AND PARALLELISM
=====================================
Configuration (CodeBrix.Imaging) is the per-operation policy object.
`Configuration.Default` is the shared global instance; clone or construct your
own before changing anything in a library or a server.

    public Configuration()
    public Configuration(params IConfigurationModule[] configurationModules)
    public static Configuration Default { get; }
    public int MaxDegreeOfParallelism { get; set; }       // -1 or >= 1
    public int StreamProcessingBufferSize { get; set; }
    public bool PreferContiguousImageBuffers { get; set; }
    public IDictionary<object, object> Properties { get; }
    public IEnumerable<IImageFormat> ImageFormats { get; }
    public ReadOrigin ReadOrigin { get; set; }            // Begin | Current
    public ImageFormatManager ImageFormatsManager { get; }
    public MemoryAllocator MemoryAllocator { get; set; }
    public void Configure(IConfigurationModule configuration)
    public Configuration CreateSandboxed(int allocationLimitMegabytes)
    public Configuration Clone()

`MaxDegreeOfParallelism` of 0 or below -1 throws ArgumentOutOfRangeException.

    var config = Configuration.Default.Clone();
    config.MaxDegreeOfParallelism = 2;
    config.PreferContiguousImageBuffers = true;
    using var image = Image.Load(config, "photo.jpg");

`CreateSandboxed(int allocationLimitMegabytes)` returns a configuration whose
allocator refuses to exceed the given budget — the right way to process
untrusted uploads without a decompression bomb exhausting memory.

ImageFormatManager (CodeBrix.Imaging.Formats) is the registry behind
`Configuration.ImageFormatsManager`:

    public IEnumerable<IImageFormat> ImageFormats { get; }
    public void AddImageFormat(IImageFormat format)
    public IImageFormat FindFormatByFileExtension(string extension)
    public IImageFormat FindFormatByMimeType(string mimeType)
    public IImageFormat FindFormatByEncoder(IImageEncoder encoder)
    public void SetEncoder(IImageFormat imageFormat, IImageEncoder encoder)
    public void SetDecoder(IImageFormat imageFormat, IImageDecoder decoder)
    public IImageEncoder FindEncoder(IImageFormat format)
    public IImageDecoder FindDecoder(IImageFormat format)
    public void AddImageFormatDetector(IImageFormatDetector detector)
    public void ClearImageFormatDetectors()

Setting a default encoder once, for every subsequent extension-based Save:

    Configuration.Default.ImageFormatsManager.SetEncoder(
        JpegFormat.Instance, new JpegEncoder { Quality = 80 });

MEMORY (CodeBrix.Imaging.Memory)

    public abstract class MemoryAllocator
    {
        public static MemoryAllocator Default { get; }
        public static MemoryAllocator Create();
        public static MemoryAllocator Create(MemoryAllocatorOptions options);
        public abstract IMemoryOwner<T> Allocate<T>(int length,
            AllocationOptions options = AllocationOptions.None) where T : struct;
        public virtual void ReleaseRetainedResources();
    }

    AllocationOptions:  None | Clean
    SimpleGcMemoryAllocator — a plain GC-backed allocator for tests or
    memory-constrained hosts.

`ReleaseRetainedResources()` drops pooled buffers; call it after a burst of
large image work in a long-running process. Disposal of IMemoryOwner<T> is
idempotent.

MemoryDiagnostics (CodeBrix.Imaging.Diagnostics) helps find leaks:

    public static event UndisposedAllocationDelegate UndisposedAllocation;
    public static int TotalUndisposedAllocationCount { get; }

PARALLELISM (CodeBrix.Imaging.Advanced)

    public readonly struct ParallelExecutionSettings
    {
        public const int DefaultMinimumPixelsProcessedPerTask = 4096;
        public ParallelExecutionSettings(int maxDegreeOfParallelism,
                                         MemoryAllocator memoryAllocator);
        public MemoryAllocator MemoryAllocator { get; }
        public int MaxDegreeOfParallelism { get; }
        public int MinimumPixelsProcessedPerTask { get; }
        public ParallelExecutionSettings MultiplyMinimumPixelsPerTask(int multiplier);
        public static ParallelExecutionSettings FromConfiguration(
            Configuration configuration);
    }

`ParallelRowIterator.IterateRows` / `IterateRowIntervals` run an
IRowOperation / IRowIntervalOperation across a Rectangle using those settings.

VISITOR PATTERN — recovering the pixel type of a non-generic Image:

    public static void AcceptVisitor(this Image source, IImageVisitor visitor)
    public static Task AcceptVisitorAsync(this Image source,
        IImageVisitorAsync visitor, CancellationToken cancellationToken = default)

    // IImageVisitor:      void Visit<TPixel>(Image<TPixel> image);
    // IImageVisitorAsync: Task VisitAsync<TPixel>(Image<TPixel> image,
    //                                             CancellationToken token);

================================================================================

OTHER PUBLIC TYPES YOU MAY ENCOUNTER
====================================
Types that show up in signatures, IntelliSense or debugger output but are not
part of a headline scenario.

CodeBrix.Imaging
    IImage : IImageInfo, IDisposable   the non-generic image contract.
    IImageInfo                         header-only description (Width, Height,
                                       PixelType, Metadata, Format,
                                       H/V Resolution).
    IDeepCloneable, IDeepCloneable<T>  implemented by the metadata profiles.
    IndexedImageFrame<TPixel>          the palettised result a quantizer
                                       produces: Configuration, Width, Height,
                                       ReadOnlyMemory<TPixel> Palette,
                                       DangerousGetRowSpan(int),
                                       GetWritablePixelRowSpanUnsafe(int).
    GeometryUtilities                  DegreeToRadian(float),
                                       RadianToDegree(float).
    IConfigurationModule               a format's registration hook, used by
                                       Configuration.Configure(...).
    ImageExtensions, ImageInfoExtensions, GraphicOptionsDefaultsExtensions
                                       the static extension holders whose
                                       methods are documented above.

CodeBrix.Imaging.Formats
    UnknownImageFormat                 the IImageFormat placeholder used when
                                       no registered format matched.
    IImageFormatDetector               plug in your own signature sniffing via
                                       ImageFormatManager.AddImageFormatDetector.
    IImageInfoDetector                 implemented by every decoder; backs
                                       Image.Identify.
    IImageFormat<TFormatMetadata> and IImageFormat<TFormatMetadata,
    TFormatFrameMetadata>              the generic format interfaces that make
                                       GetFormatMetadata type-safe.

CodeBrix.Imaging.PixelFormats
    IPixel, IPixel<TSelf>              the pixel-struct contract; consumers see
                                       it as the constraint
                                       `where TPixel : unmanaged, IPixel<TPixel>`.
    IPackedVector<TPacked>             PackedValue accessor.
    PixelOperations<TPixel>            bulk conversion between pixel types;
                                       reached via TPixel.CreatePixelOperations().
    PixelBlender<TPixel>               abstract Blend(background, source,
                                       amount) and span-based Blend overloads;
                                       the concrete blenders are the
                                       colour-mode x alpha-mode combinations
                                       selected by PixelColorBlendingMode and
                                       PixelAlphaCompositionMode.
    PixelAlphaRepresentation           None | Unassociated | Associated.
    PixelConversionModifiers           flags used by ProcessPixelRowsAsVector4.

CodeBrix.Imaging.Memory
    Buffer2D<T>                        the pixel buffer behind an image frame.
    Buffer2DRegion<T>                  a rectangular view over a Buffer2D<T>:
                                       Rectangle, Buffer, Width, Height,
                                       Stride, DangerousGetRowSpan(int),
                                       GetSubRegion(...).
    IMemoryGroup<T>, MemoryGroupEnumerator<T>
                                       the discontiguous-buffer abstraction
                                       returned by GetPixelMemoryGroup.
    RowInterval                        a [start, end) row range used by the
                                       parallel iterator.
    MemoryAllocatorOptions             AllocationLimitMegabytes and friends,
                                       passed to MemoryAllocator.Create.

CodeBrix.Imaging.Advanced
    IRowOperation, IRowOperation<TBuffer>, IRowIntervalOperation,
    IRowIntervalOperation<TBuffer>     the operations ParallelRowIterator runs.
    ArchitectureInfo                   IsRiscVArchitecture, used to gate SIMD
                                       paths.

CodeBrix.Imaging.Fonts
    IFontCollection, IReadOnlyFontCollection, IReadOnlySystemFontCollection
                                       the collection contracts behind
                                       FontCollection and SystemFonts.
    FontMetrics                        ascender/descender/line-gap and the
                                       glyph tables for a face.
    GlyphMetrics, Glyph, GlyphBounds, GlyphColor, GlyphRendererParameters,
    GlyphType (Fallback | Standard | ColrLayer)
                                       the per-glyph layout and rasterization
                                       data.
    IGlyphRenderer, IColorGlyphRenderer, IGlyphDecorationRenderer,
    IGlyphRendererExtensions           implement these to rasterize text into
                                       something other than an Image.
    FontDescription                    LoadDescription(path/stream) reads a
                                       font's family/sub-family/style without
                                       loading the whole face.
    TextAttributes                     None | Subscript | Superscript; the run
                                       attribute carried by TextRun.
    ColorFontFormat                    the colour-font table format reported by
                                       Font.ColorFormat.
    CodeBrix.Imaging.Fonts.Unicode.CodePoint
                                       the Unicode scalar type used by
                                       Font.GetGlyphs and the layout engine.
    CodeBrix.Imaging.Fonts.WellKnownIds.KnownNameIds
                                       the OpenType 'name' table ids.
    CodeBrix.Imaging.Fonts.Tables.AdvancedTypographic.Tag
                                       an OpenType feature tag, for
                                       TextOptions.FeatureTags.

CodeBrix.Imaging.Metadata.Profiles.Exif
    ExifOrientationMode                static ushort constants Unknown,
                                       TopLeft, TopRight, BottomRight,
                                       BottomLeft, LeftTop, RightTop,
                                       RightBottom, LeftBottom — the values of
                                       ExifTag.Orientation, which AutoOrient()
                                       consumes.
    ExifDataType, IExifValue, IExifValue<TValueType>, EncodedString
                                       the value model behind
                                       ExifProfile.GetValue/SetValue.
                                       IExifValue carries DataType, IsArray,
                                       Tag, GetValue() and TrySetValue(object);
                                       IExifValue<T> adds the typed
                                       `T Value { get; set; }`.

CodeBrix.Imaging.ColorSpaces.Conversion
    RgbWorkingSpace and its implementations SRgbWorkingSpace,
    Rec709WorkingSpace, Rec2020WorkingSpace, GammaWorkingSpace,
    LWorkingSpace                      selectable through
                                       ColorSpaceConverterOptions
                                       .TargetRgbWorkingSpace, with the
                                       ready-made set on RgbWorkingSpaces.
    CieXyChromaticityCoordinates, RgbPrimariesChromaticityCoordinates
                                       primaries/white-point descriptions.
    IChromaticAdaptation, VonKriesChromaticAdaptation
                                       the white-point adaptation strategy.

Format-level types that surface on the per-format Metadata objects:
    BmpFileMarkerType, BmpInfoHeaderType                             (Bmp)
    TgaImageType                                                     (Tga)
    WebpBitsPerPixel, WebpFileFormatType                             (Webp)
    TiffFormatType, TiffBitsPerSample and the
    CodeBrix.Imaging.Formats.Tiff.Constants family
    (TiffCompression, TiffPredictor, TiffPhotometricInterpretation,
    TiffSampleFormat, TiffPlanarConfiguration, TiffSubfileType,
    TiffNewSubfileType)                                              (Tiff)
    PngTextData                                                      (Png)
    CodeBrix.Imaging.PixelResolutionUnit
                                       AspectRatio | PixelsPerInch |
                                       PixelsPerCentimeter |
                                       PixelsPerMeter — the unit for
                                       ImageMetadata.ResolutionUnits.
    CodeBrix.Imaging.ByteOrder         BigEndian | LittleEndian.

Everything else in the assembly — the *Processor classes behind each Mutate
operation, the *Extensions holders, the *ConfigurationModule and
*ImageFormatDetector registrations, the several hundred pixel-blender
combination structs, the ICC tag-data-entry family and the OpenType table
readers — is implementation detail reached through the APIs documented above.

================================================================================

EXCEPTIONS AND ERROR MODEL
==========================
    CodeBrix.Imaging
        ImageFormatException : Exception
            Thrown when the library is asked to load an image whose format or
            content is invalid or unsupported.
        InvalidImageContentException : ImageFormatException
            The format was recognised but the content is corrupt.
        UnknownImageFormatException : ImageFormatException
            The byte signature matched no registered format.
        ImageProcessingException : Exception
            A processor failed while running inside Mutate()/Clone().

    CodeBrix.Imaging.Memory
        InvalidMemoryOperationException : InvalidOperationException
            An allocation request exceeded the allocator's limit (this is what
            CreateSandboxed produces), or an invalidated IMemoryGroup<T> was
            used.

    CodeBrix.Imaging.Processing.Processors.Transforms
        DegenerateTransformException
            The composed transform matrix collapses the image to zero area.

    CodeBrix.Imaging.Fonts
        FontException, FontFamilyNotFoundException, GlyphMissingException,
        InvalidFontFileException, InvalidFontTableException,
        MissingFontTableException.

Beyond these, the library uses the standard BCL exceptions: ArgumentNullException
for null images/streams/fonts, ArgumentException for a non-writable stream or an
undefined enum value, ArgumentOutOfRangeException for a bad row index or an
invalid MaxDegreeOfParallelism, NotSupportedException for an unsupported file
extension in Save/DetectEncoder and for animated WebP, and
InvalidOperationException when removing the last remaining frame.

Catch `ImageFormatException` to cover every "this file is not usable" case at
once — both of its subclasses derive from it.

================================================================================

COMPLETE EXAMPLES
=================

Example 1: Load, process and save in three formats
--------------------------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Formats.Jpeg;
    using CodeBrix.Imaging.Processing;

    using var image = Image.Load("input.jpg");

    image.Mutate(x => x
        .Resize(1920, 1080)
        .Brightness(1.1f)
        .Contrast(1.2f)
        .Saturate(1.1f));            // NOTE: Saturate, not "Saturation"

    image.Save("output.png");
    image.Save("output.webp");
    image.Save("output.jpg", new JpegEncoder { Quality = 88 });

Example 2: Thumbnail without destroying the original
----------------------------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Processing;

    using var original = Image.Load("large-photo.jpg");

    using var thumb = original.Clone(x => x.Resize(new ResizeOptions
    {
        Size = new Size(200, 200),
        Mode = ResizeMode.Crop,
        Position = AnchorPositionMode.Center,
        Sampler = KnownResamplers.Lanczos3
    }));

    thumb.Save("thumbnail.jpg");
    // `original` is still full size.

Example 3: Text watermark, bottom-right aligned
-----------------------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Fonts;
    using CodeBrix.Imaging.Fonts.Rendering;
    using CodeBrix.Imaging.PixelFormats;

    using var loaded = Image.Load("photo.jpg");
    using var image  = loaded.CloneAs<Rgba32>();

    var family = SystemFonts.TryGet("Arial", out var f)
        ? f
        : new FontCollection().Add("fonts/Roboto-Regular.ttf");
    var font = family.CreateFont(24, FontStyle.Bold);

    const string text = "(c) 2026 Contoso";
    var bounds = TextRenderingExtensions.MeasureText(text, font);

    var x = (image.Width * 0.95f) - bounds.Width;
    var y = (image.Height * 0.95f) - bounds.Height;

    // DrawText is called ON THE IMAGE — never inside Mutate().
    image.DrawText(text, font, new Rgba32(255, 255, 255, 255), x, y);

    image.Save("watermarked.jpg");

Example 4: Wrapped, centred, multi-line caption
-----------------------------------------------
    using System.Numerics;
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Fonts;
    using CodeBrix.Imaging.Fonts.Rendering;

    using var image = new Image<Rgba32>(600, 300, new Rgba32(20, 20, 30, 255));

    var collection = new FontCollection();
    var family = collection.Add("fonts/Roboto-Regular.ttf");
    var font = family.CreateFont(28);

    var options = new TextOptions(font)
    {
        Origin = new Vector2(300, 40),
        WrappingLength = 520,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        LineSpacing = 1.2f
    };

    image.DrawText("A long caption that will wrap across several lines "
                 + "inside the 520-pixel wrapping length.",
                   options, Color.White);

    image.Save("caption.png");

Example 5: Web/API pipeline, streams only, no disk I/O
------------------------------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Formats.Jpeg;
    using CodeBrix.Imaging.Processing;

    public static async Task<byte[]> MakeThumbnailAsync(
        Stream upload, CancellationToken token)
    {
        // Sandbox the allocator so a decompression bomb cannot exhaust
        // memory. CreateSandboxed already clones, so Default is untouched.
        var sandboxed = Configuration.Default.CreateSandboxed(
            allocationLimitMegabytes: 256);

        using var image = await Image.LoadAsync(sandboxed, upload, token);

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(320, 320),
            Mode = ResizeMode.Max
        }));

        using var output = new MemoryStream();
        await image.SaveAsync(output, new JpegEncoder { Quality = 75 }, token);
        return output.ToArray();
    }

Example 6: Sniff the format before committing to a decode
---------------------------------------------------------
    using CodeBrix.Imaging;

    using var stream = File.OpenRead("unknown-file");

    var format = Image.DetectFormat(stream);
    if (format is null)
    {
        return;                       // not an image this library understands
    }

    stream.Position = 0;              // DetectFormat consumed the header
    var info = Image.Identify(stream);
    if ((long)info.Width * info.Height > 50_000_000)
    {
        return;                       // refuse absurd dimensions
    }

    stream.Position = 0;
    using var image = Image.Load(stream);

Example 7: Raw RGBA pixel data from a native renderer
-----------------------------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Formats.Png;
    using CodeBrix.Imaging.PixelFormats;

    // Rgba32 expects R,G,B,A per pixel in row-major order.
    var width = 2550;                        // 8.5in at 300 DPI
    var height = 3300;                       // 11in at 300 DPI
    var pixelData = new byte[width * height * 4];
    FillFromRenderer(pixelData);

    // The 4th argument (the format) is REQUIRED.
    using var image = Image.LoadPixelData<Rgba32>(
        pixelData, width, height, PngFormat.Instance);

    image.Save("rendered-page.png", new PngEncoder());
    image.Save("rendered-page.jpg");         // saving as another format is fine

Example 8: BGRA pixel data (PDFium, Direct2D, Cairo, GDI+), with stride
-----------------------------------------------------------------------
    using System.Runtime.InteropServices;
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Formats.Png;

    // Simple case — contiguous BGRA, no row padding:
    using var simple = Image.LoadPixelDataFromBgra(
        bgraData, width, height, PngFormat.Instance);
    simple.Save("page.png", new PngEncoder());

    // Native buffers usually have a stride > width * 4 for alignment.
    // Strip the padding into a contiguous array first.
    var nativeBuffer = GetBufferFromNativeRenderer();     // IntPtr
    var stride = GetStrideFromNativeRenderer();           // int

    var contiguous = new byte[width * height * 4];
    for (var y = 0; y < height; y++)
    {
        Marshal.Copy(
            nativeBuffer + (y * stride),   // source row start
            contiguous,
            y * width * 4,                 // destination row start
            width * 4);                    // pixel bytes only, no padding
    }

    using var image = Image.LoadPixelDataFromBgra(
        contiguous, width, height, PngFormat.Instance);
    image.Save("page.png", new PngEncoder());

Example 9: Palette reduction for a small PNG
--------------------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Formats.Png;
    using CodeBrix.Imaging.Processing;
    using CodeBrix.Imaging.Processing.Processors.Quantization;

    using var image = Image.Load("screenshot.png");

    image.Mutate(x => x.Quantize(new WuQuantizer(new QuantizerOptions
    {
        MaxColors = 64,
        Dither = KnownDitherings.FloydSteinberg
    })));

    image.Save("screenshot-small.png", new PngEncoder
    {
        ColorType = PngColorType.Palette,
        BitDepth = PngBitDepth.Bit8,
        CompressionLevel = PngCompressionLevel.BestCompression,
        ChunkFilter = PngChunkFilter.ExcludeAll
    });

Example 10: Composite a logo and flatten to JPEG
------------------------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Formats.Jpeg;
    using CodeBrix.Imaging.Processing;

    using var background = Image.Load("photo.png");     // may have alpha
    using var logo       = Image.Load("logo.png");

    background.Mutate(x => x
        .DrawImage(logo, new Point(24, 24), 0.8f)
        .BackgroundColor(Color.White));                 // flatten the alpha

    background.Save("branded.jpg", new JpegEncoder { Quality = 90 });

Example 11: Read and write EXIF
-------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Metadata.Profiles.Exif;

    using var image = Image.Load("photo.jpg");

    var exif = image.Metadata.ExifProfile;
    if (exif is not null)
    {
        var make  = exif.GetValue(ExifTag.Make)?.Value;
        var model = exif.GetValue(ExifTag.Model)?.Value;
        var taken = exif.GetValue(ExifTag.DateTimeOriginal)?.Value;
        Console.WriteLine($"{make} {model} {taken}");
    }

    image.Metadata.ExifProfile ??= new ExifProfile();
    image.Metadata.ExifProfile.SetValue(ExifTag.Copyright,
        "Copyright (c) 2026 Contoso");
    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSLatitude);
    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSLongitude);

    image.Save("photo-tagged.jpg");

Example 12: Per-pixel work with ProcessPixelRows
------------------------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.PixelFormats;

    using var image = Image.Load<Rgba32>("photo.png");

    image.ProcessPixelRows(accessor =>
    {
        for (var y = 0; y < accessor.Height; y++)
        {
            var row = accessor.GetRowSpan(y);
            for (var x = 0; x < row.Length; x++)
            {
                ref var p = ref row[x];
                if (p.R > 200 && p.G < 60 && p.B < 60)
                {
                    p = new Rgba32(0, 0, 0, 0);      // knock out reds
                }
            }
        }
    });

    image.Save("keyed.png");

Example 13: Build an animated GIF
---------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Formats.Gif;
    using CodeBrix.Imaging.PixelFormats;
    using CodeBrix.Imaging.Processing;

    using var animation = new Image<Rgba32>(200, 200, Color.Black.ToPixel<Rgba32>());
    animation.Metadata.GetGifMetadata().RepeatCount = 0;             // loop forever
    animation.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 8;

    for (var i = 1; i < 12; i++)
    {
        using var step = new Image<Rgba32>(200, 200);
        step.Mutate(x => x.BackgroundColor(Color.FromRgb((byte)(i * 20), 40, 90)));

        var frame = animation.Frames.AddFrame(step.Frames.RootFrame);
        frame.Metadata.GetGifMetadata().FrameDelay = 8;               // 0.08s
        frame.Metadata.GetGifMetadata().DisposalMethod =
            GifDisposalMethod.RestoreToBackground;
    }

    animation.SaveAsGif("animation.gif");

Example 14: 8bpp grayscale BMP for a document-imaging system
------------------------------------------------------------
    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Helpers;

    using var image = Image.Load("scan.jpg");

    // GDI+-compatible bytes, straight to a file:
    await image.ExportAs8bppGrayscaleBmpFormatAsync("scan-8bpp.bmp",
        BmpFormatHelper.Bt709GrayscaleColorMatrix,
        BmpIndexingMode.SystemDrawingCompatible);

    // The in-memory image is untouched and still Rgba-based:
    image.Save("scan-copy.png");

================================================================================

MINIMUM VIABLE PROJECT
======================
    dotnet new console -n MyImageApp --framework net10.0
    cd MyImageApp
    dotnet add package CodeBrix.Imaging.ApacheLicenseForever

MyImageApp.csproj:

    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>disable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Imaging.ApacheLicenseForever" />
      </ItemGroup>
    </Project>

Program.cs:

    using CodeBrix.Imaging;
    using CodeBrix.Imaging.Formats.Jpeg;
    using CodeBrix.Imaging.Processing;

    using var image = Image.Load("input.jpg");

    image.Mutate(x => x
        .Resize(800, 0)             // 0 preserves the aspect ratio
        .Grayscale());

    image.Save("output.jpg", new JpegEncoder { Quality = 85 });

    Console.WriteLine($"Wrote {image.Width}x{image.Height}");

    dotnet build
    dotnet run

================================================================================

PERFORMANCE TIPS
================
 1. CHAIN INSIDE ONE Mutate(). Every Mutate() call runs its own pass over the
    pixels. `image.Mutate(x => x.Resize(...).Grayscale().Contrast(1.2f))` is
    one pipeline; three separate Mutate() calls are three pipelines.

 2. RESIZE FIRST when you are shrinking. Every later operation then works on
    far fewer pixels. Resize last when you are enlarging.

 3. USE Identify()/DetectFormat() when you only need dimensions or the format.
    They read the header, not the pixels — orders of magnitude cheaper than
    Load().

 4. PICK THE NARROWEST PIXEL TYPE. Rgb24 is 25% smaller than Rgba32 when you
    have no alpha; L8 is 75% smaller for greyscale; Rgba64/Rgb48 only pay for
    themselves on genuine 16-bit sources.

 5. PREFER Mutate() OVER Clone() unless you actually need the original — Clone
    allocates a second full-size buffer that you must also dispose.

 6. USE LoadPixelDataFromBgra INSTEAD OF A MANUAL BYTE SWAP. It reorders
    channels with AVX2/SSSE3 directly into the destination buffer, avoiding
    both the scalar loop and the intermediate array.

 7. USE ProcessPixelRows, NOT the [x, y] indexer, for whole-image passes. The
    indexer does bounds checking per access; row spans do not.

 8. STREAM IN WEB/API CODE. Load and Save through streams; never round-trip
    through a temp file.

 9. SET MaxDegreeOfParallelism on a cloned Configuration when you run several
    image operations concurrently, so they do not oversubscribe the thread
    pool against each other.

10. CALL MemoryAllocator.Default.ReleaseRetainedResources() after a burst of
    large-image work in a long-running process, to return pooled buffers.

11. TUNE THE ENCODER. PngCompressionLevel.BestSpeed vs BestCompression is a
    large time/size trade; JpegEncoder.Quality below about 80 shrinks files
    fast; PngChunkFilter.ExcludeAll strips metadata chunks.

12. QUANTIZE BEFORE SAVING INDEXED PNG/GIF/BMP so you control the palette
    and dithering instead of taking the encoder's defaults.

13. NO NATIVE DEPENDENCIES, NO WARM-UP. There is nothing to install and
    nothing to initialize; the same code runs identically on Windows, Linux
    and macOS.

================================================================================

COMMON PITFALLS TO AVOID
========================
 1. DO NOT write `using CodeBrix.Imaging.Drawing;`. That namespace does NOT
    exist. Text rendering is `using CodeBrix.Imaging.Fonts;` plus
    `using CodeBrix.Imaging.Fonts.Rendering;`.

 2. DO NOT call DrawText inside Mutate(). `image.Mutate(x => x.DrawText(...))`
    does not compile — DrawText is an extension on Image / Image<TPixel>:
        image.DrawText(text, font, color, x, y);

 3. DO NOT call `.Saturation(...)`. The method is `.Saturate(float amount)`.

 4. DO NOT confuse the package id with the namespace. Package:
    CodeBrix.Imaging.ApacheLicenseForever. Namespace: CodeBrix.Imaging.

 5. DO NOT use SixLabors.* namespaces or add the upstream packages. All types
    were renamed into CodeBrix.Imaging.*, and having both in one project
    produces ambiguous-reference errors.

 6. DO NOT call Image.LoadPixelData with three arguments. The IImageFormat
    fourth argument is mandatory here (CS1501 otherwise). Pass
    PngFormat.Instance or whichever format applies.

 7. DO NOT feed BGRA bytes to LoadPixelData<Rgba32>. Red and blue will be
    swapped. Use LoadPixelDataFromBgra.

 8. DO NOT confuse stride with width when copying from a native buffer.
    Stride is often larger than width * bytesPerPixel because of alignment
    padding; index the source by stride and the destination by width * 4.

 9. DO NOT forget `stream.Position = 0` after DetectFormat/Identify before
    loading from the same stream. Configuration.ReadOrigin defaults to
    ReadOrigin.Current, so nothing rewinds the stream for you unless you set
    ReadOrigin.Begin.

10. DO NOT forget `using CodeBrix.Imaging.Processing;`. Without it Resize,
    Crop, Grayscale, Mutate and Clone are simply not visible, and the error
    ("no definition for 'Mutate'") does not name the missing namespace.

11. DO NOT use the [x, y] indexer on a non-generic Image — it only exists on
    Image<TPixel>. Load with Image.Load<Rgba32>(...) or call CloneAs<Rgba32>().

12. DO NOT let a PixelAccessor or a row Span escape the ProcessPixelRows
    callback, and do not dispose the image while a Memory obtained from
    DangerousTryGetSinglePixelMemory is still in use.

13. DO NOT expect ExportAs8bppGrayscaleBmpFormat to behave like Save. It
    bypasses the encoder pipeline, does not update Metadata.ExpectedFormat,
    and leaves the in-memory image unchanged. A custom ColorMatrix only
    changes the RGB weighting — the output is always grayscale.

14. DO NOT assume system fonts exist. Containers and CI agents frequently have
    none. `SystemFonts.Get(name)` and `FontCollection.Get(name)` throw
    FontFamilyNotFoundException when the family is missing; prefer
    `SystemFonts.TryGet(...)` with a FontCollection fallback, and ship the
    .ttf files you depend on as embedded resources or content.

15. DO NOT expect automatic font fallback. Unresolved codepoints render as
    .notdef unless you populate TextOptions.FallbackFontFamilies yourself.

16. DO NOT save an image with alpha to JPEG and expect transparency. Flatten
    it first with `.BackgroundColor(Color.White)`.

17. DO NOT use ExportFrame when you meant CloneFrame. ExportFrame REMOVES the
    frame from the source image; removing the only remaining frame throws
    InvalidOperationException.

18. DO NOT mutate Configuration.Default in library or server code. Clone it,
    or use CreateSandboxed, so you do not change global behaviour for
    everyone else in the process.

19. DO NOT skip disposal. Image, ImageFrame and IMemoryOwner<T> all hold
    pooled buffers. Dispose is idempotent, so `using` everywhere is safe.

20. DO NOT decode untrusted input without limits. Use
    `Configuration.CreateSandboxed(...)`, check `Image.Identify` dimensions
    first, and set `GifDecoder.MaxFrames` / `FrameDecodingMode.First` for
    multi-frame formats.

================================================================================

WHAT THIS PACKAGE DOES NOT DO
=============================
It is a raster imaging + text-rasterization library. It does NOT provide:

  - Vector shape drawing. There are no DrawLine / DrawPolygon / FillPath /
    Brush / Pen APIs. Compositing (DrawImage) and text (DrawText) are the only
    ways to draw onto an image.
  - SVG parsing or rendering.
  - PDF reading, writing or rendering.
  - RAW camera formats (.CR2, .NEF, .ARW, .DNG) or HEIF/HEIC/AVIF.
  - JPEG 2000, JPEG XL, ICO, PSD or DICOM.
  - Animated WebP. WebP encoding is single-frame only, and decoding an
    animated WebP throws NotSupportedException. Use GIF for animation.
  - Video decoding, encoding or frame extraction; camera/webcam capture.
  - OCR, barcode reading, face/object detection or any ML inference.
  - GPU-accelerated processing. Everything is CPU SIMD (AVX2/SSSE3) plus
    parallel row iteration.
  - Any UI, window, or screen-capture surface.
  - Colour management beyond carrying the ICC profile bytes: the ICC profile
    is read, written and validated, but pixels are not transformed through it.
  - Rich text layout beyond a single TextOptions block — no HTML/RTF, no
    tables, no inline images (TextRun gives per-range font and decoration
    only).

For PDF generation and rendering, the CodeBrix family provides a separate
package (CodeBrix.PdfDocuments) — read that package's own AGENT-README.

================================================================================

WORKING EXAMPLES ON GITHUB
==========================
The test project is a working, compiling reference for nearly every API in
this document. When something here is not enough, read the test:

    https://github.com/ellisnet/CodeBrix.Imaging/tree/main/tests/CodeBrix.Imaging.Tests

Feature-to-test-file map (all paths under tests/CodeBrix.Imaging.Tests/):

  Mutate operations end to end — invert, grayscale, black-white, sepia,
  kodachrome, polaroid, lomograph, brightness, contrast, saturate, lightness,
  hue, opacity, gaussian blur/sharpen, box blur, bokeh blur, flip, rotate,
  rotate-flip, pixelate, oil paint, vignette, glow, detect edges, auto-orient,
  histogram equalization, binary threshold, DrawImage compositing, text
  rendering with MeasureText, and all eight format conversions:
    Core/ImageManipulationTests.cs
    (the single most comprehensive file — start here)

  Construction, load from stream/resource, Save and SaveAsync with explicit
  encoders, CloneAs, disposal semantics, DetectFormat, Identify, Frames:
    Core/ImageTests.cs

  Configuration surface (Default, MaxDegreeOfParallelism validation,
  MemoryAllocator, registered formats):
    Core/ConfigurationTests.cs

  LoadPixelData in all its shapes and its bounds/validation behaviour:
    Core/ImageSecurityTests.cs

  TIFF decompression hardening (LZW and PackBits overrun cases):
    Core/TiffDecompressionSecurityTests.cs

  Color creation, hex and named parsing, equality, WithAlpha:
    Colors/ColorTests.cs

  Format singletons, their names/MIME types/extensions and registration:
    Formats/ImageFormatTests.cs

  Rgba32 and Bgra32 construction, packed values, conversions, and the
  Bgra32 <-> Format32bppArgb byte-layout compatibility proofs:
    PixelFormats/Rgba32Tests.cs, PixelFormats/Bgra32Tests.cs

  Font creation, styles, prototype constructors, metrics, colour fonts:
    Fonts/FontTests.cs, Fonts/FontStyleTests.cs

  FontCollection Add from path/stream/culture, TryGet, GetByCulture:
    Fonts/FontCollectionTests.cs

  FontFamily and FontDescription:
    Fonts/FontFamilyTests.cs, Fonts/FontDescriptionTests.cs

  SystemFonts access (and how to skip gracefully when the host has no fonts):
    Fonts/SystemFontsTests.cs

  The glyph rasterizer — non-zero winding fill, counters, alpha blending:
    Fonts/ImageGlyphRendererTests.cs

  8bpp grayscale BMP export against byte-exact reference files, for both
  indexing modes and all four colour matrices:
    Advanced/Format8bppIndexedTests.cs
    Advanced/BmpFormatHelperValidationTests.cs   (argument validation)

  DetectEncoder, AcceptVisitor, GetConfiguration, pixel memory access:
    Advanced/AdvancedImageExtensionsTests.cs

  ParallelExecutionSettings and ParallelRowIterator:
    Advanced/ParallelExecutionSettingsTests.cs, Advanced/ParallelRowIteratorTests.cs

  EXIF read/write round-trips, big-value paths, DeepClone, RemoveValue and
  malformed-input tolerance:
    Metadata/ExifProfileTests.cs

  XMP profile creation, GetDocument, DeepClone, ToByteArray:
    Metadata/XmpProfileTests.cs

  MemoryAllocator behaviour, IMemoryOwner<T>, idempotent disposal,
  ReleaseRetainedResources:
    Memory/MemoryAllocatorTests.cs, MemoryAllocators/MemoryAllocatorTests.cs

  Primitives — Point, Rectangle, Size:
    Primitives/PointTests.cs, Primitives/RectangleTests.cs, Primitives/SizeTests.cs

Raw file content can be fetched from:

    https://raw.githubusercontent.com/ellisnet/CodeBrix.Imaging/main/<path>

================================================================================

QUICK REFERENCE CARD
====================
Install         dotnet add package CodeBrix.Imaging.ApacheLicenseForever
Namespace       CodeBrix.Imaging          License  Apache-2.0
Target          .NET 10.0+                Deps     none

LOAD / SAVE
    Load            Image.Load("f.jpg") | Image.Load<Rgba32>("f.jpg")
    LoadAsync       await Image.LoadAsync(stream, token)
    Create          new Image<Rgba32>(w, h[, backgroundPixel])
    LoadPixelData   Image.LoadPixelData<Rgba32>(bytes, w, h, PngFormat.Instance)
    LoadFromBgra    Image.LoadPixelDataFromBgra(bytes, w, h, PngFormat.Instance)
    WrapMemory      Image.WrapMemory<Rgba32>(memory, w, h, PngFormat.Instance)
    Save            image.Save("f.png") | image.Save(stream, new PngEncoder())
    SaveAs*         image.SaveAsJpeg(stream, new JpegEncoder { Quality = 85 })
    Bytes/base64    image.ToByteArray(PngFormat.Instance) | ToBase64String(...)
    DetectFormat    Image.DetectFormat(stream)      (reset Position after!)
    Identify        Image.Identify(stream)          -> IImageInfo
    DetectEncoder   image.DetectEncoder("out.png")

PIPELINE
    In place        image.Mutate(x => x.Resize(w, h).Grayscale())
    Copy            using var c = image.Clone(x => x.Resize(w, h))

TRANSFORMS
    Resize          .Resize(w, h) | .Resize(new ResizeOptions { ... })
    Crop            .Crop(new Rectangle(x, y, w, h)) | .EntropyCrop()
    Pad             .Pad(w, h[, Color])
    Flip            .Flip(FlipMode.Horizontal)
    Rotate          .Rotate(90f) | .Rotate(RotateMode.Rotate90)
    RotateFlip      .RotateFlip(RotateMode.Rotate90, FlipMode.Horizontal)
    AutoOrient      .AutoOrient()
    Skew            .Skew(dx, dy)
    Affine          .Transform(new AffineTransformBuilder().AppendRotationDegrees(15))
    Projective      .Transform(new ProjectiveTransformBuilder().AppendTaper(...))
    Resamplers      KnownResamplers.Bicubic / Lanczos3 / NearestNeighbor / ...

FILTERS AND EFFECTS
    .Grayscale() .BlackWhite() .Invert() .Sepia() .Kodachrome() .Polaroid()
    .Lomograph() .Brightness(a) .Contrast(a) .Saturate(a) .Lightness(a)
    .Hue(deg) .Opacity(a) .ColorBlindness(mode) .Filter(colorMatrix)
    .GaussianBlur(sigma) .GaussianSharpen(sigma) .BoxBlur(r) .BokehBlur()
    .Pixelate(size) .OilPaint() .Vignette() .Glow() .DetectEdges()
    .HistogramEqualization() .ProcessPixelRowsAsVector4(op)

PALETTE / BINARY
    .Quantize(KnownQuantizers.Wu) | .Quantize(new WuQuantizer(options))
    .Dither(KnownDitherings.FloydSteinberg)
    .BinaryThreshold(0.5f[, BinaryThresholdMode.Luminance])
    .AdaptiveThreshold() | .BinaryDither(dither)

COMPOSITING
    .DrawImage(other, new Point(x, y), opacity)
    .BackgroundColor(Color.White)

PIXELS
    Indexer         image[x, y] = new Rgba32(r, g, b, a)     (generic only)
    Rows            image.ProcessPixelRows(a => a.GetRowSpan(y))
    Copy out        image.CopyPixelDataTo(span)
    Convert         image.CloneAs<Rgb24>()

TEXT   (using CodeBrix.Imaging.Fonts; using CodeBrix.Imaging.Fonts.Rendering;)
    Font            SystemFonts.CreateFont("Arial", 24, FontStyle.Bold)
    Embedded        new FontCollection().Add("f.ttf").CreateFont(24)
    Measure         TextRenderingExtensions.MeasureText(text, font)
    Draw            image.DrawText(text, font, Color.White, x, y)
    Draw + layout   image.DrawText(text, new TextOptions(font) { ... }, color)
    NEVER           image.Mutate(x => x.DrawText(...))     <- does not compile

METADATA
    image.Metadata.ExifProfile / XmpProfile / IccProfile / IptcProfile
    image.Metadata.GetPngMetadata() / GetJpegMetadata() / GetGifMetadata() ...
    exif.SetValue(ExifTag.Copyright, "...") / exif.GetValue(ExifTag.Model)?.Value

FRAMES
    image.Frames.Count | [i] | RootFrame | AddFrame(f) | CloneFrame(i)
                       | ExportFrame(i) (removes!) | RemoveFrame(i)
    frame.Metadata.GetGifMetadata().FrameDelay = 8      // hundredths of a second
    image.Metadata.GetGifMetadata().RepeatCount = 0     // loop forever

8BPP BMP   (using CodeBrix.Imaging.Helpers;)
    image.ExportAs8bppGrayscaleBmpFormat(stream[, matrix][, indexingMode])
    await image.ExportAs8bppGrayscaleBmpFormatAsync(path)
    BmpIndexingMode.Normal | BmpIndexingMode.SystemDrawingCompatible

CONFIG
    Configuration.Default.Clone() | .CreateSandboxed(megabytes)
    config.MaxDegreeOfParallelism | .PreferContiguousImageBuffers
    MemoryAllocator.Default.ReleaseRetainedResources()

Formats     BMP, GIF, JPEG, PBM, PNG, TGA, TIFF, WebP
Pixel types Rgba32 Rgb24 Bgra32 Bgr24 Argb32 Abgr32 Byte4 L8 L16 La16 La32 A8
            Rgb48 Rgba64 Rg32 RgbaVector HalfSingle HalfVector2 HalfVector4
            Bgr565 Bgra4444 Bgra5551 Rgba1010102 NormalizedByte2
            NormalizedByte4 NormalizedShort2 NormalizedShort4 Short2 Short4

================================================================================
