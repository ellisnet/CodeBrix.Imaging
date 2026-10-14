// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Blending equations adapted from jxl-oxide crates/jxl-render/src/blend.rs,
// 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Alpha reference selection and color/alpha precedence follow libjxl
// lib/jxl/{blending,alpha}.cc and render_pipeline/stage_blending.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Rendering;

internal static class JxlCompositor
{
    internal static JxlFloatFrame Compose(JxlImageHeader image, JxlFrameHeader header, JxlFloatFrame layer,
        JxlFloatFrame[] references, CancellationToken cancellationToken, bool allowReuseLayer = false)
    {
        int width = (int)image.Size.Width, height = (int)image.Size.Height;
        if (allowReuseLayer && header.Left == 0 && header.Top == 0 && header.Width == width && header.Height == height &&
            header.Blend.Mode == 0 && Array.TrueForAll(header.ExtraBlending, b => b.Mode == 0) &&
            Array.TrueForAll(layer.Planes, p => p.Width == width && p.Height == height))
        { return layer; }
        JxlFloatFrame output = new(layer.Allocator, layer.ColorChannels, layer.Planes.Length) { ColorTransformed = layer.ColorTransformed, LinearSrgb = layer.LinearSrgb };
        try
        {
            bool hasAlpha = Array.Exists(image.ExtraChannels, extra => extra.Type == 0);
            // Each channel uses its own reference slot. Read alpha from the immutable
            // reference and layer so channel order cannot alter blending results.
            for (int c = 0; c < output.Planes.Length; c++)
            {
                JxlBlendingInfo info = c < layer.ColorChannels ? header.Blend : header.ExtraBlending[c - layer.ColorChannels];
                if (hasAlpha && header.Blend.Mode == 2 && c == layer.ColorChannels + header.Blend.AlphaChannel)
                { info = header.Blend with { Source = info.Source }; }
                if (!hasAlpha && c < layer.ColorChannels && info.Mode is 2 or 3)
                { info = info with { Mode = info.Mode == 2 ? 0U : 1U }; }
                JxlFloatFrame reference = references[info.Source];
                output.Planes[c] = layer.Allocator.Allocate2D<float>(width, height, AllocationOptions.Clean);
                int alpha = image.ExtraChannels.Length != 0 && info.Mode is 2 or 3 ? layer.ColorChannels + (int)info.AlphaChannel : -1;
                bool associated = alpha >= 0 && image.ExtraChannels[info.AlphaChannel].AlphaAssociated;
                JxlFloatFrame alphaReference = alpha < 0 ? null : references[header.ExtraBlending[info.AlphaChannel].Source];
                int left = (int)Math.Clamp((long)header.Left, 0, width);
                int right = (int)Math.Clamp(header.Left + (long)header.Width, 0, width);
                for (int y = 0; y < height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Span<float> target = output.Planes[c].DangerousGetRowSpan(y);
                    if (reference != null && y < reference.Planes[c].Height)
                    {
                        Span<float> source = reference.Planes[c].DangerousGetRowSpan(y);
                        source[..Math.Min(width, source.Length)].CopyTo(target);
                    }
                    long sy = y - (long)header.Top;
                    if (sy < 0 || sy >= header.Height || left >= right) { continue; }
                    Span<float> row = layer.Planes[c].DangerousGetRowSpan((int)sy);
                    Span<float> newAlpha = alpha < 0 ? default : layer.Planes[alpha].DangerousGetRowSpan((int)sy);
                    Span<float> baseAlpha = alphaReference == null || y >= alphaReference.Planes[alpha].Height
                        ? default : alphaReference.Planes[alpha].DangerousGetRowSpan(y);
                    uint mode = alpha < 0 ? info.Mode switch { 2 => 0U, 3 => 1U, _ => info.Mode } : info.Mode;
                    if (mode == 0)
                    {
                        row.Slice((int)(left - (long)header.Left), right - left).CopyTo(target[left..right]);
                        continue;
                    }
                    for (int x = left; x < right; x++)
                    {
                        int sx = (int)(x - (long)header.Left);
                        float a = newAlpha.IsEmpty ? 1 : newAlpha[sx];
                        float b = baseAlpha.IsEmpty || x >= baseAlpha.Length ? 0 : baseAlpha[x];
                        target[x] = Blend(target[x], row[sx], b, a, mode, info.Clamp, associated, c == alpha);
                    }
                }
            }
            return output;
        }
        catch { output.Dispose(); throw; }
    }

    internal static float Blend(float background, float foreground, float baseAlpha, float newAlpha,
        uint mode, bool clamp, bool associated, bool isAlpha)
    {
        if (clamp) { newAlpha = Math.Clamp(newAlpha, 0, 1); }
        switch (mode)
        {
            case 0: return foreground;
            case 1: return background + foreground;
            case 4: return background * (clamp ? Math.Clamp(foreground, 0, 1) : foreground);
            case 3: return isAlpha ? background : background + (newAlpha * foreground);
            case 2:
                if (isAlpha)
                {
                    float alpha = clamp ? Math.Clamp(foreground, 0, 1) : foreground;
                    return 1 - ((1 - alpha) * (1 - background));
                }
                if (associated) { return foreground + (background * (1 - newAlpha)); }
                float mixedAlpha = 1 - ((1 - newAlpha) * (1 - baseAlpha));
                return ((newAlpha * foreground) + (baseAlpha * background * (1 - newAlpha))) * (mixedAlpha > 0 ? 1 / mixedAlpha : 0);
            default: JxlThrowHelper.Invalid("invalid blending mode."); return 0;
        }
    }
}
