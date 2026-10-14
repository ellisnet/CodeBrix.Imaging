// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System;
using System.Buffers.Binary;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Metadata;
using CodeBrix.Imaging.Metadata.Profiles.Exif;
using CodeBrix.Imaging.Metadata.Profiles.Icc;
using CodeBrix.Imaging.Metadata.Profiles.Xmp;

namespace CodeBrix.Imaging.Formats.Jxl;

internal sealed partial class JxlDecoderCore
{
    private readonly JxlDecoder options;
    internal JxlDecoderCore(JxlDecoder options) => this.options = options;

    internal IImageInfo Identify(Configuration configuration, Stream stream, CancellationToken cancellationToken)
    {
        using JxlCodestreamStream codestream = new(stream, this.options.MaxMetadataBytes, this.options.IgnoreMetadata, cancellationToken);
        JxlBitReader reader = new(codestream, cancellationToken);
        JxlImageHeader header = JxlImageHeader.Read(reader, this.options.MaxPixels, this.options.MaxMetadataBytes);
        byte[] icc = header.Color.WantIcc
            ? JxlIccCodec.Decode(JxlIccCodec.ReadEncoded(reader, this.options.MaxMetadataBytes, configuration.MemoryAllocator), this.options.MaxMetadataBytes)
            : null;
        bool preview = header.Preview.HasValue;
        JxlDecodeBudget budget = new(this.options);
        int frameCount = 0;
        while (true)
        {
            budget.CountFrame();
            JxlFrameHeader frame = JxlFrameHeader.Read(reader, preview ? header.ForPreview() : header, this.options.MaxPixels, this.options.MaxMetadataBytes);
            JxlFrameToc toc = JxlFrameToc.Read(reader, frame, configuration.MemoryAllocator);
            reader.SkipBits(toc.TotalBytes * 8);
            if (preview) { preview = false; continue; }
            if (frame.IsKeyframe) { frameCount = checked(frameCount + 1); }
            if (frame.IsLast) { break; }
        }
        codestream.Drain();
        ImageMetadata metadata = this.CreateMetadata(header, codestream, icc, frameCount);
        int bits = header.ColorChannels * (int)header.BitDepth.Bits;
        foreach (JxlExtraChannel extra in header.ExtraChannels) { bits += (int)extra.BitDepth.Bits; }
        uint width = this.options.ApplyOrientation ? header.DisplayWidth : header.Size.Width;
        uint height = this.options.ApplyOrientation ? header.DisplayHeight : header.Size.Height;
        return new ImageInfo(new PixelTypeInfo(bits), (int)width, (int)height, metadata, JxlFormat.Instance);
    }

    private ImageMetadata CreateMetadata(JxlImageHeader header, JxlCodestreamStream codestream, byte[] icc, int frameCount, bool? applyOrientation = null, bool originalColor = true)
    {
        ImageMetadata metadata = new() { ExpectedFormat = JxlFormat.Instance };
        if (!this.options.IgnoreMetadata && originalColor && icc == null && !header.Color.WantIcc && header.Color.ColorSpace is 0 or 1)
        {
            icc = JxlIccSynthesis.Create(header.Color);
            if (icc.Length > this.options.MaxMetadataBytes) { JxlThrowHelper.Invalid("synthesized ICC profile exceeds the metadata limit."); }
        }
        if (!this.options.IgnoreMetadata && icc != null) { metadata.IccProfile = new IccProfile(icc); }
        JxlMetadata jxl = metadata.GetJxlMetadata();
        jxl.BitsPerSample = (int)header.BitDepth.Bits;
        jxl.ExponentBitsPerSample = (int)header.BitDepth.ExponentBits;
        jxl.Orientation = (int)header.Orientation;
        jxl.IsAnimated = header.IsAnimated;
        jxl.TicksPerSecondNumerator = header.TpsNumerator;
        jxl.TicksPerSecondDenominator = header.TpsDenominator;
        jxl.LoopCount = header.LoopCount;
        jxl.IntensityTarget = header.IntensityTarget;
        jxl.HasContainer = codestream.HasContainer;
        jxl.HasJpegReconstruction = codestream.HasJpegReconstruction;
        jxl.Level = codestream.Level;
        jxl.FrameCount = frameCount;
        if (!this.options.IgnoreMetadata)
        {
            foreach ((string type, byte[] payload) in codestream.Boxes)
            {
                if (type == "Exif" && metadata.ExifProfile == null)
                {
                    if (payload.Length < 4) { JxlThrowHelper.Invalid("Exif box is too short."); }
                    uint offset = BinaryPrimitives.ReadUInt32BigEndian(payload);
                    if (offset >= payload.Length - 4) { JxlThrowHelper.Invalid("Exif TIFF offset exceeds box size."); }
                    metadata.ExifProfile = new ExifProfile(payload.AsSpan((int)offset + 4).ToArray());
                }
                else if (type == "xml " && metadata.XmpProfile == null) { metadata.XmpProfile = new XmpProfile(payload); }
                else if (type == "jumb" && jxl.Jumbf == null) { jxl.Jumbf = payload; }
            }
        }
        bool orient = applyOrientation ?? this.options.ApplyOrientation;
        if (!orient && !this.options.IgnoreMetadata)
        {
            metadata.ExifProfile ??= new ExifProfile();
            metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)header.Orientation);
        }
        else if (orient && metadata.ExifProfile != null)
        {
            metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)1);
        }

        return metadata;
    }
}
