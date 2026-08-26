================================================================================
MAINTAINER-README: CodeBrix.Imaging
Notes for people and agents MAINTAINING this repository — not for package
consumers
================================================================================

If you are CONSUMING the NuGet package, stop here and read AGENT-README.txt
instead. This file covers building, testing, packaging and vendored-source
maintenance of the repository itself.

PURPOSE AND SCOPE
=================
The repository produces exactly one NuGet package:

    PackageId:     CodeBrix.Imaging.ApacheLicenseForever
    Project:       src/CodeBrix.Imaging/CodeBrix.Imaging.csproj
    Assembly /
    root namespace: CodeBrix.Imaging
    License:       Apache-2.0 (PackageLicenseExpression)
    Consumer doc:  AGENT-README.txt (repo root)

There are no sibling packages and no companion projects. AGENT-README.txt is
the single consumer-facing agent document, and it ships inside the .nupkg.

REPOSITORY LAYOUT
=================
    AGENT-README.txt        Consumer documentation (packed into the nupkg).
    MAINTAINER-README.txt   This file.
    EXTRAS-README.txt       Non-package content in the repo.
    README-INDEX.txt        Map of the README files.
    README.md               Human-facing overview; packed as the nupkg readme.
    LICENSE                 Apache License 2.0.
    THIRD-PARTY-NOTICES.txt Upstream notices; packed into the nupkg.
    icon-codebrix-128.png   Package icon; packed into the nupkg.
    global.json             Pins the test runner to Microsoft.Testing.Platform.
    CodeBrix.Imaging.slnx   The solution.
    src/CodeBrix.Imaging/   The library project.
    tests/CodeBrix.Imaging.Tests/
                            The xUnit v3 test project.

Source folders under src/CodeBrix.Imaging/ mirror the namespace tree:
Advanced, Color, ColorSpaces (Companding, Conversion), Common (Exceptions,
Extensions, Helpers, Tuples), Compression/Zlib, Diagnostics, Fonts
(Exceptions, IO, Native, Rendering, Tables, Unicode, Utilities, WellKnownIds),
Formats (Bmp, Gif, Jpeg, Pbm, Png, Tga, Tiff, Webp), Helpers, Infrastructure,
IO, Memory (Allocators, DiscontiguousBuffers), Metadata (Profiles/Exif, ICC,
IPTC, XMP), PixelFormats (PixelBlenders, PixelImplementations, Utils),
Primitives, Processing (Extensions, Processors).

The solution's "Solution Items" folder carries .gitignore, AGENT-README.txt,
EXTRAS-README.txt, global.json, icon-codebrix-128.png, LICENSE,
MAINTAINER-README.txt, README-INDEX.txt, README.md and
THIRD-PARTY-NOTICES.txt; the "Tests" folder carries the test project.

BUILDING
========
    dotnet restore CodeBrix.Imaging.slnx
    dotnet build   CodeBrix.Imaging.slnx

The library targets net10.0 only — no multi-targeting. Relevant csproj
settings:

    AllowUnsafeBlocks            true   (SIMD and pixel-buffer code)
    GenerateDocumentationFile    true   (CodeBrix.Imaging.xml ships with the
                                        assembly; keep XML doc comments on
                                        every public member — fix CS1591 at
                                        the source, never suppress it)
    GeneratePackageOnBuild       true   (every build emits a .nupkg)
    DefineConstants adds: NULLABLE_ATTRIBUTES, SUPPORTS_ENCODING_STRING,
                          SUPPORTS_HASHCODE, SUPPORTS_MATHF,
                          SUPPORTS_CODECOVERAGE

Internals are exposed to the test project through an InternalsVisibleTo item
for CodeBrix.Imaging.Tests, so security-sensitive helpers (BitWriterUtils, the
format decoders and the decompressors) can be tested directly without
reflection.

TESTING
=======
    dotnet test CodeBrix.Imaging.slnx

The test project uses xunit.v3 with Microsoft.Testing.Platform (selected by
global.json) and xunit.runner.visualstudio. Sample images and fonts are
compiled in as EmbeddedResource items, so the tests do not depend on files on
disk for their inputs.

THE TESTING_ON_<OS>_<USER>_USER CONSTANT CONVENTION
---------------------------------------------------
Some tests write their output images to a scratch folder so a human can look
at the results. The folder is chosen at compile time by a preprocessor
constant, in tests/CodeBrix.Imaging.Tests/Core/ImageManipulationTests.cs:

    #if TESTING_ON_MACOS_JEREMY_USER
        public const string TempFolder = @"/Users/jeremy/Temp";
    #elif TESTING_ON_LINUX_JEREMY_USER
        public const string TempFolder = @"/home/jeremy/Temp";
    #elif TESTING_ON_LINUX_ORANGEPI_USER
        public const string TempFolder = "/home/orangepi/Temp";
    #elif TESTING_ON_LINUX_MINT_USER
        public const string TempFolder = "/home/mint/Temp";
    #elif TESTING_ON_LINUX_DEBIAN_USER
        public const string TempFolder = "/home/debian/Temp";
    #elif TESTING_ON_LINUX_UBUNTU_USER
        public const string TempFolder = "/home/ubuntu/Temp";
    #else
        //TESTING_ON_WINDOWS
        public const string TempFolder = @"C:\Temp";
    #endif

