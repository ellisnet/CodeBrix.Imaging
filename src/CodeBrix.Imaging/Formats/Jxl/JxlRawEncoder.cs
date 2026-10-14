// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Original integration of the separately attributed managed JPEG XL writers.

using System;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Encoder;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Metadata.Profiles.Exif;

namespace CodeBrix.Imaging.Formats.Jxl;

internal sealed class JxlRawEncoder(JxlEncoder options, Configuration configuration)
{
    internal void Encode(JxlRawImage image, Stream stream, CancellationToken cancellationToken)
    {
        using JxlParallel.Scope parallel = JxlParallel.EncoderScope(configuration.MaxDegreeOfParallelism);
        JxlRawFrame first = image.Frames[0]; JxlMetadata metadata = image.Metadata.GetJxlMetadata();
        bool xyb = !options.IsLossless && first.ColorSpace is JxlRawColorSpace.Rgb or JxlRawColorSpace.Grayscale or JxlRawColorSpace.Xyb;
        if (options.IsLossless && first.ColorSpace == JxlRawColorSpace.Xyb)
        { throw new ArgumentException("XYB coding quantizes samples; choose a positive distance or supply RGB planes for lossless encoding.", nameof(image)); }
        if (!xyb && !options.IsLossless) { throw new ArgumentException("CMYK and YCbCr raw planes require lossless Modular encoding.", nameof(options)); }
        JxlBitDepth depth = Depth(first.Planes[0]);
        byte[] icc = image.Metadata.IccProfile?.ToByteArray();
        byte[] exif = image.Metadata.ExifProfile?.ToByteArray(), xmp = image.Metadata.XmpProfile?.Data;
        long metadataBytes = JxlEncoderPreflight.MetadataBytes(icc, exif, xmp, metadata.Jumbf);
        JxlEncoderPreflight.Require(metadataBytes, configuration.MemoryAllocator);
        uint exifOrientation = image.Metadata.ExifProfile?.GetValue(ExifTag.Orientation)?.Value ?? 0U;
        JxlImageHeader header = new()
        {
            Size = new((uint)image.Width, (uint)image.Height), XybEncoded = xyb, BitDepth = depth,
            Modular16BitBuffers = false, IntensityTarget = (float)(Half)metadata.IntensityTarget,
            Orientation = image.IsCoalesced ? 1U : exifOrientation is >= 1 and <= 8 ? exifOrientation : (uint)metadata.Orientation,
            Color = new() { ColorSpace = first.ColorSpace == JxlRawColorSpace.Grayscale ? 1U : 0, WantIcc = icc != null },
            ExtraChannels = new JxlExtraChannel[first.Planes.Count - first.ColorChannelCount],
            IsAnimated = metadata.IsAnimated, TpsNumerator = metadata.TicksPerSecondNumerator,
            TpsDenominator = metadata.TicksPerSecondDenominator, LoopCount = metadata.LoopCount,
        };
        if (header.Orientation is < 1 or > 8 || metadata.Level is not (5 or 10) || header.TpsNumerator is 0 or > 1 << 30 ||
            header.TpsDenominator is 0 or > 1024 || !float.IsFinite(header.IntensityTarget) || header.IntensityTarget <= 0)
        { throw new ArgumentException("Invalid JPEG XL image metadata.", nameof(image)); }
        int level = icc is { Length: > 1 << 22 } || depth.Bits > 16 || image.Width > 262144 || image.Height > 262144 || (long)image.Width * image.Height > 268435456 ? 10 : metadata.Level;
        for (int c = 0; c < header.ExtraChannels.Length; c++)
        {
            JxlRawPlane plane = first.Planes[first.ColorChannelCount + c];
            header.ExtraChannels[c] = Extra(plane);
            if (plane.BitsPerSample > 16 || plane.DimensionShift > 3 || c >= 4 || (xyb && options.Modular == true && plane.BitsPerSample > 12)) { level = 10; }
        }
        int displays = 0;
        for (int i = 0; i < image.Frames.Count; i++)
        {
            JxlRawFrame source = image.Frames[i]; ValidateFrame(source, first, image);
            header.HaveTimecodes |= source.Metadata.Timecode != 0;
            if (!source.Metadata.IsReferenceOnly && source.Metadata.LowFrequencyLevel == 0 && (source.Metadata.Duration != 0 || i == image.Frames.Count - 1)) { displays++; }
        }
        header.IsAnimated |= displays > 1;
        JxlRawFrame last = image.Frames[^1];
        if (last.Metadata.IsReferenceOnly || last.Metadata.LowFrequencyLevel != 0) { throw new ArgumentException("The final frame must be a displayed layer.", nameof(image)); }
        if (first.ColorSpace == JxlRawColorSpace.Cmyk && !Array.Exists(header.ExtraChannels, channel => channel.Type == 4))
        { throw new ArgumentException("CMYK requires a black extra channel.", nameof(image)); }
        if (icc != null && (icc.Length < 128 || !icc.AsSpan(16, 4).SequenceEqual(first.ColorSpace == JxlRawColorSpace.Cmyk ? "CMYK"u8 : first.ColorSpace == JxlRawColorSpace.Grayscale ? "GRAY"u8 : "RGB "u8)))
        { throw new ArgumentException("The ICC profile must match the raw color planes.", nameof(image)); }
        image.Encoding.Apply(header);
        // Feature streams, frame headers and custom matrices are retained for
        // every raw frame while their pixels are encoded one frame at a time.
        metadataBytes += image.Frames.Count * (1024L + 64L * header.ExtraChannels.Length);
        foreach (JxlRawFrame source in image.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            metadataBytes += JxlEncoderPreflight.Features(source.Encoding, header.ExtraChannels.Length, configuration.MemoryAllocator, cancellationToken);
            JxlEncoderPreflight.Require(metadataBytes, configuration.MemoryAllocator);
        }
        if (image.Encoding.Preview is { } sourcePreview)
        { metadataBytes += JxlEncoderPreflight.Features(sourcePreview.Encoding, header.ExtraChannels.Length, configuration.MemoryAllocator, cancellationToken); }
        JxlEncoderPreflight.Require(metadataBytes, configuration.MemoryAllocator);
        JxlVarDctInput lossyInput = xyb ? new(header, icc) : null;
        JxlFrameHeader[] frames = new JxlFrameHeader[image.Frames.Count];
        JxlFrameFeatures[] features = new JxlFrameFeatures[frames.Length];
        JxlDequantMatrices[] matrices = new JxlDequantMatrices[frames.Length];
        JxlFrameHeader[] references = new JxlFrameHeader[4];
        bool[] lowFrequency = new bool[5];
        for (int i = 0; i < frames.Length; i++)
        {
            frames[i] = Frame(image.Frames[i], image, header, i == frames.Length - 1);
            JxlEncoderPreflight.Frame(header, frames[i], options, configuration.MemoryAllocator,
                image.Frames[i].Width, image.Frames[i].Height, false, false, metadataBytes);
            matrices[i] = Quantization(image.Frames[i].Encoding);
            if (frames[i].UseLfFrame && (!xyb || options.Modular == true || frames[i].LfLevel >= 4 || !lowFrequency[frames[i].LfLevel + 1]))
            { throw new ArgumentException("A VarDCT LF reference requires a previously encoded frame at the next LF level.", nameof(image)); }
            if (xyb && options.Modular != true && options.Progressive is JxlProgressiveMode.Dc or JxlProgressiveMode.Full &&
                (frames[i].HaveCrop || frames[i].LfLevel >= 4))
            { throw new ArgumentException("Automatic LF progression requires an uncropped canvas and fewer than four LF levels.", nameof(image)); }
            features[i] = JxlFrameFeatures.Create(image.Frames[i].Encoding, header, frames[i], options.Effort, cancellationToken);
            foreach (JxlPatch patch in image.Frames[i].Encoding.Patches)
            {
                JxlFrameHeader reference = references[patch.Reference];
                if (reference == null || (xyb && !reference.SaveBeforeColorTransform) ||
                    (long)patch.Source.X + patch.Source.Width > reference.Width || (long)patch.Source.Y + patch.Source.Height > reference.Height)
                { throw new ArgumentException("Patches require an available reference with matching color coordinates and an in-bounds source rectangle.", nameof(image)); }
            }
            if (frames[i].CanReference) { references[frames[i].SaveAsReference] = frames[i]; }
            if (frames[i].FrameType == 1) { lowFrequency[frames[i].LfLevel] = true; }
        }
        JxlRawFrame preview = image.Encoding.Preview; JxlImageHeader previewHeader = null;
        JxlFrameHeader previewFrame = null; JxlFrameFeatures previewFeatures = null; JxlDequantMatrices previewMatrices = null;
        if (preview != null)
        {
            ValidateFrame(preview, first, image);
            if (preview.Width > 5440 || preview.Height > 5440 || preview.Metadata.LowFrequencyLevel != 0 || preview.Metadata.IsReferenceOnly ||
                preview.Metadata.CropLeft != 0 || preview.Metadata.CropTop != 0 || preview.Metadata.Blending.Mode != JxlBlendMode.Replace ||
                Array.Exists(preview.Metadata.ExtraChannelBlending, b => b.Mode != JxlBlendMode.Replace) || preview.Encoding.UseLowFrequencyFrame || preview.Encoding.Patches.Length != 0)
            { throw new ArgumentException("A preview must be a replacement frame without reference dependencies and at most 5440 pixels per axis.", nameof(image)); }
            header.Preview = new((uint)preview.Width, (uint)preview.Height); previewHeader = header.ForPreview();
            previewFrame = Frame(preview, image, previewHeader, true);
            previewFrame.PassCount = 1; previewFrame.PassShifts = []; previewFrame.Downsample = []; previewFrame.LastPass = [];
            JxlEncoderPreflight.Frame(previewHeader, previewFrame, options, configuration.MemoryAllocator,
                preview.Width, preview.Height, false, true, metadataBytes);
            previewFeatures = JxlFrameFeatures.Create(preview.Encoding, previewHeader, previewFrame, options.Effort, cancellationToken);
            previewMatrices = Quantization(preview.Encoding);
        }
        byte[] encodedIcc = icc == null ? null : JxlIccCodec.Encode(icc, cancellationToken);
        WriteContainer(exif, xmp, metadata.Jumbf, stream, level, cancellationToken);
        JxlBitWriter writer = new(stream); header.Write(writer);
        if (encodedIcc != null) { JxlIccCodec.WriteEncoded(writer, encodedIcc, options.Effort, cancellationToken); }
        if (preview != null) { WriteFrame(preview, previewHeader, previewFrame, previewFeatures, previewMatrices, lossyInput, writer, cancellationToken, true); }
        for (int index = 0; index < image.Frames.Count; index++)
        { WriteFrame(image.Frames[index], header, frames[index], features[index], matrices[index], lossyInput, writer, cancellationToken); }
        writer.ZeroPadToByte();
    }

