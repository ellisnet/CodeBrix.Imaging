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
    tools/jpeg-xl/          Optional JPEG XL fixture-maintenance tools.

Source folders under src/CodeBrix.Imaging/ mirror the namespace tree:
Advanced, Color, ColorSpaces (Companding, Conversion, Icc), Common (Exceptions,
Extensions, Helpers, Tuples), Compression/Zlib, Diagnostics, Fonts
(Exceptions, IO, Native, Rendering, Tables, Unicode, Utilities, WellKnownIds),
Formats (Bmp, Gif, Jpeg, Jxl, Pbm, Png, Tga, Tiff, Webp), Helpers, Infrastructure,
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
    dotnet test --solution CodeBrix.Imaging.slnx

The test project uses xunit.v3 with Microsoft.Testing.Platform (selected by
global.json) and xunit.runner.visualstudio. Run the same tests from Visual
Studio 2026 Test Explorer on Windows. Most sample images and fonts are embedded;
JPEG XL fixtures are copied from the repository into the test output directory.
Ordinary JPEG XL tests need no native tools, external reference files or network.

Optional JPEG XL external-reference, timing and size/quality tests are
described in "JPEG XL TESTS AND FIXTURE TOOLS" below.

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
Note that the DefineConstants line sits in the Debug|AnyCPU PropertyGroup: a
Release test run always uses C:\Temp, so on Linux or macOS those tests fail in
Release even after step 1 - run them in Debug.

Some font tests skip themselves when the host has no system fonts installed
(containers, minimal CI images). A skipped font test is not a failure.

JPEG XL TESTS AND FIXTURE TOOLS
===============================
All JPEG XL tests, including conformance, timing and size/quality checks, are
xUnit tests in CodeBrix.Imaging.Tests, run through dotnet test or Visual Studio
2026 Test Explorer. Commands below run from the repository root. Debug and
Release both support correctness testing.

Run only the JPEG XL tests:

    dotnet test --project tests/CodeBrix.Imaging.Tests/CodeBrix.Imaging.Tests.csproj -c Release -p:GeneratePackageOnBuild=false --filter-class '*Jxl*'

Resources. JPEG XL fixtures live in tests/CodeBrix.Imaging.Tests/SampleFiles/
JxlConformance and JxlSynthetic and are copied (not embedded) to the test
output directory. Together they must stay below 100,000,000 bytes. Each folder
has a NOTICE.txt carrying the image-specific licenses and attributions; keep
those files beside the images and in step with THIRD-PARTY-NOTICES.txt.
Ordinary JPEG XL tests read only these fixtures and need no native tools,
downloads, network or fixture generators. Optional checks report Skipped.

In Visual Studio, set any environment variables below before launching it so
the test host inherits them, and relaunch after changing them.

External-reference checks
-------------------------
Set CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS=1 to enable external reference
images and native-codec comparisons. Tests check this gate before reading a
cache or accessing anything outside the repository. References are downloaded
into a hash-verified temporary cache; CODEBRIX_IMAGING_JXL_REFERENCE_CACHE
overrides its directory. For native comparisons, also set
CODEBRIX_IMAGING_JXL_NATIVE_TOOLS to an absolute directory containing the
pinned libjxl tools (djxl, cjxl, butteraugli_main; .exe on Windows). There is
no PATH or shared-temp fallback; a missing tool or unset directory reports
Skipped. Native tools are test-only and never dependencies of the library.

  * --filter-class '*JxlFullConformanceTests' runs the full conformance suite.
  * DOTNET_EnableHWIntrinsic=0, set before starting the test process, gives a
    scalar (non-SIMD) correctness run.

Playback and lossless timing checks
-----------------------------------
Set CODEBRIX_IMAGING_RUN_JXL_PERFORMANCE_TESTS=1 and run in Release on an
otherwise idle machine (timing tests skip in Debug):

    dotnet test --project tests/CodeBrix.Imaging.Tests/CodeBrix.Imaging.Tests.csproj -c Release -p:GeneratePackageOnBuild=false --filter-class '*JxlPlaybackPerformanceTests' --output Detailed

JxlPlaybackPerformanceTests uses only the repository's animation inputs. Each
animation gets two warm-up passes and five measured passes; the pass rule is
median(frame.DecodeTime / frame.Duration) <= 0.5. DecodeTime includes decoding,
rendering and packing (including hidden dependencies) but excludes output-queue
waits, header setup and the final container drain. The collection disables
parallel execution with other tests. The reference host for this rule is an
Intel Core i7-12850HX (24 logical processors), LMDE 7 x64, .NET 10, normal SIMD
and configured parallelism; timings on other hosts or under QEMU do not
replace it.

With the same opt-in, JxlLosslessPerformanceTests encodes generated screenshots,
blank images and checkerboards losslessly, verifies exact round trips, and
fails any encode exceeding a 60-second regression ceiling.

