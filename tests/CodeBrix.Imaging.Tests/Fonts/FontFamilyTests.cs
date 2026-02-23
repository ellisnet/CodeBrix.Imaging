using CodeBrix.Imaging.Fonts;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace CodeBrix.Imaging.Tests.Fonts;

public class FontFamilyTests
{
    private readonly ITestOutputHelper _output;

    public FontFamilyTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private bool TryGetTestFontFamily(out FontFamily family)
    {
        var families = SystemFonts.Families.ToList();
        if (families.Count > 0)
        {
            family = families.First();
            return true;
        }
        family = default;
        return false;
    }

    [Fact]
    public void FontFamily_has_Name_property()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var name = family.Name;

        //Assert
        Assert.False(string.IsNullOrEmpty(name));

        //Output
        _output.WriteLine($"FontFamily.Name: {name}");
    }

    [Fact]
    public void FontFamily_has_Culture_property()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var culture = family.Culture;

        //Assert
        Assert.NotNull(culture);

        //Output
        _output.WriteLine($"FontFamily.Culture: {culture.Name}");
    }

    [Fact]
    public void FontFamily_CreateFont_returns_font_with_size()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var font = family.CreateFont(16f);

        //Assert
        Assert.NotNull(font);
        Assert.Equal(16f, font.Size);

        //Output
        _output.WriteLine($"Created font: {font.Name}, Size: {font.Size}");
    }

    [Fact]
    public void FontFamily_CreateFont_returns_font_with_style()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var font = family.CreateFont(18f, FontStyle.Italic);

        //Assert
        Assert.NotNull(font);
        Assert.Equal(18f, font.Size);

        //Output
        _output.WriteLine($"Created font: {font.Name}, Size: {font.Size}");
    }

    [Fact]
    public void FontFamily_GetAvailableStyles_returns_styles()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var styles = family.GetAvailableStyles().ToList();

        //Assert
        Assert.NotNull(styles);
        Assert.NotEmpty(styles);

        //Output
        _output.WriteLine($"Available styles for {family.Name}:");
        foreach (var style in styles)
        {
            _output.WriteLine($"  - {style}");
        }
    }

    [Fact]
    public void FontFamily_equality_works()
    {
        //Arrange
        var families = SystemFonts.Families.ToList();
        if (families.Count < 2)
        {
            _output.WriteLine("Not enough system fonts for equality test, skipping");
            return;
        }

        var family1 = families[0];
        var family2 = families[0];
        var family3 = families[1];

        //Act & Assert
        Assert.True(family1 == family2);
        Assert.True(family1 != family3);
        Assert.True(family1.Equals(family2));
        Assert.False(family1.Equals(family3));

        //Output
        _output.WriteLine($"family1 ({family1.Name}) == family2 ({family2.Name}): {family1 == family2}");
        _output.WriteLine($"family1 ({family1.Name}) != family3 ({family3.Name}): {family1 != family3}");
    }

    [Fact]
    public void FontFamily_ToString_returns_name()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var toString = family.ToString();

        //Assert
        Assert.Equal(family.Name, toString);

        //Output
        _output.WriteLine($"FontFamily.ToString(): {toString}");
    }

    [Fact]
    public void FontFamily_TryGetPaths_returns_paths_for_file_based_fonts()
    {
        //Arrange
        var collection = new FontCollection();
        var windowsFontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        
        if (!File.Exists(windowsFontPath))
        {
            _output.WriteLine($"Skipping test - font file not found: {windowsFontPath}");
            return;
        }
        
        var family = collection.Add(windowsFontPath);

        //Act
        var result = family.TryGetPaths(out var paths);

        //Assert
        Assert.True(result);
        Assert.NotNull(paths);
        Assert.NotEmpty(paths);

        //Output
        _output.WriteLine($"TryGetPaths returned: {result}");
        foreach (var path in paths)
        {
            _output.WriteLine($"  Path: {path}");
        }
    }
}
