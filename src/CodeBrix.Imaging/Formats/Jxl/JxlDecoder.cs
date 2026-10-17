// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.PixelFormats;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.Metadata;

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Reads JPEG XL image information and configures image decoding.</summary>
public sealed class JxlDecoder : IImageDecoder, IImageInfoDetector
{
    /// <summary>Gets or sets whether ancillary metadata is discarded.</summary>
    public bool IgnoreMetadata { get; set; }
    /// <summary>Gets or sets whether image dimensions and output use display orientation.</summary>
    public bool ApplyOrientation { get; set; } = true;
    /// <summary>Gets or sets whether spot-color channels are painted into displayed RGB pixels.</summary>
    public bool RenderSpotColors { get; set; } = true;
    /// <summary>Gets or sets the output color encoding. The default selects sRGB for XYB and the original encoding otherwise.</summary>
    public JxlTargetColor TargetColor { get; set; }
    /// <summary>Gets or sets the peak display luminance in nits for HDR-to-sRGB tone mapping. Zero preserves the source range.</summary>
    /// <remarks>Applies only to sRGB and linear-sRGB output. For example, use 255 for an SDR display. Floating-point output remains unclamped when this is zero.</remarks>
    public float TargetDisplayLuminance { get; set; }
    /// <summary>Gets or sets whether to decode all displayed animation frames or only the first.</summary>
    public FrameDecodingMode DecodingMode { get; set; } = FrameDecodingMode.All;
    /// <summary>Gets or sets the maximum number of displayed frames to decode.</summary>
    public uint MaxFrames { get; set; } = uint.MaxValue;
    /// <summary>Gets or sets whether <see cref="DecodeRaw"/> returns complete display canvases.</summary>
    /// <remarks>False returns all uncropped coded layers in coded orientation. Ordinary image decoding always returns complete canvases.</remarks>
    public bool Coalesce { get; set; } = true;
    /// <summary>Gets or sets the maximum canvas or frame pixel count, checked before pixel allocation.</summary>
    public long MaxPixels { get; set; } = 100_000_000;
    /// <summary>Gets or sets the maximum combined pixel count of frames retained by image or raw decoding.</summary>
    /// <remarks>The default is 100 million pixels. Each coalesced frame counts its full canvas; uncoalesced raw frames count their layer dimensions.
    /// Streaming animation does not retain the full animation and is instead bounded by its queue and <see cref="MaxDecodedPixels"/>.</remarks>
    public long MaxTotalPixels { get; set; } = 100_000_000;
    /// <summary>Gets or sets the cumulative pixel-work limit per decode or animation playback pass, including hidden and reference frames.</summary>
    /// <remarks>The default is one billion pixels. A normal frame counts the larger of its layer and display canvas.
    /// Patch destinations additionally count their clipped area at coded color resolution, with a minimum of one pixel per target.
    /// This is separate from retained output limits.</remarks>
    public long MaxDecodedPixels { get; set; } = 1_000_000_000;
    /// <summary>Gets or sets the maximum coded frame count per identify, decode or animation playback pass, including previews and hidden frames.</summary>
    /// <remarks>The default is 10,000. Exceeding this limit rejects the input; <see cref="MaxFrames"/> instead stops after the requested displayed frames.</remarks>
    public uint MaxCodedFrames { get; set; } = 10_000;
    /// <summary>Gets or sets the maximum simultaneous Modular integer-plane storage per frame, shared by all parallel group workspaces.</summary>
    /// <remarks>The default is 2 GiB, enough for a canvas at the default <see cref="MaxPixels"/> with three color planes and one extra channel
    /// (VarDCT frames keep their quantized coefficients as integer planes). This also covers Modular data inside VarDCT frames. Allocator
    /// and per-plane pixel limits still apply; compressed sections, floating-point rendering buffers and retained output are governed separately.</remarks>
    public long MaxModularBytes { get; set; } = 2048L * 1024 * 1024;
    /// <summary>Gets or sets the maximum retained metadata size in bytes.</summary>
    public long MaxMetadataBytes { get; set; } = 64 * 1024 * 1024;

