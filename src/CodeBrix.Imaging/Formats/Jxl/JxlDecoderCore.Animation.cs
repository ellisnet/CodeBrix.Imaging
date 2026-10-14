// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.Metadata;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Jxl;

internal sealed partial class JxlDecoderCore
{
    internal ImageMetadata DecodeAnimation(Configuration configuration, Stream stream, JxlPixelFormat format,
        Action<JxlImageHeader, ImageMetadata> onHeader, Action<JxlAnimationFrame> accept, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        (ImageMetadata metadata, _) = this.DecodeFrames(configuration, stream, true, false,
            (header, frame, rendered, _, color) =>
            {
                JxlAnimationFrame output = format == JxlPixelFormat.Bgra32
                    ? this.CreateAnimationFrame<Bgra32>(configuration, header, frame, rendered, color, format, cancellationToken)
                    : this.CreateAnimationFrame<Rgba32>(configuration, header, frame, rendered, color, format, cancellationToken);
                output.DecodeTime = Stopwatch.GetElapsedTime(started);
                try { accept(output); }
                catch { output.Dispose(); throw; }
                started = Stopwatch.GetTimestamp();
            }, cancellationToken, (header, initialMetadata) => { onHeader(header, initialMetadata); started = Stopwatch.GetTimestamp(); }, retainFrames: false);
        return metadata;
    }

    private JxlAnimationFrame CreateAnimationFrame<TPixel>(Configuration configuration, JxlImageHeader header, JxlFrameHeader frame,
        JxlFloatFrame rendered, JxlOutputColorTransform color, JxlPixelFormat format, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int width = (int)header.Size.Width, height = (int)header.Size.Height;
        if (this.options.ApplyOrientation && header.Orientation > 4) { (width, height) = (height, width); }
        if ((long)width * height > int.MaxValue / 4) { JxlThrowHelper.Invalid("animation canvas exceeds the packed-buffer limit."); }
        int length = width * height * 4;
        IMemoryOwner<byte> bytes = configuration.MemoryAllocator.Allocate<byte>(length);
        ImageFrame<TPixel> image = null;
        try
        {
            Memory<byte> pixels = bytes.Memory[..length];
            ByteMemoryOwner<TPixel> typed = new(bytes);
            MemoryGroup<TPixel> group = MemoryGroup<TPixel>.CreateContiguous(typed, false);
            bytes = null;
            try { image = new(configuration, width, height, group); }
            catch { group.Dispose(); throw; }
            this.CreateDisplayFrame(configuration, header, frame, rendered, color, cancellationToken, image);
            double ticks = (double)frame.Duration * header.TpsDenominator * TimeSpan.TicksPerSecond / header.TpsNumerator;
            TimeSpan duration = ticks >= long.MaxValue ? TimeSpan.MaxValue : TimeSpan.FromTicks((long)Math.Round(ticks));
            return new(image, pixels, width, height, format, image.Metadata.GetJxlMetadata(), duration);
        }
        catch { if (image != null) { image.Dispose(); } else { bytes?.Dispose(); } throw; }
    }
}
