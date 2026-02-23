using CodeBrix.Imaging.Advanced;
using System;
using Xunit;

namespace CodeBrix.Imaging.Tests.Advanced;

public class ParallelExecutionSettingsTests
{
    private readonly ITestOutputHelper _output;

    public ParallelExecutionSettingsTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public void Constructor_with_all_parameters_creates_settings()
    {
        //Arrange
        var maxDegreeOfParallelism = 4;
        var minimumPixelsProcessedPerTask = 2048;
        var memoryAllocator = Configuration.Default.MemoryAllocator;

        //Act
        var settings = new ParallelExecutionSettings(
            maxDegreeOfParallelism,
            minimumPixelsProcessedPerTask,
            memoryAllocator);

        //Assert
        Assert.Equal(maxDegreeOfParallelism, settings.MaxDegreeOfParallelism);
        Assert.Equal(minimumPixelsProcessedPerTask, settings.MinimumPixelsProcessedPerTask);
        Assert.Same(memoryAllocator, settings.MemoryAllocator);

        _output.WriteLine($"MaxDegreeOfParallelism: {settings.MaxDegreeOfParallelism}");
        _output.WriteLine($"MinimumPixelsProcessedPerTask: {settings.MinimumPixelsProcessedPerTask}");
    }

    [Fact]
    public void Constructor_with_two_parameters_uses_default_minimum_pixels()
    {
        //Arrange
        var maxDegreeOfParallelism = 8;
        var memoryAllocator = Configuration.Default.MemoryAllocator;

        //Act
        var settings = new ParallelExecutionSettings(maxDegreeOfParallelism, memoryAllocator);

        //Assert
        Assert.Equal(maxDegreeOfParallelism, settings.MaxDegreeOfParallelism);
        Assert.Equal(ParallelExecutionSettings.DefaultMinimumPixelsProcessedPerTask, settings.MinimumPixelsProcessedPerTask);
        Assert.Same(memoryAllocator, settings.MemoryAllocator);

        _output.WriteLine($"Default MinimumPixelsProcessedPerTask: {ParallelExecutionSettings.DefaultMinimumPixelsProcessedPerTask}");
    }

    [Fact]
    public void Constructor_with_negative_one_parallelism_is_valid()
    {
        //Arrange
        var maxDegreeOfParallelism = -1; // Unlimited parallelism
        var memoryAllocator = Configuration.Default.MemoryAllocator;

        //Act
        var settings = new ParallelExecutionSettings(maxDegreeOfParallelism, memoryAllocator);

        //Assert
        Assert.Equal(-1, settings.MaxDegreeOfParallelism);
        _output.WriteLine($"MaxDegreeOfParallelism (-1 = unlimited): {settings.MaxDegreeOfParallelism}");
    }

    [Fact]
    public void Constructor_with_zero_parallelism_throws()
    {
        //Arrange
        var maxDegreeOfParallelism = 0;
        var memoryAllocator = Configuration.Default.MemoryAllocator;

        //Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ParallelExecutionSettings(maxDegreeOfParallelism, memoryAllocator));

        Assert.NotNull(exception);
        _output.WriteLine("Constructor correctly threw ArgumentOutOfRangeException for parallelism of 0");
    }

    [Fact]
    public void Constructor_with_invalid_negative_parallelism_throws()
    {
        //Arrange
        var maxDegreeOfParallelism = -2;
        var memoryAllocator = Configuration.Default.MemoryAllocator;

        //Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ParallelExecutionSettings(maxDegreeOfParallelism, memoryAllocator));

        Assert.NotNull(exception);
        _output.WriteLine("Constructor correctly threw ArgumentOutOfRangeException for parallelism of -2");
    }

    [Fact]
    public void MultiplyMinimumPixelsPerTask_returns_multiplied_settings()
    {
        //Arrange
        var originalSettings = new ParallelExecutionSettings(4, 1000, Configuration.Default.MemoryAllocator);
        var multiplier = 5;

        //Act
        var newSettings = originalSettings.MultiplyMinimumPixelsPerTask(multiplier);

        //Assert
        Assert.Equal(originalSettings.MaxDegreeOfParallelism, newSettings.MaxDegreeOfParallelism);
        Assert.Equal(originalSettings.MinimumPixelsProcessedPerTask * multiplier, newSettings.MinimumPixelsProcessedPerTask);
        Assert.Same(originalSettings.MemoryAllocator, newSettings.MemoryAllocator);

        _output.WriteLine($"Original MinimumPixels: {originalSettings.MinimumPixelsProcessedPerTask}");
        _output.WriteLine($"Multiplied MinimumPixels: {newSettings.MinimumPixelsProcessedPerTask}");
    }

    [Fact]
    public void FromConfiguration_returns_settings_from_configuration()
    {
        //Arrange
        var configuration = Configuration.Default;

        //Act
        var settings = ParallelExecutionSettings.FromConfiguration(configuration);

        //Assert
        Assert.Equal(configuration.MaxDegreeOfParallelism, settings.MaxDegreeOfParallelism);
        Assert.Equal(ParallelExecutionSettings.DefaultMinimumPixelsProcessedPerTask, settings.MinimumPixelsProcessedPerTask);
        Assert.Same(configuration.MemoryAllocator, settings.MemoryAllocator);

        _output.WriteLine($"Settings from Configuration - MaxDegreeOfParallelism: {settings.MaxDegreeOfParallelism}");
    }

    [Fact]
    public void DefaultMinimumPixelsProcessedPerTask_has_expected_value()
    {
        //Arrange & Act
        var defaultValue = ParallelExecutionSettings.DefaultMinimumPixelsProcessedPerTask;

        //Assert
        Assert.Equal(4096, defaultValue);
        _output.WriteLine($"DefaultMinimumPixelsProcessedPerTask: {defaultValue}");
    }
}
