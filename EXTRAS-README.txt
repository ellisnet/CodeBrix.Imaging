================================================================================
EXTRAS-README: CodeBrix.Imaging
Samples, tools and other content in this repository that is not part of a
NuGet package
================================================================================

This repository has no samples folder, no tools folder and no demo
applications. Everything that ships is in src/CodeBrix.Imaging, and everything
else in the repository is either documentation or test material.

The only non-package content is the test project.

TEST PROJECT
============
    tests/CodeBrix.Imaging.Tests/

An xUnit v3 test project running on Microsoft.Testing.Platform (selected by
global.json at the repo root). It is not packed and is not published.

Beyond verifying the library, it doubles as the worked-example set for the
package: AGENT-README.txt's "WORKING EXAMPLES ON GITHUB" section maps each
feature area to the test file that exercises it. Anyone learning the API
should read those files.

    dotnet test CodeBrix.Imaging.slnx

NOTE: several tests write their output images to a scratch folder chosen at
compile time by a TESTING_ON_<OS>_<USER>_USER preprocessor constant, and they
throw if that folder does not exist. See the TESTING section of
MAINTAINER-README.txt before running the suite on a new machine.

OPTIONAL / EMBEDDED TEST DATA
=============================
    tests/CodeBrix.Imaging.Tests/SampleFiles/

All of these are EmbeddedResource items compiled into the test assembly, so
the suite does not read them from disk.

  Source images (one of each, in three formats, used across the manipulation,
  format-detection and export tests):
      test-image-01.bmp
      test-image-01.jpg
      test-image-01.png

  Byte-exact reference output for the 8bpp grayscale BMP export — 30 files
  covering the cross-product of source format (BMP / JPG / PNG), colour matrix
  (default, BT.601, BT.709, and a deliberately red-heavy custom matrix) and
  BmpIndexingMode (Normal / SystemDrawingCompatible):
      ExportAs8bppGrayscaleBmp_*_reference.bmp

  These reference files are the specification for that exporter. Do not
  regenerate them casually — a diff there means the exporter's output changed.

FONTS USED BY THE TESTS
=======================
    tests/CodeBrix.Imaging.Tests/SampleFiles/fonts/

  Roboto-Regular.ttf
      A plain TrueType face; the default font for the text-rendering tests.
  Nabla-Regular-VariableFont_EDPT_EHLT.ttf
      A variable font, used to exercise variable-font loading.
  NotoColorEmoji-Regular.ttf
      A colour (COLR/CPAL) font, used to exercise colour-glyph rendering and
      the forceMonoColor switch on DrawText.

They are embedded so the text tests run on hosts with no system fonts
installed. Tests that specifically need SYSTEM fonts skip themselves when the
host has none, which is normal on minimal containers and CI images.

DOCUMENTATION FILES
===================
Not extras exactly, but for completeness: README.md, AGENT-README.txt,
MAINTAINER-README.txt, EXTRAS-README.txt and README-INDEX.txt at the repo
root are documentation. See README-INDEX.txt for the map.

The twelve README.md files under src/CodeBrix.Imaging/ are vendored upstream
notes that came with the forked source. They are not part of this
repository's documentation set and are deliberately left as their original
authors wrote them.

================================================================================
