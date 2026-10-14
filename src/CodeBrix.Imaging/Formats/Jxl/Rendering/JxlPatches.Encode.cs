// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Patch tokenization adapted from libjxl lib/jxl/enc_patch_dictionary.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.Headers;

namespace CodeBrix.Imaging.Formats.Jxl.Rendering;

internal sealed partial class JxlPatches
{
    internal void Write(JxlBitWriter writer, JxlImageHeader image, JxlFrameHeader frame, int effort, CancellationToken cancellationToken)
    {
        ulong maximum = Math.Min(1UL << 24, (ulong)frame.Width * frame.Height / 16), targets = 0;
        if (this.patches == null || (ulong)this.patches.Length > maximum) { throw new ArgumentException("Too many JPEG XL patch references."); }
        List<JxlToken> tokens = [new(0, (uint)this.patches.Length)];
        foreach (Patch patch in this.patches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (patch == null || patch.Reference > 3 || patch.Width == 0 || patch.Height == 0 || patch.Targets == null || patch.Targets.Length == 0)
            { throw new ArgumentException("Invalid JPEG XL patch reference."); }
            targets += (ulong)patch.Targets.Length;
            if (targets > maximum * 4) { throw new ArgumentException("Too many JPEG XL patch targets."); }
            tokens.Add(new(1, patch.Reference)); tokens.Add(new(3, patch.Left)); tokens.Add(new(3, patch.Top));
            tokens.Add(new(2, patch.Width - 1)); tokens.Add(new(2, patch.Height - 1)); tokens.Add(new(7, (uint)patch.Targets.Length - 1));
            int previousX = 0, previousY = 0;
            for (int i = 0; i < patch.Targets.Length; i++)
            {
                Target target = patch.Targets[i];
                if (target == null || target.Left < 0 || target.Top < 0 || (ulong)target.Left + patch.Width > frame.Width ||
                    (ulong)target.Top + patch.Height > frame.Height || target.Blending == null || target.Blending.Length != image.ExtraChannels.Length + 1)
                { throw new ArgumentException("Invalid JPEG XL patch destination."); }
                if (i == 0) { tokens.Add(new(4, (uint)target.Left)); tokens.Add(new(4, (uint)target.Top)); }
                else { tokens.Add(new(6, JxlBitWriter.PackSigned(target.Left - previousX))); tokens.Add(new(6, JxlBitWriter.PackSigned(target.Top - previousY))); }
                previousX = target.Left; previousY = target.Top;
                foreach (JxlBlendingInfo blend in target.Blending)
                {
                    if (blend.Mode > 7 || (blend.Mode >= 4 && blend.AlphaChannel >= image.ExtraChannels.Length))
                    { throw new ArgumentException("Invalid JPEG XL patch blending fields."); }
                    tokens.Add(new(5, blend.Mode));
                    if (blend.Mode >= 4 && image.ExtraChannels.Length > 1) { tokens.Add(new(8, blend.AlphaChannel)); }
                    if (blend.Mode >= 3) { tokens.Add(new(9, blend.Clamp ? 1U : 0)); }
                }
            }
        }
        JxlToken[] values = tokens.ToArray();
        JxlEntropyEncoder model = JxlEntropyEncoder.CreateOptimized(values, 10, false, effort, false, cancellationToken);
        model.WriteHeader(writer); model.WriteTokens(writer, values, cancellationToken);
    }
}
