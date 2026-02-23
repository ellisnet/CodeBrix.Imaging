// Copyright (c) Ellisnet
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Fonts.Rendering;

/// <summary>
/// Provides extension methods for rendering text onto images.
/// </summary>
public static class TextRenderingExtensions
{
    /// <summary>
    /// Draws text onto the image at the specified location.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the image.</typeparam>
    /// <param name="image">The target image to draw text onto.</param>
    /// <param name="text">The text string to render.</param>
    /// <param name="font">The font to use for rendering.</param>
    /// <param name="color">The color of the text.</param>
    /// <param name="location">The location (origin) where text rendering begins.</param>
    /// <param name="forceMonoColor">
    /// When <c>true</c>, forces color fonts to be rendered using the specified <paramref name="color"/>
    /// instead of the font's built-in colors. Default is <c>false</c>.
    /// </param>
    /// <returns>The image with the rendered text, for method chaining.</returns>
    public static Image<TPixel> DrawText<TPixel>(
        this Image<TPixel> image,
        string text,
        Font font,
        TPixel color,
        Vector2 location,
        bool forceMonoColor = false)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (image == null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (string.IsNullOrEmpty(text))
        {
            return image;
        }

        if (font == null)
        {
            throw new ArgumentNullException(nameof(font));
        }

        var options = new TextOptions(font)
        {
            Origin = location,
            ColorFontSupport = forceMonoColor ? ColorFontSupport.None : ColorFontSupport.MicrosoftColrFormat
        };

        return DrawText(image, text, options, color);
    }

    /// <summary>
    /// Draws text onto the image using the specified text options.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the image.</typeparam>
    /// <param name="image">The target image to draw text onto.</param>
    /// <param name="text">The text string to render.</param>
    /// <param name="options">The text rendering options including font, origin, alignment, etc.</param>
    /// <param name="color">The color of the text.</param>
    /// <returns>The image with the rendered text, for method chaining.</returns>
    public static Image<TPixel> DrawText<TPixel>(
        this Image<TPixel> image,
        string text,
        TextOptions options,
        TPixel color)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (image == null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (string.IsNullOrEmpty(text))
        {
            return image;
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var renderer = new ImageGlyphRenderer<TPixel>(image, color);
        TextRenderer.RenderTextTo(renderer, text, options);

        return image;
    }

    /// <summary>
    /// Draws text onto the image at the specified X and Y coordinates.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the image.</typeparam>
    /// <param name="image">The target image to draw text onto.</param>
    /// <param name="text">The text string to render.</param>
    /// <param name="font">The font to use for rendering.</param>
    /// <param name="color">The color of the text.</param>
    /// <param name="x">The X coordinate where text rendering begins.</param>
    /// <param name="y">The Y coordinate where text rendering begins.</param>
    /// <param name="forceMonoColor">
    /// When <c>true</c>, forces color fonts to be rendered using the specified <paramref name="color"/>
    /// instead of the font's built-in colors. Default is <c>false</c>.
    /// </param>
    /// <returns>The image with the rendered text, for method chaining.</returns>
    public static Image<TPixel> DrawText<TPixel>(
        this Image<TPixel> image,
        string text,
        Font font,
        TPixel color,
        float x,
        float y,
        bool forceMonoColor = false)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        return DrawText(image, text, font, color, new Vector2(x, y), forceMonoColor);
    }

    /// <summary>
    /// Measures the size of the text when rendered with the specified font.
    /// </summary>
    /// <param name="text">The text string to measure.</param>
    /// <param name="font">The font to use for measuring.</param>
    /// <returns>A <see cref="FontRectangle"/> containing the bounds of the text.</returns>
    public static FontRectangle MeasureText(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
        {
            return FontRectangle.Empty;
        }

        if (font == null)
        {
            throw new ArgumentNullException(nameof(font));
        }

        var options = new TextOptions(font);
        return TextMeasurer.Measure(text, options);
    }

    /// <summary>
    /// Measures the size of the text when rendered with the specified options.
    /// </summary>
    /// <param name="text">The text string to measure.</param>
    /// <param name="options">The text rendering options.</param>
    /// <returns>A <see cref="FontRectangle"/> containing the bounds of the text.</returns>
    public static FontRectangle MeasureText(string text, TextOptions options)
    {
        if (string.IsNullOrEmpty(text))
        {
            return FontRectangle.Empty;
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return TextMeasurer.Measure(text, options);
    }
}