Encoder size/quality checks
---------------------------
Set CODEBRIX_IMAGING_RUN_JXL_SIZE_TESTS=1 (plus the external-reference variables
above) and run in Release:

    dotnet test --solution CodeBrix.Imaging.slnx -c Release --no-build --filter-class '*JxlEncoderSizeTests' --parallel all --max-threads 4 --output Detailed

This compares the managed lossy encoder with native cjxl at efforts 1-10 and
nominal distances 0.5, 1, 2 and 3 on six repository images, with identical
pixels, metadata and container settings. Managed distance is searched until
its Butteraugli score is within 0.5% of native. Each point requires a geometric
mean managed/native size ratio <= 1.10 and every individual ratio <= 1.20.
These are long-running tests. The traits JxlEffort, JxlDistance and
JxlQualityPoint select subsets (for example --filter-trait JxlQualityPoint=7:1).
CODEBRIX_IMAGING_JXL_SIZE_REPORT_DIRECTORY names a fresh folder for per-point
JSON-lines records. CODEBRIX_IMAGING_RUN_JXL_SIZE_PROBE=1 enables a separate
diagnostic fact (not part of the size check), adjusted with
CODEBRIX_IMAGING_JXL_SIZE_PROBE_POINT (effort:distance, e.g. 7:3),
CODEBRIX_IMAGING_JXL_SIZE_PROBE_IMAGE (one of the six image names) and
CODEBRIX_IMAGING_JXL_SIZE_PROBE_DOTS (true/false); with a report directory it
also saves the source and both codestreams.

Fuzzing
-------
JxlFuzzTests (trait Category=JxlFuzz) runs a coverage-guided, in-process
SharpFuzz campaign over the decoder entry points on every test run, with
256 mutations by default. Set CODEBRIX_IMAGING_JXL_FUZZ_ITERATIONS (1 through
100000) for a longer campaign before a release. Each case is limited to
twenty million instrumented branch visits and 128 MiB of managed allocation;
seekable and forward-only reads must agree. Failing inputs are saved under
.jxl-fuzz-failures in the test output directory.

Fixture-maintenance tools (tools/jpeg-xl/)
------------------------------------------
Optional utilities for regenerating checked-in fixtures; no test invokes them,
and they are not needed to build or test. All of them check
CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS=1 before touching external checkouts or
native tools. Run them manually only when intentionally regenerating assets.

  upstream.json             Pins the reviewed upstream revisions: jxl-oxide,
                            libjxl (and its Highway/skcms submodules used by
                            the native reference build), skcms, conformance.
  vendor-conformance.py     Copies conformance assets from a local checkout at
                            the pinned revision (never downloads); also needs
                            CODEBRIX_IMAGING_JXL_NATIVE_TOOLS. Checks the
                            100,000,000-byte aggregate cap.
  generate-render-fixtures.py
                            Small RGB sampling images run through cjxl/djxl,
                            written as .jxl plus float .npy references and
                            hashes. Default is Modular; --vardct, --noise and
                            --progressive generate separate fixture families.
  generate-jpeg-fixtures.py JPEG/JXL pairs for byte-exact reconstruction tests
                            (cjpeg, cjxl, djxl). Checks the aggregate cap
                            before writing.
  generate-hdr-fixtures.py  PQ/HLG original, sRGB and SDR float references.
                            Pass --cjxl/--djxl for the pinned libjxl build
                            (djxl 0.11.2 ignores non-XYB color requests and is
                            rejected) and --tone-reference for a compiled
                            tone-reference.cc (C++17, -O2 -DNDEBUG, include
                            paths for the pinned libjxl source and its
                            build/lib/include).
  GeneratePrecisionFixture.cs
                            Builds the 31-bit integer31.jxl fixture without
                            native tools.
  GenerateCmsFixtures.cs    Runs a compiled skcms-reference.cc to produce the
                            color-conversion fixtures. Build that wrapper with
                            skcms.cc and src/skcms_TransformBaseline.cc,
                            defining SKCMS_DISABLE_HSW and SKCMS_DISABLE_SKX.

The two .cs generators run from a temporary .NET 10 console project outside the
repository that references src/CodeBrix.Imaging/CodeBrix.Imaging.csproj, sets
AssemblyName to CodeBrix.Imaging.Tests (for internal access) and
GeneratePackageOnBuild=false, and includes the generator source.
GeneratePrecisionFixture takes the destination integer31.jxl path;
GenerateCmsFixtures takes the repository root and the skcms-reference path.

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