    private void WriteFrame(JxlRawFrame source, JxlImageHeader header, JxlFrameHeader frame, JxlFrameFeatures features,
        JxlDequantMatrices matrices, JxlVarDctInput lossyInput, JxlBitWriter writer, CancellationToken cancellationToken, bool isPreview = false)
    {
        JxlProgressiveMode progression = isPreview ? JxlProgressiveMode.None : options.Progressive;
        {
            cancellationToken.ThrowIfCancellationRequested();
            using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(configuration.MemoryAllocator, Math.Max(262144, (long)source.Width * source.Height));
            if (header.XybEncoded)
            {
                using JxlFloatFrame reference = options.Modular != true && options.Effort >= 8 && frame.LfLevel == 0 && !frame.UseLfFrame && (options.Resampling == 1 || options.EffectiveDistance > 4 * options.Resampling)
                    ? JxlButteraugliKernels.Create(source.Width, source.Height, 3, configuration.MemoryAllocator) : null;
                using JxlFloatFrame samples = lossyInput.ImportRaw(source, configuration, cancellationToken, reference);
                JxlModularPlane[] extras = Import(source, source.ColorChannelCount, workspace, cancellationToken);
                if (options.Resampling != 1)
                {
                    JxlDownsampling.Apply(samples, options.Resampling, options.Effort, cancellationToken, sharpen: options.Modular != true);
                    extras = JxlDownsampling.ExtraChannels(extras, header, options.Resampling, workspace, cancellationToken);
                }
                if (options.Modular == true)
                { JxlLossyModularEncoder.WriteFrame(writer, header, frame, samples, extras, options.EffectiveDistance, options.Effort, frame.FrameType != 2 && progression is JxlProgressiveMode.Ac or JxlProgressiveMode.Full, options.FasterDecoding, options.PhotonNoiseIso, configuration.MemoryAllocator, cancellationToken, features); }
                else
                { JxlVarDctEncoder.WriteFrame(writer, header, frame, samples, extras, options.EffectiveDistance, options.Effort, configuration.MemoryAllocator, cancellationToken,
                    progressive: frame.FrameType != 2 && progression is JxlProgressiveMode.Ac or JxlProgressiveMode.Full, photonNoiseIso: options.PhotonNoiseIso, linearReference: reference, metricIntensity: lossyInput.MetricIntensity, decodingSpeed: options.FasterDecoding, features: features,
                    progressiveDc: frame.FrameType != 2 && progression is JxlProgressiveMode.Dc or JxlProgressiveMode.Full,
                    transform: source.Encoding.Transform is { } transform ? (JxlTransformType)transform : null, matrices: matrices, noiseModeling: options.NoiseModeling); }
            }
            else
            {
                JxlModularPlane[] planes = Import(source, 0, workspace, cancellationToken);
                JxlModularSearchOptions search = JxlModularSearchOptions.ForDecodingSpeed(null, options.FasterDecoding, options.Effort, frame.GroupSizeShift);
                JxlSqueezeEncoder squeeze = null;
                if (progression != JxlProgressiveMode.None)
                { squeeze = JxlSqueezeEncoder.CreateDefault(planes, workspace, cancellationToken); planes = squeeze.Planes; }
                JxlLossyModularEncoder.WriteChannelsFrame(writer, header, frame, planes, squeeze, null, options.Effort, search, 0, configuration.MemoryAllocator, cancellationToken, features);
            }
        }
    }

