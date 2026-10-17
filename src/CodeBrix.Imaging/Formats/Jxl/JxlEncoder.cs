// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Configures managed JPEG XL encoding.</summary>
/// <remarks>
/// Before writing output, the encoder compares a conservative estimate of its working storage
/// with the configured allocator's <see cref="Memory.MemoryAllocatorOptions.AllocationLimitMegabytes"/>.
/// The estimate includes sample planes, managed tokens, histograms, candidate streams and metadata.
/// It can reject compressible images whose actual storage would be smaller; increasing the allocator
/// limit permits the requested encoding without reducing its effort or global search.
/// Caller-owned images and destination storage, runtime overhead and unrelated operations are not
/// a part of this estimate. This is admission control, not a process-wide memory cap.
/// </remarks>
public sealed class JxlEncoder : IImageEncoder
{
    /// <summary>Gets or sets whether all sample values must be preserved exactly.</summary>
    public bool Lossless { get; set; }
    /// <summary>Gets or sets the perceptual distance; zero requests lossless output and positive values below 0.05 use 0.05.</summary>
    public float Distance { get; set; } = 1;
    /// <summary>Gets or sets a quality from zero through 100, overriding <see cref="Distance"/> when present.</summary>
    public int? Quality { get; set; }
    /// <summary>Gets or sets the encoding effort from one through ten.</summary>
    public int Effort { get; set; } = 7;
    /// <summary>Gets or sets whether lossless effort eleven may be selected.</summary>
    public bool AllowExpertEffort { get; set; }
    /// <summary>Gets or sets the Brotli metadata compression quality from zero through eleven.</summary>
    public int BrotliEffort { get; set; } = 6;
    /// <summary>Gets or sets the progressive representation.</summary>
    public JxlProgressiveMode Progressive { get; set; }
    /// <summary>Gets or sets whether to force Modular encoding; null selects automatically.</summary>
    public bool? Modular { get; set; }
    /// <summary>Gets or sets how color values behind fully transparent pixels are handled.</summary>
    public JxlTransparentColorMode TransparentColorMode { get; set; } = JxlTransparentColorMode.Preserve;
    /// <summary>Gets or sets the simulated photon-noise ISO; zero disables it.</summary>
    public float PhotonNoiseIso { get; set; }
    /// <summary>Gets or sets whether to estimate a noise synthesis model from lossy VarDCT input.</summary>
    /// <remarks>Disabled by default, matching the reference encoder at supported distances. Explicit raw noise and photon noise take precedence.</remarks>
    public bool NoiseModeling { get; set; }
    /// <summary>Gets or sets whether repeated-content patches are searched when encoding displayed pixel images.</summary>
    /// <remarks>Null enables the search at efforts five and higher. Raw images use their explicit patch descriptions.</remarks>
    public bool? Patches { get; set; }
    /// <summary>Gets or sets whether isolated dots are encoded as additive patches in displayed pixel images.</summary>
    /// <remarks>Null enables detection at efforts seven and higher and distances of at least three. Text patches take precedence when found.</remarks>
    public bool? Dots { get; set; }
    /// <summary>Gets or sets the color resampling factor (one, two, four, or eight).</summary>
    public int Resampling { get; set; } = 1;
    /// <summary>Gets or sets the preference for faster decoding from zero through four.</summary>
    public int FasterDecoding { get; set; }
    /// <summary>Gets or sets whether to write a container; null selects one when required by metadata.</summary>
    public bool? UseContainer { get; set; }
    /// <summary>Gets or sets whether to compress supported metadata boxes with Brotli.</summary>
    public bool CompressMetadataBoxes { get; set; } = true;
    /// <summary>Gets or sets an enumerated color encoding that describes the image's samples; null (the default) tags them as sRGB (or gray), or uses the image's ICC profile.</summary>
    /// <remarks>
    /// Imaging's convention is that samples carry the encoding of the image's color profile, which is sRGB when there is none - floating-point
    /// pixels such as <see cref="RgbaVector"/> included. libjxl instead treats floating-point input as linear sRGB. To follow that convention,
    /// set this to <see cref="JxlColorEncodingInfo.LinearSrgb"/>: the samples are written unchanged and the file says they are linear. Any other
    /// enumerated encoding (BT.2020, PQ, HLG, ...) works the same way, for any pixel type. Decode such a file with
    /// <see cref="JxlTargetColor.LinearSrgb"/> to get linear values back. An image that carries an ICC profile cannot be combined with this option.
    /// Applies to <see cref="Encode{TPixel}"/> and <see cref="EncodeAsync{TPixel}"/>; <see cref="EncodeRaw"/> uses <see cref="JxlRawImageEncoding.ColorEncoding"/>.
    /// </remarks>
    public JxlColorEncodingInfo ColorEncoding { get; set; }

    internal JxlColorEncodingInfo ColorEncodingSnapshot { get; private set; }

    /// <inheritdoc/>
    public void Encode<TPixel>(Image<TPixel> image, Stream stream)
        where TPixel : unmanaged, IPixel<TPixel> => this.EncodeCore(image, stream, default);

