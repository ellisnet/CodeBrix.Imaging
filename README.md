# CodeBrix.Imaging

A fully managed, cross-platform 2D image processing and font handling library for .NET.

CodeBrix.Imaging has no dependencies other than .NET, and is provided as a .NET 10 library and associated `CodeBrix.Imaging.ApacheLicenseForever` NuGet package.

CodeBrix.Imaging supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

CodeBrix.Imaging is a fork of the code of the open source SixLabors.ImageSharp and SixLabors.Fonts libraries - see below for licensing details.

## CodeBrix.Imaging supports:

* Image formats: BMP, GIF, JPEG, PBM, PNG, TGA, TIFF, WebP
* Image processing: resizing, cropping, rotating, flipping, and more
* Filters: brightness, contrast, saturation, hue, grayscale, sepia, and more
* Effects: Gaussian blur, Bokeh blur, edge detection, and more
* Drawing and text rendering
* Font handling (TrueType fonts)
* Color spaces and pixel formats
* Image metadata (EXIF, IPTC, XMP)
* Quantization and dithering
* Many more...

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

Note that additional sample code and usage examples are available in the `CodeBrix.Imaging.Tests` project.

## License

The project is licensed under the Apache License 2.0. see: https://en.wikipedia.org/wiki/Apache_License

All code originating from SixLabors.ImageSharp was included as allowed by the Apache License 2.0 permissible open source software license - as of Jun 19, 2022. This project (CodeBrix.Imaging) complies with all provisions of the source code license of SixLabors.ImageSharp v2.1.3 (Apache License 2.0).

All code originating from SixLabors.Fonts was included as allowed by the Apache License 2.0 permissible open source software license - as of Jul 22, 2022. This project (CodeBrix.Imaging) complies with all provisions of the source code license of SixLabors.Fonts v1.0.0 (Apache License 2.0).
