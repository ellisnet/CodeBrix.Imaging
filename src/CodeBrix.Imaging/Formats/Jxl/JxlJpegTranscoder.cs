// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Jpeg;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Transcodes JPEG XL reconstruction data without converting JPEG coefficients to pixels.</summary>
public static class JxlJpegTranscoder
{
    /// <summary>Recompresses a JPEG into JPEG XL while retaining its original bytes for reconstruction.</summary>
    /// <param name="jpeg">The readable JPEG source, left open.</param>
    /// <param name="jxl">The writable JPEG XL destination, left open.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static void JpegToJxl(Stream jpeg, Stream jxl, CancellationToken cancellationToken = default) =>
        JpegToJxl(Configuration.Default, jpeg, jxl, new JxlJpegTranscodeOptions(), cancellationToken);

    /// <summary>Recompresses a JPEG with explicit effort, fallback and input limits.</summary>
    /// <param name="jpeg">The readable JPEG source, left open.</param>
    /// <param name="jxl">The writable JPEG XL destination, left open.</param>
    /// <param name="options">The transcoding settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static void JpegToJxl(Stream jpeg, Stream jxl, JxlJpegTranscodeOptions options, CancellationToken cancellationToken = default) =>
        JpegToJxl(Configuration.Default, jpeg, jxl, options, cancellationToken);

