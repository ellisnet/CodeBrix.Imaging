// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.Metadata;

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Identifies the storage type of decoded JPEG XL samples.</summary>
public enum JxlSampleType
{
    /// <summary>Unnormalized integer samples, stored without converting through floating point.</summary>
    Integer,
    /// <summary>IEEE binary32 samples, including values outside the normalized interval.</summary>
    FloatingPoint,
}

/// <summary>Identifies the color coordinates in raw JPEG XL planes.</summary>
public enum JxlRawColorSpace
{
    /// <summary>Red, green and blue in the image's color encoding.</summary>
    Rgb,
    /// <summary>A gray channel in the image's color encoding.</summary>
    Grayscale,
    /// <summary>JPEG XL XYB coordinates before the color transform.</summary>
    Xyb,
    /// <summary>YCbCr coordinates in Cb, Y, Cr plane order.</summary>
    YCbCr,
    /// <summary>Cyan, magenta and yellow, with black represented as an extra channel.</summary>
    Cmyk,
}

/// <summary>Identifies a JPEG XL extra channel.</summary>
public enum JxlExtraChannelType
{
    /// <summary>Transparency.</summary>
    Alpha = 0,
    /// <summary>Depth.</summary>
    Depth = 1,
    /// <summary>A spot-color coverage channel.</summary>
    SpotColor = 2,
    /// <summary>A selection mask.</summary>
    SelectionMask = 3,
    /// <summary>The black channel of a CMYK image.</summary>
    Black = 4,
    /// <summary>A color filter array channel.</summary>
    ColorFilterArray = 5,
    /// <summary>Thermal measurements.</summary>
    Thermal = 6,
    /// <summary>An application-defined optional channel.</summary>
    Optional = 15,
    /// <summary>An application-defined nonoptional channel.</summary>
    NonOptional = 16,
}

/// <summary>Describes an extra channel supplied to the raw encoder.</summary>
public sealed class JxlExtraChannelInfo
{
    /// <summary>Gets the channel's role.</summary>
    public JxlExtraChannelType Type { get; init; } = JxlExtraChannelType.Alpha;
    /// <summary>Gets its optional name, limited to 1071 UTF-8 bytes without unpaired surrogates.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets whether alpha is associated with the color values.</summary>
    public bool AlphaAssociated { get; init; }
    /// <summary>Gets the spot color's RGB values and solidity.</summary>
    public Vector4 SpotColor { get; init; }
    /// <summary>Gets the power of two by which this plane's dimensions are reduced.</summary>
    public int DimensionShift { get; init; }
    /// <summary>Gets the color filter array channel identifier.</summary>
    public int ColorFilterArrayChannel { get; init; } = 1;

    internal JxlExtraChannel ToHeader(JxlBitDepth depth)
    {
        if (!Enum.IsDefined(this.Type) || this.DimensionShift is < 0 or > 6 || this.ColorFilterArrayChannel is < 0 or > 274)
        { throw new ArgumentException("Invalid JPEG XL extra-channel description."); }
        JxlName.GetByteCount(this.Name);
        for (int c = 0; c < 4; c++)
        { if (!float.IsFinite(this.SpotColor[c]) || !Half.IsFinite((Half)this.SpotColor[c])) { throw new ArgumentException("Spot colors must have finite binary16 values."); } }
        return new() { Type = (uint)this.Type, Name = this.Name, BitDepth = depth, DimShift = (uint)this.DimensionShift,
            AlphaAssociated = this.AlphaAssociated, SpotColor = [this.SpotColor.X, this.SpotColor.Y, this.SpotColor.Z, this.SpotColor.W], CfaChannel = (uint)this.ColorFilterArrayChannel };
    }
}

/// <summary>A sample plane owned by a <see cref="JxlRawImage"/>.</summary>
public sealed class JxlRawPlane : IDisposable
{
    private Buffer2D<int> integers;
    private Buffer2D<float> floats;
    private bool disposed;

