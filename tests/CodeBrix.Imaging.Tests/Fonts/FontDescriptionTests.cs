using CodeBrix.Imaging.Fonts;
using System;
using System.Globalization;
using System.IO;
using Xunit;

namespace CodeBrix.Imaging.Tests.Fonts;

public class FontDescriptionTests
{
    private readonly ITestOutputHelper _output;

    public FontDescriptionTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private string GetTestFontPath()
    {
        var windowsFontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        return File.Exists(windowsFontPath) ? windowsFontPath : null;
    }

    [Fact]
    public void FontDescription_LoadDescription_from_path_works()
    {
        //Arrange
        var fontPath = GetTestFontPath();
        if (fontPath == null)
        {
            _output.WriteLine("Skipping test - no test font available");
            return;
        }

        //Act
        var description = FontDescription.LoadDescription(fontPath);

        //Assert
        Assert.NotNull(description);

        //Output
        _output.WriteLine($"Loaded description from: {fontPath}");
        _output.WriteLine($"  FontNameInvariantCulture: {description.FontNameInvariantCulture}");
        _output.WriteLine($"  FontFamilyInvariantCulture: {description.FontFamilyInvariantCulture}");
        _output.WriteLine($"  FontSubFamilyNameInvariantCulture: {description.FontSubFamilyNameInvariantCulture}");
        _output.WriteLine($"  Style: {description.Style}");
    }

    [Fact]
    public void FontDescription_LoadDescription_from_stream_works()
    {
        //Arrange
        var fontPath = GetTestFontPath();
        if (fontPath == null)
        {
            _output.WriteLine("Skipping test - no test font available");
            return;
        }

        //Act
        using var stream = File.OpenRead(fontPath);
        var description = FontDescription.LoadDescription(stream);

        //Assert
        Assert.NotNull(description);

        //Output
        _output.WriteLine($"Loaded description from stream");
        _output.WriteLine($"  FontNameInvariantCulture: {description.FontNameInvariantCulture}");
        _output.WriteLine($"  FontFamilyInvariantCulture: {description.FontFamilyInvariantCulture}");
    }

    [Fact]
    public void FontDescription_has_Style_property()
    {
        //Arrange
        var fontPath = GetTestFontPath();
        if (fontPath == null)
        {
            _output.WriteLine("Skipping test - no test font available");
            return;
        }

        //Act
        var description = FontDescription.LoadDescription(fontPath);
        var style = description.Style;

        //Assert
        Assert.True(Enum.IsDefined(typeof(FontStyle), style));

        //Output
        _output.WriteLine($"FontDescription.Style: {style}");
    }

    [Fact]
    public void FontDescription_FontName_with_culture_works()
    {
        //Arrange
        var fontPath = GetTestFontPath();
        if (fontPath == null)
        {
            _output.WriteLine("Skipping test - no test font available");
            return;
        }
        var description = FontDescription.LoadDescription(fontPath);

        //Act
        var fontName = description.FontName(CultureInfo.InvariantCulture);

        //Assert
        Assert.False(string.IsNullOrEmpty(fontName));

        //Output
        _output.WriteLine($"FontName (InvariantCulture): {fontName}");
    }

    [Fact]
    public void FontDescription_FontFamily_with_culture_works()
    {
        //Arrange
        var fontPath = GetTestFontPath();
        if (fontPath == null)
        {
            _output.WriteLine("Skipping test - no test font available");
            return;
        }
        var description = FontDescription.LoadDescription(fontPath);

        //Act
        var fontFamily = description.FontFamily(CultureInfo.InvariantCulture);

        //Assert
        Assert.False(string.IsNullOrEmpty(fontFamily));

        //Output
        _output.WriteLine($"FontFamily (InvariantCulture): {fontFamily}");
    }

    [Fact]
    public void FontDescription_FontSubFamilyName_with_culture_works()
    {
        //Arrange
        var fontPath = GetTestFontPath();
        if (fontPath == null)
        {
            _output.WriteLine("Skipping test - no test font available");
            return;
        }
        var description = FontDescription.LoadDescription(fontPath);

        //Act
        var subFamilyName = description.FontSubFamilyName(CultureInfo.InvariantCulture);

        //Assert
        Assert.NotNull(subFamilyName);

        //Output
        _output.WriteLine($"FontSubFamilyName (InvariantCulture): {subFamilyName}");
    }

    [Fact]
    public void FontDescription_invariant_culture_properties_match_methods()
    {
        //Arrange
        var fontPath = GetTestFontPath();
        if (fontPath == null)
        {
            _output.WriteLine("Skipping test - no test font available");
            return;
        }
        var description = FontDescription.LoadDescription(fontPath);

        //Act & Assert
        Assert.Equal(description.FontNameInvariantCulture, description.FontName(CultureInfo.InvariantCulture));
        Assert.Equal(description.FontFamilyInvariantCulture, description.FontFamily(CultureInfo.InvariantCulture));
        Assert.Equal(description.FontSubFamilyNameInvariantCulture, description.FontSubFamilyName(CultureInfo.InvariantCulture));

        //Output
        _output.WriteLine("Invariant culture properties match method results");
    }

    [Fact]
    public void FontDescription_LoadDescription_throws_for_null_path()
    {
        //Arrange
        string nullPath = null;

        //Act & Assert
        Assert.Throws<ArgumentNullException>(() => FontDescription.LoadDescription(nullPath!));

        //Output
        _output.WriteLine("LoadDescription correctly throws for null path");
    }

    [Fact]
    public void FontDescription_LoadDescription_throws_for_null_stream()
    {
        //Arrange
        Stream nullStream = null;

        //Act & Assert
        Assert.Throws<ArgumentNullException>(() => FontDescription.LoadDescription(nullStream!));

        //Output
        _output.WriteLine("LoadDescription correctly throws for null stream");
    }
}