The name is always TESTING_ON_<OS>_<USER>_USER (with the Windows fallback
named simply TESTING_ON_WINDOWS). The constant is set in the test csproj:

    <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|AnyCPU'">
      <DefineConstants>$(DefineConstants);TESTING_ON_WINDOWS</DefineConstants>
    </PropertyGroup>

BEFORE RUNNING THE TESTS ON A NEW MACHINE:
  1. Change that DefineConstants value to the constant matching this host's
     OS and user, adding a new #elif branch (and folder path) to
     ImageManipulationTests.cs if none of the existing ones fits.
  2. Create the folder — the tests throw InvalidOperationException if the
     configured folder does not exist. They do not create it.
  3. Because TempFolder is used inside [InlineData], it MUST stay a `const`;
     do not replace it with Path.GetTempPath() or any runtime lookup.

The Windows default is what the csproj ships with, so a fresh clone on Linux
or macOS fails those tests until step 1 is done. That is expected, not a bug.

Some font tests skip themselves when the host has no system fonts installed
(containers, minimal CI images). A skipped font test is not a failure.

PACKAGING AND PUBLISHING
========================
GeneratePackageOnBuild is true, so `dotnet build` produces the .nupkg; there
is no separate pack script or pack driver.

The version is date-stamped and computed from System.DateTime.UtcNow at build
time, in this shape:

    1.<x>.<y>.<z>
      1  major     always 1 for this library
      x  minor     whole years since _VersionBaseYear (2026 = 0)
      y  build     day of year, 1-based, UTC (Jan 1 = 1)
      z  revision  minute of day, UTC (0..1439)

Consequences to keep in mind:
  * Every build produces a NEW version, and combined with
    GeneratePackageOnBuild that means a fresh .nupkg on every build.
  * Two builds within the same UTC minute produce the SAME version — never
    publish two packages from within one minute.
  * This is not SemVer. Major is pinned and minor encodes the year, so
    major/minor say nothing about API compatibility.
  * To re-baseline the minor number, change _VersionBaseYear in the csproj.
  * Version, AssemblyVersion and FileVersion are all set from the same value.

Files packed into the .nupkg (None items in the csproj, PackagePath ""):
    icon-codebrix-128.png   (PackageIcon)
    README.md               (PackageReadmeFile)
    AGENT-README.txt        (the consumer agent documentation)
    THIRD-PARTY-NOTICES.txt
plus the generated CodeBrix.Imaging.xml documentation file.

Other package metadata: Product/Title CodeBrix.Imaging, Authors Jeremy Ellis,
PackageLicenseExpression Apache-2.0, PackageRequireLicenseAcceptance true,
Copyright "Copyright (c) 2026 Jeremy Ellis and contributors",
PackageProjectUrl and RepositoryUrl both https://github.com/ellisnet/CodeBrix.Imaging.

MAINTAINER-README.txt, EXTRAS-README.txt and README-INDEX.txt are NOT packed —
they are repository documentation only.

PROVENANCE AND VENDORED SOURCES
===============================
The whole of src/CodeBrix.Imaging is vendored, namespace-renamed source, not a
package reference. THIRD-PARTY-NOTICES.txt is the authoritative record; keep
it in step with any source you add.

  * SixLabors.ImageSharp v2.1.13 (Apache-2.0), from the release/2.1.x line,
    released Nov 25, 2025 — the most recent release of the Apache-2.0-licensed
    2.1.x line. Namespaces renamed SixLabors.ImageSharp.* ->
    CodeBrix.Imaging.*.
  * SixLabors.Fonts v1.0.0-beta18 (Apache-2.0), released Jul 2, 2022.
    Namespaces renamed SixLabors.Fonts.* -> CodeBrix.Imaging.Fonts.*.

No code has been taken from any version of either project carrying a license
newer than Apache-2.0. That constraint is the whole reason the fork exists —
do NOT port fixes from later, differently licensed upstream releases.

Because the vendored snapshot tracks the 2.1.x line through v2.1.13, the seven
published SixLabors.ImageSharp security advisories — CVE-2024-27929,
CVE-2024-32035, CVE-2024-32036, CVE-2024-41131, CVE-2024-41132,
CVE-2025-27598 and CVE-2025-54575 — are already fixed in this code base.
Re-check that list before advancing the vendored snapshot.

Third-party code carried forward inside the ImageSharp source (all recorded in
THIRD-PARTY-NOTICES.txt): SharpZipLib (MIT) for the Deflate implementation,
Chromium (BSD-3-Clause) for the Crc32/Adler32 SIMD implementations, and .NET
CoreFX (MIT) for the Stream read/write extensions.

