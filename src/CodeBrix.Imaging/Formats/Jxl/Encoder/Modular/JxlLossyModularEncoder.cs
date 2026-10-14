// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// XYB scaling, Squeeze quantization and stream organization adapted from
// libjxl lib/jxl/enc_modular.cc and enc_frame.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;

internal static class JxlLossyModularEncoder
{
    internal static void WriteFrame(JxlBitWriter output, JxlImageHeader image, JxlFrameHeader frame, JxlFloatFrame xyb,
        JxlModularPlane[] extras, float distance, int effort, bool progressive, int decodingSpeed, float photonNoiseIso,
        MemoryAllocator allocator, CancellationToken cancellationToken, JxlFrameFeatures features = null)
    {
        int width = (int)frame.SampleWidth, height = (int)frame.SampleHeight;
        using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, Math.Max(262144, (long)width * height));
        JxlModularPlane[] planes = new JxlModularPlane[3 + extras.Length];
        for (int c = 0; c < 3; c++) { planes[c] = workspace.Allocate(width, height); }
        for (int c = 0; c < extras.Length; c++) { planes[c + 3] = extras[c]; }
        // The coded order is Y, X, B-Y. Y and B share the same dequantizer.
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<float> xr = xyb.Planes[0].DangerousGetRowSpan(y), yr = xyb.Planes[1].DangerousGetRowSpan(y), br = xyb.Planes[2].DangerousGetRowSpan(y);
            Span<int> yy = planes[0].Row(y), xx = planes[1].Row(y), bb = planes[2].Row(y);
            for (int x = 0; x < width; x++)
            {
                yy[x] = Round(yr[x] * 4096.0); xx[x] = Round(xr[x] * 65536.0);
                bb[x] = checked(Round(br[x] * 4096.0) - yy[x]);
            }
        }
        JxlSqueezeEncoder squeeze = JxlSqueezeEncoder.CreateDefault(planes, workspace, cancellationToken);
        float quantizer = MathF.Pow(distance, 1.2f);
        for (int c = 0; c < squeeze.Planes.Length; c++)
        {
            int component = squeeze.Components[c]; if (component >= 3) { continue; }
            JxlModularPlane plane = squeeze.Planes[c];
            int shift = Math.Clamp(plane.HorizontalShift + plane.VerticalShift - 1, 0, 15);
            float factor = component == 0 ? 163.84f / (1 << shift) : Math.Max(.5f, (component == 1 ? 1024f : 2048f) / (1 << shift));
            int q = (int)(quantizer * factor); if (component == 0) { q = (int)(q * 1.5f); } q = Math.Max(q, 1);
            if (q == 1) { continue; }
            for (int y = 0; y < plane.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested(); Span<int> row = plane.Row(y);
                for (int x = 0; x < row.Length; x++)
                { long value = row[x]; row[x] = checked((int)(Math.Sign(value) * ((Math.Abs(value) + q / 2) / q) * q)); }
            }
        }
        frame.Modular = true;
        frame.GroupSizeShift = decodingSpeed >= 2 ? 0U : 1U;
        frame.Restoration = new() { GaborEnabled = false, EpfIterations = 0 };
        if (photonNoiseIso != 0) { frame.Flags |= 1; }
        if (progressive) { frame.PassCount = 3; frame.PassShifts = [0, 0]; frame.Downsample = [4, 2]; frame.LastPass = [0, 1]; }
        JxlModularSearchOptions search = new(PaletteColors: 0, LocalPalettePercent: 0, GlobalPalettePercent: 0, Sampling: -1,
            Predictor: 0, NoWeighted: true, MaximumHistograms: decodingSpeed >= 2 ? 12 : 256);
        WriteChannelsFrame(output, image, frame, squeeze.Planes, squeeze, [1f / 65536, 1f / 4096, 1f / 4096], effort, search,
            photonNoiseIso, allocator, cancellationToken, features);
    }

    private static int Round(double value)
    {
        double rounded = Math.Round(value);
        if (!double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
        { throw new ArgumentException("The sample range exceeds JPEG XL's Modular XYB precision."); }
        return (int)rounded;
    }

    internal static void WriteChannelsFrame(JxlBitWriter output, JxlImageHeader image, JxlFrameHeader frame,
        JxlModularPlane[] planes, JxlSqueezeEncoder squeeze, float[] dequant, int effort, JxlModularSearchOptions search, float photonNoiseIso,
        MemoryAllocator allocator, CancellationToken cancellationToken, JxlFrameFeatures features = null)
    {
        frame.Flags |= features?.Flags ?? 0;
        int groups = checked((int)frame.GroupCount), lfGroups = checked((int)frame.LfGroupCount), passes = (int)frame.PassCount;
        if (2L + lfGroups + groups * passes > 65536) { throw new ArgumentException("JPEG XL frame has too many groups.", nameof(frame)); }
        int globalCount = 0;
        foreach (JxlModularPlane plane in planes)
        { if (plane.Width > frame.GroupDimension || plane.Height > frame.GroupDimension) { break; } globalCount++; }
        JxlModularImage layout = new() { Channels = new(planes), GlobalChannelCount = globalCount };
        (byte[] Data, long Bits)[] sections = new (byte[], long)[2 + lfGroups + groups * passes];
        sections[0] = Section(writer =>
        {
            features?.Write(writer);
            if (photonNoiseIso != 0 && features?.HasNoise != true) { JxlPhotonNoise.Write(writer, (int)frame.Width, (int)frame.Height, photonNoiseIso); }
            writer.WriteBool(dequant == null);
            if (dequant != null) { foreach (float weight in dequant) { writer.WriteF16(weight * 128); } }
            writer.WriteBool(false);
            JxlModularEncoder.WriteStreamCore(writer, planes[..globalCount], 0, null, squeeze, effort == 1, effort,
                allocator, cancellationToken, (int)image.BitDepth.Bits, search: search);
        });
        for (int group = 0; group < lfGroups; group++) { sections[1 + group] = Group(group, true, 0); }
        sections[1 + lfGroups] = ([], 0);
        for (int pass = 0; pass < passes; pass++)
        { for (int group = 0; group < groups; group++) { sections[2 + lfGroups + pass * groups + group] = Group(group, false, pass); } }
        if (groups == 1 && passes == 1)
        { sections = [Section(writer => { foreach (var section in sections) { writer.WriteBitstream(section.Data, section.Bits); } })]; }
        frame.Write(output, image); uint[] sizes = new uint[sections.Length];
        for (int i = 0; i < sizes.Length; i++) { sizes[i] = checked((uint)sections[i].Data.Length); }
        JxlFrameToc.WriteOrdered(output, frame, sizes);
        foreach (var section in sections) { cancellationToken.ThrowIfCancellationRequested(); output.WriteBytes(section.Data); }

        (byte[] Data, long Bits) Group(int group, bool low, int pass)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JxlModularPlane[] views = JxlModularFrame.SelectGroup(layout, frame, group, low, pass).ToArray();
            if (views.Length == 0) { return ([], 0); }
            return Section(writer => JxlModularEncoder.WriteStreamCore(writer, views, 0, null, null, effort == 1, effort,
                allocator, cancellationToken, (int)image.BitDepth.Bits, search: search));
        }
    }

    private static (byte[] Data, long Bits) Section(Action<JxlBitWriter> write)
    {
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream); write(writer);
        long bits = writer.Position; writer.ZeroPadToByte(); return (stream.ToArray(), bits);
    }
}