    /// <inheritdoc/>
    public Task EncodeAsync<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ValidateInput(image, stream, cancellationToken);
        return new JxlEncoderCore(this.Snapshot()).EncodeAsync(image, stream, cancellationToken);
    }

    /// <summary>Encodes raw color planes, extra channels and explicitly described layers.</summary>
    /// <remarks>The image and output stream remain owned by the caller.</remarks>
    public void EncodeRaw(JxlRawImage image, Stream stream, Configuration configuration = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image); ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite) { throw new ArgumentException("Stream must be writable.", nameof(stream)); }
        cancellationToken.ThrowIfCancellationRequested();
        new JxlRawEncoder(this.Snapshot(), configuration ?? Configuration.Default).Encode(image, stream, cancellationToken);
    }

    private void EncodeCore<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ValidateInput(image, stream, cancellationToken);
        new JxlEncoderCore(this.Snapshot()).Encode(image, stream, cancellationToken);
    }

    private static void ValidateInput<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite) { throw new ArgumentException("Stream must be writable.", nameof(stream)); }
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal bool IsLossless => this.Lossless || (this.Quality.HasValue ? this.Quality == 100 : this.Distance == 0);

    // libjxl encode.cc, ef67fde2ec16d52e644c5a0969230b9a85e3eb31,
    // JxlEncoderDistanceFromQuality (BSD-3-Clause plus PATENTS).
    // Copyright (c) the JPEG XL Project Authors. All rights reserved.
    internal float EffectiveDistance => this.IsLossless ? 0 : !this.Quality.HasValue ? Math.Max(.05f, this.Distance)
        : this.Quality >= 30 ? (float)(.1 + (100 - this.Quality.Value) * .09)
        : (float)(53.0 / 3000 * this.Quality.Value * this.Quality.Value - 23.0 / 20 * this.Quality.Value + 25);

    private JxlEncoder Snapshot()
    {
        JxlEncoder copy = (JxlEncoder)this.MemberwiseClone();
        copy.ColorEncodingSnapshot = copy.ColorEncoding?.Copy();
        copy.ColorEncodingSnapshot?.ToHeader(0);
        if (!float.IsFinite(copy.Distance) || copy.Distance is < 0 or > 25) { throw new ArgumentOutOfRangeException(nameof(this.Distance)); }
        if (copy.Quality is < 0 or > 100) { throw new ArgumentOutOfRangeException(nameof(this.Quality)); }
        if (copy.Effort < 1 || copy.Effort > (copy.AllowExpertEffort && copy.IsLossless ? 11 : 10))
        { throw new ArgumentOutOfRangeException(nameof(this.Effort)); }
        if (copy.BrotliEffort is < 0 or > 11) { throw new ArgumentOutOfRangeException(nameof(this.BrotliEffort)); }
        if (copy.FasterDecoding is < 0 or > 4) { throw new ArgumentOutOfRangeException(nameof(this.FasterDecoding)); }
        if (copy.Resampling is not (1 or 2 or 4 or 8)) { throw new ArgumentOutOfRangeException(nameof(this.Resampling)); }
        if (copy.Resampling != 1 && copy.Progressive is JxlProgressiveMode.Dc or JxlProgressiveMode.Full)
        { throw new ArgumentException("Low-frequency progression cannot be combined with resampling."); }
        if (!float.IsFinite(copy.PhotonNoiseIso) || copy.PhotonNoiseIso < 0) { throw new ArgumentOutOfRangeException(nameof(this.PhotonNoiseIso)); }
        if (copy.NoiseModeling && (copy.IsLossless || copy.Modular == true))
        { throw new ArgumentException("Estimated noise requires lossy VarDCT encoding.", nameof(this.NoiseModeling)); }
        if (!Enum.IsDefined(copy.Progressive)) { throw new ArgumentOutOfRangeException(nameof(this.Progressive)); }
        if (!Enum.IsDefined(copy.TransparentColorMode)) { throw new ArgumentOutOfRangeException(nameof(this.TransparentColorMode)); }
        if (copy.IsLossless && (copy.Resampling != 1 || copy.PhotonNoiseIso != 0 || copy.Modular == false))
        { throw new ArgumentException("Lossless JPEG XL requires Modular coding, full resolution and no added photon noise."); }
        return copy;
    }
}

/// <summary>Specifies progressive JPEG XL encoding.</summary>
public enum JxlProgressiveMode
{
    /// <summary>Encodes one full-resolution pass.</summary>
    None,
    /// <summary>Encodes a progressive low-frequency representation.</summary>
    Dc,
    /// <summary>Encodes progressive high-frequency passes.</summary>
    Ac,
    /// <summary>Encodes both low- and high-frequency progression.</summary>
    Full,
}

/// <summary>Specifies treatment of colors behind fully transparent pixels.</summary>
public enum JxlTransparentColorMode
{
    /// <summary>Allows those colors to be cleared for compression.</summary>
    Clear,
    /// <summary>Preserves those colors.</summary>
    Preserve,
}
