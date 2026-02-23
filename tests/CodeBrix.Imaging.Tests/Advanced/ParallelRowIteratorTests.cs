using CodeBrix.Imaging.Advanced;
using System;
using Xunit;

namespace CodeBrix.Imaging.Tests.Advanced;

public class ParallelRowIteratorTests
{
    private readonly ITestOutputHelper _output;

    public ParallelRowIteratorTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public void IterateRows_with_configuration_invokes_operation()
    {
        //Arrange
        var rectangle = new Rectangle(0, 0, 10, 10);
        var configuration = Configuration.Default;
        var operation = new CountingRowOperation();

        //Act
        ParallelRowIterator.IterateRows(configuration, rectangle, in operation);

        //Assert
        Assert.True(operation.InvocationCount >= 10);
        _output.WriteLine($"Row operation invoked {operation.InvocationCount} times");
    }

    [Fact]
    public void IterateRows_with_settings_invokes_operation()
    {
        //Arrange
        var rectangle = new Rectangle(0, 0, 20, 15);
        var settings = ParallelExecutionSettings.FromConfiguration(Configuration.Default);
        var operation = new CountingRowOperation();

        //Act
        ParallelRowIterator.IterateRows(rectangle, in settings, in operation);

        //Assert
        Assert.True(operation.InvocationCount >= 15);
        _output.WriteLine($"Row operation invoked {operation.InvocationCount} times for height of 15");
    }

    [Fact]
    public void IterateRows_with_single_row_rectangle_works()
    {
        //Arrange
        var rectangle = new Rectangle(0, 5, 100, 1);
        var configuration = Configuration.Default;
        var operation = new CountingRowOperation();

        //Act
        ParallelRowIterator.IterateRows(configuration, rectangle, in operation);

        //Assert
        Assert.True(operation.InvocationCount >= 1);
        _output.WriteLine($"Single row rectangle processed: {operation.InvocationCount} invocation");
    }

    [Fact]
    public void IterateRowIntervals_with_configuration_invokes_operation()
    {
        //Arrange
        var rectangle = new Rectangle(0, 0, 10, 100);
        var configuration = Configuration.Default;
        var operation = new CountingRowIntervalOperation();

        //Act
        ParallelRowIterator.IterateRowIntervals(configuration, rectangle, in operation);

        //Assert
        Assert.True(operation.InvocationCount > 0);
        Assert.True(operation.TotalRowsProcessed >= 100);
        _output.WriteLine($"Row interval operation invoked {operation.InvocationCount} times");
        _output.WriteLine($"Total rows processed: {operation.TotalRowsProcessed}");
    }

    [Fact]
    public void IterateRowIntervals_with_settings_invokes_operation()
    {
        //Arrange
        var rectangle = new Rectangle(0, 0, 50, 50);
        var settings = ParallelExecutionSettings.FromConfiguration(Configuration.Default);
        var operation = new CountingRowIntervalOperation();

        //Act
        ParallelRowIterator.IterateRowIntervals(rectangle, in settings, in operation);

        //Assert
        Assert.True(operation.InvocationCount > 0);
        Assert.True(operation.TotalRowsProcessed >= 50);
        _output.WriteLine($"Row interval invocations: {operation.InvocationCount}");
        _output.WriteLine($"Total rows covered: {operation.TotalRowsProcessed}");
    }

    private struct CountingRowOperation : IRowOperation
    {
        public int InvocationCount;

        public void Invoke(int y)
        {
            InvocationCount++;
        }
    }

    private struct CountingRowIntervalOperation : IRowIntervalOperation
    {
        public int InvocationCount;
        public int TotalRowsProcessed;

        public void Invoke(in Memory.RowInterval rows)
        {
            InvocationCount++;
            TotalRowsProcessed += rows.Max - rows.Min;
        }
    }
}
