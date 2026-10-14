// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Original encoder integration of the separately attributed managed decoder.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal static class JxlVarDctRoundtrip
{
    // Reconstruct the same coefficients and filters that will be written, without
    // repeatedly entropy-coding them during the perceptual quantization search.
    internal static JxlFloatFrame Reconstruct(JxlImageHeader image, JxlFrameHeader frame, JxlVarDctPreparation prepared,
        MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        int width = (int)frame.SampleWidth, height = (int)frame.SampleHeight;
        int blockWidth = (width + 7) / 8, blockHeight = (height + 7) / 8, columns = (width + 2047) / 2048;
        JxlHfMetadata[] metadata = new JxlHfMetadata[frame.LfGroupCount];
        JxlModularPlane[][] quant = new JxlModularPlane[metadata.Length][];
        using Buffer2D<float> sigma = allocator.Allocate2D<float>(blockWidth, blockHeight, AllocationOptions.Clean);
        JxlFloatFrame result = null;
        try
        {
            for (int group = 0; group < metadata.Length; group++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int left = group % columns * 256, top = group / columns * 256;
                int bw = Math.Min(256, blockWidth - left), bh = Math.Min(256, blockHeight - top);
                JxlHfMetadata hf = new(); metadata[group] = hf;
                hf.Blocks = allocator.Allocate2D<JxlVarBlock>(bw, bh);
                hf.XFromY = prepared.XFromY.View(left / 8, top / 8, (bw + 7) / 8, (bh + 7) / 8);
                hf.BFromY = prepared.BFromY.View(left / 8, top / 8, (bw + 7) / 8, (bh + 7) / 8);
                quant[group] = new JxlModularPlane[3];
                for (int c = 0; c < 3; c++) { quant[group][c] = prepared.LowFrequency[c].View(left, top, bw, bh); }
                for (int y = 0; y < bh; y++)
                {
                    prepared.Blocks.DangerousGetRowSpan(top + y).Slice(left, bw).CopyTo(hf.Blocks.DangerousGetRowSpan(y));
                    if (frame.Restoration.EpfIterations == 0) { continue; }
                    for (int x = 0; x < bw; x++)
                    {
                        JxlVarBlock block = hf.Blocks.DangerousGetRowSpan(y)[x];
                        if (block.Multiplier <= 0) { continue; }
                        float value = frame.Restoration.QuantMultiplier * 65536f / prepared.GlobalScale / block.Multiplier * frame.Restoration.Sharpness[4];
                        (int dw, int dh) = block.Transform.Blocks();
                        for (int iy = 0; iy < dh; iy++) { sigma.DangerousGetRowSpan(top + y + iy).Slice(left + x, dw).Fill(value); }
                    }
                }
            }
            JxlLfGlobal global = new() { GlobalScale = prepared.GlobalScale, QuantLf = prepared.QuantLf };
            result = JxlVarDctFrame.Render(image, frame, global, [1f / 32, 1f / 4, 1f / 2], prepared.Matrices,
                quant, new int[metadata.Length], metadata, prepared.Coefficients, [], allocator, cancellationToken, null, 1);
            JxlRestoration.Apply(result, frame.Restoration, cancellationToken, sigma);
            JxlUpsampling.Apply(result, image, frame, false, cancellationToken);
            return result;
        }
        catch { result?.Dispose(); throw; }
        finally { foreach (JxlHfMetadata hf in metadata) { hf?.Dispose(); } }
    }
}
