// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// LF-group-sized input chunks and eligibility follow libjxl lib/jxl/enc_frame.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.

using System;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;

internal static partial class JxlModularEncoder
{
    // This entry point is for full-resolution lossless Image<TPixel> frames.
    // Other modes (JPEG coefficients, progressive/raw features and lossy Modular)
    // do not pass through it. Expert/global-tree efforts need the complete image.
    internal static bool ShouldChunk(JxlFrameHeader frame, int effort) => effort < 10 && frame.Modular && frame.PassCount == 1
        && frame.LfLevel == 0 && frame.Upsampling == 1 && frame.Flags == 0 && frame.GroupCount > 8
        && (frame.Width > 2048 || frame.Height > 2048);

    internal static void WriteFrameChunked(JxlBitWriter output, JxlImageHeader image, JxlFrameHeader frame,
        Action<Rectangle, JxlModularPlane[]> import, CancellationToken cancellationToken, int effort, MemoryAllocator allocator, int decodingSpeed = 0)
    {
        if (!ShouldChunk(frame, effort)) { throw new ArgumentException("Frame is not eligible for chunked lossless encoding.", nameof(frame)); }
        JxlModularSearchOptions search = JxlModularSearchOptions.ForDecodingSpeed(null, decodingSpeed, effort, frame.GroupSizeShift);
        JxlModularEncodingCache cache = effort >= 4 ? new(Math.Min(16L << 20, allocator.MemoryGroupAllocationLimitBytes / 16)) : null;
        frame.GroupSizeShift = search?.GroupShift ?? frame.GroupSizeShift;
        int dimension = (int)frame.GroupDimension, chunkSize = dimension * 8;
        int columns = (int)JxlFrameHeader.DivideRoundUp(frame.Width, (uint)dimension);
        int groups = checked((int)frame.GroupCount), lfGroups = checked((int)frame.LfGroupCount);
        if (2L + lfGroups + groups > 65536) { throw new ArgumentException("JPEG XL frame has too many groups.", nameof(frame)); }
        // Only compressed payloads survive a chunk. Keeping those permits an
        // ordinary ordered TOC on caller-owned forward-only streams, without
        // temporary files or retaining full-size uncompressed sample planes.
        byte[][] payloads = new byte[2 + lfGroups + groups][];
        Array.Fill(payloads, Array.Empty<byte>());
        bool prefix = effort == 1;
        int rct = 0;
        for (int top = 0; top < frame.Height; top += chunkSize)
        {
            for (int left = 0; left < frame.Width; left += chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Rectangle bounds = new(left, top, Math.Min(chunkSize, (int)frame.Width - left), Math.Min(chunkSize, (int)frame.Height - top));
                using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, (long)bounds.Width * bounds.Height);
                JxlModularPlane[] planes = new JxlModularPlane[image.ColorChannels + image.ExtraChannels.Length];
                for (int c = 0; c < planes.Length; c++) { planes[c] = workspace.Allocate(bounds.Width, bounds.Height); }
                import(bounds, planes);
                if (left == 0 && top == 0)
                {
                    rct = image.ColorChannels == 3 && image.BitDepth.ExponentBits == 0 ? JxlRctEncoder.Select(planes, effort, cancellationToken) : 0;
                    using MemoryStream global = new(); JxlBitWriter writer = new(global);
                    writer.WriteBool(true); writer.WriteBool(false);
                    WriteStreamCore(writer, [], rct, null, null, prefix, effort, allocator, cancellationToken, (int)image.BitDepth.Bits, search: search);
                    writer.ZeroPadToByte(); payloads[0] = global.ToArray();
                }
                if (rct != 0) { JxlRctEncoder.Apply(planes, rct, cancellationToken); }
                // Groups within the chunk are independent streams written to their own slots.
                int chunkColumns = (bounds.Width + dimension - 1) / dimension, chunkRows = (bounds.Height + dimension - 1) / dimension;
                int chunkLeft = left, chunkTop = top;
                JxlParallel.ForEncoder(chunkColumns * chunkRows, cancellationToken, index =>
                {
                    int x = index % chunkColumns * dimension, y = index / chunkColumns * dimension;
                    {
                        JxlModularPlane[] views = new JxlModularPlane[planes.Length];
                        for (int c = 0; c < views.Length; c++)
                        { views[c] = planes[c].View(x, y, Math.Min(dimension, bounds.Width - x), Math.Min(dimension, bounds.Height - y)); }
                        using MemoryStream pass = new(); JxlBitWriter writer = new(pass);
                        WriteStream(writer, views, 0, prefix, effort, image.BitDepth.ExponentBits == 0 && image.BitDepth.Bits <= 16,
                            allocator, cancellationToken, (int)image.BitDepth.Bits, search, cache);
                        writer.ZeroPadToByte();
                        int group = (((chunkTop + y) / dimension) * columns) + ((chunkLeft + x) / dimension);
                        payloads[2 + lfGroups + group] = pass.ToArray();
                    }
                });
            }
        }
        frame.Write(output, image);
        uint[] sizes = new uint[payloads.Length];
        for (int i = 0; i < sizes.Length; i++) { sizes[i] = checked((uint)payloads[i].Length); }
        JxlFrameToc.WriteOrdered(output, frame, sizes);
        foreach (byte[] payload in payloads) { cancellationToken.ThrowIfCancellationRequested(); output.WriteBytes(payload); }
    }
}
