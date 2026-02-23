using System;
using System.Collections.Generic;
using Xunit;

namespace CodeBrix.Imaging.Tests.Primitives;

public class RectangleTests
{
    private readonly ITestOutputHelper _output;

    public RectangleTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private void ReportRectangles(IList<Rectangle> rectangles)
    {
        if (rectangles is { Count: > 0 })
        {
            foreach (var rect in rectangles)
            {
                _output.WriteLine($"Rectangle: X={rect.X}, Y={rect.Y}, Width={rect.Width}, Height={rect.Height}");
            }
        }
    }

    [Fact]
    public void Constructor_with_coordinates_creates_rectangle()
    {
        //Arrange
        var rectangles = new List<Rectangle>();

        //Act
        rectangles.Add(new Rectangle(10, 20, 100, 200));
        rectangles.Add(new Rectangle(0, 0, 50, 50));
        rectangles.Add(new Rectangle(-10, -10, 30, 30));

        //Assert
        Assert.Equal(3, rectangles.Count);
        Assert.Equal(10, rectangles[0].X);
        Assert.Equal(20, rectangles[0].Y);
        Assert.Equal(100, rectangles[0].Width);
        Assert.Equal(200, rectangles[0].Height);

        //Output
        ReportRectangles(rectangles);
    }

    [Fact]
    public void Constructor_with_point_and_size_creates_rectangle()
    {
        //Arrange
        var point = new Point(15, 25);
        var size = new Size(150, 250);

        //Act
        var rectangle = new Rectangle(point, size);

        //Assert
        Assert.Equal(15, rectangle.X);
        Assert.Equal(25, rectangle.Y);
        Assert.Equal(150, rectangle.Width);
        Assert.Equal(250, rectangle.Height);

        //Output
        ReportRectangles([rectangle]);
    }

    [Fact]
    public void Location_property_returns_correct_point()
    {
        //Arrange
        var rectangle = new Rectangle(10, 20, 100, 200);

        //Act
        var location = rectangle.Location;

        //Assert
        Assert.Equal(10, location.X);
        Assert.Equal(20, location.Y);

        //Output
        _output.WriteLine($"Location: ({location.X}, {location.Y})");
    }

    [Fact]
    public void Size_property_returns_correct_size()
    {
        //Arrange
        var rectangle = new Rectangle(10, 20, 100, 200);

        //Act
        var size = rectangle.Size;

        //Assert
        Assert.Equal(100, size.Width);
        Assert.Equal(200, size.Height);

        //Output
        _output.WriteLine($"Size: {size.Width}x{size.Height}");
    }

    [Fact]
    public void Edge_properties_return_correct_values()
    {
        //Arrange
        var rectangle = new Rectangle(10, 20, 100, 200);

        //Act & Assert
        Assert.Equal(10, rectangle.Left);
        Assert.Equal(20, rectangle.Top);
        Assert.Equal(110, rectangle.Right);
        Assert.Equal(220, rectangle.Bottom);

        //Output
        _output.WriteLine($"Edges: Left={rectangle.Left}, Top={rectangle.Top}, Right={rectangle.Right}, Bottom={rectangle.Bottom}");
    }

    [Fact]
    public void Empty_rectangle_is_correctly_identified()
    {
        //Arrange
        var emptyRect = Rectangle.Empty;
        var nonEmptyRect = new Rectangle(0, 0, 10, 10);

        //Act & Assert
        Assert.True(emptyRect.IsEmpty);
        Assert.False(nonEmptyRect.IsEmpty);

        //Output
        _output.WriteLine($"Empty rectangle IsEmpty: {emptyRect.IsEmpty}");
        _output.WriteLine($"Non-empty rectangle IsEmpty: {nonEmptyRect.IsEmpty}");
    }

    [Fact]
    public void Rectangle_equality_works()
    {
        //Arrange
        var rect1 = new Rectangle(10, 20, 100, 200);
        var rect2 = new Rectangle(10, 20, 100, 200);
        var rect3 = new Rectangle(5, 5, 50, 50);

        //Act & Assert
        Assert.True(rect1.Equals(rect2));
        Assert.False(rect1.Equals(rect3));
        Assert.True(rect1 == rect2);
        Assert.True(rect1 != rect3);

        //Output
        _output.WriteLine($"rect1 == rect2: {rect1 == rect2}");
        _output.WriteLine($"rect1 != rect3: {rect1 != rect3}");
    }

    [Fact]
    public void Rectangle_is_mutable()
    {
        //Arrange
        var rectangle = new Rectangle(10, 20, 100, 200);

        //Act
        rectangle.X = 50;
        rectangle.Y = 60;
        rectangle.Width = 300;
        rectangle.Height = 400;

        //Assert
        Assert.Equal(50, rectangle.X);
        Assert.Equal(60, rectangle.Y);
        Assert.Equal(300, rectangle.Width);
        Assert.Equal(400, rectangle.Height);

        //Output
        ReportRectangles([rectangle]);
    }

    [Fact]
    public void Location_setter_modifies_coordinates()
    {
        //Arrange
        var rectangle = new Rectangle(10, 20, 100, 200);

        //Act
        rectangle.Location = new Point(50, 60);

        //Assert
        Assert.Equal(50, rectangle.X);
        Assert.Equal(60, rectangle.Y);

        //Output
        _output.WriteLine($"New location: ({rectangle.X}, {rectangle.Y})");
    }

    [Fact]
    public void Size_setter_modifies_dimensions()
    {
        //Arrange
        var rectangle = new Rectangle(10, 20, 100, 200);

        //Act
        rectangle.Size = new Size(300, 400);

        //Assert
        Assert.Equal(300, rectangle.Width);
        Assert.Equal(400, rectangle.Height);

        //Output
        _output.WriteLine($"New size: {rectangle.Width}x{rectangle.Height}");
    }
}