    internal JxlRawPlane(Buffer2D<int> integers, Buffer2D<float> floats, JxlBitDepth depth, int colorIndex, JxlExtraChannel extra, int dimensionShift = 0)
    {
        this.integers = integers;
        this.floats = floats;
        this.Width = integers?.Width ?? floats.Width;
        this.Height = integers?.Height ?? floats.Height;
        this.SampleType = integers == null ? JxlSampleType.FloatingPoint : JxlSampleType.Integer;
        this.BitsPerSample = (int)depth.Bits;
        this.ExponentBitsPerSample = (int)depth.ExponentBits;
        this.ColorChannelIndex = colorIndex;
        this.ExtraChannelType = extra == null ? null : (JxlExtraChannelType)extra.Type;
        this.Name = extra?.Name ?? string.Empty;
        this.AlphaAssociated = extra?.AlphaAssociated ?? false;
        this.SpotColor = extra == null ? default : new(extra.SpotColor[0], extra.SpotColor[1], extra.SpotColor[2], extra.SpotColor[3]);
        this.DimensionShift = dimensionShift;
        this.ColorFilterArrayChannel = (int)(extra?.CfaChannel ?? 1);
    }

    /// <summary>Copies row-major, unnormalized integer samples into an owned plane.</summary>
    /// <remarks>Color planes are numbered from zero. Supplying an extra-channel description instead creates an extra plane.</remarks>
    public static JxlRawPlane FromIntegers(int width, int height, ReadOnlySpan<int> samples, int bitsPerSample,
        int colorChannelIndex = 0, JxlExtraChannelInfo extraChannel = null, Configuration configuration = null)
    {
        ValidateSamples(width, height, samples.Length, colorChannelIndex);
        if (bitsPerSample is < 1 or > 31) { throw new ArgumentOutOfRangeException(nameof(bitsPerSample)); }
        JxlBitDepth depth = new((uint)bitsPerSample, 0); JxlExtraChannel extra = extraChannel?.ToHeader(depth);
        Buffer2D<int> buffer = (configuration ?? Configuration.Default).MemoryAllocator.Allocate2D<int>(width, height);
        try
        {
            for (int y = 0; y < height; y++) { samples.Slice(y * width, width).CopyTo(buffer.DangerousGetRowSpan(y)); }
            return new(buffer, null, depth, extra == null ? colorChannelIndex : -1, extra, (int)(extra?.DimShift ?? 0));
        }
        catch { buffer.Dispose(); throw; }
    }

    /// <summary>Copies row-major IEEE binary32 samples into an owned plane, retaining their bit patterns.</summary>
    /// <remarks>Lossless encoding preserves infinities, signed zero and NaN payloads. Perceptual encoding requires finite color samples.</remarks>
    public static JxlRawPlane FromFloats(int width, int height, ReadOnlySpan<float> samples,
        int colorChannelIndex = 0, JxlExtraChannelInfo extraChannel = null, Configuration configuration = null)
    {
        ValidateSamples(width, height, samples.Length, colorChannelIndex);
        JxlBitDepth depth = new(32, 8); JxlExtraChannel extra = extraChannel?.ToHeader(depth);
        Buffer2D<float> buffer = (configuration ?? Configuration.Default).MemoryAllocator.Allocate2D<float>(width, height);
        try
        {
            for (int y = 0; y < height; y++) { samples.Slice(y * width, width).CopyTo(buffer.DangerousGetRowSpan(y)); }
            return new(null, buffer, depth, extra == null ? colorChannelIndex : -1, extra, (int)(extra?.DimShift ?? 0));
        }
        catch { buffer.Dispose(); throw; }
    }

    private static void ValidateSamples(int width, int height, int count, int colorChannelIndex)
    {
        if (width <= 0 || height <= 0 || (long)width * height != count) { throw new ArgumentException("Sample count must match positive plane dimensions."); }
        if (colorChannelIndex is < 0 or > 2) { throw new ArgumentOutOfRangeException(nameof(colorChannelIndex)); }
    }

