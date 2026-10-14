// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using CodeBrix.Imaging.Formats.Jxl.Headers;

namespace CodeBrix.Imaging.Formats.Jxl;

// Per-call accounting: limits are checked before allocating the next frame.
internal sealed class JxlDecodeBudget(JxlDecoder options)
{
    private long remainingOutputPixels = options.MaxTotalPixels;
    private long remainingDecodedPixels = options.MaxDecodedPixels;
    private uint remainingFrames = options.MaxCodedFrames;

    internal void CountFrame()
    {
        if (this.remainingFrames == 0) { JxlThrowHelper.Invalid("coded frames exceed MaxCodedFrames."); }
        this.remainingFrames--;
    }

    internal void ReserveFrame(JxlImageHeader image, JxlFrameHeader frame, bool coalesce, bool retainFrames)
    {
        long canvas = (long)image.Size.Width * image.Size.Height;
        long layer = (long)frame.Width * frame.Height;
        long work = frame.IsNormal ? Math.Max(canvas, layer) : layer;
        if (work > this.remainingDecodedPixels) { JxlThrowHelper.Invalid("cumulative frame pixels exceed MaxDecodedPixels."); }
        this.remainingDecodedPixels -= work;
        if (retainFrames && (!coalesce || frame.IsKeyframe))
        {
            long output = coalesce ? canvas : layer;
            if (output > this.remainingOutputPixels) { JxlThrowHelper.Invalid("retained frame pixels exceed MaxTotalPixels."); }
            this.remainingOutputPixels -= output;
        }
    }

    internal void ReservePatchTarget(JxlFrameHeader frame, long x, long y, uint width, uint height)
    {
        // Patches are applied at the color channel's pre-upsampling resolution.
        // Count repeated visits, even when the same rectangle is overwritten.
        long left = Math.Clamp(x, 0, frame.SampleWidth), right = Math.Clamp(x + width, 0, frame.SampleWidth);
        long top = Math.Clamp(y, 0, frame.SampleHeight), bottom = Math.Clamp(y + height, 0, frame.SampleHeight);
        long work = Math.Max(1, (right - left) * (bottom - top));
        if (work > this.remainingDecodedPixels) { JxlThrowHelper.Invalid("cumulative patch pixels exceed MaxDecodedPixels."); }
        this.remainingDecodedPixels -= work;
    }
}
