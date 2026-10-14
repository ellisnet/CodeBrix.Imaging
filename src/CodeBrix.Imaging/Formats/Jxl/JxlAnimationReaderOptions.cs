// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Identifies the packed byte order of a streamed JPEG XL frame.</summary>
public enum JxlPixelFormat
{
    /// <summary>Red, green, blue and straight alpha bytes.</summary>
    Rgba32,
    /// <summary>Blue, green, red and straight alpha bytes.</summary>
    Bgra32,
}

/// <summary>Configures bounded JPEG XL animation playback.</summary>
public sealed class JxlAnimationReaderOptions
{
    /// <summary>Gets or sets the number of completed frames that may wait in the playback queue.</summary>
    public int DecodeAhead { get; set; } = 3;
    /// <summary>Gets or sets the packed output byte order.</summary>
    public JxlPixelFormat PixelFormat { get; set; } = JxlPixelFormat.Bgra32;
    /// <summary>Gets or sets the decoding and color options, copied when the reader opens.</summary>
    public JxlDecoder Decoder { get; set; } = new() { TargetColor = JxlTargetColor.Srgb };
    /// <summary>Gets or sets whether playback repeats according to the encoded loop count. Zero encoded loops means unlimited playback.</summary>
    public bool Loop { get; set; } = true;
    /// <summary>Gets or sets whether disposing the reader leaves its input stream open.</summary>
    public bool LeaveOpen { get; set; } = true;
    /// <summary>Gets or sets the compressed-input cache limit for looping a forward-only stream. Seekable streams need no input cache.</summary>
    public int MaxBufferedInputBytes { get; set; } = 64 * 1024 * 1024;
}
