// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from PixelStatsForChromacityAdjustment and
// ComputeChromacityAdjustments in libjxl lib/jxl/enc_frame.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal static class JxlChromaticityQuantization
{
    internal static void Adjust(JxlFloatFrame xyb, JxlFrameHeader frame, float distance, int effort, CancellationToken cancellationToken)
    {
        frame.XQuantScale = distance > 9.5f ? 6U : distance > 5.5f ? 5U : distance > 2.5f ? 4U : 3U;
        frame.BQuantScale = 2;
        if (effort < 7) { return; }
        int width = xyb.Planes[0].Width, height = xyb.Planes[0].Height;
        float maxX = 0, maxBlueDifference = 0, exposedBlue = 0;
        // The reference examines the edge-extended 8x8 block rectangle.
        // Include its repeated edge row/column without allocating padding.
        int columns = (width + 7) & ~7, rows = (height + 7) & ~7;
        for (int y = 1; y < rows; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int currentY = Math.Min(y, height - 1), previousY = Math.Min(y - 1, height - 1);
            Span<float> xRow = xyb.Planes[0].DangerousGetRowSpan(currentY), xAbove = xyb.Planes[0].DangerousGetRowSpan(previousY);
            Span<float> yRow = xyb.Planes[1].DangerousGetRowSpan(currentY), yAbove = xyb.Planes[1].DangerousGetRowSpan(previousY);
            Span<float> bRow = xyb.Planes[2].DangerousGetRowSpan(currentY), bAbove = xyb.Planes[2].DangerousGetRowSpan(previousY);
            for (int x = 1; x < columns; x++)
            {
                int currentX = Math.Min(x, width - 1), previousX = Math.Min(x - 1, width - 1);
                float xx = xRow[currentX], yy = yRow[currentX], bb = bRow[currentX], difference = bb - yy;
                maxX = Math.Max(maxX, Math.Max(Math.Abs(xx - xRow[previousX]), Math.Abs(xx - xAbove[currentX])));
                maxBlueDifference = Math.Max(maxBlueDifference, Math.Max(Math.Abs(difference - (bRow[previousX] - yRow[previousX])),
                    Math.Abs(difference - (bAbove[currentX] - yAbove[currentX]))));
                float exposed = bb - yy * 1.2f;
                if (exposed >= 0)
                { exposedBlue = Math.Max(exposedBlue, exposed * (Math.Abs(bb - bRow[previousX]) + Math.Abs(bb - bAbove[currentX]))); }
            }
        }
        uint xAdjustment = maxX >= .026f ? 3U : maxX >= .022f ? 2U : maxX >= .015f ? 1U : 0;
        frame.XQuantScale = Math.Max(frame.XQuantScale, 2 + xAdjustment);
        uint blueAdjustment = exposedBlue >= .13f ? 1U : 0;
        frame.BQuantScale = 2 + (maxBlueDifference > .38f ? 2 + blueAdjustment : maxBlueDifference > .33f ? 1 + blueAdjustment
            : maxBlueDifference > .28f ? blueAdjustment : 0);
    }
}
