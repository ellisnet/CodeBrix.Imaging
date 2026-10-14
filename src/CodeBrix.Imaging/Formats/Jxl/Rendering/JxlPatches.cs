// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-frame/src/data/patch.rs and
// crates/jxl-render/src/blend.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Extra-channel alpha selection and color/alpha precedence corrected against
// libjxl dec_patch_dictionary.cc and blending.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Rendering;

internal sealed partial class JxlPatches
{
    private Patch[] patches;

    internal JxlPatches(Patch[] patches) => this.patches = patches;

    internal static JxlPatches Read(JxlBitReader reader, JxlImageHeader image, JxlFrameHeader frame, MemoryAllocator allocator,
        JxlDecodeBudget budget = null)
    {
        using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, 10, allocator);
        decoder.Begin(reader);
        ulong maxReferences = Math.Min(1UL << 24, (ulong)frame.Width * frame.Height / 16);
        uint count = decoder.Read(reader, 0);
        if (count > maxReferences) { JxlThrowHelper.Invalid("too many patch references."); }
        List<Patch> patches = [];
        ulong targets = 0;
        for (int p = 0; p < count; p++)
        {
            uint reference = decoder.Read(reader, 1), x = decoder.Read(reader, 3), y = decoder.Read(reader, 3);
            uint width = decoder.Read(reader, 2), height = decoder.Read(reader, 2), targetCount = decoder.Read(reader, 7);
            if (reference > 3 || width == uint.MaxValue || height == uint.MaxValue || targetCount == uint.MaxValue)
            { JxlThrowHelper.Invalid("invalid patch dimensions or reference."); }
            width++; height++; targetCount++;
            targets += targetCount;
            if (targets > maxReferences * 4 || targets * (ulong)(image.ExtraChannels.Length + 1) > (ulong)allocator.MemoryGroupAllocationLimitBytes / 16)
            { JxlThrowHelper.Invalid("too many patch targets."); }
            List<Target> destinations = [];
            long px = 0, py = 0;
            for (int t = 0; t < targetCount; t++)
            {
                if (t == 0) { px = unchecked((int)decoder.Read(reader, 4)); py = unchecked((int)decoder.Read(reader, 4)); }
                else { px += JxlBitReader.UnpackSigned(decoder.Read(reader, 6)); py += JxlBitReader.UnpackSigned(decoder.Read(reader, 6)); }
                if (px < int.MinValue || px > int.MaxValue || py < int.MinValue || py > int.MaxValue)
                { JxlThrowHelper.Invalid("patch coordinate overflow."); }
                budget?.ReservePatchTarget(frame, px, py, width, height);
                JxlBlendingInfo[] blending = new JxlBlendingInfo[image.ExtraChannels.Length + 1];
                for (int c = 0; c < blending.Length; c++)
                {
                    uint mode = decoder.Read(reader, 5);
                    if (mode > 7) { JxlThrowHelper.Invalid("invalid patch blending mode."); }
                    uint alpha = mode >= 4 && image.ExtraChannels.Length >= 2 ? decoder.Read(reader, 8) : 0;
                    bool clamp = mode >= 3 && decoder.Read(reader, 9) != 0;
                    if (mode >= 4 && alpha >= image.ExtraChannels.Length) { JxlThrowHelper.Invalid("invalid patch alpha channel."); }
                    blending[c] = new(mode, alpha, clamp, reference);
                }
                destinations.Add(new((int)px, (int)py, blending));
            }
            patches.Add(new(reference, x, y, width, height, destinations.ToArray()));
        }
        decoder.Finish();
        return new(patches.ToArray());
    }

    internal void Apply(JxlImageHeader header, JxlFloatFrame frame, JxlFloatFrame[] references, CancellationToken cancellationToken)
    {
        float[] original = new float[frame.Planes.Length];
        bool hasAlpha = Array.Exists(header.ExtraChannels, extra => extra.Type == 0);
        foreach (Patch patch in this.patches)
        {
            JxlFloatFrame reference = references[patch.Reference];
            if (reference == null) { JxlThrowHelper.Invalid("patch refers to an unavailable frame."); }
            if (patch.Left + (ulong)patch.Width > (ulong)reference.Planes[0].Width || patch.Top + (ulong)patch.Height > (ulong)reference.Planes[0].Height)
            { JxlThrowHelper.Invalid("patch rectangle exceeds its reference frame."); }
            foreach (Target target in patch.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int left = (int)Math.Clamp((long)target.Left, 0, frame.Planes[0].Width);
                int right = (int)Math.Clamp(target.Left + (long)patch.Width, 0, frame.Planes[0].Width);
                int top = (int)Math.Clamp((long)target.Top, 0, frame.Planes[0].Height);
                int bottom = (int)Math.Clamp(target.Top + (long)patch.Height, 0, frame.Planes[0].Height);
                for (int y = top; y < bottom; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int sy = (int)(y - (long)target.Top + patch.Top);
                    for (int x = left; x < right; x++)
                    {
                        int sx = (int)(x - (long)target.Left + patch.Left);
                        for (int c = 0; c < original.Length; c++) { original[c] = frame.Planes[c].DangerousGetRowSpan(y)[x]; }
                        for (int c = 0; c < original.Length; c++)
                        {
                            JxlBlendingInfo info = target.Blending[c < frame.ColorChannels ? 0 : c - frame.ColorChannels + 1];
                            JxlBlendingInfo color = target.Blending[0];
                            if (hasAlpha && color.Mode is 4 or 5 && c == frame.ColorChannels + color.AlphaChannel)
                            { info = color; }
                            if (!hasAlpha && c < frame.ColorChannels && info.Mode >= 4)
                            { info = info with { Mode = info.Mode <= 5 ? 1U : 2U }; }
                            if (info.Mode == 0) { continue; }
                            float background = original[c], foreground = reference.Planes[c].DangerousGetRowSpan(sy)[sx];
                            int alpha = frame.ColorChannels + (int)info.AlphaChannel;
                            float baseAlpha = info.Mode < 4 ? 1 : original[alpha];
                            float newAlpha = info.Mode < 4 ? 1 : reference.Planes[alpha].DangerousGetRowSpan(sy)[sx];
                            bool swapped = info.Mode is 5 or 7;
                            if (swapped) { (background, foreground) = (foreground, background); (baseAlpha, newAlpha) = (newAlpha, baseAlpha); }
                            uint mode = info.Mode switch { 1 => 0, 2 => 1, 3 => 4, 4 or 5 => 2, _ => 3 };
                            bool associated = info.Mode >= 4 && header.ExtraChannels[info.AlphaChannel].AlphaAssociated;
                            frame.Planes[c].DangerousGetRowSpan(y)[x] = JxlCompositor.Blend(background, foreground, baseAlpha, newAlpha,
                                mode, info.Clamp, associated, info.Mode >= 4 && c == alpha);
                        }
                    }
                }
            }
        }
    }

    internal sealed record Patch(uint Reference, uint Left, uint Top, uint Width, uint Height, Target[] Targets);
    internal sealed record Target(int Left, int Top, JxlBlendingInfo[] Blending);
}