    /// <inheritdoc/>
    public IImageInfo Identify(Configuration configuration, Stream stream, CancellationToken cancellationToken)
    {
        Validate(configuration, stream);
        this.ValidateOptions();
        try { return new JxlDecoderCore(this).Identify(configuration, stream, cancellationToken); }
        catch (EndOfStreamException ex) { throw new InvalidImageContentException("Truncated JPEG XL image.", ex); }
        catch (InvalidMemoryOperationException ex) { throw new InvalidImageContentException("JPEG XL image exceeds the configured allocator limits.", ex); }
    }

    /// <inheritdoc/>
    public Image<TPixel> Decode<TPixel>(Configuration configuration, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Validate(configuration, stream);
        this.ValidateOptions();
        cancellationToken.ThrowIfCancellationRequested();
        try { return new JxlDecoderCore(this).Decode<TPixel>(configuration, stream, cancellationToken); }
        catch (EndOfStreamException ex) { throw new InvalidImageContentException("Truncated JPEG XL image.", ex); }
        catch (InvalidMemoryOperationException ex) { throw new InvalidImageContentException("JPEG XL image exceeds the configured allocator limits.", ex); }
    }

    /// <inheritdoc/>
    public Image Decode(Configuration configuration, Stream stream, CancellationToken cancellationToken) =>
        this.Decode<Rgba32>(configuration, stream, cancellationToken);

    /// <summary>Decodes typed sample planes, preserving all extra channels and exact untransformed integers.</summary>
    /// <param name="configuration">The allocator and decoder configuration.</param>
    /// <param name="stream">The readable JPEG XL input, left open on return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An owned image whose planes remain valid until it is disposed.</returns>
    /// <remarks>Rendering operations that require fractional arithmetic produce floating-point planes. Spot channels and associated alpha remain separate.</remarks>
    public JxlRawImage DecodeRaw(Configuration configuration, Stream stream, CancellationToken cancellationToken = default)
    {
        Validate(configuration, stream);
        this.ValidateOptions();
        cancellationToken.ThrowIfCancellationRequested();
        try { return new JxlDecoderCore(this).DecodeRaw(configuration, stream, cancellationToken); }
        catch (EndOfStreamException ex) { throw new InvalidImageContentException("Truncated JPEG XL image.", ex); }
        catch (InvalidMemoryOperationException ex) { throw new InvalidImageContentException("JPEG XL image exceeds the configured allocator limits.", ex); }
    }

    private static void Validate(Configuration configuration, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) { throw new ArgumentException("Stream must be readable.", nameof(stream)); }
    }

    internal JxlDecoder CopyValidatedOptions()
    {
        this.ValidateOptions();
        return (JxlDecoder)this.MemberwiseClone();
    }

    private void ValidateOptions()
    {
        if (this.TargetColor is not (JxlTargetColor.Auto or JxlTargetColor.Original or JxlTargetColor.Srgb or JxlTargetColor.LinearSrgb)) { throw new ArgumentOutOfRangeException(nameof(this.TargetColor)); }
        if (!float.IsFinite(this.TargetDisplayLuminance) || this.TargetDisplayLuminance < 0) { throw new ArgumentOutOfRangeException(nameof(this.TargetDisplayLuminance)); }
        if (this.MaxPixels <= 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxPixels)); }
        if (this.MaxTotalPixels <= 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxTotalPixels)); }
        if (this.MaxDecodedPixels <= 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxDecodedPixels)); }
        if (this.MaxCodedFrames == 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxCodedFrames)); }
        if (this.MaxModularBytes <= 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxModularBytes)); }
        if (this.MaxFrames == 0) { throw new ArgumentOutOfRangeException(nameof(this.MaxFrames)); }
        if (this.DecodingMode is not (FrameDecodingMode.All or FrameDecodingMode.First)) { throw new ArgumentOutOfRangeException(nameof(this.DecodingMode)); }
        if (this.MaxMetadataBytes < 0 || this.MaxMetadataBytes > int.MaxValue) { throw new ArgumentOutOfRangeException(nameof(this.MaxMetadataBytes)); }
    }
}