VENDORED PER-FOLDER README.md FILES — DO NOT INDEX OR REWRITE
--------------------------------------------------------------
Twelve README.md files live under src/CodeBrix.Imaging/. They are upstream
notes that came with the vendored source, recording where each codec or table
was adapted from and pointing at format specifications. They are NOT part of
the repository's documentation set: they are not listed in README-INDEX.txt,
they are not packed, and they must not be rewritten into CodeBrix
documentation. Leave them exactly as the upstream authors wrote them.

    Compression/Zlib/README.md                     SharpZipLib
    Fonts/Unicode/README.md                        dotnet/runtime, ICU,
                                                   RichTextKit, unicode-trie
    Formats/Bmp/README.md                          Nine.Imaging, imagetools
    Formats/Gif/README.md                          Nine.Imaging, imagetools
    Formats/Jpeg/README.md                         Go image/jpeg, pdf.js
    Formats/Png/README.md                          Nine.Imaging, imagetools,
                                                   pngcs
    Formats/Tga/README.md                          TGA format references
    Formats/Tiff/README.md                         TIFF 6.0 spec references
    Metadata/Profiles/Exif/README.md               Magick.NET
    Metadata/Profiles/IPTC/README.md               Magick.NET
    PixelFormats/README.md                         MonoGame
    Processing/Processors/Filters/README.md        colour-blindness matrices

CODING CONVENTIONS
==================
  * net10.0 only. Do not add target frameworks; do not add netstandard.
  * Nullable reference-type annotations are OFF for this repository. Do not
    add `?` to reference types.
  * XML doc comments are required on public members (GenerateDocumentationFile
    is true). Fix CS1591 by writing the comment, never by suppressing it.
  * Every renamed namespace line carries a trailing
    `//Was previously: namespace SixLabors...;` marker. Preserve that marker
    when you touch a file, and add one when you vendor a new file.
  * File-scoped namespaces throughout.
  * Vendored files keep their `// Copyright (c) Six Labors.` header, including
    partial-class files that were EXTENDED with CodeBrix-only members
    (Image.LoadPixelData.cs is the notable one). Wholly new files under
    Fonts/Rendering carry `// Copyright (c) Ellisnet`. Match the header of the
    file you are editing rather than imposing one.
  * Test classes are named <Class>Tests.cs; test method names in this repo are
    a mix of snake_case and Pascal_Case_With_Underscores; test bodies use
    Arrange / Act / Assert comment markers (both `//Arrange` and `// Arrange`
    spellings are present — follow the file you are in).
  * Never write the upstream namespaces (SixLabors.*) into new code.

NOTES
=====
  * CodeBrix-only public API that does NOT exist upstream, and therefore has
    no upstream documentation to fall back on:
        Image.LoadPixelDataFromBgra (4 overloads, SIMD channel reorder)
        CodeBrix.Imaging.Helpers.BmpFormatHelper +
            ExportAs8bppGrayscaleBmpFormat / ...Async (stream and path
            overloads, with and without a ColorMatrix)
        CodeBrix.Imaging.Helpers.BmpIndexingMode
        CodeBrix.Imaging.Fonts.Rendering.TextRenderingExtensions
            (DrawText / MeasureText)
        CodeBrix.Imaging.Fonts.Rendering.ImageGlyphRenderer<TPixel>
    Text rendering in particular is a CodeBrix addition — upstream splits it
    into a separate drawing package. Keep AGENT-README.txt loudly correct
    about the namespaces, because agents guess "CodeBrix.Imaging.Drawing"
    from upstream habit and reach for it to do text rendering. That namespace
    is NOT in this package, but it is not fictional either: it ships in the
    sibling CodeBrix.Imaging.Drawing repository, as two either/or packages,
    CodeBrix.Imaging.Drawing.ApacheLicenseForever (SkiaSharp-backed) and
    CodeBrix.Imaging.Drawing.NoSkia.ApacheLicenseForever (fully managed).
    Both take a dependency on this package and bridge back to it through
    CodeBrix.Imaging.Drawing.Extensions.ExportImagingImage(), which returns
    an Image<Rgba32>. So the correction to make in AGENT-README.txt is
    "not part of this package, reference that one" — never "does not exist",
    which was true before those packages shipped and is now wrong and
    actively unhelpful. AGENT-README.txt pitfall 21 carries the long form;
    if the sibling package ids or bridge API change, update it here too.
  * Image.LoadPixelData REQUIRES the IImageFormat argument in this fork; that
    divergence from upstream is deliberate.
  * The 8bpp BMP export is verified byte-for-byte against embedded reference
    .bmp files. If you change the palette or quantization, the reference files
    under tests/CodeBrix.Imaging.Tests/SampleFiles/ must be regenerated
    deliberately, not silently.
  * The public surface is large (more than 600 public types). When you add a
    feature area, add it to AGENT-README.txt in the same change — the file is
    the contract agents read instead of the source.

================================================================================