    /// <summary>Gets the number of samples in each row.</summary>
    public int Width { get; }
    /// <summary>Gets the number of rows.</summary>
    public int Height { get; }
    /// <summary>Gets the sample storage type.</summary>
    public JxlSampleType SampleType { get; }
    /// <summary>Gets the represented sample precision; rendered floating-point planes use 32.</summary>
    public int BitsPerSample { get; }
    /// <summary>Gets the exponent precision, or zero for integer samples.</summary>
    public int ExponentBitsPerSample { get; }
    /// <summary>Gets the zero-based color-channel index, or minus one for an extra channel.</summary>
    public int ColorChannelIndex { get; }
    /// <summary>Gets the extra-channel role, or null for a color channel.</summary>
    public JxlExtraChannelType? ExtraChannelType { get; }
    /// <summary>Gets the encoded extra-channel name.</summary>
    public string Name { get; }
    /// <summary>Gets whether an alpha channel is associated with the color samples.</summary>
    public bool AlphaAssociated { get; }
    /// <summary>Gets the spot channel's red, green, blue and solidity values.</summary>
    public Vector4 SpotColor { get; }
    /// <summary>Gets the encoded power-of-two reduction of an input extra plane; decoded rendered planes have full resolution.</summary>
    public int DimensionShift { get; }
    /// <summary>Gets the color filter array channel identifier.</summary>
    public int ColorFilterArrayChannel { get; }

    /// <summary>Returns an unnormalized integer row. The span is valid until this plane is disposed.</summary>
    public ReadOnlySpan<int> GetIntegerRowSpan(int row)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (this.integers == null) { throw new InvalidOperationException("This plane stores floating-point samples."); }
        if ((uint)row >= this.Height) { throw new ArgumentOutOfRangeException(nameof(row)); }
        return this.integers.DangerousGetRowSpan(row);
    }

    /// <summary>Returns a binary32 row. The span is valid until this plane is disposed.</summary>
    public ReadOnlySpan<float> GetFloatRowSpan(int row)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (this.floats == null) { throw new InvalidOperationException("This plane stores integer samples."); }
        if ((uint)row >= this.Height) { throw new ArgumentOutOfRangeException(nameof(row)); }
        return this.floats.DangerousGetRowSpan(row);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.integers?.Dispose(); this.floats?.Dispose();
        this.integers = null; this.floats = null; this.disposed = true;
    }
}

/// <summary>A complete canvas or uncropped decoded JPEG XL layer with all extra channels.</summary>
public sealed class JxlRawFrame : IDisposable
{
    /// <summary>Creates a frame and takes ownership of its planes when construction succeeds.</summary>
    /// <remarks>Supply color planes in order followed by extra channels. Metadata is copied; the frame's metadata can subsequently be edited.</remarks>
    public JxlRawFrame(IReadOnlyList<JxlRawPlane> planes, JxlRawColorSpace colorSpace = JxlRawColorSpace.Rgb, JxlFrameMetadata metadata = null)
    {
        ArgumentNullException.ThrowIfNull(planes);
        if (!Enum.IsDefined(colorSpace)) { throw new ArgumentOutOfRangeException(nameof(colorSpace)); }
        int colors = colorSpace == JxlRawColorSpace.Grayscale ? 1 : 3;
        if (planes.Count < colors || planes.Count > colors + 256) { throw new ArgumentException("Invalid JPEG XL plane count.", nameof(planes)); }
        JxlRawPlane[] copy = new JxlRawPlane[planes.Count];
        for (int c = 0; c < copy.Length; c++)
        {
            JxlRawPlane plane = planes[c]; ArgumentNullException.ThrowIfNull(plane);
            if (c < colors ? plane.ColorChannelIndex != c || plane.ExtraChannelType != null : plane.ExtraChannelType == null)
            { throw new ArgumentException("Planes must contain ordered color channels followed by extra channels.", nameof(planes)); }
            if (c < colors && (plane.Width != planes[0].Width || plane.Height != planes[0].Height ||
                plane.BitsPerSample != planes[0].BitsPerSample || plane.ExponentBitsPerSample != planes[0].ExponentBitsPerSample))
            { throw new ArgumentException("Color planes must have matching dimensions and precision.", nameof(planes)); }
            copy[c] = plane;
        }
        this.Planes = Array.AsReadOnly(copy); this.ColorChannelCount = colors; this.ColorSpace = colorSpace;
        this.Width = copy[0].Width; this.Height = copy[0].Height; this.Metadata = (JxlFrameMetadata)metadata?.DeepClone() ?? new();
    }

