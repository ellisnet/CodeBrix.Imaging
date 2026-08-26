# CodeBrix.Imaging

A fully managed, cross-platform 2D image processing, font handling and text rendering library for .NET.

CodeBrix.Imaging has no dependencies other than .NET, and is provided as a .NET 10 library and associated `CodeBrix.Imaging.ApacheLicenseForever` NuGet package.

CodeBrix.Imaging supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

CodeBrix.Imaging is a fork of the code of the open source SixLabors.ImageSharp and SixLabors.Fonts libraries - see below for licensing details.

## Installation

```
dotnet add package CodeBrix.Imaging.ApacheLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.Imaging`:

* NuGet package ID: `CodeBrix.Imaging.ApacheLicenseForever`
* Root namespace: `CodeBrix.Imaging` - i.e. `using CodeBrix.Imaging;`

The package has no NuGet dependencies, no native libraries and no platform-specific packages; it depends only on the .NET base class library. XML documentation (IntelliSense) ships alongside the assembly.

## CodeBrix.Imaging supports:

* Image formats: BMP, GIF, JPEG, PBM, PNG, TGA, TIFF, WebP
* Image processing: resizing, cropping, rotating, flipping, and more
* Filters: brightness, contrast, saturation, hue, grayscale, sepia, and more
* Effects: Gaussian blur, Bokeh blur, edge detection, and more
* Text rendering directly onto images, with `DrawText` and `MeasureText`
* Font handling: TrueType and OpenType/CFF fonts, variable fonts, colour (COLR/CPAL) fonts, and system fonts
* Image compositing (drawing one image onto another)
* Color spaces and pixel formats
* Image metadata (EXIF, IPTC, XMP)
* Quantization and dithering
* Animated GIF frames
* 8bpp grayscale BMP export (with optional System.Drawing-compatible mode)
* Many more...

CodeBrix.Imaging is a raster imaging and text rasterization library; it does not provide vector shape drawing (there are no `DrawLine`/`DrawPolygon`/`FillPath`/`Brush`/`Pen` APIs), SVG or PDF rendering, or GPU-accelerated processing. Compositing and text are the ways to draw onto an image.

## Sample Code

### Load and Resize an Image

```csharp
using CodeBrix.Imaging;
using CodeBrix.Imaging.Processing;

using var image = Image.Load("photo.jpg");

image.Mutate(x => x.Resize(800, 600));

image.Save("photo-resized.jpg");
```

### Convert Image Format

```csharp
using CodeBrix.Imaging;

using var image = Image.Load("photo.bmp");

image.Save("photo.png");
```

### Apply Filters and Effects

```csharp
using CodeBrix.Imaging;
using CodeBrix.Imaging.Processing;

using var image = Image.Load("photo.jpg");

image.Mutate(x => x
    .Grayscale()
    .GaussianBlur(3)
    .Resize(1024, 768));

image.Save("photo-processed.jpg");
```

### Crop an Image

```csharp
using CodeBrix.Imaging;
using CodeBrix.Imaging.Processing;

using var image = Image.Load("photo.jpg");

image.Mutate(x => x.Crop(new Rectangle(100, 100, 500, 400)));

image.Save("photo-cropped.jpg");
```

### Draw Text on an Image

```csharp
using CodeBrix.Imaging;
using CodeBrix.Imaging.Fonts;
using CodeBrix.Imaging.Fonts.Rendering;

using var image = Image.Load("photo.jpg");

var font = SystemFonts.CreateFont("Arial", 36f);

image.DrawText("Hello, world!", font, Color.White, 10f, 10f);

image.Save("photo-with-text.jpg");
```

Note that `DrawText` is an extension method on the image itself - not on the `Mutate()` processing context - and that there is no `CodeBrix.Imaging.Drawing` namespace.

### Export as 8bpp Grayscale BMP

```csharp
using CodeBrix.Imaging;
using CodeBrix.Imaging.Helpers;

using var image = Image.Load("photo.jpg");
await using var fs = new FileStream("grayscale-8bpp.bmp", FileMode.Create);
await image.ExportAs8bppGrayscaleBmpFormatAsync(fs);
```

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

Additional sample code and usage examples are available in the `CodeBrix.Imaging.Tests` project:
https://github.com/ellisnet/CodeBrix.Imaging/tree/main/tests/CodeBrix.Imaging.Tests

## License

The project is licensed under the Apache License 2.0. see: https://en.wikipedia.org/wiki/Apache_License

All code originating from SixLabors.ImageSharp was included as allowed by the Apache License 2.0 permissible open source software license. The included code corresponds to SixLabors.ImageSharp v2.1.13 (released Nov 25, 2025), the most recent release of the Apache-2.0-licensed 2.1.x line. This project (CodeBrix.Imaging) complies with all provisions of the source code license of SixLabors.ImageSharp v2.1.13 (Apache License 2.0).

All code originating from SixLabors.Fonts was included as allowed by the Apache License 2.0 permissible open source software license - as of Jul 22, 2022. The included code corresponds to SixLabors.Fonts v1.0.0-beta18 (released Jul 2, 2022). This project (CodeBrix.Imaging) complies with all provisions of the source code license of SixLabors.Fonts v1.0.0-beta18 (Apache License 2.0).
