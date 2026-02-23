using System;
using System.Linq;
using Xunit;

namespace CodeBrix.Imaging.Tests.Core;

public class ConfigurationTests
{
    private readonly ITestOutputHelper _output;

    public ConfigurationTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public void Default_configuration_exists()
    {
        //Arrange & Act
        var config = Configuration.Default;

        //Assert
        Assert.NotNull(config);

        //Output
        _output.WriteLine($"Default configuration exists: {config != null}");
    }

    [Fact]
    public void Default_configuration_has_memory_allocator()
    {
        //Arrange
        var config = Configuration.Default;

        //Act
        var allocator = config.MemoryAllocator;

        //Assert
        Assert.NotNull(allocator);

        //Output
        _output.WriteLine($"Memory allocator type: {allocator.GetType().Name}");
    }

    [Fact]
    public void Default_configuration_has_image_formats()
    {
        //Arrange
        var config = Configuration.Default;

        //Act
        var formats = config.ImageFormats.ToList();

        //Assert
        Assert.NotNull(formats);
        Assert.NotEmpty(formats);

        //Output
        _output.WriteLine($"Available formats count: {formats.Count}");
        foreach (var format in formats)
        {
            _output.WriteLine($"  - {format.Name}");
        }
    }

    [Fact]
    public void MaxDegreeOfParallelism_defaults_to_processor_count()
    {
        //Arrange
        var config = new Configuration();

        //Act
        var parallelism = config.MaxDegreeOfParallelism;

        //Assert
        Assert.Equal(Environment.ProcessorCount, parallelism);

        //Output
        _output.WriteLine($"MaxDegreeOfParallelism: {parallelism} (ProcessorCount: {Environment.ProcessorCount})");
    }

    [Fact]
    public void MaxDegreeOfParallelism_can_be_set()
    {
        //Arrange
        var config = new Configuration();

        //Act
        config.MaxDegreeOfParallelism = 4;

        //Assert
        Assert.Equal(4, config.MaxDegreeOfParallelism);

        //Output
        _output.WriteLine($"MaxDegreeOfParallelism set to: {config.MaxDegreeOfParallelism}");
    }

    [Fact]
    public void MaxDegreeOfParallelism_allows_negative_one()
    {
        //Arrange
        var config = new Configuration();

        //Act
        config.MaxDegreeOfParallelism = -1;

        //Assert
        Assert.Equal(-1, config.MaxDegreeOfParallelism);

        //Output
        _output.WriteLine($"MaxDegreeOfParallelism set to -1 (unlimited): {config.MaxDegreeOfParallelism}");
    }

    [Fact]
    public void MaxDegreeOfParallelism_throws_for_zero()
    {
        //Arrange
        var config = new Configuration();

        //Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => config.MaxDegreeOfParallelism = 0);

        //Output
        _output.WriteLine($"Setting MaxDegreeOfParallelism to 0 throws: {exception.GetType().Name}");
    }

    [Fact]
    public void MaxDegreeOfParallelism_throws_for_less_than_negative_one()
    {
        //Arrange
        var config = new Configuration();

        //Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => config.MaxDegreeOfParallelism = -2);

        //Output
        _output.WriteLine($"Setting MaxDegreeOfParallelism to -2 throws: {exception.GetType().Name}");
    }

    [Fact]
    public void Properties_dictionary_is_available()
    {
        //Arrange
        var config = new Configuration();

        //Act
        config.Properties["TestKey"] = "TestValue";
        var retrieved = config.Properties["TestKey"];

        //Assert
        Assert.NotNull(config.Properties);
        Assert.Equal("TestValue", retrieved);

        //Output
        _output.WriteLine($"Properties dictionary works: TestKey = {retrieved}");
    }

    [Fact]
    public void ImageFormatsManager_is_available()
    {
        //Arrange
        var config = Configuration.Default;

        //Act
        var manager = config.ImageFormatsManager;

        //Assert
        Assert.NotNull(manager);

        //Output
        _output.WriteLine($"ImageFormatsManager type: {manager.GetType().Name}");
    }

    [Fact]
    public void New_configuration_is_independent_of_default()
    {
        //Arrange
        var defaultConfig = Configuration.Default;
        var newConfig = new Configuration();

        //Act
        newConfig.MaxDegreeOfParallelism = 2;

        //Assert
        Assert.NotEqual(defaultConfig.MaxDegreeOfParallelism, newConfig.MaxDegreeOfParallelism);

        //Output
        _output.WriteLine($"Default parallelism: {defaultConfig.MaxDegreeOfParallelism}");
        _output.WriteLine($"New config parallelism: {newConfig.MaxDegreeOfParallelism}");
    }
}
