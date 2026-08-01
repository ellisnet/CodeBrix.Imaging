using CodeBrix.Imaging.Fonts;
using CodeBrix.Imaging.Fonts.Rendering;
using CodeBrix.Imaging.PixelFormats;
using System;
using System.Numerics;
using Xunit;

namespace CodeBrix.Imaging.Tests.Fonts;

/// <summary>
/// Tests for the scanline rasterizer behind <see cref="TextRenderingExtensions.DrawText{TPixel}"/>.
/// The fill rule and the alpha compositing are both exercised directly, because a rendered
/// string is too coarse a signal to pin either of them down.
/// </summary>
public class ImageGlyphRendererTests
{
    private readonly ITestOutputHelper _output;

    public ImageGlyphRendererTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Adds an axis-aligned square contour to the renderer, wound either clockwise or
    /// counter-clockwise.
    /// </summary>
    private static void AddSquare(IColorGlyphRenderer renderer, float x, float y, float size, bool clockwise)
    {
        renderer.BeginFigure();
        renderer.MoveTo(new Vector2(x, y));

        if (clockwise)
        {
            renderer.LineTo(new Vector2(x + size, y));
            renderer.LineTo(new Vector2(x + size, y + size));
            renderer.LineTo(new Vector2(x, y + size));
        }
        else
        {
            renderer.LineTo(new Vector2(x, y + size));
            renderer.LineTo(new Vector2(x + size, y + size));
            renderer.LineTo(new Vector2(x + size, y));
        }

        renderer.EndFigure();
    }

    [Fact]
    public void Overlapping_contours_with_the_same_winding_fill_solid()
    {
        //Arrange - TrueType ('glyf') and CFF/Type2 outlines use the NON-ZERO WINDING rule.
        //Two contours wound the same way that overlap must produce a solid region; the
        //even-odd rule would punch a hole through the overlap instead.
        using var image = new Image<Rgba32>(40, 40, new Rgba32(0, 0, 0, 255));
        var renderer = new ImageGlyphRenderer<Rgba32>(image, new Rgba32(255, 255, 255, 255));

        //Act
        renderer.BeginText(new FontRectangle(0, 0, 40, 40));
        renderer.BeginGlyph(new FontRectangle(0, 0, 40, 40), default);
        AddSquare(renderer, 5, 5, 20, clockwise: true);
        AddSquare(renderer, 15, 15, 20, clockwise: true);
        renderer.EndGlyph();
        renderer.EndText();

        //Assert
        var insideOne = image[10, 10];
        var insideOverlap = image[20, 20];
        Assert.Equal(255, insideOne.R);
        Assert.Equal(255, insideOverlap.R);
        _output.WriteLine($"inside one contour = {insideOne.R}, inside overlap = {insideOverlap.R}");
    }

    [Fact]
    public void Opposed_contours_cut_a_hole_like_a_glyph_counter()
    {
        //Arrange - a counter (the hole in an 'o') is an inner contour wound OPPOSITE to the
        //outer one. Non-zero winding must still hollow that out.
        using var image = new Image<Rgba32>(40, 40, new Rgba32(0, 0, 0, 255));
        var renderer = new ImageGlyphRenderer<Rgba32>(image, new Rgba32(255, 255, 255, 255));

        //Act
        renderer.BeginText(new FontRectangle(0, 0, 40, 40));
        renderer.BeginGlyph(new FontRectangle(0, 0, 40, 40), default);
        AddSquare(renderer, 5, 5, 30, clockwise: true);
        AddSquare(renderer, 15, 15, 10, clockwise: false);
        renderer.EndGlyph();
        renderer.EndText();

        //Assert
        var onTheRing = image[8, 20];
        var inTheCounter = image[20, 20];
        Assert.Equal(255, onTheRing.R);
        Assert.Equal(0, inTheCounter.R);
        _output.WriteLine($"ring = {onTheRing.R}, counter = {inTheCounter.R}");
    }

