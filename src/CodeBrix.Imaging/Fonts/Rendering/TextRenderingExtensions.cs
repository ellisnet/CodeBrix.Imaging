// Copyright (c) Ellisnet
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using CodeBrix.Imaging.Advanced;
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
        // Arguments are validated BEFORE the empty-text short circuit: a null font is a
        // programming error whether or not there happens to be anything to draw.
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(font);

        if (string.IsNullOrEmpty(text))
        {
            return image;
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
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(text))
        {
            return image;
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
        ArgumentNullException.ThrowIfNull(font);

        if (string.IsNullOrEmpty(text))
        {
            return FontRectangle.Empty;
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
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(text))
        {
            return FontRectangle.Empty;
        }

        return TextMeasurer.Measure(text, options);
    }

    /// <summary>
    /// Draws text onto the image at the specified location.
    /// </summary>
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
    public static Image DrawText(
        this Image image,
        string text,
        Font font,
        Color color,
        Vector2 location,
        bool forceMonoColor = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(font);

        if (string.IsNullOrEmpty(text))
        {
            return image;
        }

        var options = new TextOptions(font)
        {
            Origin = location,
            ColorFontSupport = forceMonoColor ? ColorFontSupport.None : ColorFontSupport.MicrosoftColrFormat
        };

        return DrawText(image, text, options, color);
    }

    /// <summary>
    /// Draws text onto the image at the specified X and Y coordinates.
    /// </summary>
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
    public static Image DrawText(
        this Image image,
        string text,
        Font font,
        Color color,
        float x,
        float y,
        bool forceMonoColor = false)
        => DrawText(image, text, font, color, new Vector2(x, y), forceMonoColor);

    /// <summary>
    /// Draws text onto the image using the specified text options.
    /// </summary>
    /// <param name="image">The target image to draw text onto.</param>
    /// <param name="text">The text string to render.</param>
    /// <param name="options">The text rendering options including font, origin, alignment, etc.</param>
    /// <param name="color">The color of the text.</param>
    /// <returns>The image with the rendered text, for method chaining.</returns>
    /// <remarks>
    /// This overload works on the non-generic <see cref="Image"/> and dispatches to the
    /// image's own pixel type internally, so callers do not need to know it.
    /// </remarks>
    public static Image DrawText(this Image image, string text, TextOptions options, Color color)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(text))
        {
            return image;
        }

        image.AcceptVisitor(new DrawTextVisitor(text, options, color));
        return image;
    }

    /// <summary>
    /// Draws text onto a generic image using a <see cref="Color"/> rather than a pixel value.
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
        Color color,
        Vector2 location,
        bool forceMonoColor = false)
        where TPixel : unmanaged, IPixel<TPixel>
        => DrawText(image, text, font, color.ToPixel<TPixel>(), location, forceMonoColor);

    /// <summary>
    /// Draws text onto a generic image using a <see cref="Color"/> rather than a pixel value.
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
        Color color)
        where TPixel : unmanaged, IPixel<TPixel>
        => DrawText(image, text, options, color.ToPixel<TPixel>());

    /// <summary>
    /// Applies <see cref="DrawText{TPixel}(Image{TPixel}, string, TextOptions, Color)"/> to a
    /// non-generic <see cref="Image"/> by recovering its pixel type through double dispatch.
    /// </summary>
    private sealed class DrawTextVisitor : IImageVisitor
    {
        private readonly string _text;
        private readonly TextOptions _options;
        private readonly Color _color;

        public DrawTextVisitor(string text, TextOptions options, Color color)
        {
            _text = text;
            _options = options;
            _color = color;
        }

        public void Visit<TPixel>(Image<TPixel> image)
            where TPixel : unmanaged, IPixel<TPixel>
            => DrawText(image, _text, _options, _color.ToPixel<TPixel>());
    }
}
