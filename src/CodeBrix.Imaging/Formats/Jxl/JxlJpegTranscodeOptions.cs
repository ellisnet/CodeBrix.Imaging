// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Controls JPEG recompression and its input limits.</summary>
public sealed class JxlJpegTranscodeOptions
{
    /// <summary>Gets or sets the encoding effort from one through ten. The default is seven.</summary>
    public int Effort { get; set; } = 7;
    /// <summary>Gets or sets whether effort eleven may be selected.</summary>
    public bool AllowExpertEffort { get; set; }
    /// <summary>Gets or sets whether an unsupported JPEG may be decoded to pixels and saved losslessly.</summary>
    /// <remarks>Pixel fallback does not retain the original JPEG bytes or provide JPEG reconstruction data.
    /// It requires a JPEG coding process supported by the managed JPEG pixel decoder. The default is false.</remarks>
    public bool AllowPixelFallback { get; set; }
    /// <summary>Gets or sets the maximum pixel count. The default is 100 million.</summary>
    public long MaxPixels { get; set; } = 100_000_000;
    /// <summary>Gets or sets the maximum JPEG input size in bytes. The default is 512 MiB.</summary>
    public int MaxInputBytes { get; set; } = 512 << 20;
    /// <summary>Gets or sets the maximum number of JPEG scans, including when pixel fallback is enabled. The default is 500.</summary>
    public int MaxScans { get; set; } = 500;

    internal JxlJpegTranscodeOptions Snapshot()
    {
        JxlJpegTranscodeOptions copy = (JxlJpegTranscodeOptions)this.MemberwiseClone();
        if (copy.Effort < 1 || copy.Effort > (copy.AllowExpertEffort ? 11 : 10)) { throw new ArgumentOutOfRangeException(nameof(this.Effort)); }
        if (copy.MaxPixels <= 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxPixels)); }
        if (copy.MaxInputBytes <= 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxInputBytes)); }
        if (copy.MaxScans <= 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxScans)); }
        return copy;
    }
}
