// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>JPEG XL image metadata and animation timing.</summary>
public sealed class JxlMetadata : IDeepCloneable
{
    /// <summary>Gets or sets the precision of each color sample.</summary>
    public int BitsPerSample { get; set; } = 8;
    /// <summary>Gets or sets the exponent precision, or zero for integer samples.</summary>
    public int ExponentBitsPerSample { get; set; }
    /// <summary>Gets or sets whether the image declares animation timing.</summary>
    public bool IsAnimated { get; set; }
    /// <summary>Gets or sets the numerator of the ticks-per-second ratio.</summary>
    public uint TicksPerSecondNumerator { get; set; } = 100;
    /// <summary>Gets or sets the denominator of the ticks-per-second ratio.</summary>
    public uint TicksPerSecondDenominator { get; set; } = 1;
    /// <summary>Gets or sets the playback loop count; zero denotes indefinite looping.</summary>
    public uint LoopCount { get; set; }
    /// <summary>Gets or sets whether the source uses a container.</summary>
    public bool HasContainer { get; set; }
    /// <summary>Gets or sets whether JPEG reconstruction data has been found.</summary>
    public bool HasJpegReconstruction { get; set; }
    /// <summary>Gets or sets the intensity target in nits.</summary>
    public float IntensityTarget { get; set; } = 255;
    /// <summary>Gets or sets the declared codestream level.</summary>
    public int Level { get; set; } = 5;
    /// <summary>Gets or sets the coded orientation, numbered from one through eight.</summary>
    public int Orientation { get; set; } = 1;
    /// <summary>Gets or sets the number of display frames, or null if frames have not been scanned.</summary>
    public int? FrameCount { get; set; }
    /// <summary>Gets or sets the JUMBF payload, when present.</summary>
    public byte[] Jumbf { get; set; }

    /// <inheritdoc/>
    public IDeepCloneable DeepClone()
    {
        JxlMetadata copy = (JxlMetadata)this.MemberwiseClone();
        copy.Jumbf = this.Jumbf == null ? null : (byte[])this.Jumbf.Clone();
        return copy;
    }
}
