// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Controls resource limits when reconstructing an original JPEG from JPEG XL.</summary>
public sealed class JxlJpegReconstructionOptions
{
    /// <summary>Gets or sets the maximum image or frame pixel count. The default is 100 million.</summary>
    public long MaxPixels { get; set; } = 100_000_000;
    /// <summary>Gets or sets the metadata byte budget, including JPEG reconstruction tables and payload. The default is 64 MiB.</summary>
    public int MaxMetadataBytes { get; set; } = 64 << 20;
    /// <summary>Gets or sets the shared integer-plane storage limit, including temporary group workspaces. The default is 2 GiB, enough for
    /// the coefficients of a 4:4:4 JPEG at the default <see cref="MaxPixels"/>.</summary>
    public long MaxModularBytes { get; set; } = 2048L << 20;
    /// <summary>Gets or sets the maximum number of JPEG scans. The default is 500.</summary>
    public int MaxScans { get; set; } = 500;
    /// <summary>Gets or sets the cumulative number of 8-by-8 component blocks visited across JPEG scans. The default is 100 million.</summary>
    public long MaxScanBlocks { get; set; } = 100_000_000;
    /// <summary>Gets or sets the maximum number of JPEG bytes written to the destination. The default is 512 MiB.</summary>
    /// <remarks>This counts bytes produced by this call, independently of any existing destination content. A limit failure may leave partial output.</remarks>
    public long MaxOutputBytes { get; set; } = 512L << 20;

    internal JxlJpegReconstructionOptions Snapshot()
    {
        JxlJpegReconstructionOptions copy = (JxlJpegReconstructionOptions)this.MemberwiseClone();
        if (copy.MaxPixels <= 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxPixels)); }
        if (copy.MaxMetadataBytes < 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxMetadataBytes)); }
        if (copy.MaxModularBytes <= 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxModularBytes)); }
        if (copy.MaxScans <= 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxScans)); }
        if (copy.MaxScanBlocks <= 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxScanBlocks)); }
        if (copy.MaxOutputBytes <= 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxOutputBytes)); }
        return copy;
    }
}
