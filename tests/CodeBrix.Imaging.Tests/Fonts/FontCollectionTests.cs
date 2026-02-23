using CodeBrix.Imaging.Fonts;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace CodeBrix.Imaging.Tests.Fonts;

public class FontCollectionTests
{
    private readonly ITestOutputHelper _output;

    public FontCollectionTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public void FontCollection_can_be_created()
    {
        //Arrange & Act
        var collection = new FontCollection();

        //Assert
        Assert.NotNull(collection);

        //Output
        _output.WriteLine("FontCollection created successfully");
    }

    [Fact]
    public void FontCollection_Families_is_empty_initially()
    {
        //Arrange
        var collection = new FontCollection();

        //Act
        var families = collection.Families.ToList();

        //Assert
        Assert.NotNull(families);
        Assert.Empty(families);

        //Output
        _output.WriteLine($"New FontCollection has {families.Count} families");
    }

    [Fact]
    public void FontCollection_TryGet_returns_false_for_empty_collection()
    {
        //Arrange
        var collection = new FontCollection();

        //Act
        var result = collection.TryGet("Arial", out var family);

        //Assert
        Assert.False(result);

        //Output
        _output.WriteLine($"TryGet on empty collection returned: {result}");
    }

    [Fact]
    public void FontCollection_Get_throws_for_missing_font()
    {
        //Arrange
        var collection = new FontCollection();

        //Act & Assert
        var exception = Assert.Throws<FontFamilyNotFoundException>(() => collection.Get("NonExistentFont"));

        //Output
        _output.WriteLine($"Get for missing font threw: {exception.GetType().Name}");
        _output.WriteLine($"Message: {exception.Message}");
    }

    [Fact]
    public void FontCollection_Add_from_system_font_path_works()
    {
        //Arrange
        var collection = new FontCollection();
        var windowsFontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        
        if (!File.Exists(windowsFontPath))
        {
            _output.WriteLine($"Skipping test - font file not found: {windowsFontPath}");
            return;
        }

        //Act
        var family = collection.Add(windowsFontPath);

        //Assert
        Assert.NotEqual(default, family);
        Assert.False(string.IsNullOrEmpty(family.Name));

        //Output
        _output.WriteLine($"Added font family: {family.Name}");
    }

    [Fact]
    public void FontCollection_Add_returns_description()
    {
        //Arrange
        var collection = new FontCollection();
        var windowsFontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        
        if (!File.Exists(windowsFontPath))
        {
            _output.WriteLine($"Skipping test - font file not found: {windowsFontPath}");
            return;
        }

        //Act
        var family = collection.Add(windowsFontPath, out var description);

        //Assert
        Assert.NotEqual(default, family);
        Assert.NotNull(description);
        Assert.False(string.IsNullOrEmpty(description.FontFamilyInvariantCulture));

        //Output
        _output.WriteLine($"Font family: {family.Name}");
        _output.WriteLine($"Description - Family: {description.FontFamilyInvariantCulture}");
        _output.WriteLine($"Description - Name: {description.FontNameInvariantCulture}");
        _output.WriteLine($"Description - Style: {description.Style}");
    }

    [Fact]
    public void FontCollection_GetByCulture_returns_families()
    {
        //Arrange
        var collection = new FontCollection();
        var windowsFontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        
        if (!File.Exists(windowsFontPath))
        {
            _output.WriteLine($"Skipping test - font file not found: {windowsFontPath}");
            return;
        }
        
        collection.Add(windowsFontPath);

        //Act
        var families = collection.GetByCulture(CultureInfo.InvariantCulture).ToList();

        //Assert
        Assert.NotEmpty(families);

        //Output
        _output.WriteLine($"Families by InvariantCulture: {families.Count}");
        foreach (var family in families)
        {
            _output.WriteLine($"  - {family.Name}");
        }
    }

    [Fact]
    public void FontCollection_Add_with_stream_works()
    {
        //Arrange
        var collection = new FontCollection();
        var windowsFontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        
        if (!File.Exists(windowsFontPath))
        {
            _output.WriteLine($"Skipping test - font file not found: {windowsFontPath}");
            return;
        }

        //Act
        using var stream = File.OpenRead(windowsFontPath);
        var family = collection.Add(stream);

        //Assert
        Assert.NotEqual(default, family);
        Assert.False(string.IsNullOrEmpty(family.Name));

        //Output
        _output.WriteLine($"Added font family from stream: {family.Name}");
    }
}
