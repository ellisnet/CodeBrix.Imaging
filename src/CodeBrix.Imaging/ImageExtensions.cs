// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Formats;

namespace CodeBrix.Imaging; //Was previously: namespace SixLabors.ImageSharp;

/// <summary>
/// Extension methods for the <see cref="Image"/> type.
/// </summary>
public static partial class ImageExtensions
{
    /// <summary>
    /// Writes the image to the given file path using an encoder detected from the path.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="path">The file path to save the image to.</param>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    /// <exception cref="NotSupportedException">No encoder available for provided path.</exception>
    public static void Save(this Image source, string path)
        => source.Save(path, source.DetectEncoder(path));

    /// <summary>
    /// Writes the image to the given file path using an encoder detected from the path.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="path">The file path to save the image to.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    /// <exception cref="NotSupportedException">No encoder available for provided path.</exception>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static Task SaveAsync(this Image source, string path, CancellationToken cancellationToken = default)
        => source.SaveAsync(path, source.DetectEncoder(path), cancellationToken);

    /// <summary>
    /// Writes the image to the given file path using the given image encoder.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="path">The file path to save the image to.</param>
    /// <param name="encoder">The encoder to save the image with.</param>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    /// <exception cref="ArgumentNullException">The encoder is null.</exception>
    public static void Save(this Image source, string path, IImageEncoder encoder)
    {
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(encoder, nameof(encoder));
        using (var fs = source.GetConfiguration().FileSystem.Create(path))
        {
            source.Save(fs, encoder);
        }
    }

    /// <summary>
    /// Writes the image to the given file path using the given image encoder.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="path">The file path to save the image to.</param>
    /// <param name="encoder">The encoder to save the image with.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    /// <exception cref="ArgumentNullException">The encoder is null.</exception>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static async Task SaveAsync(
        this Image source,
        string path,
        IImageEncoder encoder,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(encoder, nameof(encoder));

