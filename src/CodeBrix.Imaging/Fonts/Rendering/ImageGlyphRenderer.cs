// Copyright (c) Ellisnet
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Fonts.Rendering;

/// <summary>
/// An implementation of <see cref="IColorGlyphRenderer"/> that renders text glyphs onto
/// a <see cref="Image{TPixel}"/> bitmap. Supports both standard monochrome fonts and color fonts.
/// </summary>
/// <typeparam name="TPixel">The pixel format of the target image.</typeparam>
public sealed class ImageGlyphRenderer<TPixel> : IColorGlyphRenderer
    where TPixel : unmanaged, IPixel<TPixel>
{
    private const int BezierSegments = 10;
    private const float SubpixelScale = 4f;

    private readonly Image<TPixel> _image;
    private readonly TPixel _defaultColor;
    private TPixel _currentColor;
    //private bool _useGlyphColor;
    private readonly List<Vector2> _currentFigurePoints;
    private readonly List<List<Vector2>> _currentGlyphFigures;
    private Vector2 _currentPoint;
    private FontRectangle _textBounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageGlyphRenderer{TPixel}"/> class.
    /// </summary>
    /// <param name="image">The target image to render glyphs onto.</param>
    /// <param name="color">The default color to use for rendering the text (used for non-color fonts).</param>
    public ImageGlyphRenderer(Image<TPixel> image, TPixel color)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _defaultColor = color;
        _currentColor = color;
        //_useGlyphColor = false;
        _currentFigurePoints = new List<Vector2>();
        _currentGlyphFigures = new List<List<Vector2>>();
        _currentPoint = Vector2.Zero;
    }

    /// <summary>
    /// Gets the bounds of the rendered text.
    /// </summary>
    public FontRectangle TextBounds => _textBounds;

    /// <inheritdoc />
    public void SetColor(GlyphColor color)
    {
        // Convert GlyphColor to TPixel
        var vector = new Vector4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, color.Alpha / 255f);
        TPixel pixel = default;
        pixel.FromVector4(vector);
        _currentColor = pixel;
        //_useGlyphColor = true;
    }

    /// <inheritdoc />
    public void BeginText(FontRectangle bounds)
    {
        _textBounds = bounds;
    }

    /// <inheritdoc />
    public void EndText()
    {
        // Text rendering complete
    }

    /// <inheritdoc />
    public bool BeginGlyph(FontRectangle bounds, GlyphRendererParameters parameters)
    {
        _currentGlyphFigures.Clear();
        
        // Reset to default color at the start of each glyph
        // SetColor will be called after this if it's a color glyph layer
        _currentColor = _defaultColor;
        //_useGlyphColor = false;
        
        return true;
    }

    /// <inheritdoc />
    public void EndGlyph()
    {
        // Rasterize all collected figures for this glyph
        RasterizeGlyph();
        _currentGlyphFigures.Clear();
        
        // Reset to default color after rendering
        _currentColor = _defaultColor;
        //_useGlyphColor = false;
    }

    /// <inheritdoc />
    public void BeginFigure()
    {
        _currentFigurePoints.Clear();
    }

    /// <inheritdoc />
    public void EndFigure()
    {
        if (_currentFigurePoints.Count > 2)
        {
            // Close the figure by adding the first point at the end if needed
            var first = _currentFigurePoints[0];
            var last = _currentFigurePoints[_currentFigurePoints.Count - 1];
            if (Vector2.DistanceSquared(first, last) > 0.001f)
            {
                _currentFigurePoints.Add(first);
            }

            _currentGlyphFigures.Add(new List<Vector2>(_currentFigurePoints));
        }

        _currentFigurePoints.Clear();
    }

    /// <inheritdoc />
    public void MoveTo(Vector2 point)
    {
        _currentPoint = point;
        if (_currentFigurePoints.Count == 0)
        {
            _currentFigurePoints.Add(point);
        }
    }

    /// <inheritdoc />
    public void LineTo(Vector2 point)
    {
        _currentFigurePoints.Add(point);
        _currentPoint = point;
    }

    /// <inheritdoc />
    public void QuadraticBezierTo(Vector2 secondControlPoint, Vector2 point)
    {
        // Subdivide quadratic bezier into line segments
        var p0 = _currentPoint;
        var p1 = secondControlPoint;
        var p2 = point;

        for (var i = 1; i <= BezierSegments; i++)
        {
            var t = i / (float)BezierSegments;
            var u = 1f - t;

            // Quadratic bezier: B(t) = (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
            var pt = (u * u * p0) + (2f * u * t * p1) + (t * t * p2);
            _currentFigurePoints.Add(pt);
        }

        _currentPoint = point;
    }

    /// <inheritdoc />
    public void CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
    {
        // Subdivide cubic bezier into line segments
        var p0 = _currentPoint;
        var p1 = secondControlPoint;
        var p2 = thirdControlPoint;
        var p3 = point;

        for (var i = 1; i <= BezierSegments; i++)
        {
            var t = i / (float)BezierSegments;
            var u = 1f - t;

            // Cubic bezier: B(t) = (1-t)^3 * P0 + 3(1-t)^2 t * P1 + 3(1-t)t^2 * P2 + t^3 * P3
            var pt = (u * u * u * p0) +
                     (3f * u * u * t * p1) +
                     (3f * u * t * t * p2) +
                     (t * t * t * p3);
            _currentFigurePoints.Add(pt);
        }

        _currentPoint = point;
    }

    /// <summary>
    /// Rasterizes the current glyph's figures onto the image using a scanline fill algorithm.
    /// </summary>
    private void RasterizeGlyph()
    {
        if (_currentGlyphFigures.Count == 0)
        {
            return;
        }

        // Calculate bounding box for all figures
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        foreach (var figure in _currentGlyphFigures)
        {
            foreach (var pt in figure)
            {
                minX = Math.Min(minX, pt.X);
                minY = Math.Min(minY, pt.Y);
                maxX = Math.Max(maxX, pt.X);
                maxY = Math.Max(maxY, pt.Y);
            }
        }

        // Clamp to image bounds
        var startY = Math.Max(0, (int)Math.Floor(minY));
        var endY = Math.Min(_image.Height - 1, (int)Math.Ceiling(maxY));
        var startX = Math.Max(0, (int)Math.Floor(minX));
        var endX = Math.Min(_image.Width - 1, (int)Math.Ceiling(maxX));

        if (startY > endY || startX > endX)
        {
            return;
        }

        // Use supersampling for anti-aliasing
        var supersampleCount = (int)SubpixelScale;
        var subpixelStep = 1f / supersampleCount;

        // Reused across scanlines and sub-scanlines to keep this off the GC's back:
        // a glyph is rasterized once per draw and these would otherwise be reallocated
        // for every sub-scanline of every glyph.
        var coverage = new float[endX - startX + 1];
        var crossings = new List<(float X, int Direction)>();

        // For each scanline
        for (var y = startY; y <= endY; y++)
        {
            Array.Clear(coverage);

            // Supersample vertically
            for (var subY = 0; subY < supersampleCount; subY++)
            {
                var scanY = y + (subY + 0.5f) * subpixelStep;

                // Collect all edge crossings for this sub-scanline, recording the direction
                // each edge travels. TrueType ('glyf') and CFF/Type2 outlines are specified
                // to use the NON-ZERO WINDING rule, so a bare list of x positions is not
                // enough: two contours wound the same way that overlap must produce a solid
                // region, which the even-odd rule would punch a hole in.
                crossings.Clear();

                foreach (var figure in _currentGlyphFigures)
                {
                    for (var i = 0; i < figure.Count - 1; i++)
                    {
                        var p1 = figure[i];
                        var p2 = figure[i + 1];

                        // Half-open test on Y keeps a vertex shared by two edges from being
                        // counted twice.
                        if (p1.Y <= scanY && p2.Y > scanY)
                        {
                            var t = (scanY - p1.Y) / (p2.Y - p1.Y);
                            crossings.Add((p1.X + t * (p2.X - p1.X), 1));
                        }
                        else if (p2.Y <= scanY && p1.Y > scanY)
                        {
                            var t = (scanY - p1.Y) / (p2.Y - p1.Y);
                            crossings.Add((p1.X + t * (p2.X - p1.X), -1));
                        }
                    }
                }

                if (crossings.Count < 2)
                {
                    continue;
                }

                crossings.Sort(static (a, b) => a.X.CompareTo(b.X));

                // Walk the crossings accumulating the winding number. A span is inside the
                // glyph wherever the running total is non-zero.
                var winding = 0;
                var spanStart = 0f;

                foreach (var (x, direction) in crossings)
                {
                    var previous = winding;
                    winding += direction;

                    if (previous == 0 && winding != 0)
                    {
                        spanStart = x;
                    }
                    else if (previous != 0 && winding == 0)
                    {
                        AccumulateSpan(coverage, startX, endX, spanStart, x, supersampleCount);
                    }
                }
            }

            // Apply coverage to pixels
            for (var x = startX; x <= endX; x++)
            {
                var alpha = Math.Clamp(coverage[x - startX], 0f, 1f);

                if (alpha > 0.001f)
                {
                    BlendPixel(x, y, alpha);
                }
            }
        }
    }

    /// <summary>
    /// Adds the horizontal coverage contributed by a single filled span on one sub-scanline.
    /// </summary>
    private static void AccumulateSpan(
        float[] coverage, int startX, int endX, float xStart, float xEnd, int supersampleCount)
    {
        if (xEnd <= xStart)
        {
            return;
        }

        var pixelStart = Math.Max(startX, (int)Math.Floor(xStart));
        var pixelEnd = Math.Min(endX, (int)Math.Ceiling(xEnd));

        for (var x = pixelStart; x <= pixelEnd; x++)
        {
            // Exact horizontal area of the span covering this pixel cell.
            var left = Math.Max(x, xStart);
            var right = Math.Min(x + 1, xEnd);
            var pixelCoverage = Math.Max(0f, right - left);

            coverage[x - startX] += pixelCoverage / supersampleCount;
        }
    }

    /// <summary>
    /// Blends the text color with the existing pixel at the specified location.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="alpha">The alpha/coverage value (0-1).</param>
    private void BlendPixel(int x, int y, float alpha)
    {
        if (x < 0 || x >= _image.Width || y < 0 || y >= _image.Height)
        {
            return;
        }

        var existingVector = _image[x, y].ToVector4();
        var colorVector = _currentColor.ToVector4();

        // Scale the source alpha by the coverage of this pixel.
        var srcAlpha = colorVector.W * alpha;
        var dstAlpha = existingVector.W;

        // Standard "source over" compositing. TPixel formats such as Rgba32 store colour
        // NON-premultiplied, so the premultiplied result has to be divided back out by the
        // resulting alpha. Skipping that division is what produced dark fringes when text
        // was drawn onto a transparent background: a half-covered pixel of opaque red came
        // out as (128,0,0,128) instead of (255,0,0,128).
        var outAlpha = srcAlpha + (dstAlpha * (1f - srcAlpha));

        if (outAlpha <= 0f)
        {
            return;
        }

        var dstWeight = dstAlpha * (1f - srcAlpha);

        var blended = new Vector4(
            ((colorVector.X * srcAlpha) + (existingVector.X * dstWeight)) / outAlpha,
            ((colorVector.Y * srcAlpha) + (existingVector.Y * dstWeight)) / outAlpha,
            ((colorVector.Z * srcAlpha) + (existingVector.Z * dstWeight)) / outAlpha,
            outAlpha
        );

        TPixel result = default;
        result.FromVector4(blended);
        _image[x, y] = result;
    }
}
