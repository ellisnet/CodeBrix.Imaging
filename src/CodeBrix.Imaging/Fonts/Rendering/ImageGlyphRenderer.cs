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

            // Quadratic bezier: B(t) = (1-t)²P0 + 2(1-t)tP1 + t²P2
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

            // Cubic bezier: B(t) = (1-t)³P0 + 3(1-t)²tP1 + 3(1-t)t²P2 + t³P3
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

        // For each scanline
        for (var y = startY; y <= endY; y++)
        {
            // Create a coverage array for this scanline
            var coverage = new float[endX - startX + 1];

            // Supersample vertically
            for (var subY = 0; subY < supersampleCount; subY++)
            {
                var scanY = y + (subY + 0.5f) * subpixelStep;

                // Collect all edge intersections for this sub-scanline
                var intersections = new List<float>();

                foreach (var figure in _currentGlyphFigures)
                {
                    for (var i = 0; i < figure.Count - 1; i++)
                    {
                        var p1 = figure[i];
                        var p2 = figure[i + 1];

                        // Check if this edge crosses the scanline
                        if ((p1.Y <= scanY && p2.Y > scanY) || (p2.Y <= scanY && p1.Y > scanY))
                        {
                            // Calculate x intersection
                            var t = (scanY - p1.Y) / (p2.Y - p1.Y);
                            var x = p1.X + t * (p2.X - p1.X);
                            intersections.Add(x);
                        }
                    }
                }

                if (intersections.Count < 2)
                {
                    continue;
                }

                // Sort intersections
                intersections.Sort();

                // Fill between pairs of intersections (even-odd fill rule)
                for (var i = 0; i < intersections.Count - 1; i += 2)
                {
                    var xStart = intersections[i];
                    var xEnd = intersections[i + 1];

                    var pixelStart = Math.Max(startX, (int)Math.Floor(xStart));
                    var pixelEnd = Math.Min(endX, (int)Math.Ceiling(xEnd));

                    for (var x = pixelStart; x <= pixelEnd; x++)
                    {
                        // Calculate coverage for this pixel
                        var left = Math.Max(x, xStart);
                        var right = Math.Min(x + 1, xEnd);
                        var pixelCoverage = Math.Max(0, right - left);

                        coverage[x - startX] += pixelCoverage / supersampleCount;
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

        var existingPixel = _image[x, y];
        var existingVector = existingPixel.ToVector4();
        var colorVector = _currentColor.ToVector4();

        // Premultiply the source alpha with the coverage
        var srcAlpha = colorVector.W * alpha;

        // Alpha blend: out = src * srcAlpha + dst * (1 - srcAlpha)
        var blended = new Vector4(
            (colorVector.X * srcAlpha) + (existingVector.X * (1f - srcAlpha)),
            (colorVector.Y * srcAlpha) + (existingVector.Y * (1f - srcAlpha)),
            (colorVector.Z * srcAlpha) + (existingVector.Z * (1f - srcAlpha)),
            srcAlpha + (existingVector.W * (1f - srcAlpha))
        );

        TPixel result = default;
        result.FromVector4(blended);
        _image[x, y] = result;
    }
}