    private JxlDequantMatrices Quantization(JxlRawFrameEncoding encoding)
    {
        if (encoding.Transform.HasValue && !Enum.IsDefined(encoding.Transform.Value))
        { throw new ArgumentOutOfRangeException(nameof(encoding.Transform)); }
        ArgumentNullException.ThrowIfNull(encoding.QuantizationMatrices);
        if ((encoding.Transform.HasValue || encoding.QuantizationMatrices.Length != 0) && (options.IsLossless || options.Modular == true))
        { throw new ArgumentException("Transform and quantization controls require lossy VarDCT encoding.", nameof(encoding)); }
        return JxlDequantMatrices.Create(encoding.QuantizationMatrices);
    }

    private static JxlBitDepth Depth(JxlRawPlane plane) => new((uint)plane.BitsPerSample, (uint)plane.ExponentBitsPerSample);

    private static JxlExtraChannel Extra(JxlRawPlane plane) => new JxlExtraChannelInfo
    {
        Type = plane.ExtraChannelType.Value, Name = plane.Name, AlphaAssociated = plane.AlphaAssociated,
        SpotColor = plane.SpotColor, DimensionShift = plane.DimensionShift, ColorFilterArrayChannel = plane.ColorFilterArrayChannel,
    }.ToHeader(Depth(plane));

