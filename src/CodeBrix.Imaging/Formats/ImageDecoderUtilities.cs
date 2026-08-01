// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.IO;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats; //Was previously: namespace SixLabors.ImageSharp.Formats;

internal static class ImageDecoderUtilities
{
    public static IImageInfo Identify(
        this IImageDecoderInternals decoder,
        Configuration configuration,
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var bufferedReadStream = new BufferedReadStream(configuration, stream);

        try
        {
            return decoder.Identify(bufferedReadStream, cancellationToken);
        }
        catch (InvalidMemoryOperationException ex)
        {
            throw new InvalidImageContentException(decoder.Dimensions, ex);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidImageContentException(TruncatedStreamMessage, ex);
        }
    }

    public static Image<TPixel> Decode<TPixel>(
        this IImageDecoderInternals decoder,
        Configuration configuration,
        Stream stream,
        CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
        => decoder.Decode<TPixel>(configuration, stream, DefaultLargeImageExceptionFactory, cancellationToken);

    public static Image<TPixel> Decode<TPixel>(
        this IImageDecoderInternals decoder,
        Configuration configuration,
        Stream stream,
        Func<InvalidMemoryOperationException, Size, InvalidImageContentException> largeImageExceptionFactory,
        CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Test may pass a BufferedReadStream in order to monitor EOF hits, if so, use the existing instance.
        var bufferedReadStream = stream as BufferedReadStream ?? new BufferedReadStream(configuration, stream);

        try
        {
            return decoder.Decode<TPixel>(bufferedReadStream, cancellationToken);
        }
        catch (InvalidMemoryOperationException ex)
        {
            throw largeImageExceptionFactory(ex, decoder.Dimensions);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidImageContentException(TruncatedStreamMessage, ex);
        }
        finally
        {
            if (bufferedReadStream != stream)
            {
                bufferedReadStream.Dispose();
            }
        }
    }

    /// <summary>
    /// The message used when a decoder runs off the end of the stream. The decoders read
    /// with <see cref="Stream.ReadExactly(byte[], int, int)"/> rather than
    /// <see cref="Stream.Read(byte[], int, int)"/> so that a short read fails instead of
    /// silently decoding uninitialized buffer contents. That surfaces as an
    /// <see cref="EndOfStreamException"/>, which is wrapped here so that callers only ever
    /// have to handle the documented <see cref="ImageFormatException"/> hierarchy.
    /// </summary>
    private const string TruncatedStreamMessage =
        "Cannot decode image. The image data ended unexpectedly; the source is truncated or corrupt.";

    private static InvalidImageContentException DefaultLargeImageExceptionFactory(
        InvalidMemoryOperationException memoryOperationException,
        Size dimensions) =>
        new(dimensions, memoryOperationException);
}