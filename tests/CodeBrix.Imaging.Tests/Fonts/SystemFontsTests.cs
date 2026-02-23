using CodeBrix.Imaging.Fonts;
using System;
using System.Linq;
using Xunit;

namespace CodeBrix.Imaging.Tests.Fonts;

public class SystemFontsTests
{
    private readonly ITestOutputHelper _output;

    public SystemFontsTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public void SystemFonts_Collection_is_not_null()
    {
        //Arrange & Act
        var collection = SystemFonts.Collection;

        //Assert
        Assert.NotNull(collection);

        //Output
        _output.WriteLine($"SystemFonts.Collection exists: {collection != null}");
    }

    [Fact]
    public void SystemFonts_Families_returns_font_families()
    {
        //Arrange & Act
        var families = SystemFonts.Families.ToList();

        //Assert
        Assert.NotNull(families);
        Assert.NotEmpty(families);

        //Output
        _output.WriteLine($"System font families count: {families.Count}");
        foreach (var family in families.Take(10))
        {
            _output.WriteLine($"  - {family.Name}");
        }
        if (families.Count > 10)
        {
            _output.WriteLine($"  ... and {families.Count - 10} more");
        }
    }

    [Fact]
    public void SystemFonts_TryGet_returns_true_for_common_font()
    {
        //Arrange
        // Try common fonts that should exist on most systems
        var commonFonts = new[]
        {
            "Arial", "Times New Roman", "Segoe UI", "Verdana", "Tahoma",
            "DejaVu Sans", "FreeSans", //Should be valid on Raspberry Pi OS (Linux)
        };
        var foundFont = false;
        string foundFontName = null;

        //Act
        foreach (var fontName in commonFonts)
        {
            if (SystemFonts.TryGet(fontName, out var family))
            {
                foundFont = true;
                foundFontName = family.Name;
                break;
            }
        }

        //Assert
        Assert.True(foundFont, "At least one common system font should be available");

        //Output
        _output.WriteLine($"Found common font: {foundFontName}");
    }

    [Fact]
    public void SystemFonts_TryGet_returns_false_for_nonexistent_font()
    {
        //Arrange
        var fakeFontName = "NonExistentFont_12345_XYZ";

        //Act
        var result = SystemFonts.TryGet(fakeFontName, out var family);

        //Assert
        Assert.False(result);
        Assert.Equal(default, family);

        //Output
        _output.WriteLine($"TryGet for '{fakeFontName}' returned: {result}");
    }

    [Fact]
    public void SystemFonts_CreateFont_creates_font_with_size()
    {
        //Arrange
        var families = SystemFonts.Families.ToList();
        if (families.Count == 0)
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }
        var familyName = families.First().Name;

        //Act
        var font = SystemFonts.CreateFont(familyName, 12f);

        //Assert
        Assert.NotNull(font);
        Assert.Equal(12f, font.Size);

        //Output
        _output.WriteLine($"Created font: {font.Name}, Size: {font.Size}");
    }

    [Fact]
    public void SystemFonts_CreateFont_creates_font_with_style()
    {
        //Arrange
        var families = SystemFonts.Families.ToList();
        if (families.Count == 0)
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }
        var familyName = families.First().Name;

        //Act
        var font = SystemFonts.CreateFont(familyName, 14f, FontStyle.Bold);

        //Assert
        Assert.NotNull(font);
        Assert.Equal(14f, font.Size);

        //Output
        _output.WriteLine($"Created font: {font.Name}, Size: {font.Size}, Requested Style: Bold");
    }
}
