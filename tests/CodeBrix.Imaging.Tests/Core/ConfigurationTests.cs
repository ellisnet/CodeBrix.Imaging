using System;
using System.Linq;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Png;
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
        // Use a value that is guaranteed to differ from the default (Environment.ProcessorCount)
        newConfig.MaxDegreeOfParallelism = Environment.ProcessorCount + 1;

        //Assert
        Assert.NotEqual(defaultConfig.MaxDegreeOfParallelism, newConfig.MaxDegreeOfParallelism);

        //Output
        _output.WriteLine($"Default parallelism: {defaultConfig.MaxDegreeOfParallelism}");
        _output.WriteLine($"New config parallelism: {newConfig.MaxDegreeOfParallelism}");
    }

    [Fact]
    public void Clone_of_default_does_not_change_default_when_a_decoder_is_set()
    {
        //Arrange
        var defaultDecoder = Configuration.Default.ImageFormatsManager.FindDecoder(PngFormat.Instance);
        var clone = Configuration.Default.Clone();

        //Act
        clone.ImageFormatsManager.SetDecoder(PngFormat.Instance, new JpegDecoder());

        //Assert
        Assert.NotSame(Configuration.Default.ImageFormatsManager, clone.ImageFormatsManager);
        Assert.IsType<JpegDecoder>(clone.ImageFormatsManager.FindDecoder(PngFormat.Instance));
        Assert.Same(defaultDecoder, Configuration.Default.ImageFormatsManager.FindDecoder(PngFormat.Instance));

        //Output
        _output.WriteLine($"Default PNG decoder: {Configuration.Default.ImageFormatsManager.FindDecoder(PngFormat.Instance).GetType().Name}");
    }

    [Fact]
    public void Clone_copies_formats_encoders_decoders_and_detectors_in_order()
    {
        //Arrange
        var source = Configuration.Default;

        //Act
        var clone = source.Clone();

        //Assert
        Assert.Equal(source.ImageFormats.ToHashSet(), clone.ImageFormats.ToHashSet());
        Assert.Equal(source.ImageFormatsManager.FormatDetectors, clone.ImageFormatsManager.FormatDetectors);
        Assert.Equal(source.ImageFormatsManager.ImageDecoders.OrderBy(p => p.Key.Name), clone.ImageFormatsManager.ImageDecoders.OrderBy(p => p.Key.Name));
        Assert.Equal(source.ImageFormatsManager.ImageEncoders.OrderBy(p => p.Key.Name), clone.ImageFormatsManager.ImageEncoders.OrderBy(p => p.Key.Name));
        Assert.Equal(source.MaxHeaderSize, clone.MaxHeaderSize);

        //Output
        _output.WriteLine($"Formats copied: {clone.ImageFormats.Count()}, detectors: {clone.ImageFormatsManager.FormatDetectors.Count()}");
    }

    [Fact]
    public void Clone_registrations_on_the_source_do_not_reach_the_clone()
    {
        //Arrange
        var source = new Configuration(new PngConfigurationModule());
        var clone = source.Clone();

        //Act
        source.Configure(new JpegConfigurationModule());

        //Assert
        Assert.Contains(JpegFormat.Instance, source.ImageFormats);
        Assert.DoesNotContain(JpegFormat.Instance, clone.ImageFormats);
        Assert.Null(clone.ImageFormatsManager.FindDecoder(JpegFormat.Instance));

        //Output
        _output.WriteLine($"Source formats: {source.ImageFormats.Count()}, clone formats: {clone.ImageFormats.Count()}");
    }

    [Fact]
    public void Clone_copies_properties_and_contiguous_buffer_preference_into_its_own_dictionary()
    {
        //Arrange
        var source = new Configuration { PreferContiguousImageBuffers = true };
        var value = new object();
        source.Properties["key"] = value;

        //Act
        var clone = source.Clone();
        clone.Properties["other"] = 1;

        //Assert
        Assert.True(clone.PreferContiguousImageBuffers);
        Assert.Same(value, clone.Properties["key"]);
        Assert.NotSame(source.Properties, clone.Properties);
        Assert.False(source.Properties.ContainsKey("other"));

        //Output
        _output.WriteLine($"Clone properties: {clone.Properties.Count}");
    }

    [Fact]
    public void Clone_copies_settings_and_shares_the_memory_allocator()
    {
        //Arrange
        var source = new Configuration
        {
            MaxDegreeOfParallelism = 3,
            StreamProcessingBufferSize = 4096,
            ReadOrigin = ReadOrigin.Begin,
        };

        //Act
        var clone = source.Clone();

        //Assert
        Assert.Equal(3, clone.MaxDegreeOfParallelism);
        Assert.Equal(4096, clone.StreamProcessingBufferSize);
        Assert.Equal(ReadOrigin.Begin, clone.ReadOrigin);
        Assert.Same(source.MemoryAllocator, clone.MemoryAllocator);

        //Output
        _output.WriteLine($"Clone parallelism: {clone.MaxDegreeOfParallelism}");
    }

    [Fact]
    public void Shallow_clone_shares_the_image_formats_manager()
    {
        //Arrange
        var source = new Configuration(new PngConfigurationModule()) { PreferContiguousImageBuffers = true };
        source.Properties["key"] = 1;

        //Act
        var clone = source.Clone(doShallowClone: true);
        source.Configure(new JpegConfigurationModule());

        //Assert
        Assert.Same(source.ImageFormatsManager, clone.ImageFormatsManager);
        Assert.Contains(JpegFormat.Instance, clone.ImageFormats);
        Assert.False(clone.PreferContiguousImageBuffers);
        Assert.Empty(clone.Properties);

        //Output
        _output.WriteLine($"Shallow clone formats: {clone.ImageFormats.Count()}");
    }

    [Fact]
    public void CreateSandboxed_does_not_share_the_formats_manager()
    {
        //Arrange & Act
        var sandboxed = Configuration.Default.CreateSandboxed(64);

        //Assert
        Assert.NotSame(Configuration.Default.ImageFormatsManager, sandboxed.ImageFormatsManager);
        Assert.Equal(Configuration.Default.ImageFormats.ToHashSet(), sandboxed.ImageFormats.ToHashSet());

        //Output
        _output.WriteLine($"Sandboxed formats: {sandboxed.ImageFormats.Count()}");
    }
}