    private static void ValidateFrame(JxlRawFrame source, JxlRawFrame first, JxlRawImage image)
    {
        if (source.ColorSpace != first.ColorSpace || source.Planes.Count != first.Planes.Count)
        { throw new ArgumentException("All frames must share color coordinates and channel descriptions.", nameof(image)); }
        for (int c = 0; c < source.Planes.Count; c++)
        {
            JxlRawPlane a = source.Planes[c], b = first.Planes[c];
            if (Depth(a) != Depth(b) || a.ExtraChannelType != b.ExtraChannelType || a.DimensionShift != b.DimensionShift ||
                a.Name != b.Name || a.AlphaAssociated != b.AlphaAssociated || a.SpotColor != b.SpotColor || a.ColorFilterArrayChannel != b.ColorFilterArrayChannel)
            { throw new ArgumentException("All frames must share channel precision and metadata.", nameof(image)); }
            int scale = 1 << a.DimensionShift;
            if (a.Width != (source.Width + scale - 1) / scale || a.Height != (source.Height + scale - 1) / scale)
            { throw new ArgumentException("Plane dimensions do not match their channel sampling shift.", nameof(image)); }
            // Check ownership before writing any bytes, including disposed planes.
            if (a.SampleType == JxlSampleType.Integer) { _ = a.GetIntegerRowSpan(0); } else { _ = a.GetFloatRowSpan(0); }
        }
        JxlFrameMetadata info = source.Metadata;
        if (info.LowFrequencyLevel is < 0 or > 4 || info.SaveAsReference is < -1 or > 3 ||
            (info.IsReferenceOnly && (info.LowFrequencyLevel != 0 || info.SaveAsReference < 0)))
        { throw new ArgumentException("Invalid JPEG XL reference-frame metadata.", nameof(image)); }
        if (!image.IsCoalesced && (info.IsReferenceOnly || info.LowFrequencyLevel != 0) && (info.CropLeft != 0 || info.CropTop != 0))
        { throw new ArgumentException("Reference-only and low-frequency frames cannot encode crop offsets.", nameof(image)); }
        JxlName.GetByteCount(info.Name);
        ValidateBlend(info.Blending, source.Planes.Count - source.ColorChannelCount);
        if (info.ExtraChannelBlending == null || (info.ExtraChannelBlending.Length != 0 && info.ExtraChannelBlending.Length != source.Planes.Count - source.ColorChannelCount))
        { throw new ArgumentException("Extra-channel blending must describe every extra channel.", nameof(image)); }
        foreach (JxlBlendInfo blend in info.ExtraChannelBlending) { ValidateBlend(blend, info.ExtraChannelBlending.Length); }
        if (!image.IsCoalesced && info.LowFrequencyLevel == 0 &&
            ((info.CropWidth != 0 && info.CropWidth != source.Width) || (info.CropHeight != 0 && info.CropHeight != source.Height)))
        { throw new ArgumentException("Layer crop dimensions must match its color planes.", nameof(image)); }
    }

