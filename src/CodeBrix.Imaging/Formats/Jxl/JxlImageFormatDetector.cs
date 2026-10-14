// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Detects bare JPEG XL codestreams and JPEG XL containers.</summary>
public sealed class JxlImageFormatDetector : IImageFormatDetector
{
    /// <inheritdoc/>
    public int HeaderSize => 12;

    /// <inheritdoc/>
    public IImageFormat DetectFormat(ReadOnlySpan<byte> header) =>
        (header.Length >= 2 && header[0] == 255 && header[1] == 10) ||
        (header.Length >= 12 && header[..12].SequenceEqual(JxlConstants.ContainerSignature))
            ? JxlFormat.Instance : null;
}