    /// <summary>Recompresses a JPEG using the supplied allocator configuration.</summary>
    /// <param name="configuration">The image memory configuration.</param>
    /// <param name="jpeg">The readable JPEG source, left open.</param>
    /// <param name="jxl">The writable JPEG XL destination, left open.</param>
    /// <param name="options">The transcoding settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>Sequential and progressive 8-bit Huffman JPEGs with one or three components are supported.
    /// Unsupported coding processes throw unless pixel fallback is explicitly enabled. Cancellation or an output error may leave partial output.</remarks>
    public static void JpegToJxl(Configuration configuration, Stream jpeg, Stream jxl, JxlJpegTranscodeOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration); ArgumentNullException.ThrowIfNull(jpeg); ArgumentNullException.ThrowIfNull(jxl);
        ArgumentNullException.ThrowIfNull(options);
        if (!jpeg.CanRead) { throw new ArgumentException("The JPEG stream must be readable.", nameof(jpeg)); }
        if (!jxl.CanWrite) { throw new ArgumentException("The JPEG XL stream must be writable.", nameof(jxl)); }
        if (ReferenceEquals(jpeg, jxl)) { throw new ArgumentException("Input and output must be different streams.", nameof(jxl)); }
        JxlJpegTranscodeOptions settings = options.Snapshot(); cancellationToken.ThrowIfCancellationRequested();
        using JxlParallel.Scope parallel = JxlParallel.EncoderScope(configuration.MaxDegreeOfParallelism);
        byte[] input;
        using (MemoryStream buffer = new())
        {
            byte[] scratch = ArrayPool<byte>.Shared.Rent(65536);
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested(); int count = jpeg.Read(scratch, 0, scratch.Length);
                    if (count == 0) { break; }
                    if (buffer.Length + count > settings.MaxInputBytes) { JxlThrowHelper.Invalid("JPEG input exceeds MaxInputBytes."); }
                    Encoder.JxlEncoderPreflight.Require((buffer.Length + count) * 4, configuration.MemoryAllocator);
                    buffer.Write(scratch, 0, count);
                }
                input = buffer.ToArray();
            }
            finally { ArrayPool<byte>.Shared.Return(scratch); }
        }
        JxlJpegParser.ValidateScanCount(input, settings.MaxScans, cancellationToken);
        JxlJpegParser parsed;
        try { parsed = JxlJpegParser.Read(input, configuration.MemoryAllocator, settings.MaxPixels, cancellationToken, settings.MaxScans); }
        catch (NotSupportedException) when (settings.AllowPixelFallback)
        {
            using MemoryStream pixelsInput = new(input, false);
            IImageInfo info = Image.Identify(configuration, pixelsInput);
            if ((long)info.Width * info.Height > settings.MaxPixels) { JxlThrowHelper.Invalid("JPEG image exceeds MaxPixels."); }
            pixelsInput.Position = 0; cancellationToken.ThrowIfCancellationRequested();
            using Image<Rgba32> pixels = Image.Load<Rgba32>(configuration, pixelsInput);
            new JxlEncoderCore(new JxlEncoder { Lossless = true, Effort = settings.Effort, AllowExpertEffort = settings.AllowExpertEffort })
                .Encode(pixels, jxl, cancellationToken);
            return;
        }
        using (parsed) { JxlJpegEncoder.Write(jxl, parsed, configuration.MemoryAllocator, settings.Effort, cancellationToken, input.LongLength); }
    }

    /// <summary>Reconstructs the original JPEG bytes from a JPEG XL container with JPEG reconstruction data.</summary>
    /// <param name="jxl">The readable JPEG XL stream, left open.</param>
    /// <param name="jpeg">The writable JPEG destination, left open.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static void JxlToJpeg(Stream jxl, Stream jpeg, CancellationToken cancellationToken = default) =>
        JxlToJpeg(Configuration.Default, jxl, jpeg, cancellationToken);

    /// <summary>Reconstructs the original JPEG using the supplied allocator configuration.</summary>
    /// <param name="configuration">The image memory configuration.</param>
    /// <param name="jxl">The readable JPEG XL stream, left open.</param>
    /// <param name="jpeg">The writable JPEG destination, left open.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>The source must contain a reconstructible VarDCT frame and a jbrd box. Invalid input or cancellation may leave partial output.</remarks>
    public static void JxlToJpeg(Configuration configuration, Stream jxl, Stream jpeg, CancellationToken cancellationToken = default)
        => JxlToJpeg(configuration, jxl, jpeg, new JxlJpegReconstructionOptions(), cancellationToken);

    /// <summary>Reconstructs the original JPEG using explicit resource limits.</summary>
    /// <param name="jxl">The readable JPEG XL source, left open.</param>
    /// <param name="jpeg">The writable JPEG destination, left open.</param>
    /// <param name="options">The reconstruction limits.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static void JxlToJpeg(Stream jxl, Stream jpeg, JxlJpegReconstructionOptions options, CancellationToken cancellationToken = default)
        => JxlToJpeg(Configuration.Default, jxl, jpeg, options, cancellationToken);

    /// <summary>Reconstructs the original JPEG with explicit resource limits and allocator configuration.</summary>
    /// <param name="configuration">The image memory configuration.</param>
    /// <param name="jxl">The readable JPEG XL source, left open.</param>
    /// <param name="jpeg">The writable JPEG destination, left open.</param>
    /// <param name="options">The reconstruction limits.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>Invalid input, a resource limit, cancellation or an output error may leave partial output. Neither stream is closed.</remarks>
    public static void JxlToJpeg(Configuration configuration, Stream jxl, Stream jpeg, JxlJpegReconstructionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration); ArgumentNullException.ThrowIfNull(jxl); ArgumentNullException.ThrowIfNull(jpeg);
        ArgumentNullException.ThrowIfNull(options);
        if (!jxl.CanRead) { throw new ArgumentException("The JPEG XL stream must be readable.", nameof(jxl)); }
        if (!jpeg.CanWrite) { throw new ArgumentException("The JPEG stream must be writable.", nameof(jpeg)); }
        if (ReferenceEquals(jxl, jpeg)) { throw new ArgumentException("Input and output must be different streams.", nameof(jpeg)); }
        JxlJpegReconstructionOptions settings = options.Snapshot();
        cancellationToken.ThrowIfCancellationRequested();
        try { Reconstruct(configuration, jxl, jpeg, settings, cancellationToken); }
        catch (EndOfStreamException ex) { throw new InvalidImageContentException("Truncated JPEG XL reconstruction data.", ex); }
        catch (InvalidMemoryOperationException ex) { throw new InvalidImageContentException("JPEG reconstruction exceeds the configured allocator limits.", ex); }
    }

    private static void Reconstruct(Configuration configuration, Stream input, Stream output, JxlJpegReconstructionOptions options, CancellationToken cancellationToken)
    {
        long maxPixels = options.MaxPixels;
        int maxMetadata = options.MaxMetadataBytes;
        using JxlCodestreamStream stream = new(input, maxMetadata, false, cancellationToken);
        JxlBitReader reader = new(stream, cancellationToken);
        JxlImageHeader image = JxlImageHeader.Read(reader, maxPixels, maxMetadata);
        byte[] icc = image.Color.WantIcc ? JxlIccCodec.Decode(JxlIccCodec.ReadEncoded(reader, maxMetadata, configuration.MemoryAllocator), maxMetadata) : [];
        if (image.Preview.HasValue)
        {
            JxlFrameHeader preview = JxlFrameHeader.Read(reader, image.ForPreview(), maxPixels, maxMetadata);
            JxlFrameToc previewToc = JxlFrameToc.Read(reader, preview, configuration.MemoryAllocator);
            reader.SkipBits(previewToc.TotalBytes * 8);
        }
        JxlFrameHeader header = JxlFrameHeader.Read(reader, image, maxPixels, maxMetadata);
        JxlFrameToc toc = JxlFrameToc.Read(reader, header, configuration.MemoryAllocator);
        using JxlModularWorkspace workspace = new(configuration.MemoryAllocator, maxPixels, options.MaxModularBytes);
        using JxlVarDctFrame frame = JxlVarDctFrame.Read(reader, image, header, toc, workspace, cancellationToken, jpegReconstruction: true);
        stream.Drain();
        byte[] reconstruction = null;
        ReadOnlySpan<byte> exif = [], xmp = [];
        foreach ((string type, byte[] bytes) in stream.Boxes)
        {
            if (type == "jbrd")
            {
                if (reconstruction != null) { JxlThrowHelper.Invalid("multiple JPEG reconstruction boxes."); }
                reconstruction = bytes;
            }
            else if (type == "Exif" && exif.IsEmpty)
            {
                if (bytes.Length < 4) { JxlThrowHelper.Invalid("truncated Exif reconstruction metadata."); }
                uint offset = BinaryPrimitives.ReadUInt32BigEndian(bytes);
                if (offset >= bytes.Length - 4) { JxlThrowHelper.Invalid("Exif reconstruction offset is out of range."); }
                exif = bytes.AsSpan((int)offset + 4);
            }
            else if (type == "xml " && xmp.IsEmpty) { xmp = bytes; }
        }
        if (reconstruction == null) { JxlThrowHelper.Invalid("JPEG XL input has no JPEG reconstruction data."); }
        JxlJpegData data = JxlJpegData.Read(reconstruction, maxMetadata, cancellationToken, options.MaxScans);
        using JxlJpegOutputStream limited = new(output, options.MaxOutputBytes);
        JxlJpegReconstructor.Write(limited, data, frame.JpegCoefficients, icc, exif, xmp, cancellationToken, options.MaxScanBlocks);
    }
}