    private static void ValidateBlend(JxlBlendInfo blend, int extraCount)
    {
        if (!Enum.IsDefined(blend.Mode) || blend.SourceReference is < 0 or > 3 ||
            (blend.Mode is JxlBlendMode.Blend or JxlBlendMode.MultiplyAdd && (blend.AlphaChannel < 0 || blend.AlphaChannel >= extraCount)))
        { throw new ArgumentException("Invalid JPEG XL blending description.", nameof(blend)); }
    }

    private JxlFrameHeader Frame(JxlRawFrame source, JxlRawImage image, JxlImageHeader header, bool last)
    {
        JxlFrameMetadata info = source.Metadata; bool coalesced = image.IsCoalesced;
        uint type = coalesced ? 0U : info.LowFrequencyLevel != 0 ? 1U : info.IsReferenceOnly ? 2U : 0U;
        JxlFrameHeader frame = new()
        {
            Modular = options.IsLossless, YCbCr = source.ColorSpace == JxlRawColorSpace.YCbCr,
            Width = (uint)(type == 1 ? image.Width : source.Width), Height = (uint)(type == 1 ? image.Height : source.Height),
            FrameType = type, LfLevel = type == 1 ? (uint)info.LowFrequencyLevel : 0,
            Flags = source.Encoding.UseLowFrequencyFrame ? 32UL : 0,
            Left = coalesced ? 0 : info.CropLeft, Top = coalesced ? 0 : info.CropTop,
            IsLast = last, Duration = header.IsAnimated ? coalesced ? Math.Max(info.Duration, 1U) : info.Duration : 0,
            Name = info.Name, Timecode = info.Timecode, SaveAsReference = (uint)Math.Max(0, info.SaveAsReference),
            SaveBeforeColorTransform = type == 1 || (!coalesced && info.SaveBeforeColorTransform),
            GroupSizeShift = options.IsLossless && options.FasterDecoding >= 2 ? 0U : 1U,
            Upsampling = (uint)options.Resampling, ExtraUpsampling = new uint[header.ExtraChannels.Length],
            ExtraBlending = new JxlBlendingInfo[header.ExtraChannels.Length],
            Blend = coalesced ? default : Blend(info.Blending), Restoration = new() { GaborEnabled = false, EpfIterations = 0 },
        };
        frame.HaveCrop = type != 1 && (frame.Left != 0 || frame.Top != 0 || frame.Width != header.Size.Width || frame.Height != header.Size.Height);
        if (frame.SaveBeforeColorTransform && frame.IsNormal && (!frame.CanReference || !frame.ResetsCanvas(header, frame.Blend.Mode)))
        { throw new ArgumentException("Saving a displayed layer before color conversion requires a referenced replacement covering the canvas.", nameof(source)); }
        Array.Fill(frame.ExtraUpsampling, (uint)options.Resampling);
        for (int c = 0; c < frame.ExtraBlending.Length; c++)
        { frame.ExtraBlending[c] = coalesced || info.ExtraChannelBlending.Length == 0 ? default : Blend(info.ExtraChannelBlending[c]); }
        if (options.Progressive is JxlProgressiveMode.Ac or JxlProgressiveMode.Full && options.IsLossless && type != 2)
        { frame.PassCount = 3; frame.PassShifts = [0, 0]; frame.Downsample = [4, 2]; frame.LastPass = [0, 1]; }
        if (frame.SampleWidth != (uint)((source.Width + options.Resampling - 1) / options.Resampling) || frame.SampleHeight != (uint)((source.Height + options.Resampling - 1) / options.Resampling))
        { throw new ArgumentException("Low-frequency planes must have the canvas dimensions divided by eight to the selected level.", nameof(source)); }
        return frame;
    }

