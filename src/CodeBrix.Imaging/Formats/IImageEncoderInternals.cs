// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats; //Was previously: namespace SixLabors.ImageSharp.Formats;

/// <summary>
/// Abstraction for shared internals for ***DecoderCore implementations to be used with <see cref="ImageEncoderUtilities"/>.
/// </summary>
internal interface IImageEncoderInternals
{
    /// <summary>
    /// Encodes the image.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    void Encode<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>;
}