JPEG XL UPSTREAMS AND RE-SYNC PROCEDURE
---------------------------------------
Formats/Jxl and ColorSpaces/Icc are C# ports of three upstreams, pinned to
the exact revisions recorded in tools/jpeg-xl/upstream.json (with the libjxl
native reference build's Highway and skcms submodule revisions):

  * jxl-oxide (MIT OR Apache-2.0; Apache-2.0 selected) — the decoder.
  * libjxl (BSD-3-Clause plus the PATENTS grant) — the encoder, JPEG
    reconstruction and several decoder corrections.
  * skcms (BSD-3-Clause) — the managed ICC color management.
  * libjxl/conformance (BSD-3-Clause, per-image licenses) — test data only;
    see the NOTICE.txt files beside the fixtures.

Every ported file starts with a provenance header naming its upstream
file(s) and pinned revision ("Adapted from <upstream> <path>, <revision>").
Files without such a header are original CodeBrix code. THIRD-PARTY-NOTICES.txt carries the
upstream notices, the PATENTS grant and a record of each adaptation.

To pick up upstream fixes:

 1. Check out the old (pinned) and the new upstream revision OUTSIDE the
    repository. Confirm the new revision's license is still compatible with
    Apache-2.0; never port from a differently licensed revision.
 2. List the ported files and their sources:
        grep -rn -A2 "Adapted from" src/CodeBrix.Imaging/Formats/Jxl \
            src/CodeBrix.Imaging/ColorSpaces/Icc
    and diff each named upstream file between the two revisions
    (git diff <old>..<new> -- <paths>).
 3. Port the relevant changes, keeping upstream operation order (floating-
    point drift breaks conformance limits). Update the revision in each
    touched file's provenance header, in tools/jpeg-xl/upstream.json and in
    THIRD-PARTY-NOTICES.txt together.
 4. Rebuild the native reference tools at the new libjxl revision (outside
    the repository, e.g. ~/Temp/codebrix-jxl-reference-build):
        git clone https://github.com/libjxl/libjxl.git src
        git -C src checkout <revision>
        git -C src submodule update --init --recursive
        cmake -S src -B build -DCMAKE_BUILD_TYPE=Release -DBUILD_TESTING=OFF \
            -DJPEGXL_ENABLE_DEVTOOLS=ON -DJPEGXL_ENABLE_BENCHMARK=OFF \
            -DJPEGXL_ENABLE_EXAMPLES=OFF -DJPEGXL_ENABLE_MANPAGES=OFF \
            -DJPEGXL_ENABLE_SJPEG=OFF -DJPEGXL_ENABLE_OPENEXR=OFF \
            -DJPEGXL_ENABLE_PLUGINS=OFF -DJPEGXL_ENABLE_JNI=OFF \
            -DJPEGXL_ENABLE_DOXYGEN=OFF -DJPEGXL_ENABLE_JPEGLI=OFF
        make -C build -j cjxl djxl butteraugli_main
    then copy build/tools/{cjxl,djxl,butteraugli_main} plus a djpeg into one
    directory for CODEBRIX_IMAGING_JXL_NATIVE_TOOLS.
 5. Regenerate fixtures only where upstream behaviour intentionally changed
    (tools/jpeg-xl generators), then run the ordinary suite, the external-
    reference suite, the timing checks and the encoder size/quality checks
    described in "JPEG XL TESTS AND FIXTURE TOOLS".

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
    Fonts/Rendering carry
    `// Copyright (c) 2026 Jeremy Ellis and contributors`. Match the header of
    the file you are editing rather than imposing one.
  * Test classes are named <Class>Tests.cs; test method names in this repo are
    a mix of snake_case and Pascal_Case_With_Underscores; test bodies use
    Arrange / Act / Assert comment markers (both `//Arrange` and `// Arrange`
    spellings are present — follow the file you are in).
  * Never write the upstream namespaces (SixLabors.*) into new code.

NOTES
=====
  * TRIMMING: the assembly is trim-clean. The trim analyzer
    (-p:EnableTrimAnalyzer=true -p:IsTrimmable=true) reports nothing, and a
    fully trimmed publish of a small console app referencing the project
    (PublishTrimmed=true, TrimmerSingleWarn=false, -r linux-x64) reports no
    ILLink warnings. The one warning that publish used to report - IL2075 in
    ExifTagDescriptionAttribute.GetDescription, which looked the enum field up
    through tagValue.GetType() - surfaced as "IL2104: Assembly
    'CodeBrix.Imaging' produced trim warnings" in every trimmed consumer (an
    Android Release build, for one); it now names typeof(ExifTagValue). The
    descriptions worked even before (the trimmer keeps enum fields), so this
    removed a warning, not a malfunction; ExifProfileTests ToString_* fence
    the lookup. Re-run that trimmed publish after adding reflection.
  * CodeBrix-only public API that does NOT exist upstream, and therefore has
    no upstream documentation to fall back on:
        Image.LoadPixelDataFromBgra (4 overloads, SIMD channel reorder)
        CodeBrix.Imaging.Helpers.BmpFormatHelper +
            ExportAs8bppGrayscaleBmpFormat / ...Async (stream and path
            overloads, with and without a ColorMatrix)
        CodeBrix.Imaging.Helpers.BmpIndexingMode
        CodeBrix.Imaging.Fonts.Rendering.TextRenderingExtensions
            (DrawText extension methods / static MeasureText helper)
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
    CodeBrix.Imaging.Drawing.Extensions.ExportImagingImage(outputSize),
    which returns an Image<Rgba32> (the output size is required; there is no
    parameterless overload). So the correction to make in AGENT-README.txt is
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
