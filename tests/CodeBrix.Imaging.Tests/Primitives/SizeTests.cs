using System;
using System.Collections.Generic;
using Xunit;

namespace CodeBrix.Imaging.Tests.Primitives;

public class SizeTests
{
    private readonly ITestOutputHelper _output;

    public SizeTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private void ReportSizes(IList<Size> sizes)
    {
        if (sizes is { Count: > 0 })
        {
            foreach (var size in sizes)
            {
                _output.WriteLine($"Size: {size.Width}x{size.Height}");
            }
        }
    }

    [Fact]
    public void Constructor_with_width_and_height_creates_size()
    {
        //Arrange
        var sizes = new List<Size>();

        //Act
        sizes.Add(new Size(100, 200));
        sizes.Add(new Size(0, 0));
        sizes.Add(new Size(50, 50));

        //Assert
        Assert.Equal(3, sizes.Count);
        Assert.Equal(100, sizes[0].Width);
        Assert.Equal(200, sizes[0].Height);

        //Output
        ReportSizes(sizes);
    }

    [Fact]
    public void Constructor_with_single_value_creates_square_size()
    {
        //Arrange & Act
        var size = new Size(100);

        //Assert
        Assert.Equal(100, size.Width);
        Assert.Equal(100, size.Height);

        //Output
        ReportSizes([size]);
    }

    [Fact]
    public void Constructor_with_point_creates_size()
    {
        //Arrange
        var point = new Point(150, 250);

        //Act
        var size = new Size(point);

        //Assert
        Assert.Equal(150, size.Width);
        Assert.Equal(250, size.Height);

        //Output
        ReportSizes([size]);
    }

    [Fact]
    public void Constructor_with_size_creates_copy()
    {
        //Arrange
        var original = new Size(100, 200);

        //Act
        var copy = new Size(original);

        //Assert
        Assert.Equal(original.Width, copy.Width);
        Assert.Equal(original.Height, copy.Height);

        //Output
        ReportSizes([original, copy]);
    }

    [Fact]
    public void Empty_size_is_correctly_identified()
    {
        //Arrange
        var emptySize = Size.Empty;
        var nonEmptySize = new Size(10, 10);

        //Act & Assert
        Assert.True(emptySize.IsEmpty);
        Assert.False(nonEmptySize.IsEmpty);

        //Output
        _output.WriteLine($"Empty size IsEmpty: {emptySize.IsEmpty}");
        _output.WriteLine($"Non-empty size IsEmpty: {nonEmptySize.IsEmpty}");
    }

    [Fact]
    public void Size_equality_works()
    {
        //Arrange
        var size1 = new Size(100, 200);
        var size2 = new Size(100, 200);
        var size3 = new Size(50, 50);

        //Act & Assert
        Assert.True(size1.Equals(size2));
        Assert.False(size1.Equals(size3));
        Assert.True(size1 == size2);
        Assert.True(size1 != size3);

        //Output
        _output.WriteLine($"size1 == size2: {size1 == size2}");
        _output.WriteLine($"size1 != size3: {size1 != size3}");
    }

    [Fact]
    public void Size_is_mutable()
    {
        //Arrange
        var size = new Size(100, 200);

        //Act
        size.Width = 300;
        size.Height = 400;

        //Assert
        Assert.Equal(300, size.Width);
        Assert.Equal(400, size.Height);

        //Output
        ReportSizes([size]);
    }

    [Fact]
    public void Size_addition_works()
    {
        //Arrange
        var size1 = new Size(100, 200);
        var size2 = new Size(50, 75);

        //Act
        var result = size1 + size2;

        //Assert
        Assert.Equal(150, result.Width);
        Assert.Equal(275, result.Height);

        //Output
        _output.WriteLine($"({size1.Width}x{size1.Height}) + ({size2.Width}x{size2.Height}) = ({result.Width}x{result.Height})");
    }

    [Fact]
    public void Size_subtraction_works()
    {
        //Arrange
        var size1 = new Size(100, 200);
        var size2 = new Size(50, 75);

        //Act
        var result = size1 - size2;

        //Assert
        Assert.Equal(50, result.Width);
        Assert.Equal(125, result.Height);

        //Output
        _output.WriteLine($"({size1.Width}x{size1.Height}) - ({size2.Width}x{size2.Height}) = ({result.Width}x{result.Height})");
    }

    [Fact]
    public void Size_multiplication_by_int_works()
    {
        //Arrange
        var size = new Size(10, 20);

        //Act
        var result1 = size * 3;
        var result2 = 3 * size;

        //Assert
        Assert.Equal(30, result1.Width);
        Assert.Equal(60, result1.Height);
        Assert.Equal(30, result2.Width);
        Assert.Equal(60, result2.Height);

        //Output
        _output.WriteLine($"({size.Width}x{size.Height}) * 3 = ({result1.Width}x{result1.Height})");
    }

    [Fact]
    public void Size_converts_to_SizeF_implicitly()
    {
        //Arrange
        var size = new Size(100, 200);

        //Act
        SizeF sizeF = size;

        //Assert
        Assert.Equal(100f, sizeF.Width);
        Assert.Equal(200f, sizeF.Height);

        //Output
        _output.WriteLine($"Size ({size.Width}x{size.Height}) -> SizeF ({sizeF.Width}x{sizeF.Height})");
    }

    [Fact]
    public void Size_converts_to_Point_explicitly()
    {
        //Arrange
        var size = new Size(100, 200);

        //Act
        var point = (Point)size;

        //Assert
        Assert.Equal(100, point.X);
        Assert.Equal(200, point.Y);

        //Output
        _output.WriteLine($"Size ({size.Width}x{size.Height}) -> Point ({point.X}, {point.Y})");
    }
}