    [Fact]
    public void Partial_coverage_on_a_transparent_background_keeps_the_source_colour()
    {
        //Arrange - TPixel formats such as Rgba32 are NOT premultiplied, so a half-covered
        //pixel of opaque red must come out as (255,0,0,~128), not (128,0,0,~128). Storing the
        //premultiplied value is what produced dark fringes around text on transparent images.
        using var image = new Image<Rgba32>(30, 30, new Rgba32(0, 0, 0, 0));
        var renderer = new ImageGlyphRenderer<Rgba32>(image, new Rgba32(255, 0, 0, 255));

        //Act - the square edge lands mid-pixel at x = 5.5, so column 5 is ~50% covered.
        renderer.BeginText(new FontRectangle(0, 0, 30, 30));
        renderer.BeginGlyph(new FontRectangle(0, 0, 30, 30), default);
        renderer.BeginFigure();
        renderer.MoveTo(new Vector2(5.5f, 5.5f));
        renderer.LineTo(new Vector2(24.5f, 5.5f));
        renderer.LineTo(new Vector2(24.5f, 24.5f));
        renderer.LineTo(new Vector2(5.5f, 24.5f));
        renderer.EndFigure();
        renderer.EndGlyph();
        renderer.EndText();

        //Assert
        var edge = image[5, 15];
        var interior = image[15, 15];

        Assert.InRange(edge.A, 1, 254);          // genuinely partial coverage
        Assert.Equal(255, edge.R);               // colour NOT darkened toward the background
        Assert.Equal(0, edge.G);
        Assert.Equal(0, edge.B);
        Assert.Equal(new Rgba32(255, 0, 0, 255), interior);
        _output.WriteLine($"edge = RGBA({edge.R},{edge.G},{edge.B},{edge.A}), interior = {interior}");
    }

    [Fact]
    public void Drawing_over_an_opaque_background_blends_towards_it()
    {
        //Arrange
        using var image = new Image<Rgba32>(30, 30, new Rgba32(255, 255, 255, 255));
        var renderer = new ImageGlyphRenderer<Rgba32>(image, new Rgba32(255, 0, 0, 255));

        //Act
        renderer.BeginText(new FontRectangle(0, 0, 30, 30));
        renderer.BeginGlyph(new FontRectangle(0, 0, 30, 30), default);
        renderer.BeginFigure();
        renderer.MoveTo(new Vector2(5.5f, 5.5f));
        renderer.LineTo(new Vector2(24.5f, 5.5f));
        renderer.LineTo(new Vector2(24.5f, 24.5f));
        renderer.LineTo(new Vector2(5.5f, 24.5f));
        renderer.EndFigure();
        renderer.EndGlyph();
        renderer.EndText();

        //Assert - a half covered pixel over white sits between the text colour and white,
        //and the destination stays opaque.
        var edge = image[5, 15];
        Assert.Equal(255, edge.A);
        Assert.Equal(255, edge.R);
        Assert.InRange(edge.G, 1, 254);
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[15, 15]);
        _output.WriteLine($"edge over white = RGBA({edge.R},{edge.G},{edge.B},{edge.A})");
    }

    [Fact]
    public void MeasureText_validates_the_font_before_the_empty_text_short_circuit()
    {
        //Arrange, Act & Assert - a null font is a programming error whether or not there
        //happens to be any text to measure.
        Assert.Throws<ArgumentNullException>(() => TextRenderingExtensions.MeasureText("", (Font)null));
        Assert.Throws<ArgumentNullException>(() => TextRenderingExtensions.MeasureText("abc", (Font)null));
        Assert.Throws<ArgumentNullException>(() => TextRenderingExtensions.MeasureText("", (TextOptions)null));
        _output.WriteLine("Null font/options rejected regardless of the text argument");
    }

    [Fact]
    public void DrawText_validates_arguments_before_the_empty_text_short_circuit()
    {
        //Arrange
        using var image = new Image<Rgba32>(10, 10);

        //Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            image.DrawText("", (Font)null, new Rgba32(0, 0, 0, 255), Vector2.Zero));
        Assert.Throws<ArgumentNullException>(() =>
            image.DrawText("", (TextOptions)null, new Rgba32(0, 0, 0, 255)));
        _output.WriteLine("DrawText rejects a null font/options even for empty text");
    }
}
