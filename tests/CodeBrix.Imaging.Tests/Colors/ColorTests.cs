using System;
using System.Collections.Generic;
using Xunit;

// ReSharper disable UnusedVariable
// ReSharper disable UseObjectOrCollectionInitializer

namespace CodeBrix.Imaging.Tests.Colors;

public class ColorTests
{
    private readonly ITestOutputHelper _output;

    public ColorTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private void ReportColors(IList<Color> colors, ITestOutputHelper output)
    {
        if (colors is { Count: > 0 } && output != null)
        {
            foreach (var color in colors)
            {
                var hex = color.ToHex();
                output.WriteLine($"Color with ARGB ({color.A},{color.R},{color.G},{color.B}) is hex: #{hex}");
            }
        }
    }

    [Fact]
    public void FromRgba_returns_color()
    {
        //Arrange
        var colors = new List<Color>();

        //Act
        colors.Add(Color.FromRgba(255, 0, 0, 255));
        colors.Add(Color.FromRgba(0, 255, 0, 128));
        colors.Add(Color.FromRgba(0, 0, 255, 64));

        //Assert
        Assert.NotNull(colors);
        Assert.NotEmpty(colors);
        Assert.Equal(3, colors.Count);

        //Output
        ReportColors(colors, _output);
    }

    [Fact]
    public void FromRgb_returns_color()
    {
        //Arrange
        var colors = new List<Color>();

        //Act
        colors.Add(Color.FromRgb(255, 0, 0));
        colors.Add(Color.FromRgb(0, 255, 0));
        colors.Add(Color.FromRgb(0, 0, 255));

        //Assert
        Assert.NotNull(colors);
        Assert.NotEmpty(colors);
        Assert.Equal(3, colors.Count);

        //Output
        ReportColors(colors, _output);
    }

    [Fact]
    public void ParseHex_returns_color()
    {
        //Arrange
        var colors = new List<Color>();

        //Act
        colors.Add(Color.ParseHex("#FF0000"));
        colors.Add(Color.ParseHex("#00FF00FF"));
        colors.Add(Color.ParseHex("0000FF"));
        colors.Add(Color.ParseHex("FFF"));

        //Assert
        Assert.NotNull(colors);
        Assert.NotEmpty(colors);
        Assert.Equal(4, colors.Count);

        //Output
        ReportColors(colors, _output);
    }

    [Fact]
    public void TryParseHex_returns_color()
    {
        //Arrange
        var colors = new List<Color>();

        //Act
        var result1 = Color.TryParseHex("#FF5733", out var color1);
        var result2 = Color.TryParseHex("invalid", out var color2);
        var result3 = Color.TryParseHex("#AABBCCDD", out var color3);

        if (result1) colors.Add(color1);
        if (result3) colors.Add(color3);

        //Assert
        Assert.True(result1);
        Assert.False(result2);
        Assert.True(result3);
        Assert.Equal(2, colors.Count);

        //Output
        ReportColors(colors, _output);
    }

    [Fact]
    public void Parse_returns_color_from_name()
    {
        //Arrange
        var colors = new List<Color>();

        //Act
        colors.Add(Color.Parse("Red"));
        colors.Add(Color.Parse("Green"));
        colors.Add(Color.Parse("Blue"));
        colors.Add(Color.Parse("#FF00FF"));

        //Assert
        Assert.NotNull(colors);
        Assert.NotEmpty(colors);
        Assert.Equal(4, colors.Count);

        //Output
        ReportColors(colors, _output);
    }

    [Fact]
    public void TryParse_returns_color()
    {
        //Arrange
        var colors = new List<Color>();

        //Act
        var result1 = Color.TryParse("Coral", out var color1);
        var result2 = Color.TryParse("InvalidColorName", out var color2);
        var result3 = Color.TryParse("#123456", out var color3);

        if (result1) colors.Add(color1);
        if (result3) colors.Add(color3);

        //Assert
        Assert.True(result1);
        Assert.False(result2);
        Assert.True(result3);
        Assert.Equal(2, colors.Count);

        //Output
        ReportColors(colors, _output);
    }

    [Fact]
    public void NamedColors_returns_colors()
    {
        //Arrange
        var colors = new List<Color>();

        //Act
        colors.Add(Color.AliceBlue);
        colors.Add(Color.Crimson);
        colors.Add(Color.DarkSlateGray);
        colors.Add(Color.Transparent);

        //Assert
        Assert.NotNull(colors);
        Assert.NotEmpty(colors);
        Assert.Equal(4, colors.Count);

        //Output
        ReportColors(colors, _output);
    }

    [Fact]
    public void WithAlpha_returns_modified_color()
    {
        //Arrange
        var colors = new List<Color>();
        var originalColor = Color.Red;

        //Act
        colors.Add(originalColor);
        colors.Add(originalColor.WithAlpha(0.5f));
        colors.Add(originalColor.WithAlpha(0.25f));
        colors.Add(originalColor.WithAlpha(0.0f));

        //Assert
        Assert.NotNull(colors);
        Assert.NotEmpty(colors);
        Assert.Equal(4, colors.Count);

        //Output
        ReportColors(colors, _output);
    }

    [Fact]
    public void ToHex_returns_hex_string()
    {
        //Arrange
        var color = Color.FromRgba(255, 128, 64, 255);

        //Act
        var hex = color.ToHex();

        //Assert
        Assert.NotNull(hex);
        Assert.NotEmpty(hex);
        _output.WriteLine($"Color hex: #{hex}");
    }

    [Fact]
    public void Color_equality_works()
    {
        //Arrange
        var color1 = Color.FromRgba(100, 150, 200, 255);
        var color2 = Color.FromRgba(100, 150, 200, 255);
        var color3 = Color.FromRgba(200, 150, 100, 255);

        //Act & Assert
        Assert.True(color1 == color2);
        Assert.False(color1 == color3);
        Assert.True(color1 != color3);
        Assert.True(color1.Equals(color2));
        Assert.False(color1.Equals(color3));
    }
}
