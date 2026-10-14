// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// LF-frame organization follows libjxl lib/jxl/enc_cache.cc and enc_frame.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// The bounded power-of-two LF representation and managed integration are original.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal static class JxlLowFrequencyEncoder
{
    internal static void Write(JxlBitWriter output, JxlImageHeader image, JxlFrameHeader frame, JxlVarDctPreparation prepared,
        int effort, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        if (frame.Upsampling != 1 || frame.HaveCrop || frame.LfLevel >= 4)
        { throw new ArgumentException("Low-frequency frames require full-canvas, full-resolution input and at most four levels.", nameof(frame)); }
        int width = prepared.LowFrequency[0].Width, height = prepared.LowFrequency[0].Height;
        using JxlFloatFrame lf = new(allocator, 3, 3);
        for (int c = 0; c < 3; c++) { lf.Planes[c] = allocator.Allocate2D<float>(width, height); }
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int c = 0; c < 3; c++)
            {
                float scale = (float)((c == 0 ? 16.0 : c == 1 ? 128.0 : 256.0) / ((ulong)prepared.GlobalScale * prepared.QuantLf));
                Span<int> input = prepared.LowFrequency[c].Row(y); Span<float> values = lf.Planes[c].DangerousGetRowSpan(y);
                for (int x = 0; x < width; x++) { values[x] = input[x] * scale + (c == 2 ? lf.Planes[1].DangerousGetRowSpan(y)[x] : 0); }
            }
        }
        if ((frame.Flags & 128) == 0)
        { JxlVarDctFrame.SmoothLf(lf, [1f / 32, 1f / 4, 1f / 2], new() { GlobalScale = prepared.GlobalScale, QuantLf = prepared.QuantLf }, cancellationToken); }
        float maximum = 0;
        foreach (Buffer2D<float> plane in lf.Planes)
        { for (int y = 0; y < height; y++) { foreach (float value in plane.DangerousGetRowSpan(y)) { maximum = Math.Max(maximum, Math.Abs(value)); } } }
        float dequant = 1f / (1 << 20);
        while (maximum / dequant > 1 << 28) { dequant *= 2; }
        if (!float.IsFinite(dequant) || !Half.IsFinite((Half)(dequant * 128))) { throw new ArgumentException("Low-frequency sample magnitude is too large."); }
        using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, Math.Max(262144, (long)width * height));
        JxlModularPlane[] planes = new JxlModularPlane[3 + image.ExtraChannels.Length];
        for (int c = 0; c < 3; c++) { planes[c] = workspace.Allocate(width, height); }
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                int yy = checked((int)MathF.Round(lf.Planes[1].DangerousGetRowSpan(y)[x] / dequant));
                planes[0].Set(x, y, yy);
                planes[1].Set(x, y, checked((int)MathF.Round(lf.Planes[0].DangerousGetRowSpan(y)[x] / dequant)));
                planes[2].Set(x, y, checked((int)MathF.Round(lf.Planes[2].DangerousGetRowSpan(y)[x] / dequant) - yy));
            }
        }
        for (int c = 0; c < image.ExtraChannels.Length; c++)
        {
            int shift = (int)image.ExtraChannels[c].DimShift;
            planes[c + 3] = workspace.Allocate((width + (1 << shift) - 1) >> shift, (height + (1 << shift) - 1) >> shift, shift, shift);
        }
        JxlFrameHeader low = new()
        {
            FrameType = 1, LfLevel = frame.LfLevel + 1, Width = frame.Width, Height = frame.Height, Modular = true, IsLast = false,
            SaveBeforeColorTransform = true, ExtraUpsampling = new uint[image.ExtraChannels.Length], ExtraBlending = new JxlBlendingInfo[image.ExtraChannels.Length],
            Restoration = new() { GaborEnabled = false, EpfIterations = 0 },
        };
        Array.Fill(low.ExtraUpsampling, 1U);
        JxlLossyModularEncoder.WriteChannelsFrame(output, image, low, planes, null, [dequant, dequant, dequant], effort,
            new(PaletteColors: 0, LocalPalettePercent: 0, GlobalPalettePercent: 0, Sampling: -1, Predictor: 5, NoWeighted: true), 0, allocator, cancellationToken);
        frame.Flags |= 32;
    }
}
