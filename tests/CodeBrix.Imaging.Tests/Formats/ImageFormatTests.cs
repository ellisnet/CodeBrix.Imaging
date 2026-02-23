using CodeBrix.Imaging.Formats.Bmp;
using CodeBrix.Imaging.Formats.Gif;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Png;
using CodeBrix.Imaging.Formats.Tga;
using System;
using System.Linq;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats;

public class ImageFormatTests
{
    private readonly ITestOutputHelper _output;

    public ImageFormatTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public void PngFormat_instance_exists()
    {
        //Arrange & Act
        var format = PngFormat.Instance;

        //Assert
        Assert.NotNull(format);
        Assert.Equal("PNG", format.Name);

        //Output
        _output.WriteLine($"PngFormat Name: {format.Name}");
        _output.WriteLine($"PngFormat DefaultMimeType: {format.DefaultMimeType}");
    }

    [Fact]
    public void PngFormat_has_correct_properties()
    {
        //Arrange
        var format = PngFormat.Instance;

        //Act & Assert
        Assert.Equal("image/png", format.DefaultMimeType);
        Assert.NotNull(format.MimeTypes);
        Assert.NotEmpty(format.MimeTypes);
        Assert.NotNull(format.FileExtensions);
        Assert.NotEmpty(format.FileExtensions);

        //Output
        _output.WriteLine($"MimeTypes: {string.Join(", ", format.MimeTypes)}");
        _output.WriteLine($"FileExtensions: {string.Join(", ", format.FileExtensions)}");
    }

    [Fact]
    public void PngFormat_creates_default_metadata()
    {
        //Arrange
        var format = PngFormat.Instance;

        //Act
        var metadata = format.CreateDefaultFormatMetadata();

        //Assert
        Assert.NotNull(metadata);

        //Output
        _output.WriteLine($"Created metadata type: {metadata.GetType().Name}");
    }

    [Fact]
    public void JpegFormat_instance_exists()
    {
        //Arrange & Act
        var format = JpegFormat.Instance;

        //Assert
        Assert.NotNull(format);
        Assert.Equal("JPEG", format.Name);

        //Output
        _output.WriteLine($"JpegFormat Name: {format.Name}");
        _output.WriteLine($"JpegFormat DefaultMimeType: {format.DefaultMimeType}");
    }

    [Fact]
    public void JpegFormat_has_correct_properties()
    {
        //Arrange
        var format = JpegFormat.Instance;

        //Act & Assert
        Assert.Equal("image/jpeg", format.DefaultMimeType);
        Assert.NotNull(format.MimeTypes);
        Assert.NotEmpty(format.MimeTypes);
        Assert.NotNull(format.FileExtensions);
        Assert.NotEmpty(format.FileExtensions);

        //Output
        _output.WriteLine($"MimeTypes: {string.Join(", ", format.MimeTypes)}");
        _output.WriteLine($"FileExtensions: {string.Join(", ", format.FileExtensions)}");
    }

    [Fact]
    public void JpegFormat_creates_default_metadata()
    {
        //Arrange
        var format = JpegFormat.Instance;

        //Act
        var metadata = format.CreateDefaultFormatMetadata();

        //Assert
        Assert.NotNull(metadata);

        //Output
        _output.WriteLine($"Created metadata type: {metadata.GetType().Name}");
    }

    [Fact]
    public void BmpFormat_instance_exists()
    {
        //Arrange & Act
        var format = BmpFormat.Instance;

        //Assert
        Assert.NotNull(format);
        Assert.Equal("BMP", format.Name);

        //Output
        _output.WriteLine($"BmpFormat Name: {format.Name}");
        _output.WriteLine($"BmpFormat DefaultMimeType: {format.DefaultMimeType}");
    }

    [Fact]
    public void BmpFormat_has_correct_properties()
    {
        //Arrange
        var format = BmpFormat.Instance;

        //Act & Assert
        Assert.Equal("image/bmp", format.DefaultMimeType);
        Assert.NotNull(format.MimeTypes);
        Assert.NotEmpty(format.MimeTypes);
        Assert.NotNull(format.FileExtensions);
        Assert.NotEmpty(format.FileExtensions);

        //Output
        _output.WriteLine($"MimeTypes: {string.Join(", ", format.MimeTypes)}");
        _output.WriteLine($"FileExtensions: {string.Join(", ", format.FileExtensions)}");
    }

    [Fact]
    public void GifFormat_instance_exists()
    {
        //Arrange & Act
        var format = GifFormat.Instance;

        //Assert
        Assert.NotNull(format);
        Assert.Equal("GIF", format.Name);

        //Output
        _output.WriteLine($"GifFormat Name: {format.Name}");
        _output.WriteLine($"GifFormat DefaultMimeType: {format.DefaultMimeType}");
    }

    [Fact]
    public void GifFormat_has_correct_properties()
    {
        //Arrange
        var format = GifFormat.Instance;

        //Act & Assert
        Assert.Equal("image/gif", format.DefaultMimeType);
        Assert.NotNull(format.MimeTypes);
        Assert.NotEmpty(format.MimeTypes);
        Assert.NotNull(format.FileExtensions);
        Assert.NotEmpty(format.FileExtensions);

        //Output
        _output.WriteLine($"MimeTypes: {string.Join(", ", format.MimeTypes)}");
        _output.WriteLine($"FileExtensions: {string.Join(", ", format.FileExtensions)}");
    }

    [Fact]
    public void TgaFormat_instance_exists()
    {
        //Arrange & Act
        var format = TgaFormat.Instance;

        //Assert
        Assert.NotNull(format);
        Assert.Equal("TGA", format.Name);

        //Output
        _output.WriteLine($"TgaFormat Name: {format.Name}");
        _output.WriteLine($"TgaFormat DefaultMimeType: {format.DefaultMimeType}");
    }

    [Fact]
    public void TgaFormat_has_correct_properties()
    {
        //Arrange
        var format = TgaFormat.Instance;

        //Act & Assert
        Assert.Equal("image/x-tga", format.DefaultMimeType);
        Assert.NotNull(format.MimeTypes);
        Assert.NotEmpty(format.MimeTypes);
        Assert.NotNull(format.FileExtensions);
        Assert.NotEmpty(format.FileExtensions);

        //Output
        _output.WriteLine($"MimeTypes: {string.Join(", ", format.MimeTypes)}");
        _output.WriteLine($"FileExtensions: {string.Join(", ", format.FileExtensions)}");
    }

    [Fact]
    public void All_formats_are_registered_in_default_configuration()
    {
        //Arrange
        var config = Configuration.Default;

        //Act
        var formats = config.ImageFormats.ToList();

        //Assert
        Assert.True(formats.Any(f => f.Name == "PNG"), "PNG format should be registered");
        Assert.True(formats.Any(f => f.Name == "JPEG"), "JPEG format should be registered");
        Assert.True(formats.Any(f => f.Name == "BMP"), "BMP format should be registered");
        Assert.True(formats.Any(f => f.Name == "GIF"), "GIF format should be registered");
        Assert.True(formats.Any(f => f.Name == "TGA"), "TGA format should be registered");

        //Output
        _output.WriteLine($"Registered formats ({formats.Count}):");
        foreach (var format in formats)
        {
            _output.WriteLine($"  - {format.Name} ({format.DefaultMimeType})");
        }
    }
}
