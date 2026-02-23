using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace CodeBrix.Imaging.Tests.Primitives;

public class PointTests
{
    private readonly ITestOutputHelper _output;

    public PointTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private void ReportPoints(IList<Point> points)
    {
        if (points is { Count: > 0 })
        {
            foreach (var point in points)
            {
                _output.WriteLine($"Point: ({point.X}, {point.Y})");
            }
        }
    }

    [Fact]
    public void Constructor_with_x_and_y_creates_point()
    {
        //Arrange
        var points = new List<Point>();

        //Act
        points.Add(new Point(10, 20));
        points.Add(new Point(0, 0));
        points.Add(new Point(-5, -10));

        //Assert
        Assert.Equal(3, points.Count);
        Assert.Equal(10, points[0].X);
        Assert.Equal(20, points[0].Y);

        //Output
        ReportPoints(points);
    }

    [Fact]
    public void Constructor_with_size_creates_point()
    {
        //Arrange
        var size = new Size(100, 200);

        //Act
        var point = new Point(size);

        //Assert
        Assert.Equal(100, point.X);
        Assert.Equal(200, point.Y);

        //Output
        ReportPoints([point]);
    }

    [Fact]
    public void Empty_point_is_correctly_identified()
    {
        //Arrange
        var emptyPoint = Point.Empty;
        var nonEmptyPoint = new Point(10, 10);

        //Act & Assert
        Assert.True(emptyPoint.IsEmpty);
        Assert.False(nonEmptyPoint.IsEmpty);

        //Output
        _output.WriteLine($"Empty point IsEmpty: {emptyPoint.IsEmpty}");
        _output.WriteLine($"Non-empty point IsEmpty: {nonEmptyPoint.IsEmpty}");
    }

    [Fact]
    public void Point_equality_works()
    {
        //Arrange
        var point1 = new Point(10, 20);
        var point2 = new Point(10, 20);
        var point3 = new Point(5, 5);

        //Act & Assert
        Assert.True(point1.Equals(point2));
        Assert.False(point1.Equals(point3));
        Assert.True(point1 == point2);
        Assert.True(point1 != point3);

        //Output
        _output.WriteLine($"point1 == point2: {point1 == point2}");
        _output.WriteLine($"point1 != point3: {point1 != point3}");
    }

    [Fact]
    public void Point_is_mutable()
    {
        //Arrange
        var point = new Point(10, 20);

        //Act
        point.X = 50;
        point.Y = 60;

        //Assert
        Assert.Equal(50, point.X);
        Assert.Equal(60, point.Y);

        //Output
        ReportPoints([point]);
    }

    [Fact]
    public void Point_addition_with_size_works()
    {
        //Arrange
        var point = new Point(10, 20);
        var size = new Size(5, 10);

        //Act
        var result = point + size;

        //Assert
        Assert.Equal(15, result.X);
        Assert.Equal(30, result.Y);

        //Output
        _output.WriteLine($"({point.X}, {point.Y}) + ({size.Width}, {size.Height}) = ({result.X}, {result.Y})");
    }

    [Fact]
    public void Point_subtraction_with_size_works()
    {
        //Arrange
        var point = new Point(10, 20);
        var size = new Size(5, 10);

        //Act
        var result = point - size;

        //Assert
        Assert.Equal(5, result.X);
        Assert.Equal(10, result.Y);

        //Output
        _output.WriteLine($"({point.X}, {point.Y}) - ({size.Width}, {size.Height}) = ({result.X}, {result.Y})");
    }

    [Fact]
    public void Point_negation_works()
    {
        //Arrange
        var point = new Point(10, 20);

        //Act
        var result = -point;

        //Assert
        Assert.Equal(-10, result.X);
        Assert.Equal(-20, result.Y);

        //Output
        _output.WriteLine($"-({point.X}, {point.Y}) = ({result.X}, {result.Y})");
    }

    [Fact]
    public void Point_converts_to_PointF_implicitly()
    {
        //Arrange
        var point = new Point(10, 20);

        //Act
        PointF pointF = point;

        //Assert
        Assert.Equal(10f, pointF.X);
        Assert.Equal(20f, pointF.Y);

        //Output
        _output.WriteLine($"Point ({point.X}, {point.Y}) -> PointF ({pointF.X}, {pointF.Y})");
    }

    [Fact]
    public void Point_converts_to_Vector2_implicitly()
    {
        //Arrange
        var point = new Point(10, 20);

        //Act
        Vector2 vector = point;

        //Assert
        Assert.Equal(10f, vector.X);
        Assert.Equal(20f, vector.Y);

        //Output
        _output.WriteLine($"Point ({point.X}, {point.Y}) -> Vector2 ({vector.X}, {vector.Y})");
    }

    [Fact]
    public void Point_converts_to_Size_explicitly()
    {
        //Arrange
        var point = new Point(100, 200);

        //Act
        var size = (Size)point;

        //Assert
        Assert.Equal(100, size.Width);
        Assert.Equal(200, size.Height);

        //Output
        _output.WriteLine($"Point ({point.X}, {point.Y}) -> Size ({size.Width}x{size.Height})");
    }
}
