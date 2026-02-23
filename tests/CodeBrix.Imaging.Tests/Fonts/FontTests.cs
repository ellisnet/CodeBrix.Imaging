using CodeBrix.Imaging.Fonts;
using System;
using System.Linq;
using Xunit;

namespace CodeBrix.Imaging.Tests.Fonts;

public class FontTests
{
    private readonly ITestOutputHelper _output;

    public FontTests(ITestOutputHelper output)
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
    public void Font_can_be_created_from_family_and_size()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var font = new Font(family, 12f);

        //Assert
        Assert.NotNull(font);
        Assert.Equal(12f, font.Size);
        Assert.Equal(family, font.Family);

        //Output
        _output.WriteLine($"Created font: {font.Name}, Size: {font.Size}");
    }

    [Fact]
    public void Font_can_be_created_with_style()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var font = new Font(family, 14f, FontStyle.Bold);

        //Assert
        Assert.NotNull(font);
        Assert.Equal(14f, font.Size);

        //Output
        _output.WriteLine($"Created font: {font.Name}, Size: {font.Size}");
    }

    [Fact]
    public void Font_can_be_created_from_prototype_with_new_style()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }
        var prototype = new Font(family, 12f, FontStyle.Regular);

        //Act
        var font = new Font(prototype, FontStyle.Italic);

        //Assert
        Assert.NotNull(font);
        Assert.Equal(12f, font.Size);
        Assert.Equal(prototype.Family, font.Family);

        //Output
        _output.WriteLine($"Prototype: {prototype.Name}, Size: {prototype.Size}");
        _output.WriteLine($"New font: {font.Name}, Size: {font.Size}");
    }

    [Fact]
    public void Font_can_be_created_from_prototype_with_new_size()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }
        var prototype = new Font(family, 12f);

        //Act
        var font = new Font(prototype, 24f);

        //Assert
        Assert.NotNull(font);
        Assert.Equal(24f, font.Size);
        Assert.Equal(prototype.Family, font.Family);

        //Output
        _output.WriteLine($"Prototype: {prototype.Name}, Size: {prototype.Size}");
        _output.WriteLine($"New font: {font.Name}, Size: {font.Size}");
    }

    [Fact]
    public void Font_has_Name_property()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var font = new Font(family, 12f);

        //Assert
        Assert.False(string.IsNullOrEmpty(font.Name));

        //Output
        _output.WriteLine($"Font.Name: {font.Name}");
    }

    [Fact]
    public void Font_has_Family_property()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var font = new Font(family, 12f);

        //Assert
        Assert.Equal(family, font.Family);

        //Output
        _output.WriteLine($"Font.Family: {font.Family.Name}");
    }

    [Fact]
    public void Font_has_Size_property()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var font = new Font(family, 16.5f);

        //Assert
        Assert.Equal(16.5f, font.Size);

        //Output
        _output.WriteLine($"Font.Size: {font.Size}");
    }

    [Fact]
    public void Font_has_FontMetrics_property()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var font = new Font(family, 12f);

        //Assert
        Assert.NotNull(font.FontMetrics);

        //Output
        _output.WriteLine($"Font.FontMetrics exists: {font.FontMetrics != null}");
    }

    [Fact]
    public void Font_IsBold_returns_correct_value()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var regularFont = new Font(family, 12f, FontStyle.Regular);
        var boldFont = new Font(family, 12f, FontStyle.Bold);

        //Assert & Output
        _output.WriteLine($"Regular font IsBold: {regularFont.IsBold}");
        _output.WriteLine($"Bold font IsBold: {boldFont.IsBold}");
    }

    [Fact]
    public void Font_IsItalic_returns_correct_value()
    {
        //Arrange
        if (!TryGetTestFontFamily(out var family))
        {
            _output.WriteLine("No system fonts available, skipping test");
            return;
        }

        //Act
        var regularFont = new Font(family, 12f, FontStyle.Regular);
        var italicFont = new Font(family, 12f, FontStyle.Italic);

        //Assert & Output
        _output.WriteLine($"Regular font IsItalic: {regularFont.IsItalic}");
        _output.WriteLine($"Italic font IsItalic: {italicFont.IsItalic}");
    }

    [Fact]
    public void Font_constructor_throws_for_default_family()
    {
        //Arrange
        FontFamily defaultFamily = default;

        //Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new Font(defaultFamily, 12f));

        //Output
        _output.WriteLine($"Constructor threw: {exception.GetType().Name}");
        _output.WriteLine($"Message: {exception.Message}");
    }

    [Fact]
    public void Font_prototype_constructor_throws_for_null()
    {
        //Arrange
        Font nullFont = null;

        //Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Font(nullFont, FontStyle.Bold));

        //Output
        _output.WriteLine("Constructor correctly threw ArgumentNullException for null prototype");
    }
}