        using (var fs = source.GetConfiguration().FileSystem.Create(path))
        {
            await source.SaveAsync(fs, encoder, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes the image to the given stream using the given image format.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="stream">The stream to save the image to.</param>
    /// <param name="format">The format to save the image in.</param>
    /// <exception cref="ArgumentNullException">The stream is null.</exception>
    /// <exception cref="ArgumentNullException">The format is null.</exception>
    /// <exception cref="NotSupportedException">The stream is not writable.</exception>
    /// <exception cref="NotSupportedException">No encoder available for provided format.</exception>
    public static void Save(this Image source, Stream stream, IImageFormat format)
    {
        Guard.NotNull(stream, nameof(stream));
        Guard.NotNull(format, nameof(format));

        if (!stream.CanWrite)
        {
            throw new NotSupportedException("Cannot write to the stream.");
        }

        var encoder = source.GetConfiguration().ImageFormatsManager.FindEncoder(format);

        if (encoder is null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("No encoder was found for the provided mime type. Registered encoders include:");

            foreach (var val in source.GetConfiguration().ImageFormatsManager.ImageEncoders)
            {
                sb.AppendFormat(" - {0} : {1}{2}", val.Key.Name, val.Value.GetType().Name, Environment.NewLine);
            }

            throw new NotSupportedException(sb.ToString());
        }

        source.Save(stream, encoder);
    }

    /// <summary>
    /// Writes the image to the given stream using the given image format.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="stream">The stream to save the image to.</param>
    /// <param name="format">The format to save the image in.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException">The stream is null.</exception>
    /// <exception cref="ArgumentNullException">The format is null.</exception>
    /// <exception cref="NotSupportedException">The stream is not writable.</exception>
    /// <exception cref="NotSupportedException">No encoder available for provided format.</exception>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static Task SaveAsync(
        this Image source,
        Stream stream,
        IImageFormat format,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(stream, nameof(stream));
        Guard.NotNull(format, nameof(format));

        if (!stream.CanWrite)
        {
            throw new NotSupportedException("Cannot write to the stream.");
        }

        var encoder = source.GetConfiguration().ImageFormatsManager.FindEncoder(format);

        if (encoder is null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("No encoder was found for the provided mime type. Registered encoders include:");

            foreach (var val in source.GetConfiguration().ImageFormatsManager.ImageEncoders)
            {
                sb.AppendFormat(" - {0} : {1}{2}", val.Key.Name, val.Value.GetType().Name, Environment.NewLine);
            }

            throw new NotSupportedException(sb.ToString());
        }

        return source.SaveAsync(stream, encoder, cancellationToken);
    }

    /// <summary>
    /// Returns a Base64 encoded string from the given image.
    /// The result is prepended with a Data URI <see href="https://en.wikipedia.org/wiki/Data_URI_scheme"/>
    /// <para>
    /// <example>
    /// For example:
    /// <see href="data:image/gif;base64,R0lGODlhAQABAIABAEdJRgAAACwAAAAAAQABAAACAkQBAA=="/>
    /// </example>
    /// </para>
    /// </summary>
    /// <param name="source">The source image</param>
    /// <param name="format">The format.</param>
    /// <exception cref="ArgumentNullException">The format is null.</exception>
    /// <returns>The <see cref="string"/></returns>
    public static string ToBase64String(this Image source, IImageFormat format)
    {
        Guard.NotNull(format, nameof(format));

        using var stream = new MemoryStream();
        source.Save(stream, format);

        // Always available.
        stream.TryGetBuffer(out var buffer);
        return $"data:{format.DefaultMimeType};base64,{Convert.ToBase64String(buffer.Array, 0, (int)stream.Length)}";
    }

    /// <summary>
    /// Encodes the image in the given format and returns the result as a byte array.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="format">The format to encode the image in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="format"/> is null.</exception>
    /// <exception cref="NotSupportedException">
    /// No encoder is registered for <paramref name="format"/>. This includes
    /// <see cref="UnknownImageFormat"/>, which an image carries until it has been loaded
    /// from - or saved in - a known format.
    /// </exception>
    /// <returns>The encoded image bytes.</returns>
    public static byte[] ToByteArray(this Image source, IImageFormat format)
    {
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(format, nameof(format));

        using var stream = new MemoryStream();
        source.Save(stream, format);
        return stream.ToArray();
    }

    /// <summary>
    /// Encodes the image using the given encoder and returns the result as a byte array.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="encoder">The encoder to use.</param>
    /// <exception cref="ArgumentNullException"><paramref name="encoder"/> is null.</exception>
    /// <returns>The encoded image bytes.</returns>
    public static byte[] ToByteArray(this Image source, IImageEncoder encoder)
    {
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(encoder, nameof(encoder));

        using var stream = new MemoryStream();
        source.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>
    /// Asynchronously encodes the image in the given format and returns the result as a
    /// byte array.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="format">The format to encode the image in.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="format"/> is null.</exception>
    /// <exception cref="NotSupportedException">
    /// No encoder is registered for <paramref name="format"/>. This includes
    /// <see cref="UnknownImageFormat"/>, which an image carries until it has been loaded
    /// from - or saved in - a known format.
    /// </exception>
    /// <returns>The encoded image bytes.</returns>
    public static async Task<byte[]> ToByteArrayAsync(
        this Image source,
        IImageFormat format,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(format, nameof(format));

        using var stream = new MemoryStream();
        await source.SaveAsync(stream, format, cancellationToken).ConfigureAwait(false);
        return stream.ToArray();
    }

    /// <summary>
    /// Asynchronously encodes the image using the given encoder and returns the result as a
    /// byte array.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="encoder">The encoder to use.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="encoder"/> is null.</exception>
    /// <returns>The encoded image bytes.</returns>
    public static async Task<byte[]> ToByteArrayAsync(
        this Image source,
        IImageEncoder encoder,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(encoder, nameof(encoder));

        using var stream = new MemoryStream();
        await source.SaveAsync(stream, encoder, cancellationToken).ConfigureAwait(false);
        return stream.ToArray();
    }
}
