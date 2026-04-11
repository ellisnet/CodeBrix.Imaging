using CodeBrix.Imaging.PixelFormats;
using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace CodeBrix.Imaging.Tests.PixelFormats;

public class Rgba32Tests
{
    private readonly ITestOutputHelper _output;

    public Rgba32Tests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private void ReportPixels(IList<Rgba32> pixels)
    {
        if (pixels is { Count: > 0 })
        {
            foreach (var pixel in pixels)
            {
                _output.WriteLine($"Rgba32: R={pixel.R}, G={pixel.G}, B={pixel.B}, A={pixel.A} (Packed: 0x{pixel.PackedValue:X8})");
            }
        }
    }

    [Fact]
    public void Constructor_with_rgb_creates_opaque_pixel()
    {
        //Arrange & Act
        var pixel = new Rgba32(255, 128, 64);

        //Assert
        Assert.Equal(255, pixel.R);
        Assert.Equal(128, pixel.G);
        Assert.Equal(64, pixel.B);
        Assert.Equal(255, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void Constructor_with_rgba_creates_pixel()
    {
        //Arrange
        var pixels = new List<Rgba32>();

        //Act
        pixels.Add(new Rgba32(255, 0, 0, 255));
        pixels.Add(new Rgba32(0, 255, 0, 128));
        pixels.Add(new Rgba32(0, 0, 255, 64));
        pixels.Add(new Rgba32(128, 128, 128, 0));

        //Assert
        Assert.Equal(4, pixels.Count);
        Assert.Equal(255, pixels[0].R);
        Assert.Equal(255, pixels[1].G);
        Assert.Equal(255, pixels[2].B);
        Assert.Equal(0, pixels[3].A);

        //Output
        ReportPixels(pixels);
    }

    [Fact]
    public void Constructor_with_floats_creates_pixel()
    {
        //Arrange & Act
        var pixel = new Rgba32(1.0f, 0.5f, 0.25f, 0.75f);

        //Assert
        Assert.Equal(255, pixel.R);
        Assert.InRange(pixel.G, 127, 128);
        Assert.InRange(pixel.B, 63, 64);
        Assert.InRange(pixel.A, 191, 192);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void Constructor_with_packed_value_creates_pixel()
    {
        //Arrange
        var packed = 0xFF8040FF; // RGBA order

        //Act
        var pixel = new Rgba32(packed);

        //Assert
        Assert.Equal(packed, pixel.PackedValue);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void PackedValue_property_works()
    {
        //Arrange
        var pixel = new Rgba32(255, 128, 64, 192);

        //Act
        var packed = pixel.PackedValue;

        //Assert
        Assert.NotEqual(0u, packed);

        //Output
        _output.WriteLine($"Packed value: 0x{packed:X8}");
    }

    [Fact]
    public void Rgba_property_matches_PackedValue()
    {
        //Arrange
        var pixel = new Rgba32(255, 128, 64, 192);

        //Act & Assert
        Assert.Equal(pixel.PackedValue, pixel.Rgba);

        //Output
        _output.WriteLine($"Rgba: 0x{pixel.Rgba:X8}, PackedValue: 0x{pixel.PackedValue:X8}");
    }

    [Fact]
    public void Pixel_fields_are_mutable()
    {
        //Arrange
        var pixel = new Rgba32(0, 0, 0, 0);

        //Act
        pixel.R = 255;
        pixel.G = 128;
        pixel.B = 64;
        pixel.A = 192;

        //Assert
        Assert.Equal(255, pixel.R);
        Assert.Equal(128, pixel.G);
        Assert.Equal(64, pixel.B);
        Assert.Equal(192, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void Pixel_equality_works()
    {
        //Arrange
        var pixel1 = new Rgba32(255, 128, 64, 192);
        var pixel2 = new Rgba32(255, 128, 64, 192);
        var pixel3 = new Rgba32(0, 0, 0, 255);

        //Act & Assert
        Assert.True(pixel1.Equals(pixel2));
        Assert.False(pixel1.Equals(pixel3));
        Assert.True(pixel1 == pixel2);
        Assert.True(pixel1 != pixel3);

        //Output
        _output.WriteLine($"pixel1 == pixel2: {pixel1 == pixel2}");
        _output.WriteLine($"pixel1 != pixel3: {pixel1 != pixel3}");
    }

    [Fact]
    public void Rgb_property_returns_Rgb24()
    {
        //Arrange
        var pixel = new Rgba32(255, 128, 64, 192);

        //Act
        var rgb = pixel.Rgb;

        //Assert
        Assert.Equal(255, rgb.R);
        Assert.Equal(128, rgb.G);
        Assert.Equal(64, rgb.B);

        //Output
        _output.WriteLine($"Rgb24: R={rgb.R}, G={rgb.G}, B={rgb.B}");
    }

    [Fact]
    public void Bgr_property_returns_Bgr24()
    {
        //Arrange
        var pixel = new Rgba32(255, 128, 64, 192);

        //Act
        var bgr = pixel.Bgr;

        //Assert
        Assert.Equal(255, bgr.R);
        Assert.Equal(128, bgr.G);
        Assert.Equal(64, bgr.B);

        //Output
        _output.WriteLine($"Bgr24: R={bgr.R}, G={bgr.G}, B={bgr.B}");
    }

    [Fact]
    public void Constructor_with_Vector3_creates_opaque_pixel()
    {
        //Arrange
        var vector = new Vector3(1.0f, 0.5f, 0.25f);

        //Act
        var pixel = new Rgba32(vector);

        //Assert
        Assert.Equal(255, pixel.R);
        Assert.InRange(pixel.G, 127, 128);
        Assert.InRange(pixel.B, 63, 64);
        Assert.Equal(255, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void Constructor_with_Vector4_creates_pixel()
    {
        //Arrange
        var vector = new Vector4(1.0f, 0.5f, 0.25f, 0.75f);

        //Act
        var pixel = new Rgba32(vector);

        //Assert
        Assert.Equal(255, pixel.R);
        Assert.InRange(pixel.G, 127, 128);
        Assert.InRange(pixel.B, 63, 64);
        Assert.InRange(pixel.A, 191, 192);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void Common_colors_have_expected_values()
    {
        //Arrange
        var colors = new Dictionary<string, Rgba32>
        {
            ["Red"] = new Rgba32(255, 0, 0, 255),
            ["Green"] = new Rgba32(0, 255, 0, 255),
            ["Blue"] = new Rgba32(0, 0, 255, 255),
            ["White"] = new Rgba32(255, 255, 255, 255),
            ["Black"] = new Rgba32(0, 0, 0, 255),
            ["Transparent"] = new Rgba32(0, 0, 0, 0)
        };

        //Act & Assert
        Assert.Equal(255, colors["Red"].R);
        Assert.Equal(0, colors["Red"].G);
        Assert.Equal(255, colors["Green"].G);
        Assert.Equal(255, colors["Blue"].B);
        Assert.Equal(255, colors["White"].R);
        Assert.Equal(0, colors["Black"].R);
        Assert.Equal(0, colors["Transparent"].A);

        //Output
        foreach (var kvp in colors)
        {
            _output.WriteLine($"{kvp.Key}: R={kvp.Value.R}, G={kvp.Value.G}, B={kvp.Value.B}, A={kvp.Value.A}");
        }
    }
}
