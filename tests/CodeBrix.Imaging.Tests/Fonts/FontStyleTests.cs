using CodeBrix.Imaging.Fonts;
using System;
using System.Linq;
using Xunit;

namespace CodeBrix.Imaging.Tests.Fonts;

public class FontStyleTests
{
    private readonly ITestOutputHelper _output;

    public FontStyleTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public void FontStyle_Regular_has_value_zero()
    {
        //Arrange & Act
        var style = FontStyle.Regular;

        //Assert
        Assert.Equal(0, (int)style);

        //Output
        _output.WriteLine($"FontStyle.Regular = {(int)style}");
    }

    [Fact]
    public void FontStyle_Bold_has_value_one()
    {
        //Arrange & Act
        var style = FontStyle.Bold;

        //Assert
        Assert.Equal(1, (int)style);

        //Output
        _output.WriteLine($"FontStyle.Bold = {(int)style}");
    }

    [Fact]
    public void FontStyle_Italic_has_value_two()
    {
        //Arrange & Act
        var style = FontStyle.Italic;

        //Assert
        Assert.Equal(2, (int)style);

        //Output
        _output.WriteLine($"FontStyle.Italic = {(int)style}");
    }

    [Fact]
    public void FontStyle_BoldItalic_has_value_three()
    {
        //Arrange & Act
        var style = FontStyle.BoldItalic;

        //Assert
        Assert.Equal(3, (int)style);
        Assert.True(style.HasFlag(FontStyle.Bold));
        Assert.True(style.HasFlag(FontStyle.Italic));

        //Output
        _output.WriteLine($"FontStyle.BoldItalic = {(int)style}");
    }

    [Fact]
    public void FontStyle_can_be_combined_with_flags()
    {
        //Arrange & Act
        var combined = FontStyle.Bold | FontStyle.Italic;

        //Assert
        Assert.Equal(FontStyle.BoldItalic, combined);
        Assert.True(combined.HasFlag(FontStyle.Bold));
        Assert.True(combined.HasFlag(FontStyle.Italic));

        //Output
        _output.WriteLine($"Bold | Italic = {combined}");
    }

    [Fact]
    public void FontStyle_is_flags_enum()
    {
        //Arrange
        var type = typeof(FontStyle);

        //Act
        var hasFlagsAttribute = type.GetCustomAttributes(typeof(FlagsAttribute), false).Any();

        //Assert
        Assert.True(hasFlagsAttribute);

        //Output
        _output.WriteLine($"FontStyle has [Flags] attribute: {hasFlagsAttribute}");
    }
}