    internal JxlRawFrame(JxlRawPlane[] planes, JxlFrameMetadata metadata, int colorChannels, int width, int height, JxlRawColorSpace colorSpace)
    {
        this.Planes = Array.AsReadOnly(planes); this.Metadata = metadata;
        this.ColorChannelCount = colorChannels; this.Width = width; this.Height = height;
        this.ColorSpace = colorSpace;
    }
    /// <summary>Gets the planes in color-channel order followed by extra-channel order.</summary>
    public IReadOnlyList<JxlRawPlane> Planes { get; }
    /// <summary>Gets the timing, crop and blending metadata.</summary>
    public JxlFrameMetadata Metadata { get; }
    /// <summary>Gets the number of color planes.</summary>
    public int ColorChannelCount { get; }
    /// <summary>Gets the coordinates represented by the color planes.</summary>
    public JxlRawColorSpace ColorSpace { get; }
    /// <summary>Gets explicit features to add when this frame is encoded.</summary>
    public JxlRawFrameEncoding Encoding { get; } = new();
    /// <summary>Gets the canvas or layer width.</summary>
    public int Width { get; }
    /// <summary>Gets the canvas or layer height.</summary>
    public int Height { get; }
    /// <inheritdoc/>
    public void Dispose() { foreach (JxlRawPlane plane in this.Planes) { plane.Dispose(); } }
}

/// <summary>Owns decoded JPEG XL canvases or layers without reducing their extra channels or integer precision.</summary>
/// <remarks>Raw color and extra channels are retained separately, without spot-color painting or alpha unassociation.</remarks>
public sealed class JxlRawImage : IDisposable
{
    /// <summary>Creates an image and takes ownership of its frames when construction succeeds.</summary>
    /// <remarks>Frames represent coded layers. Their offsets, blends and reference slots are honored by <see cref="JxlEncoder.EncodeRaw"/>.</remarks>
    public JxlRawImage(int width, int height, IReadOnlyList<JxlRawFrame> frames, ImageMetadata metadata = null)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (width is <= 0 or > 1 << 30 || height is <= 0 or > 1 << 30 || (long)width * height > 1L << 40)
        { throw new ArgumentOutOfRangeException(nameof(width)); }
        if (frames.Count == 0) { throw new ArgumentException("At least one frame is required.", nameof(frames)); }
        JxlRawFrame[] copy = new JxlRawFrame[frames.Count];
        for (int i = 0; i < copy.Length; i++) { copy[i] = frames[i]; ArgumentNullException.ThrowIfNull(copy[i]); }
        this.Width = width; this.Height = height; this.Frames = Array.AsReadOnly(copy); this.Metadata = metadata?.DeepClone() ?? new();
    }

    internal JxlRawImage(JxlRawFrame[] frames, ImageMetadata metadata, int width, int height, bool coalesced)
    { this.Frames = Array.AsReadOnly(frames); this.Metadata = metadata; this.Width = width; this.Height = height; this.IsCoalesced = coalesced; }
    /// <summary>Gets the image metadata and source orientation.</summary>
    public ImageMetadata Metadata { get; }
    /// <summary>Gets the image canvas width.</summary>
    public int Width { get; }
    /// <summary>Gets the image canvas height.</summary>
    public int Height { get; }
    /// <summary>Gets whether frames have been blended into complete display canvases.</summary>
    public bool IsCoalesced { get; }
    /// <summary>Gets optional controls for encoded color, opsin, upsampling and display metadata.</summary>
    public JxlRawImageEncoding Encoding { get; } = new();
    /// <summary>Gets displayed canvases, or all coded layers when coalescing is disabled.</summary>
    public IReadOnlyList<JxlRawFrame> Frames { get; }
    /// <inheritdoc/>
    public void Dispose() { foreach (JxlRawFrame frame in this.Frames) { frame.Dispose(); } }
}