    private static JxlBlendingInfo Blend(JxlBlendInfo info) => new((uint)info.Mode, (uint)info.AlphaChannel, info.Clamp, (uint)info.SourceReference);

    private static JxlModularPlane[] Import(JxlRawFrame source, int first, JxlModularWorkspace workspace, CancellationToken cancellationToken)
    {
        JxlModularPlane[] planes = new JxlModularPlane[source.Planes.Count - first];
        for (int c = 0; c < planes.Length; c++)
        {
            JxlRawPlane input = source.Planes[first + c];
            JxlModularPlane plane = workspace.Allocate(input.Width, input.Height, input.DimensionShift, input.DimensionShift);
            plane.OriginalWidth = source.Width; plane.OriginalHeight = source.Height; planes[c] = plane;
            for (int y = 0; y < input.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested(); Span<int> row = plane.Row(y);
                if (input.SampleType == JxlSampleType.Integer) { input.GetIntegerRowSpan(y).CopyTo(row); }
                else
                {
                    ReadOnlySpan<float> values = input.GetFloatRowSpan(y);
                    for (int x = 0; x < values.Length; x++) { row[x] = BitConverter.SingleToInt32Bits(values[x]); }
                }
            }
        }
        return planes;
    }

    private void WriteContainer(byte[] exif, byte[] xmp, byte[] jumbf, Stream stream, int level, CancellationToken cancellationToken)
    {
        bool hasBoxes = exif is { Length: > 0 } || xmp is { Length: > 0 } || jumbf is { Length: > 0 };
        bool container = options.UseContainer ?? (hasBoxes || level == 10);
        if (!container && (hasBoxes || level == 10)) { throw new ArgumentException("A container is required for this metadata or codestream level.", nameof(options.UseContainer)); }
        if (!container) { return; }
        JxlContainerWriter.Start(stream, level, cancellationToken);
        if (exif is { Length: > 0 })
        {
            int skip = exif.AsSpan().StartsWith("Exif\0\0"u8) ? 6 : 0;
            byte[] payload = new byte[4 + exif.Length - skip]; exif.AsSpan(skip).CopyTo(payload.AsSpan(4));
            JxlContainerWriter.WriteMetadata(stream, "Exif"u8, payload, options.CompressMetadataBoxes, options.BrotliEffort, cancellationToken);
        }
        if (xmp is { Length: > 0 }) { JxlContainerWriter.WriteMetadata(stream, "xml "u8, xmp, options.CompressMetadataBoxes, options.BrotliEffort, cancellationToken); }
        if (jumbf is { Length: > 0 }) { JxlContainerWriter.WriteMetadata(stream, "jumb"u8, jumbf, options.CompressMetadataBoxes, options.BrotliEffort, cancellationToken); }
        JxlContainerWriter.BeginFinalCodestream(stream, cancellationToken);
    }
}